using System;
using System.Text;

namespace CriticalMass.Net
{
    /// <summary>
    /// b17-chat: the chat wire, version 1 of message type 31 (ChatWire.Msg). Chat is a NON-SIM message: the lockstep layer
    /// hands every packet of type 20 and up to the room (LockstepSession.Other), never to the inputs, the state hash or the
    /// replay's input stream, so nothing typed or called out can change a match.
    ///
    /// <para><b>Types.</b> 30 = the b11 chat line (still read, still accepted from a client, never sent by this build);
    /// 31 = chat v1 below; 34 = voice frame (VoiceChannel); 32, 33 and 35 to 39 are reserved for chat and ignored.</para>
    /// <para><b>Layout of 31.</b> [31][ver u8][scope u8][from u8][team u8][callout u8: 0 = typed text, else id + 1]
    /// [name str][text str]. A client's request to the host carries from = team = 0 and an empty name (the host never
    /// trusts them; it stamps its own member list). Strings are WireWriter.Str (one length byte, printable ASCII).</para>
    /// <para><b>Versions.</b> ver 0 is invalid. A reader takes ver 1's fields from any ver &gt;= 1 and ignores bytes after
    /// them, so a later version may only APPEND fields. A game older than b17 ignores type 31 outright: its chat reads
    /// only type 30 and its room switch has no case for 31 (the same rule this build applies to 32, 33 and 35 to 39,
    /// ChatWireTests). The room's HELLO check (same build) means mixed builds never share a room anyway.</para>
    /// </summary>
    public static class ChatWire
    {
        public const int Legacy = 30, Msg = 31, Voice = 34;
        public const int Version = 1;

        /// <summary>Every type in the chat block (30 to 39): the room hands them to ChatChannel.Handle.</summary>
        public static bool Owns(int type) => type >= 30 && type < 40;

        public struct Packet
        {
            public int Ver;
            public ChatScope Scope;
            public int From;
            public int Team;
            /// <summary>-1 = typed text, else a QuickChat id.</summary>
            public int Callout;
            public string Name;
            public string Text;
        }

        public static int Write(byte[] buf, ChatScope scope, int from, int team, int callout, string name, string text)
        {
            var w = new WireWriter(buf);
            w.U8(Msg);
            w.U8(Version);
            w.U8((int)scope);
            w.U8(from);
            w.U8(team < 0 ? 255 : team);
            w.U8(callout < 0 ? 0 : callout + 1);
            w.Str(name, ChatFilter.NameMax);
            w.Str(text, ChatFilter.MaxLength);
            return w.Overflow ? 0 : w.Pos;
        }

        /// <summary>Reads type 31 (ver &gt;= 1) or type 30 (the b11 layout); false for anything malformed or not a line.</summary>
        public static bool Read(byte[] data, int length, out Packet p)
        {
            p = default;
            if (data == null || length < 1) return false;
            var r = new WireReader(data, 1, length);
            if (data[0] == Legacy)
            {
                p.Ver = 0;
                p.Scope = (ChatScope)r.U8();
                p.From = r.U8();
                p.Name = r.Str(ChatFilter.NameMax);
                p.Text = r.Str(ChatFilter.MaxLength);
                p.Team = -1;
                p.Callout = -1;
                return !r.Bad && (int)p.Scope <= 1;
            }
            if (data[0] != Msg) return false;
            p.Ver = r.U8();
            p.Scope = (ChatScope)r.U8();
            p.From = r.U8();
            int team = r.U8();
            int call = r.U8();
            p.Name = r.Str(ChatFilter.NameMax);
            p.Text = r.Str(ChatFilter.MaxLength);
            p.Team = team == 255 ? -1 : team;
            p.Callout = call == 0 ? -1 : call - 1;
            if (r.Bad || p.Ver < 1 || (int)p.Scope > 1) return false;
            if (p.Callout >= QuickChat.Count) return false;
            return true;
        }
    }

    /// <summary>
    /// b17-chat: what the game ships with for every player (Senior Panel 2026-11-21: players may be minors and there are no
    /// accounts). Typed free text OFF; quick-chat callouts and pings ON; the word filter ON; voice OFF. Runtime's
    /// ChatSettings reads these as its defaults; ChatB17Tests pins them.
    /// </summary>
    public static class ChatDefaults
    {
        public const bool Chat = true;
        public const bool TypedText = false;
        public const bool Callouts = true;
        public const bool WordFilter = true;
        public const bool Voice = false;
        /// <summary>The line shown once when a player turns voice on.</summary>
        public const string VoiceNotice = "Voice goes to your team in this room only, through the game's relay: nobody sees your address, and nothing is recorded.";
        /// <summary>The line shown once when a player turns typed chat on.</summary>
        public const string TypedNotice = "Typed chat is on: players you do not know can write to you. Mute or report anyone from the chat panel.";
    }

