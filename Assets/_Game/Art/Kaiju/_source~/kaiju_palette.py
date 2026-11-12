"""Kaiju palette: one place for every Kaiju colour (v1, 2026-11-12).

Status: SAMPLED from the bible, pending the owner's confirmation (U-021). Re-sampled 2026-11-12 with 10-colour k-means
over ~/critical-mass/loop/parity/ref/11-front.png (slate #415066, light stone #ADA59B, belly shade #857D6C = belly
lit x 0.66, moss #B7D139 / #647034, cyan #0FECF0), 10-egg.png (egg plates #46494B / #83868C, lime #B1DE2E / #E7F14E),
10-berserk-adult.png (magenta #E60DA6 / #EE55CF) and 10-telegraph.png (red #E63E52 / #F6716B).

CM/Toon contract (ART-DIRECTION 2.2, 3.4): ONE material per mesh ("CM_Toon"). Colour lives in the vertex colour
layer "Col": RGB = the LIT colour of the part (sRGB, with the baked +6 % top / -10 % crevice gradient), ALPHA = mask
code: 0.0 fixed colour (lit by the three-band ramp), 0.75 team glow (self-lit, unshaded, never outlined).
Team-glow RGB is the stage's DEFAULT glow colour times a facet value k (0..1). At runtime CM/Toon swaps the hue:
    out = allegiance_glow * (max(rgb) / max(default_glow))
so one mesh serves Neutral (lime), Azure, Crimson and Berserk; only glow and crystal colours change (bible p10:
"one anatomy for every outcome, allegiance changes only glow and crystal colour").
"""

VOID_NAVY = "#18233B"      # every ink line (ART-DIRECTION 2.3)
OUTLINE = VOID_NAVY
GROUND_GREY = "#8C8C8C"    # plain mid-grey render ground (ART-AGENT-RULES deliverable 2a)
BACKGROUND = "#FEF7ED"     # bible page cream, measured on the crops, used only for side-by-side sheets

# Fixed (lit by the ramp) slot -> lit sRGB hex. The three bands are lit x1.00, mid x0.84, shadow x0.66 then 12 %
# toward Void Navy (ART-DIRECTION 2.2); they are computed by the shader, so only the lit colour is stored.
BASE = {
    "Slate":      "#415066",  # boulder plates (11-front k-means 23 %)
    "SlateLight": "#ADA59B",  # light stone faces: fist fronts, some shoulder plates (11-front 14 %)
    "Skin":       "#36425A",  # smooth head / snout hide, a touch darker and bluer than the plates (head-crop k-means)
    "Moss":       "#B4CF37",  # moss on up-facing plates (lit #B7D139, shade #647034)
    "Belly":      "#C9BFAA",  # tan segmented belly, muzzle and jaw (x0.66 = #857E70, sampled shade #857D6C)
    "Eye":        "#E4F046",  # yellow eyes
    "Pupil":      "#18233B",  # pupils, nose, mouth line: Void Navy (the darkest value on screen is the ink)
    "Mouth":      "#7A2E3A",  # mouth interior
    "Teeth":      "#F7F0E5",  # p05 Warm Cream
}
# per-stage overrides of fixed colours (the egg's plates are a neutral grey on p10)
STAGE_BASE = {
    "Egg": {"Slate": "#484C52", "SlateLight": "#83868C"},
}

# Team-glow slots: facet value k applied to the default glow colour (unlit, so the facets are painted in).
GLOW_K = {
    "Glow":        1.00,   # cracks between plates, egg seams, the egg's ground glow
    "GlowHalo":    0.72,   # v2: the short crack walls of every plate: the glow seen in a crack, a step dimmer than
                           # the crack floor, so a crack reads as a lit seam with a soft rim (ART-DIRECTION 2.3)
    "CrystalTip":  1.00,   # crystal tip facets (face the sky)
    "Crystal":     0.86,   # crystal side facets, lit side
    "CrystalDark": 0.64,   # crystal side facets, alternate (reads as the shaded facet)
    "CrystalBase": 0.50,   # crystal root ring, where it enters the plate
}
TEAM_SLOTS = set(GLOW_K)

# Allegiance glow colours (sRGB). Neutral = egg and hatchling lime. Stage default glow is what is baked in RGB.
GLOW = {
    "Neutral": "#B8E833",  # egg seams, hatchling spikes (10-egg #B1DE2E / #E7F14E)
    "Azure":   "#13F1F4",  # 11-front cyan #0FECF0 (p05 Azure Team #16C7D9 is the darker cousin)
    "Crimson": "#F2563F",  # 10-crimson-adult orange-red (p05 Crimson Team #FF5A5F)
    "Berserk": "#EE2CC0",  # 10-berserk-adult magenta #E60DA6 / #EE55CF (p05 Corruption #8E3FD1 is the violet end)
}
STAGE_DEFAULT_GLOW = {"Egg": "Neutral", "Hatchling": "Neutral", "Adult": "Azure", "Shards": "Azure"}

# Berserk state (bible p10 berserk adult, decided 2026-11-12: follow the bible, the plates DARKEN). Sampled with 10-colour
# k-means over 10-berserk-adult.png: plates #442E5A (azure adult #3E4F60), lit faces #6A4A85 (azure #A29990), belly
# #9A6660 / #CF8C9E (azure #C9BFAA). CM/Toon contract: a material float _Berserk (0 or 1, the game sets it with the
# Berserk state, same mesh) applied to FIXED colours (alpha 0) before the bands, in sRGB:
#     fixed' = lerp(fixed, lerp(fixed * BERSERK_MUL, CORRUPTION, BERSERK_TINT), _Berserk)
# Team-glow faces only take the Berserk allegiance glow (magenta) as before.
BERSERK_MUL = (0.82, 0.56, 0.78)
BERSERK_TINT = 0.15
CORRUPTION = "#8E3FD1"     # p05 Corruption


def berserk_fixed(h):
    """sRGB hex of a fixed colour as it renders in the Berserk state (for checks and swatches)."""
    c = srgb(h)
    t = srgb(CORRUPTION)
    out = [(x * m) * (1 - BERSERK_TINT) + tt * BERSERK_TINT for x, m, tt in zip(c, BERSERK_MUL, t)]
    return "#%02X%02X%02X" % tuple(round(255 * v) for v in out)


TELEGRAPH_RED = "#E63E52"  # 10-telegraph spikes (k-means 14 %); fixed colour, never a team colour
TELEGRAPH_RED_LIGHT = "#F6716B"

MASK_FIXED = 0.0
MASK_TEAM_GLOW = 0.75
ALLEGIANCES = ["Neutral", "Azure", "Crimson", "Berserk"]
SLOTS = list(BASE) + list(GLOW_K) + ["Telegraph"]
MATERIAL = "CM_Toon"


def srgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def lin1(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(h):
    return tuple(lin1(c) for c in srgb(h))


def slot_rgb(stage, slot):
    """sRGB 0..1 lit colour baked for a slot (team slots: default glow x k)."""
    if slot in GLOW_K:
        g = srgb(GLOW[STAGE_DEFAULT_GLOW[stage]])
        return tuple(c * GLOW_K[slot] for c in g)
    if slot == "Telegraph":
        return srgb(TELEGRAPH_RED)
    return srgb(STAGE_BASE.get(stage, {}).get(slot, BASE[slot]))


def slot_alpha(slot):
    return MASK_TEAM_GLOW if slot in TEAM_SLOTS else MASK_FIXED
