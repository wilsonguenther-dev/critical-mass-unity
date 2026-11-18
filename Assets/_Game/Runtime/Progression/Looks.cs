using System;
using System.Collections.Generic;
using CriticalMass.Net;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Online;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>
    /// b15 profile: who shows which cosmetic look. The local player's comes from the profile (its loadout, clamped by the
    /// class's mastery); another human's comes from the lobby (RoomHost / RoomClient looks, clamped on arrival) and is
    /// valid only while that player keeps the class picked in the lobby; bots show none. Cosmetic data OUTSIDE the sim:
    /// nothing here is read by the sim, the input stream, the config hash or the state hash.
    /// <para>For(actor) and TrimOf(actor) read a cache that LookSync refreshes once a second and on every profile change,
    /// so the per-frame callers (ActorViews' trim, the scoreboard) never allocate.</para>
    /// </summary>
    public static class LookRegistry
    {
        static CosmeticLook[] _looks = new CosmeticLook[0];
        static World _world;

        /// <summary>The local player's look for a class, from the profile (clamped).</summary>
        public static CosmeticLook Local(ClassId cls) => Local(Profile.Current, cls);

        public static CosmeticLook Local(PlayerProfile p, ClassId cls)
        {
            if (p == null) return CosmeticLook.Default;
            ClassLoadout l = p.Loadout(cls);
            int level = p.MasteryOf(cls).Level;
            return CosmeticLook.Clamp(new CosmeticLook(l.Title, l.Frame, l.Trim, l.Badge, level));
        }

        /// <summary>The look of an actor in the running match (Default for bots and unknown actors).</summary>
        public static CosmeticLook For(int actor) => actor >= 0 && actor < _looks.Length ? _looks[actor] : CosmeticLook.Default;

        public static int TrimOf(int actor) => actor >= 0 && actor < _looks.Length ? _looks[actor].Trim : 0;

        /// <summary>Rebuilds the cache for w (allocates only when the actor count changes).</summary>
        public static void Refresh(World w)
        {
            _world = w;
            int n = w != null ? w.ActorCount : 0;
            if (_looks.Length != n) _looks = new CosmeticLook[n];
            for (int i = 0; i < n; i++) _looks[i] = Resolve(w, i);
        }

        /// <summary>Tests and captures: set the cache directly.</summary>
        public static void SetForTests(CosmeticLook[] looks)
        {
            _looks = looks ?? new CosmeticLook[0];
            _world = null;
        }

        static CosmeticLook Resolve(World w, int actor)
        {
            Actor a = w.Actors[actor];
            if (!a.IsHuman) return CosmeticLook.Default;
            if (actor == LocalSeat.Actor) return Local(a.Cls);
            LockstepSession s = OnlineMatch.Session;
            if (s == null || s.Setup?.Humans == null) return CosmeticLook.Default;
            foreach (HumanSlot slot in s.Setup.Humans)
            {
                if (slot.Actor != actor) continue;
                if (slot.Class != a.Cls) return CosmeticLook.Default;   // switched class in the match: that class's look is unknown
                CosmeticLook l = OnlineMatch.Host != null ? OnlineMatch.Host.LookOf(slot.Peer)
                    : OnlineMatch.Client != null ? OnlineMatch.Client.LookOf(slot.Peer) : CosmeticLook.Default;
                return CosmeticLook.Clamp(l);
            }
            return CosmeticLook.Default;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _looks = new CosmeticLook[0];
            _world = null;
        }
    }

    /// <summary>Refreshes LookRegistry for the running match: on each new world, on every profile change and once a second.</summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 34, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]
    public sealed class LookSync : MonoBehaviour
    {
        const float EverySeconds = 1f;
        SimDriverBehaviour _driver;
        World _world;
        int _epoch = int.MinValue;
        float _next;
        bool _dirty = true;

        void Awake() => _driver = GetComponent<SimDriverBehaviour>();
        void OnEnable() => Profile.Changed += MarkDirty;
        void OnDisable() => Profile.Changed -= MarkDirty;
        void MarkDirty() => _dirty = true;

        void LateUpdate()
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null) return;
            if (!_dirty && ReferenceEquals(w, _world) && w.Epoch == _epoch && Time.unscaledTime < _next) return;
            _dirty = false;
            _world = w;
            _epoch = w.Epoch;
            _next = Time.unscaledTime + EverySeconds;
            LookRegistry.Refresh(w);
        }
    }

    /// <summary>
    /// b15 profile: trim materials. A trim is a clone of a character's team material with the fixed-colour mix set
    /// (MasteryRewards.TrimSpec); team cloth, glow, dark and marker colours are copied unchanged, and the shader only mixes
    /// mask code 0, so team colour regions cannot move. One material per (team material, trim), cached.
    /// </summary>
    public static class TrimMaterials
    {
        static readonly Dictionary<long, Material> Cache = new Dictionary<long, Material>();
        static readonly HashSet<Material> Made = new HashSet<Material>();
        static readonly int BerserkId = Shader.PropertyToID("_Berserk"), MulId = Shader.PropertyToID("_BerserkMul"),
            TintId = Shader.PropertyToID("_BerserkTint"), AmountId = Shader.PropertyToID("_BerserkTintAmount"),
            ColorModeId = Shader.PropertyToID("_ColorMode");

        /// <summary>The trimmed variant of a team material (the material itself for trim 0, a non-character or a null).</summary>
        public static Material For(Material team, int trim)
        {
            if (team == null || trim <= 0 || trim >= MasteryRewards.Trims.Length) return team;
            if (Made.Contains(team)) return team;
            if (!team.HasProperty(BerserkId) || !team.HasProperty(ColorModeId) || team.GetFloat(ColorModeId) < 0.5f) return team;
            if (team.GetFloat(BerserkId) > 0f) return team;   // a berserk Kaiju keeps its own mix
            long key = ((long)team.GetInstanceID() << 8) | (uint)trim;
            if (Cache.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(team) { name = team.name + " trim " + trim };
            Write(m, MasteryRewards.Trim(trim));
            Cache[key] = m;
            Made.Add(m);
            return m;
        }

        /// <summary>The material for actor id: ActorViews' one-line hook (allocation-free after the first use per pair).</summary>
        public static Material ForActor(Material team, int actor) => For(team, LookRegistry.TrimOf(actor));

        public static void Write(Material m, TrimSpec t)
        {
            m.SetFloat(BerserkId, t.Strength);
            m.SetVector(MulId, new Vector4(t.Mul.x, t.Mul.y, t.Mul.z, 1f));
            m.SetColor(TintId, t.Tint);
            m.SetFloat(AmountId, t.TintAmount);
        }
    }

    /// <summary>b15 profile: drawing a look on a card (frame ring, emblem badge, title line) with the menu's own shapes.</summary>
    public static class LookView
    {
        /// <summary>Puts a frame ring on a portrait disc (plain frame: the disc keeps its own outline).</summary>
        public static void Ring(VectorShape disc, int frame, float plainOutlinePx, Color plainOutline)
        {
            if (disc == null) return;
            bool on = frame > 0 && frame < MasteryRewards.FrameHex.Length;
            Color c = on ? MasteryRewards.FrameColor(frame) : plainOutline;
            float px = on ? MasteryRewards.FrameRingPx[frame] : plainOutlinePx;
            if (disc.OutlineColor == c && Mathf.Approximately(disc.OutlinePx, px)) return;
            disc.OutlineColor = c;
            disc.OutlinePx = px;
            disc.SetVerticesDirty();
        }

        /// <summary>An emblem badge (a navy disc with the emblem shape on it), hidden until Set shows one.</summary>
        public static VectorShape Emblem(Transform parent, string name, float x, float y, float size)
        {
            VectorShape back = CriticalMass.Runtime.Menu.MenuUi.Shape(parent, name, ShapeKind.Circle, x, y, size, size, CMTokens.VoidNavy.Color, 0f);
            back.OutlinePx = 2f;
            back.RimPx = 0f;
            float inner = size * 0.62f;
            VectorShape mark = CriticalMass.Runtime.Menu.MenuUi.Shape(back.transform, "Mark", ShapeKind.Circle, (size - inner) * 0.5f, (size - inner) * 0.5f, inner, inner, Color.white, 0f);
            mark.OutlinePx = 0f;
            mark.RimPx = 0f;
            back.gameObject.SetActive(false);
            return back;
        }

        public static void SetEmblem(VectorShape badge, ClassId cls, int emblem)
        {
            if (badge == null) return;
            bool on = emblem > 0 && emblem < MasteryRewards.EmblemShape.Length;
            if (badge.gameObject.activeSelf != on) badge.gameObject.SetActive(on);
            if (!on || badge.transform.childCount == 0) return;
            VectorShape mark = badge.transform.GetChild(0).GetComponent<VectorShape>();
            if (mark == null) return;
            ShapeKind k = MasteryRewards.EmblemShape[emblem];
            Color c = MasteryRewards.EmblemColor(cls);
            if (mark.Kind == k && mark.FillColor == c) return;
            mark.Kind = k;
            mark.SetFill(c);
            mark.SetVerticesDirty();
        }

        /// <summary>The title text of a look for a class ("" when none).</summary>
        public static string Title(ClassId cls, CosmeticLook look) => look.Title > 0 ? MetaWords.Title(cls, look.Title) : string.Empty;
    }
}
