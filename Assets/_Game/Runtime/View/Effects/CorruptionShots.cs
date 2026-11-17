using System;
using System.Collections;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// CM b13 item C: the corruption rule in a PLAYER build. Autoplay run with <c>-corruptionShots [DIR]</c> (default DIR =
    /// GamePaths.DataDir/corruption-shots); installed on the match's driver by CorruptionView only when the flag is set.
    /// The world is reset to seed 1337 16v16 with the driver's config, the clock is held and stepped tick by tick, and the
    /// scene is STAGED (positions, cargo and pools are forced where noted; the feed, the rolls, the wild state and the hill
    /// rule run through the real sim paths). Stills, each logged as
    /// <c>[CM.CorruptionShots] NN-name REACHED|NOT REACHED chance=.. pool=.. ..</c>:
    /// 01 veins, 02 a carrier (forced crystal), 03/04/05 the pool at 1 / 5 / 9 (forced, before the hatch: the HUD meter),
    /// 06 feeding a knocked-out Kaiju (the human holds E through the real feed wind-up), 07 a forced failed roll at get-up,
    /// 08 a forced successful roll at get-up, 09 the wild Kaiju on the hill 3 s later (hill contested).
    /// A forced roll picks the pool from the next roll's seeded u (Corruption.RollU) so the result is the intended one.
    /// </summary>
    public sealed class CorruptionShots : MonoBehaviour
    {
        public const string Flag = "-corruptionShots";
        public const int Seed = 1337;

        static bool? _req;
        /// <summary>True when the command line has -corruptionShots.</summary>
        public static bool Requested
        {
            get
            {
                if (_req == null) _req = Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0;
                return _req.Value;
            }
        }

        /// <summary>The pool that makes the next roll fail (chance u / 2) or succeed (chance halfway from u to the cap); NaN when impossible. Pure; tests.</summary>
        public static double ForcedPool(double u, bool success, CorruptionConfig c)
        {
            double p;
            if (success)
            {
                if (!(u < c.Cap - 0.01)) return double.NaN;
                p = u + (c.Cap - u) * 0.5;
            }
            else
            {
                if (!(u > 0.002)) return double.NaN;
                p = u * 0.5;
            }
            return p * p * c.Full;
        }

        SimDriverBehaviour _driver;
        string _dir;
        int _reached, _total;

        static void Log(string s) => Debug.Log("[CM.CorruptionShots] " + s);
        static string F(double v, string f = "0.000") => double.IsNaN(v) ? "NaN" : v.ToString(f, CultureInfo.InvariantCulture);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) { Log("no driver on this object; not run"); enabled = false; return; }
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, Flag);
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "corruption-shots");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            AutoplayArgs.ClockHeld = false;
            SimDriverBehaviour.ScriptedHumanFrame = null;
        }

        void Step(int n)
        {
            for (int t = 0; t < n; t++) _driver.StepTicks(1);
        }

        static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
        }

        IEnumerator Shot(string name, double x, double z, bool ok, string extra)
        {
            var cam = FindAnyObjectByType<CriticalMass.Runtime.Camera.CameraFollowTarget>();
            for (int f = 0; f < 12; f++)
            {
                if (cam != null) cam.SetOverride(x, z);
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(_dir, "corruption-" + name + ".png");
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            World w = _driver.World;
            _total++;
            if (ok) _reached++;
            Log(name + (ok ? " REACHED" : " NOT REACHED") + " chance=" + F(Corruption.Chance(w)) + " pool=" + F(Corruption.Pool(w), "0.00")
                + " nextRollTick=" + Corruption.NextRollTick(w) + " wild=" + Corruption.Wild(w) + " tick=" + w.Tick + " " + extra + " file=" + Path.GetFileName(file));
        }

        /// <summary>Knock the Kaiju out through the real damage path (a fighter the rules let hit it); true when it is down.</summary>
        static bool KnockOut(World w)
        {
            if (w.Kaiju.KoIn > 0) return true;
            KaijuState s = w.Kaiju.State;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                TeamId t = w.Actors[i].Team;
                if (s == KaijuState.Azure && t != TeamId.Crimson) continue;
                if (s == KaijuState.Crimson && t != TeamId.Azure) continue;
                CriticalMass.Sim.Core.Kaiju.DamageKaiju(w, i, w.Kaiju.Hp);
                break;
            }
            return w.Kaiju.KoIn > 0;
        }

        /// <summary>A forced roll at get-up: the pool from the next u, then the knock-out ends on the next tick (the real AtGetUp).</summary>
        bool ForceRoll(World w, bool success, out double chance)
        {
            chance = double.NaN;
            CorruptionConfig c = w.Config.Depth.Corruption;
            if (!KnockOut(w)) return false;
            double u = Corruption.RollU(w.Opts.Seed, w.Corrupt.RollIndex, c);
            double pool = ForcedPool(u, success, c);
            if (double.IsNaN(pool)) { Log("next roll u=" + F(u) + " cannot be forced to " + (success ? "succeed" : "fail")); return false; }
            w.Corrupt.PostPool = pool;
            chance = Corruption.Chance(w);
            w.Kaiju.KoIn = 0.5 / w.Config.SimHz;
            int before = w.Corrupt.RollIndex;
            Step(1);
            return w.Corrupt.RollIndex == before + 1 && w.Corrupt.LastSuccess == success;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime(1f);
            double scale = _driver.TimeScale;
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = Seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = _driver.Config });
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            _driver.HumanAutopilot = true;
            SimDriverBehaviour.ScriptedHumanFrame = null;
            double hz = w.Config.SimHz;
            if (!Corruption.On(w)) { Log("the corruption rule is off in this config (not the Full profile); NOT REACHED"); yield return Quit(scale, 3); yield break; }
            Step(2);

            // 01 veins
            double vx = 0, vz = 0;
            int veins = 0, charges = 0;
            foreach (ResourceNode nd in w.Nodes)
                if (nd.Kind == NodeKind.Corruption) { vx += nd.X; vz += nd.Z; veins++; charges += nd.Charges; }
            if (veins > 0) { vx /= veins; vz /= veins; }
            yield return Shot("01-veins", vx, vz, veins > 0 && charges > 0, "veins=" + veins + " charges=" + charges);

            // 02 a carrier (forced crystal, placed by the veins)
            int carrier = -1;
            for (int i = 0; i < w.ActorCount; i++)
                if (!w.Actors[i].IsHuman && w.Actors[i].Alive && Roles.MayCorrupt(w, w.Actors[i].Cls) && w.Actors[i].Cls != ClassId.Healer) { carrier = i; break; }
            if (carrier >= 0)
            {
                Place(w, carrier, vx + 2.5, vz + 2.5);
                w.Actors[carrier].Carry = CarryKind.Corruption;
                w.Actors[carrier].CarryAmount = 1;
            }
            yield return Shot("02-carrier", vx + 2.5, vz + 2.5, carrier >= 0 && Corruption.Carrying(w, carrier), "actor=" + carrier + " speedScale=" + F(carrier >= 0 ? Corruption.SpeedScale(w, carrier) : 1, "0.00"));

            // 03..05 the pool at 1 / 5 / 9 (forced, before the hatch)
            double[] pools = { 1, 5, 9 };
            string[] names = { "03-pool-1", "04-pool-5", "05-pool-9" };
            for (int p = 0; p < pools.Length; p++)
            {
                w.Corrupt.PreFed = pools[p] + w.Cleanse.Count * w.Config.KaijuFeed.Cleanse.Units + w.Healer.PurifiedUnits;
                Step(1);
                double want = Corruption.ChanceOf(pools[p], w.Config.Depth.Corruption);
                yield return Shot(names[p], w.Map.Shrine.X, w.Map.Shrine.Z, Math.Abs(Corruption.Chance(w) - want) < 1e-9, "expect=" + F(want));
            }
            w.Corrupt.PreFed = 0;

            // hatch with an empty pool (no roll), then own it for crimson so the azure human may feed it
            w.ForceHatch = true;
            int stepped = 0;
            while (!w.Match.Hatched && stepped < (int)(240 * hz)) { Step(1); if (++stepped % 300 == 0) yield return null; }
            w.ForceHatch = false;
            if (!w.Match.Hatched) { Log("NOT REACHED: the egg did not hatch"); yield return Quit(scale, 3); yield break; }
            w.Kaiju.State = KaijuState.Crimson;   // staged ownership
            Step((int)(3 * hz));

            // 06 feeding a downed Kaiju: the human carries a crystal and holds E beside it (the real feed wind-up)
            bool down = KnockOut(w);
            int human = -1;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].IsHuman) { human = i; break; }
            bool fed = false;
            if (down && human >= 0 && w.Actors[human].Alive)
            {
                Kaiju k = w.Kaiju;
                Place(w, human, k.X, k.Z + w.Config.Kaiju.Radius + 1.0);
                Actor ha = w.Actors[human];
                ha.Carry = CarryKind.Corruption;
                ha.CarryAmount = 1;
                ha.SpawnProtect = 0;
                InputFrame f = SimInput.EmptyInput();
                f.Interact = true;
                f.AimX = k.X;
                f.AimZ = k.Z;
                SimDriverBehaviour.ScriptedHumanFrame = f;
                _driver.HumanAutopilot = false;
                int need = Durations.T30(w.Config, w.Config.Depth.Corruption.FeedTicks);
                Step(need / 2);
                yield return Shot("06-feeding", k.X, k.Z, w.Corrupt.FeedTick >= w.Tick - 1, "windup=" + ha.DeliverTicks + "/" + need);
                Step(need);
                fed = Corruption.Pool(w) > 0;
                SimDriverBehaviour.ScriptedHumanFrame = null;
                _driver.HumanAutopilot = true;
                Log("feed completed=" + fed + " pool=" + F(Corruption.Pool(w), "0.00"));
            }
            else yield return Shot("06-feeding", w.Kaiju.X, w.Kaiju.Z, false, "down=" + down + " human=" + human);

            // 07 a forced failed roll, 08 a forced successful roll (both at a real get-up)
            bool fail = ForceRoll(w, false, out double failChance);
            yield return Shot("07-roll-fail", w.Kaiju.X, w.Kaiju.Z, fail, "rolledChance=" + F(failChance) + " u=" + F(w.Corrupt.LastU));
            Step(2);
            bool ok = false;
            double okChance = double.NaN;
            for (int tries = 0; tries < 6 && !ok; tries++)
            {
                ok = ForceRoll(w, true, out okChance);
                if (!ok && !Corruption.Wild(w)) ForceRoll(w, false, out _);   // u too high for a success: spend one index on a fail
            }
            yield return Shot("08-roll-success", w.Kaiju.X, w.Kaiju.Z, ok && Corruption.Wild(w), "rolledChance=" + F(okChance) + " u=" + F(w.Corrupt.LastU));

            // 09 the wild Kaiju on the hill 3 s later (staged: carried to the hill centre)
            Kaiju kj = w.Kaiju;
            kj.X = kj.PrevX = w.Map.Hill.X;
            kj.Z = kj.PrevZ = w.Map.Hill.Z;
            Step((int)Math.Round(3 * hz));
            bool held = Corruption.WildHoldsHill(w) && w.Match.HillState == HillState.Contested;
            yield return Shot("09-wild-on-hill", kj.X, kj.Z, held, "hillState=" + w.Match.HillState + " holds=" + Corruption.WildHoldsHill(w));

            Log("done: " + _reached + "/" + _total + " REACHED; stills in " + _dir);
            yield return Quit(scale, 0);
        }

        IEnumerator Quit(double scale, int code)
        {
            var cam = FindAnyObjectByType<CriticalMass.Runtime.Camera.CameraFollowTarget>();
            if (cam != null) cam.ClearOverride();
            AutoplayArgs.ClockHeld = false;
            _driver.TimeScale = scale;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(code);
        }
    }
}
