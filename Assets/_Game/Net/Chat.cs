using System;
using System.Collections.Generic;
using System.Text;

namespace CriticalMass.Net
{
    /// <summary>
    /// online-rooms-2: text chat rules for an all-ages game. Printable ASCII only, at most MaxLength characters, no links
    /// (anything that looks like a web address or e-mail is replaced), and a word filter that masks a fixed list of words
    /// even when padded with symbols or digits standing in for letters. Used for chat lines and for player names. Pure and
    /// deterministic; it never touches the Sim (chat travels beside the lockstep inputs, never inside them).
    /// </summary>
    public static class ChatFilter
    {
        public const int MaxLength = 120;
        public const int NameMax = 16;

        // Kept short and generic on purpose; the lists are data, extend them here. Lower case, letters only.
        // Anywhere: masked even inside a longer word or padded with symbols ("f.u.c.k", "sh1t").
        private static readonly string[] Anywhere =
        {
            "fuck", "shit", "bitch", "cunt", "asshole", "whore", "slut", "faggot", "nigger", "nigga", "retard", "porn",
        };
        // Whole word only (they hide inside innocent words: grape, peacock, spicy, Dickens).
        private static readonly HashSet<string> WholeWord = new HashSet<string>
        {
            "dick", "cock", "rape", "spic", "fag", "twat", "wank", "kys", "chink", "kike", "nazi", "pussy", "bastard", "dildo", "tits",
        };

        /// <summary>A chat line made safe: trimmed, ASCII, no links, blocked words masked; empty when nothing is left.</summary>
        public static string CleanLine(string s)
        {
            string a = Ascii(s, MaxLength);
            a = StripLinks(a);
            a = Mask(a);
            return a.Trim();
        }

        /// <summary>A display name made safe: letters, digits, space, '-' and '_' only, at most NameMax, filtered; "PLAYER" if empty or blocked.</summary>
        public static string CleanName(string s)
        {
            var sb = new StringBuilder(NameMax);
            foreach (char c in s ?? "")
            {
                if (sb.Length >= NameMax) break;
                char u = char.ToUpperInvariant(c);
                if ((u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9') || u == '-' || u == '_' || (u == ' ' && sb.Length > 0 && sb[sb.Length - 1] != ' ')) sb.Append(u);
            }
            string n = sb.ToString().Trim();
            if (n.Length == 0 || ContainsBlocked(n)) return "PLAYER";
            return n;
        }

        public static bool ContainsBlocked(string s)
        {
            foreach (string token in (s ?? "").Split(' ')) if (TokenBlocked(token)) return true;
            return false;
        }

        /// <summary>One space-separated word: blocked when its letters contain an Anywhere word or equal a WholeWord word.</summary>
        private static bool TokenBlocked(string token)
        {
            if (token.Length == 0) return false;
            string q = Squash(token, out _);
            foreach (string w in Anywhere) if (q.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            foreach (string part in token.Split('.', ',', '!', '?', '-', '_', '\'', '"'))
                if (IsWholeWordBlocked(part)) return true;
            return IsWholeWordBlocked(token);
        }

        private static bool IsWholeWordBlocked(string token)
        {
            if (token.Length == 0) return false;
            string q = Squash(token, out _);
            if (q.Length == 0) return false;
            if (WholeWord.Contains(q)) return true;
            return q.Length > 1 && q[q.Length - 1] == 's' && WholeWord.Contains(q.Substring(0, q.Length - 1));
        }

        private static string Ascii(string s, int max)
        {
            var sb = new StringBuilder(Math.Min(max, s != null ? s.Length : 0));
            foreach (char c in s ?? "")
            {
                if (sb.Length >= max) break;
                sb.Append(c >= 32 && c < 127 ? c : ' ');
            }
            return sb.ToString();
        }

        private static string StripLinks(string s)
        {
            string[] words = s.Split(' ');
            bool changed = false;
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i].ToLowerInvariant();
                bool link = w.Contains("://") || w.StartsWith("www.") || (w.Contains("@") && w.Contains(".")) || EndsWithDomain(w);
                if (link) { words[i] = "[link removed]"; changed = true; }
            }
            return changed ? string.Join(" ", words) : s;
        }

        private static readonly string[] Domains = { ".com", ".net", ".org", ".gg", ".io", ".tv", ".co", ".ly", ".me", ".xyz", ".app", ".dev", ".link" };

        private static bool EndsWithDomain(string w)
        {
            w = w.TrimEnd('.', ',', '!', '?', ')');
            foreach (string d in Domains)
            {
                int i = w.IndexOf(d, StringComparison.Ordinal);
                if (i > 0 && (i + d.Length == w.Length || w[i + d.Length] == '/')) return true;
            }
            return false;
        }

        /// <summary>Letters only, lower case, with common stand-ins (0 o, 1 i, 3 e, 4 a, 5 s, 7 t, @ a, $ s) mapped; map[i] = source index.</summary>
        private static string Squash(string s, out int[] map)
        {
            var sb = new StringBuilder(s.Length);
            var m = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToLowerInvariant(s[i]);
                switch (c)
                {
                    case '0': c = 'o'; break;
                    case '1': case '!': case '|': c = 'i'; break;
                    case '3': c = 'e'; break;
                    case '4': case '@': c = 'a'; break;
                    case '5': case '$': c = 's'; break;
                    case '7': c = 't'; break;
                }
                if (c >= 'a' && c <= 'z') { sb.Append(c); m.Add(i); }
            }
            map = m.ToArray();
            return sb.ToString();
        }

