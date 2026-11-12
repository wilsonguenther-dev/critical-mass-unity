"""Environment palette for the Skybreak Basin floating-ruins kit.

TO BE MERGED into the shared palette module (cm_art/palette.py on branch art/warrior-blender). Kept here, small and
separate, so this branch does not edit that file. Mask codes and the lin()/vertex-colour convention are identical to
that module: RGB is the sRGB hex stored in linear light (the FBX export writes it back as sRGB), ALPHA is the mask code.

Provenance tags:
  CORE     bible p05 printed hex (exact)
  P14      sampled from the bible p14 prop crops (median-cut quantise of ~/critical-mass/loop/parity/ref/14-*.png)
  P12      sampled from the bible p12 Skybreak Basin map crop
  P15      sampled from the bible p15 gameplay mock (world area)
Lit values are what a lit (top) face shows; the CM/Toon shade band is lit x SHADE.
"""

CORE = {
    "void_navy": "#18233B", "azure_team": "#16C7D9", "crimson_team": "#FF5A5F", "warm_cream": "#F7F0E5",
    "biomass": "#B7F238", "corruption": "#8E3FD1", "stone": "#6F7282", "tool_brass": "#C79245",
}

ENV = {
    # ground tops
    "grass":        ("#A3C150", "P12 grass tops (#BFC567 lit / #708D4C) and P14 tiles grass, mid value"),
    "grass_dark":   ("#6F9A3E", "P14 bridge-crop moss #708D4C, tufts and grass lips"),
    "moss":         ("#8FAE48", "P14 wall/ramp moss (#D6CB8E highlight, #6E6D4F shade), mid value"),
    "path":         ("#E6C597", "P12 road flagstones #ECC799 / P14 tile #CDAF8F"),
    "path_b":       ("#D8B78C", "P14 tile #C3A78A / P15 path #EEAB82 mid"),
    "path_c":       ("#EBD2A6", "P12 road highlight #F8D9AE"),
    "grout":        ("#A08A68", "P14 tile seams #645D49 / #A29173, the lighter joint (dark joints made a grid read)"),
    "dirt":         ("#D39A5C", "P14 dirt tile #D99855"),
    "dirt_dark":    ("#A8743F", "P14 dirt tile #BC8A48 shade"),
    "corrupt":      ("#6E4F70", "P14 corrupted tile #876A7A / #624F69"),
    "corrupt_dark": ("#402547", "P14 corrupted tile #402547"),
    "corrupt_glow": ("#D23F8E", "P14 corrupted tile cracks #AD3872, lifted (self-lit)"),
    # rock and stone
    "rock":         ("#8A826D", "P15 cliff faces #9E968D / P14 #908D6A"),
    "rock_mid":     ("#74705D", "P14 hill/wall #959164 shade side"),
    "rock_dark":    ("#53564F", "P14 bridge pier / hill #5B6059"),
    "crevice":      ("#444541", "P14 wall crop #444541"),
    "wall":         ("#B0A58C", "P14 wall intact #AB9274 / #938A64, mid"),
    "wall_b":       ("#9C9479", "P14 wall intact #938A64"),
    "dais":         ("#C9B58E", "P14 hill/respawn dais #CFB283 / #B9B27E"),
    "dais_dark":    ("#8E8A6A", "P14 hill dais #959164"),
    # wood and rope
    "wood":         ("#C78B50", "P14 bridge planks #C78B50"),
    "wood_b":       ("#B27440", "P14 bridge planks #BF7C43"),
    "wood_dark":    ("#633F22", "P14 bridge #633F22"),
    "rope":         ("#D9B98A", "P14 bridge rope #F0D6AD shade"),
    # water, sky, cloud
    "water":        ("#6FC0EC", "P15 waterfall #82C4EE / #57A1D1"),
    "water_light":  ("#C4E4F4", "P15 foam #B9DCF1"),
    "cloud":        ("#F7F9F6", "P12 cloud sea #F8F9F7"),
    "cloud_shade":  ("#BFD6E2", "P12 cloud shade #DFECF6 / #A1B3AC"),
    "sky":          ("#9BC6D9", "P15 sky #9BC6D0"),
    "sky_floor":    ("#B4DAEE", "P12 / P15 sky seen between islands (#9BC6D0 .. #DFECF6), mid value"),
    # foliage
    "leaf":         ("#5E9A45", "P15 tree #487C41 / P12 #66936A"),
    "leaf_light":   ("#86BD4E", "P12 tree highlight #B4D499 shade"),
    "leaf_dark":    ("#3F6E36", "P15 tree #2A562C lifted"),
    "trunk":        ("#7E4E3A", "P12 trunk #874D41"),
    # shrine and objective
    "egg":          ("#3A5257", "P14 shrine egg #3C5C5D"),
    "egg_dark":     ("#273A40", "P14 shrine egg #274145"),
    "egg_glow":     ("#4FDDC3", "P14 shrine seam glow #4FDDC3 (self-lit, fixed teal: the neutral egg)"),
    "cream":        ("#F7F0E5", "CORE Warm Cream (hill medallion, banner emblem)"),
    "brass":        ("#C79245", "CORE Tool Brass (crown, post caps)"),
    # v2 (ART-DIRECTION 2026-11-12 sections 2.4, 2.5, 5.1)
    "grass_l":      ("#ACCB56", "AD 2.5 grass cell tone light"),
    "grass_d":      ("#96B249", "AD 2.5 grass cell tone dark"),
    "seam":         ("#7E9A3F", "AD 2.5 grass cell seam"),
    "ink":          ("#18233B", "CORE Void Navy, ledge ink line (self-lit fixed so it never shades)"),
    "band":         ("#53564F", "AD 5.1 ledge face band, rock dark (self-lit fixed)"),
    "foam":         ("#F7F9F6", "AD 2.5 water foam band (self-lit fixed)"),
    "water_f":      ("#6FC0EC", "AD 2.5 water, fixed (self-lit)"),
    "water_df":     ("#57A1D1", "P15 waterfall dark streak, fixed"),
    "water_lf":     ("#C4E4F4", "P15 waterfall light streak, fixed"),
    # v3 (owner 2026-11-12: bolder, sharper; rock one step darker for value contrast against grass and paving)
    "leaf_hi":      ("#B4DB6A", "v3 painted highlight shape on the sun side of each canopy lobe"),
    "crystal_hi":   ("#EAFCFF", "v3 painted highlight streak near crystal tips (self-lit, both teams)"),
    "rim_rock":     ("#8B826C", "v3 rim rock band (chamfer under every island rim), lit stone"),
    "rim_rock_b":   ("#7A7261", "v3 rim rock band, second stone tone"),
    "sky_horizon":  ("#CDEFF8", "CORE world Sky Horizon (height fog)"),
    "biomass_c":    ("#B7F238", "CORE Biomass, crystal lit facet (self-lit)"),
    "biomass_cd":   ("#6E9A2A", "Biomass crystal body (Biomass x0.6)"),
    "corrupt_c":    ("#B67AE8", "CORE Corruption lifted, crystal lit facet (self-lit)"),
    "corrupt_cd":   ("#5E2C8C", "Corruption crystal body"),
    "flower":       ("#F7F0E5", "CORE Warm Cream, flower heads"),
    "flower_y":     ("#F2C94C", "flower heads, warm yellow"),
    # team channels (AZURE value here; crimson value in TEAM_SWAP)
    "team_glow":    ("#4FE3F0", "same as cm_art team_glow: glow rim, glyphs, crystal lit facets"),
    "team_cloth":   ("#127EC0", "P14 azure banner cloth #127EC0"),
    "team_dark":    ("#1D6A8C", "P14 respawn crystals / azure hill shade, crystal dark facets"),
}

