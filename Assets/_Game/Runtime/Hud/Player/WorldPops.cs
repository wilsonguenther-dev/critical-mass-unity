using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 23 (A17, P5 "my share of objectives pops where it happens"): pills with a brass rim from the WorldWords pool for MY
    /// part in the match: +37 ALLEGIANCE (my feed), CORRUPTED +N, +1 ORE 3/5 (my mining), HUT 4/6 (my bank toward the next
    /// hut level), HUT LEVEL 2 (a level I funded), WALL BUILT, BRIDGE REPAIRED, PURIFIED, every WorldAction kind (dig,
    /// place, build, mine, bank, feed, corrupt, purify, repair), and YOU HOLD THE HILL when removing my body would flip the
    /// hill (computed read-only from the counted hill bodies; shown on the rising edge, again every HillRepeat s while
    /// true). Objective credit reports ObjectivePop, world verbs WorldActionPop (reason = PlayerReasons.World*).
    /// </summary>
    public sealed class WorldPops
    {
        public const float Seconds = 1.5f, Lift = 2.6f, HillRepeat = 10f;

        /// <summary>The words of a WorldAction kind (pure; tests: every PlayerReasons.World* code has words).</summary>
        public static string WorldWordsFor(int kind, int amount)
        {
            switch (kind)
            {
                case PlayerReasons.WorldDig: return "DUG";
                case PlayerReasons.WorldPlace: return "PLACED";
                case PlayerReasons.WorldBuild: return "BUILT";
                case PlayerReasons.WorldMineOre: return amount > 1 ? "+" + amount + " ORE" : "+1 ORE";
                case PlayerReasons.WorldBank: return "BANKED";
                case PlayerReasons.WorldFeed: return "FED THE EGG";
                case PlayerReasons.WorldCorrupt: return "CORRUPTED";
                case PlayerReasons.WorldPurify: return "PURIFIED";
                case PlayerReasons.WorldRepair: return "REPAIRED";
                case ActionTags.WorldGather: return "GATHERED";
                default: return "DONE";
            }
        }

        /// <summary>The words of a Structure* kind (pure; tests).</summary>
        public static string StructureWordsFor(int kind)
        {
            switch (kind)
            {
                case PlayerReasons.StructureBarricade: return "BARRICADE";
                case PlayerReasons.StructureBuiltWall: return "WALL";
                case PlayerReasons.StructureBreakable: return "BREAKABLE";
                case PlayerReasons.StructurePiece: return "LADDER / BRIDGE";
                case PlayerReasons.StructureSolid: return ActionTags.CantBreak;
                default: return "STRUCTURE";
            }
        }

        /// <summary>
        /// True when the local player's body decides the hill (pure; tests): my team owns it on counted bodies, and with my
        /// weight taken off (then capped again) my team would no longer have more counted bodies than theirs.
        /// </summary>
        public static bool IHoldTheHill(int myWeighted, int myWeight, int theirCounted, int cap, bool myTeamOwns)
        {
            if (!myTeamOwns || myWeight <= 0) return false;
            int without = Math.Max(0, myWeighted - myWeight);
            if (cap > 0 && without > cap) without = cap;
            return without <= theirCounted;
        }

        readonly WorldWords _words;
        float _hillAt = -100f;
        bool _hillWas;
        float _allegiancePrev = -1f;
        int _teamPrev = -1;

        public WorldWords Words => _words;
        public const string HoldWord = "YOU HOLD THE HILL";

        public WorldPops(RectTransform world)
        {
            _words = new WorldWords(world, "World Pop", 6);
        }

        public void Clear()
        {
            _words.Clear();
            _hillWas = false;
            _hillAt = -100f;
            _allegiancePrev = -1f;
        }

        Text Pop(string text, int key, Vector3 at, float lift)
            => _words.Show(text, key, -1, at, lift, PxPalette.Cream, PxPalette.Brass, true, Seconds);   // cream on navy, brass rim (brass type on navy reads dark)

        void Objective(World w, string text, int key, double x, double z, int me, float value)
        {
            Text t = Pop(text, 5000 + key, PxProject.Point(w, x, z, Lift), Lift);
            if (t != null) PlayerFeedback.Report(FeedbackChannel.ObjectivePop, me, value, t);
        }

        void Verb(World w, string text, int kind, double x, double z, int me, float value)
        {
            Text t = Pop(text, 6000 + kind, PxProject.Point(w, x, z, Lift), Lift);
            if (t != null) PlayerFeedback.Report(FeedbackChannel.WorldActionPop, me, value, t, kind);
        }

        static float Allegiance(World w, TeamId t) => (float)(t == TeamId.Azure ? w.Match.Allegiance.Azure : w.Match.Allegiance.Crimson);

        /// <summary>Called once a frame BEFORE the events: remembers the team's allegiance so a feed pops its real gain.</summary>
        public void BeforeEvents(World w, int me)
        {
            if (me < 0 || me >= w.ActorCount) return;
            int t = (int)w.Actors[me].Team;
            if (t != _teamPrev || _allegiancePrev < 0f) { _teamPrev = t; _allegiancePrev = Allegiance(w, (TeamId)t); }
        }

        /// <summary>The events of mine that earn a pop (the caller filters nothing; this checks A / B against me).</summary>
        public void OnEvent(World w, SimEvent e, int me)
        {
            switch (e.Type)
            {
                case SimEventType.DeliverBiomass:
                    if (e.A != me) return;
                    {
                        float now = Allegiance(w, w.Actors[me].Team);
                        int gain = Mathf.RoundToInt(now - _allegiancePrev);
                        _allegiancePrev = now;
                        Objective(w, gain > 0 ? "+" + gain + " ALLEGIANCE" : "+" + Mathf.RoundToInt((float)e.Value) + " FED", 1, e.X, e.Z, me, gain);
                    }
                    return;
                case SimEventType.DeliverCorruption:
                    if (e.A == me) Objective(w, "CORRUPTED +" + Mathf.RoundToInt((float)e.Value), 2, e.X, e.Z, me, (float)e.Value);
                    return;
                case SimEventType.OreMined:
                    if (e.A != me || e.Value < 0) return;
                    Objective(w, "+1 ORE  " + Mathf.RoundToInt((float)e.Value) + "/" + (int)w.Config.Depth.Hut.OreCarry, 3, e.X, e.Z, me, (float)e.Value);
                    return;
                case SimEventType.BankDeposit:
                    if (e.A != me || e.Bio != BiomassType.Ore || w.Hut == null) return;
                    {
                        int t = (int)w.Actors[me].Team;
                        double[] cost = w.Config.Depth.Hut.LevelCost;
                        int lvl = w.Hut.Level[t];
                        string text = cost != null && lvl < cost.Length ? "HUT " + w.Hut.Ore[t] + "/" + (int)cost[lvl] : "HUT MAX";
                        Objective(w, text, 4, e.X, e.Z, me, (float)e.Value);
                    }
                    return;
                case SimEventType.HutLevel:
                    if (e.B == me) Objective(w, "HUT LEVEL " + Mathf.RoundToInt((float)e.Value), 5, e.X, e.Z, me, (float)e.Value);
                    return;
                case SimEventType.WallBuilt:
                    if (e.A == me && e.Value > 0) Verb(w, "WALL BUILT", PlayerReasons.WorldBuild, e.X, e.Z, me, (float)e.Value);
                    return;
                case SimEventType.BridgeRepaired:
                    if (e.A == me && e.Value >= 0) Verb(w, "BRIDGE REPAIRED", PlayerReasons.WorldRepair, e.X, e.Z, me, (float)e.Value);
                    return;
                case SimEventType.Purify:
                    if (e.A == me) Verb(w, "PURIFIED", PlayerReasons.WorldPurify, e.X, e.Z, me, (float)e.Value);
                    return;
                case SimEventType.WorldAction:
                    // lane S2: Value 0 = a channel START (gather / mine): the ChannelBar shows it, the pop waits for the unit
                    if (e.A == me && e.Value <= 0 && (e.B == ActionTags.WorldGather || e.B == PlayerReasons.WorldMineOre)) return;
                    if (e.A == me) Verb(w, WorldWordsFor(e.B, Mathf.RoundToInt((float)e.Value)), e.B, e.X, e.Z, me, (float)e.Value);
                    return;
            }
        }

        public void Tick(World w, int me, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            if (me >= 0 && me < w.ActorCount) _allegiancePrev = Allegiance(w, w.Actors[me].Team);
            TickHill(w, me);
            _words.Tick(w, alpha, cam, scale, dt);
        }

        void TickHill(World w, int me)
        {
            bool hold = false;
            if (me >= 0 && me < w.ActorCount && w.Actors[me].Alive && w.Map != null)
            {
                double dx = w.X[me] - w.Map.Hill.X, dz = w.Z[me] - w.Map.Hill.Z, r = w.Config.Hill.Radius;
                if (dx * dx + dz * dz < r * r)
                {
                    TeamId team = w.Actors[me].Team;
                    int t = (int)team;
                    int cap = MechRules.HillCap(w);
                    int mine = w.Mech != null ? w.Mech.HillWeighted[t] : (int)(team == TeamId.Azure ? w.Match.HillCount.Azure : w.Match.HillCount.Crimson);
                    int theirs = w.Mech != null ? w.Mech.HillCounted[1 - t] : (int)(team == TeamId.Azure ? w.Match.HillCount.Crimson : w.Match.HillCount.Azure);
                    bool owns = w.Match.HillState == (team == TeamId.Azure ? HillState.Azure : HillState.Crimson);
                    hold = IHoldTheHill(mine, MechRules.HillWeight(w, me), theirs, cap == int.MaxValue ? 0 : cap, owns);
                }
            }
            float now = Time.unscaledTime;
            if (hold && (!_hillWas || now - _hillAt >= HillRepeat))
            {
                _hillAt = now;
                Text t = _words.Show(HoldWord, 5100, me, Vector3.zero, Lift + 0.3f, PxPalette.Cream, PxPalette.Brass, true, 2f);
                if (t != null)
                {
                    PlayerFeedback.Report(FeedbackChannel.ObjectivePop, me, 1f, t);
                }
            }
            _hillWas = hold;
        }
    }
}
