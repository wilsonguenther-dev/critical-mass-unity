"""Farmer painted face decal, 256 x 256 RGBA, drawn procedurally (deterministic, no hand edits), bible p07 face:
v3: big round brown eyes with a heavy upper lid and lash flicks, shaded iris (dark top, warm bottom), two highlights, a soft lower
lid, the character's LEFT brow cocked high, the right one low and straight (attitude), an open lopsided smirk showing the top
teeth, kicking up at the character's left with a dimple, rosy cheeks and a small nose hook.

Texture space = the head's front planar projection (FarmerBuilder.add face_uv): u = (x - cx) / (2 hw) + 0.5,
v = (z - cz) / (2 hh) + 0.5, metres -> texels. Outside the features everything is transparent, so the skin vertex
colour shows through; the (0, 0) corner stays transparent for every non-face UV.
"""
import numpy as np

from . import fpalette as FP

SIZE = 256


def _hex(h, a=1.0):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)] + [a], dtype=np.float32)


def paint(cx, cz, hw, hh):
    n = SIZE
    img = np.zeros((n, n, 4), dtype=np.float32)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32) + 0.5
    X = (xs / n - 0.5) * 2 * hw + cx           # metres, character space (x = character left = screen right in front)
    Z = (ys / n - 0.5) * 2 * hh + cz           # row 0 = bottom (Blender image order)

    def over(mask, rgba):
        a = np.clip(mask, 0, 1)[..., None] * rgba[3]
        img[..., :3] = img[..., :3] * (1 - a) + rgba[:3] * a
        img[..., 3:] = img[..., 3:] * (1 - a) + a

    def ell(x0, z0, rx, rz, soft=0.003, rot=0.0):
        c, s = np.cos(rot), np.sin(rot)
        dx, dz = X - x0, Z - z0
        u, v = (dx * c + dz * s) / rx, (-dx * s + dz * c) / rz
        d = (np.sqrt(u * u + v * v) - 1.0) * min(rx, rz)
        return np.clip(0.5 - d / soft, 0, 1)

    def stroke(pts, w, soft=0.003):
        m = np.zeros_like(X)
        for (x0, z0), (x1, z1), (w0, w1) in zip(pts[:-1], pts[1:], zip(w[:-1], w[1:])):
            ax, az = x1 - x0, z1 - z0
            t = np.clip(((X - x0) * ax + (Z - z0) * az) / (ax * ax + az * az), 0, 1)
            d = np.hypot(X - (x0 + t * ax), Z - (z0 + t * az)) - (w0 + (w1 - w0) * t) / 2
            m = np.maximum(m, np.clip(0.5 - d / soft, 0, 1))
        return m

    ink = _hex("#24161A")
    lash = _hex("#2A1A17")
    brow = _hex(FP.FARMER["f_brow"][0])
    iris_dk, iris, iris_lt = _hex("#4A2A18"), _hex(FP.FARMER["f_iris"][0]), _hex("#C08850")
    for s in (1, -1):                                                               # big visible blush + hatch
        over(ell(0.140 * s, 1.425, 0.056, 0.034, soft=0.018), _hex("#F27A72", 0.75))
        for k in range(3):
            x0 = 0.115 * s + 0.020 * (k - 1) * s
            over(stroke([(x0 - 0.006 * s, 1.416), (x0 + 0.006 * s, 1.434)], [0.004, 0.004]), _hex("#E0605A", 0.7))
    for s in (1, -1):
        ex, ez = 0.094 * s, 1.502
        clip = ell(ex, ez - 0.004, 0.062, 0.070)                                    # big round eye opening
        over(clip, _hex("#FFFFFF"))
        ix = ex - 0.006 * s
        over(ell(ix, ez - 0.006, 0.048, 0.066) * clip, iris_dk)
        over(ell(ix, ez - 0.014, 0.043, 0.056) * clip, iris)                       # iris shading: dark top rim
        over(ell(ix, ez - 0.034, 0.032, 0.024) * clip, iris_lt)                    # warm lower glow
        over(ell(ix, ez - 0.006, 0.023, 0.031) * clip, ink)                        # pupil
        over(ell(ix - 0.017 * s, ez + 0.022, 0.018, 0.020), _hex("#FFFFFF"))       # highlight 1 (big)
        over(ell(ix + 0.015 * s, ez - 0.034, 0.009, 0.009), _hex("#FFFFFF"))       # highlight 2 (small)
        lid_top = ez + 0.050                                                        # thick lash line, two flicks
        over(stroke([(ex - 0.064 * s, ez + 0.012), (ex - 0.034 * s, lid_top + 0.010), (ex + 0.012 * s, lid_top + 0.016),
                     (ex + 0.054 * s, lid_top + 0.002), (ex + 0.074 * s, lid_top - 0.008)],
                    [0.010, 0.017, 0.019, 0.016, 0.008]), lash)
        over(stroke([(ex + 0.056 * s, lid_top + 0.000), (ex + 0.090 * s, lid_top + 0.022)], [0.011, 0.003]), lash)
        over(stroke([(ex + 0.040 * s, lid_top + 0.011), (ex + 0.066 * s, lid_top + 0.036)], [0.009, 0.003]), lash)
        over(stroke([(ex - 0.044 * s, ez - 0.066), (ex + 0.000 * s, ez - 0.080), (ex + 0.048 * s, ez - 0.064)],
                    [0.003, 0.005, 0.003]), _hex("#B9694E", 0.75))                 # soft lower lid
        if s > 0:     # the character's left brow cocked HIGH and arched (exaggerated)
            pts, w = [(0.034, 1.596), (0.080, 1.648), (0.132, 1.656), (0.174, 1.628)], [0.032, 0.040, 0.032, 0.012]
        else:         # right brow low, flat, angled down to the nose: a sly look
            pts, w = [(-0.028, 1.574), (-0.080, 1.588), (-0.132, 1.596), (-0.172, 1.582)], [0.034, 0.040, 0.030, 0.012]
        over(stroke(pts, w), brow)
    # nose: small warm shadow hook + a light tip
    over(stroke([(0.004, 1.452), (0.016, 1.436), (-0.002, 1.428)], [0.007, 0.010, 0.006]), _hex("#D7865F", 0.9))
    over(ell(-0.008, 1.446, 0.008, 0.006), _hex("#FFD8BE", 0.6))
    # open, lopsided, toothy smirk: kicks up hard at the character's left, a strip of top teeth, tongue, dimple
    xs_u = [-0.066, -0.030, 0.010, 0.050, 0.084]
    up_z = [1.400, 1.392, 1.392, 1.400, 1.424]
    lo_z = [1.400, 1.374, 1.364, 1.374, 1.424]
    U = np.interp(X, xs_u, up_z)
    Lz = np.interp(X, xs_u, lo_z)
    inside = (X > xs_u[0]) & (X < xs_u[-1])
    mouth_m = inside * np.clip((U - Z) / 0.002, 0, 1) * np.clip((Z - Lz) / 0.002, 0, 1)
    over(mouth_m, _hex("#5A1E22"))
    over(mouth_m * ell(0.018, 1.366, 0.034, 0.014, soft=0.004), _hex("#D9606A"))  # tongue
    teeth = mouth_m * np.clip((Z - (U - 0.013)) / 0.002, 0, 1)
    over(teeth, _hex("#FFFFFF"))
    mouthc = _hex(FP.FARMER["f_mouth"][0])
    over(stroke(list(zip(xs_u, up_z)), [0.006, 0.009, 0.009, 0.008, 0.006]), mouthc)
    over(stroke(list(zip(xs_u, lo_z)), [0.004, 0.006, 0.007, 0.006, 0.004]), _hex(FP.FARMER["f_mouth"][0], 0.9))
    over(stroke([(0.086, 1.414), (0.094, 1.432)], [0.006, 0.004]), _hex(FP.FARMER["f_mouth"][0], 0.85))  # dimple
    over(stroke([(-0.020, 1.352), (0.016, 1.348), (0.040, 1.354)], [0.003, 0.005, 0.003]), _hex("#D9806A", 0.6))
    img[:4, :4, 3] = 0.0
    return img


def make_image(bpy, name, path, cx, cz, hw, hh):
    px = paint(cx, cz, hw, hh)
    im = bpy.data.images.get(name) or bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    im.colorspace_settings.name = "sRGB"
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im