TEAM_SWAP = {
    "team_glow":  ("#FF7461", "same as cm_art TEAM_SWAP team_glow"),
    "team_cloth": ("#FC725A", "P14 crimson banner cloth #FC725A"),
    "team_dark":  ("#9B4132", "P14 crimson banner shade #9B4132 / hill #843B41"),
}

OUTLINE = "#18233B"   # v2: Void Navy (ART-DIRECTION 2.3); v1 used cm_art's #2A1E24
SHADE = 0.66
RAMP_STEP = 0.42

# Mask codes in vertex ALPHA. 0 / 0.5 / 0.75 / 1.0 are cm_art's. 0.25 is NEW (proposed for the shared module):
# a fixed-colour self-lit part (egg seams, corruption cracks, waterfall foam) that ignores the light ramp.
MASK_FIXED = 0.0
MASK_FIXED_GLOW = 0.25
MASK_TEAM_DARK = 0.5
MASK_TEAM_GLOW = 0.75
MASK_TEAM_CLOTH = 1.0
MASK_FOR = {"team_cloth": MASK_TEAM_CLOTH, "team_glow": MASK_TEAM_GLOW, "team_dark": MASK_TEAM_DARK,
            "egg_glow": MASK_FIXED_GLOW, "corrupt_glow": MASK_FIXED_GLOW, "ink": MASK_FIXED_GLOW,
            "band": MASK_FIXED_GLOW, "foam": MASK_FIXED_GLOW, "water_f": MASK_FIXED_GLOW,
            "water_df": MASK_FIXED_GLOW, "water_lf": MASK_FIXED_GLOW, "biomass_c": MASK_FIXED_GLOW,
            "corrupt_c": MASK_FIXED_GLOW, "crystal_hi": MASK_FIXED_GLOW}


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(h):
    return tuple(to_lin(c) for c in hex_rgb(h))


def vcol(name, mul=1.0):
    """RGBA for a palette entry: linear RGB (optionally darkened by mul in sRGB), alpha = mask code."""
    r, g, b = hex_rgb(ENV[name][0])
    r, g, b = (to_lin(min(1.0, c * mul)) for c in (r, g, b))
    return (r, g, b, MASK_FOR.get(name, MASK_FIXED))
