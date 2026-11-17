using System;
using UnityEngine;

namespace CriticalMass.Runtime.Audio
{
    /// <summary>
    /// CM b13 S0 (audio): the master bus. Measured while recording a match (PR #192), the game's own mix peaked about +6 dB
    /// over full scale after the Kaiju hatches: the hatch cue (gain 1.0), the Kaiju stinger (0.85, the Stinger bus is never
    /// ducked), the roar (0.9) and the music bed all land together, every one of them rendered near full scale, and nothing
    /// sat on the master: no headroom and no limiter, so the float mix clipped at the output.
    /// <para>The fix is this bus on the final mix: <see cref="HeadroomGain"/> (-1 dB) first, then a look-ahead peak limiter
    /// with its ceiling at <see cref="Ceiling"/> (-1 dBFS). Gain falls in the 1.5 ms before a peak (no clipped transient)
    /// and recovers over <see cref="ReleaseSeconds"/>. Normal play only loses the 1 dB of headroom (plus brief limiting on
    /// its loudest stacks); the hatch moment is held at -1 dBFS. Channels are linked (one gain for every channel, so the
    /// stereo image never shifts). Pure and allocation-free after construction (MasterBusTests).</para>
    /// </summary>
    public sealed class MasterLimiter
    {
        /// <summary>-1 dBFS as a linear gain.</summary>
        public const float Ceiling = 0.8912509f;
        /// <summary>-1 dB of master headroom ahead of the limiter.</summary>
        public const float HeadroomGain = 0.8912509f;
        public const double LookaheadSeconds = 0.0015;
        public const double ReleaseSeconds = 0.15;
        public const int MaxChannels = 8;
        const int MaxLookahead = 1024;   // frames: 1.5 ms up to 682 kHz

        readonly float[] _delay = new float[(MaxLookahead + 1) * MaxChannels];
        readonly float[] _dqGain = new float[MaxLookahead + 2];
        readonly long[] _dqFrame = new long[MaxLookahead + 2];
        int _dqHead, _dqCount;
        long _frame;
        int _rate = -1, _look, _channels = -1;
        float _release, _env = 1f;

        /// <summary>Largest input peak (after headroom) and smallest gain since the last <see cref="TakeStats"/>.</summary>
        public float PeakIn { get; private set; }
        public float MinGain { get; private set; } = 1f;

        public void TakeStats(out float peakIn, out float minGain)
        {
            peakIn = PeakIn;
            minGain = MinGain;
            PeakIn = 0f;
            MinGain = 1f;
        }

        /// <summary>The latency in frames at this sample rate (the look-ahead).</summary>
        public static int LookaheadFrames(int sampleRate) => Math.Max(1, Math.Min(MaxLookahead, (int)Math.Round(sampleRate * LookaheadSeconds)));

        void Configure(int channels, int sampleRate)
        {
            _channels = channels;
            _rate = sampleRate;
            _look = LookaheadFrames(sampleRate);
            _release = (float)(1.0 - Math.Exp(-1.0 / (ReleaseSeconds * sampleRate)));
            Array.Clear(_delay, 0, _delay.Length);
            _dqHead = 0;
            _dqCount = 0;
            _frame = 0;
            _env = 1f;
        }

