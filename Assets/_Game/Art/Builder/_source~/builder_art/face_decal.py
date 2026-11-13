"""256 x 256 painted face decal for the Builder (copied from the Miner's decal painter, re-laid for the Builder head) (art direction 3.x exception: one painted face decal per class).

Painted procedurally with numpy (anti-aliased signed-distance shapes), so the build stays deterministic and needs no
image editor. Transparent background: only eyes, lids, brows and cheek blush are painted; skin, nose, beard and grin
stay geometry. Layout in UV space (u = his right -> his left as seen from the front, v = bottom -> top) matches the
face plane built by build_miner.face_plane(): x -0.22..0.22 u, z 1.39..1.545 u on the front of the head.
"""
import numpy as np

N = 256
X0, X1, Z0, Z1 = -0.25, 0.25, 1.445, 1.64     # Builder face plane extent in metres (head space)


def _hex(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


INK = _hex("#24161A")       # pupil / lid line
IRIS = _hex("#5C7A3C")      # Builder: hazel-green iris (Miner brown, Warrior brown)
IRIS_DK = _hex("#33461F")   # iris rim
WHITE = _hex("#FFFFFF")
SCLERA = _hex("#FBF4EA")    # warm white
BLUSH = _hex("#E0705E")     # cheek shading, softened by alpha
LID = _hex("#C98A66")       # upper-lid skin shade


def _grid():
    u = (np.arange(N) + 0.5) / N
    x = X0 + (X1 - X0) * u
    z = Z1 - (Z1 - Z0) * u          # image row 0 = top
    return np.meshgrid(x, z)


def _cov(sd, px):
    return np.clip(0.5 - sd / px, 0.0, 1.0)


def _ellipse(X, Z, cx, cz, rx, rz, rot=0.0):
    c, s = np.cos(rot), np.sin(rot)
    dx, dz = X - cx, Z - cz
    a, b = (dx * c + dz * s) / rx, (-dx * s + dz * c) / rz
    r = np.sqrt(a * a + b * b)
    return (r - 1.0) * min(rx, rz)


def _halfplane(X, Z, p, q):
    dx, dz = q[0] - p[0], q[1] - p[1]
    L = np.hypot(dx, dz)
    return ((X - p[0]) * dz - (Z - p[1]) * dx) / L


def paint():
    """v3: big friendly bible eyes. Each eye 0.13 x 0.13 u (head space), round, a large brown iris looking slightly
    toward the viewer's centre, a black pupil, two catch lights (big upper, small lower), a soft lid shade on top
    and a dark rim; warm blush on the cheeks below. Brows, nose, moustache and smile are modelled geometry."""
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
        ex, ez = 0.150 * s, 1.540
        over(_cov(_ellipse(X, Z, 0.190 * s, 1.478, 0.052, 0.026), pxm * 7), BLUSH, 0.55)
        eye = _ellipse(X, Z, ex, ez, 0.072, 0.074)
        # a gentle upper lid: the top 12 % of the eye is lid skin, sloping down to the outside (friendly)
        lid_p, lid_q = (ex - 0.09 * s, ez + 0.058), (ex + 0.09 * s, ez + 0.036)
        if s < 0:
            lid_p, lid_q = lid_q, lid_p
        open_eye = np.maximum(eye, -_halfplane(X, Z, lid_p, lid_q))
        cheek = _ellipse(X, Z, ex, ez - 0.118, 0.11, 0.07)          # cheek arc pushes the lower lid up (smile)
        open_eye = np.maximum(open_eye, -cheek)
        over(_cov(eye - 0.007, pxm), INK)
        over(_cov(eye, pxm), LID)
        over(_cov(open_eye, pxm), SCLERA)
        ix, iz = ex - 0.012 * s, ez - 0.006
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz, 0.049, 0.056), open_eye), pxm), IRIS_DK)
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz - 0.004, 0.041, 0.046), open_eye), pxm), IRIS)
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz, 0.023, 0.027), open_eye), pxm), INK)
        over(_cov(_ellipse(X, Z, ix + 0.016, iz + 0.022, 0.015, 0.016), pxm), WHITE)      # big catch light
        over(_cov(_ellipse(X, Z, ix - 0.016, iz - 0.020, 0.006, 0.006), pxm), WHITE)      # small catch light
        lidline = np.abs(_halfplane(X, Z, lid_p, lid_q)) - 0.006
        over(_cov(np.maximum(lidline, eye), pxm), INK)
    img = np.concatenate([rgb, a[..., None]], axis=2)
    return np.clip(img, 0, 1)


def save_png_blender(path):
    """Write the decal with Blender's image API (no PIL inside Blender)."""
    import bpy
    img = paint()
    im = bpy.data.images.new("BuilderFace", N, N, alpha=True)
    im.colorspace_settings.name = "sRGB"
    px = img[::-1].reshape(-1)               # Blender rows are bottom-up
    im.pixels.foreach_set(px.astype(np.float32))
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im
