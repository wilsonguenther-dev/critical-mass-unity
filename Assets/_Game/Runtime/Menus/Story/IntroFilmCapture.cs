using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Video;

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
    /// <para>capture-gaps-1: <c>-introHandoff</c> adds the hand-off proof. From one second before IntroFilm.HandoffStart the
    /// shots go to 10 a second (film to title dissolve); after the tail it presses Play Now from the title (the same call as
    /// the title's button, Flow.PlayNow.Go), waits for the match, and writes match-0.jpg, match-1.jpg and match-3.jpg at
    /// 0, 1 and 3 s of the match plus handoff.txt with the camera heading on screen (CameraHeading.Active), the chosen one
    /// and whether it is the new default (CameraHeading.Creator, 135). The flow state at each step goes to the CSV.</para>
    /// </summary>
    public sealed class IntroFilmCapture : MonoBehaviour
    {
        public const string Flag = "-introcapture";
        static bool? _requested;
        static string _dir;
        static float _fps = 2f, _skipAt = -1f, _tail = 4f;
        static bool _handoff;
        /// <summary>Shots per second inside the hand-off window with -introHandoff.</summary>
        public const float HandoffFps = 10f;
        /// <summary>Seconds into the match at which -introHandoff writes match-N.jpg.</summary>
        public static readonly float[] MatchShotSeconds = { 0f, 1f, 3f };

        /// <summary>True when the film clock is inside the hand-off window (one second before the dissolve starts, onward).</summary>
        public static bool InHandoffWindow(double filmClock) => filmClock >= IntroFilm.HandoffStart - 1.0;

        /// <summary>Shot rate at a film clock: HandoffFps inside the window when the hand-off proof is on, else the base rate.</summary>
        public static float RateAt(double filmClock, bool handoff, float baseFps) => handoff && InHandoffWindow(filmClock) ? Mathf.Max(baseFps, HandoffFps) : baseFps;

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
                    else if (k == "-introhandoff") _handoff = true;
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
            float nextShot = 0f;
            bool skipped = false;
            int probed = 0;
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
                if (el >= nextShot)
                {
                    // the film clock reads 0 once the film is done; the window then holds through the tail (the title fade-in)
                    nextShot = el + 1f / RateAt(end >= 0f ? IntroFilm.Seconds : StoryIntro.FilmClock, _handoff, _fps);
                    Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(_dir, "shot-" + idx.ToString("000") + ".jpg"), tex.EncodeToJPG(88));
                    Destroy(tex);
                    log.AppendLine(Row(idx, start, mode, rec, SoundProbe((float)StoryIntro.FilmClock, ref probed)));
                    idx++;
                }
                if (end < 0f && mode == "none" && el > 1f) { end = el; log.AppendLine(Row(-1, start, mode, rec, "intro ended")); }
                if (end >= 0f && el > end + _tail) break;
                if (el > 70f) { log.AppendLine(Row(-1, start, mode, rec, "cap 70 s")); break; }
            }
            if (_handoff) yield return MatchHandoff(log, start, rec);
            File.WriteAllText(Path.Combine(_dir, "capture.csv"), log.ToString());
            if (rec != null)
            {
                rec.On = false;
                WriteWav(Path.Combine(_dir, "audio.wav"), rec.Buf, rec.Written, rec.Channels, rate);
            }
            Debug.Log("[intro capture] wrote " + idx + " shots to " + _dir);
            Application.Quit(0);
        }

        /// <summary>-introHandoff: Play Now from the title into a match; shots and the heading on screen at the match start.</summary>
        IEnumerator MatchHandoff(StringBuilder log, float start, IntroFilmAudioTap rec)
        {
            var report = new StringBuilder("intro hand-off into a match\n");
            float wait = 0f;
            while (Flow.GameFlow.State != Flow.FlowState.Title && wait < 15f) { wait += Time.unscaledDeltaTime; yield return null; }
            log.AppendLine(Row(-1, start, "flow", rec, "flow:" + Flow.GameFlow.State + " after " + wait.ToString("0.0", CultureInfo.InvariantCulture) + " s"));
            report.AppendLine("title reached: " + (Flow.GameFlow.State == Flow.FlowState.Title) + " (state " + Flow.GameFlow.State + ")");
            bool went = Flow.PlayNow.Go();
            report.AppendLine("play now pressed: " + went);
            log.AppendLine(Row(-1, start, "flow", rec, "play now " + went));
            wait = 0f;
            while ((Flow.GameFlow.State != Flow.FlowState.Match || FindAnyObjectByType<Game.SimDriverBehaviour>() == null) && wait < 30f)
            { wait += Time.unscaledDeltaTime; yield return null; }
            bool inMatch = Flow.GameFlow.State == Flow.FlowState.Match;
            report.AppendLine("match reached: " + inMatch + " after " + wait.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            float m0 = Time.realtimeSinceStartup;
            foreach (float at in MatchShotSeconds)
            {
                while (Time.realtimeSinceStartup - m0 < at) yield return null;
                yield return new WaitForEndOfFrame();
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_dir, "match-" + at.ToString("0", CultureInfo.InvariantCulture) + ".jpg"), tex.EncodeToJPG(88));
                Destroy(tex);
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                string yaw = cam != null ? cam.transform.eulerAngles.y.ToString("0.0", CultureInfo.InvariantCulture) : "none";
                report.AppendLine("match-" + at.ToString("0", CultureInfo.InvariantCulture) + ": heading active " + Camera.CameraHeading.Active.ToString("0.#", CultureInfo.InvariantCulture)
                    + ", chosen " + Camera.CameraHeading.Chosen.ToString("0.#", CultureInfo.InvariantCulture) + ", camera yaw " + yaw);
            }
            bool creator = Math.Abs(Camera.CameraHeading.Active - Camera.CameraHeading.Creator) < 0.5;
            report.AppendLine("new default heading (" + Camera.CameraHeading.Creator + ") on screen: " + creator);
            File.WriteAllText(Path.Combine(_dir, "handoff.txt"), report.ToString());
            Debug.Log("[intro capture] hand-off: match " + inMatch + ", heading " + Camera.CameraHeading.Active + (creator ? " (new default)" : " (NOT the new default)"));
        }

        static readonly float[] _probeBuf = new float[2048];

        /// <summary>batch 7 sound proof (the listener tap above can land on CM Audio's first AudioSource instead of the
        /// listener, and then records that source's silence): main-thread samples of the real mix
        /// (AudioListener.GetOutputData, both channels) and of the film's own AudioSource, as RMS in the event column
        /// ("snd L=.. F=.."); at film clock 5, 20 and 40 s the same plus the film source's state go to Player.log.</summary>
        static string SoundProbe(float clock, ref int probed)
        {
            float l = 0f, f = 0f;
            AudioListener.GetOutputData(_probeBuf, 0); l = Mathf.Max(l, Rms(_probeBuf));
            AudioListener.GetOutputData(_probeBuf, 1); l = Mathf.Max(l, Rms(_probeBuf));
            AudioSource src = null;
            foreach (VideoPlayer vp in FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None))
            {
                src = vp.GetTargetAudioSource(0);
                if (src != null) break;
            }
            if (src != null) { src.GetOutputData(_probeBuf, 0); f = Rms(_probeBuf); }
            float[] marks = { 5f, 20f, 40f };
            if (probed < marks.Length && clock >= marks[probed])
            {
                probed++;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[intro capture] sound at film {0:F1} s: mix rms {1:F4}, film source rms {2:F4}, source playing {3}, volume {4:F2}, mute {5}, group {6}, listener volume {7:F2}, listener paused {8}",
                    clock, l, f, src != null && src.isPlaying, src != null ? src.volume : -1f, src != null && src.mute,
                    src != null && src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "none", AudioListener.volume, AudioListener.pause));
            }
            return string.Format(CultureInfo.InvariantCulture, "snd L={0:F4} F={1:F4}", l, f);
        }

        static float Rms(float[] b)
        {
            double sum = 0;
            for (int i = 0; i < b.Length; i++) sum += b[i] * b[i];
            return (float)Math.Sqrt(sum / b.Length);
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
