#!/usr/bin/env python3
"""Writes the motion-comic shot table (Assets/_Game/Resources/Story/motion-comic.json) with every field filled in.
The JSON is the source of truth the game plays; retune timing by editing it directly or by editing this file and
re-running it. Units: seconds; stage units on a 1920 x 1080 stage, origin at the centre, y UP. depth 0 = pinned to the
screen, 1 = moves fully with the camera (parallax). Eases: inOutSine outSine inSine outCubic inCubic inOutCubic outExpo
inExpo outBack inBack step linear outQuint inOutQuint outBackSoft inOutBack (linear only for holds). Story intro 2 (2026-11-13):
cover plates, subjects, procedural life presets, dissolves, kinetic title styles, the logo anchored to the title's logo, and
named cues with procedural recipes; the edit is cut on the narration's phrases (see the timing table below)."""
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
      pulseHz=0, pulseLo=1, jitter=0, jitterFrom=0, jitterTo=0, cover=False, subject=False, breathe=0, breatheHz=0.3, bob=0, sway=0,
      driftX=0, driftY=0, glint=0, anchor="", life=None):
    """life: a preset for the procedural life (see LIFE), overridden by explicit values."""
    if life:
        p = LIFE[life]
        breathe = breathe or p.get("breathe", 0); breatheHz = p.get("breatheHz", breatheHz); bob = bob or p.get("bob", 0)
        sway = sway or p.get("sway", 0); driftX = driftX or p.get("driftX", 0); glint = glint or p.get("glint", 0)
    return dict(tex=tex, depth=depth, w=w, h=h, flip=flip, tint=tint, blur=blur, **{"from": frm}, to=to, frames=frames or [],
                frameAt=frameAt, fps=fps, flicker=flicker, pulseHz=pulseHz, pulseLo=pulseLo, jitter=jitter, jitterFrom=jitterFrom,
                jitterTo=jitterTo, cover=cover, subject=subject, breathe=breathe, breatheHz=breatheHz, bob=bob, sway=sway, driftX=driftX,
                driftY=driftY, glint=glint, anchor=anchor, keys=keys)
# Procedural life presets (story intro 2): nothing on screen is ever frozen, and nothing moves enough to draw the eye.
LIFE = {
    "fighter": dict(breathe=0.012, breatheHz=0.34, bob=2.5),
    "kaiju": dict(breathe=0.016, breatheHz=0.21, bob=4),
    "egg": dict(breathe=0.010, breatheHz=0.27, bob=3),
    "glow": dict(glint=0.32),
    "float": dict(bob=6, breatheHz=0.15),
    "banner": dict(sway=1.4),
    "plant": dict(sway=0.9, breathe=0.006),
    "sky": dict(driftX=-4),
    "cloud": dict(driftX=-14),
}
def ENTER(x0, y0, x1, y1, t0, t1, s=1, ease="outBackSoft"):
    """An entrance that lands with a small overshoot, then a squash-and-settle (no flat-card bounce)."""
    return [K(0, x0, y0, s=s), K(t0, x0, y0, s=s), K(t1, x1, y1, s=s, ease=ease), K(t1 + 0.12, x1, y1, s=s * 1.012, ease="outSine"),
            K(t1 + 0.4, x1, y1, s=s, ease="inOutSine")]
def SLAM(x, y0, y1, t0, t1, s=1):
    """Drops in and hits (accelerating), squashes on the hit, settles."""
    return [K(0, x, y0, s=s)] + ([K(t0, x, y0, s=s)] if t0 > 0 else []) + [K(t1, x, y1, s=s, ease="inExpo"), K(t1 + 0.08, x, y1 - 6, s=s * 0.985, ease="outCubic"),
            K(t1 + 0.4, x, y1, s=s, ease="outBackSoft")]
def P(tex, color, t0, t1, count, x, y, sx, sy, vx, vy, jx, jy, g=0, life=3, size=14, loop=True, depth=0.6):
    return dict(tex=tex, color=color, t0=t0, t1=t1, count=count, x=x, y=y, sx=sx, sy=sy, vx=vx, vy=vy, jx=jx, jy=jy, g=g, life=life,
                size=size, loop=loop, depth=depth)
