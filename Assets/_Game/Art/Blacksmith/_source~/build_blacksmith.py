"""CM b13 r3 ART: the Blacksmith hut (levels 0-5), the ore vein (full / depleted), the carried ore lump and the hut
level-up effect pieces. Built by script on the props pipeline (blacksmith_art/core.py is props_art/core.py vendored
verbatim: palette, `Col` corner colour with mask codes in alpha, CM/Toon preview, FBX options), exported to
Assets/_Game/Art/Blacksmith/*.fbx, measured into manifest.json and rendered for review.

Run (headless only, through the machine lock):
  ~/bin/cm-heavy blender /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 \
      --python "Assets/_Game/Art/Blacksmith/_source~/build_blacksmith.py" -- [--renders DIR] [--no-renders]

Conventions (props manifest): 1 unit = 1 m; Blender Z up; pivot at the footprint centre on the ground; the front
(forge mouth, anvil) faces Blender -Y = Unity +Z, which HutView turns toward the team pad.

Meshes (ArtMapping specs; renderer names in Unity):
  BlacksmithHut.fbx      BlacksmithHut_L0 (always) + BlacksmithHut_L1..L5 (shown while hut level >= n, cumulative):
                         L0 stone plinth, timber-framed plaster walls, team-cloth gable roof with team-dark ridge,
                            stone chimney with a dim ember, glowing forge mouth, anvil on a stump, 5 empty ridge sockets
                         L1 bellows + ore bin, ridge gem 1       L2 chimney stack + bright ember, gem 2
                         L3 weapon rack, gem 3                   L4 two team banners, gem 4
                         L5 brass ridge crest + eave trim, forge and chimney flames, gem 5
  BlacksmithOreVein.fbx  BlacksmithOreVein_Full (rock outcrop, bright gold ore facets), BlacksmithOreVein_Depleted
  BlacksmithOreLump.fbx  BlacksmithOreLump (one carried lump, 0.5 u tall; CarryProp scales it like the bundle)
  BlacksmithFx.fbx       BlacksmithFx_Ring (1 m radius ring), BlacksmithFx_Spark (spark diamond); all glow, no hull
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector, noise  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
for _m in [m for m in sys.modules if m.startswith("blacksmith_art")]:
    del sys.modules[_m]
from blacksmith_art import core as C  # noqa: E402
from blacksmith_art.core import M, Prop, box, cloth, cyl, ico, jitter, torus  # noqa: E402

OUT = os.path.dirname(HERE)  # Assets/_Game/Art/Blacksmith
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDERS = os.path.expanduser(ARGV[ARGV.index("--renders") + 1]) if "--renders" in ARGV \
    else os.path.expanduser("~/cm-art-trial/b13-hut/renders")
DO_RENDERS = "--no-renders" not in ARGV

# ---------------------------------------------------------------- palette (props palette + the forge entries)
STONE_TOP, STONE_SIDE, STONE_DARK = C.STONE, "#857E6D", "#6A655A"
PLASTER, PLASTER_TOP = "#E6D8BC", "#F1E7D2"
TIMBER, TIMBER_TOP = "#6B4426", "#8A5A33"
IRON, IRON_TOP = "#4C5260", "#9AA3B3"
STEEL, STEEL_TOP = "#B9C3CF", "#E2E8EE"
SOOT = "#2B2326"
FORGE, FORGE_CORE, FORGE_DIM = "#FF9F3A", "#FFE08A", "#E88A3A"
FLAME, FLAME_CORE = "#FFB347", "#FFF1B8"
ORE, ORE_HI = "#FFC83D", "#FFE58A"
ROCK_TOP, ROCK_SIDE = "#8E92A0", "#5F6372"
SPENT_TOP, SPENT_SIDE, SPENT_CHIP = "#6E717C", "#4E515B", "#8C7A55"
CLOTH, CLOTH_SHADE, DARK, GLOW = C.TEAM_CLOTH, "#0E6299", C.TEAM_DARK, C.TEAM_GLOW
FX_RING, FX_SPARK = "#FFD66B", "#FFF1B8"
LDIR = (-0.55, -0.35, 0.76)    # render light: upper left of the gameplay view

FIX, GLOW_UNLINED, GLOW_LINED = C.MASK_FIXED, C.MASK_GLOW, C.MASK_GLOW_LINED
T_DARK, T_GLOW_LINED, T_CLOTH = C.MASK_TEAM_DARK, C.MASK_TEAM_GLOW_LINED, C.MASK_TEAM_CLOTH


def tb(top, side, mask=FIX, thr=0.55):
    """Top faces one tone lighter (art direction: tops carry the read at a 55 degree camera)."""
    return lambda f: (top if f.normal.z > thr else side, mask)


def prism(x0, x1, y0, y1, z0, yr, zr):
    """Gable-end prism along X: triangle (y0, z0) (y1, z0) (yr, zr)."""
    bm = bmesh.new()
    ends = []
    for x in (x0, x1):
        ends.append([bm.verts.new((x, y0, z0)), bm.verts.new((x, y1, z0)), bm.verts.new((x, yr, zr))])
    a, b = ends
    bm.faces.new((a[2], a[1], a[0]))
    bm.faces.new((b[0], b[1], b[2]))
    for i in range(3):
        j = (i + 1) % 3
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def diamond(r, h):
    """Four-sided gem standing on z = 0, height h."""
    bm = bmesh.new()
    mid = [bm.verts.new((r * math.cos(k * math.pi / 2), r * math.sin(k * math.pi / 2), h * 0.42)) for k in range(4)]
    lo, hi = bm.verts.new((0, 0, 0)), bm.verts.new((0, 0, h))
    for k in range(4):
        bm.faces.new((mid[k], mid[(k + 1) % 4], hi))
        bm.faces.new((mid[(k + 1) % 4], mid[k], lo))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


# ---------------------------------------------------------------- hut geometry
CY = 0.35                      # body centre in y (front wall at y = -0.525)
WALL_W, WALL_D, PLINTH_H, WALL_H = 2.3, 1.75, 0.4, 1.25
FRONT = CY - WALL_D / 2        # -0.525
BACK = CY + WALL_D / 2         # 1.225
EAVE_Z, RIDGE_Z, OVERHANG = 1.5, 2.62, 0.3
RIDGE_GEMS_X = (-1.0, -0.55, -0.1, 0.35, 0.8)
CHIM_X, CHIM_Y = 0.72, 0.86
RIDGE_TOP = RIDGE_Z + 0.27        # top of the ridge beam (the slabs meet at about RIDGE_Z + 0.19)


def roof_slab(p, sign, length, ang, thick, centre, colour, mask, width=2.75, along=0.0, lift=0.0):
    """A slab on the roof plane. sign -1 front slope (faces -Y), +1 back. along: offset down the slope from centre."""
    rot = -sign * math.degrees(ang)
    n = Vector((0, sign * math.sin(ang), math.cos(ang)))
    down = Vector((0, sign * math.cos(ang), -math.sin(ang)))
    c = Vector(centre) + n * (thick / 2 + lift) + down * along
    p.add(box(width, length, thick, bevel=min(0.03, thick * 0.3), base=False), colour, mask, mat=M(tuple(c), (rot, 0, 0)))
    return n


def hut_l0():
    p = Prop("BlacksmithHut_L0", 2200, "building", seed=11, outline_px=2.0)
    # stone plinth with a darker footing course
    p.add(box(2.7, WALL_D + 0.3, PLINTH_H, bevel=0.06), tb(STONE_TOP, STONE_SIDE), mat=M((0, CY, 0)))
    for x in (-1.12, -0.37, 0.37, 1.12):
        p.add(box(0.62, 0.05, 0.16, bevel=0.02), STONE_DARK, mat=M((x, FRONT - 0.17, 0.08)))
    # plaster walls
    p.add(box(WALL_W, WALL_D, WALL_H, bevel=0.03), tb(PLASTER_TOP, PLASTER), mat=M((0, CY, PLINTH_H)))
    # timber frame: corner posts, a belt beam, side braces
    top = PLINTH_H + WALL_H
    for x in (-WALL_W / 2, WALL_W / 2):
        for y in (FRONT, BACK):
            p.add(box(0.14, 0.14, WALL_H + 0.02, bevel=0.02), tb(TIMBER_TOP, TIMBER), mat=M((x, y, PLINTH_H)))
    for y, d in ((FRONT - 0.02, 0.06), (BACK + 0.02, 0.06)):
        p.add(box(WALL_W + 0.1, d, 0.1, bevel=0.015), tb(TIMBER_TOP, TIMBER), mat=M((0, y, top - 0.12)))
    for x in (-WALL_W / 2 - 0.02, WALL_W / 2 + 0.02):
        p.add(box(0.06, WALL_D + 0.1, 0.1, bevel=0.015), tb(TIMBER_TOP, TIMBER), mat=M((x, CY, top - 0.12)))
        p.add(box(0.06, 0.09, 1.2, bevel=0.01), TIMBER, mat=M((x, CY - 0.25, PLINTH_H + 0.52), (38, 0, 0)))
        p.add(box(0.06, 0.09, 1.2, bevel=0.01), TIMBER, mat=M((x, CY + 0.45, PLINTH_H + 0.52), (-38, 0, 0)))
    # gable ends (plaster triangles under the roof) with a king post
    p.add(prism(-WALL_W / 2, WALL_W / 2, FRONT, BACK, top, CY, RIDGE_Z - 0.08), PLASTER)
    for x in (-WALL_W / 2 - 0.02, WALL_W / 2 + 0.02):
        p.add(box(0.06, 0.1, RIDGE_Z - top - 0.1, bevel=0.01), TIMBER, mat=M((x, CY, top)))
    # forge hearth: a stone surround with a real opening, soot recess and a glowing coal bed / fire wall inside
    sx0, sw, sd, sh = 0.0, 1.1, 0.26, 1.02
    sy = FRONT - sd / 2
    hole_w, hole_h = 0.64, 0.62
    pier = (sw - hole_w) / 2
    for s in (-1, 1):
        p.add(box(pier, sd, sh, bevel=0.03), tb(STONE_TOP, STONE_SIDE), mat=M((sx0 + s * (hole_w + pier) / 2, sy, PLINTH_H)))
    p.add(box(sw + 0.08, sd + 0.04, sh - hole_h, bevel=0.035), tb(STONE_TOP, STONE_SIDE), mat=M((sx0, sy, PLINTH_H + hole_h)))
    p.add(box(0.2, 0.06, 0.24, bevel=0.02), tb("#B5AD98", STONE_TOP), mat=M((sx0, FRONT - sd - 0.02, PLINTH_H + hole_h - 0.03)))
    p.add(box(hole_w + 0.02, 0.04, hole_h, bevel=0.0), SOOT, mat=M((sx0, FRONT - 0.03, PLINTH_H)))
    p.add(box(hole_w - 0.08, 0.04, hole_h - 0.12, bevel=0.0), FORGE_DIM, GLOW_UNLINED, mat=M((sx0, FRONT - 0.06, PLINTH_H + 0.02)))
    p.add(box(hole_w - 0.04, sd - 0.06, 0.1, bevel=0.0), lambda f: (FORGE_CORE if f.normal.z > 0.5 else FORGE, GLOW_UNLINED),
          mat=M((sx0, sy + 0.02, PLINTH_H)))
    # roof: two team-cloth slabs, darker shingle courses, team-dark ridge beam
    run = (CY - (FRONT - OVERHANG))
    rise = RIDGE_Z - EAVE_Z
    ang = math.atan2(rise, run)
    length = math.hypot(run, rise) + 0.06
    roof_col = lambda f: (CLOTH, T_CLOTH) if f.normal.z > 0.3 else (DARK, T_DARK)  # noqa: E731
    for sign in (-1, 1):
        centre = (0, CY + sign * run / 2, (EAVE_Z + RIDGE_Z) / 2)
        roof_slab(p, sign, length, ang, 0.14, centre, roof_col, T_CLOTH)
        for k, along in enumerate((-0.28, 0.12, 0.5)):
            roof_slab(p, sign, 0.07, ang, 0.035, centre, CLOTH_SHADE, T_CLOTH, width=2.66, along=along, lift=0.12)
        # eave fascia board in team dark
        roof_slab(p, sign, 0.1, ang, 0.06, centre, DARK, T_DARK, width=2.8, along=length / 2 - 0.05, lift=0.1)
    p.add(box(2.9, 0.26, 0.17, bevel=0.03), tb(DARK, DARK, T_DARK), mat=M((0, CY, RIDGE_TOP - 0.17)))
    # five ridge sockets (iron cups) waiting for the level gems
    for x in RIDGE_GEMS_X:
        p.add(cyl(0.075, 0.07, segs=6, bevel=0.0), tb(IRON_TOP, IRON), mat=M((x, CY, RIDGE_TOP - 0.01)))
    # chimney (through the back slope) with a dim ember mouth
    chim_top = 3.05
    p.add(box(0.55, 0.55, chim_top - PLINTH_H, bevel=0.04), tb(STONE_TOP, STONE_SIDE), mat=M((CHIM_X, CHIM_Y, PLINTH_H)))
    p.add(box(0.68, 0.68, 0.12, bevel=0.03), tb("#B5AD98", STONE_DARK), mat=M((CHIM_X, CHIM_Y, chim_top)))
    p.add(box(0.36, 0.36, 0.02, bevel=0.0), FORGE_DIM, GLOW_UNLINED, mat=M((CHIM_X, CHIM_Y, chim_top + 0.115)))
    # anvil on a stump, out front (left of the forge mouth), with a hammer resting on it
    ax, ay = -0.62, FRONT - 0.62
    p.add(cyl(0.27, 0.34, segs=8, bevel=0.03), tb("#D9A46A", C.WOOD_B), mat=M((ax, ay, 0)))
    p.add(box(0.36, 0.26, 0.12, bevel=0.02), tb(IRON_TOP, IRON), mat=M((ax, ay, 0.34)))
    p.add(box(0.18, 0.15, 0.13, bevel=0.01), IRON, mat=M((ax, ay, 0.46)))
    p.add(box(0.6, 0.26, 0.12, bevel=0.025), tb(IRON_TOP, IRON), mat=M((ax - 0.04, ay, 0.58)))
    p.add(cyl(0.1, 0.26, segs=6, r2=0.02, bevel=0.0, base=False), tb(IRON_TOP, IRON, thr=0.3), mat=M((ax + 0.38, ay, 0.66), (0, 90, 0)))
    p.add(box(0.05, 0.34, 0.05, bevel=0.01), tb(C.WOOD, C.WOOD_B), mat=M((ax - 0.12, ay - 0.06, 0.70), (0, 0, 20)))
    p.add(box(0.15, 0.08, 0.08, bevel=0.015), tb(STEEL_TOP, STEEL), mat=M((ax - 0.06, ay + 0.1, 0.70), (0, 0, 20)))
    return p.finish()


def gem(p, i):
    x = RIDGE_GEMS_X[i]
    p.add(diamond(0.11, 0.3), lambda f: (GLOW, T_GLOW_LINED), mat=M((x, CY, RIDGE_TOP + 0.03)))


def hut_l1():
    p = Prop("BlacksmithHut_L1", 600, "building", seed=12)
    # bellows right of the hearth: wooden top board, three leather pleats, iron nozzle into the surround, handle
    bx, by = 0.86, FRONT - 0.36
    for k, (h, w, col) in enumerate(((0.05, 0.46, "#7A5434"), (0.06, 0.4, C.LEATHER), (0.05, 0.34, "#7A5434"))):
        p.add(box(w, 0.3 - 0.03 * k, h, bevel=0.015), col, mat=M((bx + 0.03 * k, by, 0.04 + 0.06 * k), (0, -6, 0)))
    p.add(box(0.54, 0.34, 0.05, bevel=0.015), tb(C.WOOD, C.WOOD_B), mat=M((bx, by, 0.0)))
    p.add(box(0.5, 0.32, 0.05, bevel=0.015), tb(C.WOOD, C.WOOD_B), mat=M((bx + 0.05, by, 0.21), (0, -10, 0)))
    p.add(cyl(0.055, 0.3, segs=6, r2=0.03, bevel=0.0, base=False), tb(IRON_TOP, IRON, thr=0.3), mat=M((bx - 0.36, by, 0.13), (0, 90, 0)))
    p.add(box(0.06, 0.06, 0.34, bevel=0.01), tb(C.WOOD, C.WOOD_B), mat=M((bx + 0.3, by, 0.22), (0, 35, 0)))
    # ore bin at the left front, heaped with bright ore (the hut eats ore: the first upgrade shows it)
    ox, oy = -1.2, FRONT - 0.58
    for dx, dy, w, d in ((0, -0.2, 0.46, 0.05), (0, 0.2, 0.46, 0.05), (-0.21, 0, 0.05, 0.36), (0.21, 0, 0.05, 0.36)):
        p.add(box(w, d, 0.24, bevel=0.012), tb(C.WOOD, C.WOOD_B), mat=M((ox + dx, oy + dy, 0)))
    for k, (dx, dy, r, z) in enumerate(((-0.08, -0.06, 0.13, 0.22), (0.09, 0.06, 0.12, 0.22), (0.0, 0.1, 0.11, 0.2),
                                        (0.08, -0.09, 0.1, 0.2), (0.0, 0.0, 0.11, 0.32))):
        p.add(jitter(ico(r, 1), 0.02, seed=k + 3), lambda f: ((ORE_HI if f.normal.z > 0.45 else ORE), GLOW_LINED),
              mat=M((ox + dx, oy + dy, z)))
    gem(p, 0)
    return p.finish()


def hut_l2():
    p = Prop("BlacksmithHut_L2", 300, "building", seed=13)
    z0, h = 3.05, 0.6
    p.add(box(0.5, 0.5, h, bevel=0.035), tb("#A39A86", STONE_DARK), mat=M((CHIM_X, CHIM_Y, z0 + 0.08)))
    p.add(box(0.56, 0.56, 0.08, bevel=0.015), tb("#E3B867", C.BRASS), mat=M((CHIM_X, CHIM_Y, z0 + 0.32)))
    p.add(box(0.64, 0.64, 0.1, bevel=0.03), tb("#B5AD98", STONE_DARK), mat=M((CHIM_X, CHIM_Y, z0 + 0.08 + h)))
    p.add(box(0.34, 0.34, 0.02, bevel=0.0), FORGE, GLOW_UNLINED, mat=M((CHIM_X, CHIM_Y, z0 + 0.08 + h + 0.095)))
    gem(p, 1)
    return p.finish()


def hut_l3():
    p = Prop("BlacksmithHut_L3", 500, "building", seed=14)
    rx = WALL_W / 2 + 0.24
    for y in (CY - 0.5, CY + 0.5):
        p.add(box(0.08, 0.08, 1.0, bevel=0.015), tb(TIMBER_TOP, TIMBER), mat=M((rx, y, 0)))
    for z in (0.3, 0.86):
        p.add(box(0.08, 1.12, 0.07, bevel=0.015), tb(TIMBER_TOP, TIMBER), mat=M((rx, CY, z)))
    # three swords and an axe leaning on the rack
    for k, y in enumerate((CY - 0.32, CY - 0.06, CY + 0.2)):
        tilt = (8, 0, -6)[k]
        mat_b = M((rx + 0.07, y, 0.06), (tilt, -10, 0))
        p.add(box(0.02, 0.1, 0.78, bevel=0.0), tb(STEEL_TOP, STEEL, thr=0.3), mat=mat_b)
        p.add(box(0.05, 0.26, 0.05, bevel=0.01), tb("#E3B867", C.BRASS), mat=M((rx + 0.07 - 0.14 * math.sin(math.radians(10)), y, 0.8), (tilt, -10, 0)))
        p.add(box(0.04, 0.04, 0.18, bevel=0.0), C.LEATHER, mat=M((rx + 0.07 - 0.15 * math.sin(math.radians(10)), y, 0.84), (tilt, -10, 0)))
    ay = CY + 0.42
    p.add(box(0.04, 0.04, 0.9, bevel=0.0), tb(C.WOOD, C.WOOD_B), mat=M((rx + 0.08, ay, 0.0), (0, -12, 0)))
    p.add(box(0.04, 0.24, 0.18, bevel=0.012), tb(STEEL_TOP, STEEL, thr=0.3), mat=M((rx + 0.08 - 0.16, ay + 0.08, 0.72), (0, -12, 0)))
    gem(p, 2)
    return p.finish()


def hut_l4():
    p = Prop("BlacksmithHut_L4", 700, "building", seed=15)
    for s in (-1, 1):
        bx, by = s * 1.5, FRONT - 0.3
        p.add(cyl(0.045, 2.3, segs=6, bevel=0.0), tb(TIMBER_TOP, TIMBER), mat=M((bx, by, 0)))
        p.add(box(0.56, 0.06, 0.06, bevel=0.012), tb(TIMBER_TOP, TIMBER), mat=M((bx, by - 0.02, 2.12)))
        p.add(ico(0.075, 1), tb("#E3B867", C.BRASS), mat=M((bx, by, 2.36)))
        sway = s * 0.03
        p.add(cloth(0.5, 0.95, 3, 5, offset_fn=lambda u, v, sw=sway: sw * math.sin(u * math.pi) - 0.04 * v, notch=0.16),
              lambda f: (CLOTH, T_CLOTH) if abs(f.normal.y) > 0.4 else (DARK, T_DARK), mat=M((bx, by - 0.07, 2.1)))
        # cream hammer emblem on the banner face
        p.add(box(0.06, 0.03, 0.32, bevel=0.0), C.CREAM, mat=M((bx, by - 0.125, 1.36)))
        p.add(box(0.24, 0.03, 0.1, bevel=0.0), C.CREAM, mat=M((bx, by - 0.125, 1.66)))
    gem(p, 3)
    return p.finish()


def hut_l5():
    p = Prop("BlacksmithHut_L5", 700, "building", seed=16)
    # brass crest: ridge-end anvil finials and a brass trim along both eaves
    for x in (-1.45, 1.45):
        p.add(box(0.16, 0.16, 0.2, bevel=0.02), tb("#E3B867", C.BRASS), mat=M((x, CY, RIDGE_TOP - 0.02)))
        p.add(box(0.3, 0.16, 0.08, bevel=0.02), tb("#F2CD7E", C.BRASS), mat=M((x, CY, RIDGE_TOP + 0.18)))
    # hanging brass anvil sign on a timber bracket above the forge mouth
    p.add(box(0.06, 0.5, 0.06, bevel=0.01), tb(TIMBER_TOP, TIMBER), mat=M((-0.75, FRONT - 0.25, 1.42)))
    for dx in (-0.12, 0.12):
        p.add(box(0.015, 0.015, 0.12, bevel=0.0), IRON, mat=M((-0.75 + dx, FRONT - 0.42, 1.3)))
    p.add(box(0.42, 0.05, 0.12, bevel=0.015), tb("#F2CD7E", C.BRASS), mat=M((-0.75, FRONT - 0.42, 1.18)))
    p.add(box(0.18, 0.05, 0.1, bevel=0.01), tb("#F2CD7E", C.BRASS), mat=M((-0.75, FRONT - 0.42, 1.08)))
    p.add(box(0.32, 0.05, 0.06, bevel=0.01), tb("#F2CD7E", C.BRASS), mat=M((-0.75, FRONT - 0.42, 1.03)))
    # forge flames licking out of the mouth, chimney flame above the L2 stack
    for k, (dx, h, r) in enumerate(((-0.16, 0.42, 0.11), (0.03, 0.56, 0.13), (0.19, 0.38, 0.1))):
        p.add(cyl(r, h, segs=5, r2=0.015, bevel=0.0), lambda f: (FLAME_CORE if f.normal.z > 0.3 else FLAME, GLOW_UNLINED),
              mat=M((dx, FRONT - 0.2, PLINTH_H + 0.08), (-14, 0, k * 20)))
    for k, (dx, dy, h) in enumerate(((0.0, 0.0, 0.34), (0.1, -0.07, 0.24), (-0.09, 0.08, 0.26))):
        p.add(cyl(0.15 - 0.02 * k, h, segs=5, r2=0.015, bevel=0.0), lambda f: (FLAME_CORE if f.normal.z > 0.3 else FLAME, GLOW_UNLINED),
              mat=M((CHIM_X + dx, CHIM_Y + dy, 3.05 + 0.08 + 0.6 + 0.1), (0, 0, k * 30)))
    gem(p, 4)
    return p.finish()


# ---------------------------------------------------------------- ore vein, lump, fx
def rock(r, sx, sy, sz, seed):
    return jitter(ico(r, 1), r * 0.14, freq=2.2, seed=seed)


def vein_full():
    p = Prop("BlacksmithOreVein_Full", 700, "prop", seed=21)
    rc = tb(ROCK_TOP, ROCK_SIDE, thr=0.45)
    p.add(rock(0.5, 1, 1, 1, 1), rc, mat=M((0, 0, 0.26), (0, 0, 20), (1.2, 1.0, 0.85)))
    p.add(rock(0.36, 1, 1, 1, 2), rc, mat=M((0.55, 0.28, 0.16), (0, 0, -15), (1.0, 1.1, 0.8)))
    p.add(rock(0.32, 1, 1, 1, 3), rc, mat=M((-0.5, -0.24, 0.12), (0, 0, 40), (1.1, 0.95, 0.75)))
    facets = ((0.08, -0.12, 0.62, -18, 12, 0.14, 0.42), (-0.22, 0.12, 0.52, 22, -24, 0.12, 0.36),
              (0.3, -0.24, 0.42, -30, 30, 0.11, 0.3), (0.62, 0.2, 0.36, 10, 36, 0.1, 0.28),
              (-0.6, -0.3, 0.26, -24, -30, 0.1, 0.26), (0.18, 0.32, 0.5, 30, 10, 0.1, 0.3),
              (-0.32, -0.4, 0.3, -36, -8, 0.09, 0.22))
    for x, y, z, rx, ry, r, h in facets:
        p.add(cyl(r * 1.25, h * 0.72, segs=6, r2=r * 0.6, bevel=0.0), lambda f: ((ORE_HI if f.normal.z > 0.35 else ORE), GLOW_LINED),
              mat=M((x, y, z - 0.08), (rx, ry, 0)))
    return p.finish()


def vein_depleted():
    p = Prop("BlacksmithOreVein_Depleted", 500, "prop", seed=22)
    rc = tb(SPENT_TOP, SPENT_SIDE, thr=0.45)
    p.add(rock(0.42, 1, 1, 1, 4), rc, mat=M((0, 0, 0.1), (0, 0, 20), (1.2, 1.0, 0.5)))
    p.add(rock(0.28, 1, 1, 1, 5), rc, mat=M((0.5, 0.24, 0.06), (0, 0, -15), (1.0, 1.1, 0.5)))
    p.add(rock(0.24, 1, 1, 1, 6), rc, mat=M((-0.46, -0.22, 0.05), (0, 0, 40), (1.1, 0.95, 0.5)))
    for k, (x, y) in enumerate(((0.15, -0.5), (-0.2, 0.42), (0.6, -0.2), (-0.7, 0.1))):
        p.add(jitter(ico(0.07, 1), 0.01, seed=k), tb(SPENT_TOP, SPENT_CHIP), mat=M((x, y, 0.03)))
    return p.finish()


def ore_lump():
    p = Prop("BlacksmithOreLump", 260, "prop", seed=23)
    p.add(jitter(ico(0.2, 1), 0.035, seed=7), lambda f: ((ORE_HI if f.normal.z > 0.4 else ORE), GLOW_LINED),
          mat=M((0, 0, 0.2), (0, 0, 15), (1.15, 1.0, 0.95)))
    for x, y, z, r, sd in ((0.15, -0.08, 0.12, 0.1, 1), (-0.14, 0.1, 0.1, 0.09, 2), (0.02, 0.15, 0.3, 0.08, 3)):
        p.add(jitter(ico(r, 1), 0.015, seed=sd), tb(ROCK_TOP, ROCK_SIDE, thr=0.4), mat=M((x, y, z)))
    return p.finish(fit_height=0.5)


def fx_ring():
    p = Prop("BlacksmithFx_Ring", 300, "fx", seed=31, outline_px=0.0)
    p.add(torus(1.0, 0.07, segs=32, sides=4), FX_RING, GLOW_UNLINED, mat=M((0, 0, 0.035), (0, 0, 0), (1, 1, 0.5)))
    return p.finish()


def fx_spark():
    p = Prop("BlacksmithFx_Spark", 40, "fx", seed=32, outline_px=0.0)
    p.add(diamond(0.07, 0.24), FX_SPARK, GLOW_UNLINED, mat=M((0, 0, -0.12)))
    return p.finish()


# ---------------------------------------------------------------- export + measure
def export_many(path, obs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    res = bpy.ops.export_scene.fbx(filepath=path, **C.FBX_OPTS)
    if "FINISHED" not in res:
        raise SystemExit(f"FBX export failed for {path}: {res}")


def dims(ob):
    vs = [v.co for v in ob.data.vertices]
    lo = [min(v[i] for v in vs) for i in range(3)]
    hi = [max(v[i] for v in vs) for i in range(3)]
    return lo, hi


def codes(ob):
    return sorted({round(c.color[3] * 8) for c in ob.data.color_attributes["Col"].data})


# ---------------------------------------------------------------- renders
def setup_render_scene():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    try:
        sc.eevee.taa_render_samples = 8
    except AttributeError:
        pass
    sc.view_settings.view_transform = "Standard"
    sc.render.film_transparent = False
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = C.lin("#BFDCEB") + (1.0,)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    sc.world = world
    ldir = Vector(LDIR).normalized()
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.0
    sun.use_shadow = True
    try:
        sun.angle = math.radians(1.0)
    except AttributeError:
        pass
    so = bpy.data.objects.new("Sun", sun)
    so.rotation_euler = ldir.to_track_quat("Z", "Y").to_euler()
    sc.collection.objects.link(so)
    # ground: paved yard on grass, flat toon (shadow catcher)
    g = Prop("Ground", 9999, "ground", seed=40)
    g.add(cyl(6.0, 0.02, segs=24, bevel=0.0), "#A3C150", mat=M((0, 0, -0.02)))
    g.add(cyl(2.9, 0.02, segs=20, bevel=0.0), "#D8C29A", mat=M((0, 0.1, -0.015)))
    gob = g.finish()
    C.apply_toon(gob, C.make_toon("CM_Ground", flat=True), 0.0)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle = math.radians(35)
    cam.data.clip_start = 0.05
    cam.data.clip_end = 200
    return sc, cam, gob


def aim(cam, target, el, az, dist):
    el, az = math.radians(el), math.radians(az)
    d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def bounds(obs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in obs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    return lo, hi


def render(sc, path, res):
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def sheet(paths, cols, out):
    import numpy as np
    ims = []
    for pth in paths:
        im = bpy.data.images.load(pth)
        w, h = im.size
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        ims.append(a)
        bpy.data.images.remove(im)
    h, w = ims[0].shape[:2]
    rows = (len(ims) + cols - 1) // cols
    pad = 6
    S = np.ones(((h + pad) * rows + pad, (w + pad) * cols + pad, 4), dtype=np.float32)
    S[..., :3] = 0.1
    for i, a in enumerate(ims):
        r, c = divmod(i, cols)
        y0 = S.shape[0] - (r + 1) * (h + pad)        # images are bottom-up
        x0 = pad + c * (w + pad)
        S[y0:y0 + h, x0:x0 + w] = a
    img = bpy.data.images.new("sheet", S.shape[1], S.shape[0], alpha=True)
    img.pixels.foreach_set(S.ravel())
    img.filepath_raw = out
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def do_renders(hut, vein, lump, fx):
    os.makedirs(RENDERS, exist_ok=True)
    sc, cam, ground = setup_render_scene()
    toon = {"azure": C.make_toon("CM_Toon"), "crimson": C.make_toon("CM_Toon_Crimson", crimson=True)}
    C.set_ldir(tuple(Vector(LDIR).normalized()))      # after every toon material exists (ground included)
    every = hut + vein + [lump] + fx
    out = {"gameplay": {}, "close": {}}
    for team, mat in toon.items():
        for o in every:
            C.apply_toon(o, mat, 0.0 if o.name.startswith("BlacksmithFx") else 0.022)
        for o in every:
            o.hide_render = True
        for lvl in range(6):
            for i, o in enumerate(hut):
                o.hide_render = i > lvl
            lo, hi = bounds(hut[:lvl + 1])
            c = (lo + hi) / 2
            R = (hi - lo).length / 2
            p1 = os.path.join(RENDERS, f"hut-{team}-L{lvl}-gameplay.png")
            aim(cam, (c.x, c.y, c.z * 0.6), 55, 45, R * 1.5 / math.sin(math.radians(17.5)))
            render(sc, p1, (640, 360))
            p2 = os.path.join(RENDERS, f"hut-{team}-L{lvl}-close.png")
            aim(cam, (c.x, c.y, c.z), 26, 32, R * 1.02 / math.sin(math.radians(17.5)))
            render(sc, p2, (480, 480))
            out["gameplay"].setdefault(team, []).append(p1)
            out["close"].setdefault(team, []).append(p2)
        sheet(out["gameplay"][team], 3, os.path.join(RENDERS, f"sheet-hut-{team}-gameplay.png"))
        sheet(out["close"][team], 3, os.path.join(RENDERS, f"sheet-hut-{team}-close.png"))
    # ore vein full / depleted side by side, the lump, the fx pieces (azure material; none carry team codes)
    for o in every:
        C.apply_toon(o, toon["azure"], 0.0 if o.name.startswith("BlacksmithFx") else 0.018)
        o.hide_render = True
    vein[0].location = (-0.9, 0, 0)
    vein[1].location = (0.9, 0, 0)
    lump.location = (0, -1.0, 0)
    for o in vein + [lump]:
        o.hide_render = False
    bpy.context.view_layer.update()
    aim(cam, (0, -0.3, 0.3), 55, 45, 6.5)
    render(sc, os.path.join(RENDERS, "ore-vein-full-depleted-lump-gameplay.png"), (640, 360))
    aim(cam, (0, -0.3, 0.3), 28, 30, 4.2)
    render(sc, os.path.join(RENDERS, "ore-vein-full-depleted-lump-close.png"), (640, 480))
    for o in vein + [lump]:
        o.hide_render = True
    lump.location = (0, 0, 0)
    lump.hide_render = False
    aim(cam, (0, 0, 0.25), 35, 30, 1.7)
    render(sc, os.path.join(RENDERS, "ore-lump-close.png"), (400, 400))
    lump.hide_render = True
    # the level-up effect on the L2 hut, ring at 1.5 m with a spark burst (one still of the pulse)
    for i, o in enumerate(hut):
        o.hide_render = i > 2
    fx[0].scale = (1.5, 1.5, 1.0)
    fx[0].hide_render = False
    sparks = [fx[1]]
    for k in range(9):
        s = fx[1].copy()
        sc.collection.objects.link(s)
        sparks.append(s)
    for k, s in enumerate(sparks):
        a = k * 2 * math.pi / len(sparks)
        s.location = (0.9 * math.cos(a), -0.6 + 0.9 * math.sin(a) * 0.6, 1.0 + 0.6 * ((k * 7) % 5) / 5)
        s.rotation_euler = (0.4 * math.cos(a), 0.4 * math.sin(a), 0)
        s.hide_render = False
    bpy.context.view_layer.update()
    aim(cam, (0, 0.2, 1.0), 55, 45, 11.0)
    render(sc, os.path.join(RENDERS, "hut-level-up-pulse-gameplay.png"), (640, 360))
    # silhouette check: black fill of every level at the gameplay angle
    sil = bpy.data.materials.new("Sil")
    sil.use_nodes = True
    sil.node_tree.nodes.clear()
    em = sil.node_tree.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (0, 0, 0, 1)
    ou = sil.node_tree.nodes.new("ShaderNodeOutputMaterial")
    sil.node_tree.links.new(em.outputs[0], ou.inputs["Surface"])
    for o in sparks + [fx[0], ground]:
        o.hide_render = True
    sil_paths = []
    for o in hut:
        C.apply_toon(o, sil, 0.0)
    for lvl in (0, 5):
        for i, o in enumerate(hut):
            o.hide_render = i > lvl
        lo, hi = bounds(hut[:lvl + 1])
        c = (lo + hi) / 2
        R = (hi - lo).length / 2
        pth = os.path.join(RENDERS, f"hut-silhouette-L{lvl}.png")
        aim(cam, (c.x, c.y, c.z * 0.6), 55, 45, R * 1.3 / math.sin(math.radians(17.5)))
        render(sc, pth, (320, 320))
        sil_paths.append(pth)
    return out


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    noise.seed_set(1311)          # rock jitter: same mesh every run (the noise tables are otherwise seeded per session)
    col = bpy.data.collections.new("Blacksmith")
    bpy.context.scene.collection.children.link(col)
    hut = [hut_l0(), hut_l1(), hut_l2(), hut_l3(), hut_l4(), hut_l5()]
    vein = [vein_full(), vein_depleted()]
    lump = ore_lump()
    fx = [fx_ring(), fx_spark()]
    groups = (("BlacksmithHut", hut), ("BlacksmithOreVein", vein), ("BlacksmithOreLump", [lump]), ("BlacksmithFx", fx))
    rows = []
    for fname, obs in groups:
        for o in obs:
            for c in o.users_collection:
                c.objects.unlink(o)
            col.objects.link(o)
            o.data.name = o.name
        export_many(os.path.join(OUT, fname + ".fbx"), obs)
        for o in obs:
            lo, hi = dims(o)
            rows.append(dict(fbx=f"Blacksmith/{fname}.fbx", mesh=o.name, tris=C.tri_count(o), budget=o["budget"],
                             min_xyz=[round(v, 3) for v in lo], max_xyz=[round(v, 3) for v in hi],
                             height=round(hi[2] - min(0.0, lo[2]), 3), mask_codes=codes(o), outline_px=o["outline_px"]))
    hut_total = sum(r["tris"] for r in rows if r["mesh"].startswith("BlacksmithHut"))
    man = dict(generated_by="build_blacksmith.py", blender=bpy.app.version_string, unit="1 unit = 1 m",
               pivot="footprint centre on the ground (z = 0); front faces Blender -Y = Unity +Z",
               colour="vertex colour set `Col` (props pipeline): RGB = hex (sRGB), A = mask code in eighths",
               hut_tris_all_levels=hut_total, meshes=rows)
    with open(os.path.join(HERE, "manifest.json"), "w") as f:
        json.dump(man, f, indent=1)
    for r in rows:
        flag = "" if r["tris"] <= r["budget"] else "  OVER BUDGET"
        print(f"ROW {r['mesh']:28s} tris {r['tris']:5d}/{r['budget']:<5d} h {r['height']:.3f} min {r['min_xyz']} max {r['max_xyz']} codes {r['mask_codes']}{flag}")
    print(f"HUT TOTAL tris {hut_total}")
    if DO_RENDERS:
        do_renders(hut, vein, lump, fx)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(RENDERS, "blacksmith.blend"), compress=True)
    print("BUILD DONE")


main()
