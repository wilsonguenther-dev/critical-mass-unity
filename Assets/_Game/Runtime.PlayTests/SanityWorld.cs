using System;
using System.Collections;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// ci/playmode-triage (P-04): wall-clock WAIT budgets in PlayMode tests (how long a navigation or load may take), scaled
    /// for slow machines. The Mac gate passes nothing (scale 1, budgets unchanged); unity-ci.yml passes
    /// <c>-cmTestTimeScale 4</c> because a 4-core GitHub runner frames several times slower than the owner's Mac. Only for
    /// waits whose length is not what the test checks; a product promise ("results within a second") is never scaled.
    /// </summary>
    internal static class TestClock
    {
        static float? _scale;

        public static float Scale
        {
            get
            {
                if (_scale.HasValue) return _scale.Value;
                float s = 1f;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-cmTestTimeScale" &&
                        float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v >= 1f)
                        s = v;
                _scale = s;
                return s;
            }
        }

        /// <summary>A wait budget in wall seconds on this machine.</summary>
        public static float Budget(float seconds) => seconds * Scale;
    }

    /// <summary>
    /// Gameplay sanity suite: the real Boot game with the wall clock stopped (TimeScale 0, as SmokeTests and
    /// HumanInputPlayTests), so a test advances exactly the sim ticks it steps and reads the views it would see on screen.
    /// With manual input the HumanInputSource component is disabled (its Update would overwrite the move and aim every
    /// frame) and the test writes its HumanInputState directly; SimDriverBehaviour still fills actor 0's frame from it.
    /// Read-only on the views; it writes the sim only through the input state, World.ForceHatch and Damage.ApplyDamage.
    /// </summary>
    internal sealed class SanityWorld
    {
        public const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        public SimDriverBehaviour Driver;
        public ActorViews Views;
        public HumanInputSource Source;
        public GameHud Hud;
        public World W => Driver.World;
        public GameConfig Cfg => Driver.Config;
        public HumanInputState HumanInput => Source.State;

        /// <summary>
        /// Loads Boot (actor 0 as humanClass when given, through the game flow's own match request), stops the clock and
        /// settles settleSeconds of sim time with no input so the spawn crowd has left the pad.
        /// </summary>
        public IEnumerator Load(ClassId? humanClass = null, bool manualInput = true, bool autopilot = false, double settleSeconds = 3)
        {
            if (humanClass.HasValue)
                GameFlow.QueueMatch(new MatchSetupChoice { Team = TeamId.Azure, Class = humanClass.Value, TeamSize = 16, Pace = 2 });
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            Driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(Driver, "Boot did not build a SimDriverBehaviour");
            Driver.TimeScale = 0;
            Driver.HumanAutopilot = autopilot;
            Views = Driver.GetComponent<ActorViews>();
            Source = Driver.GetComponent<HumanInputSource>();
            Hud = Driver.GetComponent<GameHud>();
            Assert.IsNotNull(Views, "Boot did not build ActorViews");
            Assert.IsNotNull(Source, "Boot did not build HumanInputSource");
            if (humanClass.HasValue)
                Assert.AreEqual(humanClass.Value, W.Actors[0].Cls, "the queued match did not make actor 0 a " + humanClass.Value);
            if (manualInput)
            {
                Source.enabled = false;
                Source.State.Reset();
                // ci/playmode-triage (P-01): the test IS the player here, so the human-control watchdog is off too. With
                // the input reader disabled on purpose, HumanControlGuard reports a LOCKOUT error after 2 s of WALL time
                // and its idle autopilot takes actor 0 after 45 s of wall time; both are unscaled clocks that the stopped
                // sim clock does not stop. On the Mac a sanity test finishes inside those 2 s; on a 4-core GitHub runner one
                // synchronous Tick(55) frame took 7.2 s, and RunningRaisesFootfalls / HudShows... failed on the "unhandled"
                // LOCKOUT log. HumanControlPlayTests (which keeps the reader on) still covers the guard.
                HumanControlGuard guard = Driver.GetComponent<HumanControlGuard>();
                if (guard != null) guard.enabled = false;
            }
            if (settleSeconds > 0) Driver.StepTicks((int)Math.Ceiling(settleSeconds * Cfg.SimHz));
            yield return null;
            Sync();
        }

        /// <summary>Back to the flow's first-boot state (QueueMatch changed it); call from every TearDown.</summary>
        public static void Reset() => GameFlow.ResetForTests();

        /// <summary>One view update now (the frame's LateUpdate does the same; this lets a test sample every tick).</summary>
        /// <summary>
        /// Sync the views. The PlayMode gate runs -nographics, where no camera renders, so ModelView's
        /// AnimatorCullingMode.CullCompletely would cull every model and leave its bones in the bind pose; the sanity
        /// checks measure bones, so every Animator here always animates (a test-only setting, the game is unchanged).
        /// </summary>
        public void Sync()
        {
            Views.Sync(0f);
            foreach (Animator a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                if (a.cullingMode != AnimatorCullingMode.AlwaysAnimate) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Views.Sync(0f);
        }

        /// <summary>n sim ticks, the views synced after each (poses advance with sim time).</summary>
        public void Tick(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                Driver.StepTicks(1);
                Sync();
            }
        }

        public int Ticks(double seconds) => (int)Math.Ceiling(seconds * Cfg.SimHz);

        // ------------------------------------------------------------------ where things are

        /// <summary>Actor i's sim position as the views draw it (previous to current tick at the driver's alpha).</summary>
        public void SimPos(int i, out double x, out double z)
        {
            double a = Driver.Alpha;
            x = W.PrevX[i] + (W.X[i] - W.PrevX[i]) * a;
            z = W.PrevZ[i] + (W.Z[i] - W.PrevZ[i]) * a;
        }

        public double Ground(double x, double z) => GroundHeight.At(W.Map, x, z);

        public Vector3 WorldPos(int i)
        {
            SimPos(i, out double x, out double z);
            return SimSpace.ToWorld(x, Ground(x, z), z);
        }

        public double Speed(int i) => Math.Sqrt(W.Vx[i] * W.Vx[i] + W.Vz[i] * W.Vz[i]);

        /// <summary>The active imported model drawn for actor i (ActorViews names it "Actor i Key"), or null.</summary>
        public Transform ActorModel(int i)
        {
            string prefix = "Actor " + i + " ";
            Transform root = Views.transform;
            for (int k = 0; k < root.childCount; k++)
            {
                Transform c = root.GetChild(k);
                if (c.gameObject.activeSelf && c.name.StartsWith(prefix, StringComparison.Ordinal)) return c;
            }
            return null;
        }

        /// <summary>The ArtMapping key of actor i's drawn model, or null.</summary>
        public string ActorModelKey(int i)
        {
            Transform m = ActorModel(i);
            return m == null ? null : m.name.Substring(("Actor " + i + " ").Length);
        }

        /// <summary>The active grey-box capsule drawn for actor i, or null.</summary>
        public Transform ActorCapsule(int i)
        {
            string name = "Actor " + i;
            Transform root = Views.transform;
            for (int k = 0; k < root.childCount; k++)
            {
                Transform c = root.GetChild(k);
                if (c.gameObject.activeSelf && c.name == name) return c;
            }
            return null;
        }

        /// <summary>The active hatched Kaiju model (hatchling or adult), or null.</summary>
        public Transform KaijuModel()
        {
            Transform root = Views.transform;
            for (int k = 0; k < root.childCount; k++)
            {
                Transform c = root.GetChild(k);
                if (c.gameObject.activeSelf && (c.name == "Kaiju Adult" || c.name == "Kaiju Hatchling")) return c;
            }
            return null;
        }

        /// <summary>The body renderer (ModelSpec.Meshes[0]) under a model, or null.</summary>
        public static Renderer Body(Transform model, ModelSpec spec)
        {
            if (model == null || spec == null || spec.Meshes.Length == 0) return null;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
                if (r.name == spec.Meshes[0]) return r;
            return null;
        }

        public static bool HasModel(string key) => key != null && ArtCatalog.Load()?.Find(key) != null;

        // ------------------------------------------------------------------ screen and sim directions

        /// <summary>A sim ground direction (unit) as screen-axis move intent (right, up): the inverse of HumanInputState.Fill.</summary>
        public static void ToScreenIntent(double dx, double dz, out double right, out double up)
        {
            right = dx * HumanInputState.ScreenRightX + dz * HumanInputState.ScreenRightZ;
            up = dx * HumanInputState.ScreenUpX + dz * HumanInputState.ScreenUpZ;
        }

        /// <summary>A screen-axis move intent (right, up) as the sim ground direction HumanInputState.Fill sends.</summary>
        public static void FromScreenIntent(double right, double up, out double dx, out double dz)
        {
            dx = right * HumanInputState.ScreenRightX + up * HumanInputState.ScreenUpX;
            dz = right * HumanInputState.ScreenRightZ + up * HumanInputState.ScreenUpZ;
        }

        /// <summary>Unit sim direction from actor i to the hill centre (open ground for motion tests).</summary>
        public void TowardHill(int i, out double dx, out double dz)
        {
            dx = W.Map.Hill.X - W.X[i];
            dz = W.Map.Hill.Z - W.Z[i];
            double l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-6) { dx = 1; dz = 0; return; }
            dx /= l;
            dz /= l;
        }

        public static string ScreenWord(int eighth)
        {
            switch (((eighth % 8) + 8) % 8)
            {
                case 0: return "screen right";
                case 1: return "screen up-right";
                case 2: return "screen up";
                case 3: return "screen up-left";
                case 4: return "screen left";
                case 5: return "screen down-left";
                case 6: return "screen down";
                default: return "screen down-right";
            }
        }
    }
}
