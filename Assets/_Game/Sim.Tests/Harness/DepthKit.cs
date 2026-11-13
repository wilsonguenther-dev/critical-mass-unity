using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>Shared helpers of the mechanics-depth tests (U-063 onward): configs with chosen rule sets on, empty fields, event reads.</summary>
    public static class DepthKit
    {
        /// <summary>The browser 5v5 config with the mechanics-depth lane on; tweak may switch single rule sets off.</summary>
        public static GameConfig Cfg(Action<DepthConfig> tweak = null)
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.Depth.Classic = false;
            tweak?.Invoke(c.Depth);
            return c;
        }

        /// <summary>Stage U-063's original roles (only the Farmer feeds, only the raiders corrupt, no U-070 bonuses): the U-063 rule tests pin it.</summary>
        public static void FarmersOnly(DepthConfig d)
        {
            d.Roles.MayFeed = new[] { false, true, false, false, false, false };
            d.Roles.MayCorrupt = new[] { true, false, true, true, false, false };
            d.Roles.BestFeederBonus = 0;
            d.Roles.PassBonus = 0;
        }

        public static World Make(GameConfig cfg, ClassId human, int seed = 63, int teamSize = 5)
        {
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = human, Config = cfg });
        }

        /// <summary>A world where only actor 0 is alive, standing at (x, z), at tick 100.</summary>
        public static World Field(GameConfig cfg, ClassId human, double x = 56, double z = 40, int seed = 63)
        {
            World w = Make(cfg, human, seed);
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1; // no meat orbs from the bodies the kit removed
                w.Depth.WasAlive[j] = 0; // and no coins (U-066)
            }
            Put(w, 0, x, z);
            w.Tick = 100;
            return w;
        }

        public static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            if (!(a.Hp > 0)) a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            w.X[j] = x;
            w.Z[j] = z;
            w.PrevX[j] = x;
            w.PrevZ[j] = z;
            w.Vx[j] = 0;
            w.Vz[j] = 0;
        }

        /// <summary>Turn actor j into cls at full health (tests only).</summary>
        public static void Become(World w, int j, ClassId cls)
        {
            Actor a = w.Actors[j];
            a.Cls = cls;
            a.MaxHp = w.Config.Class(cls).MaxHp;
            a.Hp = a.MaxHp;
        }

        public static int Ally(World w, int k) => 1 + k;
        public static int Enemy(World w, int k) => w.ActorCount / 2 + k;

        public static List<SimEvent> Since(World w, int head, SimEventType type)
        {
            var list = new List<SimEvent>();
            for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type) list.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Bio = e.Bio, Cause = e.Cause, Radius = e.Radius, Slot = e.Slot });
            }
            return list;
        }

        /// <summary>Step the sim n ticks with the inputs as they stand (no bots).</summary>
        public static void Steps(World w, int n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        /// <summary>Run a bot match for ticks (actor 0 on autopilot), calling each(event) for every event pushed.</summary>
        public static void RunBots(World w, int ticks, Action<SimEvent> each = null)
        {
            InputFrame scratch = SimInput.EmptyInput();
            int cursor = w.EventHead;
            for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                if (each == null) continue;
                if (w.EventHead - cursor > World.EventCapacity) throw new InvalidOperationException("event ring overflowed inside one tick");
                for (; cursor < w.EventHead; cursor++) each(w.Events[cursor % World.EventCapacity]);
            }
        }
    }
}
