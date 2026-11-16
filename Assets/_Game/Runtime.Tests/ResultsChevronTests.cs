using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b11-af: with the results screen open no chevron graphic is active outside the results layout (which draws none).
    /// The guided match's world arrow (TrialGuide: the green chevron, its ring and the edge pointer) had no end-of-match
    /// rule and kept bobbing over the last target behind the results; the mechanics HUD's damage chevrons now also hide
    /// on the results state itself, not only on the ended phase, and are switched off when the HUD hides.
    /// </summary>
    public sealed class ResultsChevronTests
    {
        static int ActivePointers(Transform root)
        {
            int n = 0;
            foreach (VectorShape s in root.GetComponentsInChildren<VectorShape>(true))
                if (s.gameObject.activeInHierarchy && s.enabled && (s.Kind == ShapeKind.TriangleDown || s.Kind == ShapeKind.TriangleRight)) n++;
            return n;
        }

        static void ShowEveryArrowPart(TrialGuide g)
        {
            foreach (string part in new[] { "Chevron", "Ring", "Edge Pointer" })
            {
                Transform t = g.Canvas.transform.Find(part);
                Assert.IsNotNull(t, "the world arrow has its " + part);
                t.gameObject.SetActive(true);   // as it was drawn during the match
            }
        }

        [TestCase(FlowState.Results, MatchPhase.Ended)]
        [TestCase(FlowState.Results, MatchPhase.Playing)]
        [TestCase(FlowState.Match, MatchPhase.Ended)]
        public void TheGuidedMatchChevronIsGoneOnceTheMatchIsOver(FlowState state, MatchPhase phase)
        {
            var go = new GameObject("Trial Guide Probe");
            try
            {
                TrialGuide g = go.AddComponent<TrialGuide>();
                g.BuildArrow();
                Assert.IsNotNull(g.Canvas, "the arrow canvas is built");
                ShowEveryArrowPart(g);
                Assert.Greater(ActivePointers(go.transform), 0, "the chevron shows during the match (the check is not vacuous)");
                Assert.IsFalse(g.GateArrow(state, phase, true), $"{state} / {phase}: the arrow may not show");
                Assert.AreEqual(0, ActivePointers(go.transform), $"{state} / {phase}: no chevron or pointer is left active");
                Assert.IsFalse(g.ArrowVisible, "the guide reports the arrow hidden");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheChevronStillShowsInALiveMatch()
        {
            Assert.IsTrue(TrialGuide.ArrowAllowed(FlowState.Match, MatchPhase.Playing, true), "a live guided match points at its target");
            Assert.IsTrue(TrialGuide.ArrowAllowed(FlowState.Off, MatchPhase.Playing, true), "tests and capture tools (flow off) too");
            Assert.IsFalse(TrialGuide.ArrowAllowed(FlowState.Match, MatchPhase.Playing, false), "never while the player is down");
            var go = new GameObject("Trial Guide Probe");
            try
            {
                TrialGuide g = go.AddComponent<TrialGuide>();
                g.BuildArrow();
                ShowEveryArrowPart(g);
                Assert.IsTrue(g.GateArrow(FlowState.Match, MatchPhase.Playing, true), "the gate lets a live match through");
                Assert.Greater(ActivePointers(go.transform), 0, "and leaves the arrow to UpdateArrow");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheDamageChevronsHideOnTheResultsScreen()
        {
            Assert.IsTrue(MechanicsHud.HiddenOver(FlowState.Results, MatchPhase.Ended), "results after the end");
            Assert.IsTrue(MechanicsHud.HiddenOver(FlowState.Results, MatchPhase.Playing), "the results state alone hides it");
            Assert.IsTrue(MechanicsHud.HiddenOver(FlowState.Match, MatchPhase.Ended), "an ended match hides it");
            Assert.IsFalse(MechanicsHud.HiddenOver(FlowState.Match, MatchPhase.Playing), "a live match shows it");
            Assert.IsFalse(MechanicsHud.HiddenOver(FlowState.Off, MatchPhase.Playing), "tests and capture tools show it");
        }
    }
}
