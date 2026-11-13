using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// Physics audit DG-80 / DG-111 runtime half (the settings themselves are written by ProjectSetup.Apply):
    /// <list type="bullet">
    /// <item>No sun shadows while the title screen is up (R-M10: the backdrop nobody plays drew a full shadow pass), and
    /// none at a quality level without a shadow map (QualityTable: Low); restored for the match.</item>
    /// <item>One boot line with the facts the integrator checks in Player.log: the audio DSP buffer (expect 512), whether
    /// the GPU can skin (compute shaders), the skinning mode the build asked for and frame timing stats.</item>
    /// </list>
    /// Self-installed (RuntimeInitializeOnLoadMethod, the pause menu's pattern); reads only, never writes the sim.
    /// </summary>
    public sealed class RenderBudget : MonoBehaviour
    {
        public const string LogTag = "[CM.Render]";

        static RenderBudget _instance;
        Light _sun;
        int _sceneHandle = -1;
        LightShadows _sunShadows = LightShadows.Hard;
        bool _shadowsCut;

        /// <summary>true while this component holds the sun's shadows off (tests, perf report).</summary>
        public static bool SunShadowsCut => _instance != null && _instance._shadowsCut;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("CriticalMass Render Budget");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<RenderBudget>();
            LogBootFacts();
        }

        /// <summary>The boot facts line (pure formatting of engine values).</summary>
        static void LogBootFacts()
        {
            AudioSettings.GetDSPBufferSize(out int dsp, out int buffers);
            Debug.Log($"{LogTag} dsp_buffer={dsp} dsp_buffers={buffers} compute_shaders={(SystemInfo.supportsComputeShaders ? 1 : 0)} " +
                      $"gpu_skinning_possible={(SystemInfo.supportsComputeShaders ? 1 : 0)} graphics={SystemInfo.graphicsDeviceType} " +
                      $"frame_timing={(FrameTimingManager.IsFeatureEnabled() ? 1 : 0)}");
        }

        /// <summary>Whether the sun casts now (pure; tests): never on the title, never without a shadow map.</summary>
        public static bool SunCasts(bool titleUp, in QualityRow row) => !titleUp && row.ShadowMap;

        void LateUpdate()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.handle != _sceneHandle || (_sun == null && Time.frameCount % 30 == 0))
            {
                _sceneHandle = scene.handle;
                _sun = FindSun();
                _shadowsCut = false;
                if (_sun != null) _sunShadows = _sun.shadows == LightShadows.None ? LightShadows.Hard : _sun.shadows;
            }
            if (_sun == null) return;
            bool cast = SunCasts(FrameGovernor.TitleUp, QualityTable.Current);
            LightShadows want = cast ? _sunShadows : LightShadows.None;
            if (_sun.shadows != want) _sun.shadows = want;
            _shadowsCut = !cast;
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return l;
            return null;
        }
    }
}
