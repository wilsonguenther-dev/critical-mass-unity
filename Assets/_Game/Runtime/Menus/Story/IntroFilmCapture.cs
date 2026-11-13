using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CriticalMass.Runtime.Menus.Story
{
    /// <summary>
    /// intro-film-2: verifies the intro film in a real player build (the Linux test runners cannot decode H.264, so this
    /// is the video path's check; see tools/film/verify_build_capture.py and tools/film/README-video-check.md).
    /// <para>Player only, only with <c>-introCapture DIR</c> on the command line (never in the editor or a normal launch):
    /// the intro plays at launch whatever the saved setting, with subtitles on; from its first video frame this writes a
    /// screenshot every 1/<c>-introCaptureFps</c> seconds (default 2) to DIR/shot-NNN.jpg, a CSV log of each shot
    /// (wall clock, video clock, video frame, playback mode, audio samples recorded so far), and the game's own audio
    /// output (the AudioListener's mix, taken before the OS output, so a muted machine still records) to DIR/audio.wav.
    /// <c>-introSkipAt S</c> presses skip when the video clock reaches S seconds. The run keeps capturing for
    /// <c>-introCaptureTail S</c> seconds (default 4) after the intro ends (the hand-off into the title), then quits.</para>
    /// </summary>
    public sealed class IntroFilmCapture : MonoBehaviour
    {
        public const string Flag = "-introcapture";
        static bool? _requested;
        static string _dir;
        static float _fps = 2f, _skipAt = -1f, _tail = 4f;

        /// <summary>True in a player started with -introCapture DIR.</summary>
        public static bool Requested
        {
            get
            {
                if (_requested.HasValue) return _requested.Value;
                _requested = false;
                if (Application.isEditor) return false;
                string[] a = Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length; i++)
                {
                    string k = (a[i] ?? "").ToLowerInvariant();
                    string next = i + 1 < a.Length ? a[i + 1] : null;
                    if (k == Flag && next != null) { _dir = next; _requested = true; i++; }
                    else if (k == "-introcapturefps" && Num(next, out float f) && f > 0f) { _fps = f; i++; }
                    else if (k == "-introskipat" && Num(next, out float s)) { _skipAt = s; i++; }
                    else if (k == "-introcapturetail" && Num(next, out float t) && t >= 0f) { _tail = t; i++; }
                }
                return _requested.Value;
            }
        }

        static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested) return;
            var go = new GameObject("Intro Film Capture");
            DontDestroyOnLoad(go);
            go.AddComponent<IntroFilmCapture>();
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_dir);
            var log = new StringBuilder("idx,real_s,film_clock,film_frame,mode,audio_frames,event\n");
            int rate = AudioSettings.outputSampleRate;
            IntroFilmAudioTap rec = null;
            float start = -1f, end = -1f, wait = 0f;
            int idx = 0;
            bool skipped = false;
            string lastMode = "";
            while (true)
            {
                yield return new WaitForEndOfFrame();
                string mode = StoryIntro.PlaybackMode;
                if (mode != lastMode) { log.AppendLine(Row(-1, start, mode, rec, "mode:" + mode)); lastMode = mode; }
                if (start < 0f)
                {
                    wait += Time.unscaledDeltaTime;
                    if (mode == "film" || mode == "comic")
                    {
                        start = Time.realtimeSinceStartup;
                        AudioListener l = FindAnyObjectByType<AudioListener>();
                        if (l != null)
                        {
                            rec = l.gameObject.AddComponent<IntroFilmAudioTap>();
                            rec.Buf = new float[rate * 2 * 75];
                            rec.On = true;
                        }
                        log.AppendLine(Row(-1, start, mode, rec, "start rate=" + rate + " listener=" + (l != null ? l.name : "none")));
                    }
                    else if (wait > 40f) { log.AppendLine("-1,0,-1,-1,none,0,timeout: the intro never started"); break; }
                    else continue;
                }
                float el = Time.realtimeSinceStartup - start;
                if (_skipAt > 0f && !skipped && StoryIntro.FilmClock >= _skipAt)
                {
                    StoryIntro.Skip();
                    skipped = true;
                    log.AppendLine(Row(-1, start, mode, rec, "skip"));
                }
                if (el >= idx / _fps)
                {
                    Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(_dir, "shot-" + idx.ToString("000") + ".jpg"), tex.EncodeToJPG(88));
                    Destroy(tex);
                    log.AppendLine(Row(idx, start, mode, rec, ""));
                    idx++;
                }
                if (end < 0f && mode == "none" && el > 1f) { end = el; log.AppendLine(Row(-1, start, mode, rec, "intro ended")); }
                if (end >= 0f && el > end + _tail) break;
                if (el > 70f) { log.AppendLine(Row(-1, start, mode, rec, "cap 70 s")); break; }
            }
            File.WriteAllText(Path.Combine(_dir, "capture.csv"), log.ToString());
            if (rec != null)
            {
                rec.On = false;
                WriteWav(Path.Combine(_dir, "audio.wav"), rec.Buf, rec.Written, rec.Channels, rate);
            }
            Debug.Log("[intro capture] wrote " + idx + " shots to " + _dir);
            Application.Quit(0);
        }

        static string Row(int idx, float start, string mode, IntroFilmAudioTap rec, string ev)
        {
            float real = start < 0f ? 0f : Time.realtimeSinceStartup - start;
            int frames = rec != null ? rec.Written / Math.Max(1, rec.Channels) : 0;
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:F4},{2:F4},{3},{4},{5},{6}", idx, real,
                StoryIntro.FilmClock, StoryIntro.FilmFrame, mode, frames, ev);
        }

        static void WriteWav(string path, float[] buf, int n, int ch, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(rate); w.Write(rate * ch * 2); w.Write((short)(ch * 2));
                w.Write((short)16); w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                for (int i = 0; i < n; i++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(buf[i] * 32767f), -32768, 32767));
            }
        }
    }

    /// <summary>Taps the AudioListener's mix for <see cref="IntroFilmCapture"/> (audio thread writes, one writer).</summary>
    sealed class IntroFilmAudioTap : MonoBehaviour
    {
        public float[] Buf;
        public int Channels = 2;
        public volatile int Written;
        public volatile bool On;
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!On || Buf == null) return;
            Channels = channels;
            int n = Math.Min(data.Length, Buf.Length - Written);
            if (n <= 0) return;
            Array.Copy(data, 0, Buf, Written, n);
            Written += n;
        }
    }
}
