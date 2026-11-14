using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Everything the HUD shows, copied out of the Sim once per frame by HudBinder.Read. Plain fields, reused every frame
    /// (no allocation). The HUD never writes the Sim; a capture may overwrite this copy (GameHud.PreviewOverride) to show a
    /// state the match has not reached, which changes only what is drawn.
    /// </summary>
    public sealed class HudState
    {
        // match (win condition first)
        public double ClockRemaining;
        public bool Ended;
        public MatchResult Winner;
        /// <summary>Score = seconds of hill held (Match.DecideWinner compares HillSeconds).</summary>
        public int ScoreAzure, ScoreCrimson;
        public int OnHillAzure, OnHillCrimson;
        public HillState Hill;
        /// <summary>Azure's share of held hill time, 0.5 when nobody has held it yet.</summary>
        public float HillShareAzure;

        // Kaiju second
        public KaijuState Kaiju;
        public bool Hatched;
        public double UntilHatch;
        /// <summary>Egg: hatch progress; hatched: Kaiju health. 0..1.</summary>
        public float KaijuMeter01;
        /// <summary>Instability over the berserk threshold, 0..1.</summary>
        public float Instability01;
        public bool BerserkLocked;
        public float AllegianceAzure, AllegianceCrimson;

        // player third
        public bool HasPlayer;
        public TeamId Team;
        public ClassId Cls;
        public bool Alive;
        public float Hp, MaxHp;
        public float AttackCd01, AbilityCd01;
        public float AbilityCdSeconds;
        public CarryKind Carry;
        public int CarryAmount;
        public double RespawnIn;
        public float PlayerX, PlayerZ;
        public int Kills, Deaths;
        /// <summary>feat/light-weapons-2: a Healer whose attack press would swing the lantern now (nobody hurt to mend in
        /// range); the attack slot then reads LANTERN instead of MEND.</summary>
        public bool HealerLantern;

        public float Hp01 => MaxHp > 0 ? Math.Max(0f, Math.Min(1f, Hp / MaxHp)) : 0f;

        public void CopyFrom(HudState o)
        {
            ClockRemaining = o.ClockRemaining; Ended = o.Ended; Winner = o.Winner;
            ScoreAzure = o.ScoreAzure; ScoreCrimson = o.ScoreCrimson; OnHillAzure = o.OnHillAzure; OnHillCrimson = o.OnHillCrimson;
            Hill = o.Hill; HillShareAzure = o.HillShareAzure;
            Kaiju = o.Kaiju; Hatched = o.Hatched; UntilHatch = o.UntilHatch; KaijuMeter01 = o.KaijuMeter01;
            Instability01 = o.Instability01; BerserkLocked = o.BerserkLocked; AllegianceAzure = o.AllegianceAzure; AllegianceCrimson = o.AllegianceCrimson;
            HasPlayer = o.HasPlayer; Team = o.Team; Cls = o.Cls; Alive = o.Alive; Hp = o.Hp; MaxHp = o.MaxHp;
            AttackCd01 = o.AttackCd01; AbilityCd01 = o.AbilityCd01; AbilityCdSeconds = o.AbilityCdSeconds;
            Carry = o.Carry; CarryAmount = o.CarryAmount; RespawnIn = o.RespawnIn; PlayerX = o.PlayerX; PlayerZ = o.PlayerZ;
            Kills = o.Kills; Deaths = o.Deaths;
            HealerLantern = o.HealerLantern;
        }
    }

    /// <summary>Reads the Sim into a HudState. Read-only on the World; allocation-free (HudBinderTests).</summary>
    public static class HudBinder
    {
        /// <summary>The local player's actor id (actor 0 is the human, SimDriverBehaviour / HumanInputSource).</summary>
        public const int LocalActor = 0;

        public static void Read(World w, int localId, HudState s)
        {
            MatchState m = w.Match;
            GameConfig c = w.Config;
            // Timers read in real seconds at any match speed (Flow.MatchSpeed.DisplayPace; 1 in tests).
            s.ClockRemaining = Math.Max(0, global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(c.MatchSeconds - m.Time));
            s.Ended = m.Phase == MatchPhase.Ended;
            s.Winner = m.Winner;
            s.ScoreAzure = (int)Math.Floor(m.HillSeconds.Azure);
            s.ScoreCrimson = (int)Math.Floor(m.HillSeconds.Crimson);
            s.OnHillAzure = (int)m.HillCount.Azure;
            s.OnHillCrimson = (int)m.HillCount.Crimson;
            s.Hill = m.HillState;
            double held = m.HillSeconds.Azure + m.HillSeconds.Crimson;
            s.HillShareAzure = held > 1e-6 ? (float)(m.HillSeconds.Azure / held) : 0.5f;

            Kaiju k = w.Kaiju;
            s.Kaiju = k.State;
            s.Hatched = m.Hatched;
            s.UntilHatch = Math.Max(0, global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(c.HatchSeconds - m.Time));
            if (!m.Hatched || k.State == KaijuState.Egg)
                s.KaijuMeter01 = c.HatchSeconds > 0 ? Clamp01((float)(m.Time / c.HatchSeconds)) : 1f;
            else
                s.KaijuMeter01 = k.MaxHp > 0 ? Clamp01((float)(k.Hp / k.MaxHp)) : 0f;
            double thr = c.KaijuFeed.BerserkThreshold;
            s.Instability01 = thr > 0 ? Clamp01((float)(m.Instability / thr)) : 0f;
            s.BerserkLocked = m.BerserkLocked;
            s.AllegianceAzure = (float)m.Allegiance.Azure;
            s.AllegianceCrimson = (float)m.Allegiance.Crimson;

            s.HasPlayer = localId >= 0 && localId < w.ActorCount;
            if (!s.HasPlayer) return;
            Actor a = w.Actors[localId];
            ClassConfig cc = c.Classes[(int)a.Cls];
            s.Team = a.Team;
            s.Cls = a.Cls;
            s.Alive = a.Alive;
            s.Hp = (float)Math.Max(0, a.Hp);
            s.MaxHp = (float)a.MaxHp;
            // feat/light-weapons-2: a light-weapon swing sets its own cooldown; the slot sweeps over that length.
            LightWeapon lw = LightWeapons.Of(w, a.Cls);
            double atkCd = lw != null && a.Cls != ClassId.Healer ? lw.CooldownSeconds : cc.AttackCooldown;
            s.AttackCd01 = atkCd > 0 ? Clamp01((float)(a.AttackCd / atkCd)) : 0f;
            bool aim = localId < w.HasAim.Length && w.HasAim[localId] == 1;
            s.HealerLantern = a.Cls == ClassId.Healer && lw != null && a.Alive
                && Healer.MendTarget(w, localId, aim ? w.Inputs[localId].AimX : double.NaN, aim ? w.Inputs[localId].AimZ : double.NaN) < 0;
            s.AbilityCd01 = cc.AbilityCooldown > 0 ? Clamp01((float)(a.AbilityCd / cc.AbilityCooldown)) : 0f;
            s.AbilityCdSeconds = (float)Math.Max(0, a.AbilityCd);
            s.Carry = a.Carry;
            s.CarryAmount = a.CarryAmount;
            s.RespawnIn = a.Alive ? 0 : Math.Max(0, global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(a.RespawnIn));
            s.PlayerX = (float)w.X[localId];
            s.PlayerZ = (float)w.Z[localId];
            s.Kills = a.Kills;
            s.Deaths = a.Deaths;
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
