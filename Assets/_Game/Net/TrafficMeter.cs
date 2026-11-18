using System;
using System.Text;

namespace CriticalMass.Net
{
    /// <summary>b15-zero-cost: what a packet is, by its first byte (the message number every packet starts with).</summary>
    public enum TrafficKind
    {
        /// <summary>1: a joiner's inputs (lockstep MsgInputs).</summary>
        Inputs,
        /// <summary>2: the host's input bundles (lockstep MsgBundles).</summary>
        Bundles,
        /// <summary>3: the match log fed to a rejoining player (lockstep MsgLog).</summary>
        RejoinLog,
        /// <summary>20 to 29: the room (hello, roster, pick, ready, start, ping, pong, rejoin).</summary>
        Room,
        /// <summary>30 to 39: chat.</summary>
        Chat,
        /// <summary>Anything else (never expected).</summary>
        Other,
        /// <summary>The host's "lock the room" control frame (relay only).</summary>
        Lock,
    }

    /// <summary>b15-zero-cost: messages and bytes, by kind. Bytes include the relay's two-byte routing header.</summary>
    public sealed class TrafficCounts
    {
        public const int Kinds = 7;
        public readonly long[] Messages = new long[Kinds];
        public readonly long[] Bytes = new long[Kinds];

        public long TotalMessages { get { long n = 0; foreach (long m in Messages) n += m; return n; } }
        public long TotalBytes { get { long n = 0; foreach (long b in Bytes) n += b; return n; } }

        public static TrafficKind KindOf(byte[] data, int length)
        {
            if (data == null || length < 1) return TrafficKind.Other;
            int t = data[0];
            if (t == 1) return TrafficKind.Inputs;
            if (t == 2) return TrafficKind.Bundles;
            if (t == 3) return TrafficKind.RejoinLog;
            if (t >= 20 && t < 30) return TrafficKind.Room;
            if (t >= 30 && t < 40) return TrafficKind.Chat;
            return TrafficKind.Other;
        }

        public void Add(TrafficKind k, int bytes)
        {
            Messages[(int)k]++;
            Bytes[(int)k] += bytes;
        }

        public TrafficCounts Copy()
        {
            var c = new TrafficCounts();
            Array.Copy(Messages, c.Messages, Kinds);
            Array.Copy(Bytes, c.Bytes, Kinds);
            return c;
        }

        /// <summary>this minus an earlier snapshot.</summary>
        public TrafficCounts Since(TrafficCounts earlier)
        {
            var c = Copy();
            if (earlier == null) return c;
            for (int i = 0; i < Kinds; i++) { c.Messages[i] -= earlier.Messages[i]; c.Bytes[i] -= earlier.Bytes[i]; }
            return c;
        }

        /// <summary>"inputs 450 msg 29700 B, bundles ..." (kinds with nothing are left out).</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Kinds; i++)
            {
                if (Messages[i] == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(((TrafficKind)i).ToString().ToLowerInvariant()).Append(' ').Append(Messages[i]).Append(" msg ").Append(Bytes[i]).Append(" B");
            }
            return sb.Length > 0 ? sb.ToString() : "nothing";
        }
    }

    /// <summary>
    /// b15-zero-cost: an ITransport that counts what passes through another one (sent and received, by kind) and changes
    /// nothing. Around the relay leg it counts exactly the messages the relay bills: every Send (and Lock) is one incoming
    /// WebSocket message at the relay (server/relay/README.md, "Free tier").
    /// </summary>
    public sealed class MeteredTransport : ITransport, ILockableTransport
    {
        public const int RelayHeaderBytes = 2;
        public readonly ITransport Inner;
        public readonly TrafficCounts Sent = new TrafficCounts();
        public readonly TrafficCounts Received = new TrafficCounts();

        public MeteredTransport(ITransport inner) { Inner = inner ?? throw new ArgumentNullException(nameof(inner)); }

        public int LocalPeer => Inner.LocalPeer;
        public bool Reliable => Inner.Reliable;

        public void Send(int to, byte[] data, int length)
        {
            if (length > 0 && length <= Peers.MaxPacket) Sent.Add(TrafficCounts.KindOf(data, length), length + RelayHeaderBytes);
            Inner.Send(to, data, length);
        }

        public bool Poll(out TransportEvent ev)
        {
            if (!Inner.Poll(out ev)) return false;
            if (ev.Kind == TransportEventKind.Data) Received.Add(TrafficCounts.KindOf(ev.Data, ev.Length), ev.Length + RelayHeaderBytes);
            return true;
        }

        public void Lock()
        {
            if (Inner is ILockableTransport l) { Sent.Add(TrafficKind.Lock, 1); l.Lock(); }
        }

        public void Close() => Inner.Close();
    }

