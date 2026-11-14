using System;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/hit-feel-1: the swing's contact frame on the damage tick, the attacker facing and stepping into its target, the
    /// victim reacting away from the blow, hit-stop on both fighters, the camera nudge and the capped pain / effort voice.
    /// </summary>
    public sealed class HitFeelTests
    {
        static readonly ClassId[] All = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        static World NewWorld(ClassId cls) => World.CreateWorld(new WorldOptions
        {
            Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = GameConfig.CreateDefault(),
        });

        static int Enemy(World w)
        {
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team != w.Actors[0].Team && w.Actors[i].Alive) return i;
            return -1;
        }

        /// <summary>Puts actor 0 in a swing pressed `age` ticks ago with wind-up W, facing +x, and an enemy at (dx, dz).</summary>
        static World Swinging(ClassId cls, int windup, int age, double dx, double dz, out int enemy)
        {
            World w = NewWorld(cls);
            w.Tick = 200;
            enemy = Enemy(w);
            Actor a = w.Actors[0];
            a.SwingTick = w.Tick - age;
            a.SwingWindup = windup;
            a.SwingFacing = 0;
            a.Facing = 0;
            for (int i = 1; i < w.ActorCount; i++) { w.X[i] = w.X[0] + 40 + i; w.Z[i] = w.Z[0] + 40; }   // everyone else far away
            w.X[enemy] = w.X[0] + dx;
            w.Z[enemy] = w.Z[0] + dz;
            return w;
        }

        // ---------------------------------------------------------------- timing

        [Test]
        public void EveryClassHitsItsContactFrameOnTheDamageTick()
        {
            const double hz = 30;
            foreach (ClassId cls in All)
                for (int windup = 1; windup <= 14; windup++)
                {
                    string clip = ArtMapping.ClassClips(cls).Attack;
                    if (cls == ClassId.Healer || cls == ClassId.Demolitions || cls == ClassId.Builder) clip = clip ?? ArtMapping.Attack;
                    double c = ClipFacts.Contact(clip, 0.567f), len = ClipFacts.Get(clip) != null ? ClipFacts.Get(clip).length : 1.067;
                    double w = windup / hz;
                    Assert.AreEqual(c, AttackTiming.ClipTime(w, c, len, w), 1e-9, $"{cls} W={windup}: contact frame on the damage tick");
                    Assert.Less(AttackTiming.ClipTime(w - 1 / hz, c, len, w), c, $"{cls}: anticipation before contact");
                    Assert.Greater(AttackTiming.ClipTime(w + 1 / hz, c, len, w), c, $"{cls}: follow-through after contact");
                    Assert.AreEqual(w, AttackTiming.ContactAge(c, w), 1e-9, $"{cls}: 0 ms offset");
                }
        }

        // ---------------------------------------------------------------- facing and lunge

        [Test]
        public void TheAttackerTurnsToTheEnemyItSwingsAt()
        {
            World w = Swinging(ClassId.Warrior, 4, 2, 1.0, 0.4, out int e);
            Assert.AreEqual(e, HitFeel.SwingTarget(w, 0));
            Assert.AreEqual(Math.Atan2(0.4, 1.0), HitFeel.Facing(w, 0, 0), 1e-9, "faces the target, not just the press direction");

            World behind = Swinging(ClassId.Warrior, 4, 2, -1.0, 0, out _);
            Assert.AreEqual(-1, HitFeel.SwingTarget(behind, 0), "an enemy behind is not the target");
            Assert.AreEqual(0, HitFeel.Facing(behind, 0, 0), 1e-9, "falls back to the sim's swing facing");

            World late = Swinging(ClassId.Warrior, 4, 4 + 30, 1.0, 0.4, out _);
            late.Actors[0].Facing = 1.0;
            Assert.AreEqual(1.0, HitFeel.Facing(late, 0, 0), 1e-9, "after the follow-through the sim's facing rules again");
        }

        [Test]
        public void TheLungePeaksOnContactAndIsAbsorbed()
        {
            const double W = 0.2;
            Assert.AreEqual(0, HitFeel.LungeShape(0, W), 1e-9);
            Assert.Less(HitFeel.LungeShape(W * 0.3, W), 0, "a small lean back first (anticipation)");
            Assert.AreEqual(1, HitFeel.LungeShape(W, W), 1e-9, "peak on the damage tick");
            Assert.AreEqual(0, HitFeel.LungeShape(W + HitFeel.LungeReturn, W), 1e-9, "back to the sim position");
            Assert.AreEqual(0, HitFeel.LungeShape(-0.1, W), 1e-9);
            Assert.AreEqual(0, HitFeel.LungeShape(HitFeel.LungeReturn, 0), 1e-9, "classic rules: follow-through only");

            World w = Swinging(ClassId.Builder, 6, 6, 1.4, 0, out _);
            Vector3 l = HitFeel.Lunge(w, 0, 0);
            Assert.Greater(l.x, 0f, "toward the target (+x)");
            Assert.LessOrEqual(l.magnitude, (float)HitFeel.LungeMax + 1e-5f);
            World close = Swinging(ClassId.Builder, 6, 6, 0.9, 0, out _);
            Assert.AreEqual(0f, HitFeel.Lunge(close, 0, 0).magnitude, 1e-5f, "bodies already touching: no step into the target");
        }

        // ---------------------------------------------------------------- reaction

        [Test]
        public void TheVictimReactsAwayFromTheBlow()
        {
            // Victim faces +z (Unity). A blow travelling +x came from its left (-x): the chest twists right (negative).
            Assert.AreEqual(HitSide.Left, HitReactions.Side(0, 1, 1, 0));
            Assert.Less(HitReactions.Pick(10, false, false, HitSide.Left).TwistDeg, 0f);
            Assert.Greater(HitReactions.Pick(10, false, false, HitSide.Right).TwistDeg, 0f);
            Assert.AreEqual(HitSide.Front, HitReactions.Side(0, 1, 0, -1), "a blow travelling against the facing is frontal");
            Assert.AreEqual(ArtMapping.Hit, HitReactions.Pick(10, false, false, HitSide.Front).Clip, "light: the flinch");
            Assert.AreEqual(ArtMapping.HitHeavy, HitReactions.Pick(25, false, false, HitSide.Front).Clip, "big hit: the stagger");
            Assert.AreEqual(ReactionLayer.FullBody, HitReactions.Pick(60, true, false, HitSide.Front).Layer, "knock-out: full body");
        }

        // ---------------------------------------------------------------- hit-stop and camera

        [Test]
        public void HitStopIsFortyToSeventyMillisecondsAndOffUnderReduceMotion()
        {
            for (byte t = 0; t <= 2; t++)
            {
                double s = HitFeel.HoldWallSeconds(t, false);
                Assert.GreaterOrEqual(s, 0.04);
                Assert.LessOrEqual(s, 0.07);
                Assert.AreEqual(0, HitFeel.HoldWallSeconds(t, true));
            }
            Assert.Greater(HitFeel.Nudge(true, false), 0f);
            Assert.Greater(HitFeel.Nudge(false, true), 0f);
            Assert.AreEqual(0f, HitFeel.Nudge(false, false), "nobody else's hit moves the camera");
        }

        // ---------------------------------------------------------------- voice

        [Test]
        public void PainAndEffortVoicesExistAndAreCapped()
        {
            int pain = SoundTable.IndexOf("pain"), effort = SoundTable.IndexOf("effort");
            Assert.GreaterOrEqual(pain, 0);
            Assert.GreaterOrEqual(effort, 0);
            Assert.LessOrEqual(SoundTable.Cues[pain].MaxVoices, 3, "a brawl is a few voices, not a wall");
            Assert.LessOrEqual(SoundTable.Cues[effort].MaxVoices, 2);
            Assert.Greater(SoundTable.Cues[pain].PitchJitter, 0f, "each grunt a little different");
            Assert.AreEqual(3, SoundTable.Cues[pain].Synth.Length, "three pain shapes");
            foreach (string s in new[] { "pain_a", "pain_b", "pain_c", "effort_a", "effort_b" })
                Assert.IsNotNull(SynthRecipes.Find(s), s + " is synthesised in code");
            int on = 0;
            for (int t = 0; t < 100; t++) if (SoundFeed.EffortOn(3, t)) on++;
            Assert.AreEqual(50, on, "about every other swing");
        }

        [Test]
        public void TheCaptureDummyStandsInsideTheReach()
        {
            GameConfig c = GameConfig.CreateDefault();
            foreach (ClassId cls in All)
            {
                double r = LightWeaponView.Range(c, cls), d = HitFeelCapture.DummyAhead(r);
                Assert.Less(d, r + 0.3, cls + ": the swing can land");
            }
        }
    }
}
