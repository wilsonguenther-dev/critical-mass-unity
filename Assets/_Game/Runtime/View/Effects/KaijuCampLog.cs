using System;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Play-test log only (integration batch 10, for feat/kaiju-bridges-1/2): `-kaijuCampLog` watches the live match without
    /// writing anything and logs whether the Kaiju camps outside a base. Every 30 sim seconds one "[CM.KaijuCamp] t=..." line
    /// with its position, state and distance to each pad; at match end one summary: sim seconds hatched, within 8 m of the
    /// Azure pad (Map.Pads[0]) and the Crimson pad (Map.Pads[1]), on the hill, fighters knocked out by the Kaiju per team,
    /// the hill totals and the winner.
    /// </summary>
    public sealed class KaijuCampLog : MonoBehaviour
    {
        public const string Flag = "-kaijuCampLog";
        public const double CampRadius = 8.0;

        public static bool Requested =>
            Array.FindIndex(Environment.GetCommandLineArgs(), s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase)) >= 0;

        SimDriverBehaviour _driver;
        World _world;
        int _tick = -1, _hatchTick = -1, _killsOfAzure, _killsOfCrimson, _bySlam;
        double _hatched, _nearAzure, _nearCrimson, _onHill, _nextReport;
        bool[] _alive;
        bool _done;

        static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

        void Update()
        {
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return;
            World w = _driver.World;
            if (w != _world) { Reset(w); return; }
            if (_done || w.Tick <= _tick) return;
            double dt = (w.Tick - _tick) * SimStep.Dt(w.Config);
            _tick = w.Tick;

            Kaiju k = w.Kaiju;
            if (k != null && k.State != KaijuState.Egg)
            {
                if (_hatchTick < 0) _hatchTick = w.Tick;
                _hatched += dt;
                double da = Dist(k.X, k.Z, w.Map.Pads[0].X, w.Map.Pads[0].Z);
                double dc = Dist(k.X, k.Z, w.Map.Pads[1].X, w.Map.Pads[1].Z);
                if (da <= CampRadius) _nearAzure += dt;
                if (dc <= CampRadius) _nearCrimson += dt;
                if (Dist(k.X, k.Z, w.Map.Hill.X, w.Map.Hill.Z) <= w.Map.Hill.R) _onHill += dt;
            }

            for (int i = 0; i < w.Actors.Length && i < _alive.Length; i++)
            {
                Actor a = w.Actors[i];
                if (_alive[i] && !a.Alive && (a.LastAttacker == Damage.KaijuSource || a.LastAttacker == Damage.KaijuSlamSource))
                {
                    if (a.Team == TeamId.Azure) _killsOfAzure++; else _killsOfCrimson++;
                    if (a.LastAttacker == Damage.KaijuSlamSource) _bySlam++;
                }
                _alive[i] = a.Alive;
            }

            double t = w.Tick * SimStep.Dt(w.Config);
            if (t >= _nextReport)
            {
                _nextReport += 30.0;
                if (k != null && k.State != KaijuState.Egg)
                    Debug.Log($"[CM.KaijuCamp] t={F(t)}s kaiju {k.State} at ({F(k.X)}, {F(k.Z)}) target {k.TargetId}; " +
                              $"to Azure pad {F(Dist(k.X, k.Z, w.Map.Pads[0].X, w.Map.Pads[0].Z))} m, to Crimson pad {F(Dist(k.X, k.Z, w.Map.Pads[1].X, w.Map.Pads[1].Z))} m");
            }

            if (w.Match != null && w.Match.Winner != MatchResult.None)
            {
                _done = true;
                Debug.Log($"[CM.KaijuCamp] MATCH END winner={w.Match.Winner} hill Azure {F(w.Match.HillSeconds.Azure)}s Crimson {F(w.Match.HillSeconds.Crimson)}s; " +
                          $"kaiju hatched at t={(_hatchTick < 0 ? "never" : F(_hatchTick * SimStep.Dt(w.Config)) + "s")}, out {F(_hatched)}s, " +
                          $"within {F(CampRadius)} m of the Azure pad {F(_nearAzure)}s, of the Crimson pad {F(_nearCrimson)}s, on the hill {F(_onHill)}s; " +
                          $"fighters knocked out by the Kaiju: Azure {_killsOfAzure}, Crimson {_killsOfCrimson} (by slam {_bySlam})");
            }
        }

        void Reset(World w)
        {
            _world = w;
            _tick = w.Tick;
            _hatchTick = -1;
            _killsOfAzure = _killsOfCrimson = _bySlam = 0;
            _hatched = _nearAzure = _nearCrimson = _onHill = 0;
            _nextReport = 30.0;
            _done = false;
            _alive = new bool[w.Actors.Length];
            for (int i = 0; i < w.Actors.Length; i++) _alive[i] = w.Actors[i].Alive;
        }

        static double Dist(double ax, double az, double bx, double bz)
        {
            double dx = ax - bx, dz = az - bz;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
