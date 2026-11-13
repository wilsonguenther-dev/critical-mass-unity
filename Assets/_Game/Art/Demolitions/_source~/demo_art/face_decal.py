"""256 x 256 painted face decal for the Demolitions Expert (female class; art direction 3.x: one painted face per class).

Same method as the Miner (miner_art/face_decal.py): numpy anti-aliased signed-distance shapes, deterministic, written
with Blender's image API, transparent background. Owner rule 2026-11-12: faces smooth, round and clean, so EVERY face
feature except the button nose is painted here, on the smooth round head, not built from blocky prisms:
  - a soft soot band across the eyes with two CLEAN goggle rings (where the goggles sat before she pushed them up),
  - big green eyes (her left wide open, her right narrowed in a cocky squint) with the bible Farmer's winged lash line,
  - thick arched brows, her left cocked high, with singed ash-grey frizz at the tails,
  - a wide open grin with a gap between the two front teeth, tongue, rosy lips, dimples.
Layout: u = her right -> her left seen from the front, v = bottom -> top; covers X0..X1, Z0..Z1 (head space, metres).
"""
import numpy as np

N = 256
X0, X1, Z0, Z1 = -0.235, 0.235, 1.255, 1.605


def _hex(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


INK = _hex("#24161A")
IRIS = _hex("#3F7A3A")
IRIS_DK = _hex("#24461F")
WHITE = _hex("#FFFFFF")
SCLERA = _hex("#FBF4EA")
LID = _hex("#C98A66")
SOOT = _hex("#7A645B")
CLEAN = _hex("#F6C29A")
BROW = _hex("#3E271B")
SINGE = _hex("#8B7B70")
MOUTH = _hex("#6E2A22")
TONGUE = _hex("#C9605A")
TEETH = _hex("#FBF4EA")
LIPS = _hex("#B65D57")
BLUSH = _hex("#E58A6E")


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


def _capsule(X, Z, p, q, r0, r1=None):
    r1 = r0 if r1 is None else r1
    px, pz = X - p[0], Z - p[1]
    dx, dz = q[0] - p[0], q[1] - p[1]
    t = np.clip((px * dx + pz * dz) / (dx * dx + dz * dz), 0, 1)
    return np.hypot(px - t * dx, pz - t * dz) - (r0 + (r1 - r0) * t)


def _halfplane(X, Z, p, q):
    dx, dz = q[0] - p[0], q[1] - p[1]
    return ((X - p[0]) * dz - (Z - p[1]) * dx) / np.hypot(dx, dz)


EYE_X, EYE_Z = 0.112, 1.478


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

    # two soft soot smudges (her right cheek, the bridge of the nose): a hint of the job, the face stays clean
    over(_cov(_ellipse(X, Z, -0.165, 1.392, 0.045, 0.024, 0.35), pxm * 6), SOOT, 0.45)
    over(_cov(_ellipse(X, Z, 0.01, 1.468, 0.03, 0.016, -0.2), pxm * 5), SOOT, 0.35)
    for s in (1, -1):
        ex, ez = EYE_X * s, EYE_Z
        over(_cov(_ellipse(X, Z, 0.165 * s, 1.392, 0.05, 0.026), pxm * 6), BLUSH, 0.45)  # cheek blush
        eye = _ellipse(X, Z, ex, ez, 0.066, 0.075)
        if s > 0:
            lid_p, lid_q = (ex - 0.08, ez + 0.066), (ex + 0.08, ez + 0.058)
        else:
            lid_p, lid_q = (ex - 0.08, ez + 0.036), (ex + 0.08, ez + 0.048)   # squint: lid lower
        open_eye = np.maximum(eye, -_halfplane(X, Z, lid_p, lid_q))
        over(_cov(eye - 0.006, pxm), INK)
        over(_cov(eye, pxm), LID)
        over(_cov(open_eye, pxm), SCLERA)
        ix, iz = ex - 0.010 * s, ez - 0.008
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz, 0.040, 0.046), open_eye), pxm), IRIS_DK)
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz - 0.004, 0.033, 0.038), open_eye), pxm), IRIS)
        over(_cov(np.maximum(_ellipse(X, Z, ix, iz, 0.019, 0.022), open_eye), pxm), INK)
        over(_cov(np.maximum(_ellipse(X, Z, ix + 0.013, iz + 0.017, 0.012, 0.013), open_eye), pxm), WHITE)
        over(_cov(np.maximum(_ellipse(X, Z, ix - 0.013, iz - 0.015, 0.005, 0.005), open_eye), pxm), WHITE)
        lidline = np.abs(_halfplane(X, Z, lid_p, lid_q)) - 0.0085
        over(_cov(np.maximum(lidline, eye), pxm), INK)
        # winged lash line (bible Farmer): tapered flick out and up from the outer upper corner, two lashes
        oz = ez + (0.058 if s > 0 else 0.044)
        over(_cov(_capsule(X, Z, (ex + 0.050 * s, oz - 0.004), (ex + 0.090 * s, oz + 0.028), 0.0075, 0.002), pxm), INK)
        over(_cov(_capsule(X, Z, (ex + 0.030 * s, oz + 0.006), (ex + 0.054 * s, oz + 0.034), 0.0045, 0.0015), pxm), INK)
        over(_cov(_capsule(X, Z, (ex + 0.010 * s, oz + 0.010), (ex + 0.024 * s, oz + 0.036), 0.004, 0.0015), pxm), INK)
        # thick arched brow; her left (s=+1) cocked high, her right flatter and lower over the squint
        lift = 0.020 if s > 0 else -0.004
        p0, p1, p2 = (0.035 * s, 1.562 + lift), (0.112 * s, 1.588 + lift * 1.3), (0.19 * s, 1.572 + lift * 0.6)
        brow = np.minimum(_capsule(X, Z, p0, p1, 0.013, 0.012), _capsule(X, Z, p1, p2, 0.012, 0.005))
        over(_cov(brow, pxm), BROW)
        for k, (dx, dz) in enumerate(((0.012, 0.018), (0.022, 0.010), (0.016, -0.004))):   # singed frizz at the tail
            q0 = (p2[0] - 0.012 * s, p2[1])
            over(_cov(_capsule(X, Z, q0, (p2[0] + dx * s, p2[1] + dz), 0.0045, 0.0015), pxm), SINGE)
    # wide open grin with a gap between the front teeth
    zt = lambda x: 1.336 + 1.6 * x * x            # upper edge of the mouth, corners turned up
    mouth = np.maximum(Z - zt(X), _ellipse(X, Z, 0.0, 1.336, 0.098, 0.058))
    over(_cov(np.abs(mouth) - 0.0065, pxm), LIPS)                                         # lip line all round
    over(_cov(_ellipse(X, Z, 0.0, 1.274, 0.06, 0.016), pxm * 1.5), LIPS)                  # fuller lower lip
    over(_cov(mouth, pxm), MOUTH)
    over(_cov(np.maximum(mouth, _ellipse(X, Z, 0.012, 1.285, 0.05, 0.026)), pxm), TONGUE)
    teeth = np.maximum(mouth, (zt(X) - 0.024) - Z)
    teeth = np.maximum(teeth, -(np.abs(X) - 0.0065))                                      # the gap
    teeth = np.maximum(teeth, np.abs(X) - 0.075)
    over(_cov(teeth, pxm), TEETH)
    for s in (1, -1):                                                                     # dimples
        over(_cov(_capsule(X, Z, (0.097 * s, 1.346), (0.108 * s, 1.364), 0.004, 0.0015), pxm), INK, 0.8)
    img = np.concatenate([rgb, a[..., None]], axis=2)
    return np.clip(img, 0, 1)


def save_png_blender(path, name="DemolitionsFace"):
    import bpy
    img = paint()
    im = bpy.data.images.new(name, N, N, alpha=True)
    im.colorspace_settings.name = "sRGB"
    im.pixels.foreach_set(img[::-1].reshape(-1).astype(np.float32))
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im
