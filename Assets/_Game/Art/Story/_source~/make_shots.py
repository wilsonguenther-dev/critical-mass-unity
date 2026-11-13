#!/usr/bin/env python3
"""Writes the motion-comic shot table (Assets/_Game/Resources/Story/motion-comic.json) with every field filled in.
The JSON is the source of truth the game plays; retune timing by editing it directly or by editing this file and
re-running it. Units: seconds; stage units on a 1920 x 1080 stage, origin at the centre, y UP. depth 0 = pinned to the
screen, 1 = moves fully with the camera (parallax). Eases: inOutSine outSine inSine outCubic inCubic inOutCubic outExpo
inExpo outBack inBack step linear (linear only for cross-shot holds)."""
import json, os
HERE = os.path.dirname(os.path.abspath(__file__))
STORY = os.path.normpath(os.path.join(HERE, "..", "..", "..", "Resources", "Story"))
GEO = json.load(open(os.path.join(HERE, "painted-geometry.json"))) if os.path.exists(os.path.join(HERE, "painted-geometry.json")) else {}
def ASPECT(name):
    """height / width of a layer texture (png or jpg), so layouts can be set by height."""
    from PIL import Image
    for ext in (".png", ".jpg"):
        p = os.path.join(STORY, name + ext)
        if os.path.exists(p):
            w, h = Image.open(p).size; return h / w
    return 1.0
def WH(name, height): return round(height / ASPECT(name))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "..", "Resources", "Story", "motion-comic.json"))
CREAM, NAVY, BRASS, BIOMASS, CORR = "#F7F0E5", "#18233B", "#C79245", "#B7F238", "#B98AF0"

def K(t, x=0, y=0, s=1, a=1, r=0, b=0, ease="inOutSine"): return dict(t=t, x=x, y=y, s=s, a=a, r=r, b=b, ease=ease)
def C(t, zoom=1, zx=0, zy=0, dolly=1, x=0, y=0, rot=0, ease="inOutSine"): return dict(t=t, zoom=zoom, zx=zx, zy=zy, dolly=dolly, x=x, y=y, rot=rot, ease=ease)
def L(tex, depth, w, keys, h=0, flip=False, tint="#FFFFFF", blur="", frm=0, to=999, frames=None, frameAt=0, fps=12, flicker=False,
      pulseHz=0, pulseLo=1, jitter=0, jitterFrom=0, jitterTo=0):
    return dict(tex=tex, depth=depth, w=w, h=h, flip=flip, tint=tint, blur=blur, **{"from": frm}, to=to, frames=frames or [],
                frameAt=frameAt, fps=fps, flicker=flicker, pulseHz=pulseHz, pulseLo=pulseLo, jitter=jitter, jitterFrom=jitterFrom,
                jitterTo=jitterTo, keys=keys)
def P(tex, color, t0, t1, count, x, y, sx, sy, vx, vy, jx, jy, g=0, life=3, size=14, loop=True, depth=0.6):
    return dict(tex=tex, color=color, t0=t0, t1=t1, count=count, x=x, y=y, sx=sx, sy=sy, vx=vx, vy=vy, jx=jx, jy=jy, g=g, life=life,
                size=size, loop=loop, depth=depth)
def T(text, at, out, size=96, color=CREAM, x=0, y=300, font="display", track0=36, track1=10): return dict(text=text, at=at, out=out, size=size, color=color, x=x, y=y, font=font, track0=track0, track1=track1)
def F(t, dur, peak, color="#FFFFFF"): return dict(t=t, dur=dur, peak=peak, color=color)
def S(t, dur, amp): return dict(t=t, dur=dur, amp=amp)
def bars(*kv): return [dict(t=t, v=v, ease=e) for t, v, e in kv]
def shot(id, seconds, beat, layers, cam, titles=(), sub=None, vo=(), sfx=(), flash=(), shake=(), particles=(), bars_=None, boardAt=None):
    return dict(id=id, seconds=seconds, beat=beat, boardAt=boardAt if boardAt is not None else round(seconds * 0.6, 2), layers=layers, cam=cam,
                titles=list(titles), sub=sub or dict(text="", at=0, out=0), vo=[dict(clip=c, at=a) for c, a in vo],
                sfx=[dict(cue=c, at=a) for c, a in sfx], flash=list(flash), shake=list(shake), particles=list(particles),
                bars=bars_ or bars((0, 1, "linear")))

