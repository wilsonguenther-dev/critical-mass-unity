using System;
using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// feat/staged-building-1: the pure half of the staged build (no scene, no renderer; EditMode tests drive it). The sim
    /// still completes a ladder, rope bridge or barricade in one tick; the view plays the construction as a short sequence
    /// that starts when the build lands. This file decides which authored model each structure draws as, how the model is
    /// cut into pieces (connected parts of its mesh: posts and stakes first, then planks, rungs or beams in order along the
    /// span, long ropes and rails last), and when each piece arrives, drops and settles. It reads the sim and never writes it.
    /// </summary>
    public static class StagedBuildCore
    {
        /// <summary>Structure kinds: the sim's BuiltPieces.Ladder (1) and Bridge (2), plus the Miner barricade.</summary>
        public const int KindLadder = BuiltPieces.Ladder, KindBridge = BuiltPieces.Bridge, KindBarricade = 3;

        /// <summary>The sequence lasts between these (seconds, wall clock).</summary>
        public const float MinSeconds = 0.6f, MaxSeconds = 1.2f;
        /// <summary>Each piece falls this far (units) and settles in this long.</summary>
        public const float DropHeight = 0.45f, SettleSeconds = 0.16f;
        /// <summary>The build glow shrinks away over the last part of the sequence.</summary>
        public const float GlowTail = 0.25f;
        /// <summary>Most stages one model is cut into (keeps the knock count and the draw count small).</summary>
        public const int MaxStages = 10;

        /// <summary>The catalog model a structure kind draws as (null for an unknown kind).</summary>
        public static string ModelKey(int kind)
        {
            switch (kind)
            {
                case KindLadder: return ArtMapping.BuilderLadder;
                case KindBridge: return ArtMapping.BuilderRopeBridge;
                case KindBarricade: return ArtMapping.MinerBarricade;
                default: return null;
            }
        }

        /// <summary>Models the staged build cuts into parts, so they import with read/write on (ArtImportPostprocessor).</summary>
        public static bool NeedsReadable(string modelKey) =>
            modelKey == ArtMapping.BuilderLadder || modelKey == ArtMapping.BuilderRopeBridge || modelKey == ArtMapping.MinerBarricade;

        /// <summary>The child mesh of that model that shows the built structure.</summary>
        public static string PlacedMesh(int kind)
        {
            switch (kind)
            {
                case KindLadder: return ArtMapping.LadderPlaced;
                case KindBridge: return ArtMapping.RopeBridgePlaced;
                case KindBarricade: return ArtMapping.BarricadeDeployed;
                default: return null;
            }
        }

        /// <summary>The child mesh shown while a ladder or bridge is carried (null for the barricade: it is never carried).</summary>
        public static string CarriedMesh(int kind)
        {
            switch (kind)
            {
                case KindLadder: return ArtMapping.LadderCarried;
                case KindBridge: return ArtMapping.RopeBridgeCarried;
                default: return null;
            }
        }

        /// <summary>The sound each piece knocks with: existing wood cues of SoundTable.</summary>
        public static string KnockCue(int kind) => kind == KindBridge ? "build_bridge" : kind == KindLadder ? "build_ladder" : "barricade_place";

        /// <summary>Sequence length for a number of arrivals (pieces in order), clamped to MinSeconds..MaxSeconds.</summary>
        public static float Duration(int arrivals) => Mathf.Clamp(0.45f + 0.07f * Mathf.Max(1, arrivals), MinSeconds, MaxSeconds);

        /// <summary>When arrival i of n starts falling (seconds into a sequence of length dur). The last one lands before the glow tail ends.</summary>
        public static float ArriveAt(int i, int n, float dur)
        {
            if (n <= 1) return 0f;
            float span = Mathf.Max(0f, dur - SettleSeconds - 0.08f);
            return span * Mathf.Clamp(i, 0, n - 1) / (n - 1);
        }

        /// <summary>
        /// The pose of one arrival at time t: false while it has not arrived yet; otherwise drop is its height above the final
        /// spot (a fall with a small settle dip, exactly 0 once settled). With reduceMotion pieces still arrive in order but
        /// appear in place (no fall).
        /// </summary>
        public static bool Pose(float t, float arrive, bool reduceMotion, out float drop)
        {
            drop = 0f;
            if (t < arrive) return false;
            if (reduceMotion) return true;
            float u = (t - arrive) / SettleSeconds;
            if (u >= 1f) return true;
            float fall = 1f - u;
            drop = DropHeight * fall * fall - 0.035f * Mathf.Sin(Mathf.PI * u);
            return true;
        }

        /// <summary>True when arrival i has landed during (t0, t1] (the knock and the dust fire once per arrival).</summary>
        public static bool LandedBetween(float t0, float t1, float arrive) => t0 < arrive + SettleSeconds && t1 >= arrive + SettleSeconds;

        /// <summary>Build glow scale 0..1: grows in over 0.1 s, holds, shrinks away over the last GlowTail seconds.</summary>
        public static float GlowScale(float t, float dur)
        {
            if (t < 0f || t >= dur) return 0f;
            float grow = Mathf.Clamp01(t / 0.1f);
            float tail = Mathf.Clamp01((dur - t) / GlowTail);
            return grow * tail;
        }

        public static bool Done(float t, float dur) => t >= dur;
        public static float Progress(float t, float dur) => dur <= 0f ? 1f : Mathf.Clamp01(t / dur);

        /// <summary>Which axis orders the pieces of a kind: bridge along its length (+Z of the art), ladder and barricade bottom to top (Y).</summary>
        static int OrderAxis(int kind) => kind == KindBridge ? 2 : 1;

        /// <summary>
        /// Cuts a mesh into staged groups. Triangles are grouped into connected parts (vertices welded by position, since the
        /// art is flat shaded); a part taller than it is wide is a post (ladder rails, bridge posts, barricade stakes) and comes
        /// first; a part longer than 60% of the whole model along the order axis (bridge ropes and rails) comes last; the rest
        /// (planks, rungs, cross-beams) are sorted along the order axis and split into equal runs. triStage[t] gets the stage
        /// of triangle t (tris holds 3 indices per triangle). Returns the stage count (0 for an empty mesh).
        /// </summary>
        public static int Partition(IList<Vector3> verts, IList<int> tris, int kind, int maxStages, int[] triStage)
        {
            int triCount = tris.Count / 3;
            if (triCount == 0 || verts.Count == 0) return 0;
            maxStages = Mathf.Max(1, maxStages);
            // weld by position
            var weld = new Dictionary<Vector3Int, int>();
            var vid = new int[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.y * 2000f), Mathf.RoundToInt(p.z * 2000f));
                if (!weld.TryGetValue(key, out int id)) weld[key] = id = weld.Count;
                vid[i] = id;
            }
            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int t = 0; t < triCount; t++)
            {
                int a = Find(vid[tris[t * 3]]), b = Find(vid[tris[t * 3 + 1]]), c = Find(vid[tris[t * 3 + 2]]);
                if (a != b) parent[b] = a;
                c = Find(c); a = Find(a);
                if (a != c) parent[c] = a;
            }
            // parts and their bounds
            var partOf = new Dictionary<int, int>();
            var mins = new List<Vector3>();
            var maxs = new List<Vector3>();
            var triPart = new int[triCount];
            Vector3 allMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), allMax = -allMin;
            for (int t = 0; t < triCount; t++)
            {
                int r = Find(vid[tris[t * 3]]);
                if (!partOf.TryGetValue(r, out int part))
                {
                    partOf[r] = part = mins.Count;
                    mins.Add(new Vector3(float.MaxValue, float.MaxValue, float.MaxValue));
                    maxs.Add(new Vector3(float.MinValue, float.MinValue, float.MinValue));
                }
                triPart[t] = part;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = verts[tris[t * 3 + k]];
                    mins[part] = Vector3.Min(mins[part], p);
                    maxs[part] = Vector3.Max(maxs[part], p);
                    allMin = Vector3.Min(allMin, p);
                    allMax = Vector3.Max(allMax, p);
                }
            }
            int axis = OrderAxis(kind);
            float total = Mathf.Max(1e-4f, allMax[axis] - allMin[axis]);
            int parts = mins.Count;
            var cls = new int[parts];          // 0 post, 1 piece, 2 long (rope or rail)
            var key2 = new float[parts];
            for (int i = 0; i < parts; i++)
            {
                Vector3 e = maxs[i] - mins[i];
                float horiz = Mathf.Max(e.x, e.z);
                bool longPart = kind == KindBridge && e[axis] > 0.6f * total;
                bool post = !longPart && e.y > 0.25f && e.y > 0.6f * horiz;
                cls[i] = post ? 0 : longPart ? 2 : 1;
                key2[i] = (mins[i][axis] + maxs[i][axis]) * 0.5f;
            }
            int posts = 0, pieces = 0, longs = 0;
            for (int i = 0; i < parts; i++) { if (cls[i] == 0) posts++; else if (cls[i] == 1) pieces++; else longs++; }
            int postStages = posts > 0 ? 1 : 0, longStages = longs > 0 ? 1 : 0;
            int pieceStages = pieces > 0 ? Mathf.Clamp(maxStages - postStages - longStages, 1, pieces) : 0;
            if (postStages + pieceStages + longStages > maxStages)
            {
                // tiny budgets: fold the rope stage into the last piece stage, then the posts into the first
                if (longStages > 0 && pieceStages > 0) longStages = 0;
                if (postStages + pieceStages > maxStages && pieceStages > 0) postStages = 0;
            }
            var pieceIdx = new List<int>();
            for (int i = 0; i < parts; i++) if (cls[i] == 1) pieceIdx.Add(i);
            pieceIdx.Sort((x, y) => key2[x] != key2[y] ? key2[x].CompareTo(key2[y]) : x.CompareTo(y));
            var partStage = new int[parts];
            for (int r = 0; r < pieceIdx.Count; r++)
                partStage[pieceIdx[r]] = (postStages > 0 ? 1 : 0) + Mathf.Min(pieceStages - 1, r * pieceStages / Mathf.Max(1, pieceIdx.Count));
            int lastPiece = (postStages > 0 ? 1 : 0) + Mathf.Max(0, pieceStages - 1);
            for (int i = 0; i < parts; i++)
            {
                if (cls[i] == 0) partStage[i] = 0;
                else if (cls[i] == 2) partStage[i] = longStages > 0 ? lastPiece + 1 : lastPiece;
            }
            int stages = 0;
            for (int t = 0; t < triCount; t++)
            {
                int s = partStage[triPart[t]];
                if (triStage != null && t < triStage.Length) triStage[t] = s;
                if (s + 1 > stages) stages = s + 1;
            }
            return stages;
        }

        // ------------------------------------------------------------------ the sequence tracker (pure; the view and the tests use it)

        /// <summary>One running sequence. Start is the wall clock when the build landed.</summary>
        public struct Seq
        {
            public bool On;
            public int Kind;
            public int Owner;
            public TeamId Team;
            public float Start;
            public float Dur;
            public int Arrivals;
            public float LastT;
            public double X, Z;
        }

        /// <summary>
        /// Watches the sim's pieces and barricades for new builds (a piece slot's PlacedTick changes; a barricade slot turns
        /// active). It only reads the World. A slot seen when the tracker is (re)bound counts as already built (no replay on a
        /// mid-match join or a capture reset).
        /// </summary>
        public sealed class Tracker
        {
            public int[] SeenPlaced = new int[0];
            public bool[] SeenBarricade = new bool[0];
            public Seq[] Pieces = new Seq[0];
            public Seq[] Barricades = new Seq[0];
            public int Started;
            World _bound;

            public void Bind(World w)
            {
                _bound = w;
                int np = w != null && w.Builder != null && w.Builder.Pieces != null ? w.Builder.Pieces.Length : 0;
                int nb = w != null && w.Barricades != null ? w.Barricades.Length : 0;
                if (SeenPlaced.Length != np) { SeenPlaced = new int[np]; Pieces = new Seq[np]; }
                if (SeenBarricade.Length != nb) { SeenBarricade = new bool[nb]; Barricades = new Seq[nb]; }
                for (int k = 0; k < np; k++)
                {
                    BuiltPiece p = w.Builder.Pieces[k];
                    SeenPlaced[k] = p != null && p.Active ? p.PlacedTick : int.MinValue;
                    Pieces[k] = default;
                }
                for (int i = 0; i < nb; i++)
                {
                    Barricade b = w.Barricades[i];
                    SeenBarricade[i] = b != null && b.Active;
                    Barricades[i] = default;
                }
            }

            /// <summary>Checks for new builds at wall time now; enabled false records them without a sequence.</summary>
            public void Observe(World w, float now, bool enabled, int pieceArrivals, int barricadeArrivals)
            {
                if (w != _bound) Bind(w);
                if (w == null) return;
                BuiltPiece[] ps = w.Builder != null ? w.Builder.Pieces : null;
                if (ps != null)
                    for (int k = 0; k < ps.Length && k < SeenPlaced.Length; k++)
                    {
                        BuiltPiece p = ps[k];
                        if (p == null || !p.Active) { Pieces[k].On = false; SeenPlaced[k] = int.MinValue; continue; }
                        if (p.PlacedTick == SeenPlaced[k]) continue;
                        SeenPlaced[k] = p.PlacedTick;
                        if (!enabled || p.Carried) continue;
                        Pieces[k] = new Seq { On = true, Kind = p.Kind, Owner = p.Owner, Team = p.Team, Start = now, Arrivals = pieceArrivals,
                            Dur = Duration(pieceArrivals), LastT = -1f, X = p.X, Z = p.Z };
                        Started++;
                    }
                Barricade[] bs = w.Barricades;
                if (bs != null)
                    for (int i = 0; i < bs.Length && i < SeenBarricade.Length; i++)
                    {
                        Barricade b = bs[i];
                        bool on = b != null && b.Active;
                        if (!on) { SeenBarricade[i] = false; Barricades[i].On = false; continue; }
                        if (SeenBarricade[i]) continue;
                        SeenBarricade[i] = true;
                        if (!enabled) continue;
                        Barricades[i] = new Seq { On = true, Kind = KindBarricade, Owner = b.Owner, Team = b.Team, Start = now,
                            Arrivals = barricadeArrivals, Dur = Duration(barricadeArrivals), LastT = -1f, X = b.Cx + 0.5, Z = b.Cz + 0.5 };
                        Started++;
                    }
            }

            /// <summary>Ends sequences whose time ran out (the structure then draws in its final, static form).</summary>
            public void Expire(float now)
            {
                for (int k = 0; k < Pieces.Length; k++) if (Pieces[k].On && Done(now - Pieces[k].Start, Pieces[k].Dur)) Pieces[k].On = false;
                for (int i = 0; i < Barricades.Length; i++) if (Barricades[i].On && Done(now - Barricades[i].Start, Barricades[i].Dur)) Barricades[i].On = false;
            }

            public bool AnyOn
            {
                get
                {
                    for (int k = 0; k < Pieces.Length; k++) if (Pieces[k].On) return true;
                    for (int i = 0; i < Barricades.Length; i++) if (Barricades[i].On) return true;
                    return false;
                }
            }

            /// <summary>The running sequence the actor is building, if any (index into Pieces, or Pieces.Length + barricade index).</summary>
            public bool BuildingBy(int actor, out Seq seq)
            {
                for (int k = 0; k < Pieces.Length; k++) if (Pieces[k].On && Pieces[k].Owner == actor) { seq = Pieces[k]; return true; }
                for (int i = 0; i < Barricades.Length; i++) if (Barricades[i].On && Barricades[i].Owner == actor) { seq = Barricades[i]; return true; }
                seq = default;
                return false;
            }
        }
    }
}
