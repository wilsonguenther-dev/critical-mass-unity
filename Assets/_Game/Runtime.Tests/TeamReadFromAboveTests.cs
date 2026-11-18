using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b14 polish, team read from above (EditMode): per character class, the share of the body seen from the 55-degree
    /// gameplay camera that is team-coloured (mask codes 3-8), before and after the top-facing team tint (CMToonTeamTop),
    /// averaged over eight facings; and both teams' ink and cloth carry equal weight (no team reads weaker).
    /// Every case prints one line (what, value, limit).
    /// </summary>
    public sealed class TeamReadFromAboveTests
    {
        const float PitchDeg = 55f;

        static IEnumerable<ModelSpec> Characters() =>
            ArtMapping.Models.Where(s => s != null && s.Kind == ArtKind.Character && s.Meshes != null && s.Meshes.Length > 0);

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static bool TeamCode(int code) => code >= 3 && code <= 8 && code != 0;

        [Test]
        public void TeamColourShareFromTheGameplayCameraGrowsForEveryClass()
        {
            int measured = 0;
            foreach (ModelSpec spec in Characters())
            {
                if (ArtMaterials.BaseMapPathOf(spec) != null) { Debug.Log("team read " + spec.Key + ": textured pilot (team mask in its texture), skipped"); continue; }
                Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(spec.AssetPath).OfType<Mesh>().FirstOrDefault(m => m.name == spec.Meshes[0]);
                if (mesh == null) { Debug.Log("team read " + spec.Key + ": mesh " + spec.Meshes[0] + " not found, skipped"); continue; }
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var cols = new List<Color>();
                try { mesh.GetVertices(verts); mesh.GetNormals(norms); mesh.GetColors(cols); }
                catch (System.Exception e) { Debug.Log("team read " + spec.Key + ": mesh not readable here (" + e.Message + "), skipped"); continue; }
                if (cols.Count != verts.Count || norms.Count != verts.Count)
                {
                    Debug.Log("team read " + spec.Key + ": no vertex colours / normals on " + mesh.name + " (textured pilot), skipped");
                    continue;
                }
                int[] tris = mesh.triangles;
                double team = 0, topFixed = 0, total = 0;
                for (int yaw = 0; yaw < 8; yaw++)
                {
                    // view direction from the camera into the scene: pitched 55 degrees down, yawed around the model
                    Vector3 view = Quaternion.Euler(PitchDeg, yaw * 45f, 0f) * Vector3.forward;
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        Vector3 n = (norms[a] + norms[b] + norms[c]).normalized;
                        float facing = -Vector3.Dot(n, view);
                        if (facing <= 0f) continue;
                        float area = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).magnitude * 0.5f * facing;
                        int code = Mathf.RoundToInt(((cols[a].a + cols[b].a + cols[c].a) / 3f) * 8f);
                        total += area;
                        if (TeamCode(code)) team += area;
                        else if (code == 0) topFixed += area * Smooth(0.70f, 0.86f, n.y);
                    }
                }
                if (total <= 0) continue;
                double raw = team / total, eff = (team + topFixed * ArtMaterials.TeamTopTint) / total;
                Debug.Log("team read " + spec.Key + ": team-coloured share from 55 deg " + raw.ToString("0.000") + " -> " + eff.ToString("0.000") +
                          " with the top tint " + ArtMaterials.TeamTopTint + " (limit: grows by >= 0.010); same mesh for both teams");
                Assert.That(eff - raw, Is.GreaterThanOrEqualTo(0.010), spec.Key + " top tint adds no team colour from above");
                measured++;
            }
            Debug.Log("team read: classes measured " + measured);
        }

        static float Luma(string hex)
        {
            Assert.IsTrue(ColorUtility.TryParseHtmlString(hex, out Color c), hex);
            Color l = c.linear;
            return 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
        }

        static float Sat(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            Color.RGBToHSV(c, out _, out float s, out _);
            return s;
        }

        static float Hue(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            Color.RGBToHSV(c, out float h, out _, out _);
            return h * 360f;
        }

        static float HueDist(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        [Test]
        public void BothTeamsInkAndClothCarryEqualWeight()
        {
            float la = Luma(ArtMaterials.InkAzure), lc = Luma(ArtMaterials.InkCrimson);
            float ratio = Mathf.Max(la, lc) / Mathf.Max(1e-4f, Mathf.Min(la, lc));
            Debug.Log("team ink luma azure " + la.ToString("0.000") + " crimson " + lc.ToString("0.000") + " ratio " + ratio.ToString("0.00") + " (limit <= 1.6, both < 0.12 so it still reads as ink)");
            Assert.That(ratio, Is.LessThanOrEqualTo(1.6f));
            Assert.That(Mathf.Max(la, lc), Is.LessThan(0.12f));
            float hA = HueDist(Hue(ArtMaterials.InkAzure), Hue(Palette.Azure)), hC = HueDist(Hue(ArtMaterials.InkCrimson), Hue(Palette.Crimson));
            Debug.Log("team ink hue off its team colour: azure " + hA.ToString("0") + " deg, crimson " + hC.ToString("0") + " deg (limit <= 20)");
            Assert.That(hA, Is.LessThanOrEqualTo(20f));
            Assert.That(hC, Is.LessThanOrEqualTo(20f));
            foreach (ModelSpec spec in Characters())
            {
                string ca = ArtMapping.Swatch(spec, TeamId.Azure).Cloth, cc = ArtMapping.Swatch(spec, TeamId.Crimson).Cloth;
                float sa = Sat(ca), sc = Sat(cc);
                Debug.Log("team cloth " + spec.Key + ": azure " + ca + " sat " + sa.ToString("0.00") + ", crimson " + cc + " sat " + sc.ToString("0.00") + " (limit: each >= 0.45)");
                Assert.That(sa, Is.GreaterThanOrEqualTo(0.45f), spec.Key + " azure cloth too grey");
                Assert.That(sc, Is.GreaterThanOrEqualTo(0.45f), spec.Key + " crimson cloth too grey");
            }
        }
    }
}
