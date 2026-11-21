public static class Dbg2 { public static string Run(int seed, int ticks, double thr) {
 World w = PA.NewW(ClassId.Warrior, seed, 16, 30); InputFrame s = SimInput.EmptyInput(); var sb = new StringBuilder(); int n = w.ActorCount; int lines=0;
 var hist = new int[10];
 for (int t=0;t<ticks;t++){ MatchTick.Advance(w,null,s);
  for (int i=0;i<n;i++){ if(!w.Actors[i].Alive||w.Actors[i].AirLen>0) continue; for(int j=i+1;j<n;j++){ if(!w.Actors[j].Alive||w.Actors[j].AirLen>0) continue;
   double dx=w.X[i]-w.X[j],dz=w.Z[i]-w.Z[j]; double ov=Collide.ActorRadius(w,i)+Collide.ActorRadius(w,j)-Math.Sqrt(dx*dx+dz*dz);
   if (ov>0.05) hist[Math.Min(9,(int)(ov/0.1))]++;
   if (ov>thr && lines<40){ lines++; var a=w.Actors[i]; var b=w.Actors[j]; int ci=MapGrid.CellAt(w.Map,w.X[i],w.Z[i]), cj=MapGrid.CellAt(w.Map,w.X[j],w.Z[j]);
    sb.Append("t "+w.Tick+" ov "+ov.ToString("F2")+" "+i+"("+a.Team+" "+a.Cls+" cell "+ci+" su "+a.StandUp.ToString("F2")+" sp "+a.SpawnProtect.ToString("F1")+" mv "+a.Moving+") "+j+"("+b.Team+" "+b.Cls+" cell "+cj+" su "+b.StandUp.ToString("F2")+" sp "+b.SpawnProtect.ToString("F1")+" mv "+b.Moving+") at "+w.X[i].ToString("F1")+","+w.Z[i].ToString("F1")+"\n"); } } } }
 sb.Append("overlap histogram (0.05-0.1,0.1-0.2,...): "+string.Join(",",hist)+"\n"); return sb.ToString(); } }
