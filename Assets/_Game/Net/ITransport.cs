namespace CriticalMass.Net
{
    public enum TransportEventKind
    {
        /// <summary>A packet arrived from From. Data is valid only until the next Poll.</summary>
        Data,
        /// <summary>The relay accepted us; LocalPeer is now known (0 = the room's host).</summary>
        Connected,
        /// <summary>Peer From is now in the room.</summary>
        PeerJoined,
        /// <summary>Peer From left the room or its connection died.</summary>
        PeerLeft,
        /// <summary>Our own connection is gone (Reason says why, in plain words).</summary>
        Closed,
    }

    public struct TransportEvent
    {
        public TransportEventKind Kind;
        public int From;
        public byte[] Data;
        public int Length;
        public string Reason;
    }

    /// <summary>
    /// The only thing the lockstep and room code know about the network: numbered peers that exchange small packets. The
    /// loopback network (tests), the room relay over WebSocket (Runtime) and any later transport (UDP relay, Steam) implement
    /// it. Not thread-safe: Send and Poll are called from one thread (the game's main thread).
    /// </summary>
    public interface ITransport
    {
        /// <summary>Our peer number in the room (0 = host), or -1 before Connected.</summary>
        int LocalPeer { get; }

        /// <summary>
        /// True when packets arrive complete, once and in order (WebSocket over TCP): the lockstep layer then sends each item
        /// once. False for datagram links (loss, duplicates, reordering): it resends everything not yet acknowledged.
        /// </summary>
        bool Reliable { get; }

        /// <summary>Queue length bytes of data (copied) for peer to, or for every other peer when to == Broadcast.</summary>
        void Send(int to, byte[] data, int length);

        /// <summary>Next event, if any. Call until it returns false once per frame.</summary>
        bool Poll(out TransportEvent ev);

        /// <summary>Leave the room (idempotent).</summary>
        void Close();
    }

    public static class Peers
    {
        public const int Host = 0;
        public const int Broadcast = 255;
        /// <summary>Largest packet any part of the game sends (the relay refuses bigger frames).</summary>
        public const int MaxPacket = 1200;
    }
}
