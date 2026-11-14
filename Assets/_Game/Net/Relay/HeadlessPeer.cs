using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net.Relay
{
    /// <summary>
    /// A whole online peer with no screen: joins or hosts a room on the real relay, readies up, plays the match with the
    /// autoplay pilot as its human, and writes every state fingerprint to a log ("tick hash" per line) so a harness can
    /// compare two machines line by line. Used by the console peer (tools/online/HeadlessPeer, no Unity) and by the game's
    /// own -onlineHost / -onlineJoin flags (Runtime/Online/OnlineHeadless.cs, Unity's runtime): the same code on both, so a
    /// difference between the two logs is a difference between the runtimes, nothing else.
    /// Exit codes: 0 the match ran to the end (or to Ticks), 2 desync, 3 the room closed or the host left, 4 timed out,
    /// 5 could not create or join.
    /// </summary>
    public sealed class HeadlessPeer
    {
        public string RelayUrl;
        public bool Host;
        public string Code;
        public string Key;
        public string Name = "Headless";
        public TeamId Team = TeamId.Azure;
        public ClassId Class = ClassId.Warrior;
        /// <summary>Host: start once this many humans (the host included) are in and ready.</summary>
        public int Humans = 2;
        /// <summary>Stop after this many ticks (0 = the whole match).</summary>
        public int Ticks;
        public int TeamSize = 16;
        public double Pace = 1;
        public int Seed;
        public string HashLogPath;
        /// <summary>Host: the room code is also written here (a harness hands it to the joiner).</summary>
        public string CodeOutPath;
        public int TimeoutSeconds = 900;
        public string Build = "headless";
        public Action<string> Log = Console.WriteLine;

        public int Run()
        {
            var cfg = GameConfig.CreateDefault();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long deadline = TimeoutSeconds * 1000L;
            RelayTransport t;
            try
            {
                if (Host && string.IsNullOrEmpty(Code))
                {
                    (string newCode, string hostKey) = RelayTransport.CreateRoomAsync(RelayUrl, CancellationToken.None).GetAwaiter().GetResult();
                    Code = newCode;
                    Key = hostKey;
                }
                Code = RelayTransport.CleanCode(Code);
                if (Code == null) { Log("ONLINE ERROR that is not a room code"); return 5; }
                if (Host)
                {
                    Log("ROOM CODE " + Code);
                    if (!string.IsNullOrEmpty(CodeOutPath)) File.WriteAllText(CodeOutPath, Code + "\n");
                }
                t = RelayTransport.Connect(RelayUrl, Code, Host ? Key : null);
            }
            catch (RoomException e) { Log("ONLINE ERROR " + e.Message); return 5; }

            var hashes = new List<KeyValuePair<int, ulong>>();
            var pilot = new AutoplayPilot((uint)(Host ? 101 : 202) ^ (uint)Name.Length);
            LockstepSession session = null;
            RoomHost room = null;
            RoomClient client = null;
            if (Host) room = new RoomHost(t, cfg, Name, Build) { TeamSize = TeamSize, Pace = Pace };
            else
            {
                client = new RoomClient(t, cfg, Name, Build);
                client.SetPick(Team, Class);
                client.SetReady(true);
            }
            bool picked = false;
            int code = 4;
            while (clock.ElapsedMilliseconds < deadline)
            {
                long now = clock.ElapsedMilliseconds;
                if (Host)
                {
                    if (!picked && t.LocalPeer == Peers.Host) { room.SetLocalPick(Team, Class); picked = true; }
                    room.Update(now);
                    if (session == null && room.Members.Count >= Humans && room.CanStart(out _))
                    {
                        session = room.Start(now, null, Seed);
                        if (session != null) Hook(session, hashes, pilot);
                    }
                    if (session == null && room.Phase == RoomPhase.Closed) { Log("ONLINE ERROR " + room.Notice); code = 3; break; }
                }
                else
                {
                    client.Update(now);
                    if (client.Phase == RoomPhase.Closed) { Log("ONLINE ERROR " + client.Error); code = client.Session == null ? 5 : 3; break; }
                    if (session == null && client.Session != null) { session = client.Session; Hook(session, hashes, pilot); }
                }
                if (session != null)
                {
                    if (session.Status == LockstepStatus.Desynced) { Log("ONLINE DESYNC at tick " + session.DesyncTick); code = 2; break; }
                    if (session.Status == LockstepStatus.HostLeft) { Log("ONLINE ERROR " + (session.ClosedReason ?? "the host left")); code = 3; break; }
                    if (session.Status == LockstepStatus.Ended || (Ticks > 0 && session.Stepped >= Ticks))
                    {
                        // Keep answering for two seconds so the other side gets the last bundles and fingerprints.
                        long linger = clock.ElapsedMilliseconds + 2000;
                        while (clock.ElapsedMilliseconds < linger) { if (Host) room.Update(clock.ElapsedMilliseconds); else client.Update(clock.ElapsedMilliseconds); Thread.Sleep(5); }
                        code = session.Status == LockstepStatus.Desynced ? 2 : 0;
                        break;
                    }
                }
                Thread.Sleep(2);
            }
            if (session != null)
                Log($"ONLINE SUMMARY peer {t.LocalPeer} actor {session.LocalActor} ticks {session.Stepped} final 0x{StateHash.Of(session.World):X16} " +
                    $"lead {session.Lead} late {session.LateInputs} stall {session.StallMs} ms sent {session.BytesSent} B status {session.Status}");
            if (!string.IsNullOrEmpty(HashLogPath))
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<int, ulong> kv in hashes) sb.Append(kv.Key).Append(' ').Append(kv.Value.ToString("X16")).Append('\n');
                File.WriteAllText(HashLogPath, sb.ToString());
            }
            t.Close();
            Log("ONLINE EXIT " + code);
            return code;
        }

        private static void Hook(LockstepSession s, List<KeyValuePair<int, ulong>> hashes, AutoplayPilot pilot)
        {
            s.Sampler = pilot.Fill;
            s.HashRecorded = (tick, h) => hashes.Add(new KeyValuePair<int, ulong>(tick, h));
        }

        /// <summary>Reads the shared flags (the same names for the console peer and the game player).</summary>
        public static HeadlessPeer FromArgs(string[] args, string defaultRelay)
        {
            var p = new HeadlessPeer { RelayUrl = Environment.GetEnvironmentVariable("CM_RELAY_URL") ?? defaultRelay };
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a.TrimStart('-').ToLowerInvariant())
                {
                    case "onlinehost": case "host": p.Host = true; break;
                    case "onlinejoin": case "join": p.Code = next; i++; break;
                    case "onlinecode": case "code": p.Code = next; i++; break;
                    case "onlinekey": case "key": p.Key = next; i++; break;
                    case "onlinerelay": case "relay": p.RelayUrl = next; i++; break;
                    case "onlinename": case "name": p.Name = next; i++; break;
                    case "onlineteam": case "team": p.Team = (next ?? "").ToLowerInvariant().StartsWith("c") ? TeamId.Crimson : TeamId.Azure; i++; break;
                    case "onlineclass": case "class":
                        if (Enum.TryParse(next, true, out ClassId c)) p.Class = c;
                        i++;
                        break;
                    case "onlinehumans": case "humans": int.TryParse(next, out p.Humans); i++; break;
                    case "onlineticks": case "ticks": int.TryParse(next, out p.Ticks); i++; break;
                    case "onlineteamsize": case "teamsize": int.TryParse(next, out p.TeamSize); i++; break;
                    case "onlineseed": case "seed": int.TryParse(next, out p.Seed); i++; break;
                    case "onlinepace": case "pace": double.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.Pace); i++; break;
                    case "onlinehashlog": case "hash-log": p.HashLogPath = next; i++; break;
                    case "onlinecodeout": case "code-out": p.CodeOutPath = next; i++; break;
                    case "onlinetimeout": case "timeout": int.TryParse(next, out p.TimeoutSeconds); i++; break;
                    case "onlinebuild": case "build": p.Build = next; i++; break;
                }
            }
            return p;
        }
    }
}
