"""256 x 256 painted face decal for the Healer (same approach as miner_art.face_decal: one painted decal per class,
anti-aliased signed-distance shapes in numpy, deterministic, transparent background).

Painted: two big round eyes (green iris, black pupil, two catch lights), a soft upper lash line with two lash flicks
at the outer corner, and warm cheek blush. Brows, nose and the smile are modelled geometry (build_healer.build_head).
UV layout (u = her right -> her left as seen from the front, v = bottom -> top) matches build_healer.face_plane():
x X0..X1, z Z0..Z1 on the front of the head, in head space.
"""
import numpy as np

N = 256
X0, X1, Z0, Z1 = -0.25, 0.25, 1.385, 1.585
EYE_X, EYE_Z, EYE_RX, EYE_RZ = 0.128, 1.482, 0.066, 0.078


def _hex(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


INK = _hex("#24161A")
IRIS = _hex("#3F9A63")      # healer green (well clear of biomass lime #9BE23A and of the team azure)
IRIS_DK = _hex("#1F5A3A")
IRIS_LT = _hex("#7CCB8F")
WHITE = _hex("#FFFFFF")
SCLERA = _hex("#FBF4EA")
BLUSH = _hex("#D9655A")
LID = _hex("#93583A")


def _grid():
    u = (np.arange(N) + 0.5) / N
    x = X0 + (X1 - X0) * u
    z = Z1 - (Z1 - Z0) * u
    return np.meshgrid(x, z)


def _cov(sd, px):
    return np.clip(0.5 - sd / px, 0.0, 1.0)


def _ellipse(X, Z, cx, cz, rx, rz, rot=0.0):
    c, s = np.cos(rot), np.sin(rot)
    dx, dz = X - cx, Z - cz
    a, b = (dx * c + dz * s) / rx, (-dx * s + dz * c) / rz
    return (np.sqrt(a * a + b * b) - 1.0) * min(rx, rz)


def _halfplane(X, Z, p, q):
    dx, dz = q[0] - p[0], q[1] - p[1]
    return ((X - p[0]) * dz - (Z - p[1]) * dx) / np.hypot(dx, dz)


def paint():
    X, Z = _grid()
    pxm = (X1 - X0) / N
    rgb = np.zeros((N, N, 3), np.float32)
    a = np.zeros((N, N), np.float32)

    def over(cov, col, alpha=1.0):
        nonlocal rgb, a
        k = cov * alpha
        rgb = rgb * (1 - k[..., None]) + col * k[..., None]
        a = a + k * (1 - a)

    for s in (1, -1):
        ex, ez = EYE_X * s, EYE_Z
        over(_cov(_ellipse(X, Z, 0.172 * s, 1.418, 0.050, 0.024), pxm * 8), BLUSH, 0.60)
        eye = _ellipse(X, Z, ex, ez, EYE_RX, EYE_RZ)
        lid_p, lid_q = (ex - 0.09 * s, ez + 0.060), (ex + 0.09 * s, ez + 0.066)
        if s < 0:
            lid_p, lid_q = lid_q, lid_p
        open_eye = np.maximum(eye, -_halfplane(X, Z, lid_p, lid_q))
        over(_cov(eye - 0.006, pxm), INK)
        over(_cov(eye, pxm), LID)
        over(_cov(open_eye, pxm), SCLERA)
        ix, iz = ex - 0.010 * s, ez - 0.008
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz, 0.048, 0.057), open_eye), pxm), IRIS_DK)
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz - 0.006, 0.040, 0.046), open_eye), pxm), IRIS)
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz - 0.022, 0.030, 0.020), open_eye), pxm), IRIS_LT, 0.8)
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz, 0.021, 0.025), open_eye), pxm), INK)
        over(_cov(_ellipse(X, Z, ix + 0.015 * s if s > 0 else ix + 0.015, iz + 0.022, 0.015, 0.016), pxm), WHITE)
        over(_cov(_ellipse(X, Z, ix - 0.015, iz - 0.020, 0.006, 0.006), pxm), WHITE)
        # upper lash line: thicker than the Miner's lid line, and two lash flicks at the outer corner
        lash = np.maximum(np.abs(_halfplane(X, Z, lid_p, lid_q)) - 0.009, eye - 0.004)
        over(_cov(lash, pxm), INK)
        for k, (dx, dz, ang) in enumerate(((0.070, 0.050, 0.55), (0.080, 0.028, 0.15))):
            cx, cz = ex + dx * s, ez + dz
            over(_cov(_ellipse(X, Z, cx, cz, 0.024, 0.008, ang * s), pxm), INK)
    img = np.concatenate([rgb, a[..., None]], axis=2)
    return np.clip(img, 0, 1)


def save_png_blender(path):
    import bpy
    img = paint()
    im = bpy.data.images.new("HealerFace", N, N, alpha=True)
    im.colorspace_settings.name = "sRGB"
    im.pixels.foreach_set(img[::-1].reshape(-1).astype(np.float32))
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im
