using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using CameraView = CriticalMass.Runtime.Camera.CameraView;
using CameraShake = CriticalMass.Runtime.Camera.CameraShake;
using Object = UnityEngine.Object;
using T = CriticalMass.Runtime.PlayTests.PlayerSeat.PlayerSeatFeedbackTargets;

namespace CriticalMass.Runtime.PlayTests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, tier F (player-agency audit 6.4 F1-F19, the lead's F20-F34): real devices press, and the test reads
    /// what the player is shown. A cue that exists today is read in its real state (damage-number slots, camera shake offset,
    /// audio sources, particle systems, screen text); a cue the rebuild adds is read from the PlayerFeedback ledger, and its
    /// Source must be an active, enabled object. Core tests run in BOTH views (TopDown = a fresh install, ThirdPerson = how the
    /// owner plays); every test asserts the idle autopilot never drove the player.
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class PlayerSeatFeedbackTests : PlayerSeatFixture
    {
        static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };
        static readonly CameraView[] Views = { CameraView.TopDown, CameraView.ThirdPerson };

        bool _guardSaved;
        bool _guardIdle;

        public override void Setup()
        {
            base.Setup();
            _guardIdle = HumanControlGuard.IdleAutopilotEnabled;
            _guardSaved = true;
            // the wall-clock idle guard (45 s of real time) would take the seat on a slow runner: the SIM's autopilot rule is the one under test
            HumanControlGuard.IdleAutopilotEnabled = false;
        }

        public override void TearDown()
        {
            if (_guardSaved) HumanControlGuard.IdleAutopilotEnabled = _guardIdle;
            _guardSaved = false;
            base.TearDown();
        }

        // ------------------------------------------------------------------ one landed hit, watched

        sealed class Watch
        {
            public bool Hit;
            public double Dealt, Knock;
            public int HitTick = -1, LedgerAtHit;
            public double ModelMoved;
            public double KickPeak;
            /// <summary>fix round 5: the rendered frame the hit was presented on (Time.frameCount), the kick's own size in
            /// px at 1080p at the fixed point's distance and the rendered frame (0 = the hit frame) it peaked on.</summary>
            public int HitFrame = int.MaxValue / 2, KickPxFrame = -1;
            public double KickPxPeak;
            /// <summary>fix round 5: the first rendered frame (0 = the hit frame) the victim drew with the hit flash on.</summary>
            public int FlashFrame = -1;
            public int SoundsBefore, SoundsPeak;
            public bool Seen;
            public double CamPxPeak, SimMoved100, ModelMoved100, ModelMovedFull, SimMovedFull;
            public int CamPxFrame = -1, CamPx6Frame = -1;
            public string Dump = "";
            public double HitX, HitZ;
            public int SeenFrame = -1;
            public string Note = "";
        }

        /// <summary>
        /// Loads cls in view, puts a passive Farmer at 0.8 x the range in front (screen right top-down; camera forward third
        /// person), taps attack, and after the Hit watches 6 frames (3 ticks), calling cue each frame until it says true.
        /// </summary>
        IEnumerator HitAndWatch(ClassId cls, CameraView view, Func<Watch, bool> cue, Watch r, bool reduceMotion = false, bool gamepad = false, bool loaded = false)
        {
            if (!loaded) yield return Load(cls, view, reduceMotion);
            FrontDir(view, out double dx, out double dz);
            double dist = T.StandShareOfRange * W.Config.Class(cls).AttackRange;
            int e = Isolate(ClassId.Farmer, dist, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            bool pinned = true;
            Action before = () =>
            {
                if (pinned) Pin(e, px, pz);
                else { W.Actors[e].AttackCd = Math.Max(W.Actors[e].AttackCd, 1); W.Actors[e].AbilityCd = Math.Max(W.Actors[e].AbilityCd, 1); }
                if (W.Demolitions != null && W.Demolitions.TntReadyTick != null) W.Demolitions.TntReadyTick[0] = int.MaxValue / 2;   // the melee swing, not the TNT
            };
            for (int i = 0; i < 3; i++) yield return TickFrame(before);
            int head = W.EventHead;
            r.SoundsBefore = PlayingSources();
            AssertAtControls("the press");
            PressAttack(view, gamepad);
            int budget = W.Dur.AttackWindup[(int)cls] + T.ContactSlackTicks + 1;
            Transform model = S.ActorModel(e);
            Vector3 m0 = model != null ? model.position : Vector3.zero;
            UnityEngine.Camera cam0 = UnityEngine.Camera.main;
            Vector3 fixedPoint = SimSpace.ToWorld(W.X[0], S.Ground(W.X[0], W.Z[0]), W.Z[0]);
            Vector3 px0 = Vector3.zero;
            for (int t = 0; t < budget && !r.Hit; t++)
            {
                int pre = PlayerFeedback.Head;
                if (cam0 != null) px0 = cam0.WorldToScreenPoint(fixedPoint);
                yield return TickFrame(before);
                if (t == 0) ReleaseAttack(gamepad);
                List<SimEvent> hits = Events(head, SimEventType.Hit).Where(x => x.A == e && x.B == 0).ToList();
                if (hits.Count > 0)
                {
                    r.Hit = true;
                    r.Dealt = hits[0].Value;
                    r.HitTick = W.Tick;
                    r.HitFrame = Time.frameCount;   // the hit is decoded in this frame's sim step: its LateUpdate presents it
                    r.LedgerAtHit = pre;   // only cues reported from the hit's own tick on
                    r.HitX = px; r.HitZ = pz;   // where the pinned victim stood when the blow landed
                    List<SimEvent> kn = Events(head, SimEventType.Knock).Where(x => x.A == e).ToList();
                    r.Knock = kn.Count > 0 ? kn[0].Impulse : 0;
                    pinned = false;
                }
            }
            if (!r.Hit) { r.Note = "no Hit within wind-up + " + T.ContactSlackTicks + " ticks (" + string.Join(" ", Events(head, SimEventType.AttackContact).Select(x => "contact v" + x.Value)) + ")"; yield break; }
            for (int f = 0; f < T.CueFrames; f++)
            {
                r.KickPeak = Math.Max(r.KickPeak, CameraShake.Applied.magnitude);
                r.SoundsPeak = Math.Max(r.SoundsPeak, PlayingSources());
                if (model != null) r.ModelMoved = Math.Max(r.ModelMoved, Vector3.Distance(model.position, m0));
                // fix round 5 (item 3): a coroutine resumes AFTER CameraShakeReset (Update, order -1000) took the last frame's
                // shake off the camera, so the old sample never saw the kick at all (its 6 px at "frame 3" was the follow
                // drift). Sample the frame that was RENDERED: the camera as its LateUpdate left it, i.e. plus the kick it
                // applied (CameraShake.KickApplied, kept until the next LateUpdate), and count rendered frames since the hit
                // frame (0 = the frame that presented the hit), not loop passes.
                int rendered = Time.frameCount - 1 - r.HitFrame;
                // fix round 5 (item 1): the victim's drawn material carries the unshaded flash (CMToon _HitFlash share) on
                // the rendered frame that presented the hit (materials stay as that frame's LateUpdate set them)
                if (rendered >= 0 && r.FlashFrame < 0 && model != null && FlashShare(model) >= CriticalMass.Runtime.View.Anim.HitFeelTuning.FlashShareSafe) r.FlashFrame = rendered;
                if (cam0 != null && rendered >= 0)
                {
                    Vector3 pn = cam0.WorldToScreenPoint(fixedPoint - CameraShake.KickApplied);
                    double moved = new Vector2(pn.x - px0.x, pn.y - px0.y).magnitude * 1080.0 / Math.Max(1, Screen.height);
                    if (moved > r.CamPxPeak) { r.CamPxPeak = moved; r.CamPxFrame = rendered; }
                    if (moved >= 6 && r.CamPx6Frame < 0) r.CamPx6Frame = rendered;   // the promise: 6 px within 2 rendered frames
                    double kickPx = CameraShake.KickApplied.magnitude * 1080.0 / Math.Max(1e-3, 2 * Math.Tan(cam0.fieldOfView * Math.PI / 360) * Math.Max(0.5, Vector3.Distance(cam0.transform.position, fixedPoint)));
                    if (kickPx > r.KickPxPeak) { r.KickPxPeak = kickPx; r.KickPxFrame = rendered; }
                }
                if (!r.Seen && cue(r)) { r.Seen = true; r.SeenFrame = f; }
                // one rendered step = 16.7 ms of real time (60 fps): cues on unscaled time (camera kick, fades) see real time pass
                yield return new WaitForSecondsRealtime(1f / 60f);
                if (f < T.CueTicks) yield return TickFrame(before);
            }
            r.KickPeak = Math.Max(r.KickPeak, CameraShake.Applied.magnitude);
            if (!r.Seen && cue(r)) { r.Seen = true; r.SeenFrame = T.CueFrames; }
            double sx0 = r.HitX, sz0 = r.HitZ;
            r.SimMoved100 = Math.Sqrt((W.X[e] - sx0) * (W.X[e] - sx0) + (W.Z[e] - sz0) * (W.Z[e] - sz0));
            r.ModelMoved100 = model != null ? Vector3.Distance(model.position, m0) : 0;
            // the whole slide: 0.5 s more of sim, the victim still never swings
            for (int t = 0; t < S.Ticks(0.5); t++)
            {
                yield return TickFrame(before);
                if (model != null) r.ModelMovedFull = Math.Max(r.ModelMovedFull, Vector3.Distance(model.position, m0));
                r.SimMovedFull = Math.Max(r.SimMovedFull, Math.Sqrt((W.X[e] - sx0) * (W.X[e] - sx0) + (W.Z[e] - sz0) * (W.Z[e] - sz0)));
            }
            r.Dump = LedgerDump(r.LedgerAtHit);
            AssertAtControls("the end of the watch");
        }

        static string Tag(ClassId c, CameraView v) => c + " " + v;

        /// <summary>fix round 5: the largest CMToon `_HitFlash` share on the drawn renderers under a fighter's model.</summary>
        static float FlashShare(Transform model)
        {
            float best = 0f;
            foreach (Renderer rr in model.GetComponentsInChildren<Renderer>())
            {
                if (rr == null || !rr.enabled || !rr.gameObject.activeInHierarchy) continue;
                foreach (Material mm in rr.sharedMaterials)
                    if (mm != null && mm.HasProperty(CriticalMass.Runtime.View.Effects.CombatFx.HitFlashId)) best = Mathf.Max(best, mm.GetVector(CriticalMass.Runtime.View.Effects.CombatFx.HitFlashId).w);
            }
            return best;
        }

        // ------------------------------------------------------------------ F1-F4 (per class, both views)

        [UnityTest]
        public IEnumerator F01_Mouse_ClickOnEnemy_DamageNumberWithin100ms([ValueSource(nameof(Classes))] ClassId x, [ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            yield return HitAndWatch(x, v, q => Ledger(FeedbackChannel.DamageNumber, Enemy, out FeedbackEntry n, q.LedgerAtHit) && Math.Abs(n.Value - q.Dealt) <= 1.0, r);
            Check("F01 DamageNumber", Tag(x, v), r.Hit ? (r.Seen ? "DamageNumber " + Math.Round(r.Dealt) + " reported at frame " + r.SeenFrame : "hit " + Math.Round(r.Dealt) + ", no DamageNumber entry with that value on the victim in 6 frames [" + r.Dump + "]") : r.Note, "within 6 frames", r.Hit && r.Seen);
        }

        [UnityTest]
        public IEnumerator F02_Mouse_ClickOnEnemy_HealthBarDrops([ValueSource(nameof(Classes))] ClassId x, [ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            FeedbackEntry got = default;
            yield return HitAndWatch(x, v, q => false, r);
            bool seen2 = LedgerLast(FeedbackChannel.EnemyHealthBar, Enemy, r.LedgerAtHit, out got);
            r.Seen = seen2;
            double share = r.Hit ? W.Actors[Enemy].Hp / W.Actors[Enemy].MaxHp : 0;
            Check("F02 EnemyHealthBar", Tag(x, v), r.Hit ? (r.Seen ? "bar fill " + got.Value.ToString("0.00") + " (hp share " + share.ToString("0.00") + ")" : "no EnemyHealthBar ledger entry after the hit [" + r.Dump + "]") : r.Note, "bar = hp share +-0.1 within 6 frames", r.Hit && r.Seen && Math.Abs(got.Value - share) <= 0.1);
        }

        [UnityTest]
        public IEnumerator F03_Mouse_ClickOnEnemy_VictimFlashAndKnockVisible([ValueSource(nameof(Classes))] ClassId x, [ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            yield return HitAndWatch(x, v, q => Ledger(FeedbackChannel.HitFlash, Enemy, out _, q.LedgerAtHit), r);
            // PLAN: within 100 ms the drawn knock is >= 0.8 x the sim's (measured at the same instant: the sim body is still
            // sliding at 100 ms), and the drawn slide reaches >= 0.8 x the sim knock (Knock Impulse) once it is over
            bool knockOk = r.Knock <= 0 || (r.ModelMoved100 >= T.KnockVisibleShare * r.SimMoved100 && r.ModelMovedFull >= T.KnockVisibleShare * r.Knock);
            bool knockLedger = Ledger(FeedbackChannel.KnockVisual, Enemy, out _, r.LedgerAtHit);
            Check("F03 VictimFlashAndKnock", Tag(x, v), r.Hit ? "flash " + (r.Seen ? "at frame " + r.SeenFrame : "none") + ", drawn flash share on rendered frame " + r.FlashFrame + " (0 = the hit frame), at 100 ms model " + r.ModelMoved100.ToString("0.00") + " u vs sim body " + r.SimMoved100.ToString("0.00") + " u; whole slide model " + r.ModelMovedFull.ToString("0.00") + " u (sim " + r.SimMovedFull.ToString("0.00") + ") vs sim knock " + r.Knock.ToString("0.00") + " u, KnockVisual " + knockLedger + (r.Seen && knockLedger ? "" : " [" + r.Dump + "]") : r.Note, "flash and knock >= 0.8 x sim within 6 frames", r.Hit && r.Seen && r.FlashFrame == 0 && knockOk && knockLedger);
        }

        [UnityTest]
        public IEnumerator F04_Mouse_ClickOnEnemy_ImpactSound([ValueSource(nameof(Classes))] ClassId x, [ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            yield return HitAndWatch(x, v, q => Ledger(FeedbackChannel.ImpactSound, -1, out _, q.LedgerAtHit), r);
            Check("F04 ImpactSound", Tag(x, v), r.Hit ? "ImpactSound ledger " + r.Seen + "; audio sources playing " + r.SoundsBefore + " -> peak " + r.SoundsPeak : r.Note, "an impact sound within 6 frames", r.Hit && r.Seen);
        }

        // ------------------------------------------------------------------ F25 / F26 / F30 / F33 (both views)

        [UnityTest]
        public IEnumerator F25_HitMarker_AtReticle([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            yield return HitAndWatch(ClassId.Warrior, v, q => Ledger(FeedbackChannel.HitMarker, -1, out _, q.LedgerAtHit), r);
            Check("F25 HitMarker", Tag(ClassId.Warrior, v), r.Hit ? (r.Seen ? "marker at frame " + r.SeenFrame : "no HitMarker") : r.Note, "a hit marker within 6 frames", r.Hit && r.Seen);
        }

        [UnityTest]
        public IEnumerator F26_Whiff_DiffersFromHit([ValueSource(nameof(Views))] CameraView v)
        {
            yield return Load(ClassId.Warrior, v);
            // a swing at empty air first: nobody in reach
            FrontDir(v, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 9, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            for (int i = 0; i < 3; i++) yield return TickFrame(() => Pin(e, px, pz));
            int cursor = PlayerFeedback.Head;
            AssertAtControls("the whiff");
            PressAttack(v);
            for (int t = 0; t < W.Dur.AttackWindup[(int)ClassId.Warrior] + T.CueTicks + 2; t++)
            {
                yield return TickFrame(() => Pin(e, px, pz));
                if (t == 0) ReleaseAttack();
            }
            bool whiffCue = Ledger(FeedbackChannel.WhiffCue, 0, out _, cursor) || Ledger(FeedbackChannel.WhiffCue, -1, out _, cursor);
            bool whiffImpact = Ledger(FeedbackChannel.ImpactSound, -1, out _, cursor);
            while (!Combat.AttackReady(W, 0)) yield return TickFrame(() => Pin(e, px, pz));
            var r = new Watch();
            yield return HitAndWatch(ClassId.Warrior, v, q => Ledger(FeedbackChannel.ImpactSound, -1, out _, q.LedgerAtHit), r, loaded: true);
            bool hitWhiffCue = Ledger(FeedbackChannel.WhiffCue, 0, out _, r.LedgerAtHit);
            Check("F26 WhiffDiffersFromHit", Tag(ClassId.Warrior, v), "whiff: WhiffCue " + whiffCue + ", impact sound " + whiffImpact + "; hit: impact sound " + r.Seen + ", WhiffCue " + hitWhiffCue, "a whiff cue only on the whiff, an impact only on the hit", whiffCue && !whiffImpact && r.Hit && r.Seen && !hitWhiffCue);
        }

        [UnityTest]
        public IEnumerator F30_SwingPose_Within2Frames([ValueSource(nameof(Views))] CameraView v)
        {
            yield return Load(ClassId.Warrior, v);
            FrontDir(v, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 0.8 * W.Config.Class(ClassId.Warrior).AttackRange, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            for (int i = 0; i < 3; i++) yield return TickFrame(() => Pin(e, px, pz));
            int head = W.EventHead, cursor = PlayerFeedback.Head;
            AssertAtControls("the press");
            PressAttack(v);
            yield return TickFrame(() => Pin(e, px, pz));
            ReleaseAttack();
            bool started = Events(head, SimEventType.AttackStart).Any(q => q.A == 0);
            float best = 0;
            for (int f = 0; f < T.CueFrames; f++)
            {
                if (Ledger(FeedbackChannel.SwingCommit, 0, out FeedbackEntry got, cursor)) best = Math.Max(best, got.Value);
                yield return new WaitForSecondsRealtime(1f / 60f);
                yield return TickFrame(() => Pin(e, px, pz));
            }
            if (Ledger(FeedbackChannel.SwingCommit, 0, out FeedbackEntry last, cursor)) best = Math.Max(best, last.Value);
            AssertAtControls("the end");
            Check("F30 SwingPose", Tag(ClassId.Warrior, v), "sim accepted " + started + ", SwingCommit pose weight " + best.ToString("0.00") + " [" + LedgerDump(cursor) + "]", ">= 0.8 within 2 frames", started && best >= T.SwingPoseWeight);
        }

        [UnityTest]
        public IEnumerator F33_CameraKick_OnMyHit([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            yield return HitAndWatch(ClassId.Warrior, v, q => false, r);
            double kick = LedgerMax(FeedbackChannel.CameraKick, r.LedgerAtHit);
            Check("F33 CameraKick_MyHit", Tag(ClassId.Warrior, v), r.Hit ? "CameraKick reported " + kick.ToString("0.000") + " u (shake offset " + r.KickPeak.ToString("0.000") + ")" : r.Note, ">= " + T.CameraKickMine + " u", r.Hit && kick >= T.CameraKickMine);
        }

        [UnityTest]
        public IEnumerator F33_CameraKick_ReduceMotion_IsZero([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            yield return HitAndWatch(ClassId.Warrior, v, q => false, r, reduceMotion: true);
            bool ledger = LedgerMax(FeedbackChannel.CameraKick, r.LedgerAtHit) > 0;
            Check("F33 CameraKick_ReduceMotion", Tag(ClassId.Warrior, v), r.Hit ? "peak shake offset " + r.KickPeak.ToString("0.000") + " u, CameraKick reported " + ledger + ", a fixed point moved " + r.CamPxPeak.ToString("0.0") + " px (framing, informational)" : r.Note, "0 (and no kick reported)", r.Hit && r.KickPeak <= T.CameraKickReduced + 1e-4 && !ledger);
        }

        // ------------------------------------------------------------------ hits on me: F20 / F21 / F33 (both views)

        sealed class Taken
        {
            public bool Hit;
            public int Attacker = -1, LedgerAtHit;
            public double Bearing, KickPeak, ScreenBearing;
            public string Note = "";
        }

        /// <summary>An enemy Warrior bot 1.2 u in front is let swing; the player stands still (no input: under 15 s, no autopilot).</summary>
        IEnumerator TakeHit(CameraView v, Taken r)
        {
            yield return Load(ClassId.Farmer, v);
            FrontDir(v, out double dx, out double dz);
            int e = Isolate(ClassId.Warrior, 1.2, dx, dz);
            int head = W.EventHead;
            for (int t = 0; t < S.Ticks(6) && !r.Hit; t++)
            {
                // run 1 (45038552514): the bot never swung at a standing player in 6 s; after 1 s the hit is dealt through the
                // sim's own damage path with the bot as the attacker (the cues read the Hit event, not the bot's choice)
                if (t == S.Ticks(1)) Damage.ApplyHit(W, 0, 12, e, W.X[e], W.Z[e], DamageCause.Melee, 0, 0, 0, 0);
                int pre = PlayerFeedback.Head;
                yield return TickFrame();
                SimEvent[] hits = Events(head, SimEventType.Hit).Where(q => q.A == 0).ToArray();
                if (hits.Length > 0)
                {
                    r.Hit = true;
                    r.Attacker = hits[0].B;
                    r.LedgerAtHit = pre;
                    r.Bearing = BearingDeg(W.X[0], W.Z[0], W.X[e], W.Z[e]);
                    UnityEngine.Camera cam = UnityEngine.Camera.main;
                    if (cam != null)
                    {
                        // screen bearing, clockwise from screen up (DamageDirection rotates its arrow by -bearing about z)
                        Vector2 a = ScreenOf(W.X[0], W.Z[0]), b = ScreenOf(W.X[e], W.Z[e]);
                        r.ScreenBearing = Math.Atan2(b.x - a.x, b.y - a.y) * 180 / Math.PI;
                    }
                }
            }
            if (!r.Hit) { r.Note = "the bot never hit the player in 6 s"; yield break; }
            for (int f = 0; f < T.CueFrames; f++)
            {
                r.KickPeak = Math.Max(r.KickPeak, CameraShake.Applied.magnitude);
                if (f < T.CueTicks) yield return TickFrame();
                else yield return null;
            }
            r.KickPeak = Math.Max(r.KickPeak, LedgerMax(FeedbackChannel.CameraKick, r.LedgerAtHit));
            AssertAtControls("after the hit on me");
        }

        [UnityTest]
        public IEnumerator F20_HitOnMe_DirectionIndicator([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Taken();
            yield return TakeHit(v, r);
            FeedbackEntry dd = default;
            bool shown = r.Hit && (Ledger(FeedbackChannel.DamageDirection, r.Attacker, out dd, r.LedgerAtHit) || Ledger(FeedbackChannel.DamageDirection, -1, out dd, r.LedgerAtHit));
            double gap = shown ? AngleGap(dd.Value, r.ScreenBearing) : 999;
            Check("F20 DamageDirection", "Farmer " + v, r.Hit ? (shown ? "indicator " + dd.Value.ToString("0") + " deg, attacker on screen at " + r.ScreenBearing.ToString("0") + " deg clockwise from up (sim bearing " + r.Bearing.ToString("0") + ")" : "no DamageDirection") : r.Note, "within " + T.DirectionMaxDegrees + " deg, 6 frames", shown && gap <= T.DirectionMaxDegrees);
        }

        [UnityTest]
        public IEnumerator F21_HitOnMe_HurtVignetteAndSound([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Taken();
            yield return TakeHit(v, r);
            bool vig = r.Hit && Ledger(FeedbackChannel.OwnHurtVignette, 0, out _, r.LedgerAtHit);
            bool snd = r.Hit && Ledger(FeedbackChannel.OwnHurtSound, 0, out _, r.LedgerAtHit);
            Check("F21 OwnHurt", "Farmer " + v, r.Hit ? "vignette " + vig + ", hurt sound " + snd : r.Note, "both within 6 frames", vig && snd);
        }

        [UnityTest]
        public IEnumerator F33_CameraKick_OnHitOnMe([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Taken();
            yield return TakeHit(v, r);
            Check("F33 CameraKick_OnMe", "Farmer " + v, r.Hit ? "CameraKick reported " + r.KickPeak.ToString("0.000") + " u" : r.Note, ">= " + T.CameraKickOnMe + " u", r.Hit && r.KickPeak >= T.CameraKickOnMe);
        }

        // ------------------------------------------------------------------ F22 / F23 (both views)

        [UnityTest]
        public IEnumerator F22_LowHealth_PulseAndHeartbeat([ValueSource(nameof(Views))] CameraView v)
        {
            yield return Load(ClassId.Warrior, v);
            Isolate(ClassId.Farmer, 30, 1, 0);
            Actor a = W.Actors[0];
            a.Hp = a.MaxHp * (T.LowHealthShare - 0.05);
            int cursor = PlayerFeedback.Head;
            for (int t = 0; t < S.Ticks(2); t++) { a.Hp = Math.Min(a.Hp, a.MaxHp * (T.LowHealthShare - 0.05)); yield return TickFrame(); }
            int pulses = LedgerCount(FeedbackChannel.LowHealthCue, 0, cursor);
            AssertAtControls("the end");
            Check("F22 LowHealth", "Warrior " + v, "LowHealthCue entries in 2 s: " + pulses, "1 to " + (2 * T.LowHealthMaxHz + 1) + " (a pulse at most 2 Hz)", pulses >= 1 && pulses <= 2 * T.LowHealthMaxHz + 1);
        }

        [UnityTest]
        public IEnumerator F23_HealthBars_OverUnitsInView([ValueSource(nameof(Views))] CameraView v)
        {
            yield return Load(ClassId.Warrior, v);
            FrontDir(v, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 3, dx, dz);
            Put(1, ClassId.Miner, W.X[0] + 1.5 * dx - 1.0 * dz, W.Z[0] + 1.5 * dz + 1.0 * dx);
            double ex = W.X[e], ez = W.Z[e], ax = W.X[1], az = W.Z[1];
            int cursor = PlayerFeedback.Head;
            for (int t = 0; t < 10; t++) yield return TickFrame(() => { Pin(e, ex, ez); Pin(1, ax, az); });
            var bars = CriticalMass.Runtime.Hud.Player.PlayerHud.Current?.Bars;
            int visible = bars != null ? bars.VisibleCount : -1;
            bool enemyBar = visible >= 2, allyBar = visible >= 2;
            AssertAtControls("the end");
            Check("F23 HealthBarsInView", "Warrior " + v, "HealthBars.VisibleCount " + visible + " with one enemy and one ally in view, both at full health, no hit", "both shown, always on", enemyBar && allyBar);
        }

        // ------------------------------------------------------------------ F31: no mouse aim, third person and top-down

        /// <summary>Screen-intent components of the sim direction (dx, dz) in this view (top-down: screen; third person: camera).</summary>
        void Intent(CameraView v, double dx, double dz, out double right, out double up)
        {
            if (v == CameraView.TopDown) { SanityWorld.ToScreenIntent(dx, dz, out right, out up); return; }
            CameraForward(out double fx, out double fz);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 rt = cam != null ? cam.transform.right : Vector3.right;
            rt.y = 0;
            SimSpace.ToSim(rt, out double rx, out double rz);
            SimSpace.ToSim(Vector3.zero, out double ox, out double oz);
            rx -= ox; rz -= oz;
            double l = Math.Sqrt(rx * rx + rz * rz);
            if (l > 1e-9) { rx /= l; rz /= l; }
            up = dx * fx + dz * fz;
            right = dx * rx + dz * rz;
        }

        sealed class Chase
        {
            public int Swings, Landed;
            public bool Died;
        }

        /// <summary>A Normal Farmer bot fights the player, who closes in with keys or the left stick only and presses attack when in reach.</summary>
        IEnumerator NoMouseFight(CameraView v, bool keys, double offDeg, Chase c, bool lagCursor = false)
        {
            yield return Load(ClassId.Warrior, v);
            FrontDir(v, out double fdx, out double fdz);
            int e = Isolate(ClassId.Farmer, 3, fdx, fdz);
            double reach = W.Config.Class(ClassId.Warrior).AttackRange;
            int head = W.EventHead;
            var hx = new Queue<Vector2>();
            bool held = false;
            Key[] prev = new Key[0];
            for (int t = 0; t < S.Ticks(T.AssistMaxSeconds) && c.Swings < T.AssistSwings; t++)
            {
                if (!W.Actors[0].Alive) { c.Died = true; break; }
                if (!W.Actors[e].Alive) { W.Actors[e].Hp = W.Actors[e].MaxHp; Put(e, ClassId.Farmer, W.X[0] + 3 * fdx, W.Z[0] + 3 * fdz); }
                W.Actors[e].Hp = Math.Max(W.Actors[e].Hp, 1);   // never dies: the measure is the land rate
                W.Actors[0].Hp = W.Actors[0].MaxHp;              // nor the player (runs 1-2 ended after 1-7 swings on the player's death)
                double dx = W.X[e] - W.X[0], dz = W.Z[e] - W.Z[0], d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 1e-6) { dx /= d; dz /= d; }
                double ang = offDeg * Math.PI / 180;
                double rdx = dx * Math.Cos(ang) - dz * Math.Sin(ang), rdz = dx * Math.Sin(ang) + dz * Math.Cos(ang);
                bool close = d <= 0.8 * reach;
                Intent(v, rdx, rdz, out double right, out double up);
                if (close) { right *= 0.2; up *= 0.2; }
                if (keys)
                {
                    var want = new List<Key>();
                    if (up > 0.38) want.Add(Key.W); else if (up < -0.38) want.Add(Key.S);
                    if (right > 0.38) want.Add(Key.D); else if (right < -0.38) want.Add(Key.A);
                    if (close) want.Clear();
                    foreach (Key k in prev) if (!want.Contains(k)) Release(Kb[k]);
                    foreach (Key k in want) if (!prev.Contains(k)) Press(Kb[k]);
                    prev = want.ToArray();
                }
                else if (offDeg != 0)
                {
                    Intent(v, dx, dz, out double mr, out double mu);
                    if (close) { mr *= 0.2; mu *= 0.2; }
                    Set(Pad.leftStick, new Vector2((float)mr, (float)mu));
                    Intent(v, rdx, rdz, out double ar, out double au);
                    Set(Pad.rightStick, new Vector2((float)ar, (float)au));
                }
                else Set(Pad.leftStick, new Vector2((float)right, (float)up));
                hx.Enqueue(new Vector2((float)W.X[e], (float)W.Z[e]));
                if (hx.Count > T.CursorLagTicks + 1) hx.Dequeue();
                if (lagCursor) { Vector2 old = hx.Peek(); Set(Ms.position, ScreenOf(old.x, old.y)); }
                if (held) { if (keys || lagCursor) Release(Ms.leftButton); else Release(Pad.rightTrigger); held = false; }
                else if (d <= reach + 0.2 && Combat.AttackReady(W, 0))
                {
                    if (keys || lagCursor) Press(Ms.leftButton); else Press(Pad.rightTrigger);
                    held = true;
                }
                yield return TickFrame();
                if (MechRules.Autopiloted(W, 0)) AssertAtControls("tick " + t);
            }
            foreach (Key k in prev) Release(Kb[k]);
            c.Swings = Events(head, SimEventType.AttackStart).Count(q => q.A == 0 && q.B == Combat.KindAttack);
            c.Landed = Events(head, SimEventType.AttackContact).Count(q => q.A == 0 && q.Value > 0);
            AssertAtControls("the end");
        }

        [UnityTest]
        public IEnumerator F31_StickOnly_LandsOnBot([ValueSource(nameof(Views))] CameraView v)
        {
            var c = new Chase();
            yield return NoMouseFight(v, false, 0, c);
            double share = c.Swings > 0 ? (double)c.Landed / c.Swings : 0;
            Check("F31 StickOnly", "Warrior " + v + " vs a Normal Farmer bot", c.Landed + " of " + c.Swings + " swings landed (" + share.ToString("0.00") + ")" + (c.Died ? ", died" : ""), ">= " + T.AssistLandMin, c.Swings >= 4 && share >= T.AssistLandMin);
        }

        [UnityTest]
        public IEnumerator F31_KeysOnly_LandsOnBot([ValueSource(nameof(Views))] CameraView v)
        {
            var c = new Chase();
            yield return NoMouseFight(v, true, 0, c);
            double share = c.Swings > 0 ? (double)c.Landed / c.Swings : 0;
            Check("F31 KeysOnly", "Warrior " + v + " vs a Normal Farmer bot (mouse never moved)", c.Landed + " of " + c.Swings + " swings landed (" + share.ToString("0.00") + ")" + (c.Died ? ", died" : ""), ">= " + T.AssistLandMin, c.Swings >= 4 && share >= T.AssistLandMin);
        }

        // ------------------------------------------------------------------ top-down only (a fresh install): F5-F19, F24, F27-F29, F32, F34

        [UnityTest]
        public IEnumerator F05_Gamepad_RightTrigger_NoStick_HitsEnemyInFront([ValueSource(nameof(Classes))] ClassId x)
        {
            yield return Load(x, CameraView.TopDown);
            // walk at the enemy with the left stick (screen right) for a few ticks so the body faces it, then RT with the right stick at rest
            Set(Pad.leftStick, new Vector2(1, 0));
            for (int i = 0; i < 6; i++) yield return TickFrame();
            Set(Pad.leftStick, Vector2.zero);
            var r = new Watch();
            yield return HitAndWatch(x, CameraView.TopDown, q => true, r, gamepad: true, loaded: true);
            Check("F05 Gamepad_RT_NoStick", x.ToString(), r.Hit ? "hit " + Math.Round(r.Dealt) : r.Note, "a Hit within wind-up + 3", r.Hit);
        }

        [UnityTest]
        public IEnumerator F06_Gamepad_StrafingEnemy_AssistLands()
        {
            var c = new Chase();
            yield return NoMouseFight(CameraView.TopDown, false, T.StickOffDegrees, c);
            double share = c.Swings > 0 ? (double)c.Landed / c.Swings : 0;
            Check("F06 Gamepad_StickOff20", "Warrior vs a Normal Farmer bot, stick 20 deg off", c.Landed + " of " + c.Swings + " (" + share.ToString("0.00") + ")", ">= " + T.AssistLandMin, c.Swings >= 4 && share >= T.AssistLandMin);
        }

        [UnityTest]
        public IEnumerator F07_Mouse_StrafingEnemy_AssistLands()
        {
            var c = new Chase();
            yield return NoMouseFight(CameraView.TopDown, true, 0, c, lagCursor: true);
            double share = c.Swings > 0 ? (double)c.Landed / c.Swings : 0;
            Check("F07 Mouse_CursorLag0.2s", "Warrior vs a Normal Farmer bot, cursor 6 ticks behind", c.Landed + " of " + c.Swings + " (" + share.ToString("0.00") + ")", ">= " + T.AssistLandMin, c.Swings >= 4 && share >= T.AssistLandMin);
        }

        [UnityTest]
        public IEnumerator F08_ReachPreview_LitOnlyWhenHittable()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            double reach = W.Config.Class(ClassId.Warrior).AttackRange;
            int e = Isolate(ClassId.Farmer, 3 * reach, dx, dz);
            double fx = W.X[e], fz = W.Z[e];
            Set(Ms.position, ScreenOf(fx, fz));
            int far = PlayerFeedback.Head;
            for (int t = 0; t < 6; t++) yield return TickFrame(() => Pin(e, fx, fz));
            bool litFar = Ledger(FeedbackChannel.HittableCue, e, out FeedbackEntry lf, far) && lf.Value > 0;
            double nx = W.X[0] + 0.7 * reach * dx, nz = W.Z[0] + 0.7 * reach * dz;
            Set(Ms.position, ScreenOf(nx, nz));
            int near = PlayerFeedback.Head;
            for (int t = 0; t < 6; t++) yield return TickFrame(() => Pin(e, nx, nz));
            bool litNear = Ledger(FeedbackChannel.HittableCue, e, out FeedbackEntry ln, near) && ln.Value > 0;
            string aimSays = "AimAssist.HittableTarget " + CriticalMass.Runtime.Input.AimAssist.HittableTarget + " (enemy " + e + "), ledger [" + LedgerDump(far) + "]";
            AssertAtControls("the end");
            Check("F08 HittableCue", "Warrior, enemy at 3x then 0.7x reach under the cursor", "lit far " + litFar + ", lit near " + litNear + "; " + aimSays, "off far, on near", !litFar && litNear);
        }

        [UnityTest]
        public IEnumerator F09_Kill_ConfirmLineAndSting()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 0.8 * W.Config.Class(ClassId.Warrior).AttackRange, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            W.Actors[e].Hp = 1;
            int head = W.EventHead, cursor = PlayerFeedback.Head;
            AssertAtControls("the press");
            PressAttack(CameraView.TopDown);
            bool died = false;
            int frames = 0;
            for (int t = 0; t < W.Dur.AttackWindup[0] + 12 && frames < T.KillConfirmFrames; t++)
            {
                yield return TickFrame(() => { if (W.Actors[e].Alive) Pin(e, px, pz); });
                if (t == 0) ReleaseAttack();
                if (!died) died = Events(head, SimEventType.Death).Any(q => q.A == e && q.B == 0);
                else frames++;
            }
            bool confirm = Ledger(FeedbackChannel.KillConfirm, e, out _, cursor) || Ledger(FeedbackChannel.KillConfirm, -1, out _, cursor);
            bool sting = Ledger(FeedbackChannel.KillSound, -1, out _, cursor);
            AssertAtControls("the end");
            Check("F09 KillConfirm", "Warrior kills a Farmer", "death " + died + ", confirm " + confirm + ", sting " + sting, "both within 6 frames", died && confirm && sting);
        }

        [UnityTest]
        public IEnumerator F10_Kill_FeedLineNeverDropped()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 0.8 * W.Config.Class(ClassId.Warrior).AttackRange, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            // eight kills by others in the second before mine: crimson bots knock out azure bots
            int killer = e + 1;
            Put(killer, ClassId.Warrior, W.X[0] - 12, W.Z[0]);
            for (int k = 0; k < T.FeedFlood; k++)
            {
                int victim = 1 + k;
                Put(victim, ClassId.Miner, W.X[0] - 12 + 0.6, W.Z[0] + 0.3 * k);
                Damage.ApplyHit(W, victim, 9999, killer, W.X[killer], W.Z[killer], DamageCause.Melee, 0, 0, 0, 0);
                yield return TickFrame(() => Pin(e, px, pz));
            }
            W.Actors[e].Hp = 1;
            int head = W.EventHead;
            PressAttack(CameraView.TopDown);
            bool died = false;
            bool you = false;
            for (int t = 0; t < W.Dur.AttackWindup[0] + T.ScreenFrames && !you; t++)
            {
                yield return TickFrame(() => { if (W.Actors[e].Alive) Pin(e, px, pz); });
                if (t == 0) ReleaseAttack();
                if (!died) died = Events(head, SimEventType.Death).Any(q => q.A == e && q.B == 0);
                if (died) you = ScreenTexts().Any(s => s.Contains("YOU"));
            }
            AssertAtControls("the end");
            Check("F10 FeedNeverDrops", "8 kills by others then mine", "my kill " + died + ", a YOU line on screen " + you, "my line shows", died && you);
        }

        IEnumerator KillAndRead(Func<bool> check, Action<bool, bool> done)
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 0.8 * W.Config.Class(ClassId.Warrior).AttackRange, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            W.Actors[e].Hp = 1;
            int head = W.EventHead;
            PressAttack(CameraView.TopDown);
            bool died = false, ok = false;
            for (int t = 0; t < W.Dur.AttackWindup[0] + T.ScreenFrames && !ok; t++)
            {
                yield return TickFrame(() => { if (W.Actors[e].Alive) Pin(e, px, pz); });
                if (t == 0) ReleaseAttack();
                if (!died) died = Events(head, SimEventType.Death).Any(q => q.A == e && q.B == 0);
                if (died) ok = check();
            }
            done(died, ok);
        }

        [UnityTest]
        public IEnumerator F11_PlayerCard_ShowsLiveKills()
        {
            int cursor = 0;
            bool died = false, ok = false;
            yield return KillAndRead(() => Ledger(FeedbackChannel.PersonalTally, 0, out FeedbackEntry p, cursor) && Math.Abs(p.Value - W.Actors[0].Kills) < 0.5, (d, o) => { died = d; ok = o; });
            AssertAtControls("the end");
            Check("F11 PersonalTally", "after one kill", "kill " + died + ", tally reported = Actor.Kills (" + W.Actors[0].Kills + ") " + ok, "the card counts it", died && ok);
        }

        [UnityTest]
        public IEnumerator F32_Kill_ConfirmForEveryKill()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            double dist = 0.8 * W.Config.Class(ClassId.Warrior).AttackRange;
            int e = Isolate(ClassId.Farmer, dist, dx, dz);
            int kills = 0, confirms = 0;
            for (int round = 0; round < 3; round++)
            {
                Put(e, ClassId.Farmer, W.X[0] + dist * dx, W.Z[0] + dist * dz);
                double px = W.X[e], pz = W.Z[e];
                W.Actors[e].Hp = 1;
                while (!Combat.AttackReady(W, 0)) yield return TickFrame(() => Pin(e, px, pz));
                int head = W.EventHead, cursor = PlayerFeedback.Head;
                PressAttack(CameraView.TopDown);
                bool died = false;
                int after = 0;
                for (int t = 0; t < W.Dur.AttackWindup[0] + 12 && after < T.KillConfirmFrames; t++)
                {
                    yield return TickFrame(() => { if (W.Actors[e].Alive) Pin(e, px, pz); });
                    if (t == 0) ReleaseAttack();
                    if (!died) died = Events(head, SimEventType.Death).Any(q => q.A == e && q.B == 0);
                    else after++;
                }
                if (died) kills++;
                if (died && (Ledger(FeedbackChannel.KillConfirm, e, out _, cursor) || Ledger(FeedbackChannel.KillConfirm, -1, out _, cursor))) confirms++;
            }
            AssertAtControls("the end");
            Check("F32 KillConfirmEveryKill", "three kills in a row", confirms + " confirms for " + kills + " kills", "one per kill, none dropped", kills == 3 && confirms == 3);
        }

        [UnityTest]
        public IEnumerator F12_Scoreboard_ShowsDamageAndHealing()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            Press(Kb.tabKey);
            bool dmg = false, heal = false;
            for (int f = 0; f < T.ScreenFrames && !(dmg && heal); f++)
            {
                yield return null;
                List<string> texts = ScreenTexts();
                dmg = texts.Any(s => s.Contains("DMG") || s.Contains("DAMAGE"));
                heal = texts.Any(s => s.Contains("HEAL"));
            }
            Release(Kb.tabKey);
            AssertAtControls("the end");
            Check("F12 ScoreboardDmgHeal", "TAB held", "DMG column " + dmg + ", HEAL column " + heal, "both", dmg && heal);
        }

        [UnityTest]
        public IEnumerator F13_BlockedHit_ShowsReason()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 0.8 * W.Config.Class(ClassId.Warrior).AttackRange, dx, dz);
            // the target stands on ITS OWN pad (Combat.cs:44-50 drops spawn protection beyond the pad radius), me beside it
            var pad = W.Map.Pads[(int)W.Actors[e].Team];
            Put(e, ClassId.Farmer, pad.X, pad.Z);
            Put(0, W.Actors[0].Cls, pad.X - 0.8 * W.Config.Class(ClassId.Warrior).AttackRange * dx, pad.Z - 0.8 * W.Config.Class(ClassId.Warrior).AttackRange * dz);
            W.Actors[0].SpawnProtect = 0;
            for (int f = 0; f < 3; f++) yield return TickFrame();
            double px = W.X[e], pz = W.Z[e];
            int head = W.EventHead, cursor = PlayerFeedback.Head;
            Action keep = () => { Pin(e, px, pz); W.Actors[e].SpawnProtect = 2; };
            keep();
            PressAttack(CameraView.TopDown);
            for (int t = 0; t < W.Dur.AttackWindup[0] + T.CueTicks + 3; t++)
            {
                yield return TickFrame(keep);
                if (t == 0) ReleaseAttack();
            }
            bool blocked = Events(head, SimEventType.HitBlocked).Any(q => q.A == e && q.B == 0);
            bool tag = Ledger(FeedbackChannel.BlockedTag, e, out FeedbackEntry b, cursor);
            AssertAtControls("the end");
            Check("F13 BlockedTag", "hit a spawn-protected Farmer", "HitBlocked event " + blocked + ", BlockedTag " + (tag ? "reason " + b.Reason : "none") + ", hits " + Events(head, SimEventType.Hit).Count(q => q.A == e) + ", contacts " + Events(head, SimEventType.AttackContact).Count(q => q.A == 0) + " [" + LedgerDump(cursor) + "]", "IMMUNE (reason " + PlayerReasons.BlockedSpawnProtect + ") within 100 ms", tag && b.Reason == PlayerReasons.BlockedSpawnProtect);
        }

        [UnityTest]
        public IEnumerator F14_Death_RecapNamesKiller()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            int e = Isolate(ClassId.Farmer, 1.2, 1, 0);
            Damage.ApplyHit(W, 0, 9999, e, W.X[e], W.Z[e], DamageCause.Melee, 0, 0, 0, 0);
            bool named = false;
            for (int f = 0; f < T.ScreenFrames * 2 && !named; f++)
            {
                yield return TickFrame();
                named = ScreenTexts().Any(s => s.Contains("FARMER"));
            }
            Check("F14 DeathRecap", "killed by a Farmer bot", "FARMER on screen " + named, "the recap names the killer", named);
        }

        [UnityTest]
        public IEnumerator F15_Objective_PointerToHill()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            Isolate(ClassId.Farmer, 30, 1, 0);
            var pad = W.Map.Pads[0];
            Put(0, ClassId.Warrior, pad.X, pad.Z);
            bool shown = false;
            var re = new Regex(@"\d+\s*M\b");
            for (int f = 0; f < T.ScreenFrames && !shown; f++)
            {
                yield return TickFrame();
                shown = ScreenTexts().Any(s => re.IsMatch(s));
            }
            AssertAtControls("the end");
            Check("F15 ObjectivePointer", "at the home pad, hill off screen", "a distance on screen " + shown, "a pointer with a distance", shown);
        }

        [UnityTest]
        public IEnumerator F16_Feed_ShowsMyAllegiance()
        {
            yield return Load(ClassId.Farmer, CameraView.TopDown);
            Isolate(ClassId.Farmer, 30, 1, 0);
            Put(0, ClassId.Farmer, W.Map.Shrine.X, W.Map.Shrine.Z + 2.3);
            W.Actors[0].Carry = CarryKind.Biomass;
            W.Actors[0].CarryAmount = 3;
            int head = W.EventHead, cursor = PlayerFeedback.Head;
            Press(Kb.eKey);
            for (int t = 0; t < 16; t++) yield return TickFrame();
            Release(Kb.eKey);
            for (int f = 0; f < T.CueFrames; f++) yield return TickFrame();
            bool fed = Events(head, SimEventType.DeliverBiomass).Any(q => q.A == 0);
            bool pop = Ledger(FeedbackChannel.ObjectivePop, 0, out FeedbackEntry p, cursor) || Ledger(FeedbackChannel.WorldActionPop, 0, out p, cursor);
            AssertAtControls("the end");
            Check("F16 FeedPop", "Farmer feeds 3 with E", "delivered " + fed + ", pop " + (pop ? p.Value.ToString("0.0") : "none"), "+" + T.DeliveryAllegiance + " where it happens", fed && pop && Math.Abs(p.Value - T.DeliveryAllegiance) < 1);
        }

        [UnityTest]
        public IEnumerator F17_Ability_CastCueForEveryClass([ValueSource(nameof(Classes))] ClassId x)
        {
            yield return Load(x, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 3.5, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            Put(1, ClassId.Miner, W.X[0] - dz, W.Z[0] + dx);   // an ally for the Healer's pulse
            W.Actors[1].Hp = W.Actors[1].MaxHp * 0.5;
            W.Actors[0].AbilityCd = 0;
            Set(Ms.position, ScreenOf(px, pz));
            for (int i = 0; i < 2; i++) yield return TickFrame(() => Pin(e, px, pz));
            int sounds = PlayingSources(), fx = PlayingParticles(), head = W.EventHead;
            Press(Ms.rightButton);
            bool cue = false;
            for (int f = 0; f < T.CueFrames && !cue; f++)
            {
                if (f < T.CueTicks) yield return TickFrame(() => Pin(e, px, pz)); else yield return null;
                if (f == 0) Release(Ms.rightButton);
                cue = PlayingSources() > sounds || PlayingParticles() > fx;
            }
            string ev = string.Join(" ", Events(head, SimEventType.Ability).Where(q => q.A == 0).Select(q => "Ability v" + q.Value));
            AssertAtControls("the end");
            Check("F17 AbilityCastCue", x.ToString(), "a new sound or particle system within 6 frames " + cue + " (" + (ev.Length > 0 ? ev : "no Ability event") + ")", "a cast cue", cue);
        }

        [UnityTest]
        public IEnumerator F18_Prompts_BindingsMatchVerbs([ValueSource(nameof(Classes))] ClassId x)
        {
            yield return Load(x, CameraView.TopDown);
            for (int f = 0; f < 20; f++) yield return null;
            List<string> texts = ScreenTexts();
            // the classes whose F press is not a dig (S33: the Warrior and the Farmer have no dig; the Miner's F is MINE)
            bool noDig = x == ClassId.Warrior || x == ClassId.Farmer || x == ClassId.Miner;
            string bad = noDig ? texts.FirstOrDefault(s => s.Contains("DIG")) : null;
            Check("F18 PromptsMatchVerbs", x.ToString(), bad != null ? "prompt \"" + bad.Replace("\n", " ") + "\" for a verb the class lacks" : "no stray dig prompt", "every prompt maps to a real verb", bad == null);
        }

        [UnityTest, Category("PlayerSeat.Graphics")]
        public IEnumerator F19_Screenshot_HitFrameShowsNumber()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("F19 needs a graphics run (loop/gates/remote playmode --graphics software)");
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            FrontDir(CameraView.TopDown, out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, 0.8 * W.Config.Class(ClassId.Warrior).AttackRange, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            yield return new WaitForEndOfFrame();
            Texture2D before = ScreenCapture.CaptureScreenshotAsTexture();
            PressAttack(CameraView.TopDown);
            for (int t = 0; t < W.Dur.AttackWindup[0] + 4; t++) { yield return TickFrame(() => Pin(e, px, pz)); if (t == 0) ReleaseAttack(); }
            yield return new WaitForEndOfFrame();
            Texture2D after = ScreenCapture.CaptureScreenshotAsTexture();
            Vector2 at = ScreenOf(px, pz);
            int changed = 0, n = 0;
            for (int y = -40; y <= 40; y += 2)
                for (int xx = -40; xx <= 40; xx += 2)
                {
                    int sx = Mathf.Clamp((int)at.x + xx, 0, before.width - 1), sy = Mathf.Clamp((int)at.y + 20 + y, 0, before.height - 1);
                    Color a = before.GetPixel(sx, sy), b = after.GetPixel(sx, sy);
                    n++;
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 0.3f) changed++;
                }
            Object.Destroy(before);
            Object.Destroy(after);
            Check("F19 HitFramePixels", "the frame 4 ticks after a hit, 80 px around the victim", changed + " of " + n + " samples changed", "the number's pixels show (>= 5%)", changed >= n / 20);
        }

        [UnityTest]
        public IEnumerator F24_H_NeverHidesHudByAccident()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            bool before = SettingsExtras.CleanHud;
            Press(Kb.hKey);
            yield return null;
            Release(Kb.hKey);
            for (int f = 0; f < 3; f++) yield return null;
            bool after = SettingsExtras.CleanHud;
            Check("F24 H_KeepsHud", "one H press in a match (H also dismisses tips)", "clean HUD " + before + " -> " + after, "the HUD stays (one job per button)", !after);
        }

        [UnityTest]
        public IEnumerator F27_WallHit_ShowsChipsBarAndSound()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            Isolate(ClassId.Farmer, 30, 1, 0);
            SanityWorld.FromScreenIntent(1, 0, out double dx, out double dz);
            int cx = (int)Math.Floor(W.X[0] + 1.2 * dx), cz = (int)Math.Floor(W.Z[0] + 1.2 * dz);
            MinerBarricadeConfig mb = W.Config.Abilities.MinerBarricade;
            Barricade b = W.Barricades.First(q => !q.Active);
            b.Active = true; b.Owner = W.ActorCount / 2 + 3; b.Team = TeamId.Crimson; b.Cx = cx; b.Cz = cz; b.Hp = mb.MaxHp; b.Ttl = mb.LifetimeSeconds;
            b.PrevCell = W.Map.Cells[cz * W.Map.Width + cx];
            MapGrid.SetCell(W, cx, cz, Cell.Barricade);
            yield return TickFrame();
            double hp0 = b.Hp;
            int cursor = PlayerFeedback.Head;
            Set(Ms.position, ScreenOf(cx + 0.5, cz + 0.5));
            Press(Ms.leftButton);
            for (int t = 0; t < W.Dur.AttackWindup[0] + T.CueFrames; t++) { yield return TickFrame(); if (t == 0) Release(Ms.leftButton); }
            bool burst = Ledger(FeedbackChannel.ImpactBurst, -1, out _, cursor) || Ledger(FeedbackChannel.EnemyHealthBar, -1, out _, cursor), sound = Ledger(FeedbackChannel.ImpactSound, -1, out _, cursor);
            bool chipped = b.Hp < hp0;
            AssertAtControls("the end");
            Check("F27 WallHitCue", "Warrior swings at an enemy barricade", "chipped " + chipped + " (" + hp0.ToString("0") + " -> " + b.Hp.ToString("0") + "), chips/bar cue " + burst + ", knock sound " + sound, "chips and a sound within 100 ms", chipped && burst && sound);
        }

        [UnityTest]
        public IEnumerator F28_WorldVerb_PopsWhereItHappens()
        {
            yield return Load(ClassId.Builder, CameraView.TopDown);
            Isolate(ClassId.Farmer, 30, 1, 0);
            W.Walls.Tool[0] = 0;   // DIG
            double dx = 1, dz = 0;
            double dd = 1.6;
            bool found = false;
            foreach (double dist in new[] { 1.6, 2.0, 2.4 })
            {
                for (int k = 0; k < 16 && !found; k++)
                {
                    double a = k * Math.PI / 8, cx = W.X[0] + dist * Math.Cos(a), cz = W.Z[0] + dist * Math.Sin(a);
                    int ix = (int)Math.Floor(cx), iz = (int)Math.Floor(cz);
                    if (ix < 1 || iz < 1 || ix >= W.Map.Width - 1 || iz >= W.Map.Height - 1) continue;
                    if (W.Map.Cells[iz * W.Map.Width + ix] != Cell.Floor || BuilderWalls.CellRefusal(W, ix, iz) != 0) continue;
                    double ccx = ix + 0.5, ccz = iz + 0.5;
                    bool busy = W.Nodes.Any(nd => Math.Abs(nd.X - ccx) < 1.0 && Math.Abs(nd.Z - ccz) < 1.0)
                        || (W.Depth.Sources != null && W.Depth.Sources.Any(sn => sn != null && Math.Abs(sn.X - ccx) < 1.0 && Math.Abs(sn.Z - ccz) < 1.0))
                        || Enumerable.Range(1, W.ActorCount - 1).Any(j => W.Actors[j].Alive && Math.Abs(W.X[j] - ccx) < 1.0 && Math.Abs(W.Z[j] - ccz) < 1.0);
                    if (busy) continue;
                    dx = Math.Cos(a); dz = Math.Sin(a); dd = dist; found = true;
                }
                if (found) break;
            }
            Set(Ms.position, ScreenOf(W.X[0] + dd * dx, W.Z[0] + dd * dz));
            yield return TickFrame();
            int head = W.EventHead, cursor = PlayerFeedback.Head;
            Press(Kb.fKey);
            yield return TickFrame();
            Release(Kb.fKey);
            for (int f = 0; f < T.CueFrames; f++) yield return TickFrame();
            var dug = Events(head, SimEventType.Terraform).Where(q => q.A == 0).ToList();
            bool pop = Ledger(FeedbackChannel.WorldActionPop, 0, out FeedbackEntry p, cursor);
            AssertAtControls("the end");
            Check("F28 WorldActionPop", "Builder DIG with F", "Terraform " + (dug.Count > 0 ? "v" + dug[0].Value : "none") + ", pop " + (pop ? "reason " + p.Reason : "none"), "a pop with WorldDig (" + PlayerReasons.WorldDig + ")", pop && p.Reason == PlayerReasons.WorldDig);
        }

        [UnityTest]
        public IEnumerator F29_Refusal_ShowsReasonWord()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            Isolate(ClassId.Farmer, 30, 1, 0);
            W.Actors[0].AbilityCd = 5;   // on cooldown
            int head = W.EventHead, cursor = PlayerFeedback.Head;
            Press(Ms.rightButton);
            yield return TickFrame();
            Release(Ms.rightButton);
            for (int f = 0; f < T.CueFrames; f++) yield return TickFrame();
            bool refused = Events(head, SimEventType.ActionRefused).Any(q => q.A == 0);
            bool tag = Ledger(FeedbackChannel.RefusalTag, 0, out FeedbackEntry r, cursor);
            AssertAtControls("the end");
            Check("F29 RefusalTag", "ability pressed on cooldown", "ActionRefused " + refused + ", RefusalTag " + (tag ? "reason " + r.Reason : "none"), "COOLDOWN (" + PlayerReasons.RefusedCooldown + ") shown", tag && r.Reason == PlayerReasons.RefusedCooldown);
        }

        [UnityTest]
        public IEnumerator F34_Autopilot_TakeoverIsShown()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            Isolate(ClassId.Farmer, 30, 1, 0);
            AssertAtControls("the start");
            bool taken = false, shown = false;
            int takeTick = -1;
            for (int t = 0; t < S.Ticks(W.Config.Depth.Mech.AfkSeconds + 4) && !shown; t++)
            {
                S.Tick(1);
                if (t % 10 == 0) yield return null;
                if (!taken && MechRules.Autopiloted(W, 0)) { taken = true; takeTick = t; }
                if (taken)
                {
                    var presence = S.Driver.GetComponent<CriticalMass.Runtime.Spectator.PlayerPresence>();
                    shown = (presence != null && presence.BannerVisible) || ScreenTexts().Any(s => s.Contains("AUTOPILOT"));
                }
            }
            for (int f = 0; f < T.CueFrames && taken && !shown; f++)
            {
                yield return null;
                var presence = S.Driver.GetComponent<CriticalMass.Runtime.Spectator.PlayerPresence>();
                shown = (presence != null && presence.BannerVisible) || ScreenTexts().Any(s => s.Contains("AUTOPILOT"));
            }
            Check("F34 AutopilotShown", "no input for " + (W.Config.Depth.Mech.AfkSeconds + 4) + " s", "taken " + taken + (taken ? " at " + (takeTick / W.Config.SimHz).ToString("0.0") + " s" : "") + ", announced " + shown, "an AUTOPILOT banner the moment it takes over", taken && shown);
        }

        // ------------------------------------------------------------------ Editor review defects (review/DEFECTS.txt), fix-round claims

        [UnityTest]
        public IEnumerator D0_FiveRestarts_ThenLeftShift_NoErrors()
        {
            // defect 0: any error or exception logged fails a UnityTest by itself (LogAssert); this states it at the end too
            for (int k = 0; k < 5; k++) yield return Load(ClassId.Warrior, k % 2 == 0 ? CameraView.TopDown : CameraView.ThirdPerson);
            Press(Kb.leftShiftKey);
            for (int f = 0; f < 3; f++) yield return TickFrame();
            Release(Kb.leftShiftKey);
            for (int f = 0; f < 20; f++) yield return TickFrame();
            LogAssert.NoUnexpectedReceived();
            Check("D0 RestartsThenShift", "5 match loads then a left Shift press", "no error or exception logged", "none", true);
        }

        [UnityTest]
        public IEnumerator D1_P_InMatch_OpensNoPanel()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            Isolate(ClassId.Farmer, 30, 1, 0);
            for (int f = 0; f < 5; f++) yield return TickFrame();
            HashSet<int> before = new HashSet<int>(Panels().Select(g => g.GetInstanceID()));
            Press(Kb.pKey);
            yield return TickFrame();
            Release(Kb.pKey);
            for (int f = 0; f < 10; f++) yield return TickFrame();
            List<string> opened = Panels().Where(g => !before.Contains(g.GetInstanceID())).Select(g => g.name).ToList();
            bool paused = Time.timeScale == 0f && S.Driver.TimeScale != 0;
            Check("D1 P_OpensNoPanel", "P pressed once in a match (P is a Cancel binding)", opened.Count == 0 ? "no panel opened" : "opened: " + string.Join(", ", opened.Take(6)), "nothing opens", opened.Count == 0 && !paused);
        }

        static IEnumerable<GameObject> Panels() =>
            Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Select(r => r.gameObject)
                .Where(g => g.activeInHierarchy && (g.name.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0 || g.name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0 || g.name.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0));

        /// <summary>Screen rectangle of a model's renderers (bounds corners projected).</summary>
        static Rect ScreenRect(Transform model)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            if (cam == null || model == null) return Rect.zero;
            foreach (Renderer rd in model.GetComponentsInChildren<Renderer>())
            {
                if (!rd.enabled) continue;
                Bounds b = rd.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 sp = cam.WorldToScreenPoint(p);
                    if (sp.z <= 0) continue;
                    x0 = Mathf.Min(x0, sp.x); y0 = Mathf.Min(y0, sp.y); x1 = Mathf.Max(x1, sp.x); y1 = Mathf.Max(y1, sp.y);
                }
            }
            return x1 < x0 ? Rect.zero : Rect.MinMaxRect(x0, y0, x1, y1);
        }

        [UnityTest]
        public IEnumerator D2_ThirdPerson_MeleeRange_EnemyNotBehindMe([ValueSource(nameof(Classes))] ClassId x)
        {
            yield return Load(x, CameraView.ThirdPerson);
            CameraForward(out double dx, out double dz);
            int e = Isolate(ClassId.Farmer, T.StandShareOfRange * W.Config.Class(x).AttackRange, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            for (int f = 0; f < 10; f++) yield return TickFrame(() => Pin(e, px, pz));
            Rect mine = ScreenRect(S.ActorModel(0)), theirs = ScreenRect(S.ActorModel(e));
            Vector2 c = theirs.width > 0 ? theirs.center : ScreenOf(px, pz);
            // the enemy's screen box minus the part my own box covers (boxes over-cover a silhouette, so this is strict)
            float ox = Mathf.Max(0, Mathf.Min(mine.xMax, theirs.xMax) - Mathf.Max(mine.xMin, theirs.xMin)), oy = Mathf.Max(0, Mathf.Min(mine.yMax, theirs.yMax) - Mathf.Max(mine.yMin, theirs.yMin));
            float area = theirs.width * theirs.height;
            double visibleShare = area > 0 ? 1 - ox * oy / area : 0;
            bool hidden = visibleShare < T.EnemyVisibleShareMin;
            AssertAtControls("the end");
            Check("D2 EnemyNotBehindMe", x + " third person, enemy at 0.8 x range ahead", "my fighter " + mine + ", enemy centre " + c + ", enemy box " + theirs + ", share of the enemy box not covered by my box " + visibleShare.ToString("0.00") + " (target >= " + T.EnemyVisibleShareMin + ")", "the enemy stays mostly visible past my own fighter", mine.width > 0 && theirs.width > 0 && !hidden);
        }

        [UnityTest]
        public IEnumerator D3_Hit_MovesCamera6px([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            yield return HitAndWatch(ClassId.Warrior, v, q => false, r);
            Check("D3 HitMovesCamera", Tag(ClassId.Warrior, v), r.Hit ? "a fixed world point moved " + r.CamPxPeak.ToString("0.0") + " px at 1080p, peak at rendered frame " + r.CamPxFrame + ", first >= 6 px at rendered frame " + r.CamPx6Frame + "; kick alone " + r.KickPxPeak.ToString("0.0") + " px peaking at rendered frame " + r.KickPxFrame + " (0 = the hit frame)" : r.Note, ">= 6 px by rendered frame 1 and the kick's peak by rendered frame 1 (reduce motion off)", r.Hit && r.CamPx6Frame >= 0 && r.CamPx6Frame <= 1 && r.KickPxFrame >= 0 && r.KickPxFrame <= 1);
        }

        static Rect UiRect(Object src)
        {
            RectTransform rt = src is Component c ? c.transform as RectTransform : src is GameObject g ? g.transform as RectTransform : null;
            if (rt == null) return Rect.zero;
            var w = new Vector3[4];
            rt.GetWorldCorners(w);
            Canvas cv = rt.GetComponentInParent<Canvas>();
            UnityEngine.Camera cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        [UnityTest]
        public IEnumerator D4_DamageNumber_DoesNotOverlapNameOrBar([ValueSource(nameof(Views))] CameraView v)
        {
            var r = new Watch();
            FeedbackEntry num = default;
            yield return HitAndWatch(ClassId.Warrior, v, q => Ledger(FeedbackChannel.DamageNumber, Enemy, out num, q.LedgerAtHit), r);
            bool bar = LedgerLast(FeedbackChannel.EnemyHealthBar, Enemy, r.LedgerAtHit, out FeedbackEntry be);
            Rect nr = r.Seen ? UiRect(num.Source) : Rect.zero, br = bar ? UiRect(be.Source) : Rect.zero;
            // the name tag: an active text in the unit stack that names nobody else (UnitStack.cs owns the layout above a unit)
            var names = Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && t.transform.parent != null && t.transform.parent.name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0).Select(t => UiRect(t)).Where(q => q.width > 0).ToList();
            bool overlapBar = br.width > 0 && nr.Overlaps(br);
            bool overlapName = names.Any(q => q.Overlaps(nr) && Vector2.Distance(q.center, nr.center) < 200);
            Check("D4 NumberClearOfNameAndBar", Tag(ClassId.Warrior, v), r.Hit ? "number " + nr + ", bar " + br + " overlap " + overlapBar + ", name tags found " + names.Count + " overlap " + overlapName : r.Note, "no overlap", r.Hit && r.Seen && bar && !overlapBar && !overlapName);
        }

        [UnityTest]
        public IEnumerator D6_Dodge_And_Shield_ReportCues()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            SanityWorld.FromScreenIntent(1, 0, out double dx, out double dz);
            int e = Isolate(ClassId.Warrior, 1.2, dx, dz);
            double px = W.X[e], pz = W.Z[e];
            Action pin = () => Pin(e, px, pz);
            for (int f = 0; f < 3; f++) yield return TickFrame(pin);
            // a dodge (left Shift), then a melee hit from the enemy through the sim's damage path inside the i-frames
            int c1 = PlayerFeedback.Head, h1 = W.EventHead;
            Press(Kb.aKey);
            Press(Kb.leftShiftKey);
            bool dodgeStarted = false;
            for (int t = 0; t < 8 && !dodgeStarted; t++)
            {
                yield return TickFrame(pin);
                if (t == 0) Release(Kb.leftShiftKey);
                dodgeStarted = Events(h1, SimEventType.Dodge).Any(q => q.A == 0);
            }
            Release(Kb.aKey);
            if (dodgeStarted) Damage.ApplyHit(W, 0, 12, e, W.X[e], W.Z[e], DamageCause.Melee, 0, 0, 0, 0);
            for (int f = 0; f < T.CueFrames; f++) yield return TickFrame(pin);
            bool dodged = Events(h1, SimEventType.HitBlocked).Any(q => q.A == 0 && (int)q.Value == PlayerReasons.BlockedDodge);
            bool dodgeCue = LedgerReason(FeedbackChannel.BlockedTag, PlayerReasons.BlockedDodge, c1);
            for (int f = 0; f < W.Config.SimHz; f++) yield return TickFrame(pin);
            // a raised shield (right mouse, aimed at the enemy), then the same hit from the front
            int c2 = PlayerFeedback.Head, h2 = W.EventHead;
            Set(Ms.position, ScreenOf(px, pz));
            Press(Ms.rightButton);
            for (int f = 0; f < 3; f++) yield return TickFrame(pin);
            Damage.ApplyHit(W, 0, 12, e, W.X[e], W.Z[e], DamageCause.Melee, 0, 0, 0, 0);
            for (int f = 0; f < T.CueFrames; f++) yield return TickFrame(pin);
            Release(Ms.rightButton);
            bool shielded = Events(h2, SimEventType.HitBlocked).Any(q => q.A == 0 && (int)q.Value == PlayerReasons.BlockedShield);
            bool shieldCue = LedgerReason(FeedbackChannel.BlockedTag, PlayerReasons.BlockedShield, c2);
            AssertAtControls("the end");
            Check("D6 DodgeAndShieldCues", "Warrior dodges, then shields, a hit from a Warrior", "dodge started " + dodgeStarted + ", HitBlocked " + dodged + ", cue " + dodgeCue + "; shield: HitBlocked " + shielded + ", cue " + shieldCue + " [" + LedgerDump(c1) + "]", "a ledger cue for each", dodgeCue && shieldCue);
        }
    }
}
