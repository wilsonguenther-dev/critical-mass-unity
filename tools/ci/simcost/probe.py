#!/usr/bin/env python3
"""b14 sim-cost harness (CI only): write Assets/_Game/Sim/Core/SimCostProbe.cs into a tree and, with --patch, wrap the
per-tick systems (MatchTick.Advance sections, every SimStep.Step system line) and a list of nested methods with timing
probes. Timing only: the probes read a clock and write their own arrays, never Sim state."""
import re, sys, os, glob

TOP = []      # names of top-level (partition) probes
NEST = []     # names of nested inclusive probes

# (file glob relative to Assets/_Game/Sim, Class label, method name)
NESTED = [
    ("Grid/FlowField.cs", "FlowField", "ComputeFlowField"),
    ("Bots/BotFields.cs", "BotFields", "BuildField"),
    ("Terraform.cs", "Terraform", "CellsKeepRoutes"),
    ("Terraform.cs", "Terraform", "PadsReachHill"),
    ("Terraform.cs", "Terraform", "TerraformSystem"),
    ("Terraform.cs", "Terraform", "DigRefusal"),
    ("Kaiju.RouteGuard.cs", "Kaiju", "TentativeKeepsKaijuRoute"),
    ("Kaiju.RouteGuard.cs", "Kaiju", "Connected"),
    ("Kaiju.Hatch.cs", "Kaiju", "RefreshClearance"),
    ("Kaiju.Hatch.cs", "Kaiju", "BuildRegions"),
    ("Kaiju.Ai.cs", "Kaiju", "KaijuStep"),
    ("Collide.cs", "Collide", "KaijuDepenetrate"),
    ("Collide.cs", "Collide", "KaijuBody"),
    ("Collide.cs", "Collide", "SeparateActors"),
    ("Demolitions.cs", "Demolitions", "BlowBomb"),
    ("Bots/BotReflex.cs", "BotReflex", "Fill"),
    ("Bots/BotFsm.cs", "BotFsm", "Think"),
    ("Bots/BotSteer.cs", "BotSteer", "Steer"),
    ("Barricades.cs", "Barricades", "BarricadesSystem"),
    ("Topple.cs", "Topple", "ToppleSystem"),
    ("B13/BuilderWalls.cs", "BuilderWalls", "WallsSystem"),
    ("Movement.cs", "Movement", "MoveActorsPass"),
]

SIG = re.compile(r'^(?P<ind>[ \t]*)(?P<mods>(?:(?:public|internal|private|protected|static|unsafe)\s+)+)(?P<ret>[\w\.\[\]<>, ]+?)\s+(?P<name>%s)\((?P<args>[^)]*)\)')

def split_args(a):
    out, depth, cur = [], 0, ''
    for ch in a:
        if ch in '<[(': depth += 1
        if ch in '>])': depth -= 1
        if ch == ',' and depth == 0: out.append(cur); cur = ''
        else: cur += ch
    if cur.strip(): out.append(cur)
    return [x.strip() for x in out]

def call_args(a):
    res = []
    for p in split_args(a):
        p = p.split('=')[0].strip()
        toks = p.split()
        name = toks[-1]
        mod = toks[0] if toks[0] in ('out', 'ref', 'in') else ''
        res.append((mod + ' ' + name).strip())
    return ', '.join(res)

def wrap_method(src, name, pid):
    rx = re.compile(SIG.pattern % re.escape(name), re.M)
    out, pos, n = [], 0, 0
    for m in rx.finditer(src):
        if 'static' not in m.group('mods'): continue
        ret = m.group('ret').strip()
        ind = m.group('ind')
        mods = m.group('mods')
        args = m.group('args')
        callee = name + '__p'
        body = (f'{ind}{mods}{ret} {name}({args}) {{ CriticalMass.Sim.Core.SimCostProbe.B({pid}); try {{ '
                + ('' if ret == 'void' else 'return ') + f'{callee}({call_args(args)}); }} finally {{ CriticalMass.Sim.Core.SimCostProbe.E({pid}); }} }}\n')
        out.append(src[pos:m.start()]); out.append(body)
        out.append(m.group(0).replace(f' {name}(', f' {callee}(', 1).replace('public ', 'private ', 1).replace('internal ', 'private ', 1))
        pos = m.end(); n += 1
    out.append(src[pos:])
    return ''.join(out), n

def method_span(src, sig_start):
    i = src.index('{', sig_start); d = 0
    for j in range(i, len(src)):
        if src[j] == '{': d += 1
        elif src[j] == '}':
            d -= 1
            if d == 0: return i, j
    raise SystemExit('unbalanced')

def tid(name):
    TOP.append(name); return len(TOP) - 1

