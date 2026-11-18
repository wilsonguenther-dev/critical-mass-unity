using CriticalMass.Runtime.View.Mechanics;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b14 polish: the chunky built ladder is low cost, sits in the A-frame's frame (foot at z 0, leaning back to
    /// -Run while rising to Rise), reads thick from the gameplay camera, and its ghost copies share its shape.</summary>
    public sealed class ChunkyLadderTests
    {
        [Test]
        public void ChunkyLadderIsCheapThickAndInTheAFrameFrame()
        {
            Mesh m = ChunkyLadder.Mesh;
            Bounds b = m.bounds;
            int tris = ChunkyLadder.TriangleCount;
            Debug.Log("chunky ladder: " + tris + " triangles (limit <= 450), bounds " + b + ", rail section " + (ChunkyLadder.RailR * 2f).ToString("0.000") +
                      " u x scale 1.25 (limit >= 0.12 u), rungs " + ChunkyLadder.Rungs);
            Assert.That(tris, Is.LessThanOrEqualTo(450));
            Assert.That(b.max.y, Is.EqualTo(ChunkyLadder.Rise * 1.04f).Within(0.12f));
            Assert.That(b.max.z, Is.LessThan(0.12f), "foot at z 0");
            Assert.That(b.min.z, Is.LessThan(-ChunkyLadder.Run * 0.9f), "leans back to -Run");
            Assert.That(ChunkyLadder.RailR * 2f * 1.25f, Is.GreaterThanOrEqualTo(0.12f));
            Assert.IsTrue(m.isReadable, "the staged build cuts it");
            Assert.AreEqual(m.vertexCount, ChunkyLadder.Ghost(true).vertexCount);
            Assert.AreEqual(m.vertexCount, ChunkyLadder.Ghost(false).vertexCount);
            Color32[] c = m.colors32;
            for (int i = 0; i < c.Length; i++) Assert.AreEqual(0, c[i].a, "mask code 0 (fixed, banded) on every vertex");
        }
    }
}
