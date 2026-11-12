"""Painted face decal, 256 x 256 RGBA, drawn procedurally (deterministic, no hand edits): thick dark brows, large
brown eyes with white highlights, a confident closed-mouth smirk, cheek blush (bible p06 face callout).

Texture space = the head's front planar projection (kit.Builder.add face_uv): u = (x - cx) / (2 hw) + 0.5,
v = (z - cz) / (2 hh) + 0.5, metres -> texels. Everything outside the features is fully transparent, so the skin
vertex colour shows through; texel (0, 0) region stays transparent for the non-face UV.
"""
import numpy as np

from . import palette

SIZE = 256


def _hex(h, a=1.0):
    r, g, b = palette.hex_rgb(h)
    return np.array([r, g, b, a], dtype=np.float32)


def paint(cx, cz, hw, hh):
    n = SIZE
    img = np.zeros((n, n, 4), dtype=np.float32)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32) + 0.5
    X = (xs / n - 0.5) * 2 * hw + cx           # metres, character space (x = character left)
    Z = (ys / n - 0.5) * 2 * hh + cz           # row 0 = bottom (Blender image order)

    def over(mask, rgba):
        a = np.clip(mask, 0, 1)[..., None] * rgba[3]
        img[..., :3] = img[..., :3] * (1 - a) + rgba[:3] * a
        img[..., 3:] = img[..., 3:] * (1 - a) + a

    def ell(x0, z0, rx, rz, soft=0.004, rot=0.0):
        c, s = np.cos(rot), np.sin(rot)
        dx, dz = X - x0, Z - z0
        u, v = (dx * c + dz * s) / rx, (-dx * s + dz * c) / rz
        d = (np.sqrt(u * u + v * v) - 1.0) * min(rx, rz)
        return np.clip(0.5 - d / soft, 0, 1)

    def stroke(pts, w, soft=0.004):
        m = np.zeros_like(X)
        for (x0, z0), (x1, z1), (w0, w1) in zip(pts[:-1], pts[1:], zip(w[:-1], w[1:])):
            ax, az = x1 - x0, z1 - z0
            t = np.clip(((X - x0) * ax + (Z - z0) * az) / (ax * ax + az * az), 0, 1)
            d = np.hypot(X - (x0 + t * ax), Z - (z0 + t * az)) - (w0 + (w1 - w0) * t) / 2
            m = np.maximum(m, np.clip(0.5 - d / soft, 0, 1))
        return m

    skin = palette.CHAR["skin"][0]
    blush = _hex("#F2867A", 0.55)
    for s in (1, -1):
        over(ell(0.150 * s, 1.405, 0.050, 0.030, soft=0.03), blush)               # cheeks
    for s in (1, -1):
        ex, ez = 0.088 * s, 1.488
        over(ell(ex, ez, 0.050, 0.062), _hex("#FFFFFF"))                          # eye white
        over(ell(ex - 0.004 * s, ez - 0.006, 0.040, 0.054), _hex(palette.CHAR["iris"][0]))   # large brown iris
        over(ell(ex - 0.004 * s, ez - 0.010, 0.022, 0.030), _hex(palette.CHAR["pupil"][0]))
        over(ell(ex - 0.018 * s, ez + 0.016, 0.014, 0.016), _hex("#FFFFFF"))      # highlights
        over(ell(ex + 0.010 * s, ez - 0.028, 0.007, 0.007), _hex("#FFFFFF"))
        over(stroke([(ex - 0.052 * s, ez + 0.018), (ex - 0.01 * s, ez + 0.060), (ex + 0.04 * s, ez + 0.050),
                     (ex + 0.060 * s, ez + 0.020)], [0.012, 0.020, 0.020, 0.010]),
             _hex(palette.CHAR["pupil"][0]))                                      # heavy upper lid only
        over(stroke([(0.030 * s, 1.575), (0.095 * s, 1.598), (0.160 * s, 1.585)], [0.030, 0.040, 0.026]),
             _hex(palette.CHAR["brow"][0]))                                       # thick confident brows
    # nose hint
    over(stroke([(-0.012, 1.432), (0.0, 1.425), (0.014, 1.433)], [0.008, 0.009, 0.008]), _hex("#D7865F", 0.8))
    # closed-mouth smirk, higher on the character's left (+x), dimple at the high end
    over(stroke([(-0.062, 1.372), (-0.02, 1.360), (0.030, 1.362), (0.070, 1.384)], [0.010, 0.013, 0.013, 0.010]),
         _hex(palette.CHAR["mouth"][0]))
    over(stroke([(0.074, 1.378), (0.080, 1.392)], [0.007, 0.005]), _hex(palette.CHAR["mouth"][0], 0.8))
    del skin
    img[:3, :3, 3] = 0.0
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
