public static class Dbg3 { public static string Run() { var w = PA.NewW(ClassId.Warrior, 7, 16, 30); var s = SimInput.EmptyInput(); var sb = new StringBuilder();
 for (int t=0;t<6000;t++){ MatchTick.Advance(w,null,s); if (t%600==0) sb.Append(t+" phase "+w.Match.Phase+" time "+w.Match.Time.ToString("F1")+" hatched "+w.Match.Hatched+" state "+w.Kaiju.State+" k "+w.Kaiju.X.ToString("F1")+","+w.Kaiju.Z.ToString("F1")+" hp "+w.Kaiju.Hp.ToString("F0")+" ko "+w.Kaiju.KoIn.ToString("F1")+"\n"); }
 return sb.ToString(); } }