def T(text, at, out, size=96, color=CREAM, x=0, y=300, font="display", track0=36, track1=10, style="pop"): return dict(text=text, at=at, out=out, size=size, color=color, x=x, y=y, font=font, track0=track0, track1=track1, style=style)
def F(t, dur, peak, color="#FFFFFF"): return dict(t=t, dur=dur, peak=peak, color=color)
def S(t, dur, amp): return dict(t=t, dur=dur, amp=amp)
def bars(*kv): return [dict(t=t, v=v, ease=e) for t, v, e in kv]
def shot(id, seconds, beat, layers, cam, titles=(), sub=None, vo=(), sfx=(), flash=(), shake=(), particles=(), bars_=None, boardAt=None,
         trans="cut", transDur=0, camDrift=5):
    return dict(id=id, seconds=seconds, beat=beat, boardAt=boardAt if boardAt is not None else round(seconds * 0.6, 2), layers=layers, cam=cam,
                titles=list(titles), sub=sub or dict(text="", at=0, out=0), vo=[dict(clip=c, at=a) for c, a in vo],
                sfx=[dict(cue=c, at=a) for c, a in sfx], flash=list(flash), shake=list(shake), particles=list(particles),
                bars=bars_ or bars((0, 1, "linear")), trans=trans, transDur=transDur, camDrift=camDrift)

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

# ---- story intro 2 (2026-11-13): the edit ----
# Cut on the narration's phrases (measured with ffmpeg silencedetect on the CMVoice clips):
#   card-1 "The sky cracked." 0-1.25 | "And the land rose to meet it." 1.55-3.34
#   card-2 "Two banners." 0-0.90 | "One hill." 1.12-1.87 | "Neither has learned to share." 2.15-3.75
#   card-3 "Far away, a Kaiju egg sleeps." 0-2.31 | "It remembers every meal." 2.66-4.30
#   card-4 "Feed it, and it fights for you." 0-1.84 | "Corrupt it," 2.13-2.78 | "and it fights everyone." 3.03-4.50
#   card-5 "Hold the hill." 0-0.99 | "Raise the Kaiju." 1.20-2.57      title "Critical Mass." 0-2.45
# A narration clip keeps playing across a cut (the next phrase lands on the next shot). Every shot that has no narration is
# a short action beat. Composition stays inside the safe boxes of MotionComic (titles |x| <= 680, |y| <= 380; subjects
# fully on screen at 1.54), so 4:3 to 21:9 all read. Cover plates are marked cover=True; the coverage test checks them.
VO1, VO2, VO3, VO4, VO5 = ("CMVoice/story-card-%d" % i for i in (1, 2, 3, 4, 5))
VOT = "CMVoice/story-title"
ROCK = "#C9C2B4"
LOGO_W = 766   # TitleScreen.LogoSize.x: the anchored logo's keys are relative to the title's own logo

