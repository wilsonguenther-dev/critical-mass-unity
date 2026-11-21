// b16 mechanics fixes (mechanics audit 2026-11-21, design advisor 2026-11-21). Every number lives in Sim/Config/MechRulesConfig.cs.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>b16 state on the World (World.Mech). Fixed arrays, sized once; reset in place. Integers only.</summary>
    public sealed class MechState
    {
        /// <summary>Per actor: ticks since a human's frame last carried input (IDLE PLAYERS).</summary>
        public int[] IdleTicks;
        /// <summary>Per actor: 1 while the autopilot plays an idle human's fighter.</summary>
        public byte[] Auto;
        /// <summary>Per actor: 1 while a hill-job bot holds the INTERCEPT job because its team is at the hill cap.</summary>
        public byte[] Surplus;
        /// <summary>Per actor: the first tick its intercept / hill switch may change again.</summary>
        public int[] SurplusLock;
        /// <summary>Per team: this tick's weighted bodies on the hill before the cap, and what counted after it.</summary>
        public readonly int[] HillWeighted = new int[2], HillCounted = new int[2];
        /// <summary>Counters (tests, the balance probe): autopilot take-overs and hand-backs; intercept switches.</summary>
        public int Takeovers, Handbacks, Intercepts;

        public void Init(World w)
        {
            int cap = World.ActorCapacity(w.Config);
            IdleTicks = new int[cap];
            Auto = new byte[cap];
            Surplus = new byte[cap];
            SurplusLock = new int[cap];
            Clear();
        }

        public void Reset(World w) => Clear();

        void Clear()
        {
            Array.Clear(HillWeighted, 0, 2);
            Array.Clear(HillCounted, 0, 2);
            Takeovers = Handbacks = Intercepts = 0;
            if (IdleTicks == null) return;
            Array.Clear(IdleTicks, 0, IdleTicks.Length);
            Array.Clear(Auto, 0, Auto.Length);
            Array.Clear(Surplus, 0, Surplus.Length);
            Array.Clear(SurplusLock, 0, SurplusLock.Length);
        }
    }

    /// <summary>
    /// b16 mechanics fixes, the sim side. Deterministic: integer ticks, no RNG, no allocation in a step. Full profile only (every
    /// rule reads its switch through DepthRules.On), so the Classic and PhysicsOnly golden checksums are untouched.
    /// </summary>
    public static class MechRules
    {
        static MechRulesConfig K(World w) => w.Config.Depth.Mech;

        static int Ticks(World w, double seconds) => (int)Math.Floor(seconds * w.Config.SimHz + 0.5);

        // ------------------------------------------------------------------ IDLE PLAYERS

        /// <summary>The idle-player rule runs in this world (Full rules, the switch on, bots not passive).</summary>
        public static bool AfkOn(World w) =>
            K(w) != null && DepthRules.On(w, K(w).AfkAutopilot) && !(w.Config.Bots.PassiveSeconds > 0);

        /// <summary>The frame carries something a person did: a move, a button, a vote or a ping (the aim alone does not).</summary>
        public static bool HasInput(InputFrame f)
        {
            if (f == null) return false;
            return Math.Abs(f.MoveX) > 0.05 || Math.Abs(f.MoveZ) > 0.05 || f.Attack || f.Ability || f.Interact || f.Terraform || f.Jump
                || f.TerraformSwitch || f.Dodge || f.StationBuy != 0 || f.PingKind != 0;
        }

        /// <summary>
        /// MatchTick, for every human frame before it is applied: true = the autopilot plays actor id this tick (its frame is
        /// replaced by the bot input, the same input a dropped player's fighter gets). The first frame with input hands the
        /// fighter back on that tick. Every machine sees the same frames, so every machine decides the same.
        /// </summary>
        public static bool AutopilotFrame(World w, int id, InputFrame f)
        {
            MechState s = w.Mech;
            if (s == null || s.IdleTicks == null || id < 0 || id >= s.IdleTicks.Length) return false;
            if (!AfkOn(w) || w.Match.Phase != MatchPhase.Playing)
            {
                s.IdleTicks[id] = 0;
                s.Auto[id] = 0;
                return false;
            }
            if (HasInput(f))
            {
                s.IdleTicks[id] = 0;
                if (s.Auto[id] != 0)
                {
                    s.Auto[id] = 0;
                    s.Handbacks++;
                }
                return false;
            }
            if (s.IdleTicks[id] < int.MaxValue / 2) s.IdleTicks[id]++;
            if (s.IdleTicks[id] < Math.Max(1, Ticks(w, K(w).AfkSeconds))) return false;
            if (s.Auto[id] == 0)
            {
                s.Auto[id] = 1;
                s.Takeovers++;
            }
            return true;
        }

        /// <summary>The autopilot is playing idle human id right now (HUD: "A bot is playing for you").</summary>
        public static bool Autopiloted(World w, int id) =>
            w != null && w.Mech != null && w.Mech.Auto != null && id >= 0 && id < w.Mech.Auto.Length && w.Mech.Auto[id] != 0;

        /// <summary>A person is at the controls of actor id (a human, not on the idle autopilot). Actor 0 in a bot match (null
        /// frames) is on the plain autopilot: the caller passes autopilot0 = BotsState.HumanDriven.</summary>
        public static bool AtControls(World w, int id, bool autopilot0)
        {
            Actor a = w.Actors[id];
            if (!a.IsHuman) return false;
            if (id == 0 && autopilot0) return false;
            return !Autopiloted(w, id);
        }

        /// <summary>BotDirector: the extra Kaiju feeders (ahead / behind): a share of the team with the rule on, else extra.</summary>
        public static int FeedExtra(World w, int extra)
        {
            MechRulesConfig k = K(w);
            if (k == null || !DepthRules.On(w, k.FeedExtrasShare)) return extra;
            return Math.Max(0, (int)Math.Ceiling(k.FeedExtraShare * w.Opts.TeamSize - 1e-9));
        }

        // ------------------------------------------------------------------ THE HILL COUNT

        /// <summary>Bodies actor id counts for on the hill (WarriorHoldWeight for a Warrior with the rule on, else 1).</summary>
        public static int HillWeight(World w, int id)
        {
            MechRulesConfig k = K(w);
            if (k == null || k.WarriorHoldWeight <= 1 || !DepthRules.On(w, true)) return 1;
            return w.Actors[id].Cls == ClassId.Warrior ? k.WarriorHoldWeight : 1;
        }

        /// <summary>The cap on a team's counted hill bodies at this world's team size (int.MaxValue = no cap).</summary>
        public static int HillCap(World w)
        {
            MechRulesConfig k = K(w);
            if (k == null || k.HillContributorCap <= 0 || !DepthRules.On(w, true)) return int.MaxValue;
            return CapFor(k, w.Opts.TeamSize);
        }

        /// <summary>round(cap x size / reference), at least 1 (10 at 16, 5 at 8, 3 at 5).</summary>
        public static int CapFor(MechRulesConfig k, int teamSize)
        {
            int refSize = Math.Max(1, k.HillCapReferenceTeam);
            return Math.Max(1, (k.HillContributorCap * teamSize * 2 + refSize) / (2 * refSize));
        }

        /// <summary>Hill.HillSystem: stores and returns the weighted bodies after the cap (what owns and contests the hill).</summary>
        public static int Counted(World w, int team, int weighted)
        {
            int cap = HillCap(w);
            int c = weighted > cap ? cap : weighted;
            if (w.Mech != null)
            {
                w.Mech.HillWeighted[team] = weighted;
                w.Mech.HillCounted[team] = c;
            }
            return c;
        }

        /// <summary>The team's counted hill bodies have reached the cap this tick (HUD tint, bots).</summary>
        public static bool AtCap(World w, TeamId team)
        {
            int cap = HillCap(w);
            return cap != int.MaxValue && w.Mech != null && w.Mech.HillWeighted[(int)team] >= cap;
        }

        // ------------------------------------------------------------------ KAIJU WAITS OFF THE CIRCLE

        /// <summary>The rule is on for this world.</summary>
        public static bool KaijuIdleOn(World w) => K(w) != null && DepthRules.On(w, K(w).KaijuIdleOffHill);

        /// <summary>Radius of the idle circle: hill radius + Kaiju radius + margin (outside the KAIJU HOLDS NOTHING ring).</summary>
        public static double KaijuIdleRadius(World w) => w.Config.Hill.Radius + w.Config.Kaiju.Radius + K(w).KaijuIdleMargin;

        // ------------------------------------------------------------------ HEALER FOCUS

        public static bool HealerTieBreak(World w) => K(w) != null && DepthRules.On(w, K(w).HealerTieBreak);
    }
}
