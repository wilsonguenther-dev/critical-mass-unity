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

    # v4 (bible p06 front): few, clean, simple shapes. No lashes, no lid lines, no cheek or jowl strokes, no lip colour.
    dark = _hex("#3A2220")                         # eye and brow dark brown (front crop #3C2121)
    iris = _hex(palette.CHAR["iris"][0])
    for s in (1, -1):                              # soft warm blush, low on the outer cheeks
        over(ell(0.152 * s, 1.388, 0.040, 0.020, soft=0.026), _hex("#FF9A86", 0.42))
    for s in (1, -1):
        ex, ez = 0.106 * s, 1.446                  # big eyes, set wide and low on the face
        over(ell(ex + 0.007 * s, ez, 0.050, 0.061), _hex("#FFFFFF"))           # white shows only as an outer sliver
        over(ell(ex - 0.002 * s, ez, 0.043, 0.060), dark)                     # big simple dark-brown eye
        over(ell(ex - 0.002 * s, ez - 0.020, 0.029, 0.030) * ell(ex - 0.002 * s, ez, 0.043, 0.060), iris)
        over(ell(ex + 0.012 * s, ez + 0.022, 0.015, 0.017), _hex("#FFFFFF"))  # one white highlight
        # thick dark brow: a simple angled wedge well above the eye, the inner end lower (confident)
        over(stroke([(0.048 * s, 1.530), (0.105 * s, 1.552), (0.168 * s, 1.552)], [0.036, 0.031, 0.014],
                    soft=0.005), dark)
    over(ell(0.0, 1.402, 0.012, 0.008, soft=0.010), _hex("#E2906C", 0.55))     # tiny nose hint
    # small closed one-sided smile: a short curve whose character-left end lifts
    over(stroke([(-0.048, 1.356), (-0.015, 1.343), (0.025, 1.346), (0.056, 1.366)], [0.010, 0.012, 0.011, 0.007],
                soft=0.004), _hex(palette.CHAR["mouth"][0]))
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
