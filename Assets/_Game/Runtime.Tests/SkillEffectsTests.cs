using System;
using System.Linq;
using System.Reflection;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Skills;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>feat/skill-effects-1: every class skill has a registered tell, marks follow the sim status, the sim is untouched.</summary>
    public class SkillEffectsTests
    {
        static World NewWorld(GameConfig cfg, ClassId cls = ClassId.Warrior) => World.CreateWorld(new WorldOptions
        {
            Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg,
        });

        sealed class Rig : IDisposable
        {
            public readonly FxMeshBuilder G = new FxMeshBuilder("skill test ground", SkillFxView.GroundVerts, SkillFxView.GroundVerts * 3);
            public readonly FxMeshBuilder S = new FxMeshBuilder("skill test solid", SkillFxView.SolidVerts, SkillFxView.SolidVerts * 2);
            public readonly FxMeshBuilder A = new FxMeshBuilder("skill test air", SkillFxView.AirVerts, SkillFxView.AirVerts * 3);

            public void Build(SkillFxCore c, World w)
            {
                G.Clear(); S.Clear(); A.Clear();
                c.Build(w, 0, Vector3.right, Vector3.up, G, S, A);
            }

            public int Dropped => G.Dropped + S.Dropped + A.Dropped;

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(G.Mesh);
                UnityEngine.Object.DestroyImmediate(S.Mesh);
                UnityEngine.Object.DestroyImmediate(A.Mesh);
            }
        }

        static int CountAlive(World w, Func<Actor, bool> p)
        {
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Alive && p(w.Actors[i])) n++;
            return n;
        }

        [Test]
        public void EverySimAbilityKindHasARegisteredSkillEffect()
        {
            var kinds = typeof(Combat).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(int) && f.Name.StartsWith("Kind", StringComparison.Ordinal) && f.Name != "KindAttack")
                .ToList();
            Assert.GreaterOrEqual(kinds.Count, 7, "Combat.Kind* constants were found");
            foreach (FieldInfo f in kinds)
            {
                int k = (int)f.GetRawConstantValue();
                Assert.IsTrue(SkillVisuals.TryForAbilityKind(k, out SkillId id), "Combat." + f.Name + " has no registered skill effect (add it to SkillVisuals)");
                Assert.IsNotNull(SkillVisuals.Get(id), "Combat." + f.Name + " maps to " + id + " which has no SkillVisual");
            }
        }

        [Test]
        public void EverySkillIdHasThreeBeatsAndARealOrNamedPose()
        {
            foreach (SkillId id in Enum.GetValues(typeof(SkillId)))
            {
                SkillVisual v = SkillVisuals.Get(id);
                Assert.IsNotNull(v, id + " is not registered");
                Assert.IsFalse(string.IsNullOrEmpty(v.WindUp), id + " has no wind-up");
                Assert.IsFalse(string.IsNullOrEmpty(v.Effect), id + " has no effect");
                Assert.IsFalse(string.IsNullOrEmpty(v.Mark), id + " has no mark");
                Assert.IsFalse(string.IsNullOrEmpty(v.DrawnBy), id + " names no view");
                Assert.IsFalse(string.IsNullOrEmpty(v.Key), id + " has no capture key");
                if (v.Kaiju || !v.ClipIsReal) continue;
                foreach (string clip in v.Clip.Split(new[] { " then " }, StringSplitOptions.None))
                    Assert.IsNotNull(ClipFacts.Get(clip), id + ": clip " + clip + " is not in the shared clip pack (clip_facts.json)");
            }
            foreach (ClassId cls in ClassIds.All)
                Assert.GreaterOrEqual(SkillVisuals.All.Count(v => !v.Kaiju && v.Cls == cls), 2, cls + " has fewer than two registered skills");
            Assert.AreEqual(Enum.GetValues(typeof(SkillId)).Length, SkillVisuals.All.Length, "one entry per SkillId");
        }

        [Test]
        public void SlowAndShieldMarksFollowTheSimStatusTickByTick()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            var core = new SkillFxCore();
            InputFrame f = SimInput.EmptyInput();
            using (var rig = new Rig())
            {
                int enemy = -1;
                for (int i = 0; i < w.ActorCount && enemy < 0; i++) if (w.Actors[i].Alive && w.Actors[i].Team == TeamId.Crimson) enemy = i;
                Assert.GreaterOrEqual(enemy, 0);
                w.Actors[enemy].SlowTimer = cfg.Abilities.FarmerRoots.SlowSeconds;
                core.Read(w);
                rig.Build(core, w);
                Assert.GreaterOrEqual(core.MarkedCount, 1, "a slowed enemy gets vines");
                int ticks = (int)Math.Ceiling((cfg.Abilities.FarmerRoots.SlowSeconds + 6) * cfg.SimHz);
                for (int t = 0; t < ticks; t++)
                {
                    MatchTick.Advance(w, null, f);
                    core.Read(w);
                    rig.Build(core, w);
                    Assert.AreEqual(CountAlive(w, a => a.SlowTimer > 0), core.MarkedCount, "vines exactly while slowed, tick " + w.Tick);
                    Assert.AreEqual(CountAlive(w, a => a.Cls == ClassId.Warrior && a.AbilityActive > 0), core.ShieldsDrawn, "shield wall exactly while up, tick " + w.Tick);
                }
                Assert.IsFalse(w.Actors[enemy].Alive && w.Actors[enemy].SlowTimer > 0 && core.MarkedCount == 0);
                Assert.AreEqual(0, rig.Dropped, "no mesh budget overflowed");
            }
        }

        [Test]
        public void ThirtyTwoMarkedFightersStayInBudgetWithoutAllocating()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            var core = new SkillFxCore();
            for (int i = 0; i < w.ActorCount; i++)
            {
                w.Actors[i].SlowTimer = cfg.Abilities.FarmerRoots.SlowSeconds;
                if (w.Actors[i].Cls == ClassId.Warrior) w.Actors[i].AbilityActive = 2;
            }
            using (var rig = new Rig())
            {
                core.Read(w);
                rig.Build(core, w);
                Assert.AreEqual(CountAlive(w, a => true), core.MarkedCount);
                Assert.AreEqual(0, rig.Dropped, "32 slowed fighters fit the budget");
                Assert.LessOrEqual(rig.S.VertCount, SkillFxView.SolidVerts);
                Assert.That(() => rig.Build(core, w), UIs.Not.AllocatingGCMemory());
            }
        }

        [Test]
        public void SimStateHashIsIdenticalWithSkillEffectsOn()
        {
            GameConfig cfgOff = GameConfig.CreateDefault(), cfgOn = GameConfig.CreateDefault();
            World off = NewWorld(cfgOff, ClassId.Farmer), on = NewWorld(cfgOn, ClassId.Farmer);
            InputFrame f1 = SimInput.EmptyInput(), f2 = SimInput.EmptyInput();
            var core = new SkillFxCore();
            using (var rig = new Rig())
            {
                for (int t = 0; t < 6000; t++)
                {
                    MatchTick.Advance(off, null, f1);
                    MatchTick.Advance(on, null, f2);
                    core.Read(on);
                    rig.Build(core, on);
                    if (t % 500 == 499) Assert.AreEqual(CombatFxTests.StateHash(off), CombatFxTests.StateHash(on), "state hash differs at tick " + off.Tick);
                }
                Assert.AreEqual(CombatFxTests.StateHash(off), CombatFxTests.StateHash(on));
                Assert.Greater(core.Decoded, 100);
                TestContext.Progress.WriteLine("[Skill fx] fired: " + string.Join(", ", Enum.GetValues(typeof(SkillId)).Cast<SkillId>().Select(s => s + "=" + core.Fired[(int)s])));
            }
            SkillAnimCues.Clear();
        }

        [Test]
        public void AnimationCuesAreFreshBrieflyAndTheOverlayEnds()
        {
            SkillAnimCues.Clear();
            SkillAnimCues.Note(3, 100, 5.0, "Throw", SkillAnimCues.ProcThrow);
            Assert.AreEqual("Throw", SkillAnimCues.FreshClip(3, 100));
            Assert.AreEqual("Throw", SkillAnimCues.FreshClip(3, 100 + SkillAnimCues.FreshTicks));
            Assert.IsNull(SkillAnimCues.FreshClip(3, 101 + SkillAnimCues.FreshTicks));
            Assert.IsNull(SkillAnimCues.FreshClip(4, 100));
            Assert.IsTrue(SkillAnimCues.Proc(3, 5.3, out byte k, out float age));
            Assert.AreEqual(SkillAnimCues.ProcThrow, k);
            Assert.That(age, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.IsFalse(SkillAnimCues.Proc(3, 5.0 + SkillAnimCues.ProcSeconds + 0.01, out k, out age));
            Assert.AreEqual(0f, SkillAnimCues.Envelope((float)SkillAnimCues.ProcSeconds));
            SkillAnimCues.Clear();
            Assert.IsNull(SkillAnimCues.FreshClip(3, 100));
        }

        [Test]
        public void TheSkillEffectsSwitchIsOnByDefault()
        {
            bool had = PlayerPrefs.HasKey(WorldFeedbackSettings.SkillFxKey);
            int was = PlayerPrefs.GetInt(WorldFeedbackSettings.SkillFxKey, 1);
            PlayerPrefs.DeleteKey(WorldFeedbackSettings.SkillFxKey);
            try
            {
                Assert.IsTrue(WorldFeedbackSettings.SkillEffects);
                Assert.Contains(WorldFeedbackSettings.SkillFxKey, WorldFeedbackSettings.AllKeys);
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(WorldFeedbackSettings.SkillFxKey, was);
            }
        }
    }
}
