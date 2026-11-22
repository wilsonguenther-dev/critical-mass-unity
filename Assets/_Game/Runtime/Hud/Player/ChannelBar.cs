using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Lane S2's channel starts (WorldAction Value 0 on the start tick): a progress bar under my fighter for the channel's
    /// length. GATHER (B = WorldGather, Radius = seconds to the first unit) needs E HELD, so the bar says "HOLD E" and
    /// follows the sim's own Actor.GatherProgress: released (progress back to 0 without a Gather event) = the bar goes;
    /// a completed unit flashes it full and, while E stays held, the next unit starts again. MINE (B = WorldMineOre,
    /// Radius = mine seconds) is one tap: "MINING" runs on the clock and ends on OreMined, or a grace after its time
    /// (a cancel, a get-up or leaving the vein sends no event). A gather with Radius 0 (a source) shows the word only.
    /// Size, px at 1080p: bar 132 x 10 with a 2 px outline, word Display 18 above it, 36 px under the feet point.
    /// Ledger: WorldActionPop (reason = the World* code, value = seconds) when a channel bar shows.
    /// </summary>
    public sealed class ChannelBar
    {
        public const float W = 132f, H = 10f, Below = 36f, Grace = 0.4f, ZeroTolerance = 0.15f, DoneFlash = 0.18f;
        public const string HoldWord = "HOLD E", MineWord = "MINING";

        readonly RectTransform _rt;
        readonly CanvasGroup _group;
        readonly RectTransform _fill;
        readonly Text _word;
        int _kind;          // 0 none, else the World* code
        float _seconds, _age, _zeroFor, _done = -1f;
        bool _sawProgress;

        public bool Active => _kind != 0;
        public int Kind => _kind;
        public float Progress { get; private set; }

        /// <summary>Progress of a clock channel (pure; tests).</summary>
        public static float ClockProgress(float age, float seconds) => seconds > 1e-3f ? Mathf.Clamp01(age / seconds) : 0f;

        /// <summary>True while a clock channel is still worth showing (pure; tests).</summary>
        public static bool ClockAlive(float age, float seconds) => seconds > 1e-3f ? age < seconds + Grace : age < 2.5f;

        public ChannelBar(RectTransform world)
        {
            _rt = PxUi.Centred(world, "Channel Bar", W, H + 28f);
            _group = PxUi.Group(_rt);
            _word = PxUi.Label(_rt, "Word", CMTokens.Display, UiScale.TypeSmall, PxPalette.Cream, TextAnchor.MiddleCenter, -40f, 0f, W + 80f, 24f, 2f);
            PxUi.Shape(_rt, "Back", ShapeKind.RoundRect, 0f, 26f, W, H, PxPalette.BarBack, -1f, 2f, 0f);
            VectorShape f = PxUi.Shape(_rt, "Fill", ShapeKind.RoundRect, 0f, 26f, W, H, PxPalette.Brass, -1f, 0f, 0f);
            _fill = f.rectTransform;
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.anchoredPosition = new Vector2(0f, -26f - H * 0.5f);
            _rt.gameObject.SetActive(false);
        }

        public void Clear()
        {
            _kind = 0;
            _done = -1f;
            PxUi.Show(_rt, false);
        }

        /// <summary>A WorldAction of mine with Value 0 (a channel start).</summary>
        public void OnStart(int kind, float seconds, int me)
        {
            if (kind != PlayerReasons.WorldMineOre && kind != ActionTags.WorldGather) return;
            _kind = kind;
            _seconds = Mathf.Max(0f, seconds);
            _age = 0f;
            _zeroFor = 0f;
            _done = -1f;
            _sawProgress = false;
            HudText.Set(_word, kind == ActionTags.WorldGather ? HoldWord : MineWord);
            _group.alpha = 1f;
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            PlayerFeedback.Report(FeedbackChannel.WorldActionPop, me, _seconds, _word, kind);
        }

        /// <summary>The unit arrived (Gather / SourceGather / OreMined for me): flash full; a held gather carries on.</summary>
        public void OnComplete(bool gather)
        {
            if (_kind == 0 || gather != (_kind == ActionTags.WorldGather)) return;
            _done = DoneFlash;
            _age = 0f;
            _zeroFor = 0f;
        }

        public void Tick(World w, int me, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            if (_kind == 0) return;
            if (me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) { Clear(); return; }
            _age += dt;
            float p;
            if (_done > 0f)
            {
                _done -= dt;
                p = 1f;
                if (_done <= 0f && _kind == PlayerReasons.WorldMineOre) { Clear(); return; }
            }
            else if (_kind == ActionTags.WorldGather && _seconds > 1e-3f)
            {
                float g = (float)w.Actors[me].GatherProgress;
                if (g > 1e-4f) { _sawProgress = true; _zeroFor = 0f; }
                else _zeroFor += dt;
                // released: the sim zeroed the progress and no unit arrived (or it never started)
                if ((_sawProgress || _age > 0.5f) && _zeroFor > ZeroTolerance) { Clear(); return; }
                p = Mathf.Clamp01(g / _seconds);
            }
            else
            {
                if (!ClockAlive(_age, _seconds)) { Clear(); return; }
                p = _seconds > 1e-3f ? ClockProgress(_age, _seconds) : 0.5f + 0.5f * Mathf.Sin(_age * 6f);
            }
            Progress = p;
            float sx = Mathf.Max(0.001f, p);
            if (Mathf.Abs(_fill.localScale.x - sx) > 1e-3f) _fill.localScale = new Vector3(sx, 1f, 1f);
            if (!PxProject.Screen(cam, PxProject.Actor(w, me, alpha, 0f), 0f, out Vector2 at)) { PxUi.Alpha(_group, 0f); return; }
            _rt.anchoredPosition = at / scale - new Vector2(0f, Below);
            PxUi.Alpha(_group, 1f);
        }
    }
}
