using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// feat/hit-feel-1 capture flag, for the integrator: <c>-hitFeelShots</c> (own file; installs itself after the scene loads).
    /// For each of the six classes it resets the world with actor 0 as that class, stands one enemy dummy in front on open
    /// floor (held still and passive: it never swings back), presses attack once and renders 10 consecutive sim ticks (30 per
    /// sim second) from the press, HUD hidden, in two views, each as one 10-frame strip in the data folder:
    ///   hitfeel-&lt;class&gt;-side.png   side view (horizontal camera across the swing): the arm, the lunge, the flinch
    ///   hitfeel-&lt;class&gt;-game.png   the gameplay camera's own angle
    /// The frame with the damage tick has a white bar along its bottom. Each class logs one line:
    ///   [CM.HitFeel] &lt;class&gt;: clip C contact Cs, wind-up W ticks, damage tick +D (expected +W, sim offset X ms),
    ///   view contact offset Y ms, facing error E deg, lunge peak L u, reaction R (side S), hit H
    /// The match clock is held; the game quits when done. Optional: -hitFeelShotsOnly &lt;Class&gt;.
    /// </summary>
    public sealed class HitFeelCapture : MonoBehaviour
    {
        public const string Flag = "-hitFeelShots", OnlyFlag = "-hitFeelShotsOnly", Prefix = "hitfeel-";
        public const int Frames = 10, FrameW = 480, FrameH = 360;
        public const float GameDistance = 7f, SideDistance = 5.5f, SideHeight = 0.35f;
        float _sideSign = 1f, _sideLift;   // fix/hit-feel-5: which side the side camera stands on (world z sign) and its lift
        static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        SimDriverBehaviour _driver;
        readonly InputFrame _frame = SimInput.EmptyInput();
        int _dummy = -1;
        double _dummyX, _dummyZ;
        bool _holdDummy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0) return;
            var go = new GameObject("Hit Feel Capture");
            DontDestroyOnLoad(go);
            go.AddComponent<HitFeelCapture>();
        }

        static void Log(string s) => Debug.Log("[CM.HitFeel] " + s);

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        /// <summary>The dummy's distance in front: inside the weapon's reach with room for the lunge (pure; tests).</summary>
        public static double DummyAhead(double range) => Math.Max(0.9, Math.Min(1.2, range * 0.75));

        void Start() => StartCoroutine(Run());

        void OnDestroy()
        {
            SimDriverBehaviour.ScriptedHumanFrame = null;
            AutoplayArgs.ClockHeld = false;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver == null || _driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            string only = Arg(OnlyFlag);
            foreach (ClassId cls in Classes)
            {
                if (only != null && !string.Equals(only, cls.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                IEnumerator f = Fire(cls);
                while (f.MoveNext()) yield return f.Current;
            }
            Log("Kaiju: not staged here; its claw is timed by KaijuAnimator (Swipe contact frame on the claw's damage tick)");
            SimDriverBehaviour.ScriptedHumanFrame = null;
            AutoplayArgs.ClockHeld = false;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
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

        /// <summary>The dummy never swings or casts back (its blows made the attacker flash white in the weapon capture).</summary>
        void KeepDummyPassive(World w)
        {
            if (_dummy < 0 || _dummy >= w.ActorCount) return;
            Actor d = w.Actors[_dummy];
            d.AttackCd = Math.Max(d.AttackCd, 1.0);
            d.AbilityCd = Math.Max(d.AbilityCd, 1.0);
            d.AbilityActive = 0;
            d.SwingWindup = 0;
        }

        void Step(int n)
        {
            World w = _driver.World;
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            for (int t = 0; t < n; t++)
            {
                _driver.HumanAutopilot = false;
                KeepDummyPassive(w);
                _driver.StepTicks(1);
                if (_holdDummy && _dummy >= 0 && w.Actors[_dummy].Alive) Put(w, _dummy, _dummyX, _dummyZ);
            }
        }

        static bool Spot(World w, out double bx, out double bz)
        {
            MapDef m = w.Map;
            double mx = m.Width * 0.5, mz = m.Height * 0.5, best = -1;
            bx = w.X[0]; bz = w.Z[0];
            for (int cz = 3; cz < m.Height - 3; cz += 2)
                for (int cx = 3; cx < m.Width - 4; cx += 2)
                {
                    double x = cx + 0.5, z = cz + 0.5, hx = x - mx, hz = z - mz;
                    if (hx * hx + hz * hz < 81 || !Floor(w, x, z) || !Floor(w, x + 1, z) || !Floor(w, x + 2, z) || !Floor(w, x - 1, z)) continue;
                    double clear = double.MaxValue;
                    for (int i = 1; i < w.ActorCount; i++)
                    {
                        double ax = w.X[i] - x, az = w.Z[i] - z, d2 = ax * ax + az * az;
                        if (d2 < clear) clear = d2;
                    }
                    if (clear <= best) continue;
                    best = clear; bx = x; bz = z;
                }
            return best >= 0;
        }

        IEnumerator Fire(ClassId cls)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            CriticalMass.Runtime.Hud.FirstMinutes.StartBannerOff = true;
            CriticalMass.Runtime.Hud.Banner.ClearAll();
            _driver.TimeScale = 0;
            AutoplayArgs.ClockHeld = true;
            _holdDummy = false;
            _dummy = -1;
            _frame.CopyFrom(SimInput.EmptyInput());
            _frame.AimX = double.NaN;
            _frame.AimZ = double.NaN;
            SimDriverBehaviour.ScriptedHumanFrame = _frame;
            Step(3);
            string n = cls.ToString().ToLowerInvariant();
            if (!Spot(w, out double x, out double z)) Log(n + ": no open spot; staging at the spawn");
            Put(w, 0, x, z);
            w.Actors[0].Facing = 0;
            for (int i = 1; i < w.ActorCount; i++)
                if (w.Actors[i].Alive && w.Actors[i].Team != w.Actors[0].Team) { _dummy = i; break; }
            if (_dummy < 0) { Log(n + ": no enemy to use as the dummy"); yield break; }
            _dummyX = x + DummyAhead(LightWeaponView.Range(w.Config, cls));
            _dummyZ = z;
            Put(w, _dummy, _dummyX, _dummyZ);
            w.Actors[_dummy].Facing = Math.PI;   // facing the attacker: a frontal hit
            w.Actors[_dummy].SpawnProtect = 0;
            w.Actors[0].SpawnProtect = 0;
            w.Actors[0].AttackCd = 0;
            _holdDummy = true;
            _frame.AimX = _dummyX;
            _frame.AimZ = _dummyZ;
            Step(4);
            double fx0 = x + 0.5 * DummyAhead(LightWeaponView.Range(w.Config, cls));
            double g0 = GroundHeight.At(w.Map, fx0, z);
            Vector3 focus = SimSpace.ToWorld(fx0, g0 + 0.9, z);
            PickSide(w, fx0, z, g0);

            double hp0 = w.Actors[_dummy].Hp, hz = w.Config.SimHz;
            _frame.Attack = true;
            Step(1);
            _frame.Attack = false;
            int press = w.Actors[0].SwingTick > 0 ? w.Actors[0].SwingTick : w.Tick - 1;
            int windup = w.Actors[0].SwingWindup;
            int damageTick = -1;
            double facingErr = 0, lungePeak = 0, handReach = double.NaN, surface = double.NaN, tipAhead = double.NaN, stepAt = 0, leanAt = 0;
            ActorViews views = _driver.GetComponent<ActorViews>();
            var side = new Texture2D(FrameW * Frames, FrameH, TextureFormat.RGB24, false);
            var game = new Texture2D(FrameW * Frames, FrameH, TextureFormat.RGB24, false);
            for (int f = 0; f < Frames; f++)
            {
                if (damageTick < 0 && w.Actors[_dummy].Hp < hp0 - 1e-6) { damageTick = w.Tick; _holdDummy = false; }
                if (w.Tick == press + windup)
                {
                    double want = Math.Atan2(w.Z[_dummy] - w.Z[0], w.X[_dummy] - w.X[0]);
                    double d = HitFeel.Facing(w, 0, 0) - want;
                    facingErr = Math.Abs(Math.Atan2(Math.Sin(d), Math.Cos(d))) * 180 / Math.PI;
                }
                lungePeak = Math.Max(lungePeak, HitFeel.Lunge(w, 0, 0).magnitude);
                yield return null;
                yield return new WaitForEndOfFrame();
                bool contact = damageTick == w.Tick;
                RenderInto(side, f, focus, true, contact);
                RenderInto(game, f, focus, false, contact);
                if (contact)
                {
                    // feat/hit-feel-2: weapon-hand reach at the contact frame, along the line to the dummy, from the drawn body.
                    handReach = HandReach(views, w);
                    double ddx = w.X[_dummy] - w.X[0], ddz = w.Z[_dummy] - w.Z[0];
                    surface = Math.Sqrt(ddx * ddx + ddz * ddz) - w.Config.Class(w.Actors[_dummy].Cls).Radius;
                    // fix/hit-feel-5: the weapon TIP's distance ahead of the SIM position along the line to the dummy
                    tipAhead = TipAhead(views, w);
                    stepAt = HitFeel.Lunge(w, 0, 0).magnitude;
                    if (HitFeel.StrikeLean(w, 0, 0, out double ld, out _, out _)) leanAt = ld;
                }
                Step(1);
            }
            Write(side, Prefix + n + "-side.png");
            Write(game, Prefix + n + "-game.png");
            Destroy(side);
            Destroy(game);

            string clip = ArtMapping.ClassClips(cls).Attack;
            double c = ClipFacts.Contact(clip, 0.567f);
            double viewOffMs = (AttackTiming.ContactAge(c, windup / hz) - windup / hz) * 1000;
            int expect = press + windup;
            string simOff = damageTick < 0 ? "no hit landed in " + Frames + " ticks" : ((damageTick - expect) * 1000 / hz).ToString("0") + " ms";
            HitReaction r = HitReactions.Pick(hp0 - w.Actors[_dummy].Hp, !w.Actors[_dummy].Alive, false, HitSide.Front);
            Log(n + ": clip " + clip + " contact " + c.ToString("0.000") + "s, wind-up " + windup + " ticks, damage tick +" +
                (damageTick < 0 ? "?" : (damageTick - press).ToString()) + " (expected +" + windup + ", sim offset " + simOff +
                "), view contact offset " + viewOffMs.ToString("0") + " ms (+0..33 ms render interpolation), facing error " +
                facingErr.ToString("0") + " deg, lunge peak " + lungePeak.ToString("0.00") + " u, reaction " + r.Clip + " " + r.Layer +
                ", hit " + (hp0 - w.Actors[_dummy].Hp).ToString("0.#") +
                "; reach: weapon hand " + handReach.ToString("0.00") + " u at contact (incl. step-in), view reach table " +
                HitFeel.ViewReach(cls).ToString("0.00") + " u, sim range " + LightWeaponView.Range(w.Config, cls).ToString("0.00") +
                " u, target body surface " + surface.ToString("0.00") + " u; flinch " + HitFeel.FlinchLeanDeg(hp0 - w.Actors[_dummy].Hp).ToString("0") +
                " deg, shift " + HitFeel.FlinchShift(hp0 - w.Actors[_dummy].Hp).ToString("0.00") + " u, stagger " +
                HitFeel.StaggerStep(0.1, hp0 - w.Actors[_dummy].Hp).ToString("0.00") + " u");
            // fix/hit-feel-5: the numbers to check reach by. "hand" above is the weapon HAND's distance ahead of the DRAWN
            // root (step-in included) along the line to the dummy: neither the gap nor the hand's travel. "tip gap to
            // surface" is the target body surface minus the weapon TIP's distance ahead of the SIM position: 0 or less is
            // a touch (negative: the tip is that far into the body), positive is the gap left.
            ModelView mv = views != null ? views.ModelOf(0) : null;
            HitPose.TipLocal(mv, out _, out string tipSrc);
            int ci = (int)cls;
            double reach = HitFeel.ViewReach(cls), lever = HitFeel.MeasuredLever[ci];
            double cover = reach + HitFeel.MaxViewOffset + HitPose.LeanGain(HitPose.MaxStrikeLeanDeg, lever > 0 ? lever : 0.5);
            double range = LightWeaponView.Range(w.Config, cls);
            HitTier tier = r.Tier;
            Log(n + ": tip gap to surface " + (surface - tipAhead).ToString("0.00") + " u (tip " + tipAhead.ToString("0.00") +
                " u ahead of the sim position, surface " + surface.ToString("0.00") + " u; tip from " + tipSrc + "); contact tip reach " +
                reach.ToString("0.00") + (HitFeel.MeasuredReach[ci] > 0 ? " u measured" : " u ESTIMATE (not measured)") +
                ", lever " + lever.ToString("0.00") + " u, step-in " + stepAt.ToString("0.00") + " u, strike lean " + leanAt.ToString("0") +
                " deg; covers a body surface up to " + cover.ToString("0.00") + " u, sim range (to the surface) " + range.ToString("0.00") +
                " u" + (range > cover + 1e-3 ? ": SHORT by " + (range - cover).ToString("0.00") + " u (rules question: shorten the range)" : ": covered") +
                "; reaction recoil " + HitPose.RecoilDeg(tier).ToString("0") + " deg, head snap " + HitPose.HeadSnapDeg(tier).ToString("0") +
                " deg, squash " + (HitPose.SquashMax * 100).ToString("0") + "% for " + (HitPose.SquashSeconds * 1000).ToString("0") +
                " ms; side camera on the " + (_sideSign > 0 ? "-z" : "+z") + " side, lift " + _sideLift.ToString("0.00") + " u");
        }

        /// <summary>fix/hit-feel-5: the weapon tip's distance ahead of actor 0's SIM position along the line to the dummy (NaN: no tip).</summary>
        double TipAhead(ActorViews views, World w)
        {
            ActorAnimator anim = views != null ? views.AnimatorOf(0) : null;
            if (anim == null || !anim.HasTip) return double.NaN;
            Vector3 dir = SimSpace.ToWorld(w.X[_dummy] - w.X[0], 0, w.Z[_dummy] - w.Z[0]);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return double.NaN;
            Vector3 off = anim.TipWorld - SimSpace.ToWorld(w.X[0], 0, w.Z[0]);
            off.y = 0f;
            return Vector3.Dot(off, dir.normalized);
        }

        /// <summary>
        /// fix/hit-feel-5 (the empty dark side strips: the camera stood inside a wall or under higher ground on the -z
        /// side): the side with more open floor at or below the pair's ground over the camera's 6 u, and a lift over any
        /// higher ground left on that line.
        /// </summary>
        void PickSide(World w, double x, double z, double g0)
        {
            int bestClear = -1;
            for (int s = 0; s < 2; s++)
            {
                double sign = s == 0 ? 1 : -1;   // world -z camera = sim +z
                int clear = 0;
                double top = g0;
                for (int k = 1; k <= 6; k++)
                {
                    double cz = z + sign * k;
                    double g = GroundHeight.At(w.Map, x, cz);
                    if (Floor(w, x, cz) && g <= g0 + 0.3) clear++;
                    if (g > top) top = g;
                }
                if (clear > bestClear) { bestClear = clear; _sideSign = (float)sign; _sideLift = (float)Math.Max(0, top - g0); }
            }
        }

        /// <summary>Horizontal distance from actor 0's drawn root to its weapon hand along the line to the dummy (NaN: no rig).</summary>
        double HandReach(ActorViews views, World w)
        {
            ModelView m = views != null ? views.ModelOf(0) : null;
            if (m == null) return double.NaN;
            Transform hand = m.Bone(WeaponProps.HandBone) ?? m.Bone(WeaponProps.HandBoneFallback);
            if (hand == null) return double.NaN;
            Vector3 dir = SimSpace.ToWorld(w.X[_dummy] - w.X[0], 0, w.Z[_dummy] - w.Z[0]);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return double.NaN;
            Vector3 off = hand.position - m.Transform.position;
            off.y = 0f;
            return Vector3.Dot(off, dir.normalized);
        }

        void RenderInto(Texture2D strip, int f, Vector3 focus, bool sideView, bool contact)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            Transform t = cam.transform;
            Vector3 p = t.position;
            Quaternion r = t.rotation;
            RenderTexture old = cam.targetTexture, prev = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(FrameW, FrameH, 24);
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);   // capture only: hide every HUD canvas
            bool[] was = new bool[canvases.Length];
            for (int k = 0; k < canvases.Length; k++) { was[k] = canvases[k].enabled; canvases[k].enabled = false; }
            try
            {
                if (sideView)
                {
                    // Across the swing (the swing runs along world +x): a level camera on the -z side.
                    t.position = focus + new Vector3(0f, SideHeight + _sideLift, -SideDistance * _sideSign);
                    t.rotation = Quaternion.LookRotation(focus - t.position, Vector3.up);
                }
                else t.position = focus - r * Vector3.forward * GameDistance;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                strip.ReadPixels(new Rect(0, 0, FrameW, FrameH), f * FrameW, 0);
                if (contact)
                    for (int y = 0; y < 6; y++)
                        for (int x = 0; x < FrameW; x++) strip.SetPixel(f * FrameW + x, y, Color.white);
            }
            catch (Exception ex) { Log("frame " + f + " failed: " + ex.Message); }
            finally
            {
                for (int k = 0; k < canvases.Length; k++) if (canvases[k] != null) canvases[k].enabled = was[k];
                cam.targetTexture = old;
                RenderTexture.active = prev;
                t.position = p;
                t.rotation = r;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static void Write(Texture2D strip, string name)
        {
            strip.Apply();
            File.WriteAllBytes(Path.Combine(GamePaths.DataDir, name), strip.EncodeToPNG());
            Log("strip " + name);
        }
    }
}
