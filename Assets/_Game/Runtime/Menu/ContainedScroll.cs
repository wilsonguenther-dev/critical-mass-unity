using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// hud-containment-1: a settings tab taller than its panel scrolls INSIDE the panel (a clipped viewport) instead of
    /// running past the panel's bottom edge or the screen. Tabs that fit are left exactly as built. Other features add rows
    /// to the same tabs (gore, camera), so the check runs on what each tab really holds when the menu builds.
    /// </summary>
    public static class ContainedScroll
    {
        /// <summary>
        /// Wraps the children of a tab page in a clipped, vertically scrolling viewport when their bottom passes
        /// viewBottom (page coordinates, y down from the top, MenuUi.Place convention). Returns the viewport or null.
        /// </summary>
        public static RectTransform Contain(RectTransform page, float viewX, float viewTop, float viewW, float viewBottom)
        {
            if (page == null) return null;
            float bottom = ContentBottom(page);
            if (bottom <= viewBottom + 0.5f) return null;
            var kids = new List<Transform>();
            for (int i = 0; i < page.childCount; i++) kids.Add(page.GetChild(i));
            RectTransform vp = MenuUi.Rt(page, "Scroll Viewport");
            MenuUi.Place(vp, viewX, viewTop, viewW, viewBottom - viewTop);
            vp.gameObject.AddComponent<RectMask2D>();
            Image hit = vp.gameObject.AddComponent<Image>();   // lets the wheel scroll over empty space in the panel
            hit.color = new Color(0f, 0f, 0f, 0f);
            RectTransform content = MenuUi.Rt(vp, "Scroll Content");
            content.anchorMin = content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(viewW, bottom - viewTop + 24f);
            for (int i = 0; i < kids.Count; i++) kids[i].SetParent(content, true);   // keeps every row where it was built
            ScrollRect sr = vp.gameObject.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = vp;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;
            sr.scrollSensitivity = 40f;
            vp.gameObject.AddComponent<ScrollToSelected>().Scroll = sr;
            return vp;
        }

        /// <summary>The lowest bottom edge of a page's top-left anchored children (page px, y down).</summary>
        public static float ContentBottom(RectTransform page)
        {
            float bottom = 0f;
            for (int i = 0; i < page.childCount; i++)
            {
                var rt = page.GetChild(i) as RectTransform;
                if (rt == null || !rt.gameObject.activeSelf) continue;
                if (rt.anchorMin != new Vector2(0f, 1f) || rt.anchorMax != new Vector2(0f, 1f)) continue;
                float top = -rt.anchoredPosition.y - rt.sizeDelta.y * (1f - rt.pivot.y);
                bottom = Mathf.Max(bottom, top + rt.sizeDelta.y);
            }
            return bottom;
        }
    }

    /// <summary>Keeps the selected row of a scrolling tab in view for keyboard and gamepad (work only when the selection changes).</summary>
    public sealed class ScrollToSelected : MonoBehaviour
    {
        public ScrollRect Scroll;
        GameObject _last;
        static readonly Vector3[] Corners = new Vector3[4];

        void LateUpdate()
        {
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == _last || Scroll == null || Scroll.content == null) return;
            _last = sel;
            if (sel == null || !sel.transform.IsChildOf(Scroll.content)) return;
            RectTransform content = Scroll.content, vp = Scroll.viewport;
            ((RectTransform)sel.transform).GetWorldCorners(Corners);
            float top = -content.InverseTransformPoint(Corners[1]).y, bottom = -content.InverseTransformPoint(Corners[0]).y;
            float view = vp.rect.height, y = content.anchoredPosition.y;
            if (top < y) y = top - 8f;
            else if (bottom > y + view) y = bottom - view + 8f;
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, content.rect.height - view));
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
        }
    }
}
