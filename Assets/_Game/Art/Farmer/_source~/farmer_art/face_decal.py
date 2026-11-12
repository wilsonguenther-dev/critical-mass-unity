"""Farmer painted face decal, 256 x 256 RGBA, drawn procedurally (deterministic, no hand edits), bible p07 face:
big brown eyes with a heavy upper lid and lash flicks, shaded iris (dark top, warm bottom), two highlights, a soft lower
lid, the character's LEFT brow cocked high, the right one low and straight (attitude), a lopsided closed smirk kicking
up at the character's left with a dimple, rosy cheeks and a small nose hook.

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
    iris_dk, iris, iris_lt = _hex("#4A2A18"), _hex(FP.FARMER["f_iris"][0]), _hex("#B07A45")
    for s in (1, -1):                                                               # rosy cheeks, soft
        over(ell(0.135 * s, 1.425, 0.050, 0.030, soft=0.03), _hex("#F2867A", 0.55))
        for k in range(3):                                                          # three faint freckles
            over(ell(0.120 * s + 0.018 * (k - 1) * s, 1.448 - 0.006 * (k % 2), 0.0045, 0.0045, soft=0.003),
                 _hex("#C9785A", 0.55))
    for s in (1, -1):
        ex, ez = 0.090 * s, 1.500
        clip = ell(ex, ez - 0.004, 0.052, 0.064)                                    # eye opening (sclera)
        over(clip, _hex("#FFFFFF"))
        ix = ex - 0.006 * s                                                         # looking slightly to her right
        over(ell(ix, ez - 0.004, 0.040, 0.054) * clip, iris_dk)
        over(ell(ix, ez - 0.012, 0.036, 0.044) * clip, iris)                       # iris shading: dark top rim
        over(ell(ix, ez - 0.028, 0.026, 0.020) * clip, iris_lt)                    # warm lower glow
        over(ell(ix, ez - 0.004, 0.019, 0.026) * clip, ink)                        # pupil
        over(ell(ix - 0.014 * s, ez + 0.020, 0.014, 0.016), _hex("#FFFFFF"))       # main highlight
        over(ell(ix + 0.013 * s, ez - 0.030, 0.0065, 0.0065), _hex("#FFFFFF", 0.95))
        # heavy upper lid (half-lidded confidence) with a thick lash line and two outer lash flicks
        lid_top = ez + 0.040
        over(stroke([(ex - 0.056 * s, ez + 0.008), (ex - 0.030 * s, lid_top + 0.010), (ex + 0.010 * s, lid_top + 0.016),
                     (ex + 0.048 * s, lid_top + 0.002), (ex + 0.066 * s, lid_top - 0.006)],
                    [0.010, 0.016, 0.018, 0.015, 0.008]), lash)
        over(stroke([(ex + 0.050 * s, lid_top + 0.000), (ex + 0.082 * s, lid_top + 0.020)], [0.010, 0.003]), lash)
        over(stroke([(ex + 0.036 * s, lid_top + 0.010), (ex + 0.060 * s, lid_top + 0.034)], [0.008, 0.003]), lash)
        # lid crease (skin shadow) above the lash line
        over(stroke([(ex - 0.040 * s, lid_top + 0.024), (ex + 0.004 * s, lid_top + 0.034), (ex + 0.046 * s, lid_top + 0.022)],
                    [0.004, 0.005, 0.003]), _hex("#C97B5C", 0.7))
        # soft lower lid
        over(stroke([(ex - 0.040 * s, ez - 0.058), (ex + 0.000 * s, ez - 0.070), (ex + 0.044 * s, ez - 0.056)],
                    [0.003, 0.005, 0.003]), _hex("#B9694E", 0.75))
        # brows: the character's left brow cocked high and arched, the right one low, straight and a touch angry
        if s > 0:
            pts = [(0.034, 1.590), (0.080, 1.628), (0.130, 1.632), (0.162, 1.612)]
            w = [0.020, 0.026, 0.020, 0.008]
        else:
            pts = [(-0.030, 1.574), (-0.078, 1.590), (-0.128, 1.592), (-0.160, 1.578)]
            w = [0.022, 0.026, 0.020, 0.008]
        over(stroke(pts, w), brow)
    # nose: small warm shadow hook + a light tip
    over(stroke([(0.004, 1.452), (0.016, 1.436), (-0.002, 1.428)], [0.007, 0.010, 0.006]), _hex("#D7865F", 0.9))
    over(ell(-0.008, 1.446, 0.008, 0.006), _hex("#FFD8BE", 0.6))
    # lopsided smirk: wide curve kicking up hard at the character's left, a dimple, a hint of the lower lip
    mouth = _hex(FP.FARMER["f_mouth"][0])
    over(stroke([(-0.070, 1.392), (-0.034, 1.380), (0.010, 1.378), (0.052, 1.388), (0.080, 1.410)],
                [0.007, 0.012, 0.013, 0.011, 0.007]), mouth)
    over(stroke([(0.082, 1.398), (0.090, 1.416)], [0.006, 0.004]), _hex(FP.FARMER["f_mouth"][0], 0.8))   # dimple
    over(stroke([(-0.066, 1.388), (-0.074, 1.396)], [0.005, 0.003]), _hex(FP.FARMER["f_mouth"][0], 0.6))
    over(stroke([(-0.020, 1.366), (0.014, 1.362), (0.036, 1.368)], [0.004, 0.006, 0.003]), _hex("#D9806A", 0.7))
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
