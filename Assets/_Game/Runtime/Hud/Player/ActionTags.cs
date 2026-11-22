using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan items 13, 16, 18, 28 (P2 "a blocked hit says why", P6 "every button does something"): one word system for
    /// what my press did when it did not do what I wanted, shown WHERE it happened (WorldWords pills):
    /// my swing reached a body and dealt nothing or less (HitBlocked): IMMUNE / PROTECTED / DODGED / WARD / BLOCKED (a wall
    /// between) / SHIELD -N / TOO CLOSE -N, on the target; my wind-up was cancelled by a stagger (AttackCancel): INTERRUPTED,
    /// on me; a press the sim could not act on (ActionRefused, and the older per-system refusals OreMined &lt; 0, WallBuilt
    /// &lt; 0, DeliverRefused, JumpRefused, BridgeRepaired -2): the reason in plain words per button and class, on me; my
    /// feint: FEINT. Only the local player's events. One identical word a second at most.
    /// The wording tables are pure and public so the tests prove every PlayerReasons code has words (PxHudTagTests).
    /// </summary>
    public sealed class ActionTags
    {
        public const float Seconds = 1.1f, RefusalSeconds = 1.6f, TargetLift = 2.1f, SelfLift = 2.35f;
        /// <summary>S2's new HitBlocked code for the Farmer's point-blank glancing blow (TOO CLOSE). Read by name at run
        /// time from PlayerReasons when it lands (lane S2); until then this value. See FINDINGS "Needs from other lanes".</summary>
        public static readonly int BlockedGlancing = ReasonCode("BlockedGlancing", 8);
        // Lane S2's other new codes, keyed by NUMBER so the words exist before and after the combine (S2 67b56400).
        public const int ButtonStationBuy = 8, RefusedCarrying = 11, RefusedInAir = 12, RefusedRuleOff = 13, WorldGather = 10;

        /// <summary>A PlayerReasons constant by name (fallback when the lane that adds it has not merged yet).</summary>
        public static int ReasonCode(string name, int fallback)
        {
            System.Reflection.FieldInfo f = typeof(PlayerReasons).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return f != null && f.IsLiteral && f.FieldType == typeof(int) ? (int)f.GetRawConstantValue() : fallback;
        }

        // ------------------------------------------------------------------ words (pure)

        /// <summary>The word for a HitBlocked code; amount = damage lost (SHIELD / TOO CLOSE show it when over 0).</summary>
        public static string BlockedWords(int code, int amount)
        {
            switch (code)
            {
                case PlayerReasons.BlockedSpawnProtect: return "PROTECTED";
                case PlayerReasons.BlockedDodge: return "DODGED";
                case PlayerReasons.BlockedWard: return "WARD";
                case PlayerReasons.BlockedWall: return "BLOCKED";
                case PlayerReasons.BlockedPassive: return "IMMUNE";
                case PlayerReasons.BlockedImmune: return "IMMUNE";
                case PlayerReasons.BlockedShield: return amount > 0 ? Minus("SHIELD -", amount) : "SHIELD";
            }
            if (code == BlockedGlancing) return amount > 0 ? Minus("TOO CLOSE -", amount) : "TOO CLOSE";
            return "BLOCKED";
        }

        /// <summary>The name of a PlayerReasons.Button* code.</summary>
        public static string ButtonWords(int button)
        {
            switch (button)
            {
                case PlayerReasons.ButtonAttack: return "ATTACK";
                case PlayerReasons.ButtonAbility: return "SKILL";
                case PlayerReasons.ButtonDigPlace: return "DIG";
                case PlayerReasons.ButtonInteract: return "USE";
                case PlayerReasons.ButtonDodge: return "DODGE";
                case PlayerReasons.ButtonCancel: return "CANCEL";
                case PlayerReasons.ButtonJump: return "JUMP";
                case ButtonStationBuy: return "UPGRADE";
                default: return "PRESS";
            }
        }

        /// <summary>The plain reason for an ActionRefused (button, why) for a class; cooldown = seconds left (0 unknown).</summary>
        public static string RefusalWords(int button, int why, ClassId cls, int cooldown)
        {
            bool dig = button == PlayerReasons.ButtonDigPlace;
            bool builder = cls == ClassId.Builder;
            switch (why)
            {
                case PlayerReasons.RefusedCooldown: return Cooldown(cooldown);
                case PlayerReasons.RefusedNoTarget:
                    if (dig && cls == ClassId.Miner) return "NO ORE VEIN HERE";
                    if (dig) return "NOTHING TO DIG HERE";
                    if (cls == ClassId.Healer && (button == PlayerReasons.ButtonAttack || button == PlayerReasons.ButtonAbility)) return "NOBODY TO MEND";
                    if (button == PlayerReasons.ButtonInteract) return "NOTHING TO USE HERE";
                    return "NOTHING IN REACH";
                case PlayerReasons.RefusedNotYourClass:
                    if (dig) return "ONLY MINERS AND BUILDERS DIG";
                    if (button == PlayerReasons.ButtonAbility) return "NO SKILL ON THIS CLASS";
                    return "YOUR CLASS CAN'T DO THAT";
                case PlayerReasons.RefusedNoResource:
                    if (builder || dig) return "NEED WOOD";
                    if (cls == ClassId.Demolitions) return "NO TNT READY";
                    return "NOTHING TO SPEND";
                case PlayerReasons.RefusedBlockedPlace: return builder || button == PlayerReasons.ButtonAbility ? "CAN'T BUILD HERE" : dig ? "CAN'T DIG HERE" : "NOT HERE";
                case PlayerReasons.RefusedStaggered: return "STAGGERED";
                case PlayerReasons.RefusedCarryFull: return "CARRY FULL";
                case PlayerReasons.RefusedOutOfRange: return "OUT OF RANGE";
                case PlayerReasons.RefusedNothingToCancel: return "NOTHING TO CANCEL";
                case PlayerReasons.RefusedTeamLimit: return "TEAM LIMIT REACHED";
                case RefusedCarrying: return button == PlayerReasons.ButtonDodge ? "CAN'T DODGE WHILE CARRYING" : "PUT YOUR LOAD DOWN FIRST";
                case RefusedInAir: return "ALREADY IN THE AIR";
                case RefusedRuleOff: return button == ButtonStationBuy ? "STATION UPGRADES ARE OFF: THE HUT LEVELS THE TEAM" : "OFF IN THIS MATCH";
                default: return "CAN'T DO THAT NOW";
            }
        }

        /// <summary>Words for the older refusal events, as (words, PlayerReasons.Refused* code for the ledger).</summary>
        public static string OreRefusal(int code, out int reason)
        {
            switch (code)
            {
                case -1: reason = PlayerReasons.RefusedNoTarget; return "NO ORE VEIN HERE";
                case -2: reason = PlayerReasons.RefusedCarryFull; return "CARRY FULL";
                case -3: reason = PlayerReasons.RefusedCarryFull; return "HANDS FULL: DROP YOUR LOAD";
                case -4: reason = PlayerReasons.RefusedTeamLimit; return "HUT AT MAX LEVEL";
                default: reason = PlayerReasons.RefusedStaggered; return "CAN'T MINE RIGHT NOW";
            }
        }

        public static string WallRefusal(int code, out int reason)
        {
            reason = PlayerReasons.RefusedBlockedPlace;
            switch (code)
            {
                case -1: return "OFF THE MAP";
                case -2: return "CAN'T BUILD ON THIS GROUND";
                case -3: return "TOO CLOSE TO A BASE";
                case -4: return "SOMEONE IS IN THE WAY";
                case -5: reason = PlayerReasons.RefusedNoResource; return "NEED WOOD";
                case -7: return "WOULD BLOCK THE PATH";
                case -8: reason = PlayerReasons.RefusedCooldown; return "TOO FAST";
                case -9: reason = PlayerReasons.RefusedTeamLimit; return "WALL LIMIT REACHED";
                case -10: reason = PlayerReasons.RefusedStaggered; return "CAN'T BUILD WHILE DOWN";
                default: return "CAN'T BUILD HERE";
            }
        }

        public static string DeliverRefusal(int code) => code == 2 ? "YOUR CLASS CAN'T CORRUPT" : "YOUR CLASS CAN'T FEED THE EGG";
        public static string JumpRefusal(int code) => code == 2 ? "TOO LATE TO JUMP" : "NO ROOM TO LAND";
        public const string Interrupted = "INTERRUPTED", Feint = "FEINT", CantBreak = "CAN'T BREAK", NeedWoodRepair = "NEED WOOD TO REPAIR";

        static readonly string[] CooldownWords = BuildCooldown();
        static string[] BuildCooldown()
        {
            var a = new string[61];
            a[0] = "ON COOLDOWN";
            for (int i = 1; i < a.Length; i++) a[i] = "ON COOLDOWN " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s";
            return a;
        }
        public static string Cooldown(int seconds) => CooldownWords[Mathf.Clamp(seconds, 0, CooldownWords.Length - 1)];

        static readonly string[] ShieldMinus = new string[200], CloseMinus = new string[200];
        static string Minus(string head, int n)
        {
            string[] cache = head[0] == 'S' ? ShieldMinus : CloseMinus;
            n = Mathf.Clamp(n, 0, cache.Length - 1);
            return cache[n] ?? (cache[n] = head + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ showing

        readonly WorldWords _words;

        public WorldWords Words => _words;

        public ActionTags(RectTransform world)
        {
            _words = new WorldWords(world, "Action Tag", 8);
        }

        public void Clear() => _words.Clear();

        public void Tick(World w, double alpha, UnityEngine.Camera cam, float scale, float dt) => _words.Tick(w, alpha, cam, scale, dt);

        /// <summary>HitBlocked where I am the attacker (A target, Value code, Radius damage lost).</summary>
        public void OnBlocked(World w, int target, int code, double x, double z, double lost)
        {
            int amount = Mathf.RoundToInt((float)lost);
            string text = BlockedWords(code, amount);
            Vector3 at = PxProject.Point(w, x, z, TargetLift);
            Text t = _words.Show(text, 1000 + code * 4 + (target & 3), target >= 0 && target < w.ActorCount ? target : -1, at, TargetLift,
                PxPalette.StrikeInk, PxPalette.Strike, true, Seconds);
            if (t != null) PlayerFeedback.Report(FeedbackChannel.BlockedTag, target, amount, t, code);
        }

        public void OnInterrupted(World w, int me)
        {
            Text t = _words.Show(Interrupted, 2000, me, Vector3.zero, SelfLift, PxPalette.Cream, PxPalette.Hurt, true, Seconds);
            if (t != null) PlayerFeedback.Report(FeedbackChannel.InterruptedTag, me, 1f, t);
        }

        public void OnFeint(World w, int me) => _words.Show(Feint, 2001, me, Vector3.zero, SelfLift, PxPalette.Cream, PxPalette.Brass, false, 0.8f);

        /// <summary>Any refusal for me: words already chosen, reason = PlayerReasons.Refused* code.</summary>
        public void OnRefused(World w, int me, string text, int reason, int key)
        {
            Text t = _words.Show(text, 3000 + key, me, Vector3.zero, SelfLift, PxPalette.Cream, CriticalMass.Runtime.Hud.Style.CMTokens.Stone.Color, false, RefusalSeconds);
            if (t != null) PlayerFeedback.Report(FeedbackChannel.RefusalTag, me, key, t, reason);
        }

        /// <summary>ActionRefused for me (B button, Value why).</summary>
        public void OnActionRefused(World w, int me, int button, int why)
        {
            Actor a = w.Actors[me];
            int cd = 0;
            if (why == PlayerReasons.RefusedCooldown)
                cd = Mathf.CeilToInt((float)(button == PlayerReasons.ButtonAttack ? a.AttackCd : button == PlayerReasons.ButtonAbility ? a.AbilityCd : 0.0));
            OnRefused(w, me, RefusalWords(button, why, a.Cls, cd), why, button * 32 + why);
        }

        /// <summary>A structure that cannot be broken (StructureSolid, Value 0) answered my swing.</summary>
        public void OnCantBreak(World w, double x, double z, int kind)
        {
            Text t = _words.Show(CantBreak, 4000, -1, PxProject.Point(w, x, z, 1.6f), 1.6f, PxPalette.Cream, CriticalMass.Runtime.Hud.Style.CMTokens.Stone.Color, true, Seconds);
            if (t != null) PlayerFeedback.Report(FeedbackChannel.WorldActionPop, -1, 0f, t, kind);
        }
    }
}
