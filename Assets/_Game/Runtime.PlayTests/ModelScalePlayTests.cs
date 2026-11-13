using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// fix/model-scale (2026-11-12, "is this character supposed to inflate like that?"): a fighter is never drawn at a wrong
    /// size. Every scale effect is interrupted mid-way (garbage left on the root and the chest, death then respawn, class
    /// switch, pool reuse) and the model must come back to exactly its imported base; a 16v16 autopilot soak checks that no
    /// fighter's rendered body ever exceeds 1.3x its class baseline; the takeover's live-model portrait is an opaque card.
    /// </summary>
    public sealed class ModelScalePlayTests
    {
        const float Tol = 1e-4f;
        const float Inflate = 2.7f;   // the size the owner saw
        SanityWorld _w;

        [SetUp]
        public void SetUp() => ModelScale.ResetForTests();

        [TearDown]
        public void TearDown()
        {
            SanityWorld.Reset();
            _w = null;
        }

        static List<ClassId> ModelClasses()
        {
            var list = new List<ClassId>();
            foreach (ClassId c in ClassIds.All)
                if (SanityWorld.HasModel(ArtMapping.ClassModel(c))) list.Add(c);
            return list;
        }

        static Vector3 BaseOf(string key) => ArtCatalog.Load().Find(key).Model.transform.localScale;

        static void AssertScale(Vector3 want, Vector3 got, string what)
        {
            Assert.That(Vector3.Distance(want, got), Is.LessThan(Tol), $"{what}: scale {got.ToString("F4")}, base {want.ToString("F4")}");
        }

        /// <summary>Leaves an effect's leftovers on the root and the chest, as an interrupted tween would.</summary>
        static void Interrupt(Transform model)
        {
            model.localScale *= Inflate;
            Transform chest = SanityRig.Find(model, "chest");
            if (chest != null) chest.localScale *= Inflate;
        }

        /// <summary>The chest is at its rest scale times at most the 2% breath, never an effect's leftovers.</summary>
        static void AssertChestSane(Transform model, string what)
        {
            Transform chest = SanityRig.Find(model, "chest");
            if (chest == null) return;
            string key = model.name.Substring(model.name.IndexOf(' ', "Actor ".Length) + 1);
            Transform restChest = SanityRig.Find(ArtCatalog.Load().Find(key).Model.transform, "chest");
            float rest = restChest != null ? restChest.localScale.y : 1f;
            Assert.That(chest.localScale.y / rest, Is.InRange(0.97f, 1.03f), what + ": the chest kept an effect's scale");
        }

        [UnityTest]
        public IEnumerator AnInterruptedEffectNeverSurvivesTheNextFrame()
        {
            List<ClassId> classes = ModelClasses();
            if (classes.Count == 0) Assert.Ignore("no class has a model");
            foreach (ClassId cls in classes)
            {
                _w = new SanityWorld();
                yield return _w.Load(cls);
                Transform model = _w.ActorModel(0);
                Assert.IsNotNull(model, cls + ": no model drawn");
                Interrupt(model);
                _w.Sync();
                AssertScale(BaseOf(_w.ActorModelKey(0)), model.localScale, cls + " after one frame");
                AssertChestSane(model, cls.ToString());
                // Many frames on a frozen clock (the hit-stop and big-moment picture hold): nothing compounds.
                for (int f = 0; f < 120; f++) _w.Sync();
                AssertScale(BaseOf(_w.ActorModelKey(0)), model.localScale, cls + " after 120 frozen frames");
                AssertChestSane(model, cls + " frozen");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator DeathThenRespawnAndPoolReuseStartAtBase()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            Transform model = _w.ActorModel(0);
            if (model == null) Assert.Ignore("the Warrior has no model");
            string key = _w.ActorModelKey(0);
            Actor a = _w.W.Actors[0];
            Interrupt(model);
            a.Alive = false;   // view-only: no sim tick runs while the flag is flipped
            _w.Sync();
            Assert.IsFalse(model.gameObject.activeSelf, "the dead actor's model was not returned to the pool");
            AssertScale(BaseOf(key), model.localScale, "parked in the pool mid-effect");
            a.Alive = true;
            _w.Sync();
            Transform again = _w.ActorModel(0);
            Assert.AreSame(model, again, "respawn did not reuse the pooled model");
            AssertScale(BaseOf(key), again.localScale, "respawned from the pool");
            AssertChestSane(again, "respawned");
        }

        [UnityTest]
        public IEnumerator ClassSwitchDuringAHitStartsAtBase()
        {
            List<ClassId> classes = ModelClasses();
            if (classes.Count < 2) Assert.Ignore("needs two classes with models");
            _w = new SanityWorld();
            yield return _w.Load(classes[0]);
            Actor a = _w.W.Actors[0];
            ClassId was = a.Cls;
            ClassId other = classes[0] == was ? classes[1] : classes[0];
            Transform first = _w.ActorModel(0);
            Interrupt(first);
            a.Cls = other;   // the live class switch (PR #35), view-only
            _w.Sync();
            Transform second = _w.ActorModel(0);
            AssertScale(BaseOf(_w.ActorModelKey(0)), second.localScale, "after switching to " + other);
            Interrupt(second);
            a.Cls = was;
            _w.Sync();
            Transform back = _w.ActorModel(0);
            Assert.AreSame(first, back, "switching back did not reuse the pooled model");
            AssertScale(BaseOf(_w.ActorModelKey(0)), back.localScale, "after switching back to " + was);
            AssertChestSane(back, "switched back");
        }

        /// <summary>Sanity suite: 40 s of a 16v16 autopilot fight; no fighter's body is ever drawn above 1.3x its class height.</summary>
        [UnityTest]
        public IEnumerator SoakNoFighterExceedsItsClassBaseline()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior, manualInput: false, autopilot: true);
            var worst = new Dictionary<string, float>();
            var bad = new List<string>();
            int ticks = _w.Ticks(40);
            for (int t = 0; t < ticks; t++)
            {
                _w.Tick(1);
                if (t % 5 != 0) continue;
                for (int i = 0; i < _w.W.ActorCount; i++)
                {
                    Transform m = _w.ActorModel(i);
                    if (m == null) continue;
                    string key = _w.ActorModelKey(i);
                    ModelSpec spec = ArtMapping.Spec(key);
                    float baseY = BaseOf(key).y;
                    float rootRatio = m.localScale.y / Mathf.Max(1e-4f, baseY);
                    float ratio = rootRatio;
                    Transform head = SanityRig.Find(m, "head");
                    if (head != null && spec != null && spec.Height > 0.1f)
                        ratio = Mathf.Max(ratio, (head.position.y - m.position.y) / (spec.Height * baseY * m.parent.lossyScale.y));
                    if (!worst.TryGetValue(key, out float w0) || ratio > w0) worst[key] = ratio;
                    if (ratio > 1.3f && bad.Count < 12) bad.Add($"tick {_w.W.Tick}: actor {i} {key} drawn at {ratio:F2}x its class baseline");
                }
            }
            foreach (KeyValuePair<string, float> kv in worst) Debug.Log($"[ModelScale] soak worst {kv.Key}: {kv.Value:F2}x");
            Assert.AreEqual(0, ModelScale.ClampCount, "an effect factor left the sane range during the soak");
            CollectionAssert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>The cut-in's live render sits on an opaque card, never a bare cut-out over the battlefield.</summary>
        [UnityTest]
        public IEnumerator TakeoverPortraitIsAnOpaqueCard()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            var host = new GameObject("Portrait Test Host");
            var p = new ModelPortrait(host.transform);
            try
            {
                if (!p.CanRender(ModelPortrait.ClassKey(ClassId.Warrior))) Assert.Ignore("no GPU or no Warrior model");
                Transform real = _w.ActorModel(0);
                Vector3 before = real.localScale;
                Assert.IsNotNull(p.BeginClass(ClassId.Warrior, TeamId.Azure));
                Assert.AreEqual(1f, p.Backdrop.a, 1e-4f, "the portrait background is see-through: the render reads as a giant fighter");
                _w.Sync();
                AssertScale(before, real.localScale, "the real fighter while its portrait renders");
            }
            finally
            {
                p.Dispose();
                Object.Destroy(host);
            }
        }
    }
}
