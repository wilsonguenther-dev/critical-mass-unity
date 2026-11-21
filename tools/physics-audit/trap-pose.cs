public static class TrapPose { public static string Run(string mode, int at) {
 World w = PA.NewW(ClassId.Warrior, 4242, 16, 1e6); PA.Bench(w, new[] { 0 }); PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
 w.ForceHatch = true; for (int t = 0; t < 40 && !w.Match.Hatched; t++) { PA.In(w, 0, 0, 0); SimStep.Step(w); }
 int wx = (int)ox + 2; for (int cz = (int)oz - 5; cz <= (int)oz + 5; cz++) w.Map.Cells[cz * w.Map.Width + wx] = Cell.Wall;
 if (mode == "corridor2") for (int cx = (int)ox - 6; cx <= wx; cx++) { w.Map.Cells[((int)oz - 1) * w.Map.Width + cx] = Cell.Wall; w.Map.Cells[((int)oz + 2) * w.Map.Width + cx] = Cell.Wall; }
 double fx = wx - 0.5, fz = oz + 0.5; PA.Put(w, 0, fx, fz); var tr = new StringBuilder(); Kaiju k = w.Kaiju; double kx = fx - 4, kz = fz;
 for (int t = 0; t < at; t++) { kx = Math.Min(kx + 2.6 / 30, wx - 0.2); k.X = kx; k.Z = kz; k.PrevX = kx; k.PrevZ = kz; k.TargetId = -1; PA.In(w, 0, 0, t >= 30 ? -1 : 0); SimStep.Step(w); if (t >= 18) tr.Append(t + ":" + w.X[0].ToString("F2") + "," + w.Z[0].ToString("F2") + "/k" + w.Kaiju.X.ToString("F2") + " "); }
 var sb = new StringBuilder(); for (int i = 0; i < w.ActorCount; i++) sb.Append(i).Append(i == 0 ? " 1 0 " : " 0 0 ").Append(w.X[i].ToString("R")).Append(' ').Append(w.Z[i].ToString("R")).Append(' ').Append(w.Actors[i].Facing.ToString("R")).Append('\n');
 sb.Append("K 1 ").Append(w.Kaiju.X.ToString("R")).Append(' ').Append(w.Kaiju.Z.ToString("R")).Append(" 0\n");
 File.WriteAllText(PA.Out + "poses/trap-" + mode + "-t" + at + ".txt", sb.ToString());
 double dx = w.X[0] - w.Kaiju.X, dz = w.Z[0] - w.Kaiju.Z; return tr + "\n" + mode + " wall x " + wx + " corridor rows " + ((int)oz - 1) + "," + ((int)oz + 2) + " x " + ((int)ox - 6) + ".." + wx + "; fighter " + w.X[0].ToString("F2") + "," + w.Z[0].ToString("F2") + " kaiju " + w.Kaiju.X.ToString("F2") + "," + w.Kaiju.Z.ToString("F2") + " d " + Math.Sqrt(dx*dx+dz*dz).ToString("F2"); } }
