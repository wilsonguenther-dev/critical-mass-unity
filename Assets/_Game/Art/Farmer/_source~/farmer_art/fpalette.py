"""Farmer palette (bible p07). Extends the shared cm_art.palette without editing it.

Provenance tags:
  CORE      bible p05 printed hex
  P07       bible p07 swatch strip (07-swatches.png), sampled by this build at the swatch centres:
            navy #2F3349, teal #4999A5, cream #F3E1C7, dark brown #70513D, brass #BB8148, lime #D0E26A, coral #E9705F
  SAMPLED   picked by eye from the p07 crops (front, back, backpack-tank, staff-views) by this build, pending Jeisil
"""
from cm_art import palette as P

FARMER = {
    # team channels (Azure values). Crimson values in FARMER_SWAP.
    "f_team_cloth": ("#4999A5", "P07 teal (cuffs, hair band, canisters, boot trim, goggle lenses, sleeve rolls, staff wrap)"),
    # fixed colours
    "f_navy":       ("#2F3349", "P07 navy (overalls)"),
    "f_navy_dark":  ("#262A3C", "SAMPLED overall seams / bib pocket"),
    "f_cream":      ("#EDD3AC", "P07 cream #F3E1C7 warmed toward the crop's lit jacket tone #E8C99D (v4 fit report: tan jacket)"),
    "f_cream_dark": ("#D9C3A3", "SAMPLED jacket collar underside / lapel shade"),
    "f_brown":      ("#70513D", "P07 dark brown (belt, straps, gloves)"),
    "f_glove":      ("#6A4733", "SAMPLED big work gloves (a step darker than the belt)"),
    "f_leather":    ("#9A6440", "SAMPLED pouches, boot uppers"),
    "f_boot":       ("#9A6844", "SAMPLED boots (v4: a step lighter; the fit report reads the crop's boots as wood brown)"),
    "f_sole":       ("#3A3346", "SAMPLED boot soles"),
    "f_brass":      ("#BB8148", "P07 brass (buckle, goggles, tank frame, staff bands, hook)"),
    "f_patch":      ("#A7A9A6", "SAMPLED grey knee patches"),
    "f_shaft":      ("#2E4B57", "SAMPLED dark-teal staff shaft (fixed, not team)"),
    "f_hose":       ("#2A2F45", "SAMPLED dark hose"),
    "f_frame_dark": ("#333A4E", "SAMPLED dark tank frame band and nozzle"),
    "f_blade":      ("#EFE4CC", "SAMPLED cream/bone crescent blade"),
    "f_leaf":       ("#6DB33F", "SAMPLED leaves on the tank and the seed pod"),
    "f_leaf_dark":  ("#4E8A34", "SAMPLED leaf midribs, leaves inside the globe"),
    "f_wood":       ("#8C5A33", "SAMPLED seed pod wood"),
    "f_wood_dark":  ("#5C3A22", "SAMPLED seed pod seams"),
    "f_skin":       ("#F4B48C", "SAMPLED warm skin"),
    "f_skin_dark":  ("#DC8E68", "SAMPLED nose underside"),
    "f_cheek":      ("#F28C7E", "SAMPLED rosy cheeks"),
    "f_hair":       ("#4A2F25", "SAMPLED wavy dark-brown hair (lit ~#5A3A2C, shade ~#2E1C18)"),
    "f_brow":       ("#3A231B", "SAMPLED brows"),
    "f_iris":       ("#7A4A2A", "SAMPLED big brown eyes"),
    "f_pupil":      ("#24161A", "pupil"),
    "f_white":      ("#FFFFFF", "eye whites, highlights"),
    "f_mouth":      ("#8A3A30", "SAMPLED smile"),
    # fixed self-lit glow (never team): biomass
    "f_biomass":    ("#B7F238", "CORE Biomass, self-lit, fixed on both teams"),
    "f_biomass_hi": ("#D0E26A", "P07 lime swatch, used as the lighter rim facet / bulb highlight"),
}

FARMER_SWAP = {"f_team_cloth": ("#E9705F", "P07 coral")}

VOID_NAVY = P.CORE["void_navy"]           # outline, ART-DIRECTION 2.3 (the Warrior v1 used #2A1E24)

# Mask codes in the vertex-colour alpha. cm_art defines 0 / 0.5 / 0.75 / 1.0. The Farmer adds one:
MASK_FIXED_GLOW = 0.25    # self-lit, unshaded, NEVER team-swapped (biomass). New code: CM/Toon must learn it.
MASK_FOR = {"f_team_cloth": P.MASK_TEAM_CLOTH, "f_biomass": MASK_FIXED_GLOW, "f_biomass_hi": MASK_FIXED_GLOW}


def hexof(name):
    if name in FARMER:
        return FARMER[name][0]
    return P.CHAR[name][0]


def mask_of(name):
    return MASK_FOR.get(name, P.MASK_FOR.get(name, P.MASK_FIXED))


def vcol(name, k=1.0):
    """Linear RGBA; k scales the sRGB value (painted gradient, ART-DIRECTION 3.5); alpha = mask code."""
    r, g, b = (min(1.0, c * k) for c in P.hex_rgb(hexof(name)))
    return (P.to_lin(r), P.to_lin(g), P.to_lin(b), mask_of(name))
