using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Secrets
{
    /// <summary>
    /// Watches a match for the in-match secrets (DG-26), reading the World only:
    /// egg-03, hold the hill alone for 60 sim seconds: the secret announcer line an-22 (raised for the announcer, shown
    /// as a caption when subtitles are on);
    /// egg-04, an exactly tied allegiance at the hatch: a caption with the hatchling shrugging and the egg's line;
    /// egg-02, the Crown of the Hill, a large brass relic on the Azure overlook ledge, only when the sim can jump
    /// (JumpGate); taking it opens the relic-12 card and puts a small brass glint over your head for the match.
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 55, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]   // installed once by GameBootstrap through the overlay registry
    public sealed class SecretWatcher : MonoBehaviour
    {
        public const string HillAloneLine = "an-22";
        const float CrownScale = 1.5f, GlintScale = 0.45f;

        SimDriverBehaviour _driver;
        World _world;
        int _epoch = -1;
        int _cursor;
        readonly HillAloneTracker _alone = new HillAloneTracker();
        GameObject _crown, _glint;
        RelicPlacement.Spot _crownSpot;
        bool _crownSpawned;

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return;
            World w = _driver.World;
            if (!ReferenceEquals(w, _world) || w.Epoch != _epoch) NewMatch(w);
            if (w.Match.Phase != MatchPhase.Playing) return;

            if (_alone.Step(w))
            {
                SecretsBoard.Fire(SecretsBoard.HillAlone);
                SecretsBoard.Announce(HillAloneLine);
                if (SettingsExtras.Subtitles && CriticalMass.Runtime.Audio.Announcer.Instance == null) CaptionBar.Show(LoreText.Announcer(HillAloneLine), 4f);
            }
            ReadEvents(w);
            UpdateCrown(w);
        }

        void NewMatch(World w)
        {
            _world = w;
            _epoch = w.Epoch;
            _cursor = w.EventHead;
            _alone.Reset();
            SecretsBoard.CrownWorn = false;
            if (_glint != null) _glint.SetActive(false);
            _crownSpawned = false;
            if (_crown != null) _crown.SetActive(false);
            if (JumpGate.Available && !CodexProgress.IsFound(CodexProgress.CrownIndex) && RelicPlacement.TryCrownSpot(w.Map, out _crownSpot))
            {
                if (_crown == null) _crown = RelicViews.Make("Crown Of The Hill", Palette.Brass, transform, CrownScale);
                double x = _crownSpot.X, z = _crownSpot.Z;
                _crown.transform.position = SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 1.0, z);
                _crown.SetActive(true);
                _crownSpawned = true;
            }
        }

        void ReadEvents(World w)
        {
            if (_cursor > w.EventHead) _cursor = w.EventHead;
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                if (e.Type != SimEventType.Hatch) continue;
                if (!SecretRules.IsExactTie((int)e.Value, w.Match)) continue;
                SecretsBoard.Fire(SecretsBoard.ExactTie);
                CaptionBar.Show(LoreText.EggLine(SecretsBoard.ExactTie), 6f);
                Hatchling.Make(CaptionBar.Slot, 0f, 0f, 96f, Hatchling.Pose.Shrug);
            }
        }

        void UpdateCrown(World w)
        {
            const int me = HudBinder.LocalActor;
            if (_crownSpawned && _crown != null && _crown.activeSelf)
            {
                _crown.transform.rotation = Quaternion.Euler(0f, Time.time * 40f, 0f);
                if (w.ActorCount > me && w.Actors[me].Alive)
                {
                    double dx = w.X[me] - _crownSpot.X, dz = w.Z[me] - _crownSpot.Z;
                    if (dx * dx + dz * dz <= RelicField.PickupRadius * RelicField.PickupRadius)
                    {
                        _crown.SetActive(false);
                        SecretsBoard.CrownWorn = true;
                        CodexProgress.Mark(CodexProgress.CrownIndex);
                        LoreCard.ShowRelic(CodexProgress.IdOf(CodexProgress.CrownIndex), "SECRET RELIC", true);
                        SecretsBoard.Fire(SecretsBoard.Crown);
                    }
                }
            }
            if (!SecretsBoard.CrownWorn || w.ActorCount <= me) return;
            if (_glint == null) _glint = RelicViews.Make("Crown Glint", Palette.Brass, transform, GlintScale);
            bool alive = w.Actors[me].Alive;
            if (_glint.activeSelf != alive) _glint.SetActive(alive);
            if (!alive) return;
            double a = _driver.Alpha;
            double x = w.PrevX[me] + (w.X[me] - w.PrevX[me]) * a, z = w.PrevZ[me] + (w.Z[me] - w.PrevZ[me]) * a;
            _glint.transform.SetPositionAndRotation(SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 2.5, z),
                Quaternion.Euler(0f, Time.time * 120f, 0f));
        }

        /// <summary>The hill-alone timer (tests).</summary>
        public HillAloneTracker HillAlone => _alone;

        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        public static SecretWatcher Attach(Scene scene)
        {
            PauseMenu menu = MenuInstaller.Attach(scene);
            if (menu == null) return null;
            SecretWatcher s = menu.GetComponent<SecretWatcher>();
            return s != null ? s : menu.gameObject.AddComponent<SecretWatcher>();
        }
    }
}
