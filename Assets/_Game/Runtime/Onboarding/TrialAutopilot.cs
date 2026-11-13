using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// Plays the guided match for actor 0 (the acceptance test "autopilot completes every step in under 4 sim minutes",
    /// and the integrator's captures): walks a flow field to the active step's arrow target and presses what the step
    /// asks for. It produces an InputFrame exactly as the human's input does; the caller hands it to MatchTick.Advance.
    /// Allocation-free after construction (the flow field is recomputed only when the goal cell changes).
    /// </summary>
    public sealed class TrialAutopilot
    {
        const int RingGoals = 12;
        readonly PointXZ[] _goal = new PointXZ[1];
        readonly PointXZ[] _ring = new PointXZ[RingGoals];
        FlowField _field;
        int _goalCell = -1;
        int _goalGridVersion = -1;
        bool _jumpPressed;

        /// <summary>Fills frame with actor 0's input for this tick.</summary>
        public void Fill(World w, TrialRunner runner, InputFrame frame)
        {
            frame.MoveX = frame.MoveZ = 0;
            frame.Attack = frame.Ability = frame.Interact = frame.Terraform = frame.Jump = false;
            frame.ClassPick = -1;
            frame.AimX = double.NaN;
            frame.AimZ = double.NaN;
            Actor a = w.Actors[TrialChecks.Player];
            TrialStep s = runner.ActiveStep;
            if (s == null || !a.Alive) return;
            double px = w.X[TrialChecks.Player], pz = w.Z[TrialChecks.Player];
            bool has = runner.Target(w, out double tx, out double tz);
            ClassConfig cc = w.Config.Class(a.Cls);

            switch (s.Goal)
            {
                case TrialGoal.Move:
                {
                    // Walk toward the hill: any direction counts, this one leaves the pad crowd.
                    WalkTo(w, w.Map.Hill.X, w.Map.Hill.Z, 0, frame);
                    return;
                }
                case TrialGoal.ReachMarker:
                    if (has) WalkTo(w, tx, tz, 0.3, frame);
                    return;
                case TrialGoal.KnockOut:
                case TrialGoal.UseAbility:
                {
                    if (!has) return;
                    double reach = cc.AttackRange + 0.3;
                    // Whoever is already in reach gets hit first (a hurt target may run home); else chase the arrow's.
                    int near = TrialChecks.PickEnemy(w, false);
                    if (near >= 0 && TrialChecks.Dist2(px, pz, w.X[near], w.Z[near]) <= reach * reach)
                    {
                        tx = w.X[near];
                        tz = w.Z[near];
                    }
                    frame.AimX = tx;
                    frame.AimZ = tz;
                    if (TrialChecks.Dist2(px, pz, tx, tz) > reach * reach) WalkTo(w, tx, tz, 0, frame);
                    else frame.Attack = true;
                    if (s.Goal == TrialGoal.UseAbility && a.AbilityCd <= 0) frame.Ability = true;
                    return;
                }
                case TrialGoal.GatherBiomass:
                {
                    if (!has) return;
                    double r = w.Config.Resources.NodeInteractRange + cc.Radius - 0.3;
                    if (TrialChecks.Dist2(px, pz, tx, tz) > r * r) WalkTo(w, tx, tz, 1.0, frame);
                    else frame.Interact = true;
                    return;
                }
                case TrialGoal.DeliverBiomass:
                {
                    // No cargo (it died on the way): fetch more first, the step still waits for a delivery.
                    if (a.CarryAmount <= 0)
                    {
                        int ni = TrialChecks.NearestBiomassNode(w, px, pz);
                        if (ni < 0) return;
                        ResourceNode n = w.Nodes[ni];
                        double rn = w.Config.Resources.NodeInteractRange + cc.Radius - 0.3;
                        if (TrialChecks.Dist2(px, pz, n.X, n.Z) > rn * rn) WalkTo(w, n.X, n.Z, 1.0, frame);
                        else frame.Interact = true;
                        return;
                    }
                    double r = w.Config.KaijuFeed.ShrineInteractRange - 0.4;
                    if (TrialChecks.Dist2(px, pz, tx, tz) > r * r) WalkTo(w, tx, tz, 2.5, frame);
                    else frame.Interact = true;
                    return;
                }
                case TrialGoal.HoldHill:
                {
                    double hold = w.Config.Hill.Radius - 2;
                    if (TrialChecks.Dist2(px, pz, tx, tz) > hold * hold) WalkTo(w, tx, tz, 0, frame);
                    // On the hill: hit whoever comes close, so the count stays ours.
                    int e = TrialChecks.PickEnemy(w, false);
                    if (e >= 0 && TrialChecks.Dist2(px, pz, w.X[e], w.Z[e]) <= (cc.AttackRange + 0.3) * (cc.AttackRange + 0.3))
                    {
                        frame.AimX = w.X[e];
                        frame.AimZ = w.Z[e];
                        frame.Attack = true;
                    }
                    return;
                }
                case TrialGoal.JumpGap:
                {
                    if (!has) return;
                    double d2 = TrialChecks.Dist2(px, pz, tx, tz);
                    if (d2 > 2.5 * 2.5 && !runner.Probe.SawOverGap)
                    {
                        WalkTo(w, tx, tz, 1.2, frame);
                        _jumpPressed = false;
                        return;
                    }
                    // At the edge: run straight across the gap centre and press jump once.
                    Toward(px, pz, tx + (tx - px), tz + (tz - pz), frame);
                    if (!_jumpPressed)
                    {
                        frame.Jump = true;
                        _jumpPressed = true;
                    }
                    return;
                }
                case TrialGoal.Dig:
                    frame.Terraform = (w.Tick & 15) == 0;
                    return;
                case TrialGoal.Place:
                    if (!has) return;
                    if (TrialChecks.Dist2(px, pz, tx, tz) > 1.5 * 1.5) WalkTo(w, tx, tz, 1.2, frame);
                    else
                    {
                        frame.AimX = tx;
                        frame.AimZ = tz;
                        frame.Terraform = (w.Tick & 15) == 0;
                    }
                    return;
            }
        }

        /// <summary>Follows a flow field to (tx, tz); ringR > 0 targets a ring of cells around it (solid shrine, node).</summary>
        void WalkTo(World w, double tx, double tz, double ringR, InputFrame frame)
        {
            MapDef map = w.Map;
            double px = w.X[TrialChecks.Player], pz = w.Z[TrialChecks.Player];
            int cell = MapGrid.CellIndex(map, (int)Math.Floor(tx), (int)Math.Floor(tz));
            if (cell != _goalCell || w.GridVersion != _goalGridVersion || _field == null)
            {
                _goalCell = cell;
                _goalGridVersion = w.GridVersion;
                if (ringR > 0 || !MapGrid.IsWalkable(map, (int)Math.Floor(tx), (int)Math.Floor(tz)))
                {
                    double r = ringR > 0 ? ringR : 1.2;
                    for (int i = 0; i < RingGoals; i++)
                    {
                        double ang = i * (2 * Math.PI / RingGoals);
                        _ring[i] = new PointXZ(tx + Math.Cos(ang) * r, tz + Math.Sin(ang) * r);
                    }
                    _field = FlowField.ComputeFlowField(map, _ring, _field);
                }
                else
                {
                    _goal[0] = new PointXZ(tx, tz);
                    _field = FlowField.ComputeFlowField(map, _goal, _field);
                }
            }
            if (FlowField.SampleFlowDir(_field, map, px, pz, out double dx, out double dz))
            {
                frame.MoveX = dx;
                frame.MoveZ = dz;
            }
            else Toward(px, pz, tx, tz, frame);
        }

        static void Toward(double px, double pz, double tx, double tz, InputFrame frame)
        {
            double dx = tx - px, dz = tz - pz;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) return;
            frame.MoveX = dx / len;
            frame.MoveZ = dz / len;
        }
    }
}