        /// <summary>Every space-separated word that is blocked becomes asterisks of the same length (words are judged one at a time,
        /// so "this hit" is never read as one word).</summary>
        private static string Mask(string s)
        {
            char[] outc = null;
            int start = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                if (i < s.Length && s[i] != ' ') continue;
                if (i > start && TokenBlocked(s.Substring(start, i - start)))
                {
                    if (outc == null) outc = s.ToCharArray();
                    for (int k = start; k < i; k++) outc[k] = '*';
                }
                start = i + 1;
            }
            return outc != null ? new string(outc) : s;
        }
    }

    public enum ChatScope { All = 0, Team = 1, System = 2 }

    public struct ChatLine
    {
        public int FromPeer;
        public string From;
        public ChatScope Scope;
        public string Text;
        public long AtMs;
    }

    /// <summary>
    /// online-rooms-2: room and match chat. Every line goes to the host, which checks the sender's rate (a short burst,
    /// then one line per RateMs), cleans the text, and forwards it to everyone (All) or to the sender's team (Team). Each
    /// machine keeps the last Keep lines and its own mute list. Chat packets (type 30) are never read by the lockstep
    /// layer, so nothing typed can change the match.
    /// </summary>
    public sealed class ChatChannel
    {
        public const int Msg = 30;
        public const int Keep = 40;
        public const int RateMs = 1500, Burst = 3;

        private readonly ITransport _t;
        private readonly byte[] _pkt = new byte[Peers.MaxPacket];
        private readonly List<ChatLine> _lines = new List<ChatLine>(Keep);
        private readonly HashSet<int> _muted = new HashSet<int>();
        private readonly Dictionary<int, double> _tokens = new Dictionary<int, double>();
        private readonly Dictionary<int, long> _tokenAt = new Dictionary<int, long>();

        /// <summary>Host side: who is called what and on which team (the room or the match fills it).</summary>
        public Func<int, string> NameOf = p => "PLAYER";
        public Func<int, int> TeamOf = p => 0;
        /// <summary>Host side: every human peer currently in the room.</summary>
        public Func<IEnumerable<int>> PeersInRoom = () => Array.Empty<int>();
        /// <summary>Bumps on every new line (the screen redraws when it changes).</summary>
        public int Version { get; private set; }
        public IReadOnlyList<ChatLine> Lines => _lines;

        public ChatChannel(ITransport t)
        {
            _t = t;
        }

        /// <summary>Read each time: a relay connection learns its peer number only after it connects.</summary>
        private bool _host => _t.LocalPeer == Peers.Host;

        public bool IsMuted(int peer) => _muted.Contains(peer);
        public void SetMuted(int peer, bool muted) { if (muted) _muted.Add(peer); else _muted.Remove(peer); Version++; }

        /// <summary>Say something. Returns null when sent, or why not (plain words).</summary>
        public string Say(string text, ChatScope scope, long now)
        {
            string clean = ChatFilter.CleanLine(text);
            if (clean.Length == 0) return "Nothing to send.";
            if (_host)
            {
                if (!TakeToken(Peers.Host, now)) return "Slow down a little.";
                Route(Peers.Host, scope, clean, now);
                return null;
            }
            var w = new WireWriter(_pkt);
            w.U8(Msg);
            w.U8((int)scope);
            w.U8(0);
            w.Str("", 0);
            w.Str(clean, ChatFilter.MaxLength);
            _t.Send(Peers.Host, _pkt, w.Pos);
            return null;
        }

        /// <summary>A line from the game itself ("Jeisil joined."), shown locally only.</summary>
        public void Notice(string text, long now) => Add(new ChatLine { FromPeer = -1, From = "", Scope = ChatScope.System, Text = text, AtMs = now });

        /// <summary>Feed every transport event of type Msg here (rooms and the lockstep session forward them).</summary>
        public void Handle(ref TransportEvent ev, long now)
        {
            if (ev.Kind != TransportEventKind.Data || ev.Data == null || ev.Length < 1 || ev.Data[0] != Msg) return;
            var r = new WireReader(ev.Data, 1, ev.Length);
            var scope = (ChatScope)r.U8();
            int fromPeer = r.U8();
            string from = r.Str(ChatFilter.NameMax);
            string text = r.Str(ChatFilter.MaxLength);
            if (r.Bad || (int)scope > 1) return;
            if (_host)
            {
                // A client asks the host to send; the host never trusts the sender's own name or peer number.
                if (!TakeToken(ev.From, now)) return;
                string clean = ChatFilter.CleanLine(text);
                if (clean.Length > 0) Route(ev.From, scope, clean, now);
                return;
            }
            if (ev.From != Peers.Host) return;   // only the host's forwarded lines count
            Add(new ChatLine { FromPeer = fromPeer, From = ChatFilter.CleanName(from), Scope = scope, Text = ChatFilter.CleanLine(text), AtMs = now });
        }

        private void Route(int from, ChatScope scope, string clean, long now)
        {
            string name = ChatFilter.CleanName(NameOf(from));
            var w = new WireWriter(_pkt);
            w.U8(Msg);
            w.U8((int)scope);
            w.U8(from);
            w.Str(name, ChatFilter.NameMax);
            w.Str(clean, ChatFilter.MaxLength);
            int team = TeamOf(from);
            foreach (int p in PeersInRoom())
            {
                if (p == Peers.Host) continue;
                if (scope == ChatScope.Team && TeamOf(p) != team) continue;
                _t.Send(p, _pkt, w.Pos);
            }
            if (scope == ChatScope.All || TeamOf(Peers.Host) == team) Add(new ChatLine { FromPeer = from, From = name, Scope = scope, Text = clean, AtMs = now });
        }

        private bool TakeToken(int peer, long now)
        {
            if (!_tokens.TryGetValue(peer, out double tok)) { tok = Burst; _tokenAt[peer] = now; }
            long at = _tokenAt[peer];
            tok = Math.Min(Burst, tok + (now - at) / (double)RateMs);
            _tokenAt[peer] = now;
            if (tok < 1) { _tokens[peer] = tok; return false; }
            _tokens[peer] = tok - 1;
            return true;
        }

        private void Add(ChatLine line)
        {
            if (line.FromPeer >= 0 && _muted.Contains(line.FromPeer)) return;
            if (_lines.Count >= Keep) _lines.RemoveAt(0);
            _lines.Add(line);
            Version++;
        }
    }
}
