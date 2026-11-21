// Physics audit 2026-11-21, Play-mode staging (Editor-only, outside Assets/). Writes ONLY the local Editor match for captures:
// freeze the clock, place bodies from a pose file exported by PA.cs (the measured sim state), edit cells for staged geometry.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

public static class PV
{
    static SimDriverBehaviour D => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
    static void Mute() { UnityEditor.EditorUtility.audioMasterMute = true; AudioListener.volume = 0f; }
    static void Put(World w, int id, double x, double z, double f)
    {
        w.X[id] = x; w.Z[id] = z; w.PrevX[id] = x; w.PrevZ[id] = z; w.Vx[id] = 0; w.Vz[id] = 0; w.Actors[id].Facing = f;
        w.Actors[id].KVx = 0; w.Actors[id].KVz = 0; w.Actors[id].Moving = false;
    }
    public static string Freeze(int on)
    {
        Mute(); var d = D;
        d.HumanAutopilot = false; HumanControlGuard.IdleAutopilotEnabled = false;
        d.TimeScale = on == 1 ? 0 : 1; AutoplayArgs.ClockHeld = on == 1;
        return "frozen " + (on == 1) + " tick " + d.World.Tick;
    }
    /// <summary>Place every body from a pose file (bodies not alive in the file are benched); kaiju = 1 also places the Kaiju.</summary>
    public static string Stage(string file, int kaiju, int forceAllCls)
    {
        Mute(); var d = D; World w = d.World;
        string[] lines = File.ReadAllLines(file);
        int placed = 0;
        foreach (string ln in lines)
        {
            string[] p = ln.Split(' ');
            if (p[0] == "K")
            {
                if (kaiju == 1 && w.Match.Hatched) { w.Kaiju.X = double.Parse(p[2]); w.Kaiju.Z = double.Parse(p[3]); w.Kaiju.PrevX = w.Kaiju.X; w.Kaiju.PrevZ = w.Kaiju.Z; w.Kaiju.Facing = double.Parse(p[4]); }
                continue;
            }
            int id = int.Parse(p[0]); if (id >= w.ActorCount) continue;
            Actor a = w.Actors[id];
            if (p[1] == "1")
            {
                a.Alive = true; a.RespawnIn = 0; a.StandUp = 0; a.SpawnProtect = 0; if (a.Hp <= 0) a.Hp = a.MaxHp;
                if (forceAllCls == 1) a.Cls = (CriticalMass.Sim.Config.ClassId)int.Parse(p[2]);
                Put(w, id, double.Parse(p[3]), double.Parse(p[4]), double.Parse(p[5])); placed++;
            }
            else { a.Alive = false; a.Hp = 0; a.RespawnIn = 1e9; Put(w, id, -40 - id, -40, 0); }
        }
        return "placed " + placed + " tick " + w.Tick;
    }
    public static string Cells(int x0, int z0, int x1, int z1, int type)
    {
        World w = D.World; int n = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { MapGrid.SetCell(w, x, z, (byte)type); n++; }
        return "set " + n + " cells to " + type + " gridVersion " + w.GridVersion;
    }
    public static string Where(int id) { World w = D.World; Vector3 v = SimSpace.ToWorld(w.X[id], 0, w.Z[id]); return id + " sim " + w.X[id].ToString("F2") + "," + w.Z[id].ToString("F2") + " world " + v.x.ToString("F2") + "," + v.z.ToString("F2") + " cls " + w.Actors[id].Cls; }

