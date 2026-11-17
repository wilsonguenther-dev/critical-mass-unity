// Autopilot walks actor 0 until a diggable cell is in reach AND the team may terraform, then hands control back (idle
// autopilot off) and presses DigPlace (F) through the human input path, aimed at that cell.
var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>();
var src = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Input.HumanInputSource>();
CriticalMass.Runtime.Game.HumanControlGuard.IdleAutopilotEnabled = true;
d.HumanAutopilot = true;
int bx = -1, bz = -1;
System.Func<bool> ok = () => {
  var w = d.World; double r = w.Config.Abilities.MinerTerraform.Reach; double px = w.X[0], pz = w.Z[0];
  if (CriticalMass.Sim.Core.Terraform.WaitTicks(w, w.Actors[0].Team) > 0) return false;
  for (int cz = (int)(pz - 2); cz <= (int)(pz + 2); cz++) for (int cx = (int)(px - 2); cx <= (int)(px + 2); cx++) {
    if (cx < 0 || cz < 0 || cx >= w.Map.Width || cz >= w.Map.Height) continue;
    if (w.Map.Cells[cz * w.Map.Width + cx] != CriticalMass.Sim.Core.Cell.Floor) continue;
    double dx = cx + 0.5 - px, dz = cz + 0.5 - pz; double dd = dx*dx+dz*dz;
    if (dd <= r*r && dd >= 0.8 && CriticalMass.Sim.Core.Terraform.DigRefusal(w, cx, cz) == 0) { bx = cx; bz = cz; return true; } }
  return false; };
var t0 = UnityEngine.Time.realtimeSinceStartup;
bool found = false;
for (int k = 0; k < 12 && !found; k++) { try { await Wait.Until(() => ok()); found = true; } catch (System.Exception) { } }
d.HumanAutopilot = false;
CriticalMass.Runtime.Game.HumanControlGuard.IdleAutopilotEnabled = false;
if (!found) return "no spot after " + (UnityEngine.Time.realtimeSinceStartup - t0).ToString("0") + "s";
int carry0 = d.World.Terraform.Carried[0];
// HumanInputSource.Update rewrites the aim from the mouse ray every frame; pause that component for the press only.
src.enabled = false;
src.State.SetAim(bx + 0.5, bz + 0.5);
src.State.ButtonDown(CriticalMass.Runtime.Input.HumanInputState.HumanButton.DigPlace);
src.State.ButtonUp(CriticalMass.Runtime.Input.HumanInputState.HumanButton.DigPlace);
int f0 = UnityEngine.Time.frameCount;
try { await Wait.Until(() => d.World.Terraform.Carried[0] != carry0 || UnityEngine.Time.frameCount - f0 > 240); } finally { src.enabled = true; }
var ww = d.World;
return "cell(" + bx + "," + bz + ") now=" + ww.Map.Cells[bz * ww.Map.Width + bx] + " carry " + carry0 + "->" + ww.Terraform.Carried[0] + " p=(" + ww.X[0].ToString("0.00") + "," + ww.Z[0].ToString("0.00") + ") world=" + CriticalMass.Runtime.SimSpace.ToWorld(bx + 0.5, 0, bz + 0.5) + " t=" + (UnityEngine.Time.realtimeSinceStartup - t0).ToString("0") + "s";
