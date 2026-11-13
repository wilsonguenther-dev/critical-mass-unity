"""Painted face decal, 256 x 256 RGBA, drawn procedurally (deterministic, no hand edits): large
brown eyes with one white highlight, wedge brows, a tiny nose hint, a small one-sided smile, cheek blush (bible p06).

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


def paint(cx, cz, hw, hh, head_scale=None):
    n = SIZE
    img = np.zeros((n, n, 4), dtype=np.float32)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32) + 0.5
    X = (xs / n - 0.5) * 2 * hw + cx           # metres, character space (x = character left)
    Z = (ys / n - 0.5) * 2 * hh + cz           # row 0 = bottom (Blender image order)
    if head_scale is not None:                 # features are drawn in design space, before the head scale
        kxy, kz, (px, _py, pz) = head_scale
        X, Z = px + (X - px) / kxy, pz + (Z - pz) / kz

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

    # v5 (bible p06 front): few, clean shapes. No lashes, no lid lines, no jowl strokes, no lip colour.
    dark = _hex("#3A2220")                         # eye rim and brow dark brown (front crop #3C2121)
    over(ell(0.0, 1.588, 0.205, 0.036, soft=0.030), _hex("#E48F74", 0.42))    # soft shadow under the fringe
    for s in (1, -1):                              # soft warm blush, low on the outer cheeks
        over(ell(0.152 * s, 1.388, 0.040, 0.020, soft=0.026), _hex("#FF9A86", 0.42))
    for s in (1, -1):
        ex, ez = 0.108 * s, 1.444                  # big eyes, set wide and low on the face
        # the top of the eye is cut a little flat, lower toward the nose: the bible's confident look (a shape, not a line)
        u = (X - ex) * s / 0.05
        top = np.clip((ez + 0.047 + 0.008 * u - Z) / 0.003 + 0.5, 0, 1)
        rim = ell(ex - 0.002 * s, ez, 0.046, 0.065) * top
        over(ell(ex + 0.007 * s, ez, 0.053, 0.066) * top, _hex("#FFFFFF"))     # white shows only as an outer sliver
        over(rim, dark)                                                        # dark rim of the eye
        ring = ell(ex - 0.002 * s, ez - 0.006, 0.037, 0.054) * rim
        over(ring, _hex("#8A5232"))                                            # warm brown iris ring
        over(ell(ex - 0.002 * s, ez - 0.030, 0.026, 0.018) * ring, _hex("#B0723F"))   # warmer lower iris
        over(ell(ex - 0.002 * s, ez - 0.002, 0.021, 0.031) * ring, _hex("#2A1814"))   # pupil
        over(ell(ex + 0.014 * s, ez + 0.020, 0.016, 0.018), _hex("#FFFFFF"))   # main highlight
        over(ell(ex - 0.013 * s, ez - 0.027, 0.0075, 0.0075), _hex("#FFFFFF")) # small second highlight
        # thick dark brow, close above the eye, inner end lower: the confident tilt
        over(stroke([(0.050 * s, 1.513), (0.108 * s, 1.534), (0.172 * s, 1.531)], [0.042, 0.036, 0.016],
                    soft=0.005), dark)
    over(ell(0.0, 1.402, 0.012, 0.008, soft=0.010), _hex("#E2906C", 0.55))     # tiny nose hint
    # wide, warm closed smile curving up at both ends; the character-left end lifts higher into a small cheek crease
    mouth = _hex("#8E4030")
    over(stroke([(-0.066, 1.364), (-0.035, 1.346), (0.0, 1.340), (0.035, 1.347), (0.068, 1.371)],
                [0.008, 0.012, 0.013, 0.012, 0.008], soft=0.004), mouth)
    over(stroke([(0.077, 1.374), (0.083, 1.391)], [0.006, 0.003], soft=0.004), _hex("#C46F55", 0.85))
    a = img[..., 3:]
    img[..., :3] = np.where(a > 1e-4, img[..., :3] / np.maximum(a, 1e-4), 0.0)   # straight alpha (PNG), not premultiplied
    img[:3, :3, 3] = 0.0
    return img


def make_image(bpy, name, path, cx, cz, hw, hh, head_scale=None):
    px = paint(cx, cz, hw, hh, head_scale)
    im = bpy.data.images.get(name) or bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    im.colorspace_settings.name = "sRGB"
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im
