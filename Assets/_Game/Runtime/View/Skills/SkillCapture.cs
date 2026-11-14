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
    /// The -skillShots evidence director (feat/skill-effects-1; added by SkillFxView when the flag is on). Capture flags of
    /// this lane only (the building and wall-damage lanes keep their own files):
    ///   -skillShots              run the director, then quit the player.
    ///   -skillShotsOnly NAME     one class only (Warrior, Farmer, Miner, Demolitions, Builder, Healer) or Kaiju.
    ///   -skillShotsNoKaiju       skip the Kaiju part (it waits for a hatch, up to 150 s of wall time).
    /// For every class: the world is reset with actor 0 (the local player) forced to that class, the sim clock stopped
    /// (TimeScale 0, only StepTicks advances), actor 0 standing at its spawn facing the map centre, one enemy at 1.3 u in
    /// front (melee), two enemies 3.5 u in front (Root Seeds, TNT), two hurt allies to the sides (heals). The placed actors
    /// are held at their spots every tick. Each skill is pressed through SimDriverBehaviour.ScriptedHumanFrame (the real
    /// input path, so the real rules run) at press tick T, and stills are written to the data folder:
    ///   shot-skill-&lt;class&gt;-&lt;skill&gt;-0-before.png (T - 1), -1-windup.png (T + 0.1 s), -2-peak.png (T + 0.4 s),
    ///   -3-after.png (T + 1.2 s), and for TNT -4-blast.png (T + 1.7 s).
    /// Kaiju: shot-skill-kaiju-claw-0-windup / -1-hit and shot-skill-kaiju-slam-0-telegraph / -1-slam, from a bot match
    /// run fast until the Kaiju hatches and swings. Every shot logs a [CM.Skill] line with the counts SkillFxCore drew.
    /// </summary>
    public sealed class SkillCapture : MonoBehaviour
    {
        public const string Flag = "-skillShots", OnlyFlag = "-skillShotsOnly", NoKaijuFlag = "-skillShotsNoKaiju";
        public const string Prefix = "shot-skill-";
        public static readonly double[] BeatSeconds = { 0.1, 0.4, 1.2 };
        static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        SimDriverBehaviour _driver;
        SkillFxView _view;
        readonly InputFrame _frame = SimInput.EmptyInput();
        readonly int[] _held = new int[5];
        readonly double[] _heldX = new double[5], _heldZ = new double[5];
        int _heldCount;
        double _dirX = 1, _dirZ;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _view = GetComponent<SkillFxView>();
            if (_driver == null || _view == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        void OnDestroy() { SimDriverBehaviour.ScriptedHumanFrame = null; }

        static void Log(string s) => Debug.Log("[CM.Skill] " + s);

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        IEnumerator Shot(string name)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(GamePaths.DataDir, Prefix + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            SkillFxCore c = _view.Core;
            Log("shot " + name + " tick=" + _driver.World.Tick + " casts=" + c.CastsDrawn + " seeds=" + c.SeedsDrawn + " roots=" + c.RootsDrawn
                + " slowed=" + c.MarkedCount + " glows=" + c.GlowCount + " lifts=" + c.LiftsDrawn + " shields=" + c.ShieldsDrawn
                + " sparks=" + c.SparksDrawn + " bundles=" + c.BundlesDrawn + " overlay=" + _view.Overlay.Applied + " terraform=" + c.LastTerraformCode
                + " verts=" + _view.Budget);
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
            string only = Arg(OnlyFlag);
            for (int k = 0; k < Classes.Length; k++)
            {
                ClassId cls = Classes[k];
                if (only != null && !string.Equals(only, cls.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                Stage(cls);
                yield return null;
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
            _driver.TimeScale = scale;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        void Stage(ClassId cls)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            SimDriverBehaviour.ScriptedHumanFrame = Clear();
            _driver.StepTicks(3);
            Place(w);
            Log("stage " + cls + " at (" + w.X[0].ToString("0.0") + ", " + w.Z[0].ToString("0.0") + ") held " + _heldCount);
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

        /// <summary>Faces actor 0 to the map centre and holds three enemies and two hurt allies at fixed spots round him.</summary>
        void Place(World w)
        {
            double x0 = w.X[0], z0 = w.Z[0];
            double cx = w.Map.Width * 0.5 - x0, cz = w.Map.Height * 0.5 - z0;
            double d = Math.Sqrt(cx * cx + cz * cz);
            _dirX = d > 1e-6 ? cx / d : 1;
            _dirZ = d > 1e-6 ? cz / d : 0;
            // step off the base pad (dig, mines and barricades are refused on pad ground): the first floor spot 7..3 u out
            for (double out_ = 7; out_ >= 3; out_ -= 1)
            {
                double mx = x0 + _dirX * out_, mz = z0 + _dirZ * out_;
                if (Floor(w, mx, mz) && Floor(w, mx + _dirX * 3.6, mz + _dirZ * 3.6)) { x0 = mx; z0 = mz; Put(w, 0, x0, z0); break; }
            }
            w.Actors[0].Facing = Math.Atan2(_dirZ, _dirX);
            double px = -_dirZ, pz = _dirX;
            TeamId me = w.Actors[0].Team;
            double[] fx = { 1.3, 3.4, 3.6, 0.3, 0.6 }, fs = { 0, 0.6, -0.6, 1.6, -1.6 };
            _heldCount = 0;
            int enemies = 0, allies = 0;
            for (int i = 1; i < w.ActorCount && _heldCount < 5; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive) continue;
                bool enemy = a.Team != me;
                int slot;
                if (enemy && enemies < 3) slot = enemies++;
                else if (!enemy && allies < 2) slot = 3 + allies++;
                else continue;
                double x = x0 + _dirX * fx[slot] + px * fs[slot], z = z0 + _dirZ * fx[slot] + pz * fs[slot];
                if (!Floor(w, x, z)) { Log("spot " + slot + " is not floor; actor " + i + " left where it is"); continue; }
                Put(w, i, x, z);
                if (!enemy) a.Hp = a.MaxHp * 0.5;
                _held[_heldCount] = i;
                _heldX[_heldCount] = x;
                _heldZ[_heldCount] = z;
                _heldCount++;
            }
        }

        /// <summary>Steps n ticks, holding the placed actors on their spots.</summary>
        void Step(int n)
        {
            World w = _driver.World;
            for (int t = 0; t < n; t++)
            {
                _driver.StepTicks(1);
                for (int k = 0; k < _heldCount; k++)
                {
                    int id = _held[k];
                    if (id < w.ActorCount && w.Actors[id].Alive) Put(w, id, _heldX[k], _heldZ[k]);
                }
            }
        }

        void Aim(World w, SkillVisual v)
        {
            double x0 = w.X[0], z0 = w.Z[0];
            double reach;
            switch (v.Id)
            {
                case SkillId.FarmerRoots: case SkillId.DemoTnt: reach = 3.5; break;
                case SkillId.MinerBarricade: reach = 1.8; break;
                case SkillId.MinerDig: case SkillId.MinerPlace: case SkillId.DemoMine: reach = 1.2; break;
                case SkillId.HealerMend: case SkillId.HealerWings:
                    // aim at the first held ally
                    for (int k = 0; k < _heldCount; k++)
                    {
                        int id = _held[k];
                        if (w.Actors[id].Team == w.Actors[0].Team) { _frame.AimX = w.X[id]; _frame.AimZ = w.Z[id]; return; }
                    }
                    reach = 1.6; break;
                default: reach = 1.3; break;
            }
            _frame.AimX = x0 + _dirX * reach;
            _frame.AimZ = z0 + _dirZ * reach;
        }

        IEnumerator Fire(ClassId cls, SkillVisual v)
        {
            World w = _driver.World;
            double hz = w.Config.SimHz;
            string n = cls.ToString().ToLowerInvariant() + "-" + v.Key;
            if (!w.Actors[0].Alive) { Log(n + ": actor 0 is down; restaging"); Stage(cls); }
            if (v.Id == SkillId.HealerPurify)
            {
                // Purify works only within PurifyRange of the shrine: stand 2 u from it, facing it.
                ShrineDef sh = w.Map.Shrine;
                if (sh != null)
                {
                    double sx = sh.X - 2.0, sz = sh.Z;
                    if (!Floor(w, sx, sz)) sx = sh.X + 2.0;
                    Put(w, 0, sx, sz);
                    w.Actors[0].Facing = Math.Atan2(sh.Z - sz, sh.X - sx);
                }
            }
            Actor a = w.Actors[0];
            a.AttackCd = 0;
            a.AbilityCd = 0;
            Clear();
            Aim(w, v);
            if (v.Id == SkillId.HealerPurify && w.Map.Shrine != null) { _frame.AimX = w.Map.Shrine.X; _frame.AimZ = w.Map.Shrine.Z; }
            Step(2);
            yield return Shot(n + "-0-before");
            if (v.Press == SkillPress.TerraformPlace)
            {
                _frame.TerraformSwitch = true;
                Step(1);
                _frame.TerraformSwitch = false;
                Step(1);
            }
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
            for (int b = 0; b < BeatSeconds.Length; b++)
            {
                int at = press + (int)Math.Round(BeatSeconds[b] * hz);
                if (w.Tick < at) Step(at - w.Tick);
                yield return Shot(n + "-" + (b + 1) + (b == 0 ? "-windup" : b == 1 ? "-peak" : "-after"));
            }
            if (v.Id == SkillId.DemoTnt)
            {
                int at = press + (int)Math.Round(1.7 * hz);
                if (w.Tick < at) Step(at - w.Tick);
                yield return Shot(n + "-4-blast");
            }
            Log(n + ": fired " + (_view.Core.Fired[(int)v.Id] - firedBefore) + " time(s) (0 means the sim refused the press here)");
        }

        IEnumerator KaijuShots()
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = _driver.Config });
            _driver.HumanAutopilot = true;
            _driver.TimeScale = 0;
            _heldCount = 0;
            SkillFxCore c = _view.Core;
            float t0 = Time.realtimeSinceStartup;
            int claw = c.KaijuClawTick, slamT = c.KaijuSlamTelegraphTick, slam = c.KaijuSlamTick;
            bool clawShot = false, slamShot = false;
            while (Time.realtimeSinceStartup - t0 < 150f && !(clawShot && slamShot) && w.Match.Phase == MatchPhase.Playing)
            {
                Kaiju k = w.Kaiju;
                _driver.StepTicks(k.State == KaijuState.Egg ? 30 : 2);
                if (k.State == KaijuState.Egg) { yield return null; continue; }
                if (!clawShot && c.KaijuClawTick != claw)
                {
                    Near(w);
                    yield return Shot("kaiju-claw-0-windup");
                    _driver.StepTicks(9);
                    yield return Shot("kaiju-claw-1-hit");
                    clawShot = true;
                }
                if (!slamShot && c.KaijuSlamTelegraphTick != slamT)
                {
                    Near(w);
                    _driver.StepTicks((int)Math.Round(0.6 * w.Config.SimHz));
                    yield return Shot("kaiju-slam-0-telegraph");
                    int guard = 0;
                    while (c.KaijuSlamTick == slam && guard++ < 120) _driver.StepTicks(1);
                    yield return Shot("kaiju-slam-1-slam");
                    slamShot = true;
                }
                yield return null;
            }
            Log("kaiju: claw " + (clawShot ? "shot" : "not seen") + ", slam " + (slamShot ? "shot" : "not seen"));
        }

        /// <summary>Stands actor 0 (the camera's target) 4 u from the Kaiju so it is in frame.</summary>
        static void Near(World w)
        {
            Kaiju k = w.Kaiju;
            double x = k.X + 4, z = k.Z + 4;
            if (!Floor(w, x, z)) { x = k.X - 4; z = k.Z - 4; }
            if (Floor(w, x, z) && w.Actors[0].Alive) Put(w, 0, x, z);
        }
    }
}
