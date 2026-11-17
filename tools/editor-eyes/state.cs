var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>();
if (d == null) return "no driver; frame=" + UnityEngine.Time.frameCount;
var w = d.World;
if (w == null) return "driver, no world";
var a = w.Actors[0];
return "frame=" + UnityEngine.Time.frameCount + " fps~" + (1f/UnityEngine.Time.smoothDeltaTime).ToString("0") + " tick=" + w.Tick + " actors=" + w.ActorCount + " human cls=" + a.Cls + " team=" + a.Team + " pos=(" + w.X[0].ToString("0.0") + "," + w.Z[0].ToString("0.0") + ") hutOn=" + CriticalMass.Sim.Core.Blacksmith.On(w) + " phase=" + w.Match.Phase + " cam=" + (UnityEngine.Camera.main ? UnityEngine.Camera.main.name : "none");
