using System;
using CriticalMass.Net.Relay;

// online-rooms-2: console online peer. Flags as in HeadlessPeer.FromArgs (with or without the "online" prefix):
//   -host [-code C -key K] [-humans 2] [-code-out file]    host a room (creates one unless code and key are given)
//   -join CODE                                             join a room
//   -relay URL (or env CM_RELAY_URL)  -ticks N  -hash-log file  -name N  -team azure|crimson  -class Miner  -teamsize 16
public static class Program
{
    public static int Main(string[] args)
    {
        HeadlessPeer p = HeadlessPeer.FromArgs(args, null);
        if (string.IsNullOrEmpty(p.RelayUrl)) { Console.Error.WriteLine("cm-online-peer: give -relay URL or set CM_RELAY_URL"); return 64; }
        if (!p.Host && string.IsNullOrEmpty(p.Code)) { Console.Error.WriteLine("cm-online-peer: give -host or -join CODE"); return 64; }
        return p.Run();
    }
}
