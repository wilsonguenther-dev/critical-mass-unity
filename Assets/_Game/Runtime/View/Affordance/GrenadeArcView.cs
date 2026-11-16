using System;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Affordance
{
    /// <summary>
    /// b12-jk (Jeisil's j: "grenades that read as grenades"), view only. Draws into TerrainAffordanceView's ONE mesh (no new
    /// draw call, no new texture; the procedural atlas's ring, disc and a solid patch):
    /// <list type="bullet">
    /// <item>while the local Demolitions player aims: an arc from the hand to the landing point (a pooled line of
    /// GrenadeTuning.ArcPoints points on TntArc's flight curve, ending exactly where Demolitions.ThrowTnt would land: the
    /// shared GrenadeRead.Landing clamp with the config's ThrowRange). The landing ring at BlastRadius is the target ghost
    /// TerrainAffordanceView already draws (now sized exactly);</item>
    /// <item>after any throw: the ring stays on the ground at the landing point and blinks faster as the fuse runs down
    /// (GrenadeRead.BlinkPhase from the live Bomb's ticks each frame; steady under Reduce flashing). A viewer standing
    /// inside the ring of a bomb that can hurt him sees it in the warning colour with a "!" over the centre;</item>
    /// <item>land mines: a small ring for the owner's team only (MineSight's rule; enemies keep seeing the mound alone).</item>
    /// </list>
    /// Read only on the Sim; allocation-free. Quads: arc 15, at most MaxLiveRings x 5 for fuse rings, MaxMineRings for mines.
    /// </summary>
    public sealed class GrenadeArcView
    {
        public static readonly Color32 Warning = new Color32(0xFF, 0x2E, 0x2E, 0xFF);
        static readonly Color32 White = new Color32(0xFF, 0xFF, 0xFF, 0xFF);

        /// <summary>The arc's points this frame (world space), pooled.</summary>
        public readonly Vector3[] ArcWorld = new Vector3[GrenadeTuning.ArcPoints];
        /// <summary>Points drawn this frame (0 = no arc), its landing (sim), the aim ring's radius (-1 = none).</summary>
        public int ArcPointsDrawn { get; private set; }
        public double ArcLandX { get; private set; }
        public double ArcLandZ { get; private set; }
        public float AimRingRadius { get; private set; } = -1f;
        /// <summary>Fuse rings, warned rings, "!" signs and mine rings drawn this frame.</summary>
        public int LiveRings { get; private set; }
        public int WarnRings { get; private set; }
        public int Bangs { get; private set; }
        public int MineRings { get; private set; }
        /// <summary>The most urgent fuse ring this frame: its radius, fuse left 0..1, blink period (s) and beat (-1 = none).</summary>
        public float RingRadius { get; private set; } = -1f;
        public float FuseLeft { get; private set; } = -1f;
        public float BlinkPeriod { get; private set; } = -1f;
        public bool BlinkOn { get; private set; }

        void ClearStats()
        {
            ArcPointsDrawn = 0;
            AimRingRadius = -1f;
            LiveRings = WarnRings = Bangs = MineRings = 0;
            RingRadius = FuseLeft = BlinkPeriod = -1f;
            BlinkOn = false;
        }

        /// <summary>
        /// One frame. local = the local player is in the match (alive, playing, not spectating); aim = his Demolitions TNT
        /// target this frame (null = no arc: another class, or the outline switch off); rings = the Signs switch.
        /// </summary>
        public void Draw(TerrainAffordanceView v, World w, bool local, AffTarget aim, bool rings, Vector3 right, Vector3 up, Vector3 fwd)
        {
            ClearStats();
            if (v == null || w == null || w.Map == null || !local) return;
            int me = TerrainAffordanceView.LocalActor;
            if (me < 0 || me >= w.ActorCount) return;
            Actor a = w.Actors[me];
            if (aim != null && aim.Has && aim.Action == AffAction.Tnt) DrawArc(v, w, me, aim, up, fwd);
            if (!rings) return;
            DrawFuseRings(v, w, me, a, right, up);
            DrawMineRings(v, w, a.Team);
        }

        void DrawArc(TerrainAffordanceView v, World w, int me, AffTarget aim, Vector3 up, Vector3 fwd)
        {
            MapDef m = w.Map;
            int n = GrenadeTuning.ArcPoints;
            if (v.QuadsLeft < n - 1) return;
            double ox = w.X[me], oz = w.Z[me];
            for (int i = 0; i < n; i++)
            {
                GrenadeRead.ArcPoint(i, n, ox, oz, aim.X, aim.Z, out double x, out double z, out float lift);
                ArcWorld[i] = SimSpace.ToWorld(x, GroundHeight.At(m, x, z) + lift, z);
            }
            ArcPointsDrawn = n;
            ArcLandX = aim.X;
            ArcLandZ = aim.Z;
            AimRingRadius = (float)aim.GhostRadius;
            Color32 col = TerrainAffordanceView.ActionColour(AffAction.Tnt);
            float alpha = aim.Valid ? GrenadeTuning.ArcAlphaReady : GrenadeTuning.ArcAlphaWait;
            bool march = aim.Valid && !CriticalMass.Runtime.View.Effects.FeelGate.ReduceMotion;
            int beat = march ? (int)Math.Floor(Time.unscaledTimeAsDouble * GrenadeTuning.ArcMarch) : 0;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = ArcWorld[i], p1 = ArcWorld[i + 1];
                Vector3 side = Vector3.Cross(p1 - p0, fwd);
                side = side.sqrMagnitude > 1e-8f ? side.normalized * GrenadeTuning.ArcHalfWidth : up * GrenadeTuning.ArcHalfWidth;
                float k = march && ((i - beat) % 3 + 3) % 3 == 0 ? 1f : 0.62f;
                v.SolidQuad(p0 - side, p0 + side, p1 + side, p1 - side, TerrainAffordanceView.A(col, alpha * k));
            }
        }

        void DrawFuseRings(TerrainAffordanceView v, World w, int me, Actor a, Vector3 right, Vector3 up)
        {
            Bomb[] bs = w.Demolitions != null ? w.Demolitions.Bombs : null;
            if (bs == null) return;
            MapDef m = w.Map;
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            float r = GrenadeRead.RingRadius(cfg);
            double hz = w.Config.SimHz > 0 ? w.Config.SimHz : 30;
            double now = w.Tick;
            bool steady = CriticalMass.Runtime.View.Effects.FeelGate.ReduceFlashing;
            double myR = a.Alive ? w.Config.Class(a.Cls).Radius : 0;
            Color32 tnt = TerrainAffordanceView.ActionColour(AffAction.Tnt);
            float urgent = 2f;
            for (int k = 0; k < bs.Length && LiveRings < GrenadeTuning.MaxLiveRings && v.QuadsLeft >= 5; k++)
            {
                Bomb b = bs[k];
                if (b == null || !b.Active) continue;
                double left = (b.BlastTick - now) / hz, total = (b.BlastTick - b.ThrownTick) / hz;
                float f = GrenadeRead.FuseLeft01(now, b.ThrownTick, b.BlastTick);
                bool on = GrenadeRead.BlinkOn(left, total);
                bool inside = a.Alive && GrenadeRead.Inside(w.X[me], w.Z[me], myR, b.X, b.Z, cfg.BlastRadius);
                bool warn = GrenadeRead.Warns(a.Team, b.Team, inside);
                float alpha = steady ? Mathf.Lerp(0.55f, 0.9f, 1f - f) : on ? GrenadeTuning.BlinkOnAlpha : GrenadeTuning.BlinkOffAlpha;
                Color32 col = warn ? Warning : tnt;
                float g = (float)GroundHeight.At(m, b.X, b.Z);
                float y = g + TerrainAffordanceView.GroundLift + 0.03f;
                v.GroundQuad(b.X, b.Z, y, GrenadeRead.RingQuadHalf(r), TerrainAffordanceView.CellRing, TerrainAffordanceView.A(col, alpha));
                LiveRings++;
                if (f < urgent)
                {
                    urgent = f;
                    RingRadius = r;
                    FuseLeft = f;
                    BlinkPeriod = GrenadeRead.BlinkPeriod(f);
                    BlinkOn = on;
                }
                if (!warn) continue;
                WarnRings++;
                v.GroundQuad(b.X, b.Z, y - 0.002f, r, TerrainAffordanceView.CellDisc, TerrainAffordanceView.A(Warning, 0.1f + 0.12f * (1f - f)));
                // The "!": a warning disc with a white bar and dot, over the ring centre, facing the camera.
                Vector3 c = SimSpace.ToWorld(b.X, g + GrenadeTuning.BangLift, b.Z);
                float s = GrenadeTuning.BangSize;
                v.Billboard(c, s, right, up, TerrainAffordanceView.CellDisc, TerrainAffordanceView.A(Warning, 0.95f));
                Vector3 bc = c + up * (s * 0.18f), hw = right * (s * 0.13f), hh = up * (s * 0.42f);
                v.SolidQuad(bc - hw - hh, bc - hw + hh, bc + hw + hh, bc + hw - hh, White);
                v.Billboard(c - up * (s * 0.55f), s * 0.16f, right, up, TerrainAffordanceView.CellDisc, White);
                Bangs++;
            }
        }

        void DrawMineRings(TerrainAffordanceView v, World w, TeamId viewer)
        {
            Mine[] ms = w.Demolitions != null ? w.Demolitions.Mines : null;
            if (ms == null) return;
            MapDef m = w.Map;
            for (int k = 0; k < ms.Length && MineRings < GrenadeTuning.MaxMineRings && v.QuadsLeft >= 1; k++)
            {
                Mine mi = ms[k];
                if (mi == null || !mi.Active || !GrenadeRead.MineRingVisible(mi.Team, viewer)) continue;
                double x = mi.Cx + 0.5, z = mi.Cz + 0.5;
                Color32 tc = CMTokens.Team(mi.Team);
                v.GroundQuad(x, z, v.CellY(m, x, z) + 0.025f, GrenadeRead.RingQuadHalf(GrenadeTuning.MineRingRadius), TerrainAffordanceView.CellRing,
                    TerrainAffordanceView.A(tc, GrenadeTuning.MineRingAlpha));
                MineRings++;
            }
        }
    }
}
