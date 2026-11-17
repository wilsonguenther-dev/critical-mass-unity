// Play mode only. Drives the HUMAN's own input path (HumanInputState: aim + the DigPlace button = what F does) so the sim's
// Terraform rules run exactly as for a player. Never writes the World directly.
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using UnityEngine;

public static class Dig
{
    static World W(out SimDriverBehaviour d)
    {
        d = Object.FindAnyObjectByType<SimDriverBehaviour>();
        return d != null ? d.World : null;
    }

    /// <summary>mode 0 = find a diggable Floor cell in reach, 1 = find a placeable Void cell in reach. Aims at it and presses
    /// DigPlace once. Returns the chosen cell, its world centre and the carry before.</summary>
    public static string Press(int mode)
    {
        World w = W(out _);
        if (w == null) return "no world";
        var src = Object.FindAnyObjectByType<HumanInputSource>();
        if (src == null) return "no HumanInputSource";
        double reach = w.Config.Abilities.MinerTerraform.Reach;
        double px = w.X[0], pz = w.Z[0];
        int best = -1; double bestD = 1e9;
        var m = w.Map;
        for (int cz = 0; cz < m.Height; cz++)
            for (int cx = 0; cx < m.Width; cx++)
            {
                double dx = cx + 0.5 - px, dz = cz + 0.5 - pz, dd = dx * dx + dz * dz;
                if (dd > reach * reach || dd < 0.8 || dd >= bestD) continue;
                int why = mode == 0 ? Terraform.DigRefusal(w, cx, cz) : Terraform.PlaceRefusal(w, cx, cz);
                if (why != 0) continue;
                best = cz * m.Width + cx; bestD = dd;
            }
        if (best < 0) return "no valid cell in reach " + reach + " (mode " + mode + ") at (" + px.ToString("0.0") + "," + pz.ToString("0.0") + ")";
        int bx = best % m.Width, bz = best / m.Width;
        src.State.SetAim(bx + 0.5, bz + 0.5);
        src.State.ButtonDown(HumanInputState.HumanButton.DigPlace);
        src.State.ButtonUp(HumanInputState.HumanButton.DigPlace);
        Vector3 wc = CriticalMass.Runtime.SimSpace.ToWorld(bx + 0.5, 0, bz + 0.5);
        return "pressed DigPlace at cell (" + bx + "," + bz + ") type=" + m.Cells[best] + " worldCentre=" + wc + " carry=" + w.Terraform.Carried[0] + " mode=" + Terraform.ModeOf(w, 0) + " wait=" + Terraform.WaitTicks(w, w.Actors[0].Team);
    }

    /// <summary>Cell type at sim cell (cx, cz), the human's carry and mode.</summary>
    public static string Cell(int cx, int cz)
    {
        World w = W(out _);
        if (w == null) return "no world";
        return "cell(" + cx + "," + cz + ")=" + w.Map.Cells[cz * w.Map.Width + cx] + " carry=" + w.Terraform.Carried[0] + " mode=" + Terraform.ModeOf(w, 0) + " gridVersion=" + w.GridVersion + " tick=" + w.Tick;
    }
}
