using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Menus.Characters
{
    /// <summary>
    /// b13-v: the -characterShowcaseShots capture (hidden window, like every b13 flag). For each of the six classes it
    /// resets the running match world (seed 1337, 16 v 16, the class on actor 0), stands the fighter at its job (an enemy
    /// in front for the fighters, the nearest resource node for the gatherers, open floor for the builders), then plays a
    /// scripted scene through the real input path (SimDriverBehaviour.ScriptedHumanFrame, one sim tick per frame at 30 Hz)
    /// from the gameplay camera, writing into the data folder: still-&lt;class&gt;-1..4.png (1280x720) at the slide moments and
    /// clip-&lt;class&gt;/0000.png..0239.png (640x360, 30 fps), then "[CM.CharShots] class=&lt;id&gt; stills=4 frames=240 REACHED"
    /// (or NOT REACHED with the reason). The integrator encodes the frames to StreamingAssets/Characters/&lt;class&gt;.webm. Every
    /// scene presses attack, the ability, the F action, interact, one jump and one dodge; what each press does is whatever
    /// the rules on this build allow. It never runs without the flag, and quits the player when done.
    /// </summary>
    public sealed class CharacterShowcaseShots : MonoBehaviour
    {
        public const string Flag = "-characterShowcaseShots";
        /// <summary>The scene tick of each still (the middle of each 2 s slide of the 8 s clip).</summary>
        public static readonly int[] StillTicks = { 30, 90, 150, 210 };

        static int _requested = -1;
        public static bool Requested
        {
            get
            {
                if (_requested < 0) _requested = Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0 ? 1 : 0;
                return _requested == 1;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested) return;
            SceneManager.sceneLoaded -= OnScene;
            SceneManager.sceneLoaded += OnScene;
            Attach();
        }

        static void OnScene(Scene s, LoadSceneMode m) => Attach();

        static void Attach()
        {
            SimDriverBehaviour d = FindAnyObjectByType<SimDriverBehaviour>();
            if (d != null && d.GetComponent<CharacterShowcaseShots>() == null) d.gameObject.AddComponent<CharacterShowcaseShots>();
        }

        SimDriverBehaviour _driver;
        RenderTexture _clipRt, _stillRt;
        Texture2D _clipTex, _stillTex;
        readonly InputFrame _frame = SimInput.EmptyInput();

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[CM.CharShots] " + s);

        /// <summary>The scripted press for a class on scene tick f (pure; tests). Moves toward facing for the first half
        /// second, then: attack every 20 ticks in 30..90, the ability at 95, the F action every 15 ticks in 110..160 (the
        /// Builder switches piece at 125), interact held in 40..80 and 170..185, a jump at 190, a dodge at 215.</summary>
        public static void Script(ClassId cls, int f, double facing, double x, double z, InputFrame o)
        {
            o.MoveX = o.MoveZ = 0;
            o.Attack = o.Ability = o.Interact = o.Jump = o.Terraform = o.TerraformSwitch = o.Dodge = false;
            double cx = Math.Cos(facing), cz = Math.Sin(facing);
            o.AimX = x + cx * 3.0;
            o.AimZ = z + cz * 3.0;
            if (f < 15 || (f >= 160 && f < 170) || (f >= 200 && f < 214)) { o.MoveX = cx; o.MoveZ = cz; }
            if (f >= 30 && f < 90 && (f - 30) % 20 == 0) o.Attack = true;
            if (f == 95) o.Ability = true;
            if (f >= 110 && f < 160 && (f - 110) % 15 == 0) o.Terraform = true;
            if (cls == ClassId.Builder && f == 125) { o.Terraform = false; o.TerraformSwitch = true; }
            bool gatherer = cls == ClassId.Farmer || cls == ClassId.Miner || cls == ClassId.Healer;
            if (gatherer && ((f >= 40 && f < 80) || (f >= 170 && f < 186))) { o.Interact = true; if (f < 80) o.Attack = false; }
            if (f == 190) o.Jump = true;
            if (f == 215) { o.Dodge = true; o.MoveX = -cz; o.MoveZ = cx; }
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            _clipRt = new RenderTexture(CharacterShowcaseData.ClipW, CharacterShowcaseData.ClipH, 0, RenderTextureFormat.ARGB32);
            _stillRt = new RenderTexture(CharacterShowcaseData.StillW, CharacterShowcaseData.StillH, 0, RenderTextureFormat.ARGB32);
            _clipTex = new Texture2D(CharacterShowcaseData.ClipW, CharacterShowcaseData.ClipH, TextureFormat.RGB24, false);
            _stillTex = new Texture2D(CharacterShowcaseData.StillW, CharacterShowcaseData.StillH, TextureFormat.RGB24, false);
            double scale = _driver.TimeScale;
            for (int i = 0; i < CharacterShowcaseData.All.Length; i++) yield return Scene(CharacterShowcaseData.All[i]);
            SimDriverBehaviour.ScriptedHumanFrame = null;
            _driver.TimeScale = scale;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        IEnumerator Scene(ShowcaseClass k)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = k.Id, Config = _driver.Config });
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            _driver.StepTicks(3);
            string why = StandAtJob(w, k.Id);
            string dir = Path.Combine(GamePaths.DataDir, CharacterShowcaseData.ClipFolderName(k.FileId));
            try { Directory.CreateDirectory(dir); } catch (Exception e) { why = "no clip folder: " + e.Message; }
            yield return new WaitForSecondsRealtime(0.6f);   // the camera settles on the new spot
            int stills = 0, frames = 0;
            SimDriverBehaviour.ScriptedHumanFrame = _frame;
            for (int f = 0; f < CharacterShowcaseData.ClipFrames; f++)
            {
                Script(k.Id, f, w.Actors[0].Facing, w.X[0], w.Z[0], _frame);
                _driver.StepTicks(1);
                yield return new WaitForEndOfFrame();
                Texture2D screen = ScreenCapture.CaptureScreenshotAsTexture();
                if (screen == null) continue;
                if (Write(screen, _clipRt, _clipTex, Path.Combine(dir, CharacterShowcaseData.FrameName(f)))) frames++;
                int s = Array.IndexOf(StillTicks, f);
                if (s >= 0 && Write(screen, _stillRt, _stillTex, Path.Combine(GamePaths.DataDir, CharacterShowcaseData.StillName(k.FileId, s + 1)))) stills++;
                Destroy(screen);
            }
            SimDriverBehaviour.ScriptedHumanFrame = null;
            bool reached = why == null && stills == CharacterShowcaseData.SlideCount && frames == CharacterShowcaseData.ClipFrames;
            Log("class=" + k.FileId + " stills=" + stills + " frames=" + frames + (reached ? " REACHED" : " NOT REACHED " + (why ?? "missing files")));
        }

        static bool Write(Texture2D screen, RenderTexture rt, Texture2D into, string path)
        {
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(screen, rt);
            RenderTexture.active = rt;
            into.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            into.Apply(false);
            RenderTexture.active = prev;
            try { File.WriteAllBytes(path, into.EncodeToPNG()); return true; }
            catch (Exception e) { Log("write failed " + path + ": " + e.Message); return false; }
        }

        /// <summary>Stands actor 0 at its class's job: an enemy (fighters) or an ally (Healer) 1.2 u in front, the nearest
        /// resource node (Farmer, Miner), or open floor (Demolitionist, Builder). Returns null, or why it could not.</summary>
        static string StandAtJob(World w, ClassId cls)
        {
            Actor me = w.Actors[0];
            me.SpawnProtect = 0;
            if (cls == ClassId.Farmer || cls == ClassId.Miner)
            {
                ResourceNode best = null;
                double bd = double.MaxValue;
                if (w.Nodes != null)
                    foreach (ResourceNode n in w.Nodes)
                    {
                        if (n == null || n.Charges <= 0) continue;
                        double dx = n.X - w.X[0], dz = n.Z - w.Z[0], d = dx * dx + dz * dz;
                        if (d < bd) { bd = d; best = n; }
                    }
                if (best == null) return "no resource node";
                Put(w, 0, best.X - 1.0, best.Z, 0);
                return null;
            }
            if (cls == ClassId.Warrior || cls == ClassId.Healer)
            {
                int other = -1;
                for (int i = 1; i < w.ActorCount && other < 0; i++)
                    if (w.Actors[i].Alive && (cls == ClassId.Healer ? w.Actors[i].Team == me.Team : w.Actors[i].Team != me.Team)) other = i;
                if (other < 0) return "nobody to stand by";
                Put(w, 0, w.X[other] - 1.2, w.Z[other], 0);
                return null;
            }
            return null;   // Demolitionist and Builder work from their spawn's open floor
        }

        static void Put(World w, int id, double x, double z, double facing)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
            w.Actors[id].Facing = facing;
        }

        void OnDestroy()
        {
            if (SimDriverBehaviour.ScriptedHumanFrame == _frame) SimDriverBehaviour.ScriptedHumanFrame = null;
            if (_clipRt != null) { _clipRt.Release(); Destroy(_clipRt); }
            if (_stillRt != null) { _stillRt.Release(); Destroy(_stillRt); }
            if (_clipTex != null) Destroy(_clipTex);
            if (_stillTex != null) Destroy(_stillTex);
        }
    }
}
