using System;
using System.Reflection;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Spectator
{
    /// <summary>
    /// Live class switch (pause menu "CHANGE CLASS"): the pick is held here and applied on the next respawn, through the one
    /// path the sim already accepts, HumanInputState.QueueClassPick while actor 0 is down (Respawn.RequestClassChange
    /// drops a pick sent while standing). Tick sends it once actor 0 is down and actor 0 is on the human input (on
    /// autopilot the human frame never reaches the sim, so a pick there would be lost), re-sends it every 10 frames until
    /// the sim holds it (Actor.PendingClass), and clears it when actor 0 stands up as that class.
    /// <para>The picker is DG-10's ClassSelect when that screen is in the build (found by name, called by reflection, so
    /// this compiles without it): ExternalPickerAvailable, OpenExternalPicker, CloseExternalPicker. Without it the pause
    /// menu shows its own class page. SetRespawnPickerAuto turns ClassSelect's automatic respawn strip off while
    /// spectating (actor 0's knock-outs are the bot's then).</para>
    /// </summary>
    public static class ClassSwitch
    {
        public const string ClassSelectTypeName = "CriticalMass.Runtime.Menus.ClassSelect";
        const int ResendFrames = 10;

        static ClassId _pending;
        static int _nextSend;
        static bool _scanned;
        static Type _classSelect;

        /// <summary>A class waits for actor 0's next respawn.</summary>
        public static bool HasPending { get; private set; }
        public static ClassId Pending => _pending;

        /// <summary>Raised when a pick is accepted (Request) and when it is applied at the respawn (Applied).</summary>
        public static event Action<ClassId> Requested;
        public static event Action<ClassId> Applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            HasPending = false;
            _nextSend = 0;
            Requested = null;
            Applied = null;
        }

        /// <summary>Forget the held pick (a new match).</summary>
        public static void Clear() => HasPending = false;

        /// <summary>
        /// Hold cls for actor 0's next respawn. Returns false (nothing held) when actor 0 already plays cls with nothing
        /// else queued, or cls is not playable in this build (MatchSetup.Playable).
        /// </summary>
        public static bool Request(World w, ClassId cls)
        {
            if (!Menus.MatchSetup.Playable.Contains(cls)) return false;
            if (w != null && w.ActorCount > 0)
            {
                Actor a = w.Actors[0];
                if (a.Cls == cls && a.PendingClass == null && a.Alive)
                {
                    HasPending = false;
                    return false;
                }
            }
            _pending = cls;
            HasPending = true;
            _nextSend = 0;
            Requested?.Invoke(cls);
            return true;
        }

        /// <summary>One frame (SpectatorMode calls it): sends the held pick while actor 0 is down and clears it once applied.</summary>
        public static void Tick(SimDriverBehaviour driver, HumanInputSource input)
        {
            if (!HasPending || driver == null || driver.World == null || driver.World.ActorCount <= 0) return;
            Actor a = driver.World.Actors[0];
            if (a.Alive)
            {
                if (a.Cls == _pending && a.PendingClass == null)
                {
                    HasPending = false;
                    Applied?.Invoke(_pending);
                }
                return;
            }
            if (driver.HumanAutopilot || input == null) return;
            if ((a.PendingClass ?? a.Cls) == _pending) return;   // the sim holds it; applied at the respawn
            if (Time.frameCount < _nextSend) return;
            _nextSend = Time.frameCount + ResendFrames;
            input.State.QueueClassPick(_pending);
        }

        // ---------------------------------------------------------------- DG-10 ClassSelect bridge

        static Type ClassSelectType()
        {
            if (_scanned) return _classSelect;
            _scanned = true;
            _classSelect = typeof(ClassSwitch).Assembly.GetType(ClassSelectTypeName, false);
            return _classSelect;
        }

        static MethodInfo Method(string name, Type[] args)
        {
            Type t = ClassSelectType();
            return t != null ? t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null) : null;
        }

        /// <summary>DG-10's ClassSelect.ShowSetup(TeamId, Action&lt;ClassId&gt;, Action) is in this build.</summary>
        public static bool ExternalPickerAvailable =>
            Method("ShowSetup", new[] { typeof(TeamId), typeof(Action<ClassId>), typeof(Action) }) != null;

        /// <summary>Opens ClassSelect for team; onConfirm gets the class, onBack runs when the player backs out.</summary>
        public static bool OpenExternalPicker(TeamId team, Action<ClassId> onConfirm, Action onBack)
        {
            MethodInfo m = Method("ShowSetup", new[] { typeof(TeamId), typeof(Action<ClassId>), typeof(Action) });
            if (m == null) return false;
            try
            {
                m.Invoke(null, new object[] { team, onConfirm, onBack });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        public static void CloseExternalPicker()
        {
            MethodInfo m = Method("Hide", Type.EmptyTypes);
            if (m == null) return;
            try
            {
                m.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>ClassSelect.AutoRespawn (its respawn strip opening by itself), when that screen is in the build.</summary>
        public static void SetRespawnPickerAuto(bool on)
        {
            Type t = ClassSelectType();
            FieldInfo f = t != null ? t.GetField("AutoRespawn", BindingFlags.Public | BindingFlags.Static) : null;
            if (f != null && f.FieldType == typeof(bool)) f.SetValue(null, on);
        }
    }
}
