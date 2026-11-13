"""Healer palette (proposed class, no bible page). Every entry is a bible p05/p06-p08 colour or a step of one, except
the hair, skin and lantern-light entries, which are new to this class and named as such. sRGB hex + provenance.

Same contract as miner_art.mpal (the build swaps this module in for mpal): team channels hold the AZURE value and use
the same names ("kerchief" = team cloth, "crystal"/"crystal_lit" = team glow), TEAM_SWAP holds the Crimson value, and
the vertex-colour ALPHA carries the team mask (cm_art.palette codes: 0 fixed, 1.0 team cloth, 0.75 team glow).
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__))))), "Characters", "_source~"))
from cm_art import palette as _shared  # noqa: E402

MASK_FIXED = _shared.MASK_FIXED
MASK_TEAM_CLOTH = _shared.MASK_TEAM_CLOTH
MASK_TEAM_GLOW = _shared.MASK_TEAM_GLOW

C = {
    # ---- team channels (Azure), identical values to the Miner so the five classes share one team look
    "crystal":      ("#2E9BE6", "= Miner crystal (team glow gems: brooch, lantern cap, can badge)"),
    "crystal_lit":  ("#8BE6FF", "= Miner crystal_lit (lit facet)"),
    "kerchief":     ("#3E78C6", "= Miner kerchief (team cloth: stole, bun ribbons)"),
    # ---- fixed, bible palette
    "navy":         ("#364B72", "= Miner navy (coat bodice, bell skirt)"),
    "navy_dark":    ("#2C3F62", "Miner navy_dark one step down (leggings, skirt lining)"),
    "cream":        ("#E9DECF", "P08 cream (mantle, sleeves)"),
    "linen":        ("#F4EEE2", "cream lifted (bandage wraps, hem band): reads whiter than the mantle"),
    "linen_dark":   ("#CBBFAE", "bandage grooves, 1 px inner line"),
    "brass":        ("#B8793F", "= Miner brass (can, lantern frame, buckles)"),
    "brass_dark":   ("#7E4E28", "= Miner brass_dark"),
    "leather":      ("#7D5032", "= Miner leather (apron, satchel, belt)"),
    "leather_dark": ("#5F3E2A", "= Miner leather_dark (boots soles, strap)"),
    "wood":         ("#9A6640", "= Miner wood (can handle grip)"),
    "stone":        ("#8A8481", "= Miner stone (plate backing, greave edges)"),
    "stone_light":  ("#BDB9B3", "bible p06 Warrior pale stone plates (pauldrons, breastplate, tassets, gauntlet)"),
    # ---- biomass (system colour, bible p05 lime; never team)
    "lime":         ("#9BE23A", "bible biomass lime (Farmer orb / tank glow, 07-front)"),
    "lime_light":   ("#D8FF7A", "lime highlight (sprout tips, liquid surface)"),
    "leaf":         ("#5FB82E", "lime one step darker (leaf bodies)"),
    # ---- new to the Healer
    "hair":         ("#ECE8F0", "NEW silver-white hair (the only white head of the six classes)"),
    "hair_shade":   ("#B7B0C4", "NEW cool grey-lilac hair shade / brows (kept far from corruption violet)"),
    "skin":         ("#B57650", "NEW warm brown skin"),
    "skin_shadow":  ("#93583A", "ears, under the fringe"),
    "nose":         ("#A0603F", "small nose, one step below skin"),
    "mouth":        ("#5A2320", "smile interior"),
    "teeth":        ("#FBF4EA", "= Miner teeth"),
    "lamp_glass":   ("#FFF0B8", "NEW cleansing light: warm cream-gold (opposite of corruption violet; never team, never lime)"),
    "lamp_core":    ("#FFFBE6", "NEW lantern flame core"),
    "eye_white":    ("#FFFFFF", "eye white, highlights"),
    "pupil":        ("#24161A", "= Miner pupil"),
}

TEAM = {"crystal": MASK_TEAM_GLOW, "crystal_lit": MASK_TEAM_GLOW, "kerchief": MASK_TEAM_CLOTH}

TEAM_SWAP = {   # Crimson, = Miner
    "cloth": ("#EF674E", "P08 red #EF674E"),
    "glow": ("#FA743D", "P08 orange #FA743D"),
}
TEAM_REF = {"cloth": "kerchief", "glow": "crystal"}

VOID_NAVY = "#18233B"          # CORE outline ink


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def lin(h):
    return tuple(_shared.to_lin(c) for c in hex_rgb(h))


def vcol(name):
    r, g, b = lin(C[name][0])
    return (r, g, b, TEAM.get(name, MASK_FIXED))
