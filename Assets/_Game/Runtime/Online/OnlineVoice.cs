using System;
using System.Collections.Generic;
using CriticalMass.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// b17-chat: lobby voice, OFF by default (ChatSettings.Voice). Push-to-talk only: hold V (controller: LB) in the room
    /// screen. The microphone is opened when the key goes down and closed when it comes up (macOS shows its mic light only
    /// then); one press talks at most VoiceChannel.MaxPressMs. Frames go through the room's relay connection (VoiceChannel:
    /// 16 kHz IMA ADPCM, 120 ms frames, team only, host floor of two talkers a team), so no player ever learns another's
    /// address and nothing touches the match. Each teammate heard gets a streaming AudioSource with a small jitter buffer;
    /// muted players (and mute everyone) are never played. StatusLine is the one-line indicator under the chat box.
    /// No third-party library.
    /// </summary>
    public sealed class OnlineVoice : MonoBehaviour
    {
        /// <summary>Playback starts once this much is buffered (two frames), and drops the oldest past MaxBufferMs.</summary>
        public const int StartBufferMs = 240, MaxBufferMs = 900;

        /// <summary>The indicator line (empty when voice is off or not in a room's lobby).</summary>
        public static string StatusLine { get; private set; } = "";
        /// <summary>true while this player is sending.</summary>
        public static bool Talking { get; private set; }

        VoiceChannel _bound;
        string _device;
        AudioClip _mic;
        int _micRate, _micRead;
        float[] _micBuf = new float[4096];
        readonly short[] _frame = new short[VoiceChannel.FrameSamples];
        int _frameFill;
        double _resamplePos;
        long _pressAt = -1;
        bool _pressSpent;
        string _why;
        long _whyAt;
        readonly Dictionary<int, Talker> _talkers = new Dictionary<int, Talker>();
        readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(96);

        static VoiceChannel Channel => OnlineMatch.Host != null ? OnlineMatch.Host.Chat?.Voice : OnlineMatch.Client?.Chat?.Voice;

        void Update()
        {
            VoiceChannel v = Channel;
            if (v != _bound) Bind(v);
            long now = OnlineMatch.NowMs;
            bool lobby = v != null && OnlineMenuScreen.AnyOpen && !OnlineMatch.InMatch;
            if (v == null || !v.Enabled || !lobby)
            {
                StopMic();
                StatusLine = "";
                return;
            }
            bool held = PttHeld();
            if (held && !_pressSpent)
            {
                if (_pressAt < 0) { _pressAt = now; StartMic(); }
                if (now - _pressAt > VoiceChannel.MaxPressMs) { _pressSpent = true; StopMic(); Say("Let go of the talk key and press it again."); }
                else Capture(v, now);
            }
            else if (!held)
            {
                _pressAt = -1;
                _pressSpent = false;
                StopMic();
            }
            StatusLine = Indicator(v, now);
        }

        static bool PttHeld()
        {
            if (OnlineChatHud.EatsKeys || OnlineChatHud.WheelOpen) return false;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.vKey.isPressed) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && pad.leftShoulder.isPressed;
        }

        void Say(string why) { _why = why; _whyAt = OnlineMatch.NowMs; }

        string Indicator(VoiceChannel v, long now)
        {
            _sb.Clear();
            if (Talking) _sb.Append("YOU ARE TALKING TO YOUR TEAM");
            List<int> heard = v.Talking(now);
            if (heard.Count > 0)
            {
                if (_sb.Length > 0) _sb.Append("   ");
                _sb.Append("TALKING: ");
                for (int i = 0; i < heard.Count; i++) { if (i > 0) _sb.Append(", "); _sb.Append(NameOf(heard[i])); }
            }
            if (_sb.Length == 0 && _why != null && now - _whyAt < 3000) _sb.Append(_why);
            if (_sb.Length == 0) _sb.Append("VOICE ON: HOLD V (CONTROLLER: LB) TO TALK TO YOUR TEAM");
            return _sb.ToString();
        }

        static string NameOf(int peer)
        {
            var members = OnlineMatch.Host != null ? OnlineMatch.Host.Members : OnlineMatch.Client?.Members;
            if (members != null) foreach (RoomMember m in members) if (m.Peer == peer) return m.Name;
            return "PLAYER " + peer;
        }

        void Bind(VoiceChannel v)
        {
            if (_bound != null) _bound.Play = null;
            StopMic();
            foreach (Talker t in _talkers.Values) t.Dispose();
            _talkers.Clear();
            _bound = v;
            if (v != null) v.Play = OnFrame;
        }

        // ---- capture ---------------------------------------------------------------------------------------------------

        void StartMic()
        {
            if (_mic != null) return;
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0) { Say("No microphone found (or access was refused in the system's privacy settings)."); _pressSpent = true; return; }
            _device = devices[0];
            Microphone.GetDeviceCaps(_device, out int min, out int max);
            _micRate = (min == 0 && max == 0) || (VoiceChannel.SampleRate >= min && VoiceChannel.SampleRate <= max) ? VoiceChannel.SampleRate : max;
            _mic = Microphone.Start(_device, true, 1, _micRate);
            if (_mic == null) { Say("The microphone would not start (check the system's privacy settings)."); _pressSpent = true; return; }
            _micRead = 0;
            _frameFill = 0;
            _resamplePos = 0;
        }

        void StopMic()
        {
            Talking = false;
            if (_mic == null) return;
            Microphone.End(_device);
            Destroy(_mic);
            _mic = null;
        }

        void Capture(VoiceChannel v, long now)
        {
            if (_mic == null) return;
            int pos = Microphone.GetPosition(_device);
            if (pos < 0) return;
            int total = _mic.samples;
            int n = pos - _micRead;
            if (n < 0) n += total;
            if (n <= 0) return;
            if (n > total) n = total;
            if (_micBuf.Length != n) _micBuf = new float[n];   // GetData reads exactly the array's length (wrapping on a looping clip)
            float[] tmp = _micBuf;
            _mic.GetData(tmp, _micRead);
            _micRead = (_micRead + n) % total;
            // Linear resample to 16 kHz (no-op step 1.0 when the device already runs at 16 kHz).
            double step = (double)_micRate / VoiceChannel.SampleRate;
            while (_resamplePos < n - 1)
            {
                int i = (int)_resamplePos;
                float f = (float)(_resamplePos - i);
                float s = tmp[i] + (tmp[i + 1] - tmp[i]) * f;
                _frame[_frameFill++] = (short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), short.MinValue, short.MaxValue);
                _resamplePos += step;
                if (_frameFill == VoiceChannel.FrameSamples)
                {
                    _frameFill = 0;
                    string why = v.SendFrame(_frame, now);
                    Talking = why == null;
                    if (why != null) Say(why);
                }
            }
            _resamplePos -= n - 1;
            if (_resamplePos < 0) _resamplePos = 0;
        }

        // ---- playback --------------------------------------------------------------------------------------------------

        void OnFrame(int peer, short[] pcm, int n)
        {
            if (!_talkers.TryGetValue(peer, out Talker t)) { t = new Talker(transform, peer); _talkers[peer] = t; }
            t.Push(pcm, n);
        }

        void OnDestroy()
        {
            Bind(null);
        }

        /// <summary>One teammate's voice: a ring buffer read by a streaming clip (the audio thread may call the reader).</summary>
        sealed class Talker
        {
            readonly float[] _ring = new float[VoiceChannel.SampleRate * 2];
            int _read, _write, _count;
            bool _playing;
            readonly object _lock = new object();
            readonly GameObject _go;
            readonly AudioClip _clip;
            readonly AudioSource _src;

            public Talker(Transform parent, int peer)
            {
                _go = new GameObject("Voice " + peer);
                _go.transform.SetParent(parent, false);
                _src = _go.AddComponent<AudioSource>();
                _src.spatialBlend = 0f;
                _src.loop = true;
                _clip = AudioClip.Create("voice-" + peer, VoiceChannel.SampleRate, 1, VoiceChannel.SampleRate, true, Read);
                _src.clip = _clip;
                _src.Play();
            }

            public void Push(short[] pcm, int n)
            {
                lock (_lock)
                {
                    int max = VoiceChannel.SampleRate * MaxBufferMs / 1000;
                    for (int i = 0; i < n; i++)
                    {
                        _ring[_write] = pcm[i] / 32768f;
                        _write = (_write + 1) % _ring.Length;
                        if (_count < _ring.Length) _count++; else _read = (_read + 1) % _ring.Length;
                    }
                    while (_count > max) { _read = (_read + 1) % _ring.Length; _count--; }   // late audio is worse than lost audio
                    if (!_playing && _count >= VoiceChannel.SampleRate * StartBufferMs / 1000) _playing = true;
                }
            }

            void Read(float[] data)
            {
                lock (_lock)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (_playing && _count > 0)
                        {
                            data[i] = _ring[_read];
                            _read = (_read + 1) % _ring.Length;
                            _count--;
                        }
                        else { data[i] = 0f; _playing = false; }   // ran dry: wait for the buffer again
                    }
                }
            }

            public void Dispose()
            {
                if (_src != null) _src.Stop();
                if (_go != null) UnityEngine.Object.Destroy(_go);
                if (_clip != null) UnityEngine.Object.Destroy(_clip);
            }
        }
    }
}
