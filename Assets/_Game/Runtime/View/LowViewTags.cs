using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// follow-camera-2: name tags ("Name Tag n", the depth HUD) and overhead hearts ("Hearts n", the game HUD) in the third-
    /// and first-person views. Those are screen-space marks the HUD places at a fighter's projected head, so they already
    /// face the camera; this pass runs after the HUD (execution order 1100) and, only while a low play view is on, sizes
    /// each one by its fighter's distance from the camera (TagLens: larger up close, smaller far) and hides it too near
    /// the lens or beyond TagLens.TagMaxDistance / BarMaxDistance. It changes no HUD file or layout: it finds the marks by
    /// name once, matches each to the fighter whose head projects onto it, and restores scale 1 when the view changes back.
    /// No per-frame allocation (fixed arrays; the name scan runs only while nothing is bound, every 2 s at most).
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public sealed class LowViewTags : MonoBehaviour
    {
        const int Max = 24;
        /// <summary>Head lifts the two HUDs project from (DepthHud tags 2.65 u, GameHud hearts 2.8 u).</summary>
        public const float TagLift = 2.65f, HeartLift = 2.8f;
        /// <summary>A mark belongs to the fighter whose head projects within this many pixels of it.</summary>
        public const float MatchPx = 12f;

        readonly RectTransform[] _tags = new RectTransform[Max], _hearts = new RectTransform[Max];
        readonly Canvas[] _tagCanvas = new Canvas[Max], _heartCanvas = new Canvas[Max];
        int _tagN, _heartN;
        float _nextBind;
        bool _wasLow;
        SimDriverBehaviour _driver;

        /// <summary>Tags and hearts this pass hid last frame (counts for the capture log).</summary>
        public static int Hidden;

        void LateUpdate()
        {
            bool low = GroundView.PlayActive;
            if (!low)
            {
                if (_wasLow) Restore();
                _wasLow = false;
                return;
            }
            _wasLow = true;
            if ((_tagN == 0 || _heartN == 0 || _tags[0] == null) && Time.unscaledTime >= _nextBind) Bind();
            if (_driver == null && GroundView.Current != null) _driver = GroundView.Current.Driver;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            World w = _driver != null ? _driver.World : null;
            if (cam == null || w == null) return;
            Hidden = 0;
            Pass(cam, w, _tags, _tagCanvas, _tagN, TagLift, TagLens.TagMaxDistance);
            Pass(cam, w, _hearts, _heartCanvas, _heartN, HeartLift, TagLens.BarMaxDistance);
        }

        void Pass(UnityEngine.Camera cam, World w, RectTransform[] marks, Canvas[] canvases, int n, float lift, float max)
        {
            double a = _driver.Alpha;
            Vector3 cp = cam.transform.position;
            for (int k = 0; k < n; k++)
            {
                RectTransform rt = marks[k];
                if (rt == null || !rt.gameObject.activeSelf) continue;
                Canvas c = canvases[k];
                UnityEngine.Camera uiCam = c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
                Vector2 at = RectTransformUtility.WorldToScreenPoint(uiCam, rt.position);
                // The fighter this mark was placed for: the one whose head projects onto it.
                float best = MatchPx * MatchPx, dist = -1f;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    if (!w.Actors[i].Alive) continue;
                    double x = w.PrevX[i] + (w.X[i] - w.PrevX[i]) * a, z = w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * a;
                    Vector3 head = SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + lift, z);
                    Vector3 sp = cam.WorldToScreenPoint(head);
                    if (sp.z <= 0f) continue;
                    float dx = sp.x - at.x, dy = sp.y - at.y, d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; dist = (head - cp).magnitude; }
                }
                if (dist < 0f) continue;   // not matched: leave it as the HUD placed it
                if (!TagLens.Show(dist, max))
                {
                    rt.gameObject.SetActive(false);   // the HUD shows it again next frame if it still wants it; this pass runs after it
                    Hidden++;
                    continue;
                }
                float s = TagLens.Scale(dist);
                if (Mathf.Abs(rt.localScale.x - s) > 0.01f) rt.localScale = new Vector3(s, s, 1f);
            }
        }

        void Restore()
        {
            for (int k = 0; k < _tagN; k++) if (_tags[k] != null) _tags[k].localScale = Vector3.one;
            for (int k = 0; k < _heartN; k++) if (_hearts[k] != null) _hearts[k].localScale = Vector3.one;
        }

        void Bind()
        {
            _nextBind = Time.unscaledTime + 2f;
            _tagN = _heartN = 0;
            RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                RectTransform r = all[i];
                string n = r.name;
                if (_tagN < Max && IsMark(n, "Name Tag ")) { _tags[_tagN] = r; _tagCanvas[_tagN++] = r.GetComponentInParent<Canvas>(true); }
                else if (_heartN < Max && IsMark(n, "Hearts ")) { _hearts[_heartN] = r; _heartCanvas[_heartN++] = r.GetComponentInParent<Canvas>(true); }
            }
        }

        /// <summary>"Name Tag 3" or "Hearts 0": the prefix then digits only (so "Heart 2", the player's own row, is not one).</summary>
        public static bool IsMark(string name, string prefix)
        {
            if (name == null || name.Length <= prefix.Length || !name.StartsWith(prefix, System.StringComparison.Ordinal)) return false;
            for (int i = prefix.Length; i < name.Length; i++) if (name[i] < '0' || name[i] > '9') return false;
            return true;
        }
    }
}
