"""Miner palette (v3: navy, skin, leather, beard and stone re-sampled from 08-front.png k-means at the illustration's
lit value: lit navy #384F74, tan skin #E1B68F/#E79855, glove brown #573825, warm stone #A48774/#7A6762).
Original: (bible p08, CM-CHR-003). sRGB hex + provenance.

  P08      bible p08 swatch strip (08-swatches.png), sampled pixel-exact by this build
  SAMPLED  k-means cluster of 08-front.png / 08-accent-swap.png (14 clusters), picked by role
  CORE     bible p05 printed hex

Team channels hold the AZURE value; TEAM_SWAP holds the Crimson value. The vertex-colour ALPHA carries the team mask
(cm_art.palette codes: 0 fixed, 1.0 team cloth, 0.75 team glow).
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
    "crystal":      ("#2E9BE6", "P08 blue #2A8ECB lifted toward SAMPLED crystal core #0774F7/#31D7FA; self-lit"),
    "crystal_lit":  ("#8BE6FF", "SAMPLED crystal highlight facet #31D7FA lifted; the one lighter facet on the lit side"),
    "kerchief":     ("#3E78C6", "SAMPLED neckerchief #5071A4 (lit) toward P08 blue #2A8ECB"),
    # ---- fixed
    "navy":         ("#3A5078", "P08 navy #1B2D52 lifted one step (front crop lit navy #364C70)"),
    "navy_dark":    ("#32466B", "P08 navy #1B2D52 (trousers, wraps)"),
    "cream":        ("#E9DECF", "P08 cream #E9DECF (chest plate, shoulder pads)"),
    "stone":        ("#8A8481", "P08 stone grey #807470 (crate side panels, barricade posts, hammer head)"),
    "stone_light":  ("#B4B6BC", "SAMPLED light stone #9F8576/#A68876 (pick point, post tops)"),
    "helmet":       ("#A2866F", "SAMPLED tan/stone hard hat (front crop #9F8576 shade, #D0B49E lit)"),
    "helmet_light": ("#C6A88D", "v3: lit plates of the stone hard hat (08-front k-means #E8C9AF lit / #B58D77)"),
    "helmet_seam":  ("#6E5E52", "helmet plate grooves, 25% darker than helmet tone (inner line)"),
    "brass":        ("#B8793F", "P08 brown/brass #AF723B, lit one step (trim, frames, corners)"),
    "brass_dark":   ("#7E4E28", "SAMPLED dark brass shade (bands, rivet rims)"),
    "leather":      ("#7D5032", "SAMPLED brown leather #734A2F (belt, straps, pouches, boots)"),
    "leather_dark": ("#5F3E2A", "SAMPLED dark brown #4D3324 (huge gloves, soles)"),
    "wood":         ("#9A6640", "SAMPLED crate wood (front crop #AE6937 lit / #734A2F shade)"),
    "wood_dark":    ("#5A3A24", "crate board grooves and backing, 25%+ darker (1 px inner line)"),
    "skin":         ("#EFB58A", "SAMPLED skin #EEA361"),
    "skin_shadow":  ("#DC9772", "ears, under-brow"),
    "nose":         ("#EE7F5E", "big round red nose (bible); P08 red #EF674E toward skin"),
    "beard":        ("#5B3B27", "SAMPLED thick dark-brown beard (#4D3324 / #201817 shade), v2 lifted one step so the shade band is not black"),
    "beard_light":  ("#6E4830", "v2 beard crown tone (clump tops, mustache): the bible's lit beard strands, toned form not a dark mass"),
    "brow":         ("#3E271B", "heavy brows"),
    "eye_white":    ("#FFFFFF", "eye white, highlights"),
    "pupil":        ("#24161A", "pupil"),
    "mouth":        ("#6E2A22", "grin interior"),
    "teeth":        ("#FBF4EA", "grin teeth (CORE warm cream lifted)"),
    "rope":         ("#C9A16E", "crate rope coil"),
}

TEAM = {"crystal": MASK_TEAM_GLOW, "crystal_lit": MASK_TEAM_GLOW, "kerchief": MASK_TEAM_CLOTH}

TEAM_SWAP = {   # Crimson
    "cloth": ("#EF674E", "P08 red #EF674E"),
    "glow": ("#FA743D", "P08 orange #FA743D"),
}
TEAM_REF = {"cloth": "kerchief", "glow": "crystal"}   # Azure entry whose brightness maps to the swap colour

VOID_NAVY = "#18233B"          # CORE outline ink


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def lin(h):
    return tuple(_shared.to_lin(c) for c in hex_rgb(h))


def vcol(name):
    r, g, b = lin(C[name][0])
    return (r, g, b, TEAM.get(name, MASK_FIXED))
