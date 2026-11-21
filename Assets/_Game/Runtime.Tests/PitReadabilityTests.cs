using System.IO;
using CriticalMass.Runtime.View;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish r2, Editor review round 2, item 4: a dug cell was hard to see from the gameplay camera. The pit's floor
    /// and inner walls are a clear value step darker than the grass (never near-black), and a thin fresh-earth lip rims the
    /// cut. Values in display luma (Rec. 709 on sRGB). Every case PRINTS what, value, limit.
    /// </summary>
    public sealed class PitReadabilityTests
    {
        static float L(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        [Test]
        public void ThePitIsDarkerThanTheGroundAndTheLipReadsAsFreshEarth()
        {
            float grass = L(TerrainEditMesh.GroundGrass), floor = L(TerrainEditMesh.PitFloor), lip = L(TerrainEditMesh.FreshEarth);
            Debug.Log($"[b15 pit] grass {grass:0.000}, floor {floor:0.000} (limit <= {0.5f * grass:0.000}, >= 0.20), lip {lip:0.000} (limit >= floor + 0.25)");
            Assert.LessOrEqual(floor, 0.5f * grass, "the floor reads as a hole");
            Assert.GreaterOrEqual(floor, 0.20f, "never a black hole");
            foreach (Color32 wall in new[] { TerrainEditMesh.PitTopsoil, TerrainEditMesh.PitClay, TerrainEditMesh.PitRock })
            {
                Debug.Log($"[b15 pit] wall band {wall}: {L(wall):0.000} (limit <= {0.6f * grass:0.000})");
                Assert.LessOrEqual(L(wall), 0.6f * grass, "inner walls darker than the ground");
            }
            Assert.GreaterOrEqual(lip - floor, 0.25f, "the lip stands out against the floor");
            Assert.Less(lip, grass, "the lip is earth, not a highlight");
            Color.RGBToHSV(TerrainEditMesh.FreshEarth, out float h, out float s, out _);
            Assert.That(h * 360f, Is.InRange(20f, 40f), "a warm earth hue");
            Assert.Greater(s, 0.35f);
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "_Game", "Runtime", "View", "TerrainEditMesh.Organic.cs"));
            StringAssert.Contains("Jit(PitFloor, cx, cz, 11)", src);
            StringAssert.Contains("Jit(FreshEarth, cx, cz, 701)", src);
        }
    }
}