    /// <summary>
    /// b15-zero-cost: the free relay's daily allowance in matches. Every number here is from Cloudflare's documentation
    /// (checked 2026-11-17, docs/online/ZERO-COST-ONLINE.md cites the pages) or measured by ZeroCostOnlineTests; anything
    /// else is marked as an estimate.
    /// <list type="bullet">
    /// <item>Durable Objects on Workers Free: 100,000 requests a day, reset at 00:00 UTC; past it "further operations of that
    /// type will fail with an error" (https://developers.cloudflare.com/durable-objects/platform/pricing/).</item>
    /// <item>Incoming WebSocket messages count 20 to 1 (100 messages = 5 requests); outgoing messages and protocol pings are
    /// free; opening a WebSocket is one request (same page, footnote 2).</item>
    /// </list>
    /// So a room's cost is the messages the games SEND to the relay (divided by 20) plus one request per connection, one for
    /// making the room, and the room's housekeeping alarm (one a minute; whether an alarm counts on the free plan is not
    /// stated, so it is counted to be safe).
    /// </summary>
    public static class RelayBudget
    {
        public const int FreeRequestsPerDay = 100000;
        public const int MessagesPerRequest = 20;
        public const double SimHz = 30;
        /// <summary>The shipped match length (GameConfig.MatchSeconds) and an assumed lobby before it (estimate).</summary>
        public const double MatchSeconds = 480, LobbySeconds = 120;

        /// <summary>
        /// Messages a second the relay receives from a room in a match, from the send rules: every joiner sends one packet
        /// per TicksPerPacket ticks; the host's bundles go out in the same groups (small rooms) or at most every 60 ms (big
        /// rooms, where joiners' groups of 3 make it every 100 ms). preB15: one packet per tick each (two above 16 humans).
        /// ZeroCostOnlineTests checks this against measured rooms.
        /// </summary>
        public static double MatchMessagesPerSecond(int humans, bool preB15 = false)
        {
            if (humans < 2) return 0;
            bool big = humans > LockstepSession.BigRoomHumans;
            if (preB15)
            {
                double perPeer = big ? 1000.0 / 66.7 : SimHz;   // 30 ms gate on 33 ms ticks = one a tick; big rooms one per 2 ticks
                return humans * perPeer;
            }
            int k = LockstepSession.TicksPerPacketFor(humans);
            return humans * SimHz / k;
        }

        /// <summary>Lobby messages a second (measured shape: the host's ping and roster each second, one pong per joiner).</summary>
        public static double LobbyMessagesPerSecond(int humans) => humans < 1 ? 0 : 2 + (humans - 1);

        /// <summary>Billed requests for one match of `humans` (all on the relay): messages / 20, connections, the room, alarms.</summary>
        public static double RequestsPerMatch(int humans, double matchMessagesPerSecond, double matchSeconds = MatchSeconds, double lobbySeconds = LobbySeconds)
        {
            double msgs = matchMessagesPerSecond * matchSeconds + LobbyMessagesPerSecond(humans) * lobbySeconds;
            double minutes = Math.Ceiling((matchSeconds + lobbySeconds) / 60.0);
            return Math.Ceiling(msgs / MessagesPerRequest) + humans + 1 + minutes + 1;
        }

        /// <summary>Whole matches of this size the free tier allows a day if nothing else uses it.</summary>
        public static int MatchesPerDay(double requestsPerMatch) => requestsPerMatch <= 0 ? int.MaxValue : (int)Math.Floor(FreeRequestsPerDay / requestsPerMatch);

        public static int MatchesPerDay(int humans, bool preB15 = false) => MatchesPerDay(RequestsPerMatch(humans, MatchMessagesPerSecond(humans, preB15)));

        /// <summary>Matches of this size left today, given the requests already used (from the relay's /usage count).</summary>
        public static int MatchesLeft(int humans, double requestsUsedToday)
        {
            double left = Math.Max(0, FreeRequestsPerDay - requestsUsedToday);
            return (int)Math.Floor(left / RequestsPerMatch(humans, MatchMessagesPerSecond(humans)));
        }

        /// <summary>
        /// The host's plain line for the room screen ("Online today: about 24 of 27 matches of 8 players left."), or a warning
        /// when the day's allowance is nearly or wholly used. Direct and local-network play never count.
        /// </summary>
        public static string HostLine(int humans, double requestsUsedToday)
        {
            int n = Math.Max(2, humans);
            int left = MatchesLeft(n, requestsUsedToday);
            int full = MatchesPerDay(n);
            if (left <= 0) return "Online rooms are full for today; they open again at midnight UTC. Friends on your network, or who type your address, can still join.";
            if (left <= 1) return "Online rooms are nearly full for today: about 1 match left. Friends on your network can still join for free.";
            return "Online today: about " + left + " of " + full + " matches of " + n + " players left.";
        }
    }
}
