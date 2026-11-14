using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>Canvas plumbing shared by the DG-11 / DG-38 overlays (same setup as GameHud: Screen Space - Camera, 1080p scale).</summary>
    public static class FeedUi
    {
        public static Canvas MakeCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.sortingOrder = sortingOrder;
            c.planeDistance = 0.5f;
            c.pixelPerfect = false;
            Refresh(c);
            return c;
        }

        /// <summary>Attaches Camera.main and sets the 1080p scale factor; returns the icon tier for that scale.</summary>
        public static int Refresh(Canvas c)
        {
            UnityEngine.Camera cam = c.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null)
                {
                    c.worldCamera = cam;
                    c.planeDistance = Mathf.Max(0.5f, cam.nearClipPlane * 1.5f);
                }
            }
            float w = cam != null ? cam.pixelWidth : Screen.width;
            float h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return 2;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(c.scaleFactor - s) > 1e-4f) c.scaleFactor = s;
            return UiIcons.TierFor(s);
        }

        /// <summary>A container of size (w, h) whose pivot sits at anchor, offset (x, y) reference px (y up, uGUI).</summary>
        public static RectTransform Anchored(Transform parent, string name, Vector2 anchor, float x, float y, float w, float h)
        {
            // Owner walk 1: a piece anchored straight onto a HUD canvas goes into the canvas's safe-area root instead, so the
            // kill feed, recap, toasts and banners keep clear of the notch and stay beside the pieces they were laid out with.
            // Centre-anchored pieces (world markers placed from the canvas centre) stay on the canvas itself.
            if (parent != null && anchor != new Vector2(0.5f, 0.5f) && parent.GetComponent<Canvas>() != null) parent = SafeRoot(parent);
            RectTransform rt = MenuUi.Rt(parent, name);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        /// <summary>The canvas's "Safe Area" child (made once): a stretch container kept on the safe area.</summary>
        public static RectTransform SafeRoot(Transform canvas)
        {
            Transform t = canvas.Find("Safe Area");
            return t != null ? (RectTransform)t : HudSafeSpace.MakeSafeRoot(canvas);
        }

        public static float Ease(float t) => 1f - (1f - t) * (1f - t);
    }

    /// <summary>
    /// The words the DG-11 / DG-38 overlays show. Names, attack names and tips come from the words pack
    /// (Resources "Lore/classes" and "Lore/tips", Assets/_Game/Resources/Lore, DG-04) when the files are present, else from the built-in
    /// copies below (the same lines). Every string is built once; nothing here allocates during a match.
    /// </summary>
    public static class KillWords
    {
        public static readonly string[] Team = { "Azure", "Crimson" };
        /// <summary>Feed names (short, upper case).</summary>
        public static readonly string[] ClassUpper = { "WARRIOR", "FARMER", "MINER", "DEMOLITIONS", "BUILDER", "HEALER" };
        public const string You = "YOU", Kaiju = "KAIJU";
        public const string KnockedOut = "Knocked out";

        static readonly string[] ClassNameBuiltIn = { "Warrior", "Farmer", "Miner", "Demolitions Expert", "Builder", "Healer" };
        /// <summary>Attack-input names; empty while the class's attack waits on a rule (Demolitions DG-22, Builder DG-24).</summary>
        static readonly string[] AttackBuiltIn = { "Cleave", "Staff Hook", "Pick-Hammer", "TNT", "Hammer", "" };

        static readonly KeyValuePair<string, string>[] TipsBuiltIn =
        {
            new KeyValuePair<string, string>("tip-01", "Your team scores only while it has more living players on the hill than the enemy."),
            new KeyValuePair<string, string>("tip-02", "An even hill scores nothing. Bring one more friend than they did."),
            new KeyValuePair<string, string>("tip-08", "A berserk Kaiju attacks both sides, but it hunts the bigger biomass feeder harder."),
            new KeyValuePair<string, string>("tip-11", "A raised Warrior shield takes {cfg:shieldReductionPct}% off hits from the front. Face the trouble, not the exit."),
            new KeyValuePair<string, string>("tip-14", "Knocked out? You are back in {cfg:respawnSeconds} s, and nothing can hurt you for {cfg:protectionSeconds} s after."),
            new KeyValuePair<string, string>("tip-15", "The Kaiju's slam marks the ground {cfg:slamTelegraph} s before it lands. Leave the circle; it reaches {cfg:slamRadius} m."),
            new KeyValuePair<string, string>("tip-18", "Every step toward the shrine is a step off the hill. Send carriers, not the whole team."),
            new KeyValuePair<string, string>("tip-19", "Carriers are slow targets with full pockets. Escort yours, and hunt theirs."),
        };

        static string[] _className, _attack;
        static string[,] _recap;
        static string _recapKaijuSlam, _recapKaiju;
        static readonly Dictionary<string, string> _tips = new Dictionary<string, string>();
        static GameConfig _cfg;

        public static string ClassName(ClassId c) { Init(null); return _className[Ix(c)]; }
        public static string Attack(ClassId c) { Init(null); return _attack[Ix(c)]; }

        /// <summary>"Knocked out by Crimson Warrior: Cleave"; "Knocked out by the Kaiju: Slam"; "Knocked out" when unknown.</summary>
        public static string Recap(int killer, TeamId killerTeam, ClassId killerClass)
        {
            Init(null);
            if (killer >= 0) return _recap[(int)killerTeam, Ix(killerClass)];
            if (killer == KillSource.KaijuSlam) return _recapKaijuSlam;
            if (killer == KillSource.KaijuMelee) return _recapKaiju;
            return KnockedOut;
        }

        /// <summary>A resolved tip by words-pack id (null when it is missing or has an unresolved token).</summary>
        public static string Tip(string id)
        {
            Init(null);
            return _tips.TryGetValue(id, out string s) ? s : null;
        }

        static int Ix(ClassId c) => Mathf.Clamp((int)c, 0, ClassUpper.Length - 1);

        /// <summary>Builds every string once. cfg resolves {cfg:...} tokens (GameConfig.CreateDefault() when null).</summary>
        public static void Init(GameConfig cfg)
        {
            if (_recap != null && (cfg == null || ReferenceEquals(cfg, _cfg))) return;
            _cfg = cfg ?? _cfg ?? GameConfig.CreateDefault();
            _className = (string[])ClassNameBuiltIn.Clone();
            _attack = (string[])AttackBuiltIn.Clone();
            _tips.Clear();
            for (int i = 0; i < TipsBuiltIn.Length; i++)
            {
                string r = Resolve(TipsBuiltIn[i].Value, _cfg);
                if (r != null) _tips[TipsBuiltIn[i].Key] = r;
            }
            LoadPack();
            _recap = new string[Team.Length, ClassUpper.Length];
            for (int t = 0; t < Team.Length; t++)
            for (int c = 0; c < ClassUpper.Length; c++)
            {
                string who = "Knocked out by " + Team[t] + " " + _className[c];
                _recap[t, c] = _attack[c].Length > 0 ? who + ": " + _attack[c] : who;
            }
            _recapKaijuSlam = "Knocked out by the Kaiju: Slam";
            _recapKaiju = "Knocked out by the Kaiju";
        }

        // ---- words pack (DG-04), read when the files exist (in the Editor and any build that ships them)

        [Serializable] sealed class PackAbility { public string input; public string name; public string needsRule; }
        [Serializable] sealed class PackClass { public string id; public string name; public PackAbility[] abilities; }
        [Serializable] sealed class PackClasses { public PackClass[] classes; }
        [Serializable] sealed class PackTip { public string id; public string text; public string needsRule; }
        [Serializable] sealed class PackTips { public PackTip[] tips; }

        /// <summary>The words pack's Resources folder (Assets/_Game/Resources/Lore): the only path a player build can load by name.</summary>
        public const string PackResources = "Lore/";

        static string PackText(string name)
        {
            TextAsset ta = Resources.Load<TextAsset>(PackResources + name);
            return ta != null ? ta.text : null;
        }

        static void LoadPack()
        {
            try
            {
                string classes = PackText("classes");
                if (classes != null)
                {
                    PackClasses pc = JsonUtility.FromJson<PackClasses>(classes);
                    if (pc != null && pc.classes != null)
                        foreach (PackClass k in pc.classes)
                        {
                            if (k == null || !Enum.TryParse(k.id, out ClassId id)) continue;
                            int i = Ix(id);
                            if (!string.IsNullOrEmpty(k.name)) _className[i] = k.name;
                            if (k.abilities == null) continue;
                            foreach (PackAbility a in k.abilities)
                                if (a != null && a.input == "attack")
                                    _attack[i] = string.IsNullOrEmpty(a.needsRule) && !string.IsNullOrEmpty(a.name) ? a.name : "";
                        }
                }
                string tips = PackText("tips");
                if (tips != null)
                {
                    PackTips pt = JsonUtility.FromJson<PackTips>(tips);
                    if (pt != null && pt.tips != null)
                        foreach (PackTip t in pt.tips)
                        {
                            if (t == null || string.IsNullOrEmpty(t.id) || !string.IsNullOrEmpty(t.needsRule)) continue;
                            string r = Resolve(t.text, _cfg);
                            if (r != null) _tips[t.id] = r;
                        }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Words pack not read, using built-in words: " + ex.Message);
            }
        }

        /// <summary>Fills the {cfg:id} tokens this layer knows (tokens.json formats); null when any token is unknown.</summary>
        public static string Resolve(string text, GameConfig c)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string s = text;
            int guard = 0;
            while (guard++ < 16)
            {
                int a = s.IndexOf("{cfg:", StringComparison.Ordinal);
                if (a < 0) break;
                int b = s.IndexOf('}', a);
                if (b < 0) return null;
                string v = Token(s.Substring(a + 5, b - a - 5), c);
                if (v == null) return null;
                s = s.Substring(0, a) + v + s.Substring(b + 1);
            }
            return s.IndexOf('{') >= 0 ? null : s;
        }

        static string Token(string id, GameConfig c)
        {
            switch (id)
            {
                case "respawnSeconds": return Num(c.Respawn.DelaySeconds);
                case "protectionSeconds": return Num(c.Respawn.ProtectionSeconds);
                case "slamTelegraph": return Num(c.Kaiju.SlamTelegraphSeconds);
                case "slamRadius": return Num(c.Kaiju.SlamRadius);
                case "kaijuKoSeconds": return Num(c.Kaiju.KoSeconds);
                case "shieldReductionPct": return Math.Round(c.Abilities.WarriorShield.DamageReduction * 100).ToString(CultureInfo.InvariantCulture);
                case "biomassAllegiance": return Num(c.KaijuFeed.BiomassAllegiancePerUnit);
                default: return null;
            }
        }

        static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// DG-11 kill feed, top-right under the Kaiju cluster: five lines, newest on top, each shown 4 s. A line reads
    /// [crest] KILLER [attack icon] [crest] VICTIM in team colours (crest shape as well as colour); lines that involve the
    /// local player sit on a Tool Brass plate. Pooled slots, fixed columns, no allocation per line. View only.
    /// </summary>
    public sealed class KillFeed : MonoBehaviour
    {
        public const int Lines = 5;
        public const float LineSeconds = 4f;
        public const float FadeSeconds = 0.2f, SlideSeconds = 0.15f;
        // Polish pass 1: under the top band (136) and 400 wide, so the feed column (HudZones.Feed) never meets the centre lane.
        public const float Top = 136f, Right = 24f, LineW = 400f, LineH = 40f, Pitch = 46f;

        /// <summary>One shown line, newest first, for tests and captures.</summary>
        public struct Entry
        {
            public bool Active;
            public string Killer, Victim;
            public bool Own;
            public float Age;
        }

        public Canvas Canvas { get; private set; }
        CombatLog _log;
        RectTransform _box;
        readonly Slot[] _slots = new Slot[Lines];
        /// <summary>Slot index per row, row 0 = newest.</summary>
        readonly int[] _order = new int[Lines];
        int _tier = 2;
        bool _subscribed;

        sealed class Slot
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public VectorShape Plate;
            public HudIcon KillerCrest, AttackIcon, VictimCrest;
            public Text Killer, Victim;
            public float Age;
            public bool Active, Own;
            public float Y;
        }

        void Awake() => Build();

        void OnDisable() => Unsubscribe();

        public void Build()
        {
            if (Canvas != null) return;
            KillWords.Init(null);
            Canvas = FeedUi.MakeCanvas(transform, "Kill Feed Canvas", 12);
            _box = FeedUi.Anchored(Canvas.transform, "Kill Feed", new Vector2(1f, 1f), -Right, -Top, LineW, Pitch * Lines);
            HudKeepOut.Add(_box);
            for (int i = 0; i < Lines; i++)
            {
                var s = new Slot();
                s.Rt = MenuUi.Rt(_box, "Line " + i);
                MenuUi.Place(s.Rt, 0f, 0f, LineW, LineH);
                s.Group = s.Rt.gameObject.AddComponent<CanvasGroup>();
                s.Group.blocksRaycasts = false;
                s.Group.interactable = false;
                s.Plate = MenuUi.Shape(s.Rt, "Plate", ShapeKind.RoundRect, 0f, 0f, LineW, LineH, CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.9f), 12f);
                s.Plate.RimPx = 2f;
                s.KillerCrest = HudIcon.Make(s.Rt, "Killer Crest", 10f, 7f, 26f, 26f);
                s.Killer = MenuUi.Fit(MenuUi.Label(s.Rt, "Killer", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleRight, 40f, 0f, 132f, LineH, "", 2f));
                s.AttackIcon = HudIcon.Make(s.Rt, "Attack", 180f, 3f, 34f, 34f);
                s.VictimCrest = HudIcon.Make(s.Rt, "Victim Crest", 222f, 7f, 26f, 26f);
                s.Victim = MenuUi.Fit(MenuUi.Label(s.Rt, "Victim", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 254f, 0f, 136f, LineH, "", 2f));
                s.Rt.gameObject.SetActive(false);
                _slots[i] = s;
                _order[i] = i;
            }
        }

        void Subscribe()
        {
            if (_subscribed || _log == null) return;
            _log.Log.Emitted += OnEvent;
            _log.Log.MatchReset += Clear;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _log == null) return;
            _log.Log.Emitted -= OnEvent;
            _log.Log.MatchReset -= Clear;
            _subscribed = false;
        }

        /// <summary>Uses log as the event source (the installer's object; tests pass their own).</summary>
        public void Bind(CombatLog log)
        {
            if (log == _log) { Subscribe(); return; }
            Unsubscribe();
            _log = log;
            Subscribe();
        }

        void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.KillFeed, cost); }

        void LateUpdateCost()
        {
            if (_log == null) Bind(GetComponent<CombatLog>());
            else Subscribe();
            if (CriticalMass.Runtime.Flow.GameFlow.MenuIdle) return;   // batch 4: hidden behind a menu over a backdrop match
            int tier = FeedUi.Refresh(Canvas);
            if (tier != _tier) { _tier = tier; for (int i = 0; i < Lines; i++) SetTier(_slots[i], tier); }
            // owner walk 1: the feed steps away under the full map (its panel covers the right column) and the scoreboard
            bool hide = _log == null || _log.Suppressed || CriticalMass.Runtime.Hud.Interact.FullMap.IsOpen || Scoreboard.AnyOpen;
            if (Canvas.enabled == hide) Canvas.enabled = !hide;
            Tick(_log != null && _log.Paused ? 0f : Time.unscaledDeltaTime);
        }

        static void SetTier(Slot s, int tier)
        {
            s.KillerCrest.SetTier(tier);
            s.AttackIcon.SetTier(tier);
            s.VictimCrest.SetTier(tier);
        }

        void OnEvent(CombatEvent e)
        {
            if (e.Kind != CombatEventKind.Knockout) return;
            World w = _log != null && _log.Driver != null ? _log.Driver.World : null;
            int local = _log != null ? _log.LocalActor : HudBinder.LocalActor;
            Push(e, local, w != null ? w.Kaiju.State : KaijuState.Neutral);
        }

        // ---- lane cap and queue (hud-containment-2). Five lines show at most. A new line waits in a small queue while all
        // five are younger than MinShowSeconds, so a burst of knock-outs no longer flicks lines past unread; when the queue
        // is full the oldest waiting line is dropped (the newest news wins).
        public const float MinShowSeconds = 1.2f;
        public const int QueueCap = 6;
        readonly CombatEvent[] _queueEvent = new CombatEvent[QueueCap];
        readonly int[] _queueLocal = new int[QueueCap];
        readonly KaijuState[] _queueKaiju = new KaijuState[QueueCap];
        int _queueHead, _queueCount;
        /// <summary>Lines waiting for a free row (tests and captures).</summary>
        public int Queued => _queueCount;

        bool RowFree()
        {
            Slot oldest = _slots[_order[Lines - 1]];
            return !oldest.Active || oldest.Age >= MinShowSeconds;
        }

        /// <summary>Adds a knock-out line: newest on top; it waits in the queue while all five rows are still fresh.</summary>
        public void Push(CombatEvent e, int localActor, KaijuState kaiju)
        {
            if (Canvas == null) Build();
            if (_queueCount > 0 || !RowFree())
            {
                if (_queueCount == QueueCap) { _queueHead = (_queueHead + 1) % QueueCap; _queueCount--; }
                int at = (_queueHead + _queueCount) % QueueCap;
                _queueEvent[at] = e;
                _queueLocal[at] = localActor;
                _queueKaiju[at] = kaiju;
                _queueCount++;
                return;
            }
            Show(e, localActor, kaiju);
        }

        void Show(CombatEvent e, int localActor, KaijuState kaiju)
        {
            int slotIx = _order[Lines - 1];
            for (int r = Lines - 1; r > 0; r--) _order[r] = _order[r - 1];
            _order[0] = slotIx;
            Slot s = _slots[slotIx];
            bool own = e.Actor == localActor || e.Other == localActor;
            s.Active = true;
            s.Own = own;
            s.Age = 0f;
            s.Y = -Pitch * 0.5f;   // slides down into row 0
            s.Plate.SetFill(own ? CMTokens.ToolBrass.Color : CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.9f));

            // victim (always an actor)
            s.Victim.color = CMTokens.TeamInk(e.ActorTeam);
            // Mechanics depth (U-067): bots by name when the rules lane is on (DepthNames is bound by the depth feed).
            HudText.Set(s.Victim, e.Actor == localActor ? KillWords.You : View.Mechanics.DepthNames.OrFallback(e.Actor, KillWords.ClassUpper[Ix(e.ActorClass)]));
            s.VictimCrest.Set(UiIcons.Crest[(int)e.ActorTeam], _tier);

            // killer: an actor, the Kaiju, or nobody known
            if (e.Other >= 0)
            {
                s.Killer.color = CMTokens.TeamInk(e.OtherTeam);
                HudText.Set(s.Killer, e.Other == localActor ? KillWords.You : View.Mechanics.DepthNames.OrFallback(e.Other, KillWords.ClassUpper[Ix(e.OtherClass)]));
                s.KillerCrest.Set(UiIcons.Crest[(int)e.OtherTeam], _tier);
                s.AttackIcon.Set(UiIcons.Attack[Ix(e.OtherClass), (int)e.OtherTeam], _tier);
            }
            else if (KillSource.IsKaiju(e.Other))
            {
                s.Killer.color = CMTokens.WarmCream.Color;
                HudText.Set(s.Killer, KillWords.Kaiju);
                s.KillerCrest.Set(null, _tier);
                s.AttackIcon.Set(KaijuIcon(kaiju), _tier);
            }
            else
            {
                s.Killer.color = CMTokens.WarmCream.Color;
                HudText.Set(s.Killer, string.Empty);
                s.KillerCrest.Set(null, _tier);
                s.AttackIcon.Set(null, _tier);
            }
            if (!s.Rt.gameObject.activeSelf) s.Rt.gameObject.SetActive(true);
            s.Group.alpha = 0f;
        }

        static string KaijuIcon(KaijuState k)
        {
            switch (k)
            {
                case KaijuState.Azure: return UiIcons.Adult[1];
                case KaijuState.Crimson: return UiIcons.Adult[2];
                case KaijuState.Berserk: return UiIcons.Adult[3];
                case KaijuState.Neutral: return UiIcons.Adult[0];
                default: return UiIcons.Hatchling;
            }
        }

        static int Ix(ClassId c) => Mathf.Clamp((int)c, 0, KillWords.ClassUpper.Length - 1);

        /// <summary>Ages, fades and slides the lines (dt = 0 holds them, as while paused).</summary>
        public void Tick(float dt)
        {
            if (_queueCount > 0 && RowFree())
            {
                int at = _queueHead;
                _queueHead = (_queueHead + 1) % QueueCap;
                _queueCount--;
                Show(_queueEvent[at], _queueLocal[at], _queueKaiju[at]);
            }
            for (int r = 0; r < Lines; r++)
            {
                Slot s = _slots[_order[r]];
                if (!s.Active) continue;
                s.Age += dt;
                if (s.Age >= LineSeconds)
                {
                    s.Active = false;
                    s.Rt.gameObject.SetActive(false);
                    continue;
                }
                // Every row moves one pitch in SlideSeconds (a new line pushes the others down).
                float targetY = -Pitch * r;
                s.Y = Mathf.MoveTowards(s.Y, targetY, Pitch * dt / SlideSeconds);
                s.Rt.anchoredPosition = new Vector2(LineW * 0.5f, s.Y - LineH * 0.5f);
                float fadeIn = FeedUi.Ease(Mathf.Clamp01(s.Age / SlideSeconds));
                float fadeOut = Mathf.Clamp01((LineSeconds - s.Age) / FadeSeconds);
                float a = Mathf.Min(fadeIn, fadeOut);
                if (Mathf.Abs(s.Group.alpha - a) > 1e-3f) s.Group.alpha = a;
            }
        }

        /// <summary>Removes every line (a new match).</summary>
        public void Clear()
        {
            _queueHead = _queueCount = 0;
            for (int i = 0; i < Lines; i++)
            {
                _slots[i].Active = false;
                if (_slots[i].Rt != null) _slots[i].Rt.gameObject.SetActive(false);
            }
        }

        /// <summary>Row r (0 = newest) as shown.</summary>
        public Entry Row(int r)
        {
            if (r < 0 || r >= Lines) return default;
            Slot s = _slots[_order[r]];
            return new Entry { Active = s.Active, Killer = s.Killer.text, Victim = s.Victim.text, Own = s.Own, Age = s.Age };
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Lines; i++) if (_slots[i].Active) n++;
                return n;
            }
        }
    }
}
