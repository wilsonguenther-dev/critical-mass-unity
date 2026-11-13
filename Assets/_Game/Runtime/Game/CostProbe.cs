using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// Closer (integration batch 4), diagnostics only: with -costProbe on the command line (never in a normal launch), once
    /// the title has shown for 3 s it turns off every instance of one MonoBehaviour type at a time for a few frames and logs
    /// how much script time (first Update to last LateUpdate) each type costs on the title ([CM.CostProbe]). A release
    /// player has no profiler; this finds the type that eats an idle frame in one hidden run.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class CostProbe : MonoBehaviour
    {
        const int Frames = 16;
        static readonly HashSet<string> Skip = new HashSet<string>
        {
            nameof(CostProbe), nameof(CostProbeStart), "EventSystem", "InputSystemUIInputModule", "HumanInputSource",
            "AutoplayRunner", "AutoplayArgs", "SessionKeeper", "PerfOverlay",
        };

        long _start;
        float _wait = 3f;
        readonly List<Type> _types = new List<Type>();
        int _index = -1, _frame;
        double _base, _off;
        readonly List<(string name, double ms)> _results = new List<(string, double)>();
        readonly List<MonoBehaviour> _disabled = new List<MonoBehaviour>();
        bool _done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Install()
        {
            if (!Environment.CommandLine.Contains("-costProbe")) return;
            var go = new GameObject("CM Cost Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<CostProbeStart>();
            go.AddComponent<CostProbe>();
        }

        internal void Mark() => _start = Stopwatch.GetTimestamp();

        void LateUpdate()
        {
            if (_done) return;
            double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
            if (_wait > 0f)
            {
                if (FindAnyObjectByType<Hud.TitleScreen>() != null) _wait -= Time.unscaledDeltaTime;
                if (_wait <= 0f) Collect();
                return;
            }
            _frame++;
            int phase = _frame % (2 * Frames);
            if (phase < Frames) { if (phase > 2) _base += ms; }
            else if (phase - Frames > 2) _off += ms;
            if (phase == Frames - 1) Disable(_index);
            if (phase == 2 * Frames - 1)
            {
                Enable();
                if (_index >= 0) _results.Add((_types[_index].Name, (_base - _off) / (Frames - 3)));
                _base = _off = 0;
                _index++;
                if (_index >= _types.Count) Finish();
            }
        }

        void Collect()
        {
            var seen = new HashSet<Type>();
            foreach (MonoBehaviour m in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Type t = m.GetType();
                if (m.enabled && !Skip.Contains(t.Name) && t.Namespace != null && !t.Namespace.StartsWith("UnityEngine") && seen.Add(t)) _types.Add(t);
            }
            _index = 0;
            UnityEngine.Debug.Log("[CM.CostProbe] probing " + _types.Count + " types");
        }

        void Disable(int i)
        {
            if (i < 0 || i >= _types.Count) return;
            foreach (UnityEngine.Object o in FindObjectsByType(_types[i], FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (o is MonoBehaviour m && m.enabled) { m.enabled = false; _disabled.Add(m); }
        }

        void Enable()
        {
            foreach (MonoBehaviour m in _disabled) if (m != null) m.enabled = true;
            _disabled.Clear();
        }

        void Finish()
        {
            _done = true;
            _results.Sort((a, b) => b.ms.CompareTo(a.ms));
            var sb = new System.Text.StringBuilder("[CM.CostProbe] script ms saved per frame with the type off:");
            for (int k = 0; k < Math.Min(25, _results.Count); k++) sb.Append($"\n  {_results[k].name} {_results[k].ms:0.00}");
            UnityEngine.Debug.Log(sb.ToString());
        }
    }

    [DefaultExecutionOrder(-32000)]
    public sealed class CostProbeStart : MonoBehaviour
    {
        CostProbe _probe;
        void Update()
        {
            if (_probe == null) _probe = GetComponent<CostProbe>();
            _probe.Mark();
        }
    }
}
