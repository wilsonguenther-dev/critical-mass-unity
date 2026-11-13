"""Demolitions Expert palette (DG-07, PROPOSED: the bible has no page for this class).

Every value is taken from, or one value step off, a colour the bible already uses on the three class pages or the p05
core strip, so the class sits in the same world as the Warrior, Farmer and Miner. sRGB hex + provenance.

  CORE     bible p05 printed hex (cm_art.palette.CORE)
  MINER    the Miner v3 palette (miner_art/mpal.py: P08 swatches and 08-front k-means), reused so team values match
  P06/P07  Warrior / Farmer palette entries
  NEW      chosen for this class, with the reason

Team channels hold the AZURE value; TEAM_SWAP holds the Crimson value. The vertex-colour ALPHA carries the team mask
(cm_art.palette codes, identical to the Miner: 0 fixed, 1.0 team cloth, 0.75 team glow).
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
    # ---- team channels (Azure), same values as the Miner so both classes swap identically
    "crystal":      ("#2E9BE6", "MINER team glow (goggle lenses, TNT-in-hand gem-free; lens glass)"),
    "crystal_lit":  ("#8BE6FF", "MINER lit facet (goggle lens catch light)"),
    "kerchief":     ("#3E78C6", "MINER team cloth (scarf, armband, keg middle hoop, TNT wrapper bands, glove cuffs)"),
    # ---- fixed: cloth
    "navy":         ("#364B72", "MINER navy (shirt under the vest, sleeves of the undershirt)"),
    "navy_dark":    ("#32466B", "MINER navy trousers"),
    "cream":        ("#E9DECF", "MINER/P08 cream (rolled shirt sleeves, TNT end papers)"),
    "vest":         ("#A3A7B2", "NEW padded vest in Stone: CORE stone #6F7282 lifted two steps to the bible's pale cool stone"),
    "vest_seam":    ("#646878", "quilting grooves under the vest pads (CORE stone #6F7282 one step down): the inner lines"),
    # ---- fixed: hard materials
    "brass":        ("#B8793F", "MINER brass (hoops, buckles, goggle rims, toe caps)"),
    "brass_light":  ("#D9A45A", "CORE Tool Brass #C79245 lifted one step (lit hoop tops, buckle faces)"),
    "brass_dark":   ("#7E4E28", "MINER dark brass (rivet rims, bands)"),
    "stone":        ("#8A8481", "MINER stone (boot toe plates, mine pebbles)"),
    "stone_light":  ("#B4B6BC", "MINER light stone"),
    "wood":         ("#A0683F", "MINER crate wood #9A6640 one step warmer (keg staves)"),
    "wood_light":   ("#BC8150", "keg staves, alternate lit tone"),
    "wood_dark":    ("#5A3A24", "MINER wood_dark (keg core between staves, lid boards' grooves)"),
    "leather":      ("#7D5032", "MINER leather (belt, straps, cap, boots)"),
    "leather_light": ("#9A6844", "cap panel tops, one step up"),
    "leather_dark": ("#5F3E2A", "MINER dark leather (gloves, soles, bandolier)"),
    # ---- fixed: explosives (TNT red kept well away from the Crimson team hue: darker, browner; dE2000 to #FF5A5F > 20)
    "tnt":          ("#A2402F", "NEW dynamite brick red: darker and browner than Crimson #FF5A5F / P06 coral #E46753"),
    "tnt_dark":     ("#74291F", "TNT shade / stick grooves"),
    "fuse":         ("#3B2D27", "NEW dark tarred fuse cord"),
    "spark_core":   ("#FFF7DF", "CORE Warm Cream #F7F0E5 lifted: hazard spark core (never team colour)"),
    "spark":        ("#F7D58A", "Warm Cream toward Tool Brass: spark rays"),
    "spark_tip":    ("#D9A45A", "Tool Brass lifted: spark ray tips"),
    # ---- fixed: skin and face
    "skin":         ("#EFB58A", "MINER skin"),
    "skin_shadow":  ("#DC9772", "MINER ears / under-brow"),
    "soot":         ("#8E6E5F", "NEW soot smudge: skin two value steps down and greyed (vertex-colour value step)"),
    "soot_dark":    ("#5E4A44", "NEW heavy soot (nose tip, under the goggles)"),
    "nose":         ("#E98A68", "rosy nose, MINER nose toward skin"),
    "hair":         ("#4A2E22", "P06 hair #3B2622 one step up (puffed pigtails, tufts under the cap)"),
    "hair_light":   ("#64402F", "lit hair puffs, one step up (value step between the pigtail puffs)"),
    "lips":         ("#B65D57", "NEW muted rose lips (darker and greyer than Crimson #FF5A5F so it never reads as team)"),
    "hair_singed":  ("#8B7B70", "NEW singed hair and brow tips: ash grey-brown"),
    "brow":         ("#3E271B", "MINER brows"),
    "mouth":        ("#6E2A22", "MINER grin interior"),
    "tongue":       ("#C9605A", "tongue, MINER mouth lifted"),
    "teeth":        ("#FBF4EA", "MINER teeth"),
    # ---- fixed: props
    "dirt":         ("#7E5D40", "NEW loose dirt (mine mound), between MINER leather and wood"),
    "dirt_light":   ("#A27C56", "loose dirt, lit clods"),
    "dirt_dark":    ("#563E2C", "loose dirt, shade clods / crater inside"),
    "char":         ("#2B2A33", "scorch on the triggered mound (Void Navy toward grey)"),
    "smoke":        ("#9EA1AE", "explosion smoke: CORE stone #6F7282 lifted (art direction 6.1 Stone smoke)"),
    "meat":         ("#F06C47", "art direction 6.1 explosion rim Meat #F06C47"),
    "boom_core":    ("#F7F0E5", "CORE Warm Cream: explosion core"),
    "boom_body":    ("#C79245", "CORE Tool Brass: explosion body"),
    "crater":       ("#18233B", "CORE Void Navy: crater rim decal (drawn at 30 % in Unity)"),
}

TEAM = {"crystal": MASK_TEAM_GLOW, "crystal_lit": MASK_TEAM_GLOW, "kerchief": MASK_TEAM_CLOTH}

TEAM_SWAP = {   # Crimson, same as the Miner
    "cloth": ("#EF674E", "P08 red #EF674E"),
    "glow": ("#FA743D", "P08 orange #FA743D"),
}
TEAM_REF = {"cloth": "kerchief", "glow": "crystal"}

VOID_NAVY = "#18233B"


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def lin(h):
    return tuple(_shared.to_lin(c) for c in hex_rgb(h))


def vcol(name):
    r, g, b = lin(C[name][0])
    return (r, g, b, TEAM.get(name, MASK_FIXED))
