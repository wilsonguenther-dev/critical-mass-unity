using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Easy movement (2026-11-12): the owner's "stuck on the diagonal bridge corner" with the keyboard. Every case below is
    /// a keyboard direction (one key or two, mapped through the 45 degree camera exactly as HumanInputState does) held
    /// from a spot near a diagonal bridge mouth, and the corner where integration/batch-1 (49f8585) stood the human still
    /// for good. Cause: a diagonal deck's walkable band is a capsule with ROUND ends, and the rail clamps a body walking
    /// along the band edge to that edge; at the mouth the round end meets the island's staircase edge, so the clamped
    /// step fits neither the deck nor the box rule and every rail rule answered "stay". Fix: Collide.InwardTarget, the
    /// last rail answer (human-only path; bots never take the rail, so bot-only matches are bit-identical).
    /// </summary>
    [TestFixture]
    public sealed class BridgeCornerTests
    {
        private static readonly GameConfig Cfg = GameConfig.CreateDefault();
        private static readonly double Dt = SimStep.Dt(Cfg);

        private struct Case
        {
            public string Name;
            public double X, Z;      // start
            public double Right, Up; // keyboard intent (screen axes)
            public double SX, SZ;    // where 49f8585 stood still for good
        }

        // Found by a probe over every span mouth, 8 keyboard directions and starts on a 1 u grid (49f8585).
        private static readonly Case[] Cases =
        {
            new Case { Name = "A along B3 mirror, far mouth", X = 67.25, Z = 29.25, Right = -1, Up = 0, SX = 67.184, SZ = 29.332 },
            new Case { Name = "D onto B6, near mouth", X = 31.25, Z = 16.25, Right = 1, Up = 0, SX = 34.335, SZ = 14.182 },
            new Case { Name = "D onto B6, near mouth (2)", X = 32.25, Z = 15.25, Right = 1, Up = 0, SX = 34.350, SZ = 14.182 },
            new Case { Name = "S onto B9, near mouth", X = 31.25, Z = 57.25, Right = 0, Up = -1, SX = 34.355, SZ = 59.818 },
            new Case { Name = "S onto B9, near mouth (2)", X = 32.25, Z = 59.25, Right = 0, Up = -1, SX = 34.342, SZ = 59.818 },
            new Case { Name = "A onto B9 mirror, near mouth", X = 78.25, Z = 59.25, Right = -1, Up = 0, SX = 77.653, SZ = 59.818 },
            new Case { Name = "A onto B9 mirror, near mouth (2)", X = 81.25, Z = 57.25, Right = -1, Up = 0, SX = 77.638, SZ = 59.818 },
            new Case { Name = "W+A along B5 mirror, far mouth", X = 68.25, Z = 51.25, Right = -1, Up = 1, SX = 67.373, SZ = 50.853 },
            new Case { Name = "S+D along B5, far mouth", X = 40.25, Z = 51.25, Right = 1, Up = -1, SX = 44.566, SZ = 50.887 },
            new Case { Name = "S+D along B4, far mouth", X = 19.25, Z = 48.25, Right = 1, Up = -1, SX = 23.674, SZ = 51.190 },
        };

        private static World Lone()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 7,
                TeamSize = Cfg.TeamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateDefault(),
            });
            for (int i = 1; i < w.ActorCount; i++) w.Actors[i].Alive = false;
            Actor a = w.Actors[0];
            a.Alive = true;
            a.StandUp = 0;
            a.SlowTimer = 0;
            a.AbilityActive = 0;
            w.HasAim[0] = 0;
            return w;
        }

        /// <summary>The screen-to-world key map of HumanInputState (RenderConfig yaw), normalised like its Fill.</summary>
        private static void KeyToWorld(double right, double up, out double mx, out double mz)
        {
            double yaw = new RenderConfig().CameraYawDeg * Math.PI / 180;
            double upX = -Math.Sin(yaw), upZ = -Math.Cos(yaw);
            double rightX = -upZ, rightZ = upX;
            mx = right * rightX + up * upX;
            mz = right * rightZ + up * upZ;
            double len = Math.Sqrt(mx * mx + mz * mz);
            if (len > 1)
            {
                mx /= len;
                mz /= len;
            }
        }

        [Test]
        public void HoldingAKeyboardDirectionNeverStandsStillAtADiagonalBridgeCorner()
        {
            var bad = new List<string>();
            foreach (Case c in Cases)
            {
                World w = Lone();
                double r = Collide.ActorRadius(w, 0);
                Assert.IsTrue(Collide.ActorFits(w, c.X, c.Z, r), c.Name + ": start must fit");
                w.X[0] = c.X;
                w.Z[0] = c.Z;
                w.Vx[0] = 0;
                w.Vz[0] = 0;
                KeyToWorld(c.Right, c.Up, out double mx, out double mz);
                var hx = new double[61];
                var hz = new double[61];
                hx[0] = c.X;
                hz[0] = c.Z;
                for (int t = 1; t <= 60; t++)
                {
                    w.Inputs[0].MoveX = mx;
                    w.Inputs[0].MoveZ = mz;
                    Movement.MovementSystem(w, Dt);
                    hx[t] = w.X[0];
                    hz[t] = w.Z[0];
                    if (!Collide.ActorFits(w, hx[t], hz[t], r))
                    {
                        bad.Add($"{c.Name}: stands where ActorFits fails at tick {t} ({hx[t]:F3},{hz[t]:F3})");
                        break;
                    }
                    if (t < 8) continue;
                    double moved = BridgeSpans.Hypot(hx[t] - hx[t - 5], hz[t] - hz[t - 5]);
                    double fromCorner = BridgeSpans.Hypot(hx[t] - c.SX, hz[t] - c.SZ);
                    if (moved < 0.01 && fromCorner < 1.0)
                    {
                        bad.Add($"{c.Name}: stood still at the corner ({hx[t]:F3},{hz[t]:F3}) from tick {t - 5}");
                        break;
                    }
                }
            }
            Assert.IsTrue(bad.Count == 0, bad.Count + " stuck:\n" + string.Join("\n", bad));
        }

        [Test]
        public void TheCornerFixIsDeterministic()
        {
            Case c = Cases[0];
            double[] end = new double[4];
            for (int run = 0; run < 2; run++)
            {
                World w = Lone();
                w.X[0] = c.X;
                w.Z[0] = c.Z;
                KeyToWorld(c.Right, c.Up, out double mx, out double mz);
                for (int t = 0; t < 45; t++)
                {
                    w.Inputs[0].MoveX = mx;
                    w.Inputs[0].MoveZ = mz;
                    Movement.MovementSystem(w, Dt);
                }
                end[run * 2] = w.X[0];
                end[run * 2 + 1] = w.Z[0];
            }
            Assert.AreEqual(end[0], end[2]);
            Assert.AreEqual(end[1], end[3]);
        }
    }
}
