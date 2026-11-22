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

        /// <summary>
        /// b17-chat: what the host forwards: trimmed, ASCII, at most MaxLength, no links, but words NOT masked, so each
        /// player's own word-filter switch (on by default) decides on their screen (MaskWords).
        /// </summary>
        public static string Transport(string s) => StripLinks(Ascii(s, MaxLength)).Trim();

        /// <summary>b17-chat: blocked words masked (the word filter alone; CleanLine = Transport + this).</summary>
        public static string MaskWords(string s) => Mask(s ?? "");

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
        /// <summary>b17-chat: as forwarded (ASCII, no links, words NOT masked); show it through ChatChannel.Shown.</summary>
        public string Text;
        public long AtMs;
        /// <summary>b17-chat: the sender's team (0 Azure, 1 Crimson), -1 unknown or a notice.</summary>
        public int Team;
        /// <summary>b17-chat: the QuickChat id of a callout, -1 for typed text and notices.</summary>
        public int Callout;
    }

    /// <summary>
    /// online-rooms-2: room and match chat. Every line goes to the host, which checks the sender's rate (a short burst,
    /// then one line per RateMs), cleans the text, and forwards it to everyone (All) or to the sender's team (Team). Each
    /// machine keeps the last Keep lines and its own mute list. Chat packets (types 30 to 39, ChatWire) are never read by
    /// the lockstep layer, so nothing typed can change the match.
    /// <para>b17-chat: the v1 wire (type 31, ChatWire) with the sender's team and quick-chat callouts; the sender's game
    /// checks the same rate before it sends (so a stock game never spends the relay on a line the host would drop); the
    /// host spends at most RoomSendsPerSecond relay messages a second on chat for the whole room (RoomBurst at once), and
    /// an All line is ONE broadcast once every member is confirmed (the relay's fan-out is free; it used to be one message
    /// per player). Each machine's own switches: Enabled (chat on, default), FilterWords (word filter, default on),
    /// MuteAll, and the per-player mute. A voice channel (lobby only, off by default) rides type 34 (Voice).</para>
    /// </summary>
    public sealed class ChatChannel
    {
        /// <summary>The b11 chat type (still read and accepted; this build sends ChatWire.Msg).</summary>
        public const int Msg = ChatWire.Legacy;
        public const int Keep = 100;
        public const int RateMs = 1500, Burst = 3;
        /// <summary>b17-chat: the host's chat budget for the whole room, in relay messages (ChatBudget, ChatCostTests).</summary>
        public const double RoomSendsPerSecond = 6;
        public const int RoomBurst = 24;
        /// <summary>The client's own bucket key (the host's buckets are keyed by peer number).</summary>
        private const int SelfKey = -2;

        private readonly ITransport _t;
        private readonly byte[] _pkt = new byte[Peers.MaxPacket];
        private readonly List<ChatLine> _lines = new List<ChatLine>(Keep);
        private readonly HashSet<int> _muted = new HashSet<int>();
        private readonly Dictionary<int, double> _tokens = new Dictionary<int, double>();
        private readonly Dictionary<int, long> _tokenAt = new Dictionary<int, long>();
        private double _roomTokens = RoomBurst;
        private long _roomAt = long.MinValue;

        /// <summary>Host side: who is called what and on which team (the room or the match fills it).</summary>
        public Func<int, string> NameOf = p => "PLAYER";
        public Func<int, int> TeamOf = p => 0;
        /// <summary>b11-b: host side: true for a peer whose lines the host may route (a confirmed member of the room). A
        /// connection the room never confirmed, or refused, is dropped here in the lobby AND in the match (the match
        /// forwards every chat packet it hears; before this a stranger's line was routed as if it sat on Azure).</summary>
        public Func<int, bool> Knows = p => true;
        /// <summary>Host side: every human peer currently in the room.</summary>
        public Func<IEnumerable<int>> PeersInRoom = () => Array.Empty<int>();
        /// <summary>b17-chat, host side: true when one broadcast reaches exactly the room's confirmed members (the room
        /// says so when nobody is half-joined); false = one message per member.</summary>
        public Func<bool> CanBroadcast = () => false;
        /// <summary>b17-chat: chat on (the settings switch). Off: nothing is sent and players' lines are not shown (the
        /// room's own notices still are).</summary>
        public bool Enabled = true;
        /// <summary>
        /// b17-chat (Senior Panel: players may be minors, no accounts): typed free text. The GAME ships it OFF for every
        /// player (ChatDefaults.TypedText, the "TYPED CHAT" switch); quick-chat callouts and pings stay on. Off: typed
        /// lines are neither sent nor shown, callouts still are. (The channel itself defaults on, so the headless peer and
        /// the protocol tests carry text.)
        /// </summary>
        public bool TypedText = true;
        /// <summary>b17-chat: this player's word filter (on by default): Shown masks blocked words.</summary>
        public bool FilterWords = true;
        /// <summary>b17-chat: hide every other player's lines (and voice) on this machine.</summary>
        public bool MuteAll { get; private set; }
        /// <summary>b17-chat: raised for every player line that is shown (HUD markers and sounds listen).</summary>
        public event Action<ChatLine> Received;
        /// <summary>b17-chat: the lobby's push-to-talk voice (type 34), or null.</summary>
        public VoiceChannel Voice;
        /// <summary>Bumps on every new line (the screen redraws when it changes).</summary>
        public int Version { get; private set; }
        public IReadOnlyList<ChatLine> Lines => _lines;
        /// <summary>b17-chat: relay messages this machine spent on chat lines (tests and the cost line).</summary>
        public long Sent { get; private set; }

        public ChatChannel(ITransport t)
        {
            _t = t;
        }

        /// <summary>Read each time: a relay connection learns its peer number only after it connects.</summary>
        private bool _host => _t.LocalPeer == Peers.Host;

        public bool IsMuted(int peer) => _muted.Contains(peer);
        public void SetMuted(int peer, bool muted) { if (muted) _muted.Add(peer); else _muted.Remove(peer); Version++; }
        public void SetMuteAll(bool on) { MuteAll = on; Version++; }
        /// <summary>b17-chat: true when this machine hears peer (not muted one by one or by mute all).</summary>
        public bool Hears(int peer) => peer == _t.LocalPeer || (!MuteAll && !_muted.Contains(peer));

        /// <summary>
        /// b17-chat (Senior Panel): this player reports peer: it is muted on this machine at once, and the report comes back
        /// as plain text for the game to save in a local file (no server, no account): when, who, and their last lines on
        /// this screen as evidence. Null when there is nobody to report.
        /// </summary>
        public string Report(int peer, long now, string utcStamp)
        {
            if (peer < 0 || peer == _t.LocalPeer) return null;
            string name = null;
            var said = new List<string>();
            foreach (ChatLine l in _lines)
                if (l.FromPeer == peer && l.Scope != ChatScope.System)
                {
                    name = l.From;
                    said.Add("  " + (l.Scope == ChatScope.Team ? "[TEAM] " : "") + l.Text);
                }
            if (name == null) name = ChatFilter.CleanName(NameOf(peer));
            SetMuted(peer, true);
            int from = Math.Max(0, said.Count - 20);
            var sb = new System.Text.StringBuilder(256);
            sb.Append("REPORT ").Append(utcStamp ?? "").Append(": ").Append(name).Append(" (peer ").Append(peer).Append(") muted on this computer. ")
              .Append(said.Count).Append(" line(s) from them on this screen").Append(said.Count > 0 ? ", the last " + (said.Count - from) + ":" : ".").Append('\n');
            for (int i = from; i < said.Count; i++) sb.Append(said[i]).Append('\n');
            Notice(name + " is reported and muted for you. The report is saved on this computer.", now);
            return sb.ToString();
        }

        /// <summary>b17-chat: the words to draw for a line (blocked words masked while FilterWords is on).</summary>
        public string Shown(ChatLine l) => FilterWords && l.Scope != ChatScope.System && l.Callout < 0 ? ChatFilter.MaskWords(l.Text) : l.Text;

        /// <summary>Say something. Returns null when sent, or why not (plain words).</summary>
        public string Say(string text, ChatScope scope, long now) => Send(ChatFilter.Transport(text), scope, -1, now);

        /// <summary>b17-chat: a quick-chat callout (QuickChat id). Same rate as typed lines.</summary>
        public string Callout(int id, long now) => QuickChat.Valid(id) ? Send("", QuickChat.Scope[id], id, now) : "No such callout.";

        private string Send(string clean, ChatScope scope, int callout, long now)
        {
            if (!Enabled) return "Chat is off (chat settings).";
            if (callout < 0 && !TypedText) return "Typed chat is off (chat settings). Quick chat still works.";
            if (callout < 0 && clean.Length == 0) return "Nothing to send.";
            if (scope != ChatScope.All && scope != ChatScope.Team) return "Nothing to send.";
            if (_host)
            {
                if (!TakeToken(Peers.Host, now)) return "Slow down a little.";
                return Route(Peers.Host, scope, callout, clean, now) ? null : "The room's chat is busy; try again in a moment.";
            }
            if (_t.LocalPeer < 0) return "Not connected yet.";
            if (!TakeToken(SelfKey, now)) return "Slow down a little.";
            int n = ChatWire.Write(_pkt, scope, 0, 0, callout, "", clean);
            if (n <= 0) return "Too long.";
            _t.Send(Peers.Host, _pkt, n);
            Sent++;
            return null;
        }

        /// <summary>A line from the game itself ("Jeisil joined."), shown locally only.</summary>
        public void Notice(string text, long now) => Add(new ChatLine { FromPeer = -1, From = "", Scope = ChatScope.System, Text = text, AtMs = now, Team = -1, Callout = -1 });

        /// <summary>Feed every transport event of types 30 to 39 here (rooms and the lockstep session forward them).</summary>
        public void Handle(ref TransportEvent ev, long now)
        {
            if (ev.Kind != TransportEventKind.Data || ev.Data == null || ev.Length < 1 || !ChatWire.Owns(ev.Data[0])) return;
            if (ev.Data[0] == ChatWire.Voice) { Voice?.Handle(ref ev, now); return; }
            if (!ChatWire.Read(ev.Data, ev.Length, out ChatWire.Packet p)) return;   // 32, 33, 35..39 and malformed: ignored
            if (_host)
            {
                // A client asks the host to send; the host never trusts the sender's own name, team or peer number.
                if (!Knows(ev.From)) return;
                if (!TakeToken(ev.From, now)) return;
                ChatScope scope = p.Callout >= 0 ? QuickChat.Scope[p.Callout] : p.Scope;
                string clean = p.Callout >= 0 ? "" : ChatFilter.Transport(p.Text);
                if (p.Callout < 0 && clean.Length == 0) return;
                Route(ev.From, scope, p.Callout, clean, now);
                return;
            }
            if (ev.From != Peers.Host) return;   // only the host's forwarded lines count
            Add(new ChatLine
            {
                FromPeer = p.From, From = ChatFilter.CleanName(p.Name), Scope = p.Scope, AtMs = now, Team = p.Team, Callout = p.Callout,
                Text = p.Callout >= 0 ? QuickChat.Text[p.Callout] : ChatFilter.Transport(p.Text),
            });
        }

        /// <summary>Host: forwards one line; false when the room's chat budget cannot pay for it (nothing is sent).</summary>
        private bool Route(int from, ChatScope scope, int callout, string clean, long now)
        {
            string name = ChatFilter.CleanName(NameOf(from));
            int team = TeamOf(from);
            if (scope == ChatScope.Team && team < 0) return false;   // b11-b: no known team, so no team to send to
            bool broadcast = scope == ChatScope.All && CanBroadcast();
            int cost = 0;
            if (broadcast) cost = 1;
            else
                foreach (int p in PeersInRoom())
                {
                    if (p == Peers.Host) continue;
                    if (scope == ChatScope.Team && TeamOf(p) != team) continue;
                    cost++;
                }
            if (!TakeRoom(cost, now)) return false;
            int n = ChatWire.Write(_pkt, scope, from, team, callout, name, clean);
            if (n <= 0) return false;
            if (broadcast) { _t.Send(Peers.Broadcast, _pkt, n); Sent++; }
            else
                foreach (int p in PeersInRoom())
                {
                    if (p == Peers.Host) continue;
                    if (scope == ChatScope.Team && TeamOf(p) != team) continue;
                    _t.Send(p, _pkt, n);
                    Sent++;
                }
            if (scope == ChatScope.All || TeamOf(Peers.Host) == team)
                Add(new ChatLine { FromPeer = from, From = name, Scope = scope, Text = callout >= 0 ? QuickChat.Text[callout] : clean, AtMs = now, Team = team, Callout = callout });
            return true;
        }

        private bool TakeRoom(int cost, long now)
        {
            if (cost <= 0) return true;
            if (_roomAt == long.MinValue) _roomAt = now;
            _roomTokens = Math.Min(RoomBurst, _roomTokens + Math.Max(0, now - _roomAt) * RoomSendsPerSecond / 1000.0);
            _roomAt = now;
            // A line costing more than the burst still goes when the bucket is full (it leaves the bucket in debt), so the
            // long-run rate stays RoomSendsPerSecond.
            if (_roomTokens < Math.Min(cost, RoomBurst)) return false;
            _roomTokens -= cost;
            return true;
        }

        private bool TakeToken(int peer, long now)
        {
            if (!_tokens.TryGetValue(peer, out double tok)) { tok = Burst; _tokenAt[peer] = now; }
            long at = _tokenAt[peer];
            tok = Math.Min(Burst, tok + Math.Max(0, now - at) / (double)RateMs);
            _tokenAt[peer] = now;
            if (tok < 1) { _tokens[peer] = tok; return false; }
            _tokens[peer] = tok - 1;
            return true;
        }

        private void Add(ChatLine line)
        {
            if (line.Scope != ChatScope.System)
            {
                if (!Enabled) return;
                if (line.Callout < 0 && !TypedText && line.FromPeer != _t.LocalPeer) return;
                if (line.FromPeer >= 0 && !Hears(line.FromPeer)) return;
            }
            if (_lines.Count >= Keep) _lines.RemoveAt(0);
            _lines.Add(line);
            Version++;
            if (line.Scope != ChatScope.System) Received?.Invoke(line);
        }
    }
}
