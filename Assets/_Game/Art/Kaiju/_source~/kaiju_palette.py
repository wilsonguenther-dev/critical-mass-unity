"""Kaiju palette: one place for every Kaiju colour.

Status: SAMPLED, pending Jeisil's confirmation (U-021). To be merged into the shared palette module on branch
art/warrior-blender once that lands; until then the Kaiju build reads only this file.

How the numbers were taken (2026-11-12): 12-colour k-means over the bible reference crops in
~/critical-mass/loop/parity/ref (150 dpi cuts of bible pp 10-11), background pixels (min channel > 225) dropped.
  slate / stone / moss / belly / azure glow and crystal : 11-front.png, 11-three-quarter.png
  crimson glow and crystal                              : 10-crimson-adult.png
  berserk plates, glow and crystal                      : 10-berserk-adult.png
  neutral (lime) glow and crystal                       : 10-egg.png, 10-hatchling.png
Core p05 hexes (exact, D-064) are noted beside the sampled value they correspond to.

Each slot has a LIT and a SHADE colour: CM/Toon draws every pixel in one of two flat bands (two-step ramp), and the
bible's two bands differ in hue as well as value (grey-lit plates fall to navy in shade), so both are stored rather
than a shade multiplier. GLOW is unlit: one flat colour in both bands.

Material slots are allegiance-agnostic (KJ_<Slot>). An allegiance is a set of colours for the same slots; only
Glow, Crystal and CrystalBase change between Azure, Crimson and Neutral (bible p10: "allegiance changes only glow and
crystal colour"). Berserk additionally darkens and tints the plates and belly (bible p10 berserk: "darker plates").
"""

# slot -> (lit, shade). Shared by every allegiance unless the allegiance overrides it.
BASE = {
    "Slate":       ("#46566E", "#2E393D"),  # dark blue-grey boulder plates (11-front: lit between #3B4B62 and #586277, shade #2E393D)
    "SlateLight":  ("#96908C", "#586277"),  # grey stone faces, fist fronts (p05 Stone #6F7282 is between them)
    "Moss":        ("#B4CF37", "#5F6D30"),  # moss on up-facing plates (p05 Biomass #B7F238 is the brighter cousin)
    "Belly":       ("#C9BFAA", "#877D61"),  # tan segmented chest / belly, muzzle and jaw
    "Eye":         ("#E4F046", "#B4C832"),  # yellow-green eyes
    "Pupil":       ("#12171A", "#12171A"),  # pupils, nose, mouth line (darkest sampled cluster)
    "Mouth":       ("#7A2E3A", "#4E1C26"),  # mouth interior (roar)
    "Teeth":       ("#F7F0E5", "#C9BFAA"),  # p05 Warm Cream
}

ALLEGIANCE = {
    "Neutral": {   # egg seams and hatchling spikes are lime (bible p10)
        "Glow":        ("#C8EE35", "#C8EE35"),
        "Crystal":     ("#A6DC35", "#73AC2C"),
        "CrystalBase": ("#DFF456", "#A6DC35"),
    },
    "Azure": {     # p05 Azure Team #16C7D9
        "Glow":        ("#13F1F4", "#13F1F4"),
        "Crystal":     ("#3FEAF2", "#1099A9"),
        "CrystalBase": ("#B4CF37", "#73AC2C"),  # "cyan-to-lime crystal cluster" (p11)
    },
    "Crimson": {   # p05 Crimson Team #FF5A5F; the sheet paints the Crimson crystals orange-red
        "Glow":        ("#F6784D", "#F6784D"),
        "Crystal":     ("#EF483D", "#B83533"),
        "CrystalBase": ("#F8AE6A", "#F6784D"),
    },
    "Berserk": {   # p05 Corruption #8E3FD1 is the violet end; the sheet paints magenta
        "Glow":        ("#F244D7", "#F244D7"),
        "Crystal":     ("#F10BAB", "#8B0B6D"),
        "CrystalBase": ("#6D4D88", "#493160"),
        "Slate":       ("#493160", "#35193D"),
        "SlateLight":  ("#6D4D88", "#493160"),
        "Moss":        ("#6D4D88", "#35193D"),
        "Belly":       ("#C9A49A", "#95625C"),
    },
}

OUTLINE = "#1A1F2B"        # line colour: the bible's contour is dark navy-brown, not black (spec recon section 2)
BACKGROUND = "#FBF6EC"     # bible page cream, used only to composite parity images

# Slot order is the material slot order in every Kaiju mesh and FBX.
SLOTS = ["Slate", "SlateLight", "Moss", "Belly", "Glow", "Crystal", "CrystalBase", "Eye", "Pupil", "Mouth", "Teeth"]
ALLEGIANCES = ["Neutral", "Azure", "Crimson", "Berserk"]


def colours(allegiance):
    """slot -> (lit, shade) for one allegiance."""
    out = dict(BASE)
    out.update(ALLEGIANCE[allegiance])
    return out


def material_name(slot):
    return "KJ_" + slot
