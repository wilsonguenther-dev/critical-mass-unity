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
    /// b13 r3 ART framing: each hut still pins the player 2.4 u in front of the hut toward the pad (the gameplay camera then
    /// holds the hut in the middle of its base with ground around it) and waits out the level-up effect so the level reads
    /// clean; close cameras are sized from the subject (radius / sin(fov / 2)) instead of a fixed offset that cut the roof
    /// off: the hut from its forge side, mining from the side of the Miner-vein line, carrying from the Miner's front with
    /// him turned away from the vein (the ore heap is at his hands).
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

        /// <summary>A close camera framing: look at At from direction From, far enough to hold a sphere of Radius.</summary>
        struct Frame
        {
            public Vector3 At, From;
            public float Radius;
            public Frame(Vector3 at, float radius, Vector3 from) { At = at; Radius = radius; From = from.normalized; }
        }

        /// <summary>Direction on the ground plane turned by yawDeg, lifted by elevDeg (unit).</summary>
        static Vector3 Dir(Vector3 flat, float yawDeg, float elevDeg)
        {
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.back;
            Vector3 f = Quaternion.AngleAxis(yawDeg, Vector3.up) * flat.normalized;
            return f * Mathf.Cos(elevDeg * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevDeg * Mathf.Deg2Rad);
        }

        /// <summary>A still from a close camera (Frame), rendered off screen.</summary>
        void Close(string name, Frame fr)
        {
            UnityEngine.Camera main = UnityEngine.Camera.main;
            var go = new GameObject("HutShots Close Camera");
            UnityEngine.Camera cam = go.AddComponent<UnityEngine.Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.orthographic = false;
            if (cam.fieldOfView < 20f || cam.fieldOfView > 70f) cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.05f;
            float dist = fr.Radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.transform.position = fr.At + fr.From * dist;
            cam.transform.LookAt(fr.At);
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

        IEnumerator Shot(string name, bool reached, Frame close)
        {
            yield return Frames(6);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name + ".png"));
            yield return null;
            Close(name, close);
            Log(name + (reached ? " REACHED" : " NOT REACHED"));
        }

        static Vector3 HutAt(World w, int t)
        {
            return CriticalMass.Runtime.View.B13.HutView.HutPosition(w.Map, w.Map.Pads[t]);   // b13 r2: the hut's real spot
        }

        /// <summary>The hut (level 5 with its chimney flame is 4.2 m tall, 3.6 m across) from its forge side.</summary>
        static Frame HutFrame(World w, int t)
        {
            Vector3 hp = HutAt(w, t);
            PadDef pad = w.Map.Pads[t];
            Vector3 toPad = SimSpace.ToWorld(pad.X, hp.y, pad.Z) - hp;
            return new Frame(hp + Vector3.up * 1.9f, 2.9f, Dir(toPad, 38f, 27f));
        }

        /// <summary>Sim spot 2.4 u in front of team t's hut toward its pad (the player stands here for the hut stills).</summary>
        static void FrontOfHut(World w, int t, out double x, out double z, out double facing)
        {
            Vector3 hp = HutAt(w, t);
            PadDef pad = w.Map.Pads[t];
            double hx = hp.x, hz = -hp.z;                           // world z = -sim z
            double dx = pad.X - hx, dz = pad.Z - hz, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) { dx = 1; dz = 0; len = 1; }
            x = hx + dx / len * 2.4;
            z = hz + dz / len * 2.4;
            facing = Math.Atan2(hz - z, hx - x);                    // looking at the hut
        }

        static Vector3 FacingWorld(double facing) => Quaternion.Euler(0f, SimSpace.YawDeg(facing), 0f) * Vector3.forward;

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
                FrontOfHut(w, t, out double fx, out double fz, out double ff);
                Pin(fx, fz, ff);
                foreach (int L in levels)
                {
                    w.Hut.Level[0] = L; w.Hut.Level[1] = L; w.Hut.Ore[0] = 0; w.Hut.Ore[1] = 0;
                    // b13 r3: let the level-up ring and sparks finish, so the still shows the hut's level stage alone.
                    yield return new WaitForSecondsRealtime(CriticalMass.Runtime.View.B13.HutView.SparkSeconds + 0.15f);
                    yield return Shot("hut-" + (TeamId)t + "-L" + L, Blacksmith.Level(w, (TeamId)t) == L, HutFrame(w, t));
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
                Vector3 ma = SimSpace.ToWorld(sx, GroundHeight.At(w.Map, sx, sz), sz);
                // b13 r3: mining seen from the side of the Miner-vein line, both in frame.
                yield return Shot("miner-mining-mid-channel", Blacksmith.ChannelVein(w, 0) == vi,
                    new Frame((ma + va) * 0.5f + Vector3.up * 0.7f, 1.9f, Dir(va - ma, 90f, 30f)));
                _pinVein = -1;
                w.Hut.ChannelVein[0] = -1;
                _pinOre = 3;
                // b13 r3: carrying turned away from the vein (heading home), seen from his front so the ore heap at his hands shows.
                double away = Math.Atan2(sz - vein.Z, sx - vein.X);
                Pin(sx, sz, away);
                yield return Shot("miner-carrying-ore", Blacksmith.OreOf(me) == 3,
                    new Frame(ma + Vector3.up * 0.95f, 1.25f, Dir(FacingWorld(away), 30f, 22f)));
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
            yield return Shot("miner-banking", banked && w.Hut.Banked[team] == before + 3, HutFrame(w, team));
            w.Hut.Ore[team] = Blacksmith.NextCost(w, me.Team);
            int lvl = Blacksmith.LevelWhileStock(w, me.Team, 0);
            yield return new WaitForSecondsRealtime(0.25f);
            yield return Shot("hut-level-up-pulse", lvl >= 1, new Frame(HutAt(w, team) + Vector3.up * 1.4f, 3.2f, HutFrame(w, team).From));
            _pin = false;
            _driver.Ticked -= OnTick;
            yield return new WaitForSecondsRealtime(1f);
            Log("done");
            Application.Quit(0);
        }
    }
}