SKY = 2300
crack = ["crack_%d" % i for i in range(1, 7)]
L1 = "The sky cracked. And the land rose to meet it."
L2 = "Two banners. One hill. Neither has learned to share."
L3 = "Far away, a Kaiju egg sleeps. It remembers every meal."
L4 = "Feed it, and it fights for you. Corrupt it, and it fights everyone."
L5 = "Hold the hill. Raise the Kaiju."
SHRINE_W = 2300
_eu = GEO.get("egg_in_shrine_uv", [0.5, 0.5])
EGG_IN_SHRINE = (round((_eu[0] - 0.5) * SHRINE_W), round((0.5 - _eu[1]) * SHRINE_W * ASPECT("shrine")))
CRACK_W = 2112
_cu = GEO.get("crack_origin_uv", [0.56, 0.12])
CRACK_DX, CRACK_DY = 166, 19   # nudge the painted crack so its burst point sits on the storm plate's spiral centre
CRACK_AT = (round((_cu[0] - 0.5) * CRACK_W) + CRACK_DX, round((0.5 - _cu[1]) * CRACK_W * ASPECT("crack_1")) + CRACK_DY)
_ec = GEO.get("egg_canvas", {})
EGGK = (_ec["w"] / _ec["egg_w"]) if _ec else 1.0   # egg textures share the hatch canvas; scale widths so the egg keeps its size
HILL_MARK = (0, 300)   # the capture ring at the top of the painted hill
_kh = GEO.get("kaiju_head_uv", [0.5, 0.2])   # head = centroid of the top band of the neutral Kaiju's matte
KHEAD = (round((_kh[0] - 0.5) * 1040 / ASPECT("kaiju_neutral")), round(-150 + (0.5 - _kh[1]) * 1040))

