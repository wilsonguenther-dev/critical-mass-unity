// Editor "eyes" helpers for Critical Mass. Lives OUTSIDE Assets/ so it never compiles into the project or a player.
// Run with:  unity command script run --project-path ~/cm-editor -- --file ~/cm-editor-eyes/scripts/Eyes.cs --entry Eyes.<Method> --args '[...]'
// Read-only except Look/EyesCam* (Scene view pose, one temporary camera named "Eyes Cam") and Press (the human's own input path).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Eyes
{
    static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

    static string Comps(GameObject g) =>
        string.Join(", ", g.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));

    static void Walk(Transform t, int depth, StringBuilder sb, int maxDepth, int maxKids)
    {
        sb.Append(' ', depth * 2).Append(t.name);
        if (!t.gameObject.activeInHierarchy) sb.Append(" (inactive)");
        string c = Comps(t.gameObject);
        if (c.Length > 0) sb.Append("  [").Append(c).Append(']');
        if (t.childCount > 0) sb.Append("  children=").Append(t.childCount);
        sb.Append('\n');
        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++)
        {
            if (i >= maxKids) { sb.Append(' ', depth * 2 + 2).Append("... ").Append(t.childCount - maxKids).Append(" more\n"); break; }
            Walk(t.GetChild(i), depth + 1, sb, maxDepth, maxKids);
        }
    }

    static List<GameObject> Roots(StringBuilder sb)
    {
        var all = new List<GameObject>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene sc = SceneManager.GetSceneAt(s);
            var r = sc.GetRootGameObjects();
            sb?.Append("# scene '").Append(sc.name).Append("' path=").Append(sc.path).Append(" roots=").Append(r.Length).Append('\n');
            all.AddRange(r);
        }
        if (Application.isPlaying)
        {
            var probe = new GameObject("eyes-probe");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            var r = probe.scene.GetRootGameObjects().Where(g => g != probe).ToArray();
            sb?.Append("# scene 'DontDestroyOnLoad' roots=").Append(r.Length).Append('\n');
            all.AddRange(r);
            UnityEngine.Object.DestroyImmediate(probe);
        }
        return all;
    }

    /// <summary>Whole hierarchy of every loaded scene (plus DontDestroyOnLoad in Play mode): names, active, components.</summary>
    public static string Hierarchy(string outPath, int maxDepth, int maxKids)
    {
        var sb = new StringBuilder();
        sb.Append("# Eyes.Hierarchy playing=").Append(Application.isPlaying).Append(" frame=").Append(Time.frameCount).Append('\n');
        foreach (GameObject g in Roots(sb)) Walk(g.transform, 0, sb, maxDepth, maxKids);
        File.WriteAllText(outPath, sb.ToString());
        return $"wrote {outPath} ({sb.Length} chars)";
    }

    static IEnumerable<Transform> AllTransforms()
    {
        foreach (GameObject r in Roots(null))
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true)) yield return t;
    }

    static void Material(StringBuilder sb, string ind, Material m)
    {
        if (m == null) { sb.Append(ind).Append("material: NONE\n"); return; }
        Shader sh = m.shader;
        sb.Append(ind).Append("material: '").Append(m.name).Append("' shader='").Append(sh != null ? sh.name : "null")
          .Append("' queue=").Append(m.renderQueue).Append(" keywords=[").Append(string.Join(" ", m.shaderKeywords)).Append("]\n");
        if (sh == null) return;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            string n = sh.GetPropertyName(i);
            switch (sh.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    Color c = m.GetColor(n);
                    sb.Append(ind).Append("  ").Append(n).Append(" = #").Append(ColorUtility.ToHtmlStringRGBA(c)).Append('\n'); break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    sb.Append(ind).Append("  ").Append(n).Append(" = ").Append(m.GetFloat(n).ToString("0.###")).Append('\n'); break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    sb.Append(ind).Append("  ").Append(n).Append(" = ").Append(m.GetVector(n)).Append('\n'); break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    Texture tx = m.GetTexture(n);
                    sb.Append(ind).Append("  ").Append(n).Append(" = ").Append(tx != null ? tx.name + " " + tx.width + "x" + tx.height : "none").Append('\n'); break;
            }
        }
    }

    static void InspectOne(Transform t, int depth, StringBuilder sb, int maxDepth)
    {
        string ind = new string(' ', depth * 2);
        sb.Append(ind).Append("== ").Append(t.name).Append(t.gameObject.activeInHierarchy ? "" : " (inactive)")
          .Append("  layer=").Append(LayerMask.LayerToName(t.gameObject.layer)).Append('\n');
        sb.Append(ind).Append("  localPos=").Append(V(t.localPosition)).Append(" localEuler=").Append(V(t.localEulerAngles))
          .Append(" localScale=").Append(V(t.localScale)).Append('\n');
        sb.Append(ind).Append("  worldPos=").Append(V(t.position)).Append(" lossyScale=").Append(V(t.lossyScale)).Append('\n');
        sb.Append(ind).Append("  components: ").Append(Comps(t.gameObject)).Append('\n');
        var mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Mesh me = mf.sharedMesh;
            int tris = 0; for (int s = 0; s < me.subMeshCount; s++) tris += (int)me.GetIndexCount(s) / 3;
            sb.Append(ind).Append("  mesh: '").Append(me.name).Append("' verts=").Append(me.vertexCount).Append(" tris=").Append(tris)
              .Append(" submeshes=").Append(me.subMeshCount).Append(" colors=").Append(me.colors32.Length > 0).Append(" uv3=")
              .Append(me.uv4.Length > 0 || me.uv3.Length > 0).Append(" bounds=").Append(me.bounds).Append('\n');
        }
        var r = t.GetComponent<Renderer>();
        if (r != null)
        {
            sb.Append(ind).Append("  renderer: enabled=").Append(r.enabled).Append(" shadows=").Append(r.shadowCastingMode)
              .Append(" receive=").Append(r.receiveShadows).Append(" worldBounds=").Append(r.bounds).Append(" hasPropertyBlock=").Append(r.HasPropertyBlock()).Append('\n');
            foreach (Material m in r.sharedMaterials) Material(sb, ind + "  ", m);
        }
        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++) InspectOne(t.GetChild(i), depth + 1, sb, maxDepth);
    }

    /// <summary>Inspector-style dump (transform, mesh, renderer, every material property) of up to maxHits objects whose
    /// name starts with prefix, with their children to maxDepth.</summary>
    public static string Inspect(string prefix, string outPath, int maxHits, int maxDepth)
    {
        var sb = new StringBuilder();
        sb.Append("# Eyes.Inspect '").Append(prefix).Append("' playing=").Append(Application.isPlaying).Append(" frame=").Append(Time.frameCount).Append('\n');
        int n = 0;
        foreach (Transform t in AllTransforms())
        {
            if (!t.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (n++ >= maxHits) break;
            InspectOne(t, 0, sb, maxDepth);
            sb.Append('\n');
        }
        sb.Append("# hits=").Append(n).Append('\n');
        File.AppendAllText(outPath, sb.ToString());
        return $"{n} hit(s) for '{prefix}' -> {outPath}";
    }

    /// <summary>World position of the first object named exactly name (active or not), or "missing".</summary>
    public static string Where(string name)
    {
        foreach (Transform t in AllTransforms()) if (t.name == name) return V(t.position);
        return "missing";
    }

    /// <summary>Pose the Scene view: pivot (x,y,z), pitch/yaw in degrees, size = half-height of the view in metres.</summary>
    public static string Look(float x, float y, float z, float pitch, float yaw, float size)
    {
        SceneView sv = SceneView.lastActiveSceneView ?? (SceneView.sceneViews.Count > 0 ? (SceneView)SceneView.sceneViews[0] : null);
        if (sv == null) return "no Scene view open";
        sv.orthographic = false;
        Quaternion q = Quaternion.Euler(pitch, yaw, 0f);
        sv.LookAtDirect(new Vector3(x, y, z), q, size);
        // A Scene view tab hidden behind the Game tab never repaints, so its camera keeps the old pose; move the camera
        // to the pose LookAtDirect implies (distance = size / tan(fov/2)) so `capture scene_view` renders it now.
        Camera c = sv.camera;
        float dist = size / Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
        c.transform.SetPositionAndRotation(new Vector3(x, y, z) - q * Vector3.forward * dist, q);
        sv.Repaint();
        return "scene view at " + V(sv.pivot) + " size " + size;
    }

    /// <summary>A temporary camera "Eyes Cam" copying the gameplay camera's lens, looking at (x,y,z) from pitch/yaw at
    /// distance d. Capture it with: capture game_view --source camera --camera "Eyes Cam". Remove with EyesCamOff.</summary>
    public static string EyesCam(float x, float y, float z, float pitch, float yaw, float d)
    {
        Camera main = Camera.main;
        // Unity objects: never use ?? (it skips Unity's fake-null check).
        GameObject g = GameObject.Find("Eyes Cam");
        if (g == null) g = new GameObject("Eyes Cam");
        Camera c = g.GetComponent<Camera>();
        if (c == null) c = g.AddComponent<Camera>();
        if (main != null) { c.CopyFrom(main); }
        c.enabled = false; // rendered only on capture; never steals the Game view
        Quaternion q = Quaternion.Euler(pitch, yaw, 0f);
        g.transform.rotation = q;
        g.transform.position = new Vector3(x, y, z) - q * Vector3.forward * d;
        return "Eyes Cam at " + V(g.transform.position) + " fov " + c.fieldOfView + " copiedFrom=" + (main != null ? main.name : "none");
    }

    public static string EyesCamOff()
    {
        GameObject g = GameObject.Find("Eyes Cam");
        if (g == null) return "none";
        UnityEngine.Object.DestroyImmediate(g);
        return "removed";
    }

    /// <summary>The gameplay camera's name, pose and lens.</summary>
    public static string MainCam()
    {
        Camera c = Camera.main;
        if (c == null) return "no Camera.main";
        return $"{c.name} pos={V(c.transform.position)} euler={V(c.transform.eulerAngles)} fov={c.fieldOfView} ortho={c.orthographic} px={c.pixelWidth}x{c.pixelHeight}";
    }

    /// <summary>Every enabled renderer whose world bounds cover the vertical column at world (x, z) within half a cell:
    /// what is actually drawn over / under a grid cell (name path, material, shader, bounds y range).</summary>
    public static string Column(float x, float z, string outPath)
    {
        var sb = new StringBuilder();
        sb.Append("# Eyes.Column at world (").Append(x).Append(", ").Append(z).Append(") frame=").Append(Time.frameCount).Append('\n');
        int n = 0;
        foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds b = r.bounds;
            if (x < b.min.x - 0.5f || x > b.max.x + 0.5f || z < b.min.z - 0.5f || z > b.max.z + 0.5f) continue;
            if (b.size.x > 60f || b.size.z > 60f) { sb.Append("  (huge) "); }
            string path = r.name; for (Transform t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            Material m = r.sharedMaterial;
            sb.Append("  ").Append(path).Append("  y[").Append(b.min.y.ToString("0.00")).Append(", ").Append(b.max.y.ToString("0.00"))
              .Append("] size=").Append(V(b.size)).Append(" mat='").Append(m != null ? m.name : "none").Append("' shader='")
              .Append(m != null && m.shader != null ? m.shader.name : "none").Append("' type=").Append(r.GetType().Name).Append('\n');
            n++;
        }
        sb.Append("# renderers=").Append(n).Append('\n');
        File.AppendAllText(outPath, sb.ToString());
        return n + " renderers over the column -> " + outPath;
    }
}