shots = [
    shot("calm", 3.8, "calm world: the Basin floats, then the storm rolls in", [
        L("sky_day", 0.15, SKY, [K(0)], cover=True, life="sky"),
        L("sky_storm", 0.15, SKY, [K(0, a=0), K(2.5, a=0), K(3.8, a=0.6, ease="inSine")], life="sky"),
        L("basin", 0.6, 1900, [K(0, y=-250), K(3.8, y=-238)], life="float"),
        L("cloudsea", 0.85, 2700, [K(0, y=-700)], life="cloud"),
        L("vignette", 0.0, 2112, [K(0, a=0.45), K(3.8, a=0.6)]),
    ], [C(0, x=-40), C(3.8, dolly=1.08, x=30, y=10)],
        titles=[T("LONG AGO", 0.6, 3.3, size=56, y=250, track0=44, track1=18, style="rise")],
        sfx=[("rumble", 2.7), ("whoosh", 3.5)],
        particles=[P("mote", CREAM, 0, 3.8, 18, 0, -100, 1600, 700, 6, 14, 10, 8, life=4.5, size=10)],
        bars_=bars((0, 0, "linear"), (0.9, 1, "outCubic")), boardAt=2.0),
    shot("crack", 2.7, "the sky cracks: anticipation, snap zoom, flash, debris", [
        L("sky_storm", 0.15, SKY, [K(0)], cover=True, life="sky"),
        L("crack_glow", 0.2, CRACK_W * 1.45, [K(0, x=CRACK_DX, y=CRACK_DY, a=0), K(0.45, x=CRACK_DX, y=CRACK_DY, a=0),
                                              K(0.6, x=CRACK_DX, y=CRACK_DY, a=0.6, ease="outCubic"), K(2.7, x=CRACK_DX, y=CRACK_DY, a=0.3)], glint=0.25),
        L("crack_1", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)], frames=crack, frameAt=0.45, fps=8),
        L("crack_glow", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY, a=0), K(0.45, x=CRACK_DX, y=CRACK_DY, a=0), K(1.0, x=CRACK_DX, y=CRACK_DY, a=1, ease="outCubic")],
          pulseHz=1.3, pulseLo=0.75, glint=0.2),
        L("shafts", 0.25, 2200, [K(0, x=CRACK_DX, y=CRACK_DY, a=0), K(0.6, x=CRACK_DX, y=CRACK_DY, a=0), K(1.6, x=CRACK_DX, y=CRACK_DY, a=0.5, ease="outCubic")]),
        L("basin", 0.6, 1900, [K(0, y=-520), K(2.7, y=-510)], tint="#7C8AA6", life="float"),
        L("cloudsea_storm", 0.85, 2700, [K(0, y=-700)], life="cloud"),
    ], [C(0, zx=CRACK_AT[0], zy=CRACK_AT[1]), C(0.42, zoom=0.985, zx=CRACK_AT[0], zy=CRACK_AT[1]),
        C(0.62, zoom=1.10, zx=CRACK_AT[0], zy=CRACK_AT[1], ease="outExpo"), C(0.95, zoom=1.08, zx=CRACK_AT[0], zy=CRACK_AT[1], ease="outSine"),
        C(2.7, zoom=1.11, zx=CRACK_AT[0], zy=CRACK_AT[1])],
        sub=dict(text="The sky cracked.", at=1.1, out=2.7), vo=[(VO1, 1.1)],
        sfx=[("rumble", 0.0), ("skycrack", 0.43), ("thunder", 0.47), ("debris", 0.6)],
        flash=[F(0.45, 0.4, 0.7, CREAM)], shake=[S(0.45, 0.7, 16)],
        particles=[P("ember", ROCK, 0.45, 0.6, 24, CRACK_AT[0], CRACK_AT[1], 80, 40, 0, 160, 900, 600, g=-520, life=1.5, size=18, loop=False, depth=0.25)],
        boardAt=1.6),
    shot("rise", 3.6, "the land rises to meet it", [
        L("sky_storm", 0.15, SKY, [K(0)], cover=True, life="sky"),
        L("crack_6", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)]),
        L("crack_glow", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)], pulseHz=1.3, pulseLo=0.7, glint=0.2),
        L("shafts", 0.25, 2200, [K(0, x=CRACK_DX, y=CRACK_DY, a=0.7)]),
        L("basin", 0.6, 1900, [K(0, y=-900), K(1.5, y=-235, ease="outBackSoft"), K(3.6, y=-222)], life="float"),
        L("cloudsea_storm", 0.85, 2700, [K(0, y=-620), K(1.5, y=-760, ease="outCubic")], life="cloud"),
        L("debris", 0.9, 2100, [K(0, y=-1100), K(1.2, y=-470, ease="outCubic"), K(2.7, y=-1300, ease="inCubic")]),   # thrown up, falls back (never a translucent sheet, never its cut edge)
        L("rocks_r", 1.0, WH("rocks_r", 820), [K(0, x=830, y=-800), K(1.0, x=830, y=-380, ease="outCubic")], tint="#9AA6BD"),
    ], [C(0, y=-30), C(3.6, dolly=1.06, y=30)],
        titles=[T("SKYBREAK BASIN", 1.85, 3.45, size=92, y=310, track0=48, track1=12)],
        sub=dict(text="And the land rose to meet it.", at=0.0, out=1.9),
        sfx=[("rumble", 0.0), ("impact", 1.45), ("debris", 1.5)], shake=[S(0.0, 1.6, 8), S(1.45, 0.5, 14)],
        particles=[P("mote", "#E8E2D6", 0, 3.0, 30, 0, -380, 1600, 120, 0, 120, 40, 60, g=-20, life=2.2, size=9)], boardAt=2.2),
    shot("fall", 3.4, "the egg falls through the crack", [
        L("sky_storm", 0.15, SKY, [K(0)], tint="#9AA6BD", cover=True, life="sky"),
        L("crack_6", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)]),
        L("crack_glow", 0.2, CRACK_W, [K(0, x=CRACK_DX, y=CRACK_DY)], pulseHz=2.0, pulseLo=0.75, glint=0.25),
        L("basin", 0.6, 2000, [K(0, y=-330)], tint="#8D98B0", life="float"),
        L("speed_down", 0.7, 2112, [K(0, a=0), K(0.3, a=0), K(0.5, a=0.6), K(1.35, a=0.6), K(1.5, a=0)], flicker=True, fps=12),
        L("egg_glow", 0.7, round(260 * EGGK), [K(0, x=CRACK_AT[0], y=CRACK_AT[1], s=0.35, r=-30), K(1.5, x=-40, y=-140, s=1, r=200, ease="inExpo"),
                                              K(1.62, x=-40, y=-128, s=1, r=200, ease="outCubic"), K(1.9, x=-40, y=-140, s=1, r=200)], pulseHz=3, pulseLo=0.6, life="glow"),
        L("egg", 0.7, round(260 * EGGK), [K(0, x=CRACK_AT[0], y=CRACK_AT[1], s=0.35, r=-30), K(1.5, x=-40, y=-140, s=1, r=200, ease="inExpo"),
                                          K(1.62, x=-40, y=-128, s=1, r=200, ease="outCubic"), K(1.9, x=-40, y=-140, s=1, r=200)], subject=True),
        L("ring", 0.7, 300, [K(0, a=0), K(1.5, x=-40, y=-170, s=0.2, a=0, ease="step"), K(1.51, x=-40, y=-170, s=0.25, a=1, ease="step"),
                             K(2.2, x=-40, y=-170, s=3.2, a=0, ease="outCubic")]),
    ], [C(0), C(1.45), C(1.65, zoom=1.1, zx=-40, zy=-140, ease="outExpo"), C(3.4, zoom=1.12, zx=-40, zy=-140)],
        titles=[T("SOMETHING FELL THROUGH.", 1.95, 3.3, size=56, y=330, track0=26, track1=8, style="rise")],
        sfx=[("whoosh", 0.8), ("eggland", 1.5), ("thunder", 1.55), ("debris", 1.6)], flash=[F(1.5, 0.3, 0.65)], shake=[S(1.5, 0.6, 20)],
        particles=[P("mote", "#D9D2C4", 1.5, 1.56, 26, -40, -160, 120, 30, 0, 260, 520, 260, g=-420, life=1.0, size=14, loop=False, depth=0.7)],
        boardAt=2.0),
    shot("shrine", 4.9, "far away, the egg sleeps in the shrine", [
        L("shrine", 0.5, SHRINE_W, [K(0, b=0.9), K(1.4, b=0, ease="inOutCubic")], blur="shrine_blur", cover=True),
        L("shrine_glow", 0.5, SHRINE_W, [K(0, a=0.9)], pulseHz=0.8, pulseLo=0.45, glint=0.3),
        L("rocks_r", 1.0, WH("rocks_r", 700), [K(0, x=930, y=-440)], tint="#2A3550"),
        L("vignette", 0.0, 2112, [K(0, a=0.8)]),
    ], [C(0, zx=EGG_IN_SHRINE[0], zy=EGG_IN_SHRINE[1], x=-30), C(4.9, zoom=1.1, zx=EGG_IN_SHRINE[0], zy=EGG_IN_SHRINE[1], dolly=1.04, x=30)],
        sub=dict(text=L3, at=0.35, out=4.9), vo=[(VO3, 0.35)], sfx=[("bong", 0.3)],
        particles=[P("mote", BIOMASS, 0, 4.9, 22, 0, -420, 1300, 80, 0, 70, 30, 30, life=4.0, size=9, depth=0.8)], boardAt=3.0,
        trans="dissolve", transDur=0.5),
    shot("peoples", 3.35, "two peoples plant their banners", [
        L("sky_split", 0.15, SKY, [K(0)], cover=True, life="sky"),
        L("banner_azure", 0.6, 300, SLAM(-600, 900, 150, 0.35, 0.65), life="banner"),
        L("banner_crimson", 0.6, 300, SLAM(600, 900, 150, 0.55, 0.85), life="banner"),
        L("farmer", 0.72, WH("farmer", 540), ENTER(-1500, -170, -610, -175, 0.8, 1.4), subject=True, life="fighter"),
        L("farmer_crimson", 0.72, WH("farmer_crimson", 540), ENTER(1500, -170, 610, -175, 0.95, 1.55), subject=True, life="fighter"),
        L("miner", 0.82, WH("miner", 500), ENTER(-1500, -250, -455, -265, 0.9, 1.5), subject=True, life="fighter"),
        L("miner_crimson", 0.82, WH("miner_crimson", 500), ENTER(1500, -250, 455, -265, 1.05, 1.65), subject=True, life="fighter"),
        L("warrior", 0.8, WH("warrior", 600), ENTER(-1500, -210, -250, -225, 1.0, 1.6), subject=True, life="fighter"),
        L("warrior_crimson", 0.8, WH("warrior_crimson", 600), ENTER(1500, -210, 250, -225, 1.15, 1.75), subject=True, life="fighter"),
    ], [C(0, x=-30), C(3.35, dolly=1.05, x=20)],
        titles=[T("TWO BANNERS.", 2.2, 3.3, size=96, y=320, track0=50, track1=12, style="slam")],
        sub=dict(text="Two banners.", at=2.25, out=3.35), vo=[(VO2, 2.25)],
        sfx=[("whoosh", 0.3), ("clang", 0.65), ("clang", 0.85), ("whoosh", 0.85), ("hit", 1.6)],
        shake=[S(0.65, 0.2, 5), S(0.85, 0.2, 5)], boardAt=2.4),
    shot("hill", 4.0, "one hill: they clash, and neither gives", [
        L("hill", 0.5, 2300, [K(0, y=-40, b=0.6), K(0.7, y=-40, b=0, ease="outCubic")], blur="hill_blur", cover=True),
        L("speed_radial", 0.0, 2112, [K(0, a=0), K(2.6, a=0), K(2.7, a=0.6), K(3.1, a=0.6), K(3.3, a=0)], flicker=True, fps=14),
        L("warrior_charge", 1.0, WH("warrior_charge", 560), [K(0, x=-1600, y=-260), K(1.0, x=-470, y=-260, ease="outBackSoft"), K(2.35, x=-510, y=-260, ease="inOutSine"),
                                                            K(2.68, x=-360, y=-260, ease="inCubic"), K(3.0, x=-400, y=-260, ease="outCubic"), K(4.0, x=-410, y=-260)],
          subject=True, jitter=3, jitterFrom=2.7, jitterTo=3.0, life="fighter"),
        L("warrior_cleave_crimson", 1.0, WH("warrior_cleave_crimson", 660), [K(0, x=1700, y=-190), K(1.2, x=520, y=-190, ease="outBackSoft"), K(2.35, x=560, y=-195, ease="inOutSine"),
                                                                            K(2.68, x=400, y=-205, ease="inCubic"), K(3.0, x=440, y=-205, ease="outCubic"), K(4.0, x=450, y=-205)],
          subject=True, jitter=3, jitterFrom=2.7, jitterTo=3.0, life="fighter"),
        L("ring", 0.8, 300, [K(0, a=0), K(2.68, y=-80, s=0.2, a=0, ease="step"), K(2.69, y=-80, s=0.25, a=1, ease="step"), K(3.4, y=-80, s=3.4, a=0, ease="outCubic")]),
        L("vignette", 0.0, 2112, [K(0, a=0.5)]),
    ], [C(0, zx=HILL_MARK[0], zy=HILL_MARK[1], x=-20), C(2.6, zoom=1.06, zx=HILL_MARK[0], zy=HILL_MARK[1], x=0),
        C(2.78, zoom=1.10, zx=0, zy=-80, ease="outExpo"), C(4.0, zoom=1.12, zx=0, zy=-80)],
        titles=[T("ONE HILL.", 0.02, 1.45, size=104, y=320, track0=60, track1=14, style="slam")],
        sub=dict(text="One hill. Neither has learned to share.", at=0.0, out=2.75),
        sfx=[("whoosh", 0.2), ("whoosh", 2.45), ("hit", 2.68), ("clang", 2.72)],
        flash=[F(2.68, 0.25, 0.55)], shake=[S(2.68, 0.5, 16)], boardAt=3.2),
    shot("choice", 5.4, "feed it, or corrupt it", [
        L("sky_choice", 0.15, SKY, [K(0)], cover=True, life="sky"),
        L("egg_glow", 0.5, round(330 * EGGK), [K(0, y=-60, s=0.6, a=0), K(0.6, y=-60, s=1, a=0.9, ease="outBackSoft")], pulseHz=1.1, pulseLo=0.55, life="glow"),
        L("egg", 0.5, round(330 * EGGK), [K(0, y=-60, s=0.6, a=0), K(0.6, y=-60, s=1, a=1, ease="outBackSoft")], subject=True, life="egg"),
        L("biomass_plants_glow", 0.8, WH("biomass_plants", 430), [K(0, x=-560, y=-900), K(0.2, x=-560, y=-900), K(0.9, x=-560, y=-365, ease="outBackSoft")], pulseHz=0.9, pulseLo=0.5, life="glow"),
        L("biomass_plants", 0.8, WH("biomass_plants", 430), [K(0, x=-560, y=-900), K(0.2, x=-560, y=-900), K(0.9, x=-560, y=-365, ease="outBackSoft")], life="plant"),
        L("corruption_growth_glow", 0.8, WH("corruption_growth", 430), [K(0, x=560, y=-900), K(2.3, x=560, y=-900), K(3.0, x=560, y=-365, ease="outBackSoft")], pulseHz=1.4, pulseLo=0.4, life="glow"),
        L("corruption_growth", 0.8, WH("corruption_growth", 430), [K(0, x=560, y=-900), K(2.3, x=560, y=-900), K(3.0, x=560, y=-365, ease="outBackSoft")], life="plant"),
        L("farmer_harvest", 0.95, WH("farmer_harvest", 540), ENTER(-1600, -205, -535, -205, 0.5, 1.2), subject=True, life="fighter"),
    ], [C(0, x=-50), C(5.4, x=50, dolly=1.05)],
        titles=[T("FEED IT", 0.45, 5.2, size=100, color=BIOMASS, x=-405, y=330, track0=40, track1=10, style="slam"),
                T("OR CORRUPT IT", 2.55, 5.2, size=80, color=CORR, x=282, y=215, track0=36, track1=8, style="slam")],
        sub=dict(text=L4, at=0.4, out=5.4), vo=[(VO4, 0.4)], sfx=[("feed", 0.45), ("corrupt", 2.55), ("bong", 2.6)],
        particles=[P("mote", BIOMASS, 0, 5.4, 16, -520, -300, 600, 200, 0, 60, 20, 30, life=3.5, size=10, depth=0.85),
                   P("ember", CORR, 2.4, 5.4, 16, 520, -300, 600, 200, 0, 90, 30, 40, life=3.0, size=12, depth=0.85)], boardAt=4.0,
        trans="dissolve", transDur=0.45),
    shot("hatch", 3.3, "the egg cracks open", [
        L("sky_storm", 0.15, SKY, [K(0)], cover=True, life="sky"),
        L("shafts", 0.25, 2200, [K(0, a=0.5)]),
        L("egg_glow", 0.5, round(440 * EGGK), [K(0, y=-40)], to=2.3, pulseHz=3.0, pulseLo=0.4, jitter=8, jitterFrom=0.5, jitterTo=2.25, glint=0.45),
        L("egg", 0.5, round(440 * EGGK), [K(0, y=-40)], to=0.51, jitter=8, jitterFrom=0.5, jitterTo=2.25, subject=True),
        L("egg_crack_1", 0.5, round(440 * EGGK), [K(0, y=-40, a=0), K(0.5, y=-40, a=0, ease="step"), K(0.51, y=-40, a=1, ease="step")], to=2.3,
          frames=["egg_crack_%d" % i for i in range(1, 5)], frameAt=0.5, fps=2.4, jitter=8, jitterFrom=0.5, jitterTo=2.25),
        L("hatchling_glow", 0.5, WH("hatchling", 560), [K(0, y=-40, s=0.85, a=0), K(2.3, y=-40, s=0.85, a=0, ease="step"), K(2.7, y=-40, s=1, a=0.9, ease="outBackSoft")],
          pulseHz=1.5, pulseLo=0.5, life="glow"),
        L("hatchling", 0.5, WH("hatchling", 560), [K(0, y=-40, s=0.85, a=0), K(2.3, y=-40, s=0.85, a=0, ease="step"), K(2.31, y=-40, s=0.85, a=1, ease="step"),
                                                   K(2.7, y=-40, s=1, ease="outBackSoft")], subject=True, life="egg"),
        L("ring", 0.5, 320, [K(0, a=0), K(2.3, y=-40, s=0.2, a=0, ease="step"), K(2.31, y=-40, s=0.3, a=1, ease="step"), K(3.0, y=-40, s=3.4, a=0, ease="outCubic")]),
        L("vignette", 0.0, 2112, [K(0, a=0.6)]),
    ], [C(0, zx=0, zy=-40), C(2.25, zoom=1.16, zy=-40, ease="inCubic"), C(2.45, zoom=1.03, zy=-40, ease="outExpo"), C(3.3, zoom=1.05, zy=-40)],
        sfx=[("rumble", 0.3), ("hatch", 2.2), ("impact", 2.3), ("debris", 2.35)], flash=[F(2.3, 0.4, 0.7, "#F4FFD9")], shake=[S(0.5, 1.8, 4), S(2.3, 0.5, 18)],
        particles=[P("ember", "#8D90A0", 2.3, 2.36, 26, 0, -40, 60, 60, 0, 120, 700, 600, g=-700, life=1.0, size=16, loop=False, depth=0.5)],
        boardAt=1.9),
    shot("kaiju", 4.4, "the Kaiju rises", [
        L("sky_storm", 0.12, SKY, [K(0)], cover=True, life="sky"),
        L("shafts", 0.25, 2200, [K(0, a=0.6)]),
        L("kaiju_neutral_glow", 0.7, WH("kaiju_neutral", 1040), [K(0, y=-1600), K(1.2, y=-150, ease="outBackSoft"), K(4.4, y=-142, s=1.03)], pulseHz=1.6, pulseLo=0.45, life="glow"),
        L("kaiju_neutral", 0.7, WH("kaiju_neutral", 1040), [K(0, y=-1600), K(1.2, y=-150, ease="outBackSoft"), K(4.4, y=-142, s=1.03)], subject=True, life="kaiju"),
        L("cloudsea_storm", 0.9, 2700, [K(0, y=-640)], life="cloud"),
        L("debris", 0.95, 2100, [K(0, y=-1200), K(1.3, y=-560, ease="outCubic"), K(2.8, y=-1350, ease="inCubic")]),
        L("speed_radial", 0.0, 2112, [K(0, a=0), K(1.2, a=0), K(1.3, a=0.6), K(2.2, a=0.6), K(2.5, a=0)], flicker=True, fps=16),
        L("rocks_r", 1.0, WH("rocks_r", 640), [K(0, x=950, y=-500)], tint="#5A6680"),
    ], [C(0, zx=KHEAD[0], zy=KHEAD[1]), C(1.2, zx=KHEAD[0], zy=KHEAD[1]), C(1.4, zoom=1.08, zx=KHEAD[0], zy=KHEAD[1], ease="outExpo"), C(4.4, zoom=1.11, zx=KHEAD[0], zy=KHEAD[1])],
        titles=[T("IT PICKS A SIDE.", 2.0, 4.25, size=84, y=-334, track0=40, track1=10, style="slam")],
        sfx=[("rumble", 0.0), ("roar", 1.2), ("debris", 1.3)], flash=[F(0.0, 0.3, 0.5), F(1.2, 0.3, 0.5)], shake=[S(0.1, 1.0, 6), S(1.2, 1.2, 20)],
        particles=[P("ember", "#8D90A0", 0.7, 1.3, 30, 0, -420, 1100, 60, 0, 520, 300, 260, g=-600, life=1.6, size=18, loop=False, depth=0.8),
                   P("mote", "#D9D2C4", 0, 4.4, 20, 0, -400, 1600, 100, 0, 90, 40, 30, life=3.0, size=9, depth=0.9)], boardAt=2.6),
    shot("sides", 3.4, "or no side at all", [
        L("@solid", 0.0, 2600, [K(0)], h=1700, tint="#141C30", cover=True),
        L("@solid", 0.4, 620, [K(0, x=-600, r=-6)], h=1500, tint="#123C55"),
        L("@solid", 0.4, 620, [K(0, x=0, r=-6)], h=1500, tint="#5A1E2C"),
        L("@solid", 0.4, 620, [K(0, x=600, r=-6)], h=1500, tint="#3A1A5C"),
        L("kaiju_neutral_glow", 0.6, WH("kaiju_neutral", 640), SLAM(-570, -900, -60, 0.0, 0.25), pulseHz=1.2, pulseLo=0.5, life="glow"),
        L("kaiju_neutral", 0.6, WH("kaiju_neutral", 640), SLAM(-570, -900, -60, 0.0, 0.25), subject=True, life="kaiju"),
        L("kaiju_crimson_glow", 0.6, WH("kaiju_crimson", 640), SLAM(0, -900, -60, 0.2, 0.45), pulseHz=1.2, pulseLo=0.5, life="glow"),
        L("kaiju_crimson", 0.6, WH("kaiju_crimson", 640), SLAM(0, -900, -60, 0.2, 0.45), subject=True, life="kaiju"),
        L("kaiju_berserk_glow", 0.6, WH("kaiju_berserk", 640), SLAM(560, -900, -60, 0.4, 0.65), pulseHz=2.0, pulseLo=0.4, life="glow"),
        L("kaiju_berserk", 0.6, WH("kaiju_berserk", 640), SLAM(560, -900, -60, 0.4, 0.65), subject=True, life="kaiju"),
        L("@solid", 0.4, 44, [K(0, x=-300, r=-6)], h=1600, tint=NAVY),
        L("@solid", 0.4, 44, [K(0, x=300, r=-6)], h=1600, tint=NAVY),
    ], [C(0), C(3.4, dolly=1.04)],
        titles=[T("OR NO SIDE AT ALL.", 1.0, 3.3, size=80, y=330, track0=40, track1=10, style="slam")],
        sfx=[("hit", 0.25), ("hit", 0.45), ("slam", 0.65), ("bong", 1.0)], shake=[S(0.25, 0.2, 5), S(0.45, 0.2, 6), S(0.65, 0.3, 10)], boardAt=2.4),
    shot("hero", 5.9, "your turn: hold the hill; the logo lands where the title's logo sits", [
        L("hero", 0.4, 2300, [K(0, b=0.0), K(2.8, b=0.0), K(3.2, b=0.35, ease="outCubic")], blur="hero_blur", cover=True),
        L("shafts_warm", 0.2, 2200, [K(0, a=0.4)], glint=0.2),
        L("vignette", 0.0, 2112, [K(0, a=0.6)]),
        L("UI/Logo/critical-mass-bible-logo-glow@2x", 0.0, LOGO_W, [K(0, s=1.6, a=0), K(2.95, s=1.6, a=0, ease="step"), K(3.18, s=0.97, a=1, ease="inCubic"),
                                                                    K(3.45, s=1.0, a=1, ease="outBackSoft")], anchor="titleLogo", pulseHz=1.0, pulseLo=0.5),
        L("UI/Logo/critical-mass-bible-logo@2x", 0.0, LOGO_W, [K(0, s=1.6, a=0), K(2.95, s=1.6, a=0, ease="step"), K(2.96, s=1.6, a=1, ease="step"),
                                                               K(3.18, s=0.97, a=1, ease="inCubic"), K(3.45, s=1.0, a=1, ease="outBackSoft")], anchor="titleLogo", subject=True),
        L("sweep", 0.0, 600, [K(0, x=-1300, a=0, r=18), K(3.6, x=-1300, a=0.5, r=18, ease="step"), K(4.4, x=1300, a=0.5, r=18, ease="inOutCubic")], h=1500),
    ], [C(0), C(5.9, dolly=1.05)],
        titles=[T("HOLD THE HILL.", 0.3, 1.35, size=116, y=40, track0=64, track1=12, style="slam"),
                T("RAISE THE KAIJU.", 1.5, 2.85, size=108, y=40, track0=60, track1=12, style="slam")],
        sub=dict(text=L5, at=0.3, out=3.0), vo=[(VO5, 0.3), (VOT, 3.25)], sfx=[("whoosh", 2.85), ("slam", 3.18), ("chime", 3.4)],
        flash=[F(0.0, 0.25, 0.45, "#FFE2B0"), F(3.18, 0.3, 0.45)], shake=[S(3.18, 0.35, 10)],
        particles=[P("ember", "#F9A858", 0, 5.9, 24, 0, -420, 1700, 80, 0, 80, 30, 40, life=4.0, size=10, depth=0.3)],
        bars_=bars((0, 1, "linear"), (3.6, 1, "linear"), (4.5, 0, "inOutCubic")), boardAt=4.0),
]

