using System;
using System.Collections;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Reactions;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// World reactions in the real Boot game: the installer attached every component, every force source and every
    /// explosion kind goes through the one bus and the force field, ground paint, camera response and spring bones
    /// render frames without an error logged (the test runner fails on any logged error).
    /// </summary>
    public sealed class WorldReactionsPlayTests
    {
        SanityWorld _w;

        [TearDown]
        public void TearDown()
        {
            SanityWorld.Reset();
            _w = null;
        }

        [UnityTest]
        public IEnumerator EveryForceTierFiresWithoutErrors()
        {
            _w = new SanityWorld();
            yield return _w.Load(autopilot: true);
            GameObject go = _w.Driver.gameObject;
            Assert.IsNotNull(go.GetComponent<ForceField>(), "ForceField installed");
            Assert.IsNotNull(go.GetComponent<GroundPaint>(), "GroundPaint installed");
            Assert.IsNotNull(go.GetComponent<CameraResponse>(), "CameraResponse installed");
            Assert.IsNotNull(go.GetComponent<CharacterSprings>(), "CharacterSprings installed");
            Assert.IsNull(go.GetComponent<CargoView>(), "one cargo drawer: the animation pass's CarryProp");
            WorldForceBus bus = WorldForceBus.Active;
            Assert.IsNotNull(bus, "CombatFx made the force bus");

            World w = _w.W;
            double mx = w.X[0], mz = w.Z[0];
            long before = bus.Pushed;
            foreach (ForceSource s in (ForceSource[])Enum.GetValues(typeof(ForceSource)))
            {
                ForceShape shape = s == ForceSource.KaijuSlam || s == ForceSource.Explosion || s == ForceSource.Hatch ? ForceShape.Radial : ForceShape.Cone;
                WorldForce f = bus.Make(w, s, shape, mx + 2.0, mz, 1.0, 0.0, s == ForceSource.KaijuSlam ? 5f : 2f, ExplosionKind.Tnt);
                bus.Push(f);
                _w.Driver.StepTicks(1);
                yield return null;
            }
            foreach (ExplosionKind k in (ExplosionKind[])Enum.GetValues(typeof(ExplosionKind)))
            {
                Effects.Explode(mx - 3.0, mz + 1.0, 2.0, k);
                _w.Driver.StepTicks(2);
                yield return null;
            }
            for (int i = 0; i < 30; i++)
            {
                _w.Driver.StepTicks(1);
                yield return null;
            }
            Assert.Greater(bus.Pushed - before, (long)((ForceSource[])Enum.GetValues(typeof(ForceSource))).Length - 1, "every source reached the bus");
        }
    }
}
