// Critic audit 2026-11-21 probe (Editor Play mode only; lives OUTSIDE Assets/, never compiled into the game).
// Reads the live match; for latency it adds a VIRTUAL gamepad / keyboard to the Input System (no OS keystrokes, no clicks)
// and stages actor 0 only for captures. Run: ~/cm-editor-eyes/bin/u 200 script run -- --file <this> --entry Critic.<M> --args '[...]'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class Critic
{
    static SimDriverBehaviour D => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
    static void Mute() { UnityEditor.EditorUtility.audioMasterMute = true; AudioListener.volume = 0f; }
    static InputFrame HumanFrame(SimDriverBehaviour d) =>
        (InputFrame)typeof(SimDriverBehaviour).GetField("_humanFrame", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(d);

    static Gamepad Pad()
    {
        foreach (var dev in InputSystem.devices) if (dev is Gamepad g && dev.name.StartsWith("CriticPad")) return g;
        return InputSystem.AddDevice<Gamepad>("CriticPad");
    }
    public static string RemoveDevices()
    {
        int n = 0;
        foreach (var dev in InputSystem.devices.ToArray()) if (dev.name.StartsWith("CriticPad") || dev.name.StartsWith("CriticKbd")) { InputSystem.RemoveDevice(dev); n++; }
        return "removed " + n;
    }

    class Rec { public int pressTick = -1, frameTick = -1, simTick = -1, frameF = -1, simF = -1, viewF = -1; public double frameMs = -1, simMs = -1, viewMs = -1; }

    /// <summary>One press through the virtual gamepad. kind: attack ability jump dodge interact dig switch move stop turn aim</summary>
    static async Task<string> One(string kind, Gamepad pad, StringBuilder csv)
    {
        var d = D; World w = d.World; int me = LocalSeat.Actor; var frame = HumanFrame(d);
        // neutral, wait for readiness (cooldowns, ground, alive)
        InputSystem.QueueStateEvent(pad, new GamepadState());
        for (int i = 0; i < 240; i++)
        {
            await Task.Yield();
            Actor a0 = w.Actors[me];
            bool ready = a0.Alive && a0.StandUp <= 0 && a0.AttackCd <= 0 && a0.AbilityCd <= 0 && a0.AirLen == 0 && !Dodge.Active(w, me) && Dodge.CooldownTicks(w, me) == 0;
            if (ready && i > 10) break;
        }
        if (kind == "stop" || kind == "turn")
        {   // hold a move for 1 s first
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(1, 0) });
            for (int i = 0; i < 70; i++) await Task.Yield();
        }
        Actor a = w.Actors[me];
        double f0 = a.Facing, ab0 = a.AbilityCd, act0 = a.AbilityActive; int grid0 = w.GridVersion; int ev0 = w.EventHead;
        var tb = BuilderToolbelt.Local; int tool0 = tb != null ? (int)tb.Selected : -1;
        double rx0 = w.PrevX[me] + (w.X[me] - w.PrevX[me]) * d.Alpha, rz0 = w.PrevZ[me] + (w.Z[me] - w.PrevZ[me]) * d.Alpha;
        var r = new Rec(); double t0 = Time.realtimeSinceStartupAsDouble; int fr0 = Time.frameCount; int tick0 = w.Tick;
        var st = new GamepadState();
        switch (kind)
        {
            case "attack": st.rightTrigger = 1; break;
            case "ability": st.leftTrigger = 1; break;
            case "jump": st = st.WithButton(GamepadButton.South); break;
            case "dodge": st = st.WithButton(GamepadButton.RightShoulder); st.leftStick = new Vector2(0, 1); break;
            case "interact": st = st.WithButton(GamepadButton.West); break;
            case "dig": st = st.WithButton(GamepadButton.North); break;
            case "switch": st = st.WithButton(GamepadButton.LeftShoulder); break;
            case "move": st.leftStick = new Vector2(1, 0); break;
            case "stop": break;
            case "turn": st.leftStick = new Vector2(-1, 0); break;
            case "aim": st.rightStick = new Vector2(0, -1); break;
        }
        Action<World> h = ww =>
        {
            int dt = ww.Tick - tick0;
            bool inFrame = kind switch
            {
                "attack" => frame.Attack, "ability" => frame.Ability, "jump" => frame.Jump, "dodge" => frame.Dodge, "interact" => frame.Interact,
                "dig" => frame.Terraform, "switch" => frame.TerraformSwitch, "move" => (frame.MoveX * frame.MoveX + frame.MoveZ * frame.MoveZ) > 0.01,
                "stop" => (frame.MoveX * frame.MoveX + frame.MoveZ * frame.MoveZ) < 0.01, "turn" => true, "aim" => !double.IsNaN(frame.AimX), _ => false
            };
            if (inFrame && r.frameTick < 0) { r.frameTick = dt; r.frameMs = (Time.realtimeSinceStartupAsDouble - t0) * 1000; r.frameF = Time.frameCount - fr0; }
            Actor b = ww.Actors[me];
            double vx = ww.X[me] - ww.PrevX[me], vz = ww.Z[me] - ww.PrevZ[me]; double sp = Math.Sqrt(vx * vx + vz * vz) * 30;
            double df = Math.Abs(Math.IEEERemainder(b.Facing - f0, 2 * Math.PI)) * 180 / Math.PI;
            bool evt = false;
            for (int e = ev0; e < ww.EventHead; e++) { var ev = ww.Events[e % World.EventCapacity]; if (ev.A == me && (ev.Type == SimEventType.AttackStart || ev.Type == SimEventType.Ability || ev.Type == SimEventType.Terraform || ev.Type == SimEventType.TntThrown || ev.Type == SimEventType.PiecePlaced || ev.Type == SimEventType.BarricadePlaced || ev.Type == SimEventType.MinePlaced)) evt = true; }
            bool sim = kind switch
            {
                "attack" => evt || b.SwingPending || b.AttackCd > 0, "ability" => evt || b.AbilityCd > ab0 + 1e-6 || b.AbilityActive > act0 + 1e-6,
                "jump" => b.AirLen > 0, "dodge" => Dodge.Active(ww, me), "dig" => evt || ww.GridVersion != grid0, "interact" => evt,
                "move" => sp > 0.05, "stop" => sp < 0.05, "turn" => df > 150, "aim" => df > 30, "switch" => false, _ => false
            };
            if (sim && r.simTick < 0) { r.simTick = dt; r.simMs = (Time.realtimeSinceStartupAsDouble - t0) * 1000; r.simF = Time.frameCount - fr0; }
        };
        d.Ticked += h;
        InputSystem.QueueStateEvent(pad, st);
        int hold = (kind == "move" || kind == "turn" || kind == "aim") ? 50 : 4;
        for (int f = 0; f < 70; f++)
        {
            await Task.Yield();
            if (f == hold && kind != "stop") InputSystem.QueueStateEvent(pad, new GamepadState());
            // view: rendered body moved (move/stop) or the tool wheel changed (switch, view only)
            double rx = w.PrevX[me] + (w.X[me] - w.PrevX[me]) * d.Alpha, rz = w.PrevZ[me] + (w.Z[me] - w.PrevZ[me]) * d.Alpha;
            double moved = Math.Sqrt((rx - rx0) * (rx - rx0) + (rz - rz0) * (rz - rz0));
            bool view = kind switch { "move" => moved > 0.01, "switch" => tb != null && (int)tb.Selected != tool0, _ => false };
            if (kind == "stop") { view = r.simTick >= 0; }
            if (view && r.viewF < 0) { r.viewF = Time.frameCount - fr0; r.viewMs = (Time.realtimeSinceStartupAsDouble - t0) * 1000; }
            rx0 = kind == "stop" ? rx : rx0; rz0 = kind == "stop" ? rz : rz0;
        }
        d.Ticked -= h;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        string line = kind + "," + w.Actors[me].Cls + "," + r.frameTick + "," + r.frameMs.ToString("F1") + "," + r.frameF + "," + r.simTick + "," + r.simMs.ToString("F1") + "," + r.simF + "," + r.viewF + "," + r.viewMs.ToString("F1");
        csv.AppendLine(line);
        return line;
    }

    public static async Task<string> PadLatency(string outCsv, int reps)
    {
        Mute(); var d = D; d.HumanAutopilot = false; HumanControlGuard.IdleAutopilotEnabled = false; d.TimeScale = 1; AutoplayArgs.ClockHeld = false;
        var pad = Pad();
        var csv = new StringBuilder();
        bool header = !File.Exists(outCsv);
        if (header) csv.AppendLine("kind,class,ticks_to_sim_input,ms_to_sim_input,frames_to_sim_input,ticks_to_sim_response,ms_to_sim_response,frames_to_sim_response,frames_to_view,ms_to_view");
        string[] kinds = { "move", "stop", "turn", "aim", "attack", "ability", "jump", "dodge", "interact", "dig", "switch" };
        float fps0 = Time.frameCount; double tt0 = Time.realtimeSinceStartupAsDouble;
        for (int k = 0; k < reps; k++) foreach (string kind in kinds) await One(kind, pad, csv);
        double fps = (Time.frameCount - fps0) / (Time.realtimeSinceStartupAsDouble - tt0);
        csv.AppendLine("# editor fps " + fps.ToString("F1") + " simHz 30 pace " + d.TimeScale + " behaviour " + InputSystem.settings.editorInputBehaviorInPlayMode + " bg " + InputSystem.settings.backgroundBehavior);
        File.AppendAllText(outCsv, csv.ToString());
        return "fps " + fps.ToString("F1") + " rows " + (reps * kinds.Length);
    }

    /// <summary>Aim model error: for a grid of screen pixels, the game's aim point (plane at actor 0's ground height) vs the
    /// true ground point under the cursor (ray-marched on GroundHeight.At). Reports error in u and in degrees from the fighter.</summary>
    public static string AimError(string outCsv, int step)
    {
        var d = D; World w = d.World; var cam = Camera.main; int me = LocalSeat.Actor;
        var sb = new StringBuilder("px,py,aimX,aimZ,trueX,trueZ,trueH,myH,errU,errDeg\n");
        double myH = GroundHeight.At(w.Map, w.X[me], w.Z[me]);
        var errs = new System.Collections.Generic.List<double>(); var degs = new System.Collections.Generic.List<double>(); int hiErr = 0;
        for (int py = step / 2; py < cam.pixelHeight; py += step)
            for (int px = step / 2; px < cam.pixelWidth; px += step)
            {
                Ray ray = cam.ScreenPointToRay(new Vector3(px, py, 0));
                if (!GroundHeight.TryAimPoint(ray, w.Map, w.X[me], w.Z[me], out double ax, out double az)) continue;
                // march
                double tx = double.NaN, tz = double.NaN, th = 0;
                for (float t = 1f; t < 120f; t += 0.05f)
                {
                    Vector3 p = ray.origin + ray.direction * t; Vector3 s = p; double sx = s.x, sz = -s.z; // ToSim: z negated
                    double gh = GroundHeight.At(w.Map, sx, sz);
                    if (p.y <= gh) { tx = sx; tz = sz; th = gh; break; }
                }
                if (double.IsNaN(tx)) continue;
                double eu = Math.Sqrt((ax - tx) * (ax - tx) + (az - tz) * (az - tz));
                double a1 = Math.Atan2(az - w.Z[me], ax - w.X[me]), a2 = Math.Atan2(tz - w.Z[me], tx - w.X[me]);
                double ed = Math.Abs(Math.IEEERemainder(a1 - a2, 2 * Math.PI)) * 180 / Math.PI;
                errs.Add(eu); degs.Add(ed); if (ed > 10) hiErr++;
                sb.Append(px).Append(',').Append(py).Append(',').Append(ax.ToString("F2")).Append(',').Append(az.ToString("F2")).Append(',').Append(tx.ToString("F2")).Append(',').Append(tz.ToString("F2")).Append(',').Append(th.ToString("F2")).Append(',').Append(myH.ToString("F2")).Append(',').Append(eu.ToString("F2")).Append(',').Append(ed.ToString("F1")).Append('\n');
            }
        File.WriteAllText(outCsv, sb.ToString());
        errs.Sort(); degs.Sort();
        Func<System.Collections.Generic.List<double>, double, double> q = (l, p) => l.Count == 0 ? -1 : l[Math.Min(l.Count - 1, (int)(p * l.Count))];
        return "pts " + errs.Count + " myH " + myH.ToString("F2") + " errU p50 " + q(errs, .5).ToString("F2") + " p90 " + q(errs, .9).ToString("F2") + " max " + q(errs, 1).ToString("F2") +
               " errDeg p50 " + q(degs, .5).ToString("F1") + " p90 " + q(degs, .9).ToString("F1") + " max " + q(degs, 1).ToString("F1") + " share>10deg " + (hiErr / (double)Math.Max(1, degs.Count)).ToString("F3") +
               " at " + w.X[me].ToString("F1") + "," + w.Z[me].ToString("F1");
    }

    public static string Put(double x, double z, double facing)
    {
        var d = D; World w = d.World; int me = LocalSeat.Actor;
        w.X[me] = x; w.Z[me] = z; w.PrevX[me] = x; w.PrevZ[me] = z; w.Vx[me] = 0; w.Vz[me] = 0; w.Actors[me].Facing = facing;
        return "put " + x + "," + z + " h " + GroundHeight.At(w.Map, x, z).ToString("F2");
    }

    /// <summary>Surface census: cells by type and lift, written as one line (for island-change comparisons over time).</summary>
    public static string Census(string outPath, string tag)
    {
        var d = D; World w = d.World; var m = w.Map; int W = m.Width, H = m.Height;
        var sb = new StringBuilder();
        var types = new int[256]; int lifted = 0;
        var cells = (byte[])m.Cells.Clone(); foreach (byte c in cells) types[c]++;
        var lifts = m.Lift != null ? m.Lift.Select(v => (int)v).ToArray() : new int[W * H];
        foreach (int v in lifts) if (v != 0) lifted++;
        File.WriteAllBytes(outPath + "-" + tag + ".cells", cells);
        File.WriteAllText(outPath + "-" + tag + ".lift", string.Join(",", lifts));
        for (int i = 0; i < 256; i++) if (types[i] > 0) sb.Append(i).Append(':').Append(types[i]).Append(' ');
        return tag + " t " + ((int)w.Match.Time) + " grid " + W + "x" + H + " v" + w.GridVersion + " lifted " + lifted + " types " + sb;
    }
    /// <summary>Every visible UI Text: size in px at 1080p, cut (fewer glyphs drawn than the string), contrast against the
    /// nearest opaque Graphic behind it (Image colour; approximate). Appends rows to outCsv with the tag; returns a summary.</summary>
    public static string UiScan(string outCsv, string tag)
    {
        var sb = new StringBuilder(); int n = 0, tiny = 0, cut = 0, lowc = 0; var tinyList = new System.Collections.Generic.List<string>();
        foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
        {
            if (!t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text) || t.color.a < 0.05f) continue;
            var cv = t.canvas; if (cv == null) continue;
            var cg = t.GetComponentsInParent<CanvasGroup>(); float alpha = 1; foreach (var g in cg) alpha *= g.alpha; if (alpha < 0.05f) continue;
            var rc = cv.rootCanvas; var corners = new Vector3[4]; t.rectTransform.GetWorldCorners(corners);
            Camera ccam = rc.renderMode == RenderMode.ScreenSpaceOverlay ? null : (rc.worldCamera != null ? rc.worldCamera : Camera.main);
            Vector3 c0 = ccam == null ? corners[0] : ccam.WorldToScreenPoint(corners[0]), c1 = ccam == null ? corners[1] : ccam.WorldToScreenPoint(corners[1]);
            float hScreen = Vector2.Distance(c0, c1); float rectH = Mathf.Max(1e-3f, t.rectTransform.rect.height);
            int fs = t.resizeTextForBestFit && t.cachedTextGenerator.fontSizeUsedForBestFit > 0 ? t.cachedTextGenerator.fontSizeUsedForBestFit : t.fontSize;
            float px = fs * (hScreen / rectH) * 1080f / Mathf.Max(1, Screen.height);
            string space = rc.renderMode.ToString();
            int vis = t.cachedTextGenerator.characterCountVisible; string plain = System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "");
            bool isCut = t.verticalOverflow == VerticalWrapMode.Truncate && vis > 0 && vis < plain.Replace("\n", "").Replace(" ", "").Length - 1 && vis < plain.Length - 1;
            // background: first Image up the hierarchy (or a sibling drawn before it) with alpha > 0.5
            Color bg = new Color(0.1f, 0.1f, 0.1f); bool haveBg = false; Transform tr = t.transform;
            for (int up = 0; up < 6 && tr != null && !haveBg; up++, tr = tr.parent)
                foreach (var im in tr.GetComponents<UnityEngine.UI.Image>()) if (im.enabled && im.color.a > 0.5f) { bg = im.color; haveBg = true; }
            Func<Color, double> L = c => { Func<double, double> f = v => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
            double l1 = L(t.color), l2 = L(bg); double cr = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
            n++; if (px < 12) { tiny++; if (tinyList.Count < 12) tinyList.Add(space.Substring(0,5) + ":" + t.name + "=" + px.ToString("F0") + "px'" + plain.Substring(0, Math.Min(18, plain.Length)).Replace("\n", " ") + "'"); }
            if (isCut) cut++; if (haveBg && cr < 3) lowc++;
            sb.Append(tag).Append(',').Append(space).Append(',').Append(t.name.Replace(',', ' ')).Append(',').Append(px.ToString("F1")).Append(',').Append(vis).Append(',').Append(plain.Length).Append(',').Append(isCut ? 1 : 0).Append(',')
              .Append(haveBg ? cr.ToString("F2") : "").Append(',').Append('"').Append(plain.Replace("\"", "'").Replace("\n", " ").Substring(0, Math.Min(60, plain.Length))).Append('"').Append('\n');
        }
        if (!File.Exists(outCsv)) File.WriteAllText(outCsv, "screen,space,name,px1080,visible_chars,chars,cut,contrast_vs_panel,text\n");
        File.AppendAllText(outCsv, sb.ToString());
        return tag + ": texts " + n + " tiny<12px " + tiny + " cut " + cut + " lowContrast<3 " + lowc + " screenH " + Screen.height + " :: " + string.Join("; ", tinyList);
    }
    static int PurgeTicked()
    {
        var d = D; var f = typeof(SimDriverBehaviour).GetField("Ticked", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) return -1; var del = f.GetValue(d) as Delegate; if (del == null) return 0; int n = 0;
        foreach (var x in del.GetInvocationList()) if (x.Method.DeclaringType != null && x.Method.DeclaringType.FullName.Contains("Critic")) { d.Ticked -= (Action<World>)x; n++; }
        return n;
    }
    public static string Unhook() => "purged " + PurgeTicked();

    /// <summary>Position heatmap of living fighters, sampled every 15 ticks, plus blast log (Explosion events: tick, x, z).
    /// Rewrites outPath.heat (W*H ints) and outPath.blasts every 300 ticks until the match ends.</summary>
    public static string Heat(string outPath)
    {
        PurgeTicked(); var d = D; World w0 = d.World; int W = w0.Map.Width, H = w0.Map.Height;
        var heat = new int[W * H]; var blasts = new StringBuilder(); int head = w0.EventHead;
        Action<World> h = null;
        h = w =>
        {
            for (; head < w.EventHead; head++) { var ev = w.Events[head % World.EventCapacity]; if (ev.Type == SimEventType.Explosion || ev.Type == SimEventType.TntLanded || ev.Type == SimEventType.MineTriggered) blasts.Append(w.Tick).Append(',').Append(ev.Type).Append(',').Append(ev.X.ToString("F2")).Append(',').Append(ev.Z.ToString("F2")).Append('\n'); }
            if (w.Tick % 15 == 0)
                for (int i = 0; i < w.ActorCount; i++) { if (!w.Actors[i].Alive) continue; int cx = (int)Math.Floor(w.X[i]), cz = (int)Math.Floor(w.Z[i]); if (cx >= 0 && cz >= 0 && cx < W && cz < H) heat[cz * W + cx]++; }
            if (w.Tick % 300 == 0 || w.Match.Phase == MatchPhase.Ended)
            {
                File.WriteAllText(outPath + ".heat", W + " " + H + "\n" + string.Join(",", heat));
                File.WriteAllText(outPath + ".blasts", blasts.ToString());
                if (w.Match.Phase == MatchPhase.Ended) d.Ticked -= h;
            }
        };
        d.Ticked += h;
        return "heat on " + W + "x" + H;
    }

    public static string Stations()
    {
        World w = D.World; var m = w.Map; var sb = new StringBuilder("hill " + m.Hill.X + "," + m.Hill.Z + " shrine " + (m.Shrine != null ? m.Shrine.X + "," + m.Shrine.Z : "-") + " pads");
        foreach (var p in m.Pads) sb.Append(' ').Append('@').Append(p.X.ToString("F0")).Append(',').Append(p.Z.ToString("F0"));
        return sb.ToString();
    }
    static void Place(World w, int id, double x, double z, double f)
    {
        w.X[id] = x; w.Z[id] = z; w.PrevX[id] = x; w.PrevZ[id] = z; w.Vx[id] = 0; w.Vz[id] = 0; w.Actors[id].Facing = f;
        w.Actors[id].KVx = 0; w.Actors[id].KVz = 0; w.Actors[id].Alive = true; w.Actors[id].StandUp = 0; w.Actors[id].SpawnProtect = 0; w.Actors[id].RespawnIn = 0;
        if (w.Actors[id].Hp <= 0) w.Actors[id].Hp = w.Actors[id].MaxHp;
    }

    /// <summary>First cell of type t with a floor cell to its west (x-1) that is open on its own west too. Returns "cx cz".</summary>
    public static string FindCell(int type, int skip)
    {
        World w = D.World; var m = w.Map; int found = 0;
        for (int z = 2; z < m.Height - 2; z++) for (int x = 3; x < m.Width - 2; x++)
        {
            if (m.Cells[z * m.Width + x] != type) continue;
            if (m.Cells[z * m.Width + x - 1] != 1 || m.Cells[z * m.Width + x - 2] != 1) continue;
            if (found++ < skip) continue;
            return x + " " + z + " h " + GroundHeight.At(m, x - 0.5, z + 0.5).ToString("F2") + " lift " + (m.Lift != null ? m.Lift[z * m.Width + x - 1] : 0);
        }
        return "none";
    }

    /// <summary>Actor 0 (class cls) stands west of cell (cx,cz), faces it, and (mode 0) pushes into it for n ticks with the
    /// move input, or (mode 1) attacks once, or (mode 2) uses the class ability once. Logs per tick: position, displacement,
    /// second difference (vibration), Moving, events by type; WallHp and cell type before and after. Clock pace 1.</summary>
    public static async Task<string> AtWall(int cls, int cx, int cz, int mode, int n, double offset, string outCsv)
    {
        Mute(); var d = D; World w = d.World; int me = LocalSeat.Actor; var m = w.Map;
        d.HumanAutopilot = false; HumanControlGuard.IdleAutopilotEnabled = false; d.TimeScale = 1; AutoplayArgs.ClockHeld = false;
        w.Actors[me].Cls = (CriticalMass.Sim.Config.ClassId)cls;
        double x = cx - 0.5 - offset, z = cz + 0.5; Place(w, me, x, z, 0);
        w.Actors[me].AttackCd = 0; w.Actors[me].AbilityCd = 0;
        int i = cz * m.Width + cx; byte t0 = m.Cells[i]; int hp0 = m.WallHp[i];
        var f = SimInput.EmptyInput(); f.AimX = cx + 0.5; f.AimZ = cz + 0.5;
        if (mode == 0) { f.MoveX = 1; f.MoveZ = 0; }
        var sb = new StringBuilder("tick,x,z,dx,ddx,moving,events\n"); int head = w.EventHead; int startTick = w.Tick; double px = w.X[me], ppx = px; int ticks = 0;
        var evCount = new System.Collections.Generic.Dictionary<string, int>();
        double maxAbsDdx = 0; int signFlips = 0; double lastDx = 0;
        Action<World> h = ww =>
        {
            ticks++;
            if (ticks == 1) { if (mode == 1) f.Attack = true; if (mode == 2) f.Ability = true; }
            else { f.Attack = false; f.Ability = false; }
            string evs = "";
            for (; head < ww.EventHead; head++) { var ev = ww.Events[head % World.EventCapacity]; if (ev.A == me || ev.Type == SimEventType.WallBreak || ev.B == i) { evs += ev.Type + " "; string k = ev.Type.ToString(); evCount[k] = (evCount.TryGetValue(k, out int c) ? c : 0) + 1; } }
            double dx = ww.X[me] - px, ddx = dx - lastDx; if (ticks > 3) { maxAbsDdx = Math.Max(maxAbsDdx, Math.Abs(ddx)); if (Math.Sign(dx) != Math.Sign(lastDx) && Math.Abs(dx) > 1e-5 && Math.Abs(lastDx) > 1e-5) signFlips++; }
            sb.Append(ww.Tick - startTick).Append(',').Append(ww.X[me].ToString("F4")).Append(',').Append(ww.Z[me].ToString("F4")).Append(',').Append(dx.ToString("F4")).Append(',').Append(ddx.ToString("F4")).Append(',').Append(ww.Actors[me].Moving ? 1 : 0).Append(',').Append(evs).Append('\n');
            lastDx = dx; px = ww.X[me];
        };
        SimDriverBehaviour.ScriptedHumanFrame = f;
        d.Ticked += h;
        try { while (ticks < n) await Task.Yield(); }
        finally { d.Ticked -= h; SimDriverBehaviour.ScriptedHumanFrame = null; }
        File.WriteAllText(outCsv, sb.ToString());
        if (outCsv.Contains("freeze")) d.TimeScale = 0;
        var es = string.Join(" ", evCount.Select(kv => kv.Key + "=" + kv.Value));
        return "cls " + (CriticalMass.Sim.Config.ClassId)cls + " mode " + mode + " cell " + t0 + "->" + m.Cells[i] + " hp " + hp0 + "->" + m.WallHp[i] + " endX " + w.X[me].ToString("F3") + " (face at " + (cx - GroundHeight.BodyR).ToString("F2") + ") maxDDx " + maxAbsDdx.ToString("F4") + " flips " + signFlips + " moving " + w.Actors[me].Moving + " events " + es;
    }
    /// <summary>Area D: actor 0 as Demolitions at (x,z) throws TNT at (tx,tz); three enemies stand at 0.6, 1.2 and 1.8 u east of the
    /// target. Freezes the clock on the Explosion event. Reports: ticks press->blast, cells changed within 4 u (type and lift),
    /// enemy displacement and HP, worst frame time in the 30 frames around the blast vs the 30 before.</summary>
    public static async Task<string> Blast(double x, double z, double tx, double tz)
    {
        Mute(); var d = D; World w = d.World; int me = LocalSeat.Actor; var m = w.Map;
        d.HumanAutopilot = false; HumanControlGuard.IdleAutopilotEnabled = false; d.TimeScale = 1; AutoplayArgs.ClockHeld = false;
        w.Actors[me].Cls = CriticalMass.Sim.Config.ClassId.Demolitions; Place(w, me, x, z, Math.Atan2(tz - z, tx - x)); w.Actors[me].AttackCd = 0; w.Actors[me].AbilityCd = 0;
        var en = new System.Collections.Generic.List<int>(); var team = w.Actors[me].Team;
        for (int j = 1; j < w.ActorCount && en.Count < 3; j++) if (w.Actors[j].Team != team) en.Add(j);
        double[] offs = { 0.6, 1.2, 1.8 };
        for (int k = 0; k < en.Count; k++) { Place(w, en[k], tx + offs[k], tz, Math.PI); w.Actors[en[k]].Hp = w.Actors[en[k]].MaxHp; }
        var cells0 = (byte[])m.Cells.Clone(); var lift0 = m.Lift != null ? (sbyte[])m.Lift.Clone() : null;
        double[] ex0 = en.Select(j => w.X[j]).ToArray(), ez0 = en.Select(j => w.Z[j]).ToArray(), hp0 = en.Select(j => w.Actors[j].Hp).ToArray();
        var f = SimInput.EmptyInput(); f.AimX = tx; f.AimZ = tz; int ticks = 0, head = w.EventHead, boomTick = -1, throwTick = -1; double bx = 0, bz = 0;
        Action<World> h = ww =>
        {
            ticks++; f.Attack = ticks == 1;
            foreach (int j in en) { ww.Actors[j].AttackCd = 1; } // dummies never swing back
            for (; head < ww.EventHead; head++) { var ev = ww.Events[head % World.EventCapacity];
                if (ev.Type == SimEventType.TntThrown && throwTick < 0) throwTick = ticks;
                if (ev.Type == SimEventType.Explosion && boomTick < 0) { boomTick = ticks; bx = ev.X; bz = ev.Z; } }
        };
        SimDriverBehaviour.ScriptedHumanFrame = f; d.Ticked += h;
        var dts = new System.Collections.Generic.List<float>(); int boomFrame = -1;
        try { for (int fr = 0; fr < 900; fr++) { await Task.Yield(); dts.Add(Time.unscaledDeltaTime); if (boomTick >= 0 && boomFrame < 0) boomFrame = dts.Count; if (boomFrame > 0 && dts.Count > boomFrame + 30) break; if (boomTick >= 0 && ticks > boomTick + 12 && d.TimeScale > 0) { d.TimeScale = 0; } } }
        finally { d.Ticked -= h; SimDriverBehaviour.ScriptedHumanFrame = null; }
        // the dummies were left alone for 12 ticks after the blast (knock decays over about 12 ticks)
        int changed = 0, toVoid = 0, liftCh = 0; var sb = new StringBuilder();
        for (int i = 0; i < cells0.Length; i++) { if (cells0[i] != m.Cells[i]) { changed++; if (m.Cells[i] == 0) toVoid++; sb.Append(i % m.Width).Append(':').Append(i / m.Width).Append(' ').Append(cells0[i]).Append("->").Append(m.Cells[i]).Append(", "); } if (lift0 != null && lift0[i] != m.Lift[i]) liftCh++; }
        float before = dts.Take(Math.Max(1, boomFrame - 1)).DefaultIfEmpty(0).Max(), around = dts.Skip(Math.Max(0, boomFrame - 2)).DefaultIfEmpty(0).Max();
        float medianDt = dts.OrderBy(v => v).ElementAt(dts.Count / 2);
        var es = new StringBuilder();
        for (int k = 0; k < en.Count; k++) { int j = en[k]; double dd = Math.Sqrt((w.X[j] - ex0[k]) * (w.X[j] - ex0[k]) + (w.Z[j] - ez0[k]) * (w.Z[j] - ez0[k])); es.Append("e@").Append(offs[k]).Append(" moved ").Append(dd.ToString("F2")).Append(" hp ").Append(hp0[k].ToString("F0")).Append("->").Append(w.Actors[j].Hp.ToString("F0")).Append(w.Actors[j].Alive ? "" : " DEAD").Append("; "); }
        return "throw tick " + throwTick + " boom tick " + boomTick + " at " + bx.ToString("F2") + "," + bz.ToString("F2") + " cellsChanged " + changed + " toVoid " + toVoid + " liftChanged " + liftCh + " [" + sb + "] " + es +
               " frame ms: median " + (medianDt * 1000).ToString("F1") + " worst-before " + (before * 1000).ToString("F1") + " worst-around-blast " + (around * 1000).ToString("F1") + " FROZEN tick " + w.Tick;
    }

    public static async Task<string> Run(double seconds)
    {
        var d = D; d.TimeScale = 1; double t0 = Time.realtimeSinceStartupAsDouble; while (Time.realtimeSinceStartupAsDouble - t0 < seconds) await Task.Yield(); d.TimeScale = 0; return "ran " + seconds + " s, frozen tick " + d.World.Tick;
    }
    static double AngDeg(double ax, double az, double bx, double bz)
    { double a = Math.Atan2(az, ax) - Math.Atan2(bz, bx); return Math.Abs(Math.IEEERemainder(a, 2 * Math.PI)) * 180 / Math.PI; }

    /// <summary>Movement assist over the whole map: for every walkable cell centre and the 8 key directions (world dirs from a
    /// fresh HumanInputState with the live camera basis), how far LaneSteer (single keys) and CornerSlide (any device) turn the
    /// pressed direction. Actor 0 is moved to each sample with the clock frozen, then put back.</summary>
    public static string AssistMap(string outCsv)
    {
        var d = D; World w = d.World; var m = w.Map; int me = LocalSeat.Actor; double pace = d.TimeScale; d.TimeScale = 0;
        double ox = w.X[me], oz = w.Z[me], opx = w.PrevX[me], opz = w.PrevZ[me];
        var st = new HumanInputState(); st.BindWorld(w);
        var dirs = new System.Collections.Generic.List<(double x, double z, bool single, string name)>();
        int[,] keys = { { 0, 1 }, { 0, -1 }, { 1, 0 }, { -1, 0 }, { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 } };
        string[] names = { "up", "down", "right", "left", "up-right", "down-right", "up-left", "down-left" };
        for (int k = 0; k < 8; k++) { st.SetMove(keys[k, 0], keys[k, 1], true); st.MoveDirection(out double dx, out double dz); double l = Math.Sqrt(dx * dx + dz * dz); dirs.Add((dx / l, dz / l, k < 4, names[k])); }
        var sb = new StringBuilder("x,z,dir,kind,deg\n"); int samples = 0, laneHits = 0, slideHits = 0; var laneDeg = new System.Collections.Generic.List<double>(); var slideDeg = new System.Collections.Generic.List<double>();
        int singles = 0;
        try
        {
            for (int cz = 0; cz < m.Height; cz++) for (int cx = 0; cx < m.Width; cx++)
            {
                byte c = m.Cells[cz * m.Width + cx]; if (c != 1 && c != 7) continue;
                double x = cx + 0.5, z = cz + 0.5; w.X[me] = x; w.Z[me] = z; w.PrevX[me] = x; w.PrevZ[me] = z;
                foreach (var dr in dirs)
                {
                    samples++; if (dr.single) singles++;
                    if (dr.single && MoveAssist.LaneSteer(x, z, dr.x, dr.z, out double lx, out double lz)) { double a = AngDeg(lx, lz, dr.x, dr.z); if (a > 1) { laneHits++; laneDeg.Add(a); sb.Append(x).Append(',').Append(z).Append(',').Append(dr.name).Append(",lane,").Append(a.ToString("F1")).Append('\n'); } }
                    if (MoveAssist.CornerSlide(w, me, dr.x, dr.z, out double sx, out double sz)) { double a = AngDeg(sx, sz, dr.x, dr.z); if (a > 1) { slideHits++; slideDeg.Add(a); sb.Append(x).Append(',').Append(z).Append(',').Append(dr.name).Append(",slide,").Append(a.ToString("F1")).Append('\n'); } }
                }
            }
        }
        finally { w.X[me] = ox; w.Z[me] = oz; w.PrevX[me] = opx; w.PrevZ[me] = opz; d.TimeScale = pace; }
        File.WriteAllText(outCsv, sb.ToString());
        laneDeg.Sort(); slideDeg.Sort();
        Func<System.Collections.Generic.List<double>, double, double> q = (l, pp) => l.Count == 0 ? 0 : l[Math.Min(l.Count - 1, (int)(pp * l.Count))];
        var dn = string.Join(" ", dirs.Select(x => x.name + "=(" + x.x.ToString("F2") + "," + x.z.ToString("F2") + ")"));
        return "samples " + samples + " (single-key " + singles + ") lane bends " + laneHits + " (" + (laneHits / (double)Math.Max(1, singles)).ToString("P1") + " of single-key) deg p50 " + q(laneDeg, .5).ToString("F0") + " p90 " + q(laneDeg, .9).ToString("F0") + " max " + q(laneDeg, 1).ToString("F0") +
               "; corner slide bends " + slideHits + " (" + (slideHits / (double)Math.Max(1, samples)).ToString("P1") + ") deg p50 " + q(slideDeg, .5).ToString("F0") + " p90 " + q(slideDeg, .9).ToString("F0") + " max " + q(slideDeg, 1).ToString("F0") + " | dirs " + dn;
    }

    /// <summary>Swing commitment: actor 0 (class cls) walks east at full stick; on tick 3 presses attack aimed east; if turn=1 the
    /// aim swings to north at the press + 3 ticks. Logs: travel during wind-up (press -> hit tick), during recovery, SwingFacing at
    /// the start vs at the hit, the dummy hit or not (a dummy stands where the swing pointed at the START, 1.1 u ahead of the
    /// press position). Also the first rendered frame where any bone of the model turns more than 6 degrees beyond the idle
    /// baseline (standing variant only, moving=0).</summary>
    public static async Task<string> Swing(int cls, int moving, int turn, double x, double z)
    {
        Mute(); var d = D; World w = d.World; int me = LocalSeat.Actor; d.HumanAutopilot = false; HumanControlGuard.IdleAutopilotEnabled = false; d.TimeScale = 1; AutoplayArgs.ClockHeld = false;
        w.Actors[me].Cls = (CriticalMass.Sim.Config.ClassId)cls; Place(w, me, x, z, 0); w.Actors[me].AttackCd = 0; w.Actors[me].AbilityCd = 0;
        int dummy = -1; for (int j = 1; j < w.ActorCount && dummy < 0; j++) if (w.Actors[j].Team != w.Actors[me].Team) dummy = j;
        Place(w, dummy, -50, -50, 0);
        var f = SimInput.EmptyInput(); f.MoveX = moving; f.MoveZ = 0; f.AimX = x + 10; f.AimZ = z;
        int ticks = 0, head = w.EventHead, pressTick = -1, hitTick = -1, startTick = -1, windup = -1, recovery = -1; double px = 0, pz = 0, hx = 0, hz = 0, sf0 = double.NaN, sfHit = double.NaN; bool hitDummy = false; double ex = 0, ez = 0; double rx = 0, rz = 0;
        Action<World> h = ww =>
        {
            ticks++;
            ww.Actors[dummy].AttackCd = 1;
            if (ticks == 3) { f.Attack = true; pressTick = ticks; px = ww.X[me]; pz = ww.Z[me]; ex = px + 1.1; ez = pz; Place(ww, dummy, ex, ez, Math.PI); ww.Actors[dummy].Hp = ww.Actors[dummy].MaxHp; }
            else f.Attack = false;
            if (turn == 1 && pressTick > 0 && ticks == pressTick + 3) { f.AimX = ww.X[me]; f.AimZ = ww.Z[me] - 10; }
            if (pressTick > 0 && ticks > pressTick && hitTick < 0) { ww.X[dummy] = ex; ww.Z[dummy] = ez; ww.PrevX[dummy] = ex; ww.PrevZ[dummy] = ez; }
            for (; head < ww.EventHead; head++) { var ev = ww.Events[head % World.EventCapacity];
                if (ev.Type == SimEventType.AttackStart && ev.A == me && startTick < 0) { startTick = ticks; windup = ev.Windup; recovery = ev.Recovery; sf0 = ww.Actors[me].SwingFacing; }
                if (ev.Type == SimEventType.Hit && ev.B == me) { if (hitTick < 0) { hitTick = ticks; hx = ww.X[me]; hz = ww.Z[me]; sfHit = ww.Actors[me].SwingFacing; } if (ev.A == dummy) hitDummy = true; } }
            if (startTick > 0 && windup >= 0 && ticks == startTick + windup + 1 && hitTick < 0) { hx = ww.X[me]; hz = ww.Z[me]; sfHit = ww.Actors[me].SwingFacing; }
            if (startTick > 0 && windup >= 0 && ticks == startTick + windup + recovery + 1) { rx = ww.X[me]; rz = ww.Z[me]; }
        };
        // bone baseline (standing only)
        var views = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.View.ActorViews>();
        var mv = views != null ? views.ModelOf(me) : null; Transform[] bones = mv != null ? mv.Root.GetComponentsInChildren<Transform>() : new Transform[0];
        var prev = bones.Select(b => b.localRotation).ToArray(); double baseMax = 0; int frame = 0, poseFrame = -1, pressFrame = -1; double poseMs = -1, tPress = 0;
        SimDriverBehaviour.ScriptedHumanFrame = f; d.Ticked += h;
        try
        {
            while (ticks < 60)
            {
                await Task.Yield(); frame++;
                double mx = 0; for (int b = 0; b < bones.Length; b++) { if (bones[b] == null) continue; mx = Math.Max(mx, Quaternion.Angle(prev[b], bones[b].localRotation)); prev[b] = bones[b].localRotation; }
                if (pressTick < 0) baseMax = Math.Max(baseMax, mx);
                else { if (pressFrame < 0) { pressFrame = frame; tPress = Time.realtimeSinceStartupAsDouble; } else if (poseFrame < 0 && moving == 0 && mx > Math.Max(6, baseMax * 1.5)) { poseFrame = frame - pressFrame; poseMs = (Time.realtimeSinceStartupAsDouble - tPress) * 1000; } }
            }
        }
        finally { d.Ticked -= h; SimDriverBehaviour.ScriptedHumanFrame = null; }
        double wind = Math.Sqrt((hx - px) * (hx - px) + (hz - pz) * (hz - pz)), rec = Math.Sqrt((rx - hx) * (rx - hx) + (rz - hz) * (rz - hz));
        return (CriticalMass.Sim.Config.ClassId)cls + " moving " + moving + " turn " + turn + " windup " + windup + "t recovery " + recovery + "t press->hitTick " + (hitTick > 0 ? (hitTick - pressTick).ToString() : "none") +
               " travel windup " + wind.ToString("F2") + " u recovery " + rec.ToString("F2") + " u swingFacing start " + (sf0 * 180 / Math.PI).ToString("F0") + " at hit " + (sfHit * 180 / Math.PI).ToString("F0") + " dummyHit " + hitDummy +
               (moving == 0 ? " firstPoseFrame " + poseFrame + " (" + poseMs.ToString("F0") + " ms, baseline " + baseMax.ToString("F1") + " deg)" : "");
    }
    /// <summary>Warrior (or cls) at (x,z) dodges east from standing; the clock freezes on dash tick k (1..6) for a picture.
    /// Returns per-tick positions of the dash.</summary>
    public static async Task<string> DodgeShot(int cls, double x, double z, int k)
    {
        Mute(); var d = D; World w = d.World; int me = LocalSeat.Actor; d.HumanAutopilot = false; HumanControlGuard.IdleAutopilotEnabled = false; d.TimeScale = 1;
        w.Actors[me].Cls = (CriticalMass.Sim.Config.ClassId)cls; Place(w, me, x, z, 0);
        var f = SimInput.EmptyInput(); f.MoveX = 1; f.AimX = x + 10; f.AimZ = z; int ticks = 0, dash = 0; var sb = new StringBuilder(); double lx = x;
        Action<World> h = ww => { ticks++; f.Dodge = ticks == 2; if (Dodge.Active(ww, me)) { dash++; sb.Append((ww.X[me] - lx).ToString("F2")).Append(' '); } lx = ww.X[me]; if (dash == k) d.TimeScale = 0; };
        SimDriverBehaviour.ScriptedHumanFrame = f; d.Ticked += h;
        try { for (int i = 0; i < 400 && dash < k; i++) await Task.Yield(); }
        finally { d.Ticked -= h; SimDriverBehaviour.ScriptedHumanFrame = null; }
        return "dash ticks " + dash + " per-tick u: " + sb + " frozen at " + w.X[me].ToString("F2") + "," + w.Z[me].ToString("F2");
    }
}