data = dict(schema="cm.story.motioncomic/2", stageW=1920, stageH=1080, letterbox=0.11, skipPromptAfter=1.5, outroSeconds=0.5,
            cues=[dict(id="whoosh", synth="whoosh_heavy", variants=2, files=["knifeSlice", "knifeSlice2"], pitch=0.8, gain=0.55),
                  dict(id="skycrack", synth="blast_far", files=["impactPlate_heavy_001"], pitch=0.75, gain=0.9),
                  dict(id="thunder", files=["impactPlate_heavy_001"], pitch=0.45, gain=0.7),
                  dict(id="impact", synth="land_heavy", files=["impactWood_heavy_000", "impactWood_heavy_001"], pitch=0.7, gain=0.85),
                  dict(id="eggland", synth="blast_near", files=["impactWood_heavy_000"], pitch=0.85, gain=0.8),
                  dict(id="debris", synth="debris", files=["impactMining_000", "impactMining_001"], pitch=0.9, gain=0.5),
                  dict(id="rumble", files=["impactSoft_heavy_000", "impactSoft_heavy_001"], pitch=0.45, gain=0.7),
                  dict(id="hit", files=["impactPunch_heavy_000", "impactPunch_heavy_001"], pitch=0.8, gain=0.7),
                  dict(id="clang", files=["impactPlate_medium_000", "impactPlate_medium_001"], pitch=0.85, gain=0.55),
                  dict(id="hatch", synth="kaiju_hatch", files=["impactMining_002"], pitch=1.0, gain=0.85),
                  dict(id="roar", synth="kaiju_roar", variants=2, files=["impactPunch_heavy_002", "impactPlate_heavy_001"], pitch=1.0, gain=0.95),
                  dict(id="slam", synth="kaiju_slam", files=["impactWood_heavy_001"], pitch=1.0, gain=0.7),
                  dict(id="feed", synth="egg_feed", files=["confirmation_003"], pitch=1.0, gain=0.6),
                  dict(id="corrupt", synth="corrupt", files=["error_004"], pitch=1.0, gain=0.6),
                  dict(id="bong", files=["bong_001"], pitch=0.6, gain=0.5),
                  dict(id="chime", files=["confirmation_002"], pitch=0.9, gain=0.45)],
            sequences=[dict(id="intro", shots=[s["id"] for s in shots]),
                       dict(id="attract", shots=[s["id"] for s in shots])],
            codex=[dict(id="relic-01", shots=["peoples"]), dict(id="relic-02", shots=["crack", "rise"]),
                   dict(id="relic-03", shots=["shrine"]), dict(id="relic-04", shots=["calm"]), dict(id="relic-06", shots=["choice"]),
                   dict(id="relic-09", shots=["hatch"]), dict(id="relic-12", shots=["hill"])],
            shots=shots)
json.dump(data, open(OUT, "w"), indent=1)
print("shots", len(shots), "seconds", round(sum(s["seconds"] for s in shots), 2), "->", OUT)
