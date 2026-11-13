using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// The character panel (owner ask 2026-11-12: "see the character, customize him, the outfit, the tools, effects, and the
    /// skills") and, opened editable, the Barracks (DG-27). One contained panel with four tabs:
    /// Character (the class model turning on a small render texture, or the portrait when the class has no model yet;
    /// name, role line, health, speed, carry), Outfit (headgear, emblem, victory emote, name; team colours locked),
    /// Tools (attack, ability and gather/deliver with icon, key, what it does, cooldown, numbers from the sim config) and
    /// Skills (level, skill-point bar, coins, record, what the next level unlocks).
    /// It pauses nothing: in a live match it is a view over the running game and the Outfit tab is view-only ("change your
    /// look in the Barracks between matches"). Opened by clicking the HUD portrait, C, or D-pad up (ProgressionHud), and
    /// by other screens through Show / Toggle / Barracks.Show. Escape or B closes it (it takes the Pause press while open,
    /// as the pause menu does). Read-only on the sim; the profile is the only thing it writes.
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 32, Scope = CriticalMass.Runtime.Flow.OverlayScope.Always)]   // installed once by GameBootstrap through the overlay registry
    public sealed class CharacterPanel : MonoBehaviour
    {
        public enum Tab { Character, Outfit, Tools, Skills }

        public static CharacterPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;
        /// <summary>The Outfit tab can change the loadout (Barracks, no live match).</summary>
        public static bool Editable => Instance != null && Instance._editable;
        public static bool IsTyping => Instance != null && Instance._typing;
        public static event Action Opened, Closed;
        /// <summary>The loadout or name changed (after the profile was saved).</summary>
        public static event Action<ClassId> LoadoutChanged;

        public Tab CurrentTab { get; private set; } = Tab.Character;
        public ClassId ShownClass { get; private set; } = ClassId.Warrior;

        const float PX = 240f, PY = 96f, PW = 1440f, PH = 888f;
        const float CY = PY + 230f;
        const float RX = PX + 560f, RW = PW - 600f;

        static readonly string[] TabNames = { "CHARACTER", "OUTFIT", "TOOLS", "SKILLS" };
        static readonly string[] NameA = { "BASIN", "HILL", "IRON", "MOSS", "STONE", "EMBER", "SKY", "ROOT", "BRASS", "STORM" };
        static readonly string[] NameB = { "RUNNER", "WARDEN", "DIGGER", "KEEPER", "BRAWLER", "SAPPER", "HARVEST", "CROWN", "HAMMER", "SPROUT" };

        SimDriverBehaviour _driver;
        HumanInputSource _input;
        Func<bool> _prevPause;
        Canvas _canvas;
        RectTransform _space;
        bool _open, _editable, _built, _typing;
        int _typingFrame;
        readonly MenuItem[] _tabs = new MenuItem[4];
        readonly GameObject[] _pages = new GameObject[4];
        MenuItem _back, _classStep;
        Text _className, _role, _headerInfo, _hint;

        // character page
        GameObject _previewArea;
        RawImage _previewImage, _portrait;
        readonly CharacterPreview _preview = new CharacterPreview();
        Text _nameBig, _team;
        readonly Text[] _statValue = new Text[4];

        // outfit page
        readonly MenuItem[] _slotItems = new MenuItem[3];
        readonly Text[] _slotStatus = new Text[3];
        readonly List<CosmeticItem>[] _options = { new List<CosmeticItem>(), new List<CosmeticItem>(), new List<CosmeticItem>() };
        readonly int[] _browse = new int[3];
        MenuItem _nameItem, _randomItem;
        Text _outfitNote;

        // tools page
        readonly RawImage[] _toolIcon = new RawImage[3];
        readonly Text[] _toolTitle = new Text[3], _toolKey = new Text[3], _toolText = new Text[3], _toolCd = new Text[3];

        // skills page
        LevelBar _bar;
        Text _coins, _record, _next;

        // ---- static API ----

        /// <summary>Opens the panel on a tab. editable is honoured only when no match is live. cls: the class to show
        /// (null = the local player's class in a match, else the last shown).</summary>
        public static void Show(Tab tab = Tab.Character, bool editable = false, ClassId? cls = null)
        {
            CharacterPanel p = Ensure();
            p.Open(tab, editable && !MatchLive(p._driver), cls);
        }

        /// <summary>The HUD portrait, C and D-pad up: open on Character (Barracks rules apply), or close.</summary>
        public static void Toggle()
        {
            if (IsOpen) Hide();
            else Show(Tab.Character, true);
        }

        public static void Hide()
        {
            if (Instance != null) Instance.Close();
        }

        /// <summary>A match is running (world exists and the phase is Playing).</summary>
        public static bool MatchLive(SimDriverBehaviour d)
        {
            if (d == null) d = FindAnyObjectByType<SimDriverBehaviour>();
            // The bot match behind the menus is not a live match: the Barracks stay editable there.
            if (d != null && d.Request != null && d.Request.Backdrop) return false;
            return d != null && d.World != null && d.World.Match.Phase == MatchPhase.Playing;
        }

        static CharacterPanel Ensure()
        {
            if (Instance != null) return Instance;
            SimDriverBehaviour d = FindAnyObjectByType<SimDriverBehaviour>();
            GameObject host = d != null ? d.gameObject : new GameObject("Character Panel");
            CharacterPanel p = host.GetComponent<CharacterPanel>();
            if (p == null) p = host.AddComponent<CharacterPanel>();
            Instance = p;
            return p;
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (_open) Close();
            if (Instance == this) Instance = null;
        }

        void OnDestroy() => _preview.Release();

        // ---- open / close ----

        void Open(Tab tab, bool editable, ClassId? cls)
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _input = GetComponent<HumanInputSource>();
            Build();
            Actor me = LocalActor();
            if (cls.HasValue) ShownClass = cls.Value;
            else if (me != null) ShownClass = me.Cls;
            _editable = editable;
            if (!_editable && me != null) ShownClass = me.Cls;
            _open = true;
            _canvas.enabled = true;
            if (_input != null && _input.PauseHandler != (Func<bool>)OnPause)
            {
                _prevPause = _input.PauseHandler;
                _input.PauseHandler = OnPause;
            }
            PauseMenu.EnsureEventSystem(transform);
            _classStep.gameObject.SetActive(_editable);
            RefreshAll();
            ShowTab(tab);
            Opened?.Invoke();
        }

        void Close()
        {
            if (!_open) return;
            StopTyping();
            _open = false;
            if (_canvas != null) _canvas.enabled = false;
            _preview.Release();
            if (_input != null && _input.PauseHandler == (Func<bool>)OnPause) _input.PauseHandler = _prevPause;
            _prevPause = null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Closed?.Invoke();
        }

        bool OnPause()
        {
            if (!_open) return false;
            if (_typing) StopTyping();
            else Close();
            return true;
        }

        Actor LocalActor()
        {
            World w = _driver != null ? _driver.World : null;
            return w != null && w.ActorCount > 0 ? w.Actors[0] : null;
        }

        TeamId ShownTeam
        {
            get
            {
                Actor me = LocalActor();
                return me != null ? me.Team : TeamId.Azure;
            }
        }

        GameConfig Config => _driver != null && _driver.Config != null ? _driver.Config : (_fallbackConfig ?? (_fallbackConfig = GameConfig.CreateDefault()));
        static GameConfig _fallbackConfig;

        // ---- tabs ----

        public void ShowTab(Tab tab)
        {
            StopTyping();
            CurrentTab = tab;
            for (int i = 0; i < 4; i++)
            {
                _pages[i].SetActive(i == (int)tab);
                VectorShape s = _tabs[i].Body;
                s.SetRim(i == (int)tab ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color);
                s.RimPx = i == (int)tab ? 4f : 2f;
                s.SetVerticesDirty();
            }
            bool preview = tab == Tab.Character || tab == Tab.Outfit;
            _previewArea.SetActive(preview);
            if (preview) RefreshPreview();
            else _preview.Release();
            WireNavigation();
            ProgressionUi.Select(tab == Tab.Outfit && _editable ? _slotItems[0] : (Selectable)_tabs[(int)tab]);
            UpdateHint();
        }

        void StepClass(int d)
        {
            if (!_editable) return;
            int n = ClassIds.All.Count;
            ShownClass = (ClassId)(((int)ShownClass + d + n) % n);
            RefreshAll();
            if (_previewArea.activeSelf) RefreshPreview();
        }

        // ---- build ----

        void Build()
        {
            if (_built) return;
            _built = true;
            _canvas = ProgressionUi.MakeCanvas(transform, "Character Panel Canvas", 46, out _space);
            ProgressionUi.Wash(_canvas.transform, 0.7f).transform.SetAsFirstSibling();
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;
            MenuUi.Panel(_space, "Panel", PX, PY, PW, PH);
            _className = MenuUi.Label(_space, "Class", CMTokens.Display, 50, cream, TextAnchor.MiddleLeft, PX + 40f, PY + 24f, 760f, 64f, "", 4f);
            _role = MenuUi.Label(_space, "Role", CMTokens.Ui, 20, cream, TextAnchor.MiddleLeft, PX + 40f, PY + 88f, 900f, 32f, "", 2f);
            _headerInfo = MenuUi.Label(_space, "Header Info", CMTokens.Display, 24, brass, TextAnchor.MiddleRight, PX + PW - 680f, PY + 36f, 420f, 44f, "", 2f);
            _back = MenuUi.Item(_space, "Back", PX + PW - 40f - 200f, PY + 30f, 200f, 60f, "BACK", MenuItem.Kind.Button, 26, true, CMTokens.Display);
            _back.Submitted = Close;
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                MenuItem t = MenuUi.Item(_space, "Tab " + TabNames[i], PX + 40f + i * 256f, PY + 140f, 240f, 60f, TabNames[i],
                    MenuItem.Kind.Button, 24, true, CMTokens.Display);
                t.Submitted = () => ShowTab((Tab)k);
                _tabs[i] = t;
            }
            _classStep = MenuUi.Item(_space, "Class Step", PX + PW - 40f - 400f, PY + 140f, 400f, 60f, "CLASS", MenuItem.Kind.Stepper, 22, false, CMTokens.Display);
            _classStep.Value = MenuUi.Label(_classStep.transform, "Value", CMTokens.Ui, 22, MenuItem.IdleText, TextAnchor.MiddleCenter, 150f, 0f, 220f, 60f, "", 2f);
            MenuUi.Label(_classStep.transform, "Left", CMTokens.Display, 24, brass, TextAnchor.MiddleCenter, 120f, 0f, 30f, 60f, "<", 2f);
            MenuUi.Label(_classStep.transform, "Right", CMTokens.Display, 24, brass, TextAnchor.MiddleCenter, 365f, 0f, 30f, 60f, ">", 2f);
            _classStep.Stepped = StepClass;
            _hint = MenuUi.Label(_space, "Hint", CMTokens.Ui, 20, cream, TextAnchor.MiddleCenter, 260f, 1012f, 1400f, 36f, "", 2f);

            // preview (Character and Outfit tabs)
            RectTransform pa = MenuUi.Rt(_space, "Preview Area");
            MenuUi.Stretch(pa);
            _previewArea = pa.gameObject;
            VectorShape frame = MenuUi.Shape(pa, "Preview Frame", ShapeKind.Chamfer, PX + 40f, CY, 480f, 600f, CMTokens.PanelNavy.Color, 22f);
            frame.OutlinePx = 4f;
            frame.RimPx = 3f;
            _previewImage = MenuUi.Rt(frame.transform, "Preview").gameObject.AddComponent<RawImage>();
            MenuUi.Place(_previewImage.rectTransform, 8f, 8f, 464f, 584f);
            _previewImage.raycastTarget = false;
            _portrait = MenuUi.Rt(frame.transform, "Portrait").gameObject.AddComponent<RawImage>();
            MenuUi.Place(_portrait.rectTransform, 40f, 100f, 400f, 400f);
            _portrait.raycastTarget = false;

            for (int i = 0; i < 4; i++)
            {
                RectTransform page = MenuUi.Rt(_space, TabNames[i] + " Page");
                MenuUi.Stretch(page);
                _pages[i] = page.gameObject;
            }
            BuildCharacter(_pages[0].transform);
            BuildOutfit(_pages[1].transform);
            BuildTools(_pages[2].transform);
            BuildSkills(_pages[3].transform);
            _canvas.enabled = false;
        }

        static readonly string[] StatNames = { "HEALTH", "SPEED", "CARRY", "GATHER" };

        void BuildCharacter(Transform page)
        {
            Color cream = CMTokens.WarmCream.Color;
            _nameBig = MenuUi.Label(page, "Name", CMTokens.Display, 40, cream, TextAnchor.MiddleLeft, RX, CY, RW, 56f, "", 3f);
            _team = MenuUi.Label(page, "Team", CMTokens.Display, 22, cream, TextAnchor.MiddleLeft, RX, CY + 60f, RW, 34f, "", 2f);
            for (int i = 0; i < StatNames.Length; i++)
            {
                float y = CY + 130f + i * 74f;
                VectorShape row = MenuUi.Shape(page, "Stat " + StatNames[i], ShapeKind.RoundRect, RX, y, 620f, 60f, CMTokens.PanelNavy.Color, 12f);
                row.RimPx = 2f;
                MenuUi.Label(row.transform, "Label", CMTokens.Display, 22, cream, TextAnchor.MiddleLeft, 20f, 0f, 300f, 60f, StatNames[i], 2f);
                _statValue[i] = MenuUi.Label(row.transform, "Value", CMTokens.Mono, 24, CMTokens.ToolBrass.Color, TextAnchor.MiddleRight, 300f, 0f, 300f, 60f, "", 2f);
            }
            ProgressionUi.Wrap(MenuUi.Label(page, "Team Note", CMTokens.Ui, 18, CMTokens.Stone.Color, TextAnchor.UpperLeft, RX, CY + 440f, 760f, 60f,
                ProgressionWords.TeamColoursLocked, 0f));
        }

        static readonly string[] SlotNames = { "HEADGEAR", "EMBLEM", "VICTORY EMOTE" };

        void BuildOutfit(Transform page)
        {
            Color brass = CMTokens.ToolBrass.Color;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                float y = CY + i * 96f;
                MenuItem it = MenuUi.Item(page, SlotNames[i], RX, y, 560f, 80f, SlotNames[i], MenuItem.Kind.Stepper, 22, false, CMTokens.Display);
                it.Value = MenuUi.Label(it.transform, "Value", CMTokens.Ui, 22, MenuItem.IdleText, TextAnchor.MiddleCenter, 250f, 0f, 270f, 80f, "", 2f);
                MenuUi.Label(it.transform, "Left", CMTokens.Display, 24, brass, TextAnchor.MiddleCenter, 230f, 0f, 24f, 80f, "<", 2f);
                MenuUi.Label(it.transform, "Right", CMTokens.Display, 24, brass, TextAnchor.MiddleCenter, 520f, 0f, 24f, 80f, ">", 2f);
                it.Stepped = d => StepSlot(k, d);
                _slotItems[i] = it;
                _slotStatus[i] = MenuUi.Label(page, SlotNames[i] + " Status", CMTokens.Ui, 18, CMTokens.Stone.Color, TextAnchor.MiddleLeft,
                    RX + 580f, y, 260f, 80f, "", 0f);
            }
            float ny = CY + 3 * 96f;
            _nameItem = MenuUi.Item(page, "Name", RX, ny, 560f, 80f, "NAME", MenuItem.Kind.Button, 22, false, CMTokens.Display);
            _nameItem.Value = MenuUi.Label(_nameItem.transform, "Value", CMTokens.Ui, 24, MenuItem.IdleText, TextAnchor.MiddleCenter, 150f, 0f, 390f, 80f, "", 2f);
            _nameItem.Submitted = StartTyping;
            _randomItem = MenuUi.Item(page, "Random Name", RX + 580f, ny, 260f, 80f, "RANDOM", MenuItem.Kind.Button, 22, true, CMTokens.Display);
            _randomItem.Submitted = RandomName;
            _outfitNote = ProgressionUi.Wrap(MenuUi.Label(page, "Outfit Note", CMTokens.Ui, 20, CMTokens.WarmCream.Color, TextAnchor.UpperLeft,
                RX, CY + 410f, RW, 90f, "", 2f));
            ProgressionUi.Wrap(MenuUi.Label(page, "Team Note", CMTokens.Ui, 18, CMTokens.Stone.Color, TextAnchor.UpperLeft, RX, CY + 510f, RW, 60f,
                ProgressionWords.TeamColoursLocked, 0f));
        }

        static readonly string[] ToolNames = { "ATTACK", "ABILITY", "GATHER AND DELIVER" };

        void BuildTools(Transform page)
        {
            Color cream = CMTokens.WarmCream.Color;
            for (int i = 0; i < 3; i++)
            {
                float x = PX + 40f + i * 456f;
                VectorShape card = MenuUi.Shape(page, "Card " + ToolNames[i], ShapeKind.RoundRect, x, CY, 432f, 600f, CMTokens.PanelNavy.Color, 18f);
                card.OutlinePx = 4f;
                card.RimPx = 3f;
                _toolIcon[i] = MenuUi.Rt(card.transform, "Icon").gameObject.AddComponent<RawImage>();
                MenuUi.Place(_toolIcon[i].rectTransform, 24f, 24f, 112f, 112f);
                _toolIcon[i].raycastTarget = false;
                _toolTitle[i] = ProgressionUi.Wrap(MenuUi.Label(card.transform, "Title", CMTokens.Display, 26, cream, TextAnchor.MiddleLeft, 152f, 24f, 260f, 66f, "", 2f));
                _toolKey[i] = MenuUi.Label(card.transform, "Key", CMTokens.Mono, 22, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 152f, 92f, 260f, 34f, "", 2f);
                _toolText[i] = ProgressionUi.Wrap(MenuUi.Label(card.transform, "Text", CMTokens.Ui, 21, cream, TextAnchor.UpperLeft, 24f, 166f, 384f, 320f, "", 2f));
                _toolCd[i] = MenuUi.Label(card.transform, "Cooldown", CMTokens.Mono, 20, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 24f, 530f, 384f, 44f, "", 2f);
            }
        }

        void BuildSkills(Transform page)
        {
            Color cream = CMTokens.WarmCream.Color;
            _bar = LevelBar.Build(page, "Level Bar", PX + 40f, CY, PW - 80f);
            ProgressionUi.Coin(page, "Coin", PX + 40f, CY + 116f, 40f);
            _coins = MenuUi.Label(page, "Coins", CMTokens.Display, 30, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, PX + 96f, CY + 110f, 600f, 52f, "", 3f);
            _record = MenuUi.Label(page, "Record", CMTokens.Ui, 22, cream, TextAnchor.MiddleLeft, PX + 40f, CY + 176f, PW - 80f, 40f, "", 2f);
            MenuUi.Label(page, "Next Head", CMTokens.Display, 24, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, PX + 40f, CY + 246f, PW - 80f, 40f,
                ProgressionWords.NextUnlock, 2f);
            _next = ProgressionUi.Wrap(MenuUi.Label(page, "Next", CMTokens.Ui, 22, cream, TextAnchor.UpperLeft, PX + 40f, CY + 292f, PW - 80f, 200f, "", 2f));
            ProgressionUi.Wrap(MenuUi.Label(page, "How", CMTokens.Ui, 18, CMTokens.Stone.Color, TextAnchor.UpperLeft, PX + 40f, CY + 520f, PW - 80f, 70f,
                "Knock-outs, deliveries to the egg, time on the hill and knocking out the Kaiju earn skill points and coins at the end of every match. " +
                "Levels unlock looks only, never strength.", 0f));
        }

        // ---- navigation ----

        void WireNavigation()
        {
            var tabs = new List<Selectable>(_tabs);
            if (_editable) tabs.Add(_classStep);
            tabs.Add(_back);
            ProgressionUi.ChainHorizontal(tabs);
            var rows = new List<Selectable>();
            if (CurrentTab == Tab.Outfit && _editable)
            {
                rows.AddRange(_slotItems);
                rows.Add(_nameItem);
            }
            Selectable current = _tabs[(int)CurrentTab];
            foreach (Selectable t in tabs)
            {
                Navigation n = t.navigation;
                n.selectOnDown = rows.Count > 0 ? rows[0] : null;
                n.selectOnUp = rows.Count > 0 ? rows[rows.Count - 1] : null;
                t.navigation = n;
            }
            if (rows.Count == 0) return;
            ProgressionUi.ChainVertical(rows);
            Navigation first = rows[0].navigation;
            first.selectOnUp = current;
            rows[0].navigation = first;
            Navigation last = rows[rows.Count - 1].navigation;
            last.selectOnDown = current;
            last.selectOnRight = _randomItem;
            rows[rows.Count - 1].navigation = last;
            Navigation rnd = _randomItem.navigation;
            rnd.mode = Navigation.Mode.Explicit;
            rnd.selectOnLeft = _nameItem;
            rnd.selectOnRight = _nameItem;
            rnd.selectOnUp = rows[rows.Count - 2];
            rnd.selectOnDown = current;
            _randomItem.navigation = rnd;
            // A stepper's left / right steps its value, so the class stepper is reached through the tab row only.
        }

        // ---- refresh ----

        void RefreshAll()
        {
            PlayerProfile p = Profile.Current;
            ClassId c = ShownClass;
            TeamId team = ShownTeam;
            int ci = (int)c, ti = (int)team;
            HudText.Set(_className, ProgressionWords.ClassName(c));
            HudText.Set(_role, ProgressionWords.RoleLines[ci]);
            HudText.Set(_headerInfo, ProgressionWords.Level + " " + p.Level.ToString(CultureInfo.InvariantCulture) + "   " +
                                     ProgressionUi.Thousands(p.Coins) + " " + ProgressionWords.Coins);
            if (_classStep.Value != null) HudText.Set(_classStep.Value, ProgressionWords.ClassName(c));

            // character
            GameConfig cfg = Config;
            ClassConfig k = cfg.Class(c);
            HudText.Set(_nameBig, p.PlayerName);
            _team.color = CMTokens.Team(team);
            HudText.Set(_team, team == TeamId.Azure ? "AZURE" : "CRIMSON");
            HudText.Set(_statValue[0], ProgressionWords.Number(k.MaxHp) + " HP");
            HudText.Set(_statValue[1], ProgressionWords.Number(k.MoveSpeed) + " M/S");
            HudText.Set(_statValue[2], ProgressionWords.Number(k.CarryCapacity));
            HudText.Set(_statValue[3], ProgressionWords.Seconds(k.GatherSeconds));
            int tier = UiIcons.TierFor(_canvas.scaleFactor);
            _portrait.texture = UiIcons.Get(UiIcons.Portrait[ci, ti], tier);

            // outfit
            ClassLoadout l = p.Loadout(c);
            for (int s = 0; s < 3; s++)
            {
                var slot = (CosmeticSlot)s;
                List<CosmeticItem> opts = _options[s];
                opts.Clear();
                if (slot == CosmeticSlot.Head) opts.Add(null);   // bare head
                opts.AddRange(CosmeticItems.WithExtras(c, slot, Cosmetics.ArtIds(c, slot)));
                string saved = slot == CosmeticSlot.Head ? l.Head : slot == CosmeticSlot.Emblem ? l.Emblem : l.Emote;
                _browse[s] = Mathf.Max(0, IndexOf(opts, string.IsNullOrEmpty(saved) && slot != CosmeticSlot.Head ? DefaultId(c, slot) : saved));
                _slotItems[s].interactable = _editable;
                ShowSlot(s);
            }
            HudText.Set(_nameItem.Value, p.PlayerName);
            _nameItem.interactable = _editable;
            _randomItem.interactable = _editable;
            HudText.Set(_outfitNote, _editable ? "Choose a look for each class. New pieces unlock as you level up." : ProgressionWords.ChangeBetweenMatches);

            // tools
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            ControlDevice dev = ControlBindings.ActiveDevice;
            string[] actions = { "Attack", "Ability", "Interact" };
            string[] icons = { UiIcons.Attack[ci, ti], UiIcons.Ability[ci, ti], UiIcons.Biomass };
            for (int i = 0; i < 3; i++)
            {
                _toolIcon[i].texture = UiIcons.Get(icons[i], tier);
                string key = map != null ? ControlBindings.PromptLabel(map.FindAction(actions[i], false), dev) : "-";
                HudText.Set(_toolKey[i], key);
            }
            HudText.Set(_toolTitle[0], ToolNames[0]);
            HudText.Set(_toolText[0], ProgressionWords.AttackText(cfg, c));
            HudText.Set(_toolCd[0], c == ClassId.Demolitions ? string.Empty : "EVERY " + ProgressionWords.Seconds(k.AttackCooldown));
            HudText.Set(_toolTitle[1], ProgressionWords.AbilityNames[ci]);
            HudText.Set(_toolText[1], ProgressionWords.AbilityText(cfg, c));
            HudText.Set(_toolCd[1], "COOLDOWN " + ProgressionWords.Seconds(k.AbilityCooldown));
            HudText.Set(_toolTitle[2], ToolNames[2]);
            HudText.Set(_toolText[2], "Hold at a plant to gather (" + ProgressionWords.Seconds(k.GatherSeconds).ToLowerInvariant() +
                                      " each), carry up to " + ProgressionWords.Number(k.CarryCapacity) +
                                      ", and bring it to the egg. Whoever feeds the egg most turns the Kaiju to their side.");
            HudText.Set(_toolCd[2], "CARRY " + ProgressionWords.Number(k.CarryCapacity));

            // skills
            _bar.Set(p.SkillPoints);
            HudText.Set(_coins, ProgressionUi.Thousands(p.Coins) + " " + ProgressionWords.Coins);
            HudText.Set(_record, p.MatchesPlayed.ToString(CultureInfo.InvariantCulture) + " MATCHES   " +
                                 p.Wins.ToString(CultureInfo.InvariantCulture) + " WINS   " +
                                 p.Draws.ToString(CultureInfo.InvariantCulture) + " DRAWS   " +
                                 p.KnockOuts.ToString(CultureInfo.InvariantCulture) + " KNOCK-OUTS");
            HudText.Set(_next, NextUnlockText(c, p.Level));
            UpdateHint();
        }

        static string DefaultId(ClassId c, CosmeticSlot s) => CosmeticItems.Default(c, s)?.Id;

        static int IndexOf(List<CosmeticItem> opts, string id)
        {
            for (int i = 0; i < opts.Count; i++)
                if ((opts[i] == null && string.IsNullOrEmpty(id)) || (opts[i] != null && opts[i].Id == id)) return i;
            return 0;
        }

        /// <summary>What the next level that unlocks anything unlocks for this class.</summary>
        public static string NextUnlockText(ClassId c, int level)
        {
            for (int l = level + 1; l <= Leveling.MaxLevel; l++)
            {
                var names = new List<string>();
                foreach (CosmeticItem it in CosmeticItems.UnlockedAtLevel(l))
                    if (it.Class == c) names.Add(it.DisplayName + " (" + SlotNames[(int)it.Slot].ToLowerInvariant() + ")");
                if (names.Count > 0)
                    return ProgressionWords.Level + " " + l.ToString(CultureInfo.InvariantCulture) + ": " + string.Join(", ", names);
            }
            return ProgressionWords.NothingNext;
        }

        void ShowSlot(int s)
        {
            List<CosmeticItem> opts = _options[s];
            if (opts.Count == 0) return;
            CosmeticItem it = opts[Mathf.Clamp(_browse[s], 0, opts.Count - 1)];
            HudText.Set(_slotItems[s].Value, it == null ? ProgressionWords.NoHeadgear : it.DisplayName);
            int level = Profile.Current.Level;
            string status;
            Color col = CMTokens.Stone.Color;
            if (it == null || (CosmeticItems.Unlocked(it, level) && Cosmetics.Has(ShownClass, (CosmeticSlot)s, it.Id)))
            {
                bool equipped = IsEquipped(s, it);
                status = equipped ? ProgressionWords.Equipped : string.Empty;
                col = CMTokens.ToolBrass.Color;
            }
            else if (!CosmeticItems.Unlocked(it, level)) status = ProgressionWords.Unlocks + it.UnlockLevel.ToString(CultureInfo.InvariantCulture);
            else status = ProgressionWords.ComingSoon;
            _slotStatus[s].color = col;
            HudText.Set(_slotStatus[s], status);
        }

        bool IsEquipped(int s, CosmeticItem it)
        {
            ClassLoadout l = Profile.Current.Loadout(ShownClass);
            string saved = s == 0 ? l.Head : s == 1 ? l.Emblem : l.Emote;
            if (string.IsNullOrEmpty(saved) && s != 0) saved = DefaultId(ShownClass, (CosmeticSlot)s);
            return it == null ? string.IsNullOrEmpty(saved) : it.Id == saved;
        }

        void StepSlot(int s, int d)
        {
            if (!_editable) return;
            List<CosmeticItem> opts = _options[s];
            if (opts.Count == 0) return;
            _browse[s] = (_browse[s] + d + opts.Count) % opts.Count;
            CosmeticItem it = opts[_browse[s]];
            int level = Profile.Current.Level;
            bool usable = it == null || (CosmeticItems.Unlocked(it, level) && Cosmetics.Has(ShownClass, (CosmeticSlot)s, it.Id));
            if (usable)
            {
                ClassLoadout l = Profile.Current.Loadout(ShownClass);
                string id = it == null ? string.Empty : it.Id;
                if (s == 0) l.Head = id;
                else if (s == 1) l.Emblem = id;
                else l.Emote = id;
                Commit();
                RefreshPreview();
            }
            ShowSlot(s);
        }

        void Commit()
        {
            Profile.Commit();
            LoadoutChanged?.Invoke(ShownClass);
        }

        void RefreshPreview()
        {
            PlayerProfile p = Profile.Current;
            bool live = _preview.Show(ShownClass, ShownTeam, p.Loadout(ShownClass), p.Level);
            _previewImage.texture = live ? _preview.Texture : null;
            _previewImage.enabled = live;
            _portrait.enabled = !live;
        }

        // ---- name ----

        void StartTyping()
        {
            if (!_editable || _typing) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            _typing = true;
            _typingFrame = Time.frameCount;
            kb.onTextInput += OnChar;
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = false;
            // The default name is replaced, not appended to.
            if (Profile.Current.PlayerName == PlayerProfile.DefaultName) Profile.Current.PlayerName = string.Empty;
            HudText.Set(_nameItem.Value, Profile.Current.PlayerName + "_");
            UpdateHint();
        }

        void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            Keyboard kb = Keyboard.current;
            if (kb != null) kb.onTextInput -= OnChar;
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = true;
            PlayerProfile p = Profile.Current;
            p.PlayerName = PlayerProfile.CleanName(p.PlayerName);
            Commit();
            if (_built) RefreshAll();
        }

        void OnChar(char c)
        {
            if (!_typing || Time.frameCount == _typingFrame || char.IsControl(c)) return;
            PlayerProfile p = Profile.Current;
            string cur = p.PlayerName ?? string.Empty;
            if (cur.Length >= PlayerProfile.MaxNameLength) return;
            if (c == ' ')
            {
                if (cur.Length > 0 && cur[cur.Length - 1] != ' ') p.PlayerName = cur + " ";
            }
            else
            {
                char u = char.ToUpperInvariant(c);
                bool ok = (u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9') || u == '-' || u == '_';
                if (ok) p.PlayerName = cur + u;
            }
            HudText.Set(_nameItem.Value, p.PlayerName + "_");
        }

        void RandomName()
        {
            if (!_editable) return;
            StopTyping();
            Profile.Current.PlayerName = NameA[UnityEngine.Random.Range(0, NameA.Length)] + " " + NameB[UnityEngine.Random.Range(0, NameB.Length)];
            Commit();
            RefreshAll();
        }

        // ---- per frame ----

        void Update()
        {
            if (!_open) return;
            ProgressionUi.UpdateScale(_canvas);
            if (_previewArea.activeSelf) _preview.Tick(Time.unscaledDeltaTime);
            if (CurrentTab == Tab.Skills) _bar.Tick();
            Keyboard kb = Keyboard.current;
            if (_typing)
            {
                if (kb == null) return;
                if (kb.backspaceKey.wasPressedThisFrame)
                {
                    PlayerProfile p = Profile.Current;
                    if (p.PlayerName.Length > 0) p.PlayerName = p.PlayerName.Substring(0, p.PlayerName.Length - 1);
                    HudText.Set(_nameItem.Value, p.PlayerName + "_");
                }
                if (Time.frameCount != _typingFrame && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) StopTyping();
                return;
            }
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame)
                {
                    Close();
                    return;
                }
                if (pad.leftShoulder.wasPressedThisFrame) ShowTab((Tab)(((int)CurrentTab + 3) % 4));
                else if (pad.rightShoulder.wasPressedThisFrame) ShowTab((Tab)(((int)CurrentTab + 1) % 4));
            }
            // A live match that ends while open: the Outfit tab stays view-only until reopened (no surprise edits).
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null && NavigationPressed(kb, pad)) ProgressionUi.Select(_tabs[(int)CurrentTab]);
        }

        static bool NavigationPressed(Keyboard kb, Gamepad pad)
        {
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame ||
                               kb.rightArrowKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) return true;
            return pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame ||
                                   pad.dpad.right.wasPressedThisFrame || pad.leftStick.ReadValue().sqrMagnitude > 0.25f);
        }

        void UpdateHint()
        {
            if (_hint == null) return;
            bool pad = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            string s;
            if (_typing) s = "TYPE A NAME   ·   ENTER DONE   ·   ESC DONE";
            else if (CurrentTab == Tab.Outfit && _editable)
                s = pad ? "A SELECT   ·   LEFT / RIGHT CHANGE   ·   LB / RB TABS   ·   B CLOSE" : "LEFT / RIGHT CHANGE   ·   ENTER SELECT   ·   ESC CLOSE";
            else s = pad ? "A SELECT   ·   LB / RB TABS   ·   B CLOSE" : "ENTER SELECT   ·   ESC CLOSE";
            HudText.Set(_hint, s);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Opened = null;
            Closed = null;
            LoadoutChanged = null;
            _fallbackConfig = null;
        }
    }

    /// <summary>
    /// The Barracks (DG-27): the character panel opened editable on the Outfit tab. Between matches only: in a live
    /// match it opens view-only. DG-01's main menu calls Barracks.Show().
    /// </summary>
    public static class Barracks
    {
        public static bool IsOpen => CharacterPanel.IsOpen && CharacterPanel.Editable;

        public static void Show(ClassId? cls = null) => CharacterPanel.Show(CharacterPanel.Tab.Outfit, true, cls);

        public static void Hide() => CharacterPanel.Hide();
    }
}
