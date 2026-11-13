using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-63 crash log writer and repeat guard (replaces the DG-05 synchronous File.AppendAllText in AutoplayArgs).
    /// <para>Repeat key: the message with every run of digits and hex replaced by "#", plus the first stack line, so
    /// "index 7 out of range" and "index 8 out of range" are one key. Exceptions and Errors are both counted. The table is
    /// never cleared wholesale: past TableCap keys every new key shares one overflow bucket.</para>
    /// <para>Writing: Submit only enqueues (bounded queue, at most MaxLinesPerSecond entries per second; the rest are
    /// counted and written as one "suppressed N" line); one background thread writes the file. The file rotates at
    /// MaxBytes (the previous one kept as crash-log.1.txt). Submit is safe from any thread and never throws.</para>
    /// </summary>
    public sealed class CrashLog : IDisposable
    {
        public const int RepeatLimit = 20;
        public const int MaxLinesPerSecond = 20;
        public const int QueueCap = 64;
        public const int TableCap = 4096;
        public const long MaxBytes = 1024 * 1024;
        public const int MaxConditionChars = 400;
        public const int MaxStackChars = 2000;
        const string OverflowKey = "#overflow";

        readonly string _path;
        readonly object _lock = new object();
        readonly object _fileLock = new object();
        readonly Queue<string> _queue = new Queue<string>(QueueCap);
        readonly Dictionary<string, int> _repeats = new Dictionary<string, int>(256, StringComparer.Ordinal);
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        readonly Thread _thread;
        readonly string _label;
        long _windowStart;
        int _windowCount, _suppressed;
        volatile bool _stop;

        /// <summary>Entries written since boot (tests).</summary>
        public int Written { get; private set; }

        /// <summary>The file; a background writer thread when background is true (tests use false and FlushNow).</summary>
        public CrashLog(string path, bool background, string buildLabel = null)
        {
            _path = path;
            _label = buildLabel ?? "";
            if (!background) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "CM crash log", Priority = System.Threading.ThreadPriority.Normal };
            _thread.Start();
        }

        public string Path => _path;

        /// <summary>The repeat key of a message (pure; tests): digits and hex runs become "#".</summary>
        public static string RepeatKey(string kind, string condition, string firstStackLine)
        {
            var sb = new StringBuilder(96);
            sb.Append(kind).Append('|');
            Normalise(sb, condition, 200);
            sb.Append('|');
            Normalise(sb, firstStackLine, 160);
            return sb.ToString();
        }

        static void Normalise(StringBuilder sb, string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return;
            int n = Math.Min(s.Length, max);
            bool inRun = false;
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                bool digit = c >= '0' && c <= '9';
                bool hexTail = inRun && ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || c == 'x' || c == 'X');
                if (digit || hexTail)
                {
                    if (!inRun) sb.Append('#');
                    inRun = true;
                    continue;
                }
                inRun = false;
                sb.Append(c);
            }
        }

        /// <summary>
        /// Counts one message and queues it unless it repeated RepeatLimit times or the per-second budget is spent.
        /// Returns the repeat count of its key (RepeatLimit exactly once per key: the caller's "stop the offender" moment).
        /// </summary>
        public int Submit(string kind, string condition, string stack)
        {
            try
            {
                string first = FirstLine(stack);
                string key = RepeatKey(kind, condition, first);
                int n;
                lock (_lock)
                {
                    if (!_repeats.TryGetValue(key, out n) && _repeats.Count >= TableCap) key = OverflowKey;
                    _repeats.TryGetValue(key, out n);
                    _repeats[key] = ++n;
                    if (n > RepeatLimit) return n;
                    string note = n == RepeatLimit ? $" (repeated {RepeatLimit} times; further repeats are counted, not written)" : "";
                    long now = _clock.ElapsedMilliseconds;
                    if (now - _windowStart >= 1000)
                    {
                        _windowStart = now;
                        _windowCount = 0;
                    }
                    if (_windowCount >= MaxLinesPerSecond || _queue.Count >= QueueCap)
                    {
                        _suppressed++;
                        return n;
                    }
                    _windowCount++;
                    _queue.Enqueue(Format(kind, condition, note, n == RepeatLimit ? null : stack));
                }
                _signal.Set();
                return n;
            }
            catch (Exception)
            {
                return 0;   // never throw from a log handler
            }
        }

        string Format(string kind, string condition, string note, string stack)
        {
            var sb = new StringBuilder(128);
            sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append(' ');
            if (_label.Length > 0) sb.Append(_label).Append(' ');
            sb.Append(kind).Append(": ");
            string c = condition ?? "";
            sb.Append(c.Length > MaxConditionChars ? c.Substring(0, MaxConditionChars) + "..." : c).Append(note).Append('\n');
            if (!string.IsNullOrEmpty(stack))
            {
                string s = stack.TrimEnd();
                sb.Append(s.Length > MaxStackChars ? s.Substring(0, MaxStackChars) + "..." : s).Append('\n');
            }
            return sb.ToString();
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return nl >= 0 ? s.Substring(0, nl) : s;
        }

        void Run()
        {
            while (!_stop)
            {
                _signal.WaitOne(500);
                FlushNow();
            }
            FlushNow();
        }

        /// <summary>Writes everything queued (the writer thread; quit; tests). Never throws.</summary>
        public void FlushNow()
        {
            StringBuilder sb = null;
            lock (_lock)
            {
                if (_queue.Count == 0 && _suppressed == 0) return;
                sb = new StringBuilder(1024);
                while (_queue.Count > 0) sb.Append(_queue.Dequeue());
                if (_suppressed > 0)
                {
                    sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                      .Append(" suppressed ").Append(_suppressed).Append(" more log entries (over ").Append(MaxLinesPerSecond).Append(" per second)\n");
                    _suppressed = 0;
                }
            }
            lock (_fileLock)
            {
                try
                {
                    string dir = System.IO.Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = System.IO.Path.Combine(fi.DirectoryName ?? "", System.IO.Path.GetFileNameWithoutExtension(_path) + ".1" + System.IO.Path.GetExtension(_path));
                        File.Copy(_path, old, true);
                        File.Delete(_path);
                        fi.Refresh();
                    }
                    if (!fi.Exists) sb.Insert(0, "# Critical Mass crash log. Newest last; capped at 1 MB (older: crash-log.1.txt).\n");
                    byte[] bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
                    using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        fs.Write(bytes, 0, bytes.Length);
                    Written++;
                }
                catch (Exception)
                {
                    // a full disk loses log lines, never the game
                }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
            if (_thread != null && _thread.IsAlive) _thread.Join(500);
            FlushNow();
        }
    }
}
