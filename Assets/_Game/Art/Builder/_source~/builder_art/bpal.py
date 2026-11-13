"""Builder palette (DG-08, proposed: the bible has no Builder page). sRGB hex + provenance.

  CORE     bible p05 printed hex
  P06/P08  bible p06 / p08 swatch strips (same values the Warrior and Miner builds use)
  P14      bible p14 bridge crop, exactly as the environment kit v3 samples it (env_art/palette.py), so the
           Builder's wood and rope match the world's bridges
  DESIGN   chosen for the Builder inside the bible's families (value steps of an existing swatch)

Team channels hold the AZURE value; TEAM_SWAP holds the Crimson value. Vertex-colour ALPHA carries the team mask, the
same contract as the Miner (cm_art.palette codes: 0 fixed, 1.0 team cloth, 0.75 team glow). The Biomass gem is a system
colour and stays fixed (alpha 0).
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
    # ---- team channels (Azure)
    "team":         ("#2A9DBA", "P06 teal #2A95AE lifted a step (cap band, apron trim, rope wraps, rung tips, straps)"),
    "team_glow":    ("#4FE3F0", "shared team glow (unused on the Builder body; kept so the toon swap has a glow ref)"),
    # ---- fixed: cloth
    "navy":         ("#364B72", "P08 navy lifted one step (shirt), same as the Miner's lit navy"),
    "navy_dark":    ("#2E4166", "P08 navy #1B2D52 lifted (trousers)"),
    "navy_roll":    ("#4A6290", "rolled sleeve cuff, a value step above the shirt"),
    "canvas":       ("#C9A67A", "DESIGN canvas apron: P08 cream #E9DECF toward P14 rope #D9B98A, one value step down"),
    "canvas_dark":  ("#9F7E57", "apron pockets and seams, a step darker"),
    "cream":        ("#E9DECF", "P08 cream (shirt collar, sock rolls)"),
    "cap":          ("#7C6A5A", "DESIGN warm grey-brown tweed flat cap (between P08 stone #807470 and leather)"),
    "cap_light":    ("#9A8672", "cap crown lit panels"),
    # ---- fixed: hard materials
    "leather":      ("#7D5032", "P08 sampled brown leather (belt, straps, boots)"),
    "leather_dark": ("#5F3E2A", "P08 sampled dark brown (gloves, soles)"),
    "brass":        ("#C79245", "CORE Tool Brass (rivets, buckles, mallet bands, toe caps)"),
    "brass_dark":   ("#8A5A2E", "brass shade (rivet rims, band edges)"),
    "stone":        ("#8A8481", "P08 stone grey #807470 (knee pads)"),
    "stone_light":  ("#B4B6BC", "P08 light stone (knee pad faces)"),
    "wood":         ("#C78B50", "P14 bridge planks #C78B50 (ladder rails, mallet head, A-frame)"),
    "wood_b":       ("#B27440", "P14 bridge planks #BF7C43 (alternate boards, rungs)"),
    "wood_dark":    ("#633F22", "P14 bridge #633F22 (grooves, end grain, seams; 1 px inner line)"),
    "rope":         ("#D9B98A", "P14 bridge rope (#F0D6AD shade)"),
    "rope_dark":    ("#B0905F", "rope twist groove, a step darker"),
    "crystal":      ("#B7F238", "CORE Biomass (mallet gem; system colour, never team)"),
    "crystal_lit":  ("#E4FFA0", "Biomass lit facet"),
    "pencil":       ("#F2C14E", "DESIGN carpenter's pencil (yellow, CORE brass family lifted)"),
    "graphite":     ("#3A3346", "pencil tip"),
    # ---- fixed: face
    "skin":         ("#EFB58A", "P08 sampled skin"),
    "skin_shadow":  ("#DC9772", "ears, under-brow"),
    "nose":         ("#EE8C6A", "round nose, P08 red toward skin (softer than the Miner's)"),
    "hair":         ("#8A4A2A", "DESIGN copper-brown hair and moustache (not the Miner's dark beard)"),
    "hair_light":   ("#A65E36", "moustache crown tone"),
    "brow":         ("#6A3720", "brows, a step darker than the hair"),
    "eye_white":    ("#FFFFFF", "eye white, highlights"),
    "mouth":        ("#6E2A22", "grin interior"),
    "teeth":        ("#FBF4EA", "grin teeth (CORE warm cream lifted)"),
}

TEAM = {"team": MASK_TEAM_CLOTH, "team_glow": MASK_TEAM_GLOW}

TEAM_SWAP = {   # Crimson
    "cloth": ("#E46753", "P06 coral (Crimson cloth)"),
    "glow": ("#FF7461", "shared Crimson glow"),
}
TEAM_REF = {"cloth": "team", "glow": "team_glow"}

VOID_NAVY = "#18233B"          # CORE outline ink


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def lin(h):
    return tuple(_shared.to_lin(c) for c in hex_rgb(h))


def vcol(name):
    r, g, b = lin(C[name][0])
    return (r, g, b, TEAM.get(name, MASK_FIXED))
