using System;
using System.Runtime.InteropServices;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Sim.Config;

// online-rooms-2: console online peer. Flags as in HeadlessPeer.FromArgs (with or without the "online" prefix):
//   -host [-code C -key K] [-humans 2] [-code-out file]    host a room (creates one unless code and key are given)
//   -join CODE                                             join a room
//   -relay URL (or env CM_RELAY_URL)  -ticks N  -hash-log file  -name N  -team azure|crimson  -class Miner  -teamsize 16
//   -direct-port P [-no-relay] [-lan]   host: also (or only) take direct joiners on TCP port P (b15-zero-cost)
//   -direct HOST:PORT [-join CODE]      join directly; with a code, fall back to the relay room if the host cannot be reached
//   -build B   the version string sent to the host (default "headless"); a host refuses a joiner whose B differs, so
//              a console peer that meets a built game passes that game's commit (tools/online/player-stamp.py --netbuild)
// b12-w: prints one version line before it joins and one at match start (the same format as the game's hidden peer):
//   [CM.Online] build=<B> config=<ConfigHash, 16 hex> protocol=<NetVersion.Protocol> platform=<os>/<arch> runtime=.. netbuild=<B> phase=join|start
public static class Program
{
    public static int Main(string[] args)
    {
        HeadlessPeer p = HeadlessPeer.FromArgs(args, null);
        // b15-zero-cost: a direct-only host (-no-relay) or a direct joiner without a code needs no relay.
        bool relayNeeded = p.Host ? !p.NoRelay : !string.IsNullOrEmpty(p.Code);
        if (relayNeeded && string.IsNullOrEmpty(p.RelayUrl)) { Console.Error.WriteLine("cm-online-peer: give -relay URL or set CM_RELAY_URL"); return 64; }
        if (!p.Host && string.IsNullOrEmpty(p.Code) && string.IsNullOrEmpty(p.DirectAddr)) { Console.Error.WriteLine("cm-online-peer: give -host, -join CODE or -direct HOST:PORT"); return 64; }
        Action<string> write = Console.WriteLine;
        string config = ConfigHash.Of(GameConfig.CreateDefault()).ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        string Line(string phase) =>
            "[CM.Online] build=" + p.Build + " config=" + config + " protocol=" + NetVersion.Protocol.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " platform=" + Platform() + " runtime=dotnet" + Environment.Version.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " netbuild=" + p.Build + " phase=" + phase;
        p.Log = s =>
        {
            write(s);
            if (s.StartsWith("ONLINE SETUP", StringComparison.Ordinal)) write(Line("start"));
        };
        write(Line("join"));
        return p.Run();
    }

    private static string Platform()
    {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "other";
        return os + "/" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    }
}
