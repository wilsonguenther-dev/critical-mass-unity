using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Skills
{
    /// <summary>
    /// The -skillShots evidence director (feat/skill-effects-1, staging fixed in fix/skill-effects-2; added by SkillFxView
    /// when the flag is on). Capture flags of this lane only:
    ///   -skillShots              run the director, then quit the player.
    ///   -skillShotsOnly NAME     one class only (Warrior, Farmer, Miner, Demolitions, Builder, Healer) or Kaiju.
    ///   -skillShotsNoKaiju       skip the Kaiju part.
    /// fix/skill-effects-2: under -autoplay, AutoplayArgs forced actor 0 back onto the bots and the clock back to speed every
    /// frame, so the presses were ignored and the shots were taken in the hill brawl. Now the director holds the clock
    /// (AutoplayArgs.ClockHeld), takes actor 0 off the bots before every tick it steps, and stages each class on the open
    /// floor spot with the most room (away from the hill and both pads), with two enemies, one hurt ally and a melee enemy
    /// held at fixed spots. All positions and aims are sim space, so the camera heading (135 by default) does not matter.
    /// Each beat writes a HUD-free close-up rendered from the gameplay camera's own angle at 11 u
    /// (shot-skill-&lt;class&gt;-&lt;skill&gt;-&lt;n&gt;-&lt;beat&gt;.png: 0-before, 1-windup +0.1 s, 2-peak +0.4 s, 3-after +1.2 s,
    /// 4-blast +1.7 s for TNT) and the peak also as the normal full screen (...-2-peak-wide.png). The Kaiju part forces the
    /// hatch (World.ForceHatch, the rules' own switch), stands the fighters next to it and shoots the claw wind-up and hit and
    /// the slam telegraph and slam. Every press logs "[CM.Skill] class-skill: FIRED" or "REFUSED (reason)".
    /// <para>capture-gaps-1: five presses never fired on the open stage. Why, from the rules:
    /// Miner barricade: the aimed tile must be at least MinDistanceFromActor (0.9) plus a body radius from EVERY actor, the
    /// Miner himself included; the 1.8 u aim landed on a tile within 1.35 u of the Miner or of the enemy held 1.3 u ahead
    /// (RefusedOccupied). Miner dig: the world-wide terrain gap (WallChangeTick + MinGapTicks) was still running a few ticks
    /// after the reset (RefusedRate, -5), and the 1.2 u aim cell could touch the Miner's own circle or a held enemy (-3).
    /// Miner place: the team terrain cooldown and the gap restart at the pre-dig, 23 ticks before the press (-5).
    /// Builder build: a ladder or bridge needs a wall, cliff, gap or water edge ahead and a full bank; the open stage has
    /// neither. Healer purify: needs corruption delivered (net units above 0) with the egg unhatched; a fresh match has none.
    /// Now each of those skills is staged legally with the sim's own checks: the held fighters step 5 u behind, the Miner's aim
    /// is searched around him (then across the map) until BarricadeRefusal or DigRefusal says 0, the terrain gap and team
    /// cooldown are cleared before each terrain press (fixture, as after a long match), the Builder stands at a site where
    /// BuildRefusal is 0 with a filled bank, and the Healer stage gets four corruption units delivered by the other team.</para>
    /// </summary>
    public sealed class SkillCapture : MonoBehaviour
    {
        public const string Flag = "-skillShots", OnlyFlag = "-skillShotsOnly", NoKaijuFlag = "-skillShotsNoKaiju";
        public const string Prefix = "shot-skill-";
        public const float CloseDistance = 11f;
        public const int CloseW = 1600, CloseH = 900;
        public static readonly double[] BeatSeconds = { 0.1, 0.4, 1.2 };
        static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        SimDriverBehaviour _driver;
        SkillFxView _view;
        readonly InputFrame _frame = SimInput.EmptyInput();
        readonly int[] _held = new int[4];
        readonly double[] _heldX = new double[4], _heldZ = new double[4];
        int _heldCount;
        double _dirX = 1, _dirZ;
        double _aimX = double.NaN, _aimZ = double.NaN;   // a legal aim found by StageLegal (NaN = the default reach ahead)
        bool _bots;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _view = GetComponent<SkillFxView>();
            if (_driver == null || _view == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            SimDriverBehaviour.ScriptedHumanFrame = null;
            AutoplayArgs.ClockHeld = false;
        }

        static void Log(string s) => Debug.Log("[CM.Skill] " + s);

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        string Counts()
        {
            SkillFxCore c = _view.Core;
            return "tick=" + _driver.World.Tick + " casts=" + c.CastsDrawn + " seeds=" + c.SeedsDrawn + " roots=" + c.RootsDrawn
                + " slowed=" + c.MarkedCount + " glows=" + c.GlowCount + " lifts=" + c.LiftsDrawn + " shields=" + c.ShieldsDrawn
                + " sparks=" + c.SparksDrawn + " bundles=" + c.BundlesDrawn + " overlay=" + _view.Overlay.Applied + " verts=" + _view.Budget;
        }

        /// <summary>A close-up from the gameplay camera's own rotation, CloseDistance from focus, HUD-free (camera render only).</summary>
        IEnumerator Shot(string name, Vector3 focus, bool wideToo)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            if (wideToo) ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, Prefix + name + "-wide.png"));
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                Transform t = cam.transform;
                Vector3 p = t.position;
                Quaternion r = t.rotation;
                RenderTexture old = cam.targetTexture, prev = RenderTexture.active;
                RenderTexture rt = RenderTexture.GetTemporary(CloseW, CloseH, 24);
                Texture2D tex = null;
                try
                {
                    t.position = focus - r * Vector3.forward * CloseDistance;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex = new Texture2D(CloseW, CloseH, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, CloseW, CloseH), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(GamePaths.DataDir, Prefix + name + ".png"), tex.EncodeToPNG());
                }
                catch (Exception ex) { Log("close-up " + name + " failed: " + ex.Message); }
                finally
                {
                    cam.targetTexture = old;
                    RenderTexture.active = prev;
                    t.position = p;
                    RenderTexture.ReleaseTemporary(rt);
                    if (tex != null) Destroy(tex);
                }
            }
            Log("shot " + name + " " + Counts());
            yield return null;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            double scale = _driver.TimeScale;
            AutoplayArgs.ClockHeld = true;
            string only = Arg(OnlyFlag);
            for (int k = 0; k < Classes.Length; k++)
            {
                ClassId cls = Classes[k];
                if (only != null && !string.Equals(only, cls.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                for (int s = 0; s < SkillVisuals.All.Length; s++)
                {
                    SkillVisual v = SkillVisuals.All[s];
                    if (v.Kaiju || v.Cls != cls || v.Press == SkillPress.None) continue;
                    IEnumerator f = Fire(cls, v);
                    while (f.MoveNext()) yield return f.Current;
                }
            }
            SimDriverBehaviour.ScriptedHumanFrame = null;
            bool kaiju = Array.IndexOf(Environment.GetCommandLineArgs(), NoKaijuFlag) < 0
                && (only == null || string.Equals(only, "Kaiju", StringComparison.OrdinalIgnoreCase));
            if (kaiju)
            {
                IEnumerator kf = KaijuShots();
                while (kf.MoveNext()) yield return kf.Current;
            }
            AutoplayArgs.ClockHeld = false;
            _driver.TimeScale = scale;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        /// <summary>A fresh world with actor 0 forced to cls, the clock held, actor 0 off the bots, the cast staged in open ground.</summary>
        void Stage(ClassId cls)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            _bots = false;
            _driver.TimeScale = 0;
            AutoplayArgs.ClockHeld = true;
            _heldCount = 0;
            _aimX = _aimZ = double.NaN;
            SimDriverBehaviour.ScriptedHumanFrame = Clear();
            Step(3);
            Place(w);
            Log("stage " + cls + " at (" + w.X[0].ToString("0.0") + ", " + w.Z[0].ToString("0.0") + ") facing (" + _dirX.ToString("0.00") + ", " + _dirZ.ToString("0.00") + ") held " + _heldCount);
        }

        InputFrame Clear()
        {
            _frame.CopyFrom(SimInput.EmptyInput());
            _frame.AimX = double.NaN;
            _frame.AimZ = double.NaN;
            return _frame;
        }

        static bool Floor(World w, double x, double z)
        {
            MapDef m = w.Map;
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= m.Width || cz >= m.Height) return false;
            return m.Cells[cz * m.Width + cx] == Cell.Floor;
        }

        static void Put(World w, int id, double x, double z)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
        }

        static readonly double[] Fwd = { 1.3, 3.4, 3.6, 0.4 }, Side = { 0, 0.7, -0.7, 1.7 };

        bool LayoutFits(World w, double x, double z, double dx, double dz)
        {
            if (!Floor(w, x, z)) return false;
            for (int k = 0; k < Fwd.Length; k++)
                if (!Floor(w, x + dx * Fwd[k] - dz * Side[k], z + dz * Fwd[k] + dx * Side[k])) return false;
            return Floor(w, x + dx * 1.8, z + dz * 1.8) && Floor(w, x + dx * 1.2, z + dz * 1.2);
        }

        /// <summary>
        /// The open floor spot with the most room: every cast cell is floor, it is at least 9 u from the map centre (the hill)
        /// and as far as possible from every actor that is not staged. Then three enemies and one hurt ally are held round it.
        /// </summary>
        void Place(World w)
        {
            MapDef m = w.Map;
            double mx = m.Width * 0.5, mz = m.Height * 0.5;
            double best = -1, bx = w.X[0], bz = w.Z[0], bdx = 1, bdz = 0;
            for (int cz = 3; cz < m.Height - 3; cz += 2)
            {
                for (int cx = 3; cx < m.Width - 3; cx += 2)
                {
                    double x = cx + 0.5, z = cz + 0.5;
                    double hx = x - mx, hz = z - mz;
                    if (hx * hx + hz * hz < 81) continue;
                    if (!Floor(w, x, z)) continue;
                    double clear = double.MaxValue;
                    for (int i = 1; i < w.ActorCount; i++)
                    {
                        double ax = w.X[i] - x, az = w.Z[i] - z;
                        double d2 = ax * ax + az * az;
                        if (d2 < clear) clear = d2;
                    }
                    if (clear <= best) continue;
                    for (int f = 0; f < 8; f++)
                    {
                        double a = f * Math.PI / 4;
                        double dx = Math.Cos(a), dz = Math.Sin(a);
                        if (!LayoutFits(w, x, z, dx, dz)) continue;
                        best = clear; bx = x; bz = z; bdx = dx; bdz = dz;
                        break;
                    }
                }
            }
            if (best < 0) Log("no open spot fits the layout; staging at the spawn");
            _dirX = bdx;
            _dirZ = bdz;
            Put(w, 0, bx, bz);
            w.Actors[0].Facing = Math.Atan2(_dirZ, _dirX);
            TeamId me = w.Actors[0].Team;
            _heldCount = 0;
            int enemies = 0, allies = 0;
            for (int i = 1; i < w.ActorCount && _heldCount < 4; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive) continue;
                bool enemy = a.Team != me;
                int slot;
                if (enemy && enemies < 3) slot = enemies++;
                else if (!enemy && allies < 1) slot = 3 + allies++;
                else continue;
                double x = bx + _dirX * Fwd[slot] - _dirZ * Side[slot], z = bz + _dirZ * Fwd[slot] + _dirX * Side[slot];
                if (!Floor(w, x, z)) { Log("spot " + slot + " is not floor; actor " + i + " left where it is"); continue; }
                Put(w, i, x, z);
                if (!enemy) a.Hp = a.MaxHp * 0.5;
                a.SpawnProtect = 0;
                _held[_heldCount] = i;
                _heldX[_heldCount] = x;
                _heldZ[_heldCount] = z;
                _heldCount++;
            }
        }

        /// <summary>Steps n ticks with actor 0 on the scripted frame (or the bots), holding the staged actors on their spots.</summary>
        void Step(int n)
        {
            World w = _driver.World;
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            for (int t = 0; t < n; t++)
            {
                _driver.HumanAutopilot = _bots;   // AutoplayArgs turns the bots back on every frame
                _driver.StepTicks(1);
                for (int k = 0; k < _heldCount; k++)
                {
                    int id = _held[k];
                    if (id < w.ActorCount && w.Actors[id].Alive) Put(w, id, _heldX[k], _heldZ[k]);
                }
            }
        }

        Vector3 Focus(World w)
        {
            double fx = w.X[0] + _dirX * 1.6, fz = w.Z[0] + _dirZ * 1.6;
            return SimSpace.ToWorld(fx, GroundHeight.At(w.Map, fx, fz) + 0.8, fz);
        }

        void Aim(World w, SkillVisual v)
        {
            if (!double.IsNaN(_aimX)) { _frame.AimX = _aimX; _frame.AimZ = _aimZ; return; }
            double reach;
            switch (v.Id)
            {
                case SkillId.FarmerRoots: case SkillId.DemoTnt: reach = 3.5; break;
                case SkillId.MinerBarricade: reach = 1.8; break;
                case SkillId.MinerDig: case SkillId.MinerPlace: case SkillId.DemoMine: reach = 1.2; break;
                case SkillId.HealerMend: case SkillId.HealerWings:
                    for (int k = 0; k < _heldCount; k++)
                    {
                        int id = _held[k];
                        if (w.Actors[id].Team == w.Actors[0].Team) { _frame.AimX = w.X[id]; _frame.AimZ = w.Z[id]; return; }
                    }
                    reach = 1.7; break;
                default: reach = 1.3; break;
            }
            _frame.AimX = w.X[0] + _dirX * reach;
            _frame.AimZ = w.Z[0] + _dirZ * reach;
        }

        /// <summary>The rules' own words for a refusal where the sim has a public check; else the state that matters.</summary>
        string WhyRefused(World w, SkillVisual v, double cdBefore)
        {
            Actor a = w.Actors[0];
            string state = " (alive " + a.Alive + ", class " + a.Cls + ", cooldown before press " + cdBefore.ToString("0.00") + " s, last ability code " + _view.Core.LastAbilityCode0
                + ", last terraform code " + _view.Core.LastTerraformCode + ")";
            switch (v.Id)
            {
                case SkillId.MinerBarricade: return "BarricadeRefusal=" + Barricades.BarricadeRefusal(w, 0, _frame.AimX, _frame.AimZ) + state;
                case SkillId.BuilderBuild:
                    return "BuildRefusal=" + BuiltPieces.BuildRefusal(w, 0, (int)Math.Round(_dirX), (int)Math.Round(_dirZ)) + " (a ladder or bridge needs a wall, cliff, gap or water edge ahead)" + state;
                case SkillId.HealerPurify: return "PurifyRefusal=" + Healer.PurifyRefusal(w, 0) + " (-1 too far from the shrine, -2 no cleanse possible now)" + state;
                case SkillId.DemoTnt: return "TntWaitTicks=" + Demolitions.TntWaitTicks(w, 0) + state;
                case SkillId.MinerDig: case SkillId.MinerPlace: return "Terraform code " + _view.Core.LastTerraformCode + " (-1 outside, -2 bad target, -3 occupied, -4 cuts the path, -5 rate)" + state;
                default: return "no event" + state;
            }
        }

        IEnumerator Fire(ClassId cls, SkillVisual v)
        {
            Stage(cls);                      // a fresh, open stage per skill: no crowd, no leftover cooldowns
            World w = _driver.World;
            double hz = w.Config.SimHz;
            string n = cls.ToString().ToLowerInvariant() + "-" + v.Key;
            if (NeedsLegalStage(v.Id)) StageLegal(w, v, n);
            if (v.Id == SkillId.MinerPlace)
            {
                // place needs a carried block and a hole: dig the cell first (not shot), then switch to Place mode
                Clear(); Aim(w, v);
                OpenTerrainGap(w);
                _frame.Terraform = true; Step(2); _frame.Terraform = false; Step(20);
                _frame.TerraformSwitch = true; Step(1); _frame.TerraformSwitch = false; Step(2);
                OpenTerrainGap(w);   // the dig restarted the gap and the team cooldown: clear them again for the place
                Log(n + ": pre-dig terraform code " + _view.Core.LastTerraformCode);
            }
            if (v.Id == SkillId.HealerPurify)
            {
                ShrineDef sh = w.Map.Shrine;
                if (sh != null)
                {
                    double sx = sh.X - 2.5, sz = sh.Z;
                    if (!Floor(w, sx, sz)) sx = sh.X + 2.5;
                    Put(w, 0, sx, sz);
                    w.Actors[0].Facing = Math.Atan2(sh.Z - sz, sh.X - sx);
                    _dirX = Math.Cos(w.Actors[0].Facing);
                    _dirZ = Math.Sin(w.Actors[0].Facing);
                }
            }
            Actor a = w.Actors[0];
            a.AttackCd = 0;
            a.AbilityCd = 0;
            double cdBefore = v.Press == SkillPress.Attack ? a.AttackCd : a.AbilityCd;
            if (v.Id != SkillId.MinerPlace) Clear();
            Aim(w, v);
            if (v.Id == SkillId.HealerPurify && w.Map.Shrine != null) { _frame.AimX = w.Map.Shrine.X; _frame.AimZ = w.Map.Shrine.Z; }
            Step(2);
            Vector3 focus = Focus(w);
            yield return Shot(n + "-0-before", focus, false);
            int press = w.Tick;
            int firedBefore = _view.Core.Fired[(int)v.Id];
            switch (v.Press)
            {
                case SkillPress.Attack: _frame.Attack = true; break;
                case SkillPress.Ability: _frame.Ability = true; break;
                default: _frame.Terraform = true; break;
            }
            Step(2);
            _frame.Attack = _frame.Ability = _frame.Terraform = false;
            bool fired = _view.Core.Fired[(int)v.Id] > firedBefore || v.Press == SkillPress.Attack && w.Actors[0].AttackCd > 0;
            Log(n + ": " + (fired ? "FIRED" : "REFUSED " + WhyRefused(w, v, cdBefore)));
            for (int b = 0; b < BeatSeconds.Length; b++)
            {
                int at = press + (int)Math.Round(BeatSeconds[b] * hz);
                if (w.Tick < at) Step(at - w.Tick);
                yield return Shot(n + "-" + (b + 1) + (b == 0 ? "-windup" : b == 1 ? "-peak" : "-after"), focus, b == 1);
            }
            if (v.Id == SkillId.DemoTnt)
            {
                int at = press + (int)Math.Round(1.7 * hz);
                if (w.Tick < at) Step(at - w.Tick);
                yield return Shot(n + "-4-blast", focus, false);
            }
        }

        static bool NeedsLegalStage(SkillId id) =>
            id == SkillId.MinerBarricade || id == SkillId.MinerDig || id == SkillId.MinerPlace || id == SkillId.BuilderBuild || id == SkillId.HealerPurify;

        /// <summary>Fixture: the terrain-change gap and both teams' terrain cooldowns long past (as after a long match).</summary>
        static void OpenTerrainGap(World w)
        {
            w.WallChangeTick = w.Tick - 1000000;
            int[] r = w.Terraform != null ? w.Terraform.TeamReadyTick : null;
            if (r != null) for (int i = 0; i < r.Length; i++) r[i] = 0;
        }

        /// <summary>The held fighters step 5 u behind (or beside) actor 0, out of every placement and occupancy check.</summary>
        void ClearCast(World w)
        {
            for (int k = 0; k < _heldCount; k++)
            {
                double back = 5 + k * 1.2;
                double x = w.X[0] - _dirX * back, z = w.Z[0] - _dirZ * back;
                if (!Floor(w, x, z)) { x = w.X[0] + _dirZ * back; z = w.Z[0] - _dirX * back; }
                if (!Floor(w, x, z)) { x = w.X[0] - _dirZ * back; z = w.Z[0] + _dirX * back; }
                if (!Floor(w, x, z)) continue;
                _heldX[k] = x;
                _heldZ[k] = z;
                Put(w, _held[k], x, z);
            }
        }

        /// <summary>A legal Miner aim from where actor 0 stands (16 directions, reach first), checked with the sim's own refusal.</summary>
        bool MinerAim(World w, SkillId id)
        {
            double range = id == SkillId.MinerBarricade ? w.Config.Abilities.MinerBarricade.PlaceRange : w.Config.Abilities.MinerTerraform.Reach;
            double facing = w.Actors[0].Facing;
            for (int f = 0; f < 16; f++)
            {
                double a = f * Math.PI / 8, dx = Math.Cos(a), dz = Math.Sin(a);
                for (int s = 0; s < 3; s++)
                {
                    double d = range * (1.0 - 0.2 * s);
                    double ax = w.X[0] + dx * d, az = w.Z[0] + dz * d;
                    w.Actors[0].Facing = Math.Atan2(dz, dx);
                    int code;
                    if (id == SkillId.MinerBarricade) code = Barricades.BarricadeRefusal(w, 0, ax, az);
                    else code = Terraform.TargetCell(w, 0, ax, az, out int cx, out int cz) ? Terraform.DigRefusal(w, cx, cz) : Terraform.RefusedOutside;
                    if (code != 0) continue;
                    _dirX = dx; _dirZ = dz; _aimX = ax; _aimZ = az;
                    return true;
                }
            }
            w.Actors[0].Facing = facing;
            return false;
        }

        /// <summary>A Builder site where BuildRefusal is 0 (the bank filled first), nearest the staged spot.</summary>
        bool BuildSite(World w)
        {
            if (w.Builder != null && w.Builder.Spent != null)
            {
                int t = (int)w.Actors[0].Team;
                w.Builder.Spent[t] = Math.Min(w.Builder.Spent[t], -20.0);   // fixture: enough material for any build
            }
            MapDef m = w.Map;
            double ox = w.X[0], oz = w.Z[0], best = double.MaxValue;
            int bx = -1, bz = 0, bd = 0;
            int[] sx = { 1, -1, 0, 0 }, sz = { 0, 0, 1, -1 };
            for (int cz = 1; cz < m.Height - 1; cz++)
                for (int cx = 1; cx < m.Width - 1; cx++)
                {
                    if (m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                    double ddx = cx + 0.5 - ox, ddz = cz + 0.5 - oz, d2 = ddx * ddx + ddz * ddz;
                    if (d2 >= best) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        Put(w, 0, cx + 0.5, cz + 0.5);
                        w.Actors[0].Facing = Math.Atan2(sz[d], sx[d]);
                        if (BuiltPieces.BuildRefusal(w, 0, sx[d], sz[d]) != 0) continue;
                        best = d2; bx = cx; bz = cz; bd = d;
                        break;
                    }
                }
            if (bx < 0) { Put(w, 0, ox, oz); return false; }
            Put(w, 0, bx + 0.5, bz + 0.5);
            _dirX = sx[bd]; _dirZ = sz[bd];
            w.Actors[0].Facing = Math.Atan2(_dirZ, _dirX);
            _aimX = bx + 0.5 + _dirX * 2.0;
            _aimZ = bz + 0.5 + _dirZ * 2.0;
            return true;
        }

        /// <summary>Stage the five skills that need more than open floor (see the class notes), with the sim's own checks.</summary>
        void StageLegal(World w, SkillVisual v, string n)
        {
            OpenTerrainGap(w);
            if (v.Id == SkillId.HealerPurify)
            {
                TeamId other = w.Actors[0].Team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
                if (!(Cleanse.NetCorruptionUnits(w) > 0)) w.Match.Delivered[(int)other].Corruption += 4;   // fixture
                if (w.Healer != null && w.Healer.PurifyReadyTick != null && w.Healer.PurifyReadyTick.Length > 0) w.Healer.PurifyReadyTick[0] = 0;
                Log(n + ": staged corruption net " + Cleanse.NetCorruptionUnits(w).ToString("0.0") + " units, cleanse open " + Cleanse.CleanseOpen(w)
                    + " (phase " + w.Match.Phase + ")");
                return;
            }
            if (v.Id == SkillId.BuilderBuild)
            {
                bool ok = BuildSite(w);
                ClearCast(w);
                Log(n + ": " + (ok ? "legal build site at (" + w.X[0].ToString("0.0") + ", " + w.Z[0].ToString("0.0") + ") step (" + _dirX + ", " + _dirZ + ")" : "no legal build site on the map"));
                return;
            }
            ClearCast(w);
            if (MinerAim(w, v.Id)) { Log(n + ": legal aim at the staged spot (" + _aimX.ToString("0.0") + ", " + _aimZ.ToString("0.0") + ")"); return; }
            MapDef m = w.Map;
            double mx = m.Width * 0.5, mz = m.Height * 0.5;
            for (int cz = 3; cz < m.Height - 3; cz += 2)
                for (int cx = 3; cx < m.Width - 3; cx += 2)
                {
                    double x = cx + 0.5, z = cz + 0.5;
                    if ((x - mx) * (x - mx) + (z - mz) * (z - mz) < 81 || !Floor(w, x, z)) continue;
                    Put(w, 0, x, z);
                    ClearCast(w);
                    if (MinerAim(w, v.Id)) { Log(n + ": legal aim from (" + x + ", " + z + ") at (" + _aimX.ToString("0.0") + ", " + _aimZ.ToString("0.0") + ")"); return; }
                }
            Log(n + ": no legal aim found on the map");
        }

        IEnumerator KaijuShots()
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = _driver.Config });
            _bots = true;
            _heldCount = 0;
            w.ForceHatch = true;             // the rules' own hatch switch: no waiting minutes for the egg
            SkillFxCore c = _view.Core;
            int claw = c.KaijuClawTick, slamT = c.KaijuSlamTelegraphTick, slam = c.KaijuSlamTick;
            bool clawShot = false, slamShot = false;
            int stepped = 0, limit = (int)(240 * w.Config.SimHz);
            while (stepped < limit && !(clawShot && slamShot) && w.Match.Phase == MatchPhase.Playing)
            {
                Kaiju k = w.Kaiju;
                if (k.State != KaijuState.Egg) Gather(w);
                Step(2);
                stepped += 2;
                if (!clawShot && c.KaijuClawTick != claw)
                {
                    Vector3 f = KaijuFocus(w);
                    yield return Shot("kaiju-claw-0-windup", f, true);
                    Step(9);
                    yield return Shot("kaiju-claw-1-hit", f, false);
                    clawShot = true;
                }
                if (!slamShot && c.KaijuSlamTelegraphTick != slamT)
                {
                    Vector3 f = KaijuFocus(w);
                    Step((int)Math.Round(0.6 * w.Config.SimHz));
                    yield return Shot("kaiju-slam-0-telegraph", f, true);
                    int guard = 0;
                    while (c.KaijuSlamTick == slam && guard++ < 120) Step(1);
                    yield return Shot("kaiju-slam-1-slam", f, false);
                    slamShot = true;
                }
                if (stepped % 60 == 0) yield return null;
            }
            w.ForceHatch = false;
            Log("kaiju: claw " + (clawShot ? "shot" : "NOT SEEN") + ", slam " + (slamShot ? "shot" : "NOT SEEN") + " after " + stepped + " ticks, state " + w.Kaiju.State
                + ", hatched " + w.Match.Hatched + ", phase " + w.Match.Phase);
        }

        /// <summary>Keeps actor 0 and four living fighters 2.5 u from the Kaiju, so it has targets and the camera has it in frame.</summary>
        static void Gather(World w)
        {
            Kaiju k = w.Kaiju;
            int moved = 0;
            for (int i = 0; i < w.ActorCount && moved < 5; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double a = moved * 1.2566;
                double x = k.X + Math.Cos(a) * 2.5, z = k.Z + Math.Sin(a) * 2.5;
                if (!Floor(w, x, z)) continue;
                if (Math.Abs(w.X[i] - x) + Math.Abs(w.Z[i] - z) > 6) Put(w, i, x, z);
                moved++;
            }
        }

        static Vector3 KaijuFocus(World w)
        {
            Kaiju k = w.Kaiju;
            return SimSpace.ToWorld(k.X, GroundHeight.At(w.Map, k.X, k.Z) + 1.5, k.Z);
        }
    }
}