        /// <summary>Processes interleaved samples in place (the audio thread's OnAudioFilterRead buffer).</summary>
        public void Process(float[] data, int channels, int sampleRate)
        {
            if (data == null || channels <= 0 || channels > MaxChannels || sampleRate <= 0) return;
            if (channels != _channels || sampleRate != _rate) Configure(channels, sampleRate);
            int frames = data.Length / channels;
            int slots = _look + 1;
            int qcap = _dqGain.Length;
            for (int f = 0; f < frames; f++)
            {
                int o = f * channels;
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float x = data[o + c] * HeadroomGain;
                    if (float.IsNaN(x)) x = 0f;
                    data[o + c] = x;
                    float a = x < 0f ? -x : x;
                    if (a > peak) peak = a;
                }
                if (peak > PeakIn) PeakIn = peak;
                float need = peak > Ceiling ? Ceiling / peak : 1f;
                // Sliding-window minimum of the needed gain over the look-ahead (monotonic queue, no allocation).
                while (_dqCount > 0 && _dqGain[(_dqHead + _dqCount - 1) % qcap] >= need) _dqCount--;
                int tail = (_dqHead + _dqCount) % qcap;
                _dqGain[tail] = need;
                _dqFrame[tail] = _frame;
                _dqCount++;
                while (_dqFrame[_dqHead] < _frame - _look) { _dqHead = (_dqHead + 1) % qcap; _dqCount--; }
                float windowMin = _dqGain[_dqHead];
                // Release toward 1, never above what the window needs (the delayed frame is inside the window).
                float env = _env + (1f - _env) * _release;
                if (env > windowMin) env = windowMin;
                _env = env;
                if (env < MinGain) MinGain = env;
                // Delay line: write this frame, read the one _look frames ago.
                int w = (int)(_frame % slots) * channels;
                int r = (int)((_frame + 1) % slots) * channels;   // the oldest slot = _frame - _look
                for (int c = 0; c < channels; c++)
                {
                    float x = data[o + c];
                    float y = _delay[r + c] * env;
                    if (y > Ceiling) y = Ceiling;   // safety: never past the ceiling
                    else if (y < -Ceiling) y = -Ceiling;
                    _delay[w + c] = x;
                    data[o + c] = y;
                }
                _frame++;
            }
        }
    }

    /// <summary>
    /// CM b13 S0 (audio): the master bus on the active AudioListener's GameObject. A filter there processes the whole mixed
    /// output (every AudioSource and the AudioMixer, whose master group outputs to the listener), so the limiter sees the
    /// same mix the speakers and AudioRenderer (the match recorder) get.
    /// </summary>
    public sealed class MasterBusFilter : MonoBehaviour
    {
        readonly MasterLimiter _core = new MasterLimiter();
        volatile int _rate;

        public MasterLimiter Core => _core;

        void Awake() => _rate = AudioSettings.outputSampleRate;
        void OnEnable() { AudioSettings.OnAudioConfigurationChanged += OnConfig; _rate = AudioSettings.outputSampleRate; }
        void OnDisable() => AudioSettings.OnAudioConfigurationChanged -= OnConfig;
        void OnConfig(bool deviceWasChanged) => _rate = AudioSettings.outputSampleRate;

        void OnAudioFilterRead(float[] data, int channels) => _core.Process(data, channels, _rate);
    }

    /// <summary>
    /// CM b13 S0 (audio): keeps exactly one <see cref="MasterBusFilter"/> on the active AudioListener (cameras and scenes can
    /// bring their own listener). Checks every 30 frames, and searches only when the listener it knows is gone.
    /// </summary>
    public sealed class MasterBusKeeper : MonoBehaviour
    {
        AudioListener _listener;
        int _next;
        bool _warned;

        public static MasterBusFilter Active { get; private set; }

        void Update()
        {
            if (Time.frameCount < _next) return;
            _next = Time.frameCount + 30;
            if (_listener != null && _listener.isActiveAndEnabled && Active != null && Active.gameObject == _listener.gameObject) return;
            _listener = FindAnyObjectByType<AudioListener>();
            if (_listener == null) return;
            // A filter beside AudioSources would bind to them, not to the listener (Unity logs an error): skip such a listener.
            if (_listener.GetComponent<AudioSource>() != null)
            {
                if (!_warned) Debug.LogWarning("[CM.Audio] master bus skipped: the AudioListener shares its GameObject with an AudioSource");
                _warned = true;
                Active = null;
                return;
            }
            MasterBusFilter f = _listener.GetComponent<MasterBusFilter>();
            if (f == null) f = _listener.gameObject.AddComponent<MasterBusFilter>();
            Active = f;
        }
    }
}
