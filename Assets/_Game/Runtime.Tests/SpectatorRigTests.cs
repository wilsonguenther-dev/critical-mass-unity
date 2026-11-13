using System;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// The spectator camera's rules (SpectatorRig, no Unity objects): follow, cycle (wrapping, skipping the knocked out),
    /// pick under a point, objectives, free flight, zoom and speed limits; and the sim is never written: a match stepped
    /// with a spectator rig cutting, flying and zooming between ticks hashes the same as one stepped alone.
    /// </summary>
    public sealed class SpectatorRigTests
    {
        static World NewWorld(int seed = 4242)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = cfg.TeamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = cfg,
            });
        }

        /// <summary>FNV-1a over every actor's position, velocity, health and state, the Kaiju, the match and the event head.</summary>
        public static ulong StateHash(World w)
        {
            ulong h = 1469598103934665603UL;
            void Mix(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                for (int i = 0; i < 8; i++)
                {
                    h ^= (ulong)((bits >> (i * 8)) & 0xFF);
                    h *= 1099511628211UL;
                }
            }
            Mix(w.Tick);
            Mix(w.EventHead);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                Mix(w.X[i]);
                Mix(w.Z[i]);
                Mix(w.Vx[i]);
                Mix(w.Vz[i]);
                Mix(a.Hp);
                Mix(a.Alive ? 1 : 0);
                Mix((int)a.Cls);
                Mix(a.Kills);
                Mix(a.Deaths);
                Mix(a.Facing);
            }
            if (w.Kaiju != null)
            {
                Mix(w.Kaiju.X);
                Mix(w.Kaiju.Z);
                Mix(w.Kaiju.Hp);
                Mix((int)w.Kaiju.State);
            }
            Mix(w.Match.HillSeconds.Azure);
            Mix(w.Match.HillSeconds.Crimson);
            Mix(w.Match.Time);
            return h;
        }

        [Test]
        public void ResetFollowsTheGivenActorAndSnapsToIt()
        {
            World w = NewWorld();
            var rig = new SpectatorRig();
            rig.Reset(w, 3);
            Assert.AreEqual(SpectatorFocus.Player, rig.Focus);
            Assert.AreEqual(3, rig.Followed);
            Assert.AreEqual(w.X[3], rig.X, 1e-9);
            Assert.AreEqual(w.Z[3], rig.Z, 1e-9);
            Assert.AreEqual(1.0, rig.Zoom);
            Assert.IsFalse(rig.Tactical);
        }

        [Test]
        public void CycleWrapsBothWaysAndSkipsTheKnockedOut()
        {
            World w = NewWorld();
            var rig = new SpectatorRig();
            rig.Reset(w, 0);
            Assert.AreEqual(1, rig.Cycle(w, 1));
            Assert.AreEqual(0, rig.Cycle(w, -1));
            Assert.AreEqual(w.ActorCount - 1, rig.Cycle(w, -1), "wraps backwards");
            Assert.AreEqual(0, rig.Cycle(w, 1), "wraps forwards");
            w.Actors[1].Alive = false;   // a test world, never the game's
            Assert.AreEqual(2, rig.Cycle(w, 1), "a knocked-out player is skipped while others stand");
        }

        [Test]
        public void PickFindsThePlayerUnderThePointOnly()
        {
            World w = NewWorld();
            int id = SpectatorRig.PickAt(w, w.X[5], w.Z[5]);
            Assert.GreaterOrEqual(id, 0);
            double dx = w.X[id] - w.X[5], dz = w.Z[id] - w.Z[5];
            Assert.AreEqual(0.0, dx * dx + dz * dz, 1e-9, "the player standing on the point is picked");
            int near = SpectatorRig.PickAt(w, w.X[5] + 0.5, w.Z[5]);
            Assert.GreaterOrEqual(near, 0, "within the pick radius");
            Assert.AreEqual(-1, SpectatorRig.PickAt(w, w.Map.Bounds.MinX - 50, w.Map.Bounds.MinZ - 50), "nobody far away");
        }

        [Test]
        public void ObjectivesAreTheMapsPlacesAndTheKaijuIsFollowed()
        {
            World w = NewWorld();
            var rig = new SpectatorRig();
            rig.Reset(w, 0);
            rig.JumpTo(SpectatorObjective.Hill);
            rig.Goal(w, 1, out double hx, out double hz);
            Assert.AreEqual(w.Map.Hill.X, hx, 1e-9);
            Assert.AreEqual(w.Map.Hill.Z, hz, 1e-9);
            rig.JumpTo(SpectatorObjective.Shrine);
            rig.Goal(w, 1, out double sx, out double sz);
            Assert.AreEqual(w.Map.Shrine.X, sx, 1e-9);
            Assert.AreEqual(w.Map.Shrine.Z, sz, 1e-9);
            Assert.IsTrue(SpectatorRig.ObjectivePoint(w, SpectatorObjective.AzureBase, out double ax, out double az));
            Assert.IsTrue(SpectatorRig.ObjectivePoint(w, SpectatorObjective.CrimsonBase, out double cx, out double cz));
            Assert.Greater(Math.Abs(ax - cx) + Math.Abs(az - cz), 1.0, "the two bases are apart");
            rig.JumpTo(SpectatorObjective.Kaiju);
            rig.Goal(w, 1, out double kx, out double kz);
            Assert.AreEqual(w.Kaiju.X, kx, 1e-9);
            Assert.AreEqual(w.Kaiju.Z, kz, 1e-9);
            // Easing: a cut travels (the look point moves toward the goal, not onto it in one frame).
            double before = Math.Abs(rig.X - kx) + Math.Abs(rig.Z - kz);
            rig.Step(w, 1, 1.0 / 60);
            double after = Math.Abs(rig.X - kx) + Math.Abs(rig.Z - kz);
            Assert.Less(after, before);
            if (before > 1) Assert.Greater(after, 0.01);
        }

        [Test]
        public void FreeFlightMovesUpTheScreenAndStaysInTheBounds()
        {
            World w = NewWorld();
            var rig = new SpectatorRig();
            rig.Reset(w, 0);
            rig.JumpTo(SpectatorObjective.Hill);
            for (int i = 0; i < 200; i++) rig.Step(w, 1, 1.0 / 60);
            double x0 = rig.X, z0 = rig.Z;
            rig.FreeFly(0, 1, 0.1);
            rig.Step(w, 1, 0.1);
            Assert.AreEqual(SpectatorFocus.Free, rig.Focus);
            double along = (rig.X - x0) * HumanInputState.ScreenUpX + (rig.Z - z0) * HumanInputState.ScreenUpZ;
            Assert.Greater(along, 1.0, "up flies along screen-up");
            for (int i = 0; i < 400; i++)
            {
                rig.FreeFly(1, 1, 0.1);
                rig.Step(w, 1, 0.1);
            }
            Assert.LessOrEqual(rig.X, w.Map.Bounds.MaxX + 1e-9);
            Assert.GreaterOrEqual(rig.X, w.Map.Bounds.MinX - 1e-9);
            Assert.LessOrEqual(rig.Z, w.Map.Bounds.MaxZ + 1e-9);
            Assert.GreaterOrEqual(rig.Z, w.Map.Bounds.MinZ - 1e-9);
            Assert.IsTrue(rig.Follow(w, 4));
            Assert.AreEqual(SpectatorFocus.Player, rig.Focus, "following a player leaves free flight");
        }

        [Test]
        public void ZoomAndSpeedAreClamped()
        {
            var rig = new SpectatorRig();
            for (int i = 0; i < 50; i++) rig.ZoomBy(1);
            Assert.AreEqual(SpectatorRig.MaxZoom, rig.Zoom, 1e-9);
            for (int i = 0; i < 50; i++) rig.ZoomBy(-1);
            Assert.AreEqual(SpectatorRig.MinZoom, rig.Zoom, 1e-9);
            Assert.AreEqual(1.0, rig.Speed);
            rig.StepSpeed(1);
            rig.StepSpeed(1);
            Assert.AreEqual(2.0, rig.Speed, "clamped at the fastest");
            rig.StepSpeed(1, true);
            Assert.AreEqual(0.5, rig.Speed, "wraps when asked");
            rig.ToggleTactical();
            Assert.IsTrue(rig.Tactical);
        }

        [Test]
        public void TheSimStateHashIsUnaffectedByTheSpectatorRig()
        {
            World a = NewWorld(777), b = NewWorld(777);
            var sa = SimInput.EmptyInput();
            var sb = SimInput.EmptyInput();
            var rig = new SpectatorRig();
            rig.Reset(a, 0);
            const int ticks = 600;
            for (int t = 0; t < ticks; t++)
            {
                // Everything a spectator does, between ticks, on world a only.
                if (t % 37 == 0) rig.Cycle(a, 1);
                if (t % 53 == 0) rig.JumpTo((SpectatorObjective)(t / 53 % 5));
                if (t % 71 == 0) rig.FreeFly(0.7, -0.4, 0.05);
                if (t % 29 == 0) rig.ZoomBy(t % 2 == 0 ? 1 : -1);
                if (t % 97 == 0) rig.ToggleTactical();
                SpectatorRig.PickAt(a, rig.X, rig.Z);
                rig.Nearest(a);
                rig.Step(a, 0.5, 1.0 / 60);
                MatchTick.Advance(a, null, sa);
                MatchTick.Advance(b, null, sb);
                if (t % 100 == 99) Assert.AreEqual(StateHash(b), StateHash(a), $"diverged by tick {t + 1}");
            }
            Assert.AreEqual(ticks, a.Tick);
            Assert.AreEqual(StateHash(b), StateHash(a));
        }
    }
}
