// CM b13 S0: the batch 13 item views, created from the same root that creates DepthWorldView (MechanicsWorldView.EnsureOn).
using CriticalMass.Runtime.Game;
using UnityEngine;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// CM b13 S0: one child GameObject of the driver ("B13 Item Views") carries one world-space view per batch 13 item:
    /// HutView [H], WallView [B], DodgeView [J], KaijuTypeView [K], CorruptionView [C]. Each item fills its own view file;
    /// HUD (screen-space) work is V's. Created once per driver; the views go with the driver.
    /// </summary>
    public static class B13Views
    {
        public const string RootName = "B13 Item Views";

        /// <summary>Adds the item views under the driver unless they exist; returns their GameObject.</summary>
        public static GameObject EnsureOn(SimDriverBehaviour driver)
        {
            if (driver == null) return null;
            HutView existing = driver.GetComponentInChildren<HutView>(true);
            if (existing != null) return existing.gameObject;
            var go = new GameObject(RootName);
            go.transform.SetParent(driver.transform, false);
            go.AddComponent<HutView>();
            go.AddComponent<WallView>();
            go.AddComponent<DodgeView>();
            go.AddComponent<KaijuTypeView>();
            go.AddComponent<CorruptionView>();
            return go;
        }
    }
}
