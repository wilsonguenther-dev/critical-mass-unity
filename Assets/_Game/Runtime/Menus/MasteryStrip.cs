using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// b15 profile: the results screen's progress strip, in 1920 x 1080 reference px. It sits in the free band below the
    /// way-out buttons and left of the footer (ResultsLayout: buttons end at 964, the footer starts at x 1180), inside the
    /// safe margin, so it never covers a results zone (MasteryStripTests checks it against ResultsLayout.Zones()). The
    /// match-summary item, which owns ResultsLayout, may later give it a named zone instead.
    /// </summary>
    public static class MasteryStripLayout
    {
        public static readonly Rect Strip = new Rect(120f, 972f, 1040f, 56f);
        public static Rect Left => new Rect(Strip.x + 16f, Strip.y + 4f, 480f, 28f);
        public static Rect Bar => new Rect(Strip.x + 16f, Strip.y + 36f, 480f, 10f);
        public static Rect Right => new Rect(Strip.x + 516f, Strip.y + 4f, Strip.width - 532f, 48f);

        public static List<(string name, Rect rect)> Parts() => new List<(string, Rect)> { ("left", Left), ("bar", Bar), ("right", Right) };

        /// <summary>The two lines the strip shows for a match's meta (pure; tests): what was earned, then what is next.</summary>
        public static (string earned, string next) Lines(MetaResult m)
        {
            if (m == null || m.Award == null) return (string.Empty, string.Empty);
            string cls = ProgressionWords.ClassNames[(int)m.Class];
            MasteryAward a = m.Award;
            string earned;
            if (a.Xp <= 0 && (a.Why == MasteryWhy.PracticeMode || a.Why == MasteryWhy.NotFinished))
                earned = MetaWords.F(MetaWords.StripNone, MetaWords.Why(a.Why));
            else if (a.LevelAfter > a.LevelBefore)
                earned = MetaWords.F(MetaWords.StripLevelUp, cls, a.LevelAfter) + "   +" + a.Xp.ToString("#,0", CultureInfo.InvariantCulture) + " XP";
            else
                earned = MetaWords.F(MetaWords.StripGained, cls, a.Xp.ToString("#,0", CultureInfo.InvariantCulture));
            string next;
            if (m.Unlocked.Count > 0)
            {
                MasteryReward u = m.Unlocked[m.Unlocked.Count - 1];
                next = MetaWords.F(MetaWords.StripUnlocked, MetaWords.F(MetaWords.RewardLine, u.KindName, u.Name));
            }
            else if (m.Next.HasValue)
                next = MetaWords.F(MetaWords.StripNext, m.Next.Value.Level, MetaWords.F(MetaWords.RewardLine, m.Next.Value.KindName, m.Next.Value.Name));
            else next = MetaWords.MaxMastery;
            string why = a.Why == MasteryWhy.BotMatch || a.Why == MasteryWhy.BotDailyAllowanceUsed || a.Why == MasteryWhy.MatchCap ? MetaWords.Why(a.Why) : string.Empty;
            if (why.Length > 0) next = next + "  " + why;
            return (earned, next);
        }
    }

    /// <summary>
    /// b15 profile: draws MasteryStripLayout over the results screen while it is visible after a played match (never in a
    /// replay). Its own canvas, above the results canvas, so ResultsScreen.cs stays untouched.
    /// </summary>
    [GameOverlay(Order = 96, Scope = OverlayScope.MatchOnly)]
    public sealed class MasteryStrip : MonoBehaviour
    {
        Canvas _canvas;
        RectTransform _root;
        Text _left, _right;
        VectorShape _fill;
        MetaResult _shown;
        bool _built;

        public bool Showing => _canvas != null && _canvas.enabled;
        public string LeftText => _left != null ? _left.text : null;
        public string RightText => _right != null ? _right.text : null;

        void Build()
        {
            if (_built) return;
            _built = true;
            _canvas = ProgressionUi.MakeCanvas(transform, "Mastery Strip Canvas", 31, out RectTransform space);
            Rect s = MasteryStripLayout.Strip;
            _root = MenuUi.Rt(space, "Strip");
            MenuUi.Place(_root, 0f, 0f, HudLayout.RefW, HudLayout.RefH);
            VectorShape back = MenuUi.Shape(_root, "Back", ShapeKind.RoundRect, s.x, s.y, s.width, s.height, CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.92f), 14f);
            back.RimPx = 2f;
            Rect l = MasteryStripLayout.Left, b = MasteryStripLayout.Bar, r = MasteryStripLayout.Right;
            _left = MenuUi.Fit(MenuUi.Label(_root, "Earned", CMTokens.Display, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, l.x, l.y, l.width, l.height, "", 2f), UiScale.TypeSmall);
            VectorShape track = MenuUi.Shape(_root, "Track", ShapeKind.RoundRect, b.x, b.y, b.width, b.height, CMTokens.MeterEmpty.Color, 5f);
            track.RimPx = 0f;
            track.OutlinePx = 0f;
            _fill = MenuUi.Shape(track.transform, "Fill", ShapeKind.RoundRect, 0f, 0f, 0f, b.height, CMTokens.ToolBrass.Color, 5f);
            _fill.RimPx = 0f;
            _fill.OutlinePx = 0f;
            _right = MenuUi.Fit(MenuUi.Label(_root, "Next", CMTokens.Ui, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, r.x, r.y, r.width, r.height, "", 2f), UiScale.TypeSmall);
            _right.horizontalOverflow = HorizontalWrapMode.Wrap;
            _canvas.enabled = false;
        }

        void LateUpdate()
        {
            FlowDirector fd = FlowDirector.Current;
            ResultsScreen rs = fd != null ? fd.Results : null;
            bool replay = fd != null && fd.Driver != null && fd.Driver.IsReplay;
            MetaResult m = MetaRecorder.Last;
            bool show = rs != null && rs.IsVisible && !replay && m != null;
            if (!show)
            {
                if (_canvas != null && _canvas.enabled) _canvas.enabled = false;
                return;
            }
            Build();
            ProgressionUi.UpdateScale(_canvas);
            if (!_canvas.enabled) _canvas.enabled = true;
            if (ReferenceEquals(m, _shown)) return;
            _shown = m;
            Show(m);
        }

        /// <summary>Fills the strip for a match (tests call it directly).</summary>
        public void Show(MetaResult m)
        {
            Build();
            (string earned, string next) = MasteryStripLayout.Lines(m);
            HudText.Set(_left, earned);
            HudText.Set(_right, next);
            float f = m != null && m.Award != null ? MasteryRules.Progress(m.Award.XpAfter) : 0f;
            MenuUi.Place(_fill.rectTransform, 0f, 0f, MasteryStripLayout.Bar.width * f, MasteryStripLayout.Bar.height);
            _fill.gameObject.SetActive(f > 0f);
        }
    }
}
