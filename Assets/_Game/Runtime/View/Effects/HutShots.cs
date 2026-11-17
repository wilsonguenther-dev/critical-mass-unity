using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Capture only (CM b13 item H): `-hutShots [dir]` stages the Blacksmith hut in the live match and writes stills at the
    /// gameplay camera and from a close camera: both teams' huts at levels 0, 1, 3 and 5, the player as a Miner mid-channel at
    /// an ore vein, carrying ore, banking at the hut, and the level-up ring pulse. Each step logs
    /// "[CM.HutShots] &lt;name&gt; REACHED" or "NOT REACHED". Writes the sim (positions, the hut state), so never in a real match:
    /// only with the flag (HutView adds this component to the driver).
    /// </summary>
    public sealed class HutShots : MonoBehaviour
    {
        public const string Flag = "-hutShots";

        SimDriverBehaviour _driver;
        string _dir;
        bool _pin;
        double _pinX, _pinZ, _pinFacing;
        int _pinVein = -1, _pinOre;

        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0;

        static void Log(string s) => Debug.Log("[CM.HutShots] " + s);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) { enabled = false; return; }
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, Flag);
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "hut-shots");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        void OnDestroy() { if (_driver != null) _driver.Ticked -= OnTick; }

        static bool TitleShowing()
        {
            var t = FindAnyObjectByType<CriticalMass.Runtime.Hud.TitleScreen>();
            return t != null && t.isActiveAndEnabled;
        }

        void OnTick(World w)
        {
            if (!_pin) return;
            w.X[0] = _pinX; w.Z[0] = _pinZ; w.PrevX[0] = _pinX; w.PrevZ[0] = _pinZ;
            w.Vx[0] = 0; w.Vz[0] = 0;
            Actor a = w.Actors[0];
            a.Facing = _pinFacing;
            a.Hp = a.MaxHp;
            a.SpawnProtect = 1;
            if (_pinOre > 0) { a.Carry = CarryKind.Ore; a.CarryAmount = _pinOre; }
            if (_pinVein >= 0) { w.Hut.ChannelVein[0] = _pinVein; w.Hut.ChannelTicks[0] = Blacksmith.MineTicks(w.Config) / 2; }
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        void Pin(double x, double z, double facing)
        {
            _pinX = x; _pinZ = z; _pinFacing = facing; _pin = true;
        }

        /// <summary>A still from a close camera looking at (x, z) from the south-east, rendered off screen.</summary>
        void Close(string name, Vector3 at)
        {
            UnityEngine.Camera main = UnityEngine.Camera.main;
            var go = new GameObject("HutShots Close Camera");
            UnityEngine.Camera cam = go.AddComponent<UnityEngine.Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.transform.position = at + new Vector3(3.2f, 3.0f, -4.2f);
            cam.transform.LookAt(at + Vector3.up * 0.8f);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_dir, name + "-close.png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Destroy(rt);
            Destroy(tex);
            Destroy(go);
        }

        IEnumerator Shot(string name, bool reached, Vector3 closeAt)
        {
            yield return Frames(6);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name + ".png"));
            yield return null;
            Close(name, closeAt);
            Log(name + (reached ? " REACHED" : " NOT REACHED"));
        }

        static Vector3 HutAt(World w, int t)
        {
            return CriticalMass.Runtime.View.B13.HutView.HutPosition(w.Map, w.Map.Pads[t]);   // b13 r2: the hut's real spot
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3 || TitleShowing())
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            World w = _driver.World;
            if (!Blacksmith.On(w)) { Log("hut rule off (not the Full profile); NOT REACHED"); Application.Quit(4); yield break; }
            for (int id = 1; id < w.ActorCount; id++)
            {
                Actor b = w.Actors[id];
                b.Alive = false; b.Hp = 0; b.RespawnIn = 1e9; b.Moving = false;
            }
            Actor me = w.Actors[0];
            me.Cls = ClassId.Miner;
            me.MaxHp = Stations.MaxHp(w, me.Team, ClassId.Miner);
            me.Hp = me.MaxHp;
            _driver.Ticked += OnTick;
            int[] levels = { 0, 1, 3, 5 };
            for (int t = 0; t < 2; t++)
            {
                PadDef pad = w.Map.Pads[t];
                Pin(pad.X, pad.Z + 2.0, 0);
                foreach (int L in levels)
                {
                    w.Hut.Level[0] = L; w.Hut.Level[1] = L; w.Hut.Ore[0] = 0; w.Hut.Ore[1] = 0;
                    yield return Shot("hut-" + (TeamId)t + "-L" + L, Blacksmith.Level(w, (TeamId)t) == L, HutAt(w, t));
                }
            }
            w.Hut.Level[0] = 0; w.Hut.Level[1] = 0;
            SourceNode vein = null;
            int vi = -1;
            for (int i = 0; i < w.Depth.Sources.Length; i++) if (w.Depth.Sources[i].Kind == BiomassType.Ore) { vein = w.Depth.Sources[i]; vi = i; break; }
            if (vein != null)
            {
                // b13 r2: stand on walkable ground beside the vein, facing it (round 1 pinned 1 u west of the first vein, which
                // could be the void: the Miner fell, lost the ore and both shots logged NOT REACHED).
                double sx = vein.X - 1.0, sz = vein.Z;
                double rr = w.Config.Class(ClassId.Miner).Radius;
                for (int k = 0; k < 16; k++)
                {
                    double ang = k * Math.PI / 8, d = 1.0 + 0.2 * (k / 8);
                    double px = vein.X + Math.Cos(ang) * d, pz = vein.Z + Math.Sin(ang) * d;
                    if (!Collide.ActorFits(w, px, pz, rr) || !MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(px), JsMath.FloorToInt(pz))) continue;
                    sx = px; sz = pz; break;
                }
                me.Alive = true;
                Pin(sx, sz, Math.Atan2(vein.Z - sz, vein.X - sx));
                Log("miner stand point (" + sx.ToString("0.0") + ", " + sz.ToString("0.0") + ") for vein " + vi + " at (" + vein.X.ToString("0.0") + ", " + vein.Z.ToString("0.0") + ")");
                _pinVein = vi;
                Vector3 va = SimSpace.ToWorld(vein.X, GroundHeight.At(w.Map, vein.X, vein.Z), vein.Z);   // b13 r2: world z = -sim z
                yield return Shot("miner-mining-mid-channel", Blacksmith.ChannelVein(w, 0) == vi, va);
                _pinVein = -1;
                w.Hut.ChannelVein[0] = -1;
                _pinOre = 3;
                yield return Shot("miner-carrying-ore", Blacksmith.OreOf(me) == 3, va);
            }
            else Log("miner-mining-mid-channel NOT REACHED (no ore vein)");
            int team = (int)me.Team;
            PadDef home = w.Map.Pads[team];
            Pin(home.X + 1.0, home.Z, 0);
            _pinOre = 3;
            yield return Frames(4);
            _pinOre = 0;
            int before = w.Hut.Banked[team];
            bool banked = Blacksmith.BankOre(w, 0);
            yield return Shot("miner-banking", banked && w.Hut.Banked[team] == before + 3, HutAt(w, team));
            w.Hut.Ore[team] = Blacksmith.NextCost(w, me.Team);
            int lvl = Blacksmith.LevelWhileStock(w, me.Team, 0);
            yield return new WaitForSecondsRealtime(0.25f);
            yield return Shot("hut-level-up-pulse", lvl >= 1, HutAt(w, team));
            _pin = false;
            _driver.Ticked -= OnTick;
            yield return new WaitForSecondsRealtime(1f);
            Log("done");
            Application.Quit(0);
        }
    }
}
