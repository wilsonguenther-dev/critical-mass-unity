# Level-of-detail switches shared by every class kit (lib.py, demolib.py, build_builder.py).
# CM_LOD = Hero | LOD0 | LOD1 (env). The kits call these from ring_loft / ellipsoid / box / emit, so ONE build script
# produces every level: fewer segments (more kept on head, headgear, hands and tools), tiny and thin parts dropped.
import os, inspect
import numpy as np

LOD = os.environ.get("CM_LOD", "Hero")
# Kaiju (5 m creature): thresholds scaled ~2.4x the 2.2 m classes
CFG = {"Hero": dict(s=1.0, hi=1.0, mind=0.0, thin=0.0, bevel=True, rings=0, minbig=0),
       "LOD0": dict(s=0.5, hi=0.7, mind=0.10, thin=0.04, bevel=True, rings=6, minbig=10),
       "LOD1": dict(s=0.3, hi=0.4, mind=0.34, thin=0.12, bevel=False, rings=3, minbig=6)}[LOD]
# lines of the build scripts that mention these words keep the higher segment scale (silhouette + identity areas)
HI_WORDS = ('"Head"', '"Jaw"', "CREST", "HI(")
# palette names never dropped at LOD0 (the face must survive), and at LOD1 only the eyes
KEEP0 = {"eye", "socket", "teeth", "nostril", "mouth"}
KEEP1 = {"eye"}
DROPPED = {}
FORCE_HERO = False   # set by a build script around parts that must keep full detail at every level (e.g. a grip)

def _hi():
    if LOD == "Hero": return False
    if FORCE_HERO: return True
    for fr in inspect.stack()[2:12]:
        fn = os.path.basename(fr.filename)
        if fn.startswith("build_") and fr.code_context:
            line = fr.code_context[0]
            return any(w in line for w in HI_WORDS)
    return False

def scale():
    return CFG["hi"] if _hi() else CFG["s"]

def seg(n):
    """Angular segments of a loft ring."""
    if LOD == "Hero" or n <= 4 or FORCE_HERO: return n
    s = scale(); m = max(4, int(round(n * s)))
    if n >= 12: m = max(m, CFG["minbig"])   # body masses keep a round silhouette
    if m % 2: m += 1          # even: symmetric about the character's centre line
    return min(m, n)

def rings(rs):
    """Drop every other interior ring of long lofts at LOD1."""
    if FORCE_HERO: return rs
    if not CFG["rings"] or len(rs) <= CFG["rings"]: return rs
    keep = [rs[0]] + [r for i, r in enumerate(rs[1:-1], 1) if i % 2 == 0] + [rs[-1]]
    return keep

def sphere(seg_, rings_):
    if LOD == "Hero" or FORCE_HERO: return seg_, rings_
    s = scale()
    return max(5, int(round(seg_ * s))), max(3, int(round(rings_ * s)))

def bevel_ok(size):
    if LOD == "Hero" or FORCE_HERO: return True
    if LOD == "LOD0": return min(size) >= 0.06
    return False

def keep(bm, col):
    """False when a primitive is too small or too thin to read at this level."""
    if LOD == "Hero" or FORCE_HERO: return True
    if col in (KEEP0 if LOD == "LOD0" else KEEP1): return True
    P = np.array([v.co[:] for v in bm.verts])
    if len(P) < 4: return False
    C = np.cov((P - P.mean(0)).T)
    w, V = np.linalg.eigh(C)
    ext = sorted(float(np.ptp(P @ V[:, i])) for i in range(3))
    ok = ext[2] >= CFG["mind"] and ext[1] >= CFG["thin"]
    if not ok: DROPPED[col] = DROPPED.get(col, 0) + 1
    return ok