    /// <summary>View lag: per rendered frame for `frames` frames (autopilot on, pace 1): rendered (Lerp) vs current sim position of the
    /// local fighter, camera ground focus vs the fighter, frame-to-frame focus jumps, facing flips of the fighter per tick.</summary>
    public static async Task<string> Lag(int frames)
    {
        Mute(); var d = D; d.TimeScale = 1; AutoplayArgs.ClockHeld = false; d.HumanAutopilot = true;
        var sb = new StringBuilder(); var cam = Camera.main;
        int me = LocalSeat.Actor; double sumLag = 0, maxLag = 0, sumCam = 0, maxCam = 0, maxJump = 0; int n = 0, flips90 = 0, ticks = 0; int lastTick = -1; double lastFace = 0;
        Vector3 lastFocus = Vector3.zero; bool haveFocus = false; double maxFocusAccel = 0; Vector3 lastVel = Vector3.zero;
        var csv = new StringBuilder("frame,dt,tick,alpha,simX,simZ,lerpX,lerpZ,focusX,focusZ\n");
        for (int f = 0; f < frames; f++)
        {
            await Task.Yield();
            World w = d.World; if (w == null || !w.Actors[me].Alive) continue;
            double a = d.Alpha;
            double lx = w.PrevX[me] + (w.X[me] - w.PrevX[me]) * a, lz = w.PrevZ[me] + (w.Z[me] - w.PrevZ[me]) * a;
            double lag = Math.Sqrt((w.X[me] - lx) * (w.X[me] - lx) + (w.Z[me] - lz) * (w.Z[me] - lz));
            sumLag += lag; maxLag = Math.Max(maxLag, lag); n++;
            if (w.Tick != lastTick)
            {
                double df = w.Actors[me].Facing - lastFace; while (df > Math.PI) df -= 2 * Math.PI; while (df < -Math.PI) df += 2 * Math.PI;
                if (lastTick >= 0 && Math.Abs(df) > Math.PI / 2) flips90++;
                lastFace = w.Actors[me].Facing; lastTick = w.Tick; ticks++;
            }
            Ray r = new Ray(cam.transform.position, cam.transform.forward);
            if (Mathf.Abs(r.direction.y) > 1e-4)
            {
                float t = -r.origin.y / r.direction.y; Vector3 focus = r.origin + r.direction * t;
                Vector3 pl = SimSpace.ToWorld(lx, 0, lz);
                double cd = Math.Sqrt((focus.x - pl.x) * (focus.x - pl.x) + (focus.z - pl.z) * (focus.z - pl.z));
                sumCam += cd; maxCam = Math.Max(maxCam, cd);
                if (haveFocus) { Vector3 v = (focus - lastFocus) / Mathf.Max(1e-4f, Time.deltaTime); maxJump = Math.Max(maxJump, (focus - lastFocus).magnitude); maxFocusAccel = Math.Max(maxFocusAccel, (v - lastVel).magnitude / Mathf.Max(1e-4f, Time.deltaTime)); lastVel = v; }
                lastFocus = focus; haveFocus = true;
                csv.Append(f).Append(',').Append(Time.deltaTime.ToString("F4")).Append(',').Append(w.Tick).Append(',').Append(a.ToString("F3")).Append(',').Append(w.X[me].ToString("F3")).Append(',').Append(w.Z[me].ToString("F3")).Append(',')
                   .Append(lx.ToString("F3")).Append(',').Append(lz.ToString("F3")).Append(',').Append(focus.x.ToString("F3")).Append(',').Append((-focus.z).ToString("F3")).Append('\n');
            }
        }
        File.WriteAllText("/Users/wilsonguenther/cm-incidents/physics-audit-2026-11-21/data/view-lag.csv", csv.ToString());
        sb.Append("frames ").Append(n).Append(", ticks ").Append(ticks).Append(": rendered behind sim mean ").Append((sumLag / Math.Max(1, n)).ToString("F3")).Append(" u, max ").Append(maxLag.ToString("F3"))
          .Append(" u; camera ground focus off the rendered fighter mean ").Append((sumCam / Math.Max(1, n)).ToString("F2")).Append(" u, max ").Append(maxCam.ToString("F2"))
          .Append(" u; largest focus move in one frame ").Append(maxJump.ToString("F3")).Append(" u; fighter facing flips > 90 deg in one tick ").Append(flips90);
        return sb.ToString();
    }
}
public static class PVT
{
    static SimDriverBehaviour D => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
    static readonly int[] V = { 16, 17, 18, 19, 20 };
    static readonly double[] R = { 0.4, 0.9, 1.4, 1.9, 2.4 };
    public static string Setup(double cx, double cz)
    {
        World w = D.World; int th = 1;
        for (int id = 0; id < w.ActorCount; id++)
        {
            Actor a = w.Actors[id];
            bool keep = id == 0 || id == th || Array.IndexOf(V, id) >= 0;
            if (!keep) { a.Alive = false; a.Hp = 0; a.RespawnIn = 1e9; w.X[id] = -40 - id; w.Z[id] = -40; w.PrevX[id] = w.X[id]; w.PrevZ[id] = -40; continue; }
            a.Alive = true; a.RespawnIn = 0; a.StandUp = 0; a.SpawnProtect = 0; a.Hp = a.MaxHp; a.KVx = a.KVz = 0; a.HitStunTicks = 0;
        }
        w.Actors[th].Cls = CriticalMass.Sim.Config.ClassId.Demolitions;
        Set(w, 0, cx - 7, cz + 0.5, 0); Set(w, th, cx - 5, cz, 0);
        for (int k = 0; k < V.Length; k++) { double ang = k * 2 * Math.PI / V.Length + 0.3; Set(w, V[k], cx + R[k] * Math.Cos(ang), cz + R[k] * Math.Sin(ang), ang + Math.PI); }
        w.Demolitions.TntReadyTick[th] = 0;
        int res = CriticalMass.Sim.Core.Demolitions.ThrowTnt(w, th, cx, cz);
        return "throw " + res + " tick " + w.Tick;
    }
    static void Set(World w, int id, double x, double z, double f) { w.X[id] = x; w.Z[id] = z; w.PrevX[id] = x; w.PrevZ[id] = z; w.Vx[id] = 0; w.Vz[id] = 0; w.Actors[id].Facing = f; }
    public static string Step(int n)
    {
        World w = D.World; var f = CriticalMass.Sim.Core.SimInput.EmptyInput(); var sb = new StringBuilder();
        for (int t = 0; t < n; t++) { for (int id = 0; id < w.ActorCount; id++) CriticalMass.Sim.Core.SimInput.ApplyInput(w, id, f); SimStep.Step(w); }
        for (int id = 0; id < w.ActorCount; id++) { w.PrevX[id] = w.X[id]; w.PrevZ[id] = w.Z[id]; }
        sb.Append("tick ").Append(w.Tick);
        foreach (int id in V) sb.Append(" | ").Append(id).Append(' ').Append(w.X[id].ToString("F2")).Append(',').Append(w.Z[id].ToString("F2")).Append(" hp ").Append(w.Actors[id].Hp.ToString("F0")).Append(w.Actors[id].Alive ? "" : " DEAD");
        return sb.ToString();
    }
}
