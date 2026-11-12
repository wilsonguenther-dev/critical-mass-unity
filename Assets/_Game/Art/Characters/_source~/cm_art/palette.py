"""Shared bible palette for every Critical Mass character (Warrior, Farmer, Miner).

Values are sRGB hex exactly as printed or sampled from Jeisil's Visual Asset Bible. Blender stores colour attributes in
linear light, so use `lin()` when writing vertex colours; the FBX export converts back to sRGB (colors_type='SRGB'), so
Unity receives the hex below byte-for-byte.

Provenance tags:
  CORE      bible p05, printed hex (D-064: swatch pixels equal the printed hex)
  P06       bible p06 swatch strip, sampled by the spec recon (ESTIMATE; spec-recon-2026-11-11.md section 4)
  SAMPLED   sampled from the p06 crops by the art build (U-021: "sampled, pending Jeisil's confirmation")
"""

# ---- core palette (bible p05, exact) ----------------------------------------------------------------------------
CORE = {
    "void_navy": "#18233B",
    "azure_team": "#16C7D9",
    "crimson_team": "#FF5A5F",
    "warm_cream": "#F7F0E5",
    "biomass": "#B7F238",
    "corruption": "#8E3FD1",
    "stone": "#6F7282",
    "tool_brass": "#C79245",
}

# ---- character palette: name -> (hex, provenance) ---------------------------------------------------------------
# Team-channel entries hold the AZURE value; the Crimson value is in TEAM_SWAP below.
CHAR = {
    # team channels (Azure values)
    "team_cloth":   ("#2A95AE", "P06 teal (scarf, tabard)"),
    "team_glow":    ("#4FE3F0", "SAMPLED glowing cyan (blade edge, shield core, armour cracks); core Azure #16C7D9 lifted"),
    "team_dark":    ("#2A374D", "reserved team-dark channel; UNUSED on the Warrior (bible accent-swap keeps the doublet navy)"),
    # fixed colours
    "navy":         ("#2A374D", "P06 navy, fixed (padded doublet, sleeves, shield core ring; stays navy on Crimson)"),
    "cream":       ("#F2E5D5", "P06 cream (quilted undershirt)"),
    "trouser":      ("#DCC6AE", "SAMPLED baggy trousers, a step darker than the undershirt (front crop cluster #D6B9A6)"),
    "leather":      ("#AB6844", "P06 rust brown (bracers, gloves, grip wrap)"),
    "leather_dark": ("#5E3B2B", "SAMPLED dark belt and straps (front crop cluster #5A382C)"),
    "boot":         ("#7E4E33", "SAMPLED boots (front crop cluster #775241)"),
    "wood":         ("#9C6040", "SAMPLED shield planks"),
    "wood_seam":    ("#6A3E2A", "SAMPLED shield plank seams"),
    "brass":        ("#C79245", "CORE Tool Brass (rivets, buckle, topknot ring, shield rings)"),
    "stone_armour": ("#B4B0AA", "SAMPLED warm light stone of pauldrons/knee guards/rim (front crop lit ~#C3AFA4, shade ~#696D6E); 0.66 shade band ~#77746F, near CORE Stone #6F7282"),
    "stone_blade":  ("#8E97AE", "SAMPLED blue-grey cleaver stone (front crop #8089A6, lit)"),
    "wrap_dark":    ("#3A3346", "SAMPLED dark shin wraps under the knee guards"),
    "skin":         ("#F7B48A", "SAMPLED warm tan skin (cheek #FBA779 lit, face mid-tones ~#F2B08A)"),
    "skin_shadow":  ("#D7865F", "SAMPLED ears / nose underside"),
    "hair":         ("#3B2622", "SAMPLED dark-brown hair (front crop #382D3E / #24191A)"),
    "brow":         ("#2C1B19", "SAMPLED thick dark eyebrows"),
    "eye_white":    ("#FFFFFF", "eye white and highlights"),
    "iris":         ("#6B3A22", "SAMPLED large brown eyes"),
    "pupil":        ("#24161A", "SAMPLED pupil"),
    "mouth":        ("#7A3328", "SAMPLED closed-mouth smirk line"),
}

# Crimson replacements for each team channel (bible p06 accent-swap crop and P06 Crimson row).
TEAM_SWAP = {
    "team_cloth": ("#E46753", "P06 coral"),
    "team_glow":  ("#FF7461", "SAMPLED red-orange glow on the accent-swap crop; core Crimson #FF5A5F lifted"),
    "team_dark":  ("#652D2F", "P06 maroon"),
}

# Outline (inverted hull) colour.
OUTLINE = "#18233B"      # Void Navy, ART-DIRECTION 2.3 (v1 used #2A1E24)

# Team-mask codes stored in the vertex-colour ALPHA channel (one colour set survives Unity's FBX import).
MASK_FIXED = 0.0
MASK_TEAM_DARK = 0.5
MASK_TEAM_GLOW = 0.75
MASK_TEAM_CLOTH = 1.0
MASK_FOR = {"team_cloth": MASK_TEAM_CLOTH, "team_glow": MASK_TEAM_GLOW, "team_dark": MASK_TEAM_DARK}


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(h):
    """sRGB hex -> linear RGB tuple."""
    return tuple(to_lin(c) for c in hex_rgb(h))


def vcol(name):
    """Vertex colour RGBA (linear RGB, alpha = team mask code) for a palette entry."""
    r, g, b = lin(CHAR[name][0])
    return (r, g, b, MASK_FOR.get(name, MASK_FIXED))


def vcol_scaled(name, k):
    """Vertex colour of `name` with its sRGB value scaled by k (k = 0.75: the 25 % darker modelled crease groove)."""
    r, g, b = hex_rgb(CHAR[name][0])
    return (to_lin(r * k), to_lin(g * k), to_lin(b * k), MASK_FOR.get(name, MASK_FIXED))
