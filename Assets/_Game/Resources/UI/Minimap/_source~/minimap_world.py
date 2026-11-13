# hud-art-1: the minimap from the REAL arena. Input: minimap-world-ortho.png, written by
# CriticalMass.Editor.Flow.HudArtCaptureTool (an orthographic, straight-down render of the generated arena at the gameplay
# camera's yaw, framed by MinimapProjection at 8x the 1x size, cleared to magenta). Output: minimap@1x/2x/4x.png in the HUD's
# illustrated language, on the same projection as every HUD marker (so fighter dots, the egg and the crown land where the
# world is):
#   1. land = every non-magenta pixel (tiny floating sky islets under 0.4 % of the land are dropped: they are scenery);
#   2. toon simplification: edge-preserving mean-shift flattening + a 12-level per-channel posterise, then a mild saturation
#      lift, so trees, paths, plazas, walls, bridges and water read as flat shapes at 205 px;
#   3. a cliff drop under every island (the land silhouette pushed down, cliff stone colour) and a Void Navy outline;
#   4. team-coloured base plazas (Azure, Crimson rings) at the pads, found in the sim-baked reference (minimap-sim-ref@4x.png);
#   5. the navy sky with a soft haze around the land (the HUD frame's colours).
# usage: python3 minimap_world.py ORTHO.png SIMREF@4x.png OUTDIR
import sys, os, json
import numpy as np, cv2
from PIL import Image
from scipy import ndimage as ndi
NAVY = np.array([24, 35, 59]); SKY = np.array([30, 45, 74]); HAZE = np.array([46, 74, 112]); CLIFF = np.array([94, 88, 102])
AZURE = np.array([22, 199, 217]); CRIMSON = np.array([255, 90, 95])

def main(ortho, simref, out):
    src = np.asarray(Image.open(ortho).convert("RGBA")).astype(np.float32)
    rgb = src[..., :3]
    mag = (rgb[..., 0] > 200) & (rgb[..., 1] < 70) & (rgb[..., 2] > 200)
    land = ~mag & (src[..., 3] > 10) if (src[..., 3] < 250).mean() > 0.2 and (src[..., 3] > 10).mean() > 0.02 else ~mag
    land = ndi.binary_opening(land, iterations=2)
    lab, n = ndi.label(land)
    if n:
        sizes = ndi.sum(land, lab, range(1, n + 1)); tot = sizes.sum()
        land = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s > 0.004 * tot])
    H8, W8 = land.shape
    W4, H4 = W8 // 2, H8 // 2
    # 2. toon simplification at 4x
    img = cv2.resize(rgb.astype(np.uint8), (W4, H4), interpolation=cv2.INTER_AREA)
    lm = cv2.resize(land.astype(np.uint8) * 255, (W4, H4), interpolation=cv2.INTER_AREA).astype(np.float32) / 255
    flat = cv2.pyrMeanShiftFiltering(cv2.cvtColor(img, cv2.COLOR_RGB2BGR), 7, 22)
    flat = cv2.cvtColor(flat, cv2.COLOR_BGR2RGB).astype(np.float32)
    q = 255 / 11.0; flat = np.round(flat / q) * q
    hsv = cv2.cvtColor(np.clip(flat, 0, 255).astype(np.uint8), cv2.COLOR_RGB2HSV).astype(np.float32)
    hsv[..., 1] = np.clip(hsv[..., 1] * 1.12, 0, 255); hsv[..., 2] = np.clip(hsv[..., 2] * 1.04, 0, 255)
    flat = cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2RGB).astype(np.float32)
    # 3. sky, haze, cliff drop, outline
    canvas = np.ones((H4, W4, 3), np.float32) * SKY
    hard = lm > 0.5
    dist = ndi.distance_transform_edt(~hard)
    haze = np.clip(1 - dist / 26.0, 0, 1) ** 2 * 0.55
    canvas = canvas * (1 - haze[..., None]) + HAZE * haze[..., None]
    drop = 9
    cliff = np.zeros_like(lm); cliff[drop:] = lm[:-drop]
    cliff = np.maximum(cliff, 0)
    shade = np.linspace(1.0, 0.7, drop)
    canvas = canvas * (1 - cliff[..., None]) + CLIFF * cliff[..., None]
    outline_mask = np.clip(np.maximum(lm, cliff), 0, 1)
    ring = ndi.binary_dilation(outline_mask > 0.5, iterations=4) & ~(outline_mask > 0.5)
    ringf = cv2.GaussianBlur(ring.astype(np.float32), (3, 3), 0)
    canvas = canvas * (1 - ringf[..., None]) + NAVY * ringf[..., None]
    canvas = canvas * (1 - lm[..., None]) + flat * lm[..., None]
    # inner outline on the land edge (thin)
    edge = (ndi.binary_dilation(hard, iterations=1) & ~ndi.binary_erosion(hard, iterations=1)).astype(np.float32)
    edge = cv2.GaussianBlur(edge, (3, 3), 0) * 0.85
    canvas = canvas * (1 - edge[..., None]) + NAVY * edge[..., None]
    # 4. team plazas from the sim-baked reference (saturated azure / crimson blobs)
    ref = np.asarray(Image.open(simref).convert("RGB").resize((W4, H4))).astype(np.float32)
    pads = {}
    for team, col in (("azure", AZURE), ("crimson", CRIMSON)):
        dmask = np.linalg.norm(ref - col, axis=2) < 70
        lab2, n2 = ndi.label(dmask)
        if not n2: continue
        sizes = ndi.sum(dmask, lab2, range(1, n2 + 1)); k = int(np.argmax(sizes)) + 1
        ys, xs = np.nonzero(lab2 == k); cy, cx = ys.mean(), xs.mean(); r = max(18.0, np.sqrt(sizes[k - 1] / np.pi) * 1.25)
        pads[team] = [round(float(cx) / 4, 2), round(float(cy) / 4, 2)]
        Y, X = np.mgrid[0:H4, 0:W4]; d = np.sqrt((X - cx) ** 2 + (Y - cy) ** 2)
        tint = np.clip((r - d) / 3, 0, 1) * 0.38
        canvas = canvas * (1 - tint[..., None]) + col * tint[..., None]
        ringt = np.clip(1 - np.abs(d - r) / 3.2, 0, 1) * 0.95
        canvas = canvas * (1 - ringt[..., None]) + col * ringt[..., None]
    out4 = np.clip(canvas, 0, 255).astype(np.uint8)
    os.makedirs(out, exist_ok=True)
    for t, s in ((4, 1), (2, 2), (1, 4)):
        im = Image.fromarray(out4, "RGB")
        if s > 1: im = im.resize((W4 // s, H4 // s), Image.LANCZOS)
        im.convert("RGBA").save(os.path.join(out, f"minimap@{t}x.png"), optimize=True)
    print(json.dumps({"size4x": [W4, H4], "land_share": round(float(hard.mean()), 4), "pads_1x": pads}))

if __name__ == "__main__":
    main(*sys.argv[1:4])