def patch_simstep(path):
    s = open(path).read()
    k = s.index('public static void Step(World w, double dt)')
    a, b = method_span(s, k)
    body = s[a:b]
    lines = body.split('\n'); res = []
    rx = re.compile(r'^(\s+)((?:[A-Z]\w*)(?:\.[A-Z]\w*)+)\((w(?:\.\w+)?(?:, [^;]*)?)\);(.*)$')
    for ln in lines:
        m = rx.match(ln)
        if m and not m.group(2).startswith('SimProfile') and not m.group(2).startswith('SimCostProbe'):
            p = tid('step:' + m.group(2))
            res.append(f'{m.group(1)}CriticalMass.Sim.Core.SimCostProbe.T({p}); {m.group(2)}({m.group(3)});{m.group(4)}')
        else: res.append(ln)
    # close the last section before the trailing bookkeeping
    s = s[:a] + '\n'.join(res) + s[b:]
    open(path, 'w').write(s)

def patch_matchtick(path):
    s = open(path).read()
    k = s.index('public static void Advance(World w, InputFrame humanFrame, InputFrame scratch)')
    a, b = method_span(s, k)
    body = s[a:b]
    rep = [('BotReflex.BeginFills(w);', 'tick:BotReflex.BeginFills'), ('// (1)', 'tick:actor0 fill+apply'),
           ('// (2)', 'tick:BotBrain.BotsThink'), ('// (3)', 'tick:bots fill+apply'), ('BotReflex.EndFills(w);', 'tick:BotReflex.EndFills'),
           ('// (4)', 'step:prologue')]
    for needle, nm in rep:
        if needle in body:
            p = tid(nm)
            body = body.replace(needle, f'CriticalMass.Sim.Core.SimCostProbe.T({p}); ' + needle, 1)
    s = s[:a] + body + s[b:]
    open(path, 'w').write(s)

def main():
    tree = sys.argv[1]; patch = '--patch' in sys.argv
    sim = os.path.join(tree, 'Assets/_Game/Sim')
    if patch:
        patch_matchtick(os.path.join(sim, 'Bots/MatchTick.cs'))
        patch_simstep(os.path.join(sim, 'Core/SimStep.cs'))
        for rel, cls, meth in NESTED:
            f = os.path.join(sim, rel)
            if not os.path.exists(f): continue
            pid = len(NEST)
            src, n = wrap_method(open(f).read(), meth, pid)
            if n:
                NEST.append(f'{cls}.{meth}'); open(f, 'w').write(src)
    names_top = ', '.join('"%s"' % x for x in TOP); names_nest = ', '.join('"%s"' % x for x in NEST)
    open(os.path.join(sim, 'Core/SimCostProbe.cs'), 'w').write(TEMPLATE.replace('@TOP@', names_top).replace('@NEST@', names_nest))
    print(f'probe: {tree} patch={patch} top={len(TOP)} nested={len(NEST)} [{", ".join(NEST)}]')

TEMPLATE = r'''// b14 sim-cost harness, CI only (generated by tools/ci/simcost/probe.py; never merged). Timing only: no Sim state.
using System.Diagnostics;
namespace CriticalMass.Sim.Core
{
    public static class SimCostProbe
    {
        public static readonly string[] TopNames = { @TOP@ };
        public static readonly string[] NestNames = { @NEST@ };
        /// <summary>Per-tick accumulators (the bench reads and clears them after every tick).</summary>
        public static readonly long[] Top = new long[TopNames.Length + 1];
        public static readonly long[] Nest = new long[NestNames.Length + 1];
        public static readonly int[] NestCalls = new int[NestNames.Length + 1];
        static readonly long[] T0 = new long[NestNames.Length + 1];
        static readonly int[] Depth = new int[NestNames.Length + 1];
        static int cur = -1; static long curT;
        /// <summary>Top-level section boundary: closes the running section, opens section k.</summary>
        public static void T(int k) { long t = Stopwatch.GetTimestamp(); if (cur >= 0) Top[cur] += t - curT; cur = k; curT = t; }
        /// <summary>Close the running top section (the bench calls it after each Advance).</summary>
        public static void Close() { long t = Stopwatch.GetTimestamp(); if (cur >= 0) Top[cur] += t - curT; cur = -1; }
        public static void B(int k) { NestCalls[k]++; if (Depth[k]++ == 0) T0[k] = Stopwatch.GetTimestamp(); }
        public static void E(int k) { if (--Depth[k] == 0) Nest[k] += Stopwatch.GetTimestamp() - T0[k]; }
        public static void Clear() { System.Array.Clear(Top, 0, Top.Length); System.Array.Clear(Nest, 0, Nest.Length); System.Array.Clear(NestCalls, 0, NestCalls.Length); }
    }
}
'''

if __name__ == '__main__': main()
