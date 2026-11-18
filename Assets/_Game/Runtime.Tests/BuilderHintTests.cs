using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b15 small fix: next to a knock-down pillar the Builder's bottom hint still names his tool.</summary>
    public sealed class BuilderHintTests
    {
        const int Me = 0;

        static World Near(ClassId cls, out int pillar)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = GameConfig.CreateDefault() });
            for (int j = 1; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; }
            w.Actors[Me].Alive = true;
            w.Match.Phase = MatchPhase.Playing;
            w.Tick = 100;
            pillar = 0;
            DecorationDef[] ds = w.Map.Decorations;
            if (ds == null || w.Topple == null) return w;
            for (int k = 0; k < ds.Length && pillar == 0; k++)
            {
                if (k < w.Topple.Fallen.Length && w.Topple.Fallen[k] != Topple.Standing) continue;
                w.X[Me] = ds[k].Cx + 0.5 + 1.5;
                w.Z[Me] = ds[k].Cz + 0.5;
                pillar = TerrainPrompt.PillarModeHere(w, Me);
            }
            return w;
        }

        [Test]
        public void TheBuilderNextToAPillarSeesHisToolNotTheKnockDownLine()
        {
            World w = Near(ClassId.Builder, out int pillar);
            Assume.That(pillar, Is.Not.EqualTo(0), "the default map has a standing pillar to stand next to");
            int key = TerrainPrompt.StateKey(w);
            bool toolStrip = key >= 0 && (key & (7 << 16)) == (3 << 16);
            Debug.Log("[BuilderHint] Builder next to a pillar (mode " + pillar + "): key " + key + ", tool strip " + toolStrip + " (want True)");
            Assert.IsTrue(toolStrip, "the Builder's strip names his tool next to a pillar");

            World v = Near(ClassId.Warrior, out int p2);
            Assume.That(p2, Is.Not.EqualTo(0));
            int k2 = TerrainPrompt.StateKey(v);
            Debug.Log("[BuilderHint] Warrior next to the same pillar: key " + k2 + ", pillar line " + ((k2 & 3) != 0) + " (want True)");
            Assert.AreNotEqual(0, k2 & 3, "everyone else still sees which way the pillar falls");
        }
    }
}
