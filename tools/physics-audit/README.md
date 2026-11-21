Physics audit 2026-11-21 scratch tools (Editor-only, outside Assets/, never compiled into the game or a player).
Run against the open Editor: `~/cm-editor-eyes/bin/u 300 script run -- --file PA.cs --entry PA.Movement` (entries: PA.Movement,
PA.Collision, PACrowd.Run [seed, ticks], PAV.Vertical, PAV.Hits, ...). overlap-probe.cs is appended to PA.cs to run (entry Dbg2.Run).
Results: ~/cm-incidents/physics-audit-2026-11-21/ (PHYSICS-AUDIT.md, data/).
