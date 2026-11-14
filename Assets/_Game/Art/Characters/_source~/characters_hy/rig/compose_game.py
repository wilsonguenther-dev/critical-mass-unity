import os, sys, json
from PIL import Image, ImageDraw
R, O = sys.argv[1], sys.argv[2]
for cls in ("Warrior", "Farmer", "Miner", "Kaiju"):
    d = os.path.join(R, cls)
    clips = sorted({f[len("gameworst_after_"):-4] for f in os.listdir(d) if f.startswith("gameworst_after_")})
    if not clips: continue
    m = json.load(open(os.path.expanduser(f"~/cm-rig-scratch/out/metrics_{cls}.json")))
    tile = 150; cols = 8; rows = (len(clips) + cols - 1) // cols
    W, H = cols * tile + 10, rows * (2 * tile + 40) + 40
    im = Image.new("RGB", (W, H), (205, 205, 205)); dr = ImageDraw.Draw(im)
    dr.text((8, 8), f"{cls}: gameplay camera (55 deg, LOD0, game-resolution crop shown 1:1.3), worst frame of every clip; top BEFORE, bottom AFTER", fill=(0, 0, 0))
    for i, c in enumerate(clips):
        x = (i % cols) * tile + 5; y = 30 + (i // cols) * (2 * tile + 40)
        dr.text((x, y), c[:24], fill=(0, 0, 0))
        for k, w in enumerate(("before", "after")):
            t = Image.open(os.path.join(d, f"gameworst_{w}_{c}.png")).convert("RGB").resize((tile - 4, tile - 4))
            im.paste(t, (x, y + 12 + k * tile))
        a = m["Hero_after"].get(c, {}); b = m["Hero_before"].get(c, {})
        dr.text((x, y + 12 + 2 * tile - 2), f"{b.get('max','')}/{b.get('inv','')} -> {a.get('max','')}/{a.get('inv','')}", fill=(0, 70, 0))
    if im.width > 1400: im = im.resize((1400, int(im.height * 1400 / im.width)))
    p = os.path.join(O, f"game-{cls.lower()}.jpg"); q = 85
    while True:
        im.save(p, quality=q)
        if os.path.getsize(p) <= 400_000 or q < 40: break
        q -= 10
    print("wrote", p, os.path.getsize(p))
