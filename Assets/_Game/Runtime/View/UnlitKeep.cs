using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// fix/px-rough-spots (item 6, the thin bright-magenta lines at the fighter's feet in the shipped film, 0:08 / 3:00 / 3:35 /
    /// 7:30): the dodge streaks, the height pips and the moves meter / heavy telegraph made their material from
    /// Shader.Find("Universal Render Pipeline/Unlit"). No asset in the build used that shader, so the player build stripped it
    /// (the 1.0.1690 data files hold no "Universal Render Pipeline/Unlit" at all), Shader.Find returned null, the material
    /// was null and Unity drew those quads with its magenta error shader. Two materials in Resources/UnlitKeep (opaque and
    /// transparent) keep the shader and both its variants in every build; the views take the shader from them.
    /// </summary>
    public static class UnlitKeep
    {
        public const string OpaquePath = "UnlitKeep/CMUnlitKeepOpaque", TransparentPath = "UnlitKeep/CMUnlitKeepTransparent";

        public static UnityEngine.Shader Get()
        {
            Material m = Resources.Load<Material>(TransparentPath);
            if (m == null) m = Resources.Load<Material>(OpaquePath);
            if (m != null && m.shader != null && m.shader.isSupported) return m.shader;
            UnityEngine.Shader sh = UnityEngine.Shader.Find("Universal Render Pipeline/Unlit");
            return sh != null ? sh : UnityEngine.Shader.Find("Unlit/Color");
        }
    }
}