shots = [
    shot("calm", 4.5, "calm world: the Basin floats, quiet", [
        L("sky_day", 0.15, SKY, [K(0), K(4.5, x=-30)]),
        L("basin", 0.6, 1900, [K(0, y=-250), K(4.5, x=-40, y=-225)]),
        L("cloudsea", 0.85, 2700, [K(0, y=-700), K(4.5, x=-90, y=-700)]),
        L("vignette", 0.0, 2112, [K(0, a=0.45)]),
    ], [C(0), C(4.5, dolly=1.07, y=10)],
        titles=[T("LONG AGO", 1.1, 4.0, size=56, y=250, track0=44, track1=18)],
        particles=[P("mote", CREAM, 0, 4.5, 18, 0, -100, 1900, 700, 6, 14, 10, 8, life=4.5, size=10)],
        bars_=bars((0, 0, "linear"), (0.9, 1, "outCubic"))),
    shot("crack", 4.0, "the sky cracks: snap zoom, white flash", [
        L("sky_storm", 0.15, SKY, [K(0)]),
        L("crack_1", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)], frames=crack, frameAt=0.45, fps=10),
        L("crack_glow", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY, a=0), K(0.45, x=CRACK_DX, y=CRACK_DY, a=0), K(1.1, x=CRACK_DX, y=CRACK_DY, a=1, ease="outCubic")], pulseHz=1.3, pulseLo=0.7),
        L("shafts", 0.25, 2200, [K(0, x=CRACK_DX, y=CRACK_DY, a=0), K(0.6, x=CRACK_DX, y=CRACK_DY, a=0), K(1.8, x=CRACK_DX, y=CRACK_DY, a=0.8, ease="outCubic")]),
        L("basin", 0.6, 1900, [K(0, y=-360), K(4.0, y=-350)], tint="#7C8AA6"),
        L("cloudsea_storm", 0.85, 2700, [K(0, y=-700)]),
    ], [C(0), C(0.45), C(0.7, zoom=1.24, zx=CRACK_AT[0], zy=CRACK_AT[1], ease="outExpo"), C(4.0, zoom=1.3, zx=CRACK_AT[0], zy=CRACK_AT[1])],
        sub=dict(text=L1, at=0.6, out=4.0), vo=[("CMVoice/story-card-1", 0.6)], sfx=[("whoosh", 0.25), ("thunder", 0.42)],
        flash=[F(0.45, 0.35, 0.85, CREAM)], shake=[S(0.45, 0.8, 22)], boardAt=1.6),
    shot("rise", 4.0, "the land rises to meet it", [
        L("sky_storm", 0.15, SKY, [K(0)]),
        L("crack_6", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)]),
        L("crack_glow", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)], pulseHz=1.3, pulseLo=0.7),
        L("shafts", 0.25, 2200, [K(0, x=CRACK_DX, y=CRACK_DY, a=0.8)]),
        L("basin", 0.6, 1900, [K(0, y=-900), K(1.6, y=-230, ease="outBack"), K(4.0, y=-215)]),
        L("cloudsea_storm", 0.85, 2700, [K(0, y=-620), K(1.6, y=-740, ease="outCubic")]),
        L("debris", 0.9, 2100, [K(0, y=-1100), K(1.2, y=-420, ease="outCubic"), K(4.0, y=900, a=0.6, ease="inCubic")]),
        L("rocks_l", 1.0, WH("rocks_l", 1350), [K(0, x=-1010, y=-190), K(0.9, x=-1010, y=-140, ease="outCubic")], tint="#9AA6BD"),   # a full-height cliff: its cut top stays above the frame
        L("rocks_r", 1.0, WH("rocks_r", 820), [K(0, x=830, y=-760), K(0.9, x=830, y=-360, ease="outCubic")], tint="#9AA6BD"),
    ], [C(0), C(4.0, dolly=1.05, y=30)],
        titles=[T("SKYBREAK BASIN", 1.7, 3.85, size=92, y=230, track0=48, track1=12)],
        sfx=[("rumble", 0.0), ("impact", 1.45)], shake=[S(0.0, 1.8, 10)],
        particles=[P("mote", "#E8E2D6", 0, 3.0, 30, 0, -380, 1800, 120, 0, 120, 40, 60, g=-20, life=2.2, size=9)], boardAt=2.2),
    shot("fall", 4.0, "the egg falls through the crack", [
        L("sky_storm", 0.15, SKY, [K(0)], tint="#9AA6BD"),
        L("crack_6", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)]),
        L("crack_glow", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)], pulseHz=2.0, pulseLo=0.75),
        L("basin", 0.6, 2000, [K(0, y=-330)], tint="#8D98B0"),
        L("speed_down", 0.7, 2112, [K(0, a=0), K(1.3, a=0), K(1.6, a=0.75), K(2.8, a=0.75), K(2.9, a=0)], flicker=True, fps=12),
        L("egg_glow", 0.7, round(260 * EGGK), [K(0, x=CRACK_AT[0], y=CRACK_AT[1], s=0.4, r=-30), K(2.8, x=-40, y=-140, s=1, r=200, ease="inExpo"), K(4.0, x=-40, y=-140, s=1, r=200)], pulseHz=3, pulseLo=0.6),
        L("egg", 0.7, round(260 * EGGK), [K(0, x=CRACK_AT[0], y=CRACK_AT[1], s=0.4, r=-30), K(2.8, x=-40, y=-140, s=1, r=200, ease="inExpo"), K(4.0, x=-40, y=-140, s=1, r=200)]),
        L("ring", 0.7, 300, [K(0, a=0), K(2.8, x=-40, y=-170, s=0.2, a=0, ease="step"), K(2.81, x=-40, y=-170, s=0.25, a=1, ease="step"), K(3.5, x=-40, y=-170, s=3.2, a=0, ease="outCubic")]),
    ], [C(0), C(2.8), C(3.0, zoom=1.1, zx=-40, zy=-140, ease="outExpo"), C(4.0, zoom=1.12, zx=-40, zy=-140)],
        titles=[T("SOMETHING FELL THROUGH.", 0.35, 2.5, size=54, y=300, track0=26, track1=8)],
        sfx=[("whoosh", 1.2), ("impact", 2.8), ("thunder", 2.85)], flash=[F(2.8, 0.3, 1.0)], shake=[S(2.8, 0.6, 28)],
        particles=[P("mote", "#D9D2C4", 2.8, 2.86, 26, -40, -160, 120, 30, 0, 260, 520, 260, g=-420, life=1.0, size=14, loop=False, depth=0.7)],
        boardAt=2.95),
    shot("shrine", 5.0, "far away, the egg sleeps in the shrine", [
        L("sky_night", 0.1, SKY, [K(0)]),
        L("shrine", 0.5, SHRINE_W, [K(0, b=0.9), K(1.7, b=0, ease="inOutCubic")], blur="shrine_blur"),
        L("shrine_glow", 0.5, SHRINE_W, [K(0, a=0.9)], pulseHz=0.8, pulseLo=0.4),
        L("rocks_l", 1.0, WH("rocks_l", 1350), [K(0, x=-1040, y=-130), K(5.0, x=-1070, y=-130)], tint="#2A3550"),
        L("rocks_r", 1.0, WH("rocks_r", 700), [K(0, x=920, y=-440), K(5.0, x=950, y=-440)], tint="#2A3550"),
        L("vignette", 0.0, 2112, [K(0, a=0.8)]),
    ], [C(0, zx=EGG_IN_SHRINE[0], zy=EGG_IN_SHRINE[1]), C(5.0, zoom=1.12, zx=EGG_IN_SHRINE[0], zy=EGG_IN_SHRINE[1], dolly=1.03)],
        sub=dict(text=L3, at=0.5, out=5.0), vo=[("CMVoice/story-card-3", 0.5)], sfx=[("bong", 0.35)],
        particles=[P("mote", BIOMASS, 0, 5.0, 22, 0, -420, 1500, 80, 0, 70, 30, 30, life=4.0, size=9, depth=0.8)], boardAt=3.0),
    shot("peoples", 6.0, "two peoples plant their banners", [
        L("sky_split", 0.15, SKY, [K(0)]),
        L("banner_azure", 0.6, 300, [K(0, x=-640, y=900), K(0.5, x=-640, y=900), K(1.0, x=-640, y=130, ease="outBack")]),
        L("banner_crimson", 0.6, 300, [K(0, x=640, y=900), K(1.2, x=640, y=900), K(1.7, x=640, y=130, ease="outBack")]),
        L("farmer", 0.75, WH("farmer", 540), [K(0, x=-1500, y=-190), K(0.9, x=-1500, y=-190), K(1.5, x=-770, y=-200, ease="outBack")]),
        L("miner", 0.85, WH("miner", 500), [K(0, x=-1500, y=-250), K(1.1, x=-1500, y=-250), K(1.7, x=-300, y=-270, ease="outBack")]),
        L("warrior", 0.8, WH("warrior", 600), [K(0, x=-1500, y=-210), K(0.8, x=-1500, y=-210), K(1.4, x=-560, y=-225, ease="outBack")]),
        L("farmer_crimson", 0.75, WH("farmer_crimson", 540), [K(0, x=1500, y=-190), K(1.6, x=1500, y=-190), K(2.2, x=770, y=-200, ease="outBack")]),
        L("miner_crimson", 0.85, WH("miner_crimson", 500), [K(0, x=1500, y=-250), K(1.8, x=1500, y=-250), K(2.4, x=300, y=-270, ease="outBack")]),
        L("warrior_crimson", 0.8, WH("warrior_crimson", 600), [K(0, x=1500, y=-210), K(1.5, x=1500, y=-210), K(2.1, x=560, y=-225, ease="outBack")]),
    ], [C(0, x=-20), C(6.0, dolly=1.04, x=20)],
        titles=[T("TWO BANNERS.", 0.25, 2.6, size=100, y=300, track0=50, track1=12)],
        sub=dict(text=L2, at=2.4, out=6.0), vo=[("CMVoice/story-card-2", 2.4)], sfx=[("whoosh", 0.75), ("clang", 1.4), ("whoosh", 1.45), ("clang", 2.1)],
        shake=[S(1.4, 0.25, 6), S(2.1, 0.25, 6)], boardAt=3.2),
    shot("hill", 5.5, "one hill: they clash", [
        L("hill", 0.5, 2300, [K(0, y=-40, b=0.7), K(0.9, y=-40, b=0, ease="outCubic")], blur="hill_blur"),
        L("speed_radial", 0.0, 2112, [K(0, a=0), K(2.35, a=0), K(2.45, a=0.7), K(3.0, a=0.7), K(3.2, a=0)], flicker=True, fps=14),
        L("warrior_charge", 1.0, WH("warrior_charge", 640), [K(0, x=-1600, y=-260), K(1.1, x=-560, y=-260, ease="outBack"), K(2.45, x=-400, y=-260, ease="inCubic"), K(5.5, x=-440, y=-260)]),
        L("warrior_cleave_crimson", 1.0, WH("warrior_cleave_crimson", 700), [K(0, x=1700, y=-180), K(1.3, x=560, y=-180, ease="outBack"), K(2.45, x=420, y=-200, ease="inCubic"), K(5.5, x=460, y=-200)]),
        L("ring", 0.8, 300, [K(0, a=0), K(2.45, y=-80, s=0.2, a=0, ease="step"), K(2.46, y=-80, s=0.25, a=1, ease="step"), K(3.2, y=-80, s=3.4, a=0, ease="outCubic")]),
        L("vignette", 0.0, 2112, [K(0, a=0.5)]),
    ], [C(0, zx=HILL_MARK[0], zy=HILL_MARK[1]), C(5.5, zoom=1.12, zx=HILL_MARK[0], zy=HILL_MARK[1])],
        titles=[T("ONE HILL.", 0.45, 2.3, size=116, y=300, track0=60, track1=14)],
        sub=dict(text=L2, at=0, out=1.0), sfx=[("whoosh", 0.3), ("hit", 2.45), ("clang", 2.5)],
        flash=[F(2.45, 0.25, 0.6)], shake=[S(2.45, 0.5, 18)], boardAt=3.4),
    shot("choice", 6.5, "feed it, or corrupt it", [
        L("sky_choice", 0.15, SKY, [K(0)]),
        L("egg_glow", 0.5, round(330 * EGGK), [K(0, y=-60, s=0.6, a=0), K(0.6, y=-60, s=1, a=0.9, ease="outBack")], pulseHz=1.1, pulseLo=0.5),
        L("egg", 0.5, round(330 * EGGK), [K(0, y=-60, s=0.6, a=0), K(0.6, y=-60, s=1, a=1, ease="outBack")]),
        L("biomass_plants_glow", 0.8, WH("biomass_plants", 430), [K(0, x=-600, y=-900), K(0.3, x=-600, y=-900), K(1.0, x=-600, y=-365, ease="outBack")], pulseHz=0.9, pulseLo=0.5),
        L("biomass_plants", 0.8, WH("biomass_plants", 430), [K(0, x=-600, y=-900), K(0.3, x=-600, y=-900), K(1.0, x=-600, y=-365, ease="outBack")]),
        L("corruption_growth_glow", 0.8, WH("corruption_growth", 430), [K(0, x=620, y=-900), K(2.6, x=620, y=-900), K(3.3, x=620, y=-365, ease="outBack")], pulseHz=1.4, pulseLo=0.4),
        L("corruption_growth", 0.8, WH("corruption_growth", 430), [K(0, x=620, y=-900), K(2.6, x=620, y=-900), K(3.3, x=620, y=-365, ease="outBack")]),
        L("farmer_harvest", 0.95, WH("farmer_harvest", 560), [K(0, x=-1600, y=-200), K(0.6, x=-1600, y=-200), K(1.3, x=-640, y=-200, ease="outBack")]),
    ], [C(0, x=-60), C(6.5, x=60, dolly=1.04)],
        titles=[T("FEED IT", 0.5, 6.0, size=104, color=BIOMASS, x=-470, y=300, track0=40, track1=10),
                T("OR CORRUPT IT", 2.7, 6.0, size=96, color=CORR, x=420, y=300, track0=40, track1=8)],
        sub=dict(text=L4, at=0.6, out=6.5), vo=[("CMVoice/story-card-4", 0.6)], sfx=[("chime", 0.6), ("bong", 2.7)],
        particles=[P("mote", BIOMASS, 0, 6.5, 16, -520, -300, 700, 200, 0, 60, 20, 30, life=3.5, size=10, depth=0.85),
                   P("ember", CORR, 2.6, 6.5, 16, 560, -300, 700, 200, 0, 90, 30, 40, life=3.0, size=12, depth=0.85)], boardAt=4.2),
    shot("hatch", 3.5, "the egg cracks open", [
        L("sky_storm", 0.15, SKY, [K(0)]),
        L("shafts", 0.25, 2200, [K(0, a=0.5)]),
        L("egg_glow", 0.5, round(440 * EGGK), [K(0, y=-40)], to=2.45, pulseHz=3.0, pulseLo=0.4, jitter=7, jitterFrom=0.6, jitterTo=2.4),
        L("egg", 0.5, round(440 * EGGK), [K(0, y=-40)], to=0.61, jitter=7, jitterFrom=0.6, jitterTo=2.4),
        L("egg_crack_1", 0.5, round(440 * EGGK), [K(0, y=-40, a=0), K(0.6, y=-40, a=0, ease="step"), K(0.61, y=-40, a=1, ease="step")], to=2.45,
          frames=["egg_crack_%d" % i for i in range(1, 5)], frameAt=0.6, fps=2.2, jitter=7, jitterFrom=0.6, jitterTo=2.4),
        L("hatchling_glow", 0.5, WH("hatchling", 560), [K(0, y=-40, s=0.85, a=0), K(2.45, y=-40, s=0.85, a=0, ease="step"), K(2.85, y=-40, s=1, a=0.9, ease="outBack")], pulseHz=1.5, pulseLo=0.5),
        L("hatchling", 0.5, WH("hatchling", 560), [K(0, y=-40, s=0.85, a=0), K(2.45, y=-40, s=0.85, a=0, ease="step"), K(2.46, y=-40, s=0.85, a=1, ease="step"), K(2.85, y=-40, s=1, ease="outBack")]),
        L("ring", 0.5, 320, [K(0, a=0), K(2.45, y=-40, s=0.2, a=0, ease="step"), K(2.46, y=-40, s=0.3, a=1, ease="step"), K(3.2, y=-40, s=3.4, a=0, ease="outCubic")]),
        L("vignette", 0.0, 2112, [K(0, a=0.6)]),
    ], [C(0, zx=0, zy=-40), C(2.4, zoom=1.16, zy=-40, ease="inCubic"), C(2.6, zoom=1.04, zy=-40, ease="outExpo"), C(3.5, zoom=1.06, zy=-40)],
        sfx=[("rumble", 0.5), ("impact", 2.45), ("bong", 2.5)], flash=[F(2.45, 0.4, 0.9, "#F4FFD9")], shake=[S(0.6, 1.8, 4), S(2.45, 0.5, 20)],
        particles=[P("ember", "#8D90A0", 2.45, 2.5, 26, 0, -40, 60, 60, 0, 120, 700, 600, g=-700, life=1.0, size=16, loop=False, depth=0.5)],
        boardAt=1.9),
    shot("kaiju", 6.0, "the Kaiju rises", [
        L("sky_storm", 0.12, SKY, [K(0)]),
        L("shafts", 0.25, 2200, [K(0, a=0.6)]),
        L("kaiju_neutral_glow", 0.7, WH("kaiju_neutral", 1040), [K(0, y=-1600), K(1.3, y=-150, ease="outBack"), K(6.0, y=-140, s=1.04)], pulseHz=1.6, pulseLo=0.45),
        L("kaiju_neutral", 0.7, WH("kaiju_neutral", 1040), [K(0, y=-1600), K(1.3, y=-150, ease="outBack"), K(6.0, y=-140, s=1.04)]),
        L("cloudsea_storm", 0.9, 2700, [K(0, y=-640)]),
        L("debris", 0.95, 2100, [K(0, y=-1200), K(1.4, y=-560, ease="outCubic"), K(3.2, y=1100, a=0.5, ease="inCubic")]),
        L("speed_radial", 0.0, 2112, [K(0, a=0), K(1.4, a=0), K(1.5, a=0.65), K(2.6, a=0.65), K(2.9, a=0)], flicker=True, fps=16),
        L("rocks_l", 1.0, WH("rocks_l", 1350), [K(0, x=-1060, y=-130)], tint="#5A6680"),
        L("rocks_r", 1.0, WH("rocks_r", 640), [K(0, x=950, y=-500)], tint="#5A6680"),
    ], [C(0, zx=KHEAD[0], zy=KHEAD[1]), C(1.4, zx=KHEAD[0], zy=KHEAD[1]), C(1.6, zoom=1.08, zx=KHEAD[0], zy=KHEAD[1], ease="outExpo"), C(6.0, zoom=1.11, zx=KHEAD[0], zy=KHEAD[1])],
        titles=[T("IT PICKS A SIDE.", 3.0, 5.85, size=88, y=-300, track0=40, track1=10)],
        sfx=[("rumble", 0.0), ("roar", 1.4)], flash=[F(1.4, 0.3, 0.7)], shake=[S(0.2, 1.1, 8), S(1.4, 1.2, 26)],
        particles=[P("ember", "#8D90A0", 0.8, 1.5, 30, 0, -420, 1200, 60, 0, 520, 300, 260, g=-600, life=1.6, size=18, loop=False, depth=0.8),
                   P("mote", "#D9D2C4", 0, 6.0, 20, 0, -400, 1800, 100, 0, 90, 40, 30, life=3.0, size=9, depth=0.9)], boardAt=3.6),
    shot("sides", 4.0, "or no side at all", [
        L("@solid", 0.4, 700, [K(0, x=-660, r=-6)], h=1500, tint="#123C55"),
        L("@solid", 0.4, 700, [K(0, x=0, r=-6)], h=1500, tint="#5A1E2C"),
        L("@solid", 0.4, 700, [K(0, x=660, r=-6)], h=1500, tint="#3A1A5C"),
        L("kaiju_neutral_glow", 0.6, WH("kaiju_neutral", 740), [K(0, x=-640, y=-900), K(0.1, x=-640, y=-900), K(0.7, x=-640, y=-80, ease="outBack")], pulseHz=1.2, pulseLo=0.5),
        L("kaiju_neutral", 0.6, WH("kaiju_neutral", 740), [K(0, x=-640, y=-900), K(0.1, x=-640, y=-900), K(0.7, x=-640, y=-80, ease="outBack")]),
        L("kaiju_crimson_glow", 0.6, WH("kaiju_crimson", 740), [K(0, y=-900), K(0.4, y=-900), K(1.0, y=-80, ease="outBack")], pulseHz=1.2, pulseLo=0.5),
        L("kaiju_crimson", 0.6, WH("kaiju_crimson", 740), [K(0, y=-900), K(0.4, y=-900), K(1.0, y=-40, ease="outBack")]),
        L("kaiju_berserk_glow", 0.6, WH("kaiju_berserk", 740), [K(0, x=640, y=-900), K(0.7, x=640, y=-900), K(1.3, x=640, y=-40, ease="outBack")], pulseHz=2.0, pulseLo=0.4),
        L("kaiju_berserk", 0.6, WH("kaiju_berserk", 740), [K(0, x=640, y=-900), K(0.7, x=640, y=-900), K(1.3, x=640, y=-40, ease="outBack")]),
        L("@solid", 0.4, 44, [K(0, x=-330, r=-6)], h=1600, tint=NAVY),
        L("@solid", 0.4, 44, [K(0, x=330, r=-6)], h=1600, tint=NAVY),
    ], [C(0), C(4.0, dolly=1.03)],
        titles=[T("OR NO SIDE AT ALL.", 1.6, 3.95, size=84, y=-310, track0=40, track1=10)],
        sfx=[("hit", 0.6), ("hit", 0.9), ("hit", 1.2), ("bong", 1.6)], shake=[S(0.6, 0.2, 5), S(0.9, 0.2, 5), S(1.2, 0.25, 8)], boardAt=2.6),
    shot("hero", 5.5, "your turn: hold the hill", [
        L("hero", 0.4, 2300, [K(0, b=0.0), K(2.0, b=0.0), K(2.5, b=0.35, ease="outCubic")], blur="hero_blur"),
        L("shafts_warm", 0.2, 2200, [K(0, a=0.4)]),
        L("vignette", 0.0, 2112, [K(0, a=0.6)]),
        L("UI/Logo/critical-mass-bible-logo-glow@2x", 0.0, 900, [K(0, y=170, s=1.6, a=0), K(2.0, y=170, s=1.6, a=0, ease="step"), K(2.45, y=170, s=1, a=1, ease="outBack")], pulseHz=1.0, pulseLo=0.5),
        L("UI/Logo/critical-mass-bible-logo@2x", 0.0, 900, [K(0, y=170, s=1.6, a=0), K(2.0, y=170, s=1.6, a=0, ease="step"), K(2.01, y=170, s=1.6, a=1, ease="step"), K(2.45, y=170, s=1, a=1, ease="outBack")]),
        L("sweep", 0.0, 600, [K(0, x=-1300, a=0, r=18), K(2.9, x=-1300, a=0.5, r=18, ease="step"), K(3.7, x=1300, a=0.5, r=18, ease="inOutCubic")], h=1500),
    ], [C(0), C(5.5, dolly=1.04)],
        titles=[T("HOLD THE HILL.", 0.25, 2.0, size=120, y=20, track0=64, track1=12)],
        sub=dict(text=L5, at=0.2, out=2.6), vo=[("CMVoice/story-card-5", 0.2), ("CMVoice/story-title", 2.9)], sfx=[("impact", 2.0), ("chime", 2.9)],
        flash=[F(2.0, 0.3, 0.5)], shake=[S(2.0, 0.3, 10)],
        particles=[P("ember", "#F9A858", 0, 5.5, 24, 0, -420, 1900, 80, 0, 80, 30, 40, life=4.0, size=10, depth=0.3)],
        bars_=bars((0, 1, "linear"), (3.6, 1, "linear"), (4.6, 0, "inOutCubic")), boardAt=3.2),
]

