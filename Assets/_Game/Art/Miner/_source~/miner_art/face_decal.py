"""256 x 256 painted face decal for the Miner (art direction 3.x exception: one painted face decal per class).

Painted procedurally with numpy (anti-aliased signed-distance shapes), so the build stays deterministic and needs no
image editor. Transparent background: only eyes, lids, brows and cheek blush are painted; skin, nose, beard and grin
stay geometry. Layout in UV space (u = his right -> his left as seen from the front, v = bottom -> top) matches the
face plane built by build_miner.face_plane(): x -0.22..0.22 u, z 1.39..1.545 u on the front of the head.
"""
import numpy as np

N = 256
X0, X1, Z0, Z1 = -0.22, 0.22, 1.39, 1.545     # face plane extent in metres (head space, before HEAD_WIDEN)


def _hex(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


INK = _hex("#24161A")       # pupil / lid line (pupil swatch)
BROW = _hex("#3A2419")      # heavy brows (brow swatch)
IRIS = _hex("#5A3A28")      # warm dark-brown iris
WHITE = _hex("#FFFFFF")
SCLERA = _hex("#FBF4EA")    # warm white, so the eye is not a hole of pure white at 84 px
BLUSH = _hex("#E8735C")     # nose red, softened by alpha


def _grid():
    # metres, so shapes can be written in the same units as the model
    u = (np.arange(N) + 0.5) / N
    x = X0 + (X1 - X0) * u
    z = Z1 - (Z1 - Z0) * u          # image row 0 = top
    return np.meshgrid(x, z)


def _cov(sd, px):
    """Signed distance (metres, negative inside) to an anti-aliased coverage; px = metres per pixel."""
    return np.clip(0.5 - sd / px, 0.0, 1.0)


def _ellipse(X, Z, cx, cz, rx, rz, rot=0.0):
    c, s = np.cos(rot), np.sin(rot)
    dx, dz = X - cx, Z - cz
    a, b = (dx * c + dz * s) / rx, (-dx * s + dz * c) / rz
    r = np.sqrt(a * a + b * b)
    return (r - 1.0) * min(rx, rz)


def _halfplane(X, Z, p, q):
    """Positive on the right of the directed line p -> q (metres)."""
    dx, dz = q[0] - p[0], q[1] - p[1]
    L = np.hypot(dx, dz)
    return ((X - p[0]) * dz - (Z - p[1]) * dx) / L


def _capsule(X, Z, p, q, r0, r1):
    px_, pz = X - p[0], Z - p[1]
    dx, dz = q[0] - p[0], q[1] - p[1]
    t = np.clip((px_ * dx + pz * dz) / (dx * dx + dz * dz), 0, 1)
    d = np.hypot(px_ - t * dx, pz - t * dz)
    return d - (r0 + (r1 - r0) * t)


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

    for s in (1, -1):                      # s = +1 his left (screen right in the front view)
        ex, ez = 0.098 * s, 1.462
        # cheek blush, soft
        over(_cov(_ellipse(X, Z, 0.150 * s, 1.415, 0.045, 0.024), pxm * 6), BLUSH, 0.42)
        # eye: warm sclera ellipse clipped flat on top by a lid line that slopes down toward the nose (determined look)
        lid_p, lid_q = (ex - 0.06 * s, ez + 0.020), (ex + 0.06 * s, ez + 0.036)
        if s < 0:
            lid_p, lid_q = lid_q, lid_p
        # p -> q runs left to right, so "below the lid" is the right-hand side (halfplane > 0); sd is negative inside
        eye = np.maximum(_ellipse(X, Z, ex, ez, 0.047, 0.040), -_halfplane(X, Z, lid_p, lid_q))
        over(_cov(eye - 0.006, pxm), INK)                 # dark rim around the eye (reads as the 1.5 px inner line)
        over(_cov(eye, pxm), SCLERA)
        iris = _ellipse(X, Z, ex - 0.010 * s, ez - 0.004, 0.026, 0.031)
        over(_cov(np.maximum(iris, eye), pxm), IRIS)
        over(_cov(np.maximum(_ellipse(X, Z, ex - 0.010 * s, ez - 0.004, 0.015, 0.019), eye), pxm), INK)
        over(_cov(_ellipse(X, Z, ex - 0.018 * s, ez + 0.006, 0.0075, 0.0085), pxm), WHITE)   # catch light
        # heavy upper lid line along the clip
        over(_cov(_capsule(X, Z, lid_p, lid_q, 0.008, 0.008) , pxm), INK)
        # heavy brow: a thick tapered wedge, high at the temple, low and thick over the nose
        over(_cov(_capsule(X, Z, (0.175 * s, 1.523), (0.040 * s, 1.506), 0.014, 0.021), pxm), BROW)
    img = np.concatenate([rgb, a[..., None]], axis=2)
    return np.clip(img, 0, 1)


def save_png_blender(path):
    """Write the decal with Blender's image API (no PIL inside Blender)."""
    import bpy
    img = paint()
    im = bpy.data.images.new("MinerFace", N, N, alpha=True)
    im.colorspace_settings.name = "sRGB"
    px = img[::-1].reshape(-1)               # Blender rows are bottom-up
    im.pixels.foreach_set(px.astype(np.float32))
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im
