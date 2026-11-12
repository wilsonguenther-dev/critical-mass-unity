"""Painted face decal, 256 x 256 RGBA, drawn procedurally (deterministic, no hand edits): large
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

    ink = _hex(palette.CHAR["pupil"][0])
    lash = _hex("#2A1810")
    blush = _hex("#F2867A", 0.55)
    for s in (1, -1):
        over(ell(0.140 * s, 1.384, 0.056, 0.030, soft=0.03), blush)                # warm cheeks, on the cheek volumes
    for s in (1, -1):
        # v3 eyes (bible p06 front): big, tall, set wide, cut flat across the top by a heavy upper lid; the brown
        # iris sits toward the nose so the white shows as a wedge on the outer side only (no ring: v2 read as glasses)
        ex, ez = 0.098 * s, 1.464
        u = (X - ex) * s / 0.06                                                    # -1 inner .. +1 outer
        lid = ez + 0.044 + 0.010 * u                                               # inner corner lower: determined
        under = (Z < lid).astype(np.float32)
        over(ell(ex, ez - 0.002, 0.055, 0.066) * under, _hex("#FFFFFF"))
        ix = ex - 0.011 * s
        over(ell(ix, ez - 0.004, 0.043, 0.060) * under, _hex(palette.CHAR["iris"][0]))
        over(ell(ix, ez - 0.030, 0.032, 0.026) * under * ell(ix, ez - 0.004, 0.043, 0.060), _hex("#A0623A"))
        over(ell(ix, ez + 0.002, 0.025, 0.035) * under, ink)
        over(ell(ix - 0.015 * s, ez + 0.022, 0.016, 0.018), _hex("#FFFFFF"))      # main highlight
        over(ell(ix + 0.015 * s, ez - 0.030, 0.008, 0.008), _hex("#FFFFFF"))      # small lower highlight
        # heavy upper lid with an outer lash flick, plus a short lower lash at the outer corner
        over(stroke([(ex - 0.058 * s, ez + 0.033), (ex - 0.02 * s, ez + 0.040),
                     (ex + 0.03 * s, ez + 0.050), (ex + 0.058 * s, ez + 0.054), (ex + 0.080 * s, ez + 0.068)],
                    [0.018, 0.028, 0.030, 0.022, 0.008]), lash)
        over(stroke([(ex + 0.036 * s, ez - 0.058), (ex + 0.054 * s, ez - 0.044)], [0.008, 0.004]), lash)
    # nose: the nose is modelled (v3); only a soft warm shadow under it
    over(ell(0.004, 1.366, 0.026, 0.009, soft=0.012), _hex("#C9775A", 0.55))
    # confident smirk (bible p06): a wide closed smile, the character's left corner kicks up into a dimple
    mouth = _hex(palette.CHAR["mouth"][0])
    over(stroke([(-0.070, 1.343), (-0.034, 1.331), (0.015, 1.330), (0.057, 1.342), (0.086, 1.366)],
                [0.012, 0.020, 0.022, 0.018, 0.012]), mouth)
    over(stroke([(0.084, 1.354), (0.098, 1.378)], [0.010, 0.006]), _hex(palette.CHAR["mouth"][0], 0.9))
    over(stroke([(-0.070, 1.339), (-0.082, 1.349)], [0.008, 0.005]), _hex(palette.CHAR["mouth"][0], 0.7))
    over(ell(0.000, 1.313, 0.034, 0.008, soft=0.010), _hex("#C9775A", 0.45))      # lower-lip shadow
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
