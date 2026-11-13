using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// Letter spacing (tracking) for a centred single-line uGUI Text: shifts glyph quad q by (q - (n - 1) / 2) * Spacing
    /// canvas units, so the line opens or closes about its centre. Add it BEFORE TextOutline on the same object, so the
    /// outline copies the spaced glyphs. Whitespace makes no quad in Unity 6, so word gaps keep their natural width.
    /// </summary>
    public sealed class TextTracking : BaseMeshEffect
    {
        float _spacing;
        static readonly List<UIVertex> Verts = new List<UIVertex>(1024);

        public float Spacing
        {
            get => _spacing;
            set
            {
                if (Mathf.Abs(_spacing - value) < 0.01f) return;
                _spacing = value;
                if (graphic != null) graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Abs(_spacing) < 0.01f) return;
            Verts.Clear();
            vh.GetUIVertexStream(Verts);   // a triangle list: 6 vertices per glyph quad
            int quads = Verts.Count / 6;
            if (quads < 2) return;
            float half = (quads - 1) * 0.5f;
            for (int q = 0; q < quads; q++)
            {
                float dx = (q - half) * _spacing;
                for (int k = 0; k < 6; k++)
                {
                    int i = q * 6 + k;
                    UIVertex v = Verts[i];
                    v.position.x += dx;
                    Verts[i] = v;
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(Verts);
        }
    }
}
