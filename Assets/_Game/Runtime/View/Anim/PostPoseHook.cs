using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
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
    /// Integration batch 3: the animation pass's ActorViews.PostPose is on main, so this no longer dispatches anything; only
    /// the model-name parser remains (tests). Former note: stand-in driver for contract C3 until DG-71 merges: after ActorViews' LateUpdate (order 0) and before the camera
    /// brain (100), it walks ActorViews' active models ("Actor i Key", "Kaiju Adult", "Kaiju Hatchling": the names the
    /// sanity suite reads too) and dispatches them to PostPoseHub. Allocation-free (manual name parse). With
    /// CM_HAS_POST_POSE defined it does nothing.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class PostPoseShim : MonoBehaviour
    {
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