data = dict(schema="cm.story.motioncomic/1", stageW=1920, stageH=1080, letterbox=0.11, skipPromptAfter=1.0, outroSeconds=0.5,
            cues=[dict(id="whoosh", files=["knifeSlice", "knifeSlice2"], pitch=0.5, gain=0.7),
                  dict(id="thunder", files=["impactPlate_heavy_001"], pitch=0.45, gain=0.9),
                  dict(id="impact", files=["impactWood_heavy_000", "impactWood_heavy_001"], pitch=0.6, gain=0.9),
                  dict(id="rumble", files=["impactSoft_heavy_000", "impactSoft_heavy_001"], pitch=0.45, gain=0.8),
                  dict(id="hit", files=["impactPunch_heavy_000", "impactPunch_heavy_001"], pitch=0.8, gain=0.75),
                  dict(id="clang", files=["impactPlate_medium_000", "impactPlate_medium_001"], pitch=0.85, gain=0.6),
                  dict(id="roar", files=["impactPunch_heavy_002", "impactPlate_heavy_001"], pitch=0.5, gain=1.0),
                  dict(id="bong", files=["bong_001"], pitch=0.6, gain=0.6),
                  dict(id="chime", files=["confirmation_002"], pitch=0.9, gain=0.5)],
            sequences=[dict(id="intro", shots=[s["id"] for s in shots]),
                       dict(id="attract", shots=[s["id"] for s in shots])],
            codex=[dict(id="relic-01", shots=["peoples"]), dict(id="relic-02", shots=["crack", "rise"]),
                   dict(id="relic-03", shots=["shrine"]), dict(id="relic-04", shots=["calm"]), dict(id="relic-06", shots=["choice"]),
                   dict(id="relic-09", shots=["hatch"]), dict(id="relic-12", shots=["hill"])],
            shots=shots)
json.dump(data, open(OUT, "w"), indent=1)
print("shots", len(shots), "seconds", round(sum(s["seconds"] for s in shots), 2), "->", OUT)
