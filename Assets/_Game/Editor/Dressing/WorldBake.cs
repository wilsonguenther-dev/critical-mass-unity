using System;
using System.IO;
using System.Linq;
using System.Text;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor.Dressing
{
    /// <summary>
    /// The world dresser's editor side (2026-11-12). Bake writes tools/art/world-plan.tsv: the plan fingerprint
    /// (WorldPlan.Hash) and the placement count per piece for the Skybreak Basin map, so a change to the map, the kits or
    /// the rules shows up as a reviewable diff (the EditMode test fails when the committed file is stale). The menu item
    /// bakes the dressed world into the open scene as GameObjects for inspection. The game itself dresses at run time from
    /// the running Sim's map (WorldView), because the grid changes in play (dig, place, breakables); the plan is a pure
    /// function of the map, so both are the same world.
    /// </summary>
    public static class WorldBake
    {
        public const string PlanTsv = "tools/art/world-plan.tsv";

        public static WorldPlan PlanSkybreak() =>
            WorldDresser.Plan(SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault()), BasesFile.Load(), WorldKitCatalog.Load().Centroid, WorldKitCatalog.Load().Body);

        public static string PlanText(WorldPlan plan) => plan.Summary();

        /// <summary>Batch entry: write tools/art/world-plan.tsv; exit 0.</summary>
        public static void Bake()
        {
            try
            {
                WorldPlan plan = PlanSkybreak();
                string root = Path.GetDirectoryName(Application.dataPath);
                File.WriteAllText(Path.Combine(root, PlanTsv), PlanText(plan));
                Debug.Log($"[WorldBake] {plan.Placements.Count} placements, hash {plan.Hash()} -> {PlanTsv}");
                foreach (string note in plan.Notes) Debug.Log("[WorldBake] note: " + note);
                foreach (string dropped in plan.DroppedBases) Debug.Log("[WorldBake] dropped: " + dropped);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        [MenuItem("Critical Mass/World/Bake Dressed World Into Open Scene")]
        public static void BakeIntoScene()
        {
            WorldPlan plan = PlanSkybreak();
            WorldKitCatalog cat = WorldKitCatalog.Load();
            if (cat == null) throw new InvalidOperationException("WorldKitCatalog missing: run tools/art-sync.sh");
            var root = new GameObject("World Art (baked " + plan.Hash() + ")");
            foreach (WorldPlacement p in plan.Placements)
            {
                GameObject model = cat.Find(p.Piece);
                if (model == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                go.transform.SetPositionAndRotation(p.Matrix.GetColumn(3), p.Matrix.rotation);
                go.transform.localScale = p.Matrix.lossyScale;
            }
            Undo.RegisterCreatedObjectUndo(root, "Bake dressed world");
            Debug.Log($"[WorldBake] baked {plan.Placements.Count} placements into the open scene");
        }
    }
}
