using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net
{
    /// <summary>One human in an online match: their peer number, the fighter (actor id) they play, and their room picks.</summary>
    public struct HumanSlot
    {
        public int Peer;
        public int Actor;
        public ClassId Class;
        public string Name;
    }

    /// <summary>
    /// Everything every machine needs to build the identical world and run the same lockstep: sent once by the host in START.
    /// Slot 0 is always the host and always actor 0 (the host's team is the world's HumanTeam).
    /// </summary>
    public sealed class MatchSetup
    {
        public const int MaxHumans = 32;

        public int Seed;
        public int TeamSize = 16;
        public TeamId HostTeam = TeamId.Azure;
        public double Pace;
        public BotDifficulty BotDifficulty;
        public BotDifficulty AllyBotDifficulty;
        /// <summary>Starting input delay in ticks (each peer adapts its own lead from here).</summary>
        public int InputDelay = 4;
        /// <summary>Ticks between state-hash checks (30 = once a second).</summary>
        public int HashEvery = 30;
        public HumanSlot[] Humans = new HumanSlot[0];

        public int SlotOfPeer(int peer)
        {
            for (int i = 0; i < Humans.Length; i++) if (Humans[i].Peer == peer) return i;
            return -1;
        }

        /// <summary>The world options every machine creates the match from (identical bytes in, identical world out).</summary>
        public WorldOptions ToWorldOptions(GameConfig cfg)
        {
            int extra = Humans.Length > 0 ? Humans.Length - 1 : 0;
            var ids = new int[extra];
            var cls = new ClassId[extra];
            for (int i = 0; i < extra; i++)
            {
                ids[i] = Humans[i + 1].Actor;
                cls[i] = Humans[i + 1].Class;
            }
            return new WorldOptions
            {
                Seed = Seed,
                TeamSize = TeamSize,
                HumanTeam = HostTeam,
                HumanClass = Humans.Length > 0 ? Humans[0].Class : ClassId.Warrior,
                Config = cfg,
                BotDifficulty = BotDifficulty,
                AllyBotDifficulty = AllyBotDifficulty,
                Pace = Pace,
                ExtraHumans = ids,
                ExtraHumanClasses = cls,
            };
        }

        /// <summary>
        /// Actor ids for a room's humans: the host is actor 0; others on the host's team take ids 1, 2, ...; others on the
        /// other team take n, n+1, ... in room order. Returns false (and leaves actors alone) if a team is over-full.
        /// </summary>
        public static bool AssignActors(int teamSize, TeamId hostTeam, TeamId[] teams, int[] actorsOut)
        {
            int same = 1, other = 0;
            for (int i = 0; i < teams.Length; i++)
            {
                if (i == 0) { actorsOut[0] = 0; continue; }
                if (teams[i] == hostTeam) { if (same >= teamSize) return false; actorsOut[i] = same++; }
                else { if (other >= teamSize) return false; actorsOut[i] = teamSize + other++; }
            }
            return true;
        }

        public void Write(ref WireWriter w)
        {
            w.I32(Seed);
            w.U8(TeamSize);
            w.U8((int)HostTeam);
            w.U64((ulong)System.BitConverter.DoubleToInt64Bits(Pace));
            w.U8((int)BotDifficulty);
            w.U8((int)AllyBotDifficulty);
            w.U8(InputDelay);
            w.U8(HashEvery);
            w.U8(Humans.Length);
            for (int i = 0; i < Humans.Length; i++)
            {
                w.U8(Humans[i].Peer);
                w.U8(Humans[i].Actor);
                w.U8((int)Humans[i].Class);
                w.Str(Humans[i].Name, 16);
            }
        }

        /// <summary>Reads and validates a setup; null if anything is out of range (a hostile or broken host).</summary>
        public static MatchSetup Read(ref WireReader r, GameConfig cfg)
        {
            var s = new MatchSetup
            {
                Seed = r.I32(),
                TeamSize = r.U8(),
                HostTeam = (TeamId)r.U8(),
                Pace = System.BitConverter.Int64BitsToDouble((long)r.U64()),
                BotDifficulty = (BotDifficulty)r.U8(),
                AllyBotDifficulty = (BotDifficulty)r.U8(),
                InputDelay = r.U8(),
                HashEvery = r.U8(),
            };
            int n = r.U8();
            if (r.Bad || n < 1 || n > MaxHumans || s.TeamSize < 1 || s.TeamSize > cfg.TeamSizeMax) return null;
            if ((int)s.HostTeam > 1 || (int)s.BotDifficulty > 2 || (int)s.AllyBotDifficulty > 2) return null;
            if (s.InputDelay < 1 || s.InputDelay > 30 || s.HashEvery < 1) return null;
            if (!(double.IsFinite(s.Pace) && s.Pace >= 0 && s.Pace <= 8)) return null;
            s.Humans = new HumanSlot[n];
            for (int i = 0; i < n; i++)
            {
                s.Humans[i].Peer = r.U8();
                s.Humans[i].Actor = r.U8();
                int c = r.U8();
                s.Humans[i].Name = r.Str(16);
                if (c >= ClassIds.All.Count) return null;
                s.Humans[i].Class = (ClassId)c;
                if (s.Humans[i].Actor >= 2 * s.TeamSize) return null;
                for (int j = 0; j < i; j++) if (s.Humans[j].Actor == s.Humans[i].Actor || s.Humans[j].Peer == s.Humans[i].Peer) return null;
            }
            if (r.Bad || s.Humans[0].Actor != 0 || s.Humans[0].Peer != Peers.Host) return null;
            return s;
        }
    }
}
