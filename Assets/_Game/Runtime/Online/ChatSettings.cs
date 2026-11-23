using CriticalMass.Net;
using UnityEngine;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// b17-chat: each player's chat switches, saved on this computer (PlayerPrefs, cm.chat.*). Defaults are ChatDefaults
    /// (Senior Panel 2026-11-21: players may be minors, no accounts): typed chat OFF, quick chat and pings ON, word filter
    /// ON, voice OFF. Kept out of GameSettings on purpose (another branch edits it); Reset puts every switch back.
    /// The switches live on the room screen (CHAT SETTINGS) and in the pause screen's chat panel.
    /// </summary>
    public static class ChatSettings
    {
        public const string ChatKey = "cm.chat.on", TypedKey = "cm.chat.typed", FilterKey = "cm.chat.filter", VoiceKey = "cm.chat.voice", MuteAllKey = "cm.chat.muteAll";
        public static readonly string[] AllKeys = { ChatKey, TypedKey, FilterKey, VoiceKey, MuteAllKey };

        static bool Get(string k, bool d) => CmPrefs.GetInt(k, d ? 1 : 0) == 1;
        static void Set(string k, bool v) { CmPrefs.SetInt(k, v ? 1 : 0); CmPrefs.Save(); Changed++; }

        /// <summary>Bumps on every change (the chat applies the switches when it moves).</summary>
        public static int Changed { get; private set; }

        /// <summary>Chat at all (quick chat, pings' callouts and, when on, typed chat).</summary>
        public static bool Chat { get => Get(ChatKey, ChatDefaults.Chat); set => Set(ChatKey, value); }
        /// <summary>Typed free text (off by default).</summary>
        public static bool TypedText { get => Get(TypedKey, ChatDefaults.TypedText); set => Set(TypedKey, value); }
        public static bool WordFilter { get => Get(FilterKey, ChatDefaults.WordFilter); set => Set(FilterKey, value); }
        /// <summary>Lobby push-to-talk voice (off by default).</summary>
        public static bool Voice { get => Get(VoiceKey, ChatDefaults.Voice); set => Set(VoiceKey, value); }
        public static bool MuteAll { get => Get(MuteAllKey, false); set => Set(MuteAllKey, value); }

        public static void Reset()
        {
            foreach (string k in AllKeys) CmPrefs.DeleteKey(k);
            CmPrefs.Save();
            Changed++;
        }

        /// <summary>Puts the switches on a channel (cheap; called when the channel or the switches change).</summary>
        public static void Apply(ChatChannel ch)
        {
            if (ch == null) return;
            ch.Enabled = Chat;
            ch.TypedText = Chat && TypedText;
            ch.FilterWords = WordFilter;
            if (ch.MuteAll != MuteAll) ch.SetMuteAll(MuteAll);
            if (ch.Voice != null) ch.Voice.Enabled = Chat && Voice;
        }

        /// <summary>The words on each switch's button.</summary>
        public static string ChatLabel => "CHAT: " + (Chat ? "ON" : "OFF");
        public static string TypedLabel => "TYPED CHAT: " + (TypedText ? "ON" : "OFF");
        public static string FilterLabel => "WORD FILTER: " + (WordFilter ? "ON" : "OFF");
        public static string VoiceLabel => "VOICE (ROOM, TEAM): " + (Voice ? "ON" : "OFF");
        public static string MuteAllLabel => "MUTE EVERYONE: " + (MuteAll ? "ON" : "OFF");
    }
}
