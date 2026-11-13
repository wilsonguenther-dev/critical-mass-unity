using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>What an actor's (or the Kaiju's) pose was built from this frame, handed to post-pose listeners.</summary>
    public struct ActorPoseState
    {
        /// <summary>Actor id, or -2 for the Kaiju.</summary>
        public int Actor;
        public int Tick;
        public double Alpha;
        public double SimTime;
        /// <summary>Sim seconds since the previous view update (0 while the clock is stopped or on a repeated sync).</summary>
        public double DeltaSim;
        public TeamId Team;
        public ClassId Cls;
        public bool IsKaiju;
        /// <summary>False when the animation level of detail skipped the graph this frame (the bones hold last frame's pose).</summary>
        public bool Evaluated;
        public bool Corpse;
        public bool Airborne;
        /// <summary>Rendered velocity (Unity XZ, u per sim second) and speed, from the drawn displacement.</summary>
        public Vector3 Velocity;
        public float Speed;
        /// <summary>The smoothed yaw the model is drawn at (degrees) and its rate (degrees per sim second).</summary>
        public float Yaw;
        public float YawRate;
        public CarryKind Carry;
        public int CarryAmount;
        /// <summary>Last hit: sim seconds since it (large when none), its tier and the Unity direction it pushed.</summary>
        public double HitAge;
        public HitTier HitTier;
        public Vector3 HitDir;
        /// <summary>0 none, 1 attack, 2 ability; and sim seconds since it began.</summary>
        public byte ActionKind;
        public double ActionAge;
    }

    /// <summary>
    /// Contract C3 (DG-71): anything that rides on bones (spring bones DG-79, carried cargo, death launch DG-88, hand-bone
    /// trails DG-105) registers here and is called once per drawn actor after its ModelView.Evaluate and before render.
    /// Listeners live in their own files; nobody else edits ActorViews.cs or ModelView.cs.
    /// </summary>
    public interface IPostPoseListener
    {
        void OnPostPose(int actor, ModelView mv, in ActorPoseState s);
    }

    /// <summary>The listener registry behind ActorViews.PostPose: a fixed array, no allocation per call.</summary>
    public static class PostPoseHooks
    {
        public const int MaxListeners = 8;
        static readonly IPostPoseListener[] _list = new IPostPoseListener[MaxListeners];
        static int _count;

        public static int Count => _count;

        public static void Register(IPostPoseListener l)
        {
            if (l == null) return;
            for (int i = 0; i < _count; i++) if (ReferenceEquals(_list[i], l)) return;
            if (_count < MaxListeners) _list[_count++] = l;
            else Debug.LogWarning("[PostPose] listener list full (" + MaxListeners + ")");
        }

        public static void Unregister(IPostPoseListener l)
        {
            for (int i = 0; i < _count; i++)
                if (ReferenceEquals(_list[i], l))
                {
                    _list[i] = _list[--_count];
                    _list[_count] = null;
                    return;
                }
        }

        public static void Dispatch(int actor, ModelView mv, in ActorPoseState s)
        {
            for (int i = 0; i < _count; i++) _list[i].OnPostPose(actor, mv, in s);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForTests()
        {
            for (int i = 0; i < MaxListeners; i++) _list[i] = null;
            _count = 0;
        }
    }
}
