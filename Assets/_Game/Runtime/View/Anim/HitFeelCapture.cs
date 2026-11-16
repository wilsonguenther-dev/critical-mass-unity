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
        int _failed;
        public const float ChestHeight = 1.2f, CamGroundClear = 0.5f, FrameCentre = 0.6f;
        Vector3 _sideCamPos;
        Quaternion _sideCamRot = Quaternion.identity;   // fix/hit-feel-6: classes whose pair was not drawn where staged or whose tip gap was not measured
        const double MaxGap = 2.0, MaxDrawnOff = 0.5;
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
            Log(_failed > 0 ? "done with " + _failed + " FAILED class(es): see the FAILED lines" : "done: every class staged, framed and measured");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(_failed > 0 ? 1 : 0);
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
            ActorViews views0 = _driver.GetComponent<ActorViews>();
            // fix/hit-feel-6: one rendered frame so the views put the LIVE models on the staged pair, then check them
            // there (the second and later classes measured a pooled model parked about 46 u away and framed empty sky).
            // Up to 30 frames (sim clock held, no tick changes) for the views to swap in the class's model and draw it there.
            for (int k = 0; k < 30; k++)
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                if (DrawnAtPair(views0, w, n, out _, quiet: true)) break;
            }
            bool drawnOk = DrawnAtPair(views0, w, n, out Vector3 drawnMid);
            if (!drawnOk) _failed++;
            // fix/hit-feel-7: frame from the LIVE pair: its drawn midpoint at fighter chest height (the drawn ground can sit
            // above the sim's GroundHeight: the camera then stood under the island and saw its underside and the void).
            Vector3 feet = drawnOk ? drawnMid : SimSpace.ToWorld(fx0, g0, z);
            focus = new Vector3(feet.x, feet.y + ChestHeight, feet.z);
            if (!PlaceSideCamera(w, n, focus, feet, fx0, z)) _failed++;

            double hp0 = w.Actors[_dummy].Hp, hz = w.Config.SimHz;
            _frame.Attack = true;
            Step(1);
            _frame.Attack = false;
            int press = w.Actors[0].SwingTick > 0 ? w.Actors[0].SwingTick : w.Tick - 1;
            int windup = w.Actors[0].SwingWindup;
            int damageTick = -1;
            double facingErr = 0, lungePeak = 0, handReach = double.NaN, surface = double.NaN, tipAhead = double.NaN, stepAt = 0, leanAt = 0;
            // b11-c: the tip is also read on the frame AFTER the damage tick, with each frame's render alpha, and the gap is
            // judged on the frame where the strike is drawn (the one with the tip further forward): the wind-up craft makes
            // the whole strike inside the last tick, so a frame drawn at alpha 0 still shows the drawn-back extreme.
            double tipContact = double.NaN, tipNext = double.NaN, alphaContact = double.NaN, alphaNext = double.NaN;
            bool readNext = false;
            string liveSrc = "no model";
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
                if (readNext)
                {
                    readNext = false;
                    alphaNext = _driver.Alpha;
                    tipNext = TipAhead(views, w, cls, out _);
                }
                if (contact)
                {
                    // feat/hit-feel-2: weapon-hand reach at the contact frame, along the line to the dummy, from the drawn body.
                    handReach = HandReach(views, w);
                    double ddx = w.X[_dummy] - w.X[0], ddz = w.Z[_dummy] - w.Z[0];
                    surface = Math.Sqrt(ddx * ddx + ddz * ddz) - w.Config.Class(w.Actors[_dummy].Cls).Radius;
                    // fix/hit-feel-5: the weapon TIP's distance ahead of the SIM position along the line to the dummy
                    tipContact = TipAhead(views, w, cls, out liveSrc);
                    alphaContact = _driver.Alpha;
                    readNext = true;
                    stepAt = HitFeel.Lunge(w, 0, 0).magnitude;
                    if (HitFeel.StrikeLean(w, 0, 0, out double ld, out _, out _)) leanAt = ld;
                }
                Step(1);
            }
            // b11-c: the strike frame: the contact frame or the one after it, whichever draws the tip further forward.
            tipAhead = double.IsNaN(tipNext) || (!double.IsNaN(tipContact) && tipContact >= tipNext) ? tipContact : tipNext;
            Log(n + ": strike frame: tip " + tipContact.ToString("0.00") + " u at the damage-tick frame (render alpha " +
                alphaContact.ToString("0.00") + "), " + tipNext.ToString("0.00") + " u on the frame after (alpha " + alphaNext.ToString("0.00") +
                "); judged on " + (tipAhead.Equals(tipContact) ? "the damage-tick frame" : "the frame after"));
            if (damageTick >= 0 && (double.IsNaN(tipAhead) || Math.Abs(surface - tipAhead) > MaxGap))
            {
                _failed++;
                Log(n + ": FAILED: tip gap " + (surface - tipAhead).ToString("0.00") + " u is not a measurement (over " + MaxGap +
                    " u or no tip; tip from " + liveSrc + ")");
            }
            SwingTable(views, w, n, cls);
            Write(side, Prefix + n + "-side.png");
            Write(game, Prefix + n + "-game.png");
            Destroy(side);
            Destroy(game);

            // b11-c: the clip the animator really played (the Healer's lantern swing plays Attack, not Cast)
            ActorAnimator played = views != null ? views.AnimatorOf(0) : null;
            string clip = played != null && played.MeasuredClip != null ? played.MeasuredClip : ArtMapping.ClassClips(cls).Attack;
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
            HitPose.StrikeEnds(mv, cls, out _, out _, out string tipSrc);
            int ci = (int)cls;
            double reach = HitFeel.ViewReach(cls), lever = HitFeel.MeasuredLever[ci];
            double cover = reach + HitFeel.MaxViewOffset + HitPose.LeanGain(HitPose.MaxStrikeLeanDeg, lever > 0 ? lever : 0.5);
            double range = LightWeaponView.Range(w.Config, cls);
            HitTier tier = r.Tier;
            Log(n + ": tip gap verdict " + ((surface - tipAhead) <= 0.15 ? "OK (within 0.15 u or overlapping)" : "OVER 0.15 u") + ", striking end " +
                (views != null && views.AnimatorOf(0) != null ? views.AnimatorOf(0).StrikingEnd + ", view contact " +
                views.AnimatorOf(0).ViewContactNow.ToString("0.000") + " s (fact " +
                views.AnimatorOf(0).FactContact.ToString("0.000") + " s)" : "?"));
            Log(n + ": tip gap to surface " + (surface - tipAhead).ToString("0.00") + " u (tip " + tipAhead.ToString("0.00") +
                " u ahead of the sim position, surface " + surface.ToString("0.00") + " u; tip from " + tipSrc + "); contact tip reach " +
                reach.ToString("0.00") + (HitFeel.MeasuredReach[ci] > 0 ? " u measured" : " u ESTIMATE (not measured)") +
                ", lever " + lever.ToString("0.00") + " u, step-in " + stepAt.ToString("0.00") + " u, strike lean " + leanAt.ToString("0") +
                " deg; covers a body surface up to " + cover.ToString("0.00") + " u, sim range (to the surface) " + range.ToString("0.00") +
                " u" + (range > cover + 1e-3 ? ": SHORT by " + (range - cover).ToString("0.00") + " u (weapon-reach table row to re-measure)" : ": covered") +
                " (weapon-reach rule " + (Combat.WeaponReachOn(w.Config) ? "on" : "off") + ")" +
                "; reaction recoil " + HitPose.RecoilDeg(tier).ToString("0") + " deg, head snap " + HitPose.HeadSnapDeg(tier).ToString("0") +
                " deg, squash " + (HitPose.SquashMax * 100).ToString("0") + "% for " + (HitPose.SquashSeconds * 1000).ToString("0") +
                " ms; side camera on the " + (_sideSign > 0 ? "-z" : "+z") + " side, lift " + _sideLift.ToString("0.00") + " u");
        }

        /// <summary>
        /// fix/hit-feel-7: the side camera for this class, from the live pair: SideDistance off the pair on the open side
        /// (PickSide), at chest height plus SideHeight, and at least CamGroundClear above the drawn ground at its own spot
        /// (the sim ground there plus the pair's drawn-minus-sim offset). Logs "side cam pos/fwd/mid" and fails the class
        /// loudly unless the pair's midpoint projects inside the central FrameCentre of the frame.
        /// </summary>
        bool PlaceSideCamera(World w, string n, Vector3 focus, Vector3 feet, double simX, double simZ)
        {
            Vector3 pos = focus + new Vector3(0f, SideHeight, -SideDistance * _sideSign);
            double offset = feet.y - GroundHeight.At(w.Map, simX, simZ);
            float groundAtCam = (float)(GroundHeight.At(w.Map, pos.x, -pos.z) + offset);
            if (pos.y < groundAtCam + CamGroundClear) pos.y = groundAtCam + CamGroundClear;
            _sideCamPos = pos;
            _sideCamRot = Quaternion.LookRotation(focus - pos, Vector3.up);
            Vector3 vp = new Vector3(float.NaN, float.NaN, float.NaN);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                Transform t = cam.transform;
                Vector3 p0 = t.position;
                Quaternion r0 = t.rotation;
                float near = cam.nearClipPlane, far = cam.farClipPlane, fov = cam.fieldOfView;
                bool ortho = cam.orthographic;
                float aspect = cam.aspect;
                try
                {
                    t.position = _sideCamPos;
                    t.rotation = _sideCamRot;
                    cam.orthographic = false;
                    cam.fieldOfView = 40f;
                    cam.nearClipPlane = 0.1f;
                    cam.farClipPlane = 200f;
                    cam.aspect = (float)FrameW / FrameH;
                    vp = cam.WorldToViewportPoint(focus);
                }
                finally
                {
                    t.position = p0;
                    t.rotation = r0;
                    cam.orthographic = ortho;
                    cam.fieldOfView = fov;
                    cam.nearClipPlane = near;
                    cam.farClipPlane = far;
                    cam.aspect = aspect;
                }
            }
            Vector3 fwd = _sideCamRot * Vector3.forward;
            float m = (1f - FrameCentre) * 0.5f;
            bool inside = vp.z > 0f && vp.x >= m && vp.x <= 1f - m && vp.y >= m && vp.y <= 1f - m;
            Log(n + ": side cam pos " + pos.ToString("F2") + " fwd " + fwd.ToString("F2") + " mid " + focus.ToString("F2") +
                " (drawn ground at the pair " + feet.y.ToString("0.00") + ", sim ground offset " + offset.ToString("0.00") +
                ", ground at the camera " + groundAtCam.ToString("0.00") + "), pair at viewport " + vp.x.ToString("0.00") + ", " +
                vp.y.ToString("0.00") + (inside ? ": inside the central 60%" : ""));
            if (!inside) Log(n + ": FAILED: the side camera does not have the pair in the central 60% of its frame");
            return inside;
        }

        /// <summary>
        /// fix/hit-feel-7: the striking weapon end's forward distance (along the line to the dummy, from the attacker's
        /// drawn root) through the whole attack clip, 25 samples, with the fact-table contact (C) and the view contact the
        /// damage tick shows (V) marked. Runs after the strips (it poses the live model by hand).
        /// </summary>
        void SwingTable(ActorViews views, World w, string n, ClassId cls)
        {
            ModelView mv = views != null ? views.ModelOf(0) : null;
            ActorAnimator anim = views != null ? views.AnimatorOf(0) : null;
            if (mv == null || anim == null || anim.TipHand == null) { Log(n + ": swing table: no measured tip"); return; }
            // b11-c: the clip the animator measured and plays (Healer: the lantern swing's Attack), on the drawn pose
            string clip = anim.MeasuredClip != null && anim.TipModel == mv ? anim.MeasuredClip : AttackTiming.SwingClip(mv, cls, w.Config);
            float len = clip != null ? mv.ClipLength(clip) : 0f;
            if (len <= 0f) { Log(n + ": swing table: no attack clip"); return; }
            double fact = ClipFacts.Contact(clip, len * 0.5f), view = anim.ViewContact(clip, fact);
            Vector3 dir = SimSpace.ToWorld(w.X[_dummy] - w.X[0], 0, w.Z[_dummy] - w.Z[0]);
            dir.y = 0f;
            dir.Normalize();
            var sb = new System.Text.StringBuilder();
            sb.Append(n).Append(": swing table (drawn pose: idle legs, clip on the upper layer; clip ").Append(clip).Append(", striking end ").Append(anim.StrikingEnd)
              .Append(", C contact ").Append(fact.ToString("0.000")).Append(" s, V view contact ").Append(view.ToString("0.000")).Append(" s):");
            double bestF = double.NegativeInfinity, bestT = 0;
            const int samples = 24;
            for (int k = 0; k <= samples; k++)
            {
                double t = len * k / samples;
                if (!AttackTiming.PoseDrawn(mv, clip, t)) break;
                double f = Vector3.Dot(anim.TipHand.TransformPoint(anim.TipLocalNow) - mv.Transform.position, dir);
                if (f > bestF) { bestF = f; bestT = t; }
                bool atC = Math.Abs(t - fact) <= len / samples * 0.5, atV = Math.Abs(t - view) <= len / samples * 0.5;
                sb.Append(' ').Append(t.ToString("0.00")).Append(atC ? "C" : "").Append(atV ? "V" : "").Append('=').Append(f.ToString("0.00"));
            }
            sb.Append("; furthest forward ").Append(bestF.ToString("0.00")).Append(" u at ").Append(bestT.ToString("0.000")).Append(" s");
            Log(sb.ToString());
        }

        /// <summary>
        /// fix/hit-feel-6: the weapon tip's distance ahead of actor 0's SIM position along the line to the dummy, read from
        /// the LIVE model (ActorViews.ModelOf(0) now, its own hand and tip: never the animator's cache, which kept the first
        /// class's model after the views swapped actor 0 to the next class). NaN: no model or hand.
        /// </summary>
        double TipAhead(ActorViews views, World w, ClassId cls, out string source)
        {
            source = "no model";
            ModelView mv = views != null ? views.ModelOf(0) : null;
            if (mv == null) return double.NaN;
            // b11-c: the STRIKING end the animator measured on this live model (hit-feel-7 read end "a" here even when the
            // swing strikes with the other end), else the class's strike ends (the Healer's lantern hand).
            ActorAnimator anim = views.AnimatorOf(0);
            Vector3 local;
            Transform hand;
            if (anim != null && anim.TipModel == mv && anim.TipHand != null)
            {
                local = anim.TipLocalNow;
                hand = anim.TipHand;
                source = "striking end " + anim.StrikingEnd + " on " + hand.name;
            }
            else local = HitPose.StrikeEnds(mv, cls, out hand, out _, out source);
            if (hand == null) return double.NaN;
            Vector3 dir = SimSpace.ToWorld(w.X[_dummy] - w.X[0], 0, w.Z[_dummy] - w.Z[0]);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return double.NaN;
            Vector3 off = hand.TransformPoint(local) - SimSpace.ToWorld(w.X[0], 0, w.Z[0]);
            off.y = 0f;
            return Vector3.Dot(off, dir.normalized);
        }

        /// <summary>
        /// fix/hit-feel-6: the attacker's and the dummy's LIVE models are drawn on their staged sim spots (within
        /// MaxDrawnOff) and are the right class; logs them either way. drawnMid: the drawn pair's midpoint (camera focus).
        /// </summary>
        bool DrawnAtPair(ActorViews views, World w, string n, out Vector3 drawnMid, bool quiet = false)
        {
            drawnMid = Vector3.zero;
            ModelView a = views != null ? views.ModelOf(0) : null, d = views != null ? views.ModelOf(_dummy) : null;
            if (a == null || d == null)
            {
                if (!quiet) Log(n + ": FAILED: no live model for " + (a == null ? "the attacker" : "the dummy") + " after staging");
                return false;
            }
            Vector3 sa = SimSpace.ToWorld(w.X[0], 0, w.Z[0]), sd = SimSpace.ToWorld(w.X[_dummy], 0, w.Z[_dummy]);
            Vector3 pa = a.Transform.position, pd = d.Transform.position;
            float oa = new Vector2(pa.x - sa.x, pa.z - sa.z).magnitude, od = new Vector2(pd.x - sd.x, pd.z - sd.z).magnitude;
            string want = ArtMapping.ClassModel(w.Actors[0].Cls), key = a.Entry != null ? a.Entry.Key : "?";
            bool active = a.Transform.gameObject.activeInHierarchy && d.Transform.gameObject.activeInHierarchy;
            if (!quiet) Log(n + ": staged pair drawn: attacker model " + key + " " + oa.ToString("0.00") + " u from its sim spot, dummy " +
                od.ToString("0.00") + " u, both active " + active);
            drawnMid = (pa + pd) * 0.5f;
            if (oa > MaxDrawnOff || od > MaxDrawnOff || !active || (want != null && key != want))
            {
                if (!quiet) Log(n + ": FAILED: the pair is not drawn where it was staged (or the wrong model is live): the strips and the measure would read elsewhere");
                return false;
            }
            return true;
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
            // fix/hit-feel-6: the side shot sets its own lens (perspective, 40 degrees, near 0.1, far 200): the gameplay
            // camera's clip planes and zoom follow its own height and the class, and could cull a pair 5.5 u away.
            float near = cam.nearClipPlane, far = cam.farClipPlane, fov = cam.fieldOfView;
            bool ortho = cam.orthographic;
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
                    t.position = _sideCamPos;   // fix/hit-feel-7: placed and checked per class by PlaceSideCamera
                    t.rotation = _sideCamRot;
                    cam.orthographic = false;
                    cam.fieldOfView = 40f;
                    cam.nearClipPlane = 0.1f;
                    cam.farClipPlane = 200f;
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
                cam.orthographic = ortho;
                cam.fieldOfView = fov;
                cam.nearClipPlane = near;
                cam.farClipPlane = far;
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
