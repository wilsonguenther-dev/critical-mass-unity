using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// Actor, Kaiju, resource node, barricade and hill views. Since the art import (2026-11-12) every entity looks its model
    /// up in ArtMapping (the one class-to-model table) and the ArtCatalog built from Assets/_Game/Art/**: Warrior and Miner
    /// models in team colours, the Kaiju egg / hatchling / adult in the allegiance glow, biomass and corruption props for
    /// the nodes, the Miner barricade for barricade entities. Anything without a model (Farmer, Demolitions, Builder, or a
    /// project whose catalog was never built) keeps the grey-box primitive on CM/Toon (ToonMaterials).
    /// Clips are driven from sim state only (drawn displacement, cooldown timers, Kaiju timers) and sim time, so a capture
    /// after the same ticks shows the same pose. Since the animation pass (2026-11-12) the clip logic lives in
    /// Runtime/View/Anim (ActorAnimator, KaijuAnimator); this class places models and calls them, then PostPose (contract C3). Every Sim-to-Unity position goes through SimSpace (O-13). No Unity physics.
    /// Reads the Sim only; never writes it. Model instances are pooled per model and never destroyed during play.
    /// </summary>
    public sealed class ActorViews : MonoBehaviour
    {
        /// <summary>The human's capsule is this much wider in X and Z (grey-box only).</summary>
        private const float HumanScale = 1.15f;
        private const float NodeSize = 0.6f;
        private const float HillThickness = 0.05f;
        /// <summary>Seconds before the hatch the egg shakes.</summary>
        private const double EggShakeSeconds = 6.0;

        private sealed class CharSlot
        {
            public Transform Capsule;
            public Renderer CapsuleRenderer;
            public ModelView Model;
            /// <summary>Animation pass (2026-11-12): all clip choice, timing, facing spring and squash for this actor (Runtime/View/Anim).</summary>
            public Anim.ActorAnimator Anim;
        }

        /// <summary>M9: spare pooled models per class model made at load, so a class change on respawn never instantiates.</summary>
        public const int PrewarmPerClass = 2;

        private readonly Dictionary<(int, string), string> _names = new Dictionary<(int, string), string>();
        private UnityEngine.Camera _cam;
        private Vector3 _camPos, _targetPos;
        private int _lodSelf;

        private SimDriverBehaviour _driver;
        private ArtCatalog _catalog;
        private CharSlot[] _actors;
        private readonly Dictionary<string, Stack<ModelView>> _pool = new Dictionary<string, Stack<ModelView>>();

        private Transform _kaijuCube;
        private Renderer _kaijuCubeRenderer;
        private ModelView _egg, _hatchling, _adult, _shards, _spikes;
        private double _hatchTime = double.NegativeInfinity;
        private bool _sawEgg;
        private KaijuState _lastKaijuState;
        private double _berserkAt = double.NegativeInfinity, _slamAt = double.NegativeInfinity;
        private bool _wasTelegraph;
        private double _eggIdleT;
        private readonly Anim.KaijuAnimator _kaijuAnim = new Anim.KaijuAnimator();
        private Anim.CarryProp _carryProp;
        /// <summary>feat/light-weapons-2: the light weapons in the hand (its own listener file).</summary>
        private Anim.WeaponProps _weaponProps;
        public Anim.WeaponProps WeaponProps => _weaponProps;
        private Anim.ActorPoseState _poseState;

        private Transform[] _nodes;
        private Renderer[] _nodeRenderers;
        private Dictionary<string, ModelView>[] _nodeModels;

        private ModelView[] _barricades;
        private Transform _hill;
        private double _lastSimTime = double.NaN;
        /// <summary>Combat feel (view only, Runtime/View/Effects): corpse, Hit clip, flash, hold and lean per actor.</summary>
        private Effects.CombatFx _fx;

        /// <summary>Models in use (for tests and the capture's frame report).</summary>
        public int ActiveModelCount { get; private set; }

        private void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null)
            {
                Debug.LogError("ActorViews needs a SimDriverBehaviour on the same GameObject");
                enabled = false;
                return;
            }
            World w = _driver.World;
            GameConfig cfg = _driver.Config;
            _catalog = ArtCatalog.Load();

            int capacity = World.ActorCapacity(cfg);
            _actors = new CharSlot[capacity];
            for (int i = 0; i < capacity; i++) _actors[i] = new CharSlot { Anim = new Anim.ActorAnimator(i) };
            _carryProp = new Anim.CarryProp(_catalog);
            Anim.PostPoseHooks.Register(_carryProp);
            _weaponProps = new Anim.WeaponProps(_catalog);
            Anim.PostPoseHooks.Register(_weaponProps);
            // DG-108: one model per class built before the first tick, so the match never instantiates mid-fight.
            foreach (ClassId c in ClassIds.All)
            {
                string key = ArtMapping.ClassModel(c);
                if (key == null || _catalog?.Find(key) == null) continue;
                ModelView pre = Make(key, "Actor pool " + key);
                if (pre != null) Give(pre);
            }

            GameObject kaiju = CreatePrimitive(PrimitiveType.Cube, "Kaiju");
            _kaijuCube = kaiju.transform;
            _kaijuCubeRenderer = kaiju.GetComponent<Renderer>();
            _egg = Make(ArtMapping.KaijuEgg, "Kaiju Egg");
            _hatchling = Make(ArtMapping.KaijuHatchling, "Kaiju Hatchling");
            _adult = Make(ArtMapping.KaijuAdult, "Kaiju Adult");
            if (_adult != null) _shards = Make(ArtMapping.KaijuShards, "Kaiju Berserk Shards", _adult.Transform);
            _spikes = Make(ArtMapping.KaijuSpikes, "Kaiju Telegraph Spikes");

            int nodeCount = w.Nodes.Length;
            _nodes = new Transform[nodeCount];
            _nodeRenderers = new Renderer[nodeCount];
            _nodeModels = new Dictionary<string, ModelView>[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                GameObject go = CreatePrimitive(PrimitiveType.Cube, "Node " + i);
                _nodes[i] = go.transform;
                _nodeRenderers[i] = go.GetComponent<Renderer>();
                _nodeModels[i] = new Dictionary<string, ModelView>();
            }

            _barricades = new ModelView[w.Barricades != null ? w.Barricades.Length : 0];

            GameObject hill = CreatePrimitive(PrimitiveType.Cube, "Hill");
            _hill = hill.transform;
            hill.GetComponent<Renderer>().sharedMaterial = ToonMaterials.Prop(Palette.Brass);

            // M9 (physics audit): prewarm spare models per class so a respawn as another class takes one from the pool.
            if (_catalog != null)
                foreach (ClassId cls in ClassIds.All)
                {
                    string key = ArtMapping.ClassModel(cls);
                    if (key == null || _catalog.Find(key) == null) continue;
                    for (int k = 0; k < PrewarmPerClass; k++)
                    {
                        ModelView m = Make(key, "Pool " + key);
                        if (m != null) Give(m);
                    }
                }
        }

        private void OnDestroy()
        {
            if (_carryProp != null) Anim.PostPoseHooks.Unregister(_carryProp);
            if (_weaponProps != null) Anim.PostPoseHooks.Unregister(_weaponProps);
            foreach (Stack<ModelView> s in _pool.Values) foreach (ModelView m in s) m.Dispose();
        }

        private GameObject CreatePrimitive(PrimitiveType type, string objectName)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            return go;
        }

        /// <summary>A new model instance for a mapping key, or null when the catalog has no model for it.</summary>
        private ModelView Make(string key, string objectName, Transform parent = null)
        {
            ArtEntry e = _catalog != null ? _catalog.Find(key) : null;
            if (e == null || ArtMapping.Spec(key) == null) return null;
            var m = new ModelView(e, parent != null ? parent : transform, objectName);
            m.SetActive(false);
            return m;
        }

        /// <summary>
        /// perf/fullscreen-cost-1: new fighter models built per frame. The walk saw one 80 ms frame at match start where all
        /// 32 rigs were instantiated at once (and the 449 ms start frame carried 14 ms of them). Pooled models are free;
        /// beyond this many new ones a fighter waits a frame (drawn as nothing, never as the capsule), the local player
        /// first. 32 fighters are all built within 6 frames, behind the loading screen in a normal match.
        /// </summary>
        public const int NewModelsPerFrame = 6;
        int _newModelsLeft;

        /// <summary>True when a model for key can be had this frame (one waits in the pool, or the build budget allows one).</summary>
        private bool CanTake(string key)
        {
            if (_pool.TryGetValue(key, out Stack<ModelView> s) && s.Count > 0) return true;
            if (_newModelsLeft <= 0) return false;
            _newModelsLeft--;
            return true;
        }

        private ModelView Take(string key, string objectName)
        {
            if (_pool.TryGetValue(key, out Stack<ModelView> s) && s.Count > 0)
            {
                ModelView m = s.Pop();
                if (!ReferenceEquals(m.AssignedName, objectName))
                {
                    m.Root.name = objectName;
                    m.AssignedName = objectName;
                }
                m.ResetScale();   // fix/model-scale: a reused model starts at its imported size (respawn, class switch)
                return m;
            }
            return Make(key, objectName);
        }

        private void Give(ModelView m)
        {
            m.SetActive(false);
            m.ResetScale();   // fix/model-scale: never park a model mid-effect (death pop, hit punch, squash)
            if (!_pool.TryGetValue(m.Entry.Key, out Stack<ModelView> s)) _pool[m.Entry.Key] = s = new Stack<ModelView>();
            s.Push(m);
        }

        private void LateUpdate() { if (CriticalMass.Runtime.Hud.TitleBackdrop.Frozen) return; var c = CriticalMass.Runtime.Game.FrameCost.Enter(); Sync(Time.deltaTime); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.ActorViews, c); }

        /// <summary>
        /// Contract C3 (DG-71): called once per drawn actor (and the Kaiju, actor -2) after its pose is evaluated and before
        /// render. Spring bones, carried cargo, death launch and weapon trails register an Anim.IPostPoseListener with
        /// Anim.PostPoseHooks and ride the bones from their own files.
        /// </summary>
        public static void PostPose(int actor, ModelView mv, in Anim.ActorPoseState s) => Anim.PostPoseHooks.Dispatch(actor, mv, in s);

        /// <summary>The animator of actor i (tests and the capture report), or null.</summary>
        public Anim.ActorAnimator AnimatorOf(int i) => _actors != null && i >= 0 && i < _actors.Length ? _actors[i].Anim : null;

        /// <summary>The imported model drawn for actor i (tests' pose diagnostics), or null (capsule, dead, out of range).</summary>
        public ModelView ModelOf(int i) => _actors != null && i >= 0 && i < _actors.Length ? _actors[i].Model : null;

        /// <summary>The Kaiju's animator (tests).</summary>
        public Anim.KaijuAnimator KaijuAnimator => _kaijuAnim;

        /// <summary>
        /// One view update (LateUpdate, and the capture tool's frame timing). Clip time advances with SIM time (tick plus
        /// interpolation alpha), not with dt, so frames with the clock stopped hold the pose; dt is unused.
        /// </summary>
        public void Sync(float dt)
        {
            if (_driver == null || _actors == null) return;
            World w = _driver.World;
            GameConfig cfg = _driver.Config;
            MapDef map = w.Map;
            double a = _driver.Alpha;
            double simTime = (w.Tick + a) / cfg.SimHz;
            double dSim = double.IsNaN(_lastSimTime) ? 0 : Math.Max(0, simTime - _lastSimTime);
            _lastSimTime = simTime;
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = UnityEngine.Camera.main;
            _camPos = _cam != null ? _cam.transform.position : Vector3.zero;
            // Contract C4: LOD by distance to the camera target (the local player, whom the camera follows).
            // feat/ground-camera: a low camera follows another body (spectating, knocked out): LOD by distance to it, and it
            // counts as "self" (always LOD0), so the rule holds from that angle too.
            _lodSelf = CriticalMass.Runtime.Camera.GroundView.LodActive ? CriticalMass.Runtime.Camera.GroundView.LodSelf : 0;
            if (CriticalMass.Runtime.Camera.GroundView.LodActive) _targetPos = CriticalMass.Runtime.Camera.GroundView.LodAnchor;
            else if (w.ActorCount > 0 && w.Actors[0].Alive) _targetPos = SimSpace.ToWorld(Lerp(w.PrevX[0], w.X[0], a), 0, Lerp(w.PrevZ[0], w.Z[0], a));
            if (_stressHill) _targetPos = SimSpace.ToWorld(map.Hill.X, 0, map.Hill.Z);
            _lodCut = CrowdCutNow(w, map, a);
            int models = 0;
            _newModelsLeft = NewModelsPerFrame;
            Anim.AnimLod.BeginFrame();
            // feat/follow-camera-1: frustum and distance culling for the low views, and the drawn / culled / triangle counts.
            CriticalMass.Runtime.Camera.ViewCull.Begin(_cam, CriticalMass.Runtime.Camera.GroundView.LodActive);

            for (int i = 0; i < _actors.Length; i++) models += SyncActor(i, w, cfg, map, a, simTime, dSim) ? 1 : 0;
            models += SyncKaiju(w, cfg, map, a, simTime, dSim);
            models += SyncNodes(w, map);
            models += SyncBarricades(w, cfg, map);

            HillDef hill = map.Hill;
            double hh = GroundHeight(map, hill.X, hill.Z);
            float d = (float)(2 * hill.R);
            _hill.localPosition = SimSpace.ToWorld(hill.X, hh + HillThickness / 2.0, hill.Z);
            _hill.localRotation = Quaternion.identity;
            _hill.localScale = new Vector3(d, HillThickness, d);
            ActiveModelCount = models;
        }

        /// <summary>feat/ground-camera: horizontal radius around the low camera inside which other fighters are not drawn.</summary>
        public const float NearLensMetres = 1.6f;
        /// <summary>feat/follow-camera-1: half-width of the camera-to-player line inside which other fighters are not drawn.</summary>
        public const float CorridorMetres = 0.7f;

        private bool NearLens(double x, double h, double z)
        {
            Vector3 p = SimSpace.ToWorld(x, h, z);
            float dx = p.x - _camPos.x, dz = p.z - _camPos.z;
            return dx * dx + dz * dz < NearLensMetres * NearLensMetres && _camPos.y < p.y + 2.6f;
        }

        private bool SyncActor(int i, World w, GameConfig cfg, MapDef map, double a, double simTime, double dSim)
        {
            CharSlot slot = _actors[i];
            if (_fx == null) _fx = GetComponent<Effects.CombatFx>();
            Effects.ActorPoseFx fx = default;
            bool hasFx = _fx != null && _fx.isActiveAndEnabled && i < w.ActorCount && _fx.Pose(i, out fx);
            // A dead actor's body stays for its Death clip and the pop (CombatFx decides how long); otherwise hidden.
            bool alive = i < w.ActorCount && (w.Actors[i].Alive || (hasFx && fx.Corpse));
            Actor actor = alive ? w.Actors[i] : null;
            string key = alive ? ArtMapping.ClassModel(actor.Cls) : null;
            if (key != null && _catalog?.Find(key) == null) key = null;

            if (slot.Model != null && (!alive || slot.Model.Entry.Key != key))
            {
                Give(slot.Model);
                slot.Model = null;
            }
            if (alive && key != null && slot.Model == null)
            {
                if (!CanTake(key))
                {
                    if (slot.Capsule != null && slot.Capsule.gameObject.activeSelf) slot.Capsule.gameObject.SetActive(false);
                    return false;
                }
                slot.Model = Take(key, ActorName(i, key));
                slot.Anim.Reset();
            }
            bool capsule = alive && slot.Model == null;
            if (capsule && slot.Capsule == null)
            {
                GameObject go = CreatePrimitive(PrimitiveType.Capsule, "Actor " + i);
                slot.Capsule = go.transform;
                slot.CapsuleRenderer = go.GetComponent<Renderer>();
            }
            if (slot.Capsule != null && slot.Capsule.gameObject.activeSelf != capsule) slot.Capsule.gameObject.SetActive(capsule);
            if (!alive) return false;

            double x = hasFx && fx.Corpse ? fx.CorpseX : Lerp(w.PrevX[i], w.X[i], a);
            double z = hasFx && fx.Corpse ? fx.CorpseZ : Lerp(w.PrevZ[i], w.Z[i], a);
            if (_stressHill) StressPlace(map.Hill, i, ref x, ref z);
            double h = GroundHeight(map, x, z);
            Quaternion facing = Quaternion.Euler(0f, SimSpace.YawDeg(actor.Facing), 0f);
            if (hasFx) facing = fx.Lean * facing;
            if (hasFx && fx.Holding) dSim = 0; // the fake hit-stop: loops do not advance during the hold
            if (capsule)
            {
                double charHeight = cfg.Render.CharacterHeight;
                slot.Capsule.localPosition = SimSpace.ToWorld(x, h + charHeight / 2, z);
                slot.Capsule.localRotation = facing;
                float d = (float)(2 * cfg.Class(actor.Cls).Radius);
                if (i == 0) d *= HumanScale;
                slot.Capsule.localScale = new Vector3(d, (float)(charHeight / 2), d);
                slot.Capsule.localPosition += JumpLift.Offset(w, map, i, a, x, z); // DG-02 jump arc
                SetMaterial(slot.CapsuleRenderer, ToonMaterials.Character(actor.Team == TeamId.Azure ? Palette.Azure : Palette.Crimson));
                return false;
            }

            ModelView m = slot.Model;
            // feat/ground-camera: from the low camera a fighter standing at the lens fills a third of the frame and hides the
            // shot; bodies within NearLensMetres of the camera (horizontally, other than the followed one) are not drawn.
            if (CriticalMass.Runtime.Camera.GroundView.LodActive && i != _lodSelf && NearLens(x, h, z))
            {
                m.SetActive(false);
                return false;
            }
            if (CriticalMass.Runtime.Camera.GroundView.LodActive)
            {
                // feat/follow-camera-1: first person hides your own body (the lens sits in its head); fighters outside the
                // view or beyond the draw distance are not posed or drawn (their effects skip too). follow-camera-2: a
                // fighter on the line from the camera to you is no longer hidden here: the toon shader dithers it
                // (CMToonFadeClip, set by GroundView), so it reads as a ghost instead of vanishing.
                Vector3 at = SimSpace.ToWorld(x, h, z);
                bool hide = i == 0 && CriticalMass.Runtime.Camera.GroundView.HideSelf;
                bool culled = !hide && i != _lodSelf && !CriticalMass.Runtime.Camera.ViewCull.Visible(at, 1.5f, 2.6f);
                if (hide || culled)
                {
                    if (culled) CriticalMass.Runtime.Camera.ViewCull.FightersCulled++;
                    m.SetActive(false);
                    return false;
                }
            }
            m.SetActive(true);
            m.ApplyTier();
            QualityRow row = QualityTable.Current;
            m.SetLod(AnimLod.PickLod(i == _lodSelf, Vector3.Distance(SimSpace.ToWorld(x, 0, z), _targetPos), _lodCut, row));
            Vector3 basePos = SimSpace.ToWorld(x, h, z);
            m.Transform.localPosition = basePos + (hasFx ? fx.Nudge : Vector3.zero) + JumpLift.Offset(w, map, i, a, x, z); // DG-02 jump arc
            Material teamMat = ArtMaterials.ForTeam(m.Spec, actor.Team);
            m.SetMaterial(hasFx && fx.Flash ? _fx.FlashMaterial(teamMat) : teamMat);
            // Animation pass: clips, timing, facing spring, squash and the level of detail live in Anim.ActorAnimator.
            slot.Anim.Pose(m, w, cfg, i, a, simTime, dSim, basePos, _fx, hasFx, in fx, ref _poseState);
            PostPose(i, m, in _poseState);
            CriticalMass.Runtime.Camera.ViewCull.FightersDrawn++;
            if (CriticalMass.Runtime.Camera.ViewCull.SampleFrame) CriticalMass.Runtime.Camera.ViewCull.FighterTriangles += CriticalMass.Runtime.Camera.ViewCull.Triangles(m.Renderers);
            return true;
        }

        private float _lodCut = float.PositiveInfinity;
        private float[] _lodScratch = new float[64];

        /// <summary>perf-60hz-32p crowd budget: the LOD0 cut distance this frame (the row's Lod0Budget-th nearest fighter
        /// other than your own, by the same drawn position and target SyncActor uses).</summary>
        private float CrowdCutNow(World w, MapDef map, double a)
        {
            int budget = QualityTable.Current.Lod0Budget;
            if (budget < 0) return float.PositiveInfinity;
            if (_lodScratch.Length < w.ActorCount) _lodScratch = new float[w.ActorCount];
            int n = 0;
            for (int i = 1; i < w.ActorCount && i < _actors.Length; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double x = Lerp(w.PrevX[i], w.X[i], a), z = Lerp(w.PrevZ[i], w.Z[i], a);
                if (_stressHill) StressPlace(map.Hill, i, ref x, ref z);
                _lodScratch[n++] = Vector3.Distance(SimSpace.ToWorld(x, 0, z), _targetPos);
            }
            return AnimLod.CrowdCut(_lodScratch, n, budget);
        }

        /// <summary>perf-60hz-32p (-stressHill, automation only): read once; the stress scene draws every fighter at the hill.</summary>
        private static readonly bool _stressHill = AutoplayArgs.Current.Autoplay && AutoplayArgs.Current.StressHill;

        /// <summary>The stress scene's drawn spot (view only): a fighter farther than StressRadius from the hill is drawn on a
        /// ring 2 to 9 m out along its own bearing (spread by actor index), so all 32 stand on screen and keep animating.</summary>
        internal static void StressPlace(HillDef hill, int i, ref double x, ref double z)
        {
            const double StressRadius = 9.0;
            double dx = x - hill.X, dz = z - hill.Z, d = Math.Sqrt(dx * dx + dz * dz);
            if (d <= StressRadius) return;
            double r = 2.0 + 7.0 * ((i * 37) % 32) / 31.0;
            x = hill.X + dx / d * r;
            z = hill.Z + dz / d * r;
        }

        private int SyncKaiju(World w, GameConfig cfg, MapDef map, double a, double simTime, double dSim)
        {
            Kaiju k = w.Kaiju;
            double x = Lerp(k.PrevX, k.X, a);
            double z = Lerp(k.PrevZ, k.Z, a);
            double h = GroundHeight(map, x, z);
            Quaternion facing = Quaternion.Euler(0f, SimSpace.YawDeg(k.Facing), 0f);

            if (k.State == KaijuState.Egg) _sawEgg = true;
            else if (_sawEgg) { _sawEgg = false; _hatchTime = simTime; }
            if (k.State == KaijuState.Berserk && _lastKaijuState != KaijuState.Berserk) _berserkAt = simTime;
            _lastKaijuState = k.State;
            bool telegraph = k.TelegraphIn > 0;
            if (_wasTelegraph && !telegraph && k.KoIn <= 0) _slamAt = simTime;
            _wasTelegraph = telegraph;

            string key = ArtMapping.KaijuModel(k.State, simTime - _hatchTime);
            ModelView m = key == ArtMapping.KaijuEgg ? _egg : key == ArtMapping.KaijuHatchling ? (_hatchling ?? _adult) : _adult;
            if (m == null && key != ArtMapping.KaijuEgg) m = _hatchling;
            SetActive(_egg, m == _egg && m != null);
            SetActive(_hatchling, m == _hatchling && m != null);
            SetActive(_adult, m == _adult && m != null);
            _kaijuCube.gameObject.SetActive(m == null);

            int count = 0;
            if (m == null)
            {
                double kh = cfg.Render.KaijuHeight;
                float d = (float)(2 * cfg.Kaiju.Radius);
                _kaijuCube.localPosition = SimSpace.ToWorld(x, h + kh / 2, z);
                _kaijuCube.localRotation = facing;
                _kaijuCube.localScale = new Vector3(d, (float)kh, d);
                SetMaterial(_kaijuCubeRenderer, ToonMaterials.Character(KaijuHex(k.State), true));
            }
            else
            {
                count++;
                string glow = ArtMapping.KaijuGlow(k.State);
                // The egg rests on the shrine's raised centre cell (a 1.5 u wall block in the grid view), not inside it.
                double baseY = m == _egg ? h + RaisedTop(map, x, z) : h;
                m.Transform.localPosition = SimSpace.ToWorld(x, baseY, z);
                m.SetFacing(facing);
                // Kaiju v4 face swap (art landing): berserk, hurt after a hit or knock-out, angry while it telegraphs, winds
                // up or strikes, sleepy just after the hatch, happy when allied, else neutral with a blink. View only.
                bool kaijuAttacking = telegraph || k.MeleeWindupIn > 0 || simTime - _slamAt < Anim.KaijuAnimator.StrikeSeconds ||
                                      simTime - _kaijuAnim.LastStrikeAt < Anim.KaijuAnimator.StrikeSeconds;
                bool kaijuHurt = k.KoIn > 0 || simTime - _kaijuAnim.LastFlinchAt < ArtMapping.KaijuHurtSeconds;
                KaijuExpression face = ArtMapping.KaijuExpressionFor(k.State, m == _hatchling ? simTime - _hatchTime : double.NegativeInfinity,
                    kaijuAttacking, kaijuHurt, false, simTime);
                m.SetMaterial(ArtMaterials.ForGlow(m.Spec, glow, face));
                double speed = Math.Sqrt((k.X - k.PrevX) * (k.X - k.PrevX) + (k.Z - k.PrevZ) * (k.Z - k.PrevZ)) * cfg.SimHz;
                if (m == _egg)
                {
                    m.Transform.localScale = m.BaseScale;
                    _eggIdleT += dSim;
                    m.BeginPose();
                    bool shake = cfg.HatchSeconds - w.Match.Time < EggShakeSeconds && m.HasClip(ArtMapping.Shake);
                    m.Pose(shake ? ArtMapping.Shake : ArtMapping.Idle, _eggIdleT, 1f, true);
                    m.Evaluate();
                }
                else
                {
                    // Animation pass (DG-92, DG-93): strike, flinch, growth tween, measured stride, footfalls.
                    _kaijuAnim.Pose(m, _hatchling, _adult, w, cfg, facing, simTime, dSim, speed, telegraph, _slamAt, _berserkAt);
                    _poseState = default;
                    _poseState.Actor = -2;
                    _poseState.IsKaiju = true;
                    _poseState.Tick = w.Tick;
                    _poseState.Alpha = a;
                    _poseState.SimTime = simTime;
                    _poseState.DeltaSim = dSim;
                    _poseState.Evaluated = true;
                    _poseState.Speed = (float)speed;
                    _poseState.Yaw = SimSpace.YawDeg(k.Facing);
                    PostPose(-2, m, in _poseState);
                }
            }

            bool shards = _shards != null && m == _adult && k.State == KaijuState.Berserk;
            SetActive(_shards, shards);
            if (shards) _shards.SetMaterial(ArtMaterials.ForGlow(_shards.Spec, ArtMapping.GlowBerserk));

            bool spikes = _spikes != null && k.TelegraphIn > 0 && k.State != KaijuState.Egg;
            SetActive(_spikes, spikes);
            if (spikes)
            {
                // fix/kaiju-alive-1 (nothing-glides audit): the spikes were a static mesh popped in for the whole telegraph.
                // They now push up out of the ground over the wind-up (eased, overshoot near the end), turn slowly, and
                // tremble in the last quarter before the slam lands. View only; the ring on the ground stays the hitbox.
                double tTotal = Effects.Telegraphs.SlamTotalSim(w);
                float u = Mathf.Clamp01(1f - (float)(k.TelegraphIn / tTotal));
                float rise = u < 0.85f ? Mathf.SmoothStep(0f, 1.08f, u / 0.85f) : Mathf.Lerp(1.08f, 1f, (u - 0.85f) / 0.15f);
                float shake = u > 0.75f ? 0.04f * Mathf.Sin((float)(simTime * 70.0)) * (u - 0.75f) * 4f : 0f;
                Vector3 basePos = SimSpace.ToWorld(k.TelegraphX, GroundHeight(map, k.TelegraphX, k.TelegraphZ), k.TelegraphZ);
                _spikes.Transform.localPosition = basePos + new Vector3(shake, -0.6f * (1f - rise), -shake);
                _spikes.Transform.localScale = Vector3.Scale(_spikes.BaseScale, new Vector3(1f, Mathf.Max(0.05f, rise), 1f));
                _spikes.SetFacing(Quaternion.Euler(0f, (float)(simTime * 25.0) % 360f, 0f));
                _spikes.SetMaterial(ArtMaterials.ForGlow(_spikes.Spec, ArtMapping.KaijuGlow(k.State)));
                count++;
            }
            return count;
        }

        private int SyncNodes(World w, MapDef map)
        {
            int count = 0;
            int nodeCount = Math.Min(_nodes.Length, w.Nodes.Length);
            for (int i = 0; i < nodeCount; i++)
            {
                ResourceNode n = w.Nodes[i];
                string key = ArtMapping.NodeModel(n.Kind, n.Charges);
                Dictionary<string, ModelView> variants = _nodeModels[i];
                ModelView shown = null;
                if (key != null && !variants.TryGetValue(key, out shown))
                {
                    shown = Make(key, "Node " + i + " " + key);
                    variants[key] = shown;
                }
                foreach (KeyValuePair<string, ModelView> v in variants) SetActive(v.Value, v.Value != null && v.Value == shown);
                double h = GroundHeight(map, n.X, n.Z);
                bool cube = shown == null && (key != null || _catalog == null);
                Transform t = _nodes[i];
                if (t.gameObject.activeSelf != cube) t.gameObject.SetActive(cube);
                if (shown != null)
                {
                    count++;
                    shown.Transform.localPosition = SimSpace.ToWorld(n.X, h, n.Z);
                    shown.Transform.localRotation = Quaternion.Euler(0f, (i * 137.5f) % 360f, 0f);
                    shown.SetMaterial(ArtMaterials.ForTeam(shown.Spec, TeamId.Azure));
                }
                else if (cube)
                {
                    float sy = 0.3f + 0.1f * n.Charges;
                    t.localPosition = SimSpace.ToWorld(n.X, h + sy / 2.0, n.Z);
                    t.localRotation = Quaternion.identity;
                    t.localScale = new Vector3(NodeSize, sy, NodeSize);
                    SetMaterial(_nodeRenderers[i], ToonMaterials.Prop(n.Kind == NodeKind.Biomass ? Palette.Biomass : Palette.Corruption));
                }
            }
            return count;
        }

        private int SyncBarricades(World w, GameConfig cfg, MapDef map)
        {
            if (_barricades.Length == 0 || _catalog?.Find(ArtMapping.MinerBarricade) == null) return 0;
            int count = 0;
            for (int i = 0; i < _barricades.Length; i++)
            {
                Barricade b = w.Barricades[i];
                bool on = b != null && b.Active;
                if (on && _barricades[i] == null) _barricades[i] = Make(ArtMapping.MinerBarricade, "Barricade " + i);
                ModelView m = _barricades[i];
                if (m == null) continue;
                m.SetActive(on);
                if (!on) { CriticalMass.Runtime.View.WallDamage.BarricadeDamage.Sync(i, m, b, cfg.Abilities.MinerBarricade.MaxHp, false); continue; }
                count++;
                double x = b.Cx + 0.5, z = b.Cz + 0.5;
                m.Transform.localPosition = SimSpace.ToWorld(x, GroundHeight(map, x, z), z);
                m.Transform.localRotation = Quaternion.identity;
                string state = ArtMapping.BarricadeState(b.Hp, cfg.Abilities.MinerBarricade.MaxHp);
                for (int k = 0; k < m.Renderers.Length; k++)
                {
                    Renderer r = m.Renderers[k];
                    bool show = m.RendererNames[k] == state;   // cached names: Object.name allocates on every read
                    if (r != null && r.enabled != show) r.enabled = show;
                }
                m.SetMaterial(ArtMaterials.ForTeam(m.Spec, b.Team));
                // wall-damage-1: damage stages ON the barricade (pieces askew, cracks, lean), the per-hit shake, the break.
                CriticalMass.Runtime.View.WallDamage.BarricadeDamage.Sync(i, m, b, cfg.Abilities.MinerBarricade.MaxHp, true);
            }
            return count;
        }

        /// <summary>"Actor i key", built once per (slot, model) pair so a respawn never formats a string.</summary>
        private string ActorName(int i, string key)
        {
            if (!_names.TryGetValue((i, key), out string n)) _names[(i, key)] = n = "Actor " + i + " " + key;
            return n;
        }

        private static void SetActive(ModelView m, bool on)
        {
            if (m != null) m.SetActive(on);
        }

        /// <summary>Height of the grid view's block above the ground at a sim point (GridChunkMeshView.CellBox: walls and intact breakables 1.5 u).</summary>
        private static double RaisedTop(MapDef map, double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (map.Cells == null || cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return 0;
            byte c = map.Cells[cz * map.Width + cx];
            return c == Cell.Wall || c == Cell.BreakableIntact ? 1.5 : 0;
        }

        private static double Lerp(double from, double to, double t) => from + (to - from) * t;

        /// <summary>Ground height under a sim point, the cell clamped to the map (GroundHeight.At, picking.ts groundHeightAt).</summary>
        private static double GroundHeight(MapDef map, double x, double z) => CriticalMass.Runtime.GroundHeight.At(map, x, z);

        private static string KaijuHex(KaijuState s)
        {
            switch (s)
            {
                case KaijuState.Egg: return Palette.Cream;
                case KaijuState.Neutral: return Palette.Stone;
                case KaijuState.Azure: return Palette.Azure;
                case KaijuState.Crimson: return Palette.Crimson;
                case KaijuState.Berserk: return Palette.Corruption;
                default: return Palette.Stone;
            }
        }

        private static void SetMaterial(Renderer r, Material m)
        {
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }
    }
}