    /// <summary>Where a quick-chat callout's HUD marker goes (resolved on each machine from the shared world).</summary>
    public enum CalloutMarker : byte { None = 0, Sender = 1, Hill = 2, Kaiju = 3, OwnBase = 4 }

    /// <summary>
    /// b17-chat: the eight preset callouts of the controller's quick-chat wheel (typing on a pad is miserable). The id
    /// travels, never the words, so every machine shows the same line; team callouts also drop a short marker through the
    /// existing ping markers (Runtime: Pings.Mirror, view only). MarkerKind is the ping call (Attack 0, On my way 1,
    /// Defend 2, Help 3) whose shape and colour the marker borrows.
    /// </summary>
    public static class QuickChat
    {
        public const int Count = 8;
        public static readonly string[] Text =
        {
            "Group on the hill", "Kaiju coming!", "Need a Healer", "Defend our base",
            "Attack now", "Fall back", "Nice one!", "Good game",
        };
        public static readonly ChatScope[] Scope =
        {
            ChatScope.Team, ChatScope.Team, ChatScope.Team, ChatScope.Team, ChatScope.Team, ChatScope.Team, ChatScope.All, ChatScope.All,
        };
        public static readonly CalloutMarker[] Marker =
        {
            CalloutMarker.Hill, CalloutMarker.Kaiju, CalloutMarker.Sender, CalloutMarker.OwnBase,
            CalloutMarker.Sender, CalloutMarker.Sender, CalloutMarker.None, CalloutMarker.None,
        };
        public static readonly int[] MarkerKind = { 1, 3, 3, 2, 0, 2, -1, -1 };

        public static bool Valid(int id) => id >= 0 && id < Count;
    }

    /// <summary>
    /// b17-chat: what a chat line looks like on screen. Typed text is shown on rich-text labels (the sender's name in its
    /// team colour), so every '&lt;' and '&gt;' a player typed becomes a look-alike angle quote: no Unity UI or
    /// TextMeshPro tag ("&lt;color&gt;", "&lt;size=200&gt;", "&lt;link&gt;", "&lt;noparse&gt;") can ever form. Links are never
    /// clickable: the label has no link handler, and the filter has already replaced anything that looks like an address.
    /// </summary>
    public static class ChatText
    {
        public const char Lt = '‹', Gt = '›';

        public static string EscapeRich(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c == '<' ? Lt : c == '>' ? Gt : c);
            return sb.ToString();
        }

        /// <summary>The line as plain text (measuring, logs).</summary>
        public static string Plain(ChatLine l)
        {
            if (l.Scope == ChatScope.System) return "· " + l.Text;
            return (l.Scope == ChatScope.Team ? "[TEAM] " : "") + l.From + ": " + l.Text;
        }

        /// <summary>The line for a rich-text label: the name wrapped in its team colour (hex "RRGGBB"; null = no colour).</summary>
        public static string Rich(ChatLine l, string teamHex)
        {
            if (l.Scope == ChatScope.System) return "· " + EscapeRich(l.Text);
            string name = EscapeRich(l.From);
            if (!string.IsNullOrEmpty(teamHex) && IsHex6(teamHex)) name = "<color=#" + teamHex + ">" + name + "</color>";
            return (l.Scope == ChatScope.Team ? "[TEAM] " : "") + name + ": " + EscapeRich(l.Text);
        }

        static bool IsHex6(string h)
        {
            if (h.Length != 6) return false;
            foreach (char c in h) if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }

    /// <summary>
    /// b17-chat: the chat's share of the free relay, from the rate rules (ChatCostTests measures a full room against it).
    /// The relay bills INCOMING messages (20 to 1); every line costs its sender's one message to the host plus the host's
    /// routing (one broadcast for an All line, one message per teammate for a Team line), capped by the host's room bucket.
    /// </summary>
    public static class ChatBudget
    {
        /// <summary>Worst case: every joiner sends at the client limit and the host routes at its room cap (messages a minute).</summary>
        public static double WorstMessagesPerMinute(int humans)
        {
            if (humans < 2) return 0;
            double perPlayer = ChatChannel.Burst + 60000.0 / ChatChannel.RateMs;   // the burst once, then one per RateMs
            double uplink = (humans - 1) * perPlayer;
            double hostSends = ChatChannel.RoomBurst + ChatChannel.RoomSendsPerSecond * 60.0;
            return uplink + hostSends;
        }

        public static double WorstRequestsPerMinute(int humans) => WorstMessagesPerMinute(humans) / RelayBudget.MessagesPerRequest;

        /// <summary>The worst case as a share of what the match itself costs a minute (32 humans: about 5.6%).</summary>
        public static double WorstShareOfMatch(int humans)
        {
            double match = RelayBudget.MatchMessagesPerSecond(humans) * 60.0;
            return match <= 0 ? 0 : WorstMessagesPerMinute(humans) / match;
        }
    }
}
