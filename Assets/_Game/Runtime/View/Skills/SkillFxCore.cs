using System;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Skills
{
    /// <summary>
    /// The skill tells (feat/skill-effects-1): decodes its own sim events once per tick (Ability, Purify, HealLanded,
    /// HealCast, BarricadePlaced, PiecePlaced, Terraform, Hit, the Kaiju's wind-up and slam) and reads sim state each frame
    /// (SlowTimer, AbilityActive, bombs), then draws into three fixed-budget meshes the caller owns: ground decals, solid
    /// toon chunks, air billboards. Three beats per skill: a wind-up ring on the caster, the effect, a mark on whoever it
    /// affects for as long as the sim status lasts. All timing is sim time, so a frozen capture shows the exact beat.
    /// Reads the world only; fixed pools (capped for 32 players); no allocation after construction.
    /// </summary>
    public sealed class SkillFxCore
    {
        public const int MaxCasts = 40, MaxSeeds = 12, MaxLifts = 16, MaxSparks = 24, MaxActors = SkillAnimCues.MaxActors;
        public const double CastSeconds = 0.35, PulseSeconds = 0.6, SeedFlight = 0.3, RootsGrow = 0.15, RootsHold = 1.0,
            RootsSink = 0.5, HealGlowSeconds = 0.7, LiftSeconds = 0.35, LiftTail = 0.3, SparkSeconds = 0.3;
        public const int RootSpikes = 8, VineCoils = 5;

        struct Cast { public bool On; public int Actor; public SkillId Id; public TeamId Team; public double T0; public float Reach; }
        struct Seed { public bool On; public TeamId Team; public double FromX, FromZ, ToX, ToZ, T0; public float Radius, Patch; }
        struct Lift { public bool On; public bool Dug; public int Actor; public double X, Z, T0; }
        struct Spark { public bool On; public TeamId Team; public double X, Z, T0; }

        readonly Cast[] _casts = new Cast[MaxCasts];
        readonly Seed[] _seeds = new Seed[MaxSeeds];
        readonly Lift[] _lifts = new Lift[MaxLifts];
        readonly Spark[] _sparks = new Spark[MaxSparks];
        readonly double[] _healT0 = new double[MaxActors];
        int _castNext, _seedNext, _liftNext, _sparkNext;
        int _cursor, _epoch = int.MinValue;
        World _world;

        /// <summary>Events decoded since construction (tests).</summary>
        public int Decoded { get; private set; }
        /// <summary>Times each skill fired since the last reset (index = (int)SkillId).</summary>
        public readonly int[] Fired = new int[SkillVisuals.Count];
        public int LastTerraformCode { get; private set; }
        /// <summary>The Value of the last Ability event actor 0 raised (negative = a refusal code), int.MinValue = none.</summary>
        public int LastAbilityCode0 { get; private set; } = int.MinValue;
        public int KaijuClawTick { get; private set; } = int.MinValue;
        public int KaijuSlamTelegraphTick { get; private set; } = int.MinValue;
        public int KaijuSlamTick { get; private set; } = int.MinValue;

        // ---- counts from the last Build (tests, capture log)
        public int CastsDrawn, SeedsDrawn, RootsDrawn, MarkedCount, GlowCount, LiftsDrawn, SparksDrawn, ShieldsDrawn, BundlesDrawn;

        static readonly Color32 Navy = new Color32(24, 35, 59, 255);
        static readonly Color32 SeedLight = new Color32(236, 206, 128, 255), SeedMid = new Color32(196, 156, 82, 255), SeedDark = new Color32(132, 98, 50, 255);
        static readonly Color32 RootLight = new Color32(150, 118, 76, 255), RootMid = new Color32(112, 84, 52, 255), RootDark = new Color32(74, 54, 34, 255);
        static readonly Color32 VineLight = new Color32(150, 214, 92, 255), VineMid = new Color32(92, 168, 62, 255), VineDark = new Color32(52, 110, 44, 255);
        static readonly Color32 DirtLight = new Color32(140, 106, 72, 255), Dirt = new Color32(107, 79, 53, 255), DirtDark = new Color32(76, 55, 37, 255);
        static readonly Color32 HealGreen = new Color32(170, 240, 120, 255), SparkWhite = new Color32(255, 246, 214, 255), SparkGold = new Color32(255, 214, 72, 255);
        static readonly Color32 TntLight = new Color32(232, 96, 80, 255), TntRed = new Color32(200, 53, 46, 255), TntDark = new Color32(132, 30, 28, 255);

        public static Color32 TeamColor(TeamId t) => t == TeamId.Azure ? FxScene.AzureC : FxScene.CrimsonC;

        static Color32 A(Color32 c, float a)
        {
            c.a = (byte)Mathf.Clamp(a * 255f, 0, 255);
            return c;
        }

        public void Reset()
        {
            for (int i = 0; i < MaxCasts; i++) _casts[i].On = false;
            for (int i = 0; i < MaxSeeds; i++) _seeds[i].On = false;
            for (int i = 0; i < MaxLifts; i++) _lifts[i].On = false;
            for (int i = 0; i < MaxSparks; i++) _sparks[i].On = false;
            for (int i = 0; i < MaxActors; i++) _healT0[i] = double.NegativeInfinity;
            for (int i = 0; i < Fired.Length; i++) Fired[i] = 0;
            KaijuClawTick = KaijuSlamTelegraphTick = KaijuSlamTick = int.MinValue;
            LastAbilityCode0 = int.MinValue;
            SkillAnimCues.Clear();
        }

        public SkillFxCore() { Reset(); }

        // ------------------------------------------------------------------ decode (once per sim tick)

        public void Read(World w)
        {
            if (w == null) return;
            if (w != _world || w.Epoch != _epoch || _cursor > w.EventHead)
            {
                _world = w;
                _epoch = w.Epoch;
                Reset();
                _cursor = w.EventHead;
            }
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            double hz = w.Config.SimHz;
            double now = w.Tick / hz;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                if (e == null) continue;
                Decoded++;
                switch (e.Type)
                {
                    case SimEventType.Ability: OnAbility(w, e, now); break;
                    case SimEventType.Purify:
                        if (Valid(w, e.A))
                        {
                            AddCast(w, e.A, SkillId.HealerPurify, now, (float)(e.Radius > 0 ? e.Radius : 4.0));
                            SkillAnimCues.Note(e.A, w.Tick, now, ArtMapping.Cast, SkillAnimCues.ProcRaise);
                        }
                        break;
                    case SimEventType.HealLanded:
                        if (e.A >= 0 && e.A < MaxActors) _healT0[e.A] = now;
                        break;
                    case SimEventType.HealCast:
                        if (e.B == Healer.KindMend && Valid(w, e.A)) AddCast(w, e.A, SkillId.HealerMend, now, 0f);
                        break;
                    case SimEventType.BarricadePlaced:
                        if (Valid(w, e.A) && w.Actors[e.A].Cls == ClassId.Miner) AddCast(w, e.A, SkillId.MinerBarricade, now, 0f);
                        break;
                    case SimEventType.PiecePlaced:
                        if (Valid(w, e.A) && w.Actors[e.A].Cls == ClassId.Builder) AddCast(w, e.A, SkillId.BuilderBuild, now, 0f);
                        break;
                    case SimEventType.Terraform:
                        LastTerraformCode = (int)e.Value;
                        if ((int)e.Value == Terraform.Dug || (int)e.Value == Terraform.Placed) AddLift(w, e, now);
                        break;
                    case SimEventType.Hit: OnHit(w, e, now); break;
                    case SimEventType.KaijuMeleeWindup: KaijuClawTick = w.Tick; Fired[(int)SkillId.KaijuClaw]++; break;
                    case SimEventType.KaijuTelegraph: KaijuSlamTelegraphTick = w.Tick; break;
                    case SimEventType.KaijuSlam: KaijuSlamTick = w.Tick; Fired[(int)SkillId.KaijuSlam]++; break;
                }
            }
        }

        static bool Valid(World w, int id) => id >= 0 && id < w.ActorCount && id < MaxActors;

        void OnAbility(World w, SimEvent e, double now)
        {
            if (!Valid(w, e.A)) return;
            Actor a = w.Actors[e.A];
            int code = (int)e.Value;
            if (e.A == 0) LastAbilityCode0 = code;
            switch (a.Cls)
            {
                case ClassId.Warrior:
                    if (code == 1) AddCast(w, e.A, SkillId.WarriorShield, now, 0f);
                    break;
                case ClassId.Farmer:
                    if (code != 1) break;
                    AddCast(w, e.A, SkillId.FarmerRoots, now, 0f);
                    AddSeed(w, e.A, e.X, e.Z, now);
                    SkillAnimCues.Note(e.A, w.Tick, now, ArtMapping.Throw, SkillAnimCues.ProcThrow);
                    break;
                case ClassId.Demolitions:
                    if (code == Demolitions.TntThrownCode)
                    {
                        AddCast(w, e.A, SkillId.DemoTnt, now, 0f);
                        SkillAnimCues.Note(e.A, w.Tick, now, ArtMapping.Throw, SkillAnimCues.ProcThrow);
                    }
                    else if (code == Demolitions.MinePlacedCode) AddCast(w, e.A, SkillId.DemoMine, now, 0f);
                    break;
                case ClassId.Healer:
                    if (code == Healer.PulseCode) AddCast(w, e.A, SkillId.HealerWings, now, 2.2f);
                    break;
            }
        }

        void OnHit(World w, SimEvent e, double now)
        {
            if (!Valid(w, e.A)) return;
            Actor v = w.Actors[e.A];
            if (v.Cls != ClassId.Warrior || !(v.AbilityActive > 0)) return;
            double sx, sz;
            if (e.B >= 0 && e.B < w.ActorCount) { sx = w.X[e.B]; sz = w.Z[e.B]; }
            else if (e.B == -2 && w.Kaiju != null) { sx = w.Kaiju.X; sz = w.Kaiju.Z; }
            else return;
            if (!Damage.IsFrontal(w, e.A, sx, sz)) return;
            double dx = sx - w.X[e.A], dz = sz - w.Z[e.A];
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) { dx = Math.Cos(v.Facing); dz = Math.Sin(v.Facing); d = 1; }
            ref Spark s = ref _sparks[_sparkNext];
            _sparkNext = (_sparkNext + 1) % MaxSparks;
            s.On = true;
            s.Team = v.Team;
            s.X = w.X[e.A] + dx / d * 0.8;
            s.Z = w.Z[e.A] + dz / d * 0.8;
            s.T0 = now;
        }

        void AddCast(World w, int actor, SkillId id, double now, float reach)
        {
            ref Cast c = ref _casts[_castNext];
            _castNext = (_castNext + 1) % MaxCasts;
            c.On = true;
            c.Actor = actor;
            c.Id = id;
            c.Team = w.Actors[actor].Team;
            c.T0 = now;
            c.Reach = reach;
            Fired[(int)id]++;
        }

        void AddSeed(World w, int actor, double tx, double tz, double now)
        {
            ref Seed s = ref _seeds[_seedNext];
            _seedNext = (_seedNext + 1) % MaxSeeds;
            s.On = true;
            s.Team = w.Actors[actor].Team;
            s.FromX = w.X[actor];
            s.FromZ = w.Z[actor];
            s.ToX = tx;
            s.ToZ = tz;
            s.T0 = now;
            s.Radius = (float)w.Config.Abilities.FarmerRoots.Radius;
            s.Patch = (float)w.Config.Abilities.FarmerRoots.SlowSeconds;
        }

        void AddLift(World w, SimEvent e, double now)
        {
            if (!Valid(w, e.A)) return;
            ref Lift l = ref _lifts[_liftNext];
            _liftNext = (_liftNext + 1) % MaxLifts;
            l.On = true;
            l.Dug = (int)e.Value == Terraform.Dug;
            l.Actor = e.A;
            l.X = e.X;
            l.Z = e.Z;
            l.T0 = now;
            Fired[(int)(l.Dug ? SkillId.MinerDig : SkillId.MinerPlace)]++;
        }

        // ------------------------------------------------------------------ draw (every frame)

        World _w;
        FxMeshBuilder _g, _s, _a;
        Vector3 _camR, _camU;

        float H(double x, double z) => (float)GroundHeight.At(_w.Map, x, z);
        Vector3 P(double x, double z, float lift) => SimSpace.ToWorld(x, H(x, z) + lift, z);

        Vector3 ActorAt(int id, double alpha, float lift)
        {
            double x = _w.PrevX[id] + (_w.X[id] - _w.PrevX[id]) * alpha, z = _w.PrevZ[id] + (_w.Z[id] - _w.PrevZ[id]) * alpha;
            return P(x, z, lift);
        }

        void GroundRing(Vector3 c, float r0, float r1, float a0, float a1, int seg, Color32 col) =>
            _g.Ring(c, Vector3.right, Vector3.forward, r0, r1, a0, a1, seg, col);

        void Dashed(Vector3 c, float r0, float r1, int dashes, float fill, float rot, Color32 col)
        {
            float step = Mathf.PI * 2f / dashes;
            for (int k = 0; k < dashes; k++)
            {
                float a0 = rot + k * step;
                GroundRing(c, r0, r1, a0, a0 + step * fill, 4, col);
            }
        }

        /// <summary>Draws every live tell for sim time (w.Tick + alpha) / SimHz. camRight / camUp span the air billboards.</summary>
        public void Build(World w, double alpha, Vector3 camRight, Vector3 camUp, FxMeshBuilder ground, FxMeshBuilder solid, FxMeshBuilder air)
        {
            CastsDrawn = SeedsDrawn = RootsDrawn = MarkedCount = GlowCount = LiftsDrawn = SparksDrawn = ShieldsDrawn = BundlesDrawn = 0;
            if (w == null || ground == null || solid == null || air == null) return;
            _w = w; _g = ground; _s = solid; _a = air; _camR = camRight; _camU = camUp;
            double now = (w.Tick + alpha) / w.Config.SimHz;
            DrawCasts(w, alpha, now);
            DrawSeeds(now);
            DrawSlowMarks(w, alpha, now);
            DrawHealGlows(w, alpha, now);
            DrawLifts(w, alpha, now);
            DrawShields(w, alpha, now);
            DrawSparks(now);
            DrawBundles(w, alpha);
        }

        // wind-up: a team ring closing on the caster, a glint at hand height; heal pulses also send a ring outwards
        void DrawCasts(World w, double alpha, double now)
        {
            for (int i = 0; i < MaxCasts; i++)
            {
                ref Cast c = ref _casts[i];
                if (!c.On) continue;
                bool pulse = c.Id == SkillId.HealerWings || c.Id == SkillId.HealerPurify;
                double dur = pulse ? PulseSeconds : CastSeconds;
                double age = now - c.T0;
                if (age > dur || c.Actor >= w.ActorCount) { c.On = false; continue; }
                if (age < 0 || !w.Actors[c.Actor].Alive) continue;
                float k = (float)(age / dur);
                Color32 team = TeamColor(c.Team);
                Vector3 at = ActorAt(c.Actor, alpha, 0.04f);
                float kc = Mathf.Clamp01((float)(age / CastSeconds));
                if (kc < 1f)
                {
                    float e = 1f - (1f - kc) * (1f - kc);
                    float r = Mathf.Lerp(1.05f, 0.42f, e);
                    GroundRing(at, r - 0.1f, r + 0.02f, 0f, Mathf.PI * 2f, 28, A(Navy, 0.55f * (1f - kc)));
                    GroundRing(at, r - 0.08f, r, 0f, Mathf.PI * 2f, 28, A(team, 0.9f * (1f - 0.5f * kc)));
                    _a.Add = 255;
                    _a.Star(at + Vector3.up * 1.3f, _camR, _camU, 0.08f + 0.2f * (1f - kc), 0.4f, 4, kc * 1.6f, A(SparkWhite, 0.85f * (1f - kc)));
                    _a.Add = 0;
                }
                if (pulse)
                {
                    float reach = c.Reach > 0 ? c.Reach : 2f;
                    float r = 0.4f + (reach - 0.4f) * (1f - (1f - k) * (1f - k));
                    GroundRing(at, r - 0.09f, r, 0f, Mathf.PI * 2f, 40, A(HealGreen, 0.55f * (1f - k)));
                    GroundRing(at, r - 0.13f, r - 0.09f, 0f, Mathf.PI * 2f, 40, A(team, 0.45f * (1f - k)));
                }
                CastsDrawn++;
            }
        }

        // Root Seeds: the seed arc, the area ring, roots breaking out, the patch fading over the slow's length
        void DrawSeeds(double now)
        {
            for (int i = 0; i < MaxSeeds; i++)
            {
                ref Seed s = ref _seeds[i];
                if (!s.On) continue;
                double age = now - s.T0;
                if (age < 0) continue;
                if (age > SeedFlight + s.Patch) { s.On = false; continue; }
                Color32 team = TeamColor(s.Team);
                Vector3 land = P(s.ToX, s.ToZ, 0.035f);
                if (age < SeedFlight)
                {
                    float p = (float)(age / SeedFlight);
                    double gx = s.FromX + (s.ToX - s.FromX) * p, gz = s.FromZ + (s.ToZ - s.FromZ) * p;
                    float dist = (float)Math.Sqrt((s.ToX - s.FromX) * (s.ToX - s.FromX) + (s.ToZ - s.FromZ) * (s.ToZ - s.FromZ));
                    float apex = Mathf.Min(1.4f, 0.5f + 0.12f * dist);
                    float h = Mathf.Lerp(1.1f, 0.06f, p) + 4f * apex * p * (1f - p);
                    Vector3 g = P(gx, gz, 0.03f);
                    _g.Blob(g, Vector3.right, Vector3.forward, 0.16f, null, 10, 0f, A(Navy, 0.4f));
                    Vector3 at = g + Vector3.up * h;
                    _s.Chunk(at, Quaternion.Euler(p * 400f, p * 250f, 0f), new Vector3(0.11f, 0.11f, 0.13f), 0.026f, SeedLight, SeedMid, SeedDark, Navy);
                    _a.Add = 255;
                    _a.Star(at, _camR, _camU, 0.14f, 0.45f, 5, p * 6f, A(team, 0.75f));
                    _a.Add = 0;
                    // the area telegraph while the seed flies: a dashed team ring growing in
                    Dashed(land, s.Radius - 0.08f, s.Radius, 16, 0.6f, p, A(team, 0.35f + 0.5f * p));
                    SeedsDrawn++;
                    continue;
                }
                float t = (float)(age - SeedFlight);
                float fade = 1f - Mathf.Clamp01(t / s.Patch);
                // the burst: a ring racing out to the edge, dirt crumbs
                if (t < 0.25f)
                {
                    float r = Mathf.Lerp(0.3f, s.Radius, t / 0.25f);
                    GroundRing(land, r - 0.14f, r, 0f, Mathf.PI * 2f, 40, A(RootLight, 0.85f * (1f - t / 0.25f)));
                }
                if (t < 0.45f)
                {
                    for (int k = 0; k < 6; k++)
                    {
                        float ang = k * 1.047f + i;
                        Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                        Vector3 c = land + d * (1.6f * t) + Vector3.up * (2.4f * t - 5f * t * t + 0.05f);
                        _s.Chunk(c, Quaternion.Euler(t * 500f, ang * 57f, 0f), Vector3.one * 0.05f, 0.015f, DirtLight, Dirt, DirtDark, Navy);
                    }
                }
                // the patch: where the slow landed, team ring and soft fill, fading over the slow's length
                _g.Blob(land, Vector3.right, Vector3.forward, s.Radius, null, 32, 0f, A(RootDark, 0.1f * fade));
                Dashed(land, s.Radius - 0.08f, s.Radius, 16, 0.6f, 0f, A(team, 0.7f * fade));
                // roots breaking out: spikes on a golden-angle spread, grow, hold, sink
                float grow = Mathf.Clamp01(t / (float)RootsGrow);
                float sink = t > RootsHold ? Mathf.Clamp01((t - (float)RootsHold) / (float)RootsSink) : 0f;
                float hk = grow * (1f - sink);
                if (hk > 0.01f)
                {
                    for (int k = 0; k < RootSpikes; k++)
                    {
                        float ang = k * 2.39996f + i * 0.7f;
                        float rr = s.Radius * 0.85f * Mathf.Sqrt((k + 0.5f) / RootSpikes);
                        double rx = s.ToX + Math.Cos(ang) * rr, rz = s.ToZ + Math.Sin(ang) * rr;
                        float hh = 0.62f * hk * (0.7f + 0.3f * ((k * 37) % 10) / 10f);
                        Vector3 b = P(rx, rz, 0f);
                        Quaternion rot = Quaternion.Euler(18f * Mathf.Cos(ang * 3f), ang * Mathf.Rad2Deg, 18f * Mathf.Sin(ang * 2f));
                        _s.Chunk(b + Vector3.up * (hh * 0.5f), rot, new Vector3(0.085f, hh * 0.5f, 0.085f), 0.024f, RootLight, RootMid, RootDark, Navy);
                        _s.Chunk(b + rot * new Vector3(0f, hh, 0f), rot, new Vector3(0.12f, 0.05f, 0.07f), 0.02f, VineLight, VineMid, VineDark, Navy);
                        RootsDrawn++;
                    }
                }
                SeedsDrawn++;
            }
        }

        // the mark while slowed: vines coiled round the feet, a ring counting the slow down, a slow icon at the shoulder
        void DrawSlowMarks(World w, double alpha, double now)
        {
            double full = w.Config.Abilities.FarmerRoots.SlowSeconds;
            int n = Math.Min(w.ActorCount, MaxActors);
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || !(a.SlowTimer > 0)) continue;
                MarkedCount++;
                float frac = Mathf.Clamp01((float)(a.SlowTimer / (full > 0 ? full : 3.0)));
                Color32 byTeam = TeamColor(a.Team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure); // only an enemy roots you
                Vector3 c = ActorAt(i, alpha, 0f);
                float sway = Mathf.Sin((float)now * 5f + i) * 0.04f;
                for (int k = 0; k < VineCoils * 2; k++)
                {
                    float ang = k * (Mathf.PI * 2f / VineCoils) + (k >= VineCoils ? 0.6f : 0f) + i * 0.9f;
                    Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 tangent = new Vector3(-d.z, k >= VineCoils ? -0.5f : 0.5f, d.x);
                    Vector3 p = c + d * (0.36f + sway) + Vector3.up * (k >= VineCoils ? 0.3f : 0.1f);
                    _s.Chunk(p, Quaternion.LookRotation(tangent), new Vector3(0.06f, 0.05f, 0.22f), 0.02f, VineLight, VineMid, VineDark, Navy);
                }
                _g.Ring(c + Vector3.up * 0.05f, Vector3.right, Vector3.forward, 0.28f, 0.5f, 0f, Mathf.PI * 2f, 20, A(VineMid, 0.75f));
                Vector3 ring = c + Vector3.up * 0.04f;
                GroundRing(ring, 0.36f, 0.46f, 0f, Mathf.PI * 2f, 24, A(Navy, 0.55f));
                GroundRing(ring, 0.38f, 0.44f, Mathf.PI * 0.5f - Mathf.PI * 2f * frac, Mathf.PI * 0.5f, Mathf.Max(3, (int)(24 * frac)), A(byTeam, 0.95f));
                // slow icon: two down chevrons on a navy disc beside the shoulder (clear of the name label above the head)
                Vector3 icon = c + Vector3.up * 1.5f + _camR * 0.6f;
                _a.Blob(icon, _camR, _camU, 0.27f, null, 16, 0f, A(Navy, 0.85f));
                _a.Ring(icon, _camR, _camU, 0.25f, 0.29f, 0f, Mathf.PI * 2f, 16, A(VineLight, 1f));
                Vector3 d1 = (_camR + _camU).normalized, d2 = (_camR - _camU).normalized;
                for (int j = 0; j < 2; j++)
                {
                    Vector3 m = icon + _camU * (0.08f - j * 0.13f);
                    _a.Quad(m + (-_camR + _camU) * 0.06f, d2 * 0.09f, d1 * 0.028f, A(byTeam, 1f), A(byTeam, 1f));
                    _a.Quad(m + (_camR + _camU) * 0.06f, d1 * 0.09f, d2 * 0.028f, A(byTeam, 1f), A(byTeam, 1f));
                }
            }
        }

        // the mark on a healed ally: a brief green glow and a rising ring (Mend, Healing Wings)
        void DrawHealGlows(World w, double alpha, double now)
        {
            int n = Math.Min(w.ActorCount, MaxActors);
            for (int i = 0; i < n; i++)
            {
                double t = now - _healT0[i];
                if (t < 0 || t > HealGlowSeconds || !w.Actors[i].Alive) continue;
                GlowCount++;
                float k = (float)(t / HealGlowSeconds);
                Vector3 c = ActorAt(i, alpha, 0.04f);
                float r = Mathf.Lerp(0.25f, 0.75f, k);
                GroundRing(c, r - 0.1f, r, 0f, Mathf.PI * 2f, 24, A(HealGreen, 0.85f * (1f - k)));
                GroundRing(c, r - 0.14f, r - 0.1f, 0f, Mathf.PI * 2f, 24, A(TeamColor(w.Actors[i].Team), 0.7f * (1f - k)));
                _a.Add = 255;
                _a.Blob(c + Vector3.up * (0.8f + 0.3f * k), _camR, _camU, 0.42f, null, 16, 0f, A(HealGreen, 0.16f * (1f - k)));
                _a.Add = 0;
            }
        }

        // Miner dig: dirt burst, the block lifted to his hands. Place: the block dropped into the hole, a dust ring.
        void DrawLifts(World w, double alpha, double now)
        {
            for (int i = 0; i < MaxLifts; i++)
            {
                ref Lift l = ref _lifts[i];
                if (!l.On) continue;
                double age = now - l.T0;
                if (age > LiftSeconds + LiftTail || l.Actor >= w.ActorCount) { l.On = false; continue; }
                if (age < 0) continue;
                Vector3 cell = P(l.X, l.Z, 0.15f);
                Vector3 hands = ActorAt(l.Actor, alpha, 1.25f);
                float t = (float)age;
                if (l.Dug)
                {
                    if (t < LiftSeconds)
                    {
                        float p = t / (float)LiftSeconds;
                        Vector3 at = Vector3.Lerp(cell, hands, p) + Vector3.up * (Mathf.Sin(p * Mathf.PI) * 0.45f);
                        float hs = Mathf.Lerp(0.24f, 0.13f, p);
                        _s.Chunk(at, Quaternion.Euler(0f, p * 90f, p * 25f), Vector3.one * hs, 0.022f, DirtLight, Dirt, DirtDark, Navy);
                    }
                    if (t < 0.5f)
                    {
                        for (int k = 0; k < 6; k++)
                        {
                            float ang = k * 1.047f + i * 0.5f;
                            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                            Vector3 c = cell + d * (1.3f * t) + Vector3.up * (2.2f * t - 4.9f * t * t);
                            _s.Chunk(c, Quaternion.Euler(t * 600f, ang * 57f, 0f), Vector3.one * 0.05f, 0.015f, DirtLight, Dirt, DirtDark, Navy);
                        }
                    }
                }
                else
                {
                    if (t < LiftSeconds)
                    {
                        float p = t / (float)LiftSeconds;
                        Vector3 at = Vector3.Lerp(hands, cell + Vector3.up * 0.1f, p * p) + Vector3.up * (Mathf.Sin(p * Mathf.PI) * 0.3f);
                        _s.Chunk(at, Quaternion.Euler(0f, 90f - p * 90f, 0f), Vector3.one * Mathf.Lerp(0.13f, 0.26f, p), 0.022f, DirtLight, Dirt, DirtDark, Navy);
                    }
                    else
                    {
                        float k = (t - (float)LiftSeconds) / (float)LiftTail;
                        float r = Mathf.Lerp(0.3f, 0.9f, k);
                        GroundRing(P(l.X, l.Z, 0.05f), r - 0.12f, r, 0f, Mathf.PI * 2f, 24, A(DirtLight, 0.8f * (1f - k)));
                    }
                }
                LiftsDrawn++;
            }
        }

        // Shield Up: a ground wedge over the blocked front and a low raised wall (translucent, so the Warrior stays visible)
        void DrawShields(World w, double alpha, double now)
        {
            float half = (float)(Damage.ShieldHalfArcDeg * Math.PI / 180.0);
            int n = Math.Min(w.ActorCount, MaxActors);
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Cls != ClassId.Warrior || !(a.AbilityActive > 0)) continue;
                ShieldsDrawn++;
                Color32 team = TeamColor(a.Team);
                Vector3 c = ActorAt(i, alpha, 0.03f);
                float centre = (float)-a.Facing;
                float a0 = centre - half, a1 = centre + half;
                _g.Ring(c, Vector3.right, Vector3.forward, 0.35f, 1.35f, a0, a1, 12, A(team, 0.2f));
                _g.Ring(c, Vector3.right, Vector3.forward, 1.25f, 1.38f, a0, a1, 12, A(team, 0.85f));
                const int seg = 8;
                const float rad = 0.78f, y0 = 0.25f, y1 = 1.25f;
                for (int k = 0; k < seg; k++)
                {
                    float s0 = a0 + (a1 - a0) * k / seg, s1 = a0 + (a1 - a0) * (k + 1) / seg;
                    Vector3 p0 = new Vector3(Mathf.Cos(s0), 0f, Mathf.Sin(s0)) * rad, p1 = new Vector3(Mathf.Cos(s1), 0f, Mathf.Sin(s1)) * rad;
                    Vector3 mid = c + (p0 + p1) * 0.5f;
                    Vector3 hr = (p1 - p0) * 0.5f;
                    _a.Quad(mid + Vector3.up * ((y0 + y1) * 0.5f), hr, Vector3.up * ((y1 - y0) * 0.5f), A(team, 0.06f), A(team, 0.3f));
                    _a.Quad(mid + Vector3.up * y1, hr * 1.02f, Vector3.up * 0.04f, A(SparkWhite, 0.9f), A(SparkWhite, 0.9f));
                }
            }
        }

        // the block spark: a burst on the shield where a frontal hit was stopped
        void DrawSparks(double now)
        {
            for (int i = 0; i < MaxSparks; i++)
            {
                ref Spark s = ref _sparks[i];
                if (!s.On) continue;
                double age = now - s.T0;
                if (age > SparkSeconds) { s.On = false; continue; }
                if (age < 0) continue;
                float k = (float)(age / SparkSeconds);
                Vector3 c = P(s.X, s.Z, 0.9f);
                _a.Add = 255;
                _a.Star(c, _camR, _camU, 0.16f + 0.28f * (1f - k), 0.35f, 6, k * 2f, A(SparkGold, 1f - k));
                _a.Star(c, _camR, _camU, 0.08f + 0.12f * (1f - k), 0.5f, 4, -k * 3f, A(SparkWhite, 1f - k));
                _a.Add = 0;
                SparksDrawn++;
            }
        }

        // TNT: two more sticks and a team band round the one MechanicsWorldView draws, so it reads as a bundle
        void DrawBundles(World w, double alpha)
        {
            Bomb[] bs = w.Demolitions != null ? w.Demolitions.Bombs : null;
            if (bs == null) return;
            double nowTicks = w.Tick + alpha;
            for (int k = 0; k < bs.Length; k++)
            {
                Bomb b = bs[k];
                if (b == null || !b.Active) continue;
                Vector3 at;
                Quaternion rot;
                if (!b.Landed)
                {
                    float p = TntArc.Progress(nowTicks, b.ThrownTick, b.LandTick);
                    double gx = b.FromX + (b.X - b.FromX) * p, gz = b.FromZ + (b.Z - b.FromZ) * p;
                    float dist = (float)Math.Sqrt((b.X - b.FromX) * (b.X - b.FromX) + (b.Z - b.FromZ) * (b.Z - b.FromZ));
                    float h = TntArc.Height(p, dist);
                    Vector3 dir = SimSpace.ToWorld(b.X - b.FromX, 0, b.Z - b.FromZ);
                    Vector3 axis = Vector3.Cross(Vector3.up, dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward);
                    rot = Quaternion.AngleAxis(p * 540f, axis) * Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward);
                    at = P(gx, gz, 0.03f) + Vector3.up * (h + 0.3f);
                }
                else
                {
                    rot = Quaternion.Euler(90f, 20f + k * 37f, 0f);
                    at = P(b.X, b.Z, 0.1f);
                }
                Vector3 side = rot * Vector3.right * 0.17f;
                Vector3 back = rot * Vector3.up * 0.06f;
                _s.Chunk(at + side + back, rot, new Vector3(0.08f, 0.08f, 0.2f), 0.022f, TntLight, TntRed, TntDark, Navy);
                _s.Chunk(at - side + back, rot, new Vector3(0.08f, 0.08f, 0.2f), 0.022f, TntLight, TntRed, TntDark, Navy);
                Color32 team = TeamColor(b.Team);
                _s.Chunk(at + back * 0.5f, rot, new Vector3(0.3f, 0.14f, 0.035f), 0.015f, team, team, A(Navy, 1f), Navy);
                BundlesDrawn++;
            }
        }
    }
}
