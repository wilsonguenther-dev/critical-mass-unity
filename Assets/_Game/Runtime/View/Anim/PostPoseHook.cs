using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
#if !CM_HAS_POST_POSE
    // Contract C3 (physics build list 2026-11-12): the animation lane's DG-71 adds ActorViews.PostPose(int actor, ModelView
    // mv, in ActorPoseState s), dispatched to IPostPoseListener after ModelView.Evaluate and before render. Until that PR
    // merges, these two local definitions stand in (same names, namespace and signature). When DG-71 is on main, define
    // CM_HAS_POST_POSE (Player Settings > Scripting Define Symbols): these drop out, DG-71's types are used, the shim
    // driver below stops dispatching, and ActorViews.PostPose must also call PostPoseHub.Dispatch (one line, or register
    // PostPoseHub.Listeners in DG-71's registry). Nothing here reads ActorPoseState's fields, so any shape DG-71 gives
    // the struct compiles.

    /// <summary>Local stand-in for DG-71's per-actor pose state (no fields are read by the world-reactions lane).</summary>
    public struct ActorPoseState
    {
        public bool Corpse;
    }

    /// <summary>Local stand-in for DG-71's post-pose listener (contract C3).</summary>
    public interface IPostPoseListener
    {
        void PostPose(int actor, ModelView mv, in ActorPoseState s);
    }
#endif

    /// <summary>What the world-reactions lane needs from a posed model: the actor (-2 the Kaiju), its root and model key.</summary>
    public interface IPostPoseRootListener
    {
        void PostPoseRoot(int actor, Transform root, string modelKey);
    }

    /// <summary>
    /// The registry the world-reactions listeners (CharacterSprings, CargoView) join. Dispatch is called once per posed
    /// model per frame, after its Evaluate: by PostPoseShim until DG-71 merges, then from ActorViews.PostPose.
    /// </summary>
    public static class PostPoseHub
    {
        public const int KaijuActor = -2;
        private static readonly List<IPostPoseRootListener> _listeners = new List<IPostPoseRootListener>(4);

        public static IReadOnlyList<IPostPoseRootListener> Listeners => _listeners;

        public static void Register(IPostPoseRootListener l)
        {
            if (l != null && !_listeners.Contains(l)) _listeners.Add(l);
        }

        public static void Unregister(IPostPoseRootListener l) => _listeners.Remove(l);

        public static void Dispatch(int actor, Transform root, string modelKey)
        {
            if (root == null) return;
            for (int i = 0; i < _listeners.Count; i++) _listeners[i].PostPoseRoot(actor, root, modelKey);
        }

        /// <summary>The C3 entry point for DG-71's hook: forwards to every registered listener.</summary>
        public static void Dispatch(int actor, ModelView mv)
        {
            if (mv == null) return;
            Dispatch(actor, mv.Transform, mv.Entry != null ? mv.Entry.Key : null);
        }

        /// <summary>Finds a joint by exact name (case-insensitive), else one whose name ends with it ("Armature_Tail1").</summary>
        public static Transform FindJoint(Transform root, string name)
        {
            if (root == null) return null;
            Transform suffix = null;
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
                if (suffix == null && t.name.EndsWith(name, System.StringComparison.OrdinalIgnoreCase)) suffix = t;
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            }
            return suffix;
        }
    }

    /// <summary>
    /// Stand-in driver for contract C3 until DG-71 merges: after ActorViews' LateUpdate (order 0) and before the camera
    /// brain (100), it walks ActorViews' active models ("Actor i Key", "Kaiju Adult", "Kaiju Hatchling": the names the
    /// sanity suite reads too) and dispatches them to PostPoseHub. Allocation-free (manual name parse). With
    /// CM_HAS_POST_POSE defined it does nothing.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class PostPoseShim : MonoBehaviour
    {
        private ActorViews _views;

        private void LateUpdate()
        {
#if !CM_HAS_POST_POSE
            if (PostPoseHub.Listeners.Count == 0) return;
            if (_views == null) _views = GetComponent<ActorViews>();
            if (_views == null || !_views.isActiveAndEnabled) return;
            Transform root = _views.transform;
            for (int k = 0; k < root.childCount; k++)
            {
                Transform c = root.GetChild(k);
                if (!c.gameObject.activeSelf) continue;
                string n = c.name;
                if (n == "Kaiju Adult") { PostPoseHub.Dispatch(PostPoseHub.KaijuActor, c, ArtMapping.KaijuAdult); continue; }
                if (n == "Kaiju Hatchling") { PostPoseHub.Dispatch(PostPoseHub.KaijuActor, c, ArtMapping.KaijuHatchling); continue; }
                int actor = ParseActor(n);
                if (actor >= 0) PostPoseHub.Dispatch(actor, c, null);
            }
#endif
        }

        /// <summary>"Actor 12 Warrior" gives 12; the grey-box capsule "Actor 12" and anything else give -1.</summary>
        public static int ParseActor(string n)
        {
            const string prefix = "Actor ";
            if (n == null || n.Length < prefix.Length + 2 || !n.StartsWith(prefix, System.StringComparison.Ordinal)) return -1;
            int i = prefix.Length, v = 0, digits = 0;
            while (i < n.Length && n[i] >= '0' && n[i] <= '9' && digits < 6)
            {
                v = v * 10 + (n[i] - '0');
                i++;
                digits++;
            }
            return digits > 0 && i < n.Length && n[i] == ' ' ? v : -1;
        }
    }
}
