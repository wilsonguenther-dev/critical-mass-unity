using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-03 unit rings: pool, styles, placement, no per-frame allocation, shader shipped, self-install.</summary>
    public class UnitRingsTests
    {
        GameObject _go;
        UnitRings _rings;
        World _world;
        GameConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            _cfg = GameConfig.CreateDefault();
            _world = World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = _cfg,
            });
            var scratch = new InputFrame();
            for (int i = 0; i < 60; i++) MatchTick.AdvanceNoHuman(_world, scratch);
            _go = new GameObject("UnitRingsTest");
            _rings = _go.AddComponent<UnitRings>();
            _rings.Bind(_world, _cfg);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rings != null)
            {
                if (_rings.Mesh != null) UnityEngine.Object.DestroyImmediate(_rings.Mesh);
                if (_rings.Material != null) UnityEngine.Object.DestroyImmediate(_rings.Material);
            }
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void PoolHoldsOneQuadPerActorSlot()
        {
            Assert.AreEqual(32, World.ActorCapacity(_cfg));
            Assert.AreEqual(32, _rings.Capacity);
            Assert.AreEqual(32 * 4, _rings.Mesh.vertexCount);
            Assert.AreEqual(32 * 6, (int)_rings.Mesh.GetIndexCount(0));
            // Art direction 6.4 sizes.
            Assert.AreEqual(0.55f, UnitRings.Radius);
            Assert.AreEqual(0.06f, UnitRings.SelfWidth);
            Assert.AreEqual(0.04f, UnitRings.TeamWidth);
            Assert.AreEqual(8, UnitRings.EnemyDashes);
        }

        [Test]
        public void SelfIsBrassAlliesSolidEnemiesDashed()
        {
            _rings.Sync(1.0, new Vector3(0.5f, -0.8f, 0.5f));
            Assert.AreEqual(UnitRings.Style.Self, _rings.StyleOf(0));
            int self = 0, ally = 0, enemy = 0;
            TeamId mine = _world.Actors[0].Team;
            for (int i = 0; i < _world.ActorCount; i++)
            {
                UnitRings.Style s = _rings.StyleOf(i);
                if (!_world.Actors[i].Alive) { Assert.AreEqual(UnitRings.Style.Hidden, s, "dead actor " + i); continue; }
                if (s == UnitRings.Style.Self) self++;
                else if (s == UnitRings.Style.Ally) { ally++; Assert.AreEqual(mine, _world.Actors[i].Team, "ally " + i); }
                else if (s == UnitRings.Style.Enemy) { enemy++; Assert.AreNotEqual(mine, _world.Actors[i].Team, "enemy " + i); }
            }
            Assert.AreEqual(1, self);
            Assert.AreEqual(_rings.VisibleCount, self + ally + enemy);
            Assert.Greater(ally, 0);
            Assert.Greater(enemy, 0);
        }

        [Test]
        public void RingSitsUnderTheFeetOnTheGround()
        {
            _rings.Sync(1.0, Vector3.forward);
            for (int i = 0; i < _world.ActorCount; i++)
            {
                if (!_world.Actors[i].Alive) continue;
                double h = CriticalMass.Runtime.GroundHeight.At(_world.Map, _world.X[i], _world.Z[i]);
                Vector3 want = SimSpace.ToWorld(_world.X[i], h + UnitRings.Lift, _world.Z[i]);
                Assert.Less((want - _rings.CentreOf(i)).magnitude, 1e-4f, "actor " + i);
            }
            // The quad's own vertices are centred on the ring and flat on the ground plane.
            var verts = _rings.Mesh.vertices;
            Vector3 c = _rings.CentreOf(0);
            Vector3 mid = (verts[0] + verts[1] + verts[2] + verts[3]) / 4f;
            Assert.Less((mid - c).magnitude, 1e-4f);
            for (int k = 0; k < 4; k++) Assert.AreEqual(c.y, verts[k].y, 1e-5f);
        }

        [Test]
        public void KnockedOutUnitsHaveNoRing()
        {
            _world.Actors[5].Alive = false;
            _rings.Sync(1.0, Vector3.forward);
            Assert.AreEqual(UnitRings.Style.Hidden, _rings.StyleOf(5));
            var verts = _rings.Mesh.vertices;
            Assert.AreEqual(verts[20], verts[21]);
            Assert.AreEqual(verts[20], verts[22]);
            Assert.AreEqual(verts[20], verts[23]);
        }

        [Test]
        public void SyncAllocatesNothing()
        {
            var scratch = new InputFrame();
            for (int i = 0; i < 5; i++) _rings.Sync(0.5, Vector3.forward);
            long total = 0;
            for (int frame = 0; frame < 120; frame++)
            {
                MatchTick.AdvanceNoHuman(_world, scratch);   // positions and deaths change between frames
                long before = GC.GetAllocatedBytesForCurrentThread();
                _rings.Sync(frame / 120.0, new Vector3(0.6f, -0.8f, 0.2f));
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.AreEqual(0, total, "bytes allocated by 120 Syncs");
        }

        [Test]
        public void ShaderShipsInResourcesAndDrawsAfterGroundDecals()
        {
            var shader = UnityEngine.Resources.Load<Shader>(UnitRings.ShaderResource);
            Assert.IsNotNull(shader, "Resources/" + UnitRings.ShaderResource);
            Assert.AreEqual("CM/UnitRing", shader.name);
            Assert.IsTrue(shader.isSupported, "CM/UnitRing does not compile (a shader with errors draws nothing)");
            Assert.IsNotNull(_rings.Material);
            Assert.AreEqual(3000 - 40, _rings.Material.renderQueue, "Transparent-40: after CM/WorldDecal (Transparent-50)");
            AssertColor(Color(Palette.Brass), _rings.Material.GetColor("_SelfColor"), "_SelfColor");
            AssertColor(Color(Palette.Azure), _rings.Material.GetColor("_AzureColor"), "_AzureColor");
            AssertColor(Color(Palette.Crimson), _rings.Material.GetColor("_CrimsonColor"), "_CrimsonColor");
            AssertColor(Color(Palette.VoidNavy), _rings.Material.GetColor("_OutlineColor"), "_OutlineColor");
            var r = _go.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(r);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode);
            Assert.AreSame(_rings.Mesh, r.GetComponent<MeshFilter>().sharedMesh);
        }

        [Test]
        public void InstallerAddsExactlyOneNextToTheDriver()
        {
            var driverGo = new GameObject("DriverForRings");
            try
            {
                var driver = driverGo.AddComponent<SimDriverBehaviour>();
                UnitRings a = UnitRingsInstaller.EnsureOn(driver);
                UnitRings b = UnitRingsInstaller.EnsureOn(driver);
                Assert.AreSame(a, b);
                Assert.AreEqual(1, driverGo.GetComponents<UnitRings>().Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(driverGo);
            }
        }

        /// <summary>Material colours round-trip through float storage: equal within 1/1000 per channel, not bit-exact.</summary>
        static void AssertColor(Color want, Color got, string what)
        {
            for (int c = 0; c < 4; c++) Assert.AreEqual(want[c], got[c], 1e-3, what + " channel " + c);
        }

        static Color Color(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }
    }
}
