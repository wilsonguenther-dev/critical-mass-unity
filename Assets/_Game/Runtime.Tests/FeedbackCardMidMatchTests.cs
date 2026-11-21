using System.IO;
using System.Reflection;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish, Editor review item 6: the feedback card threw a NullReferenceException when opened mid-match (Show()
    /// ran with no finished match's answers: _pending.Fun). Opening and closing it mid-match is now safe, and a match that
    /// starts while the card waits or shows closes it instead of leaving it over the match.
    /// </summary>
    public sealed class FeedbackCardMidMatchTests
    {
        string _dir;
        GameObject _go;

        [SetUp]
        public void Make()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cm-feedback-midmatch-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            FeedbackCard.FolderOverride = _dir;
            _go = new GameObject("feedback card test");
        }

        [TearDown]
        public void Clean()
        {
            if (_go != null)
            {
                FeedbackCard c = _go.GetComponent<FeedbackCard>();
                if (c != null) c.Close();
                Object.DestroyImmediate(_go);
            }
            FeedbackCard.FolderOverride = null;
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void OpeningAndClosingMidMatchNeverThrows()
        {
            FeedbackCard card = _go.AddComponent<FeedbackCard>();
            bool shown = true;
            Assert.DoesNotThrow(() => shown = card.TryShowFor(FlowState.Match), "open mid-match");
            Assert.IsFalse(shown, "the card does not open during a match");
            Assert.IsFalse(card.Showing);
            // the reviewer's exact path: the private Show() invoked mid-match
            MethodInfo show = typeof(FeedbackCard).GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(show);
            Assert.DoesNotThrow(() => show.Invoke(card, null), "Show() with no answers pending");
            Assert.IsFalse(card.Showing);
            Assert.DoesNotThrow(card.Close, "close mid-match");
            // with answers pending, still refused during a match
            card.PendForTests();
            Assert.IsFalse(card.TryShowFor(FlowState.Match));
            Debug.Log("[b15 feedback] mid-match open refused without an exception, close is a no-op");
        }

        [Test]
        public void AMatchStartingClosesTheCardOrDropsTheWait()
        {
            FeedbackCard card = _go.AddComponent<FeedbackCard>();
            // waits after the results (show delay), then a match starts: never shown
            card.PendForTests();
            card.FlowChangedForTests(FlowState.Results, FlowState.Menu);
            card.FlowChangedForTests(FlowState.Menu, FlowState.Match);
            Assert.IsFalse(card.TryShowFor(FlowState.Menu), "the wait was dropped with the match start");
            // shown on the home screen, then a match starts: closed (written as skipped)
            card.PendForTests();
            Assert.IsTrue(card.TryShowFor(FlowState.Menu), "shown outside a match");
            Assert.IsTrue(card.Showing);
            Assert.DoesNotThrow(() => card.FlowChangedForTests(FlowState.Menu, FlowState.Match));
            Assert.IsFalse(card.Showing, "a match start closes the card");
            Assert.IsTrue(File.Exists(FeedbackCard.FilePath), "the match line was written");
            Debug.Log("[b15 feedback] match start closes the card; line written to " + FeedbackCard.FilePath);
        }
    }
}
