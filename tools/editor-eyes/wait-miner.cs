var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>();
var t0 = UnityEngine.Time.realtimeSinceStartup;
await Wait.Until(() => d != null && d.World != null && (CriticalMass.Sim.Core.Blacksmith.ChannelVein(d.World, 0) >= 0 || UnityEngine.Time.realtimeSinceStartup - t0 > 140f));
var w = d.World;
return "channel vein=" + CriticalMass.Sim.Core.Blacksmith.ChannelVein(w, 0) + " ore=" + CriticalMass.Sim.Core.Blacksmith.OreOf(w.Actors[0]) + " tick=" + w.Tick + " pos=(" + w.X[0].ToString("0.0") + "," + w.Z[0].ToString("0.0") + ") waited=" + (UnityEngine.Time.realtimeSinceStartup - t0).ToString("0") + "s";
