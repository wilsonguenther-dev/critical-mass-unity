"""Bases and structures palette (art direction 2026-11-12 sections 2.4 and 4.3).

Same vertex-colour contract as the environment kit (env_art/palette.py) and cm_art: RGB = the sRGB hex stored in
linear light (the FBX export writes it back as sRGB), ALPHA = CM/Toon mask code. Team regions are authored in AZURE;
the game swaps them per team (Crimson) or to the neutral set by the team value. Stone, wood and brass are never
team-masked.
"""

MASK_FIXED = 0.0
MASK_FIXED_GLOW = 0.25   # self-lit, fixed colour (flames, door glow, egg seam)
MASK_TEAM_DARK = 0.5     # team colour, shaded (roof undersides, crystal dark facets, sword on banner)
MASK_TEAM_GLOW = 0.75    # team colour, self-lit (crystals, glyphs, hill rim)
MASK_TEAM = 1.0          # team colour, shaded (roofs, cone caps, merlon caps, banners, pennants)

HEX = {
    # wall body (dark) and caps (light), art direction 2.4 rule 2
    "body_a": "#5B5E58", "body_b": "#63665F", "body_c": "#6F7282",
    "cap": "#C9BFA6", "cap_side": "#B0A58C",
    # warm bible stone (p14 wall crops) for breakable sections, horns, pads and daises
    "warm_a": "#9C9479", "warm_b": "#8E876E", "warm_c": "#A59D84", "warm_d": "#7E775F", "body_d": "#575A63",
    # v2 block tops: warm tan (bible p14 chunky stone tops #AB9274 .. #D8C9A0), cool-leaning variants for dark walls
    "top_a": "#C2AE86", "top_b": "#B49E78", "top_c": "#CDBB93", "top_cool_a": "#A9A28C", "top_cool_b": "#9E9A8A",
    "grass": "#A3C24F", "grass_d": "#7FA43F",
    "dais": "#C9B58E", "dais_d": "#8E8A6A", "pave": "#B9B19C", "pave_d": "#A39B86",
    "moss": "#8FAE48", "moss_d": "#6F9A3E",
    "wood": "#C78B50", "wood_b": "#B27440", "wood_d": "#633F22", "rope": "#D9B98A",
    "brass": "#C79245", "brass_d": "#9A6E33", "iron": "#4A4E57",
    "cream": "#F7F0E5", "white": "#FFFFFF", "navy": "#18233B",
    "flame": "#FFC27A", "flame_out": "#FF9F4A", "coal": "#5A3A2A",
    "door_lo": "#F7F0E5", "door_hi": "#FFC27A",
    "egg": "#3A5257", "egg_d": "#273A40", "egg_glow": "#4FDDC3",
    "water": "#6FC0EC", "foam": "#F7F9F6",
    "stone_neutral": "#6F7282",
    # team (Azure authored)
    "team": "#16C7D9", "team_dark": "#0E8E9E", "team_glow": "#7FF0FA",
}

TEAM_SETS = {
    "azure":   {"team": "#16C7D9", "team_dark": "#0E8E9E", "team_glow": "#7FF0FA"},
    "crimson": {"team": "#FF5A5F", "team_dark": "#B83A44", "team_glow": "#FF7C7C"},
    # neutral: Stone caps, Brass trim (rule 4.3); glow regions go Brass
    "neutral": {"team": "#6F7282", "team_dark": "#55586A", "team_glow": "#C79245"},
}

MASK_OF = {"team": MASK_TEAM, "team_dark": MASK_TEAM_DARK, "team_glow": MASK_TEAM_GLOW,
           "flame": MASK_FIXED_GLOW, "flame_out": MASK_FIXED_GLOW, "door_lo": MASK_FIXED_GLOW,
           "door_hi": MASK_FIXED_GLOW, "egg_glow": MASK_FIXED_GLOW, "foam": MASK_FIXED_GLOW}

# CM/Toon three-band ramp (art direction 2.2)
BAND_LIT, BAND_MID, BAND_SHADOW, SHADOW_NAVY_MIX = 1.0, 0.84, 0.66, 0.12
TOP_LIFT = 1.0 / 0.84    # a block's top face is one band lighter than its sides


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(h):
    return tuple(to_lin(c) for c in hex_rgb(h))


def vc(name, mul=1.0):
    """RGBA for a palette entry: linear RGB (optionally scaled by mul in sRGB), alpha = mask code."""
    hx = HEX.get(name, name)
    r, g, b = (to_lin(min(1.0, c * mul)) for c in hex_rgb(hx))
    return (r, g, b, MASK_OF.get(name, MASK_FIXED))


def lerp_vc(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(4))
