import sys, io, contextlib, numpy as np, evaluate as ev, opt
K = ("max","p99","inv","inv_total","vis_sum")
S = dict(alpha=0.0005, ring=3, free_thr=0.995, iters=3)
for cls, variant in (("Farmer","cur"),("Warrior","cur"),("Miner","cur"),("Kaiju","refit")):
    z = dict(np.load(f"{ev.OUT}/fix_{cls}.npz"))
    clips = lambda dd: [str(c) for c in dd["clip_names"]]
    if cls == "Farmer":
        ev.INIT_MIX = 1.0
        of = opt.make_fn(clips, per=7, **S)
    def fn(d, dd, W, A, Sb, tag, locked, bind):
        if cls == "Farmer" and tag == "LOD0":
            return of(d, dd, W, A, Sb, tag, locked, bind)
        return z[f"W_{tag}"].astype(float)
    with contextlib.redirect_stdout(io.StringIO()):
        r = ev.evaluate(cls, variant, every=1, weights_fn=fn, save=True)[0]
    ev.INIT_MIX = 0.0
    if "FUV_Hero" in z:
        z2 = dict(np.load(f"{ev.OUT}/fix_{cls}.npz"))
        for t in ("Hero", "LOD0", "LOD1"): z2[f"FUV_{t}"] = z[f"FUV_{t}"]
        np.savez_compressed(f"{ev.OUT}/fix_{cls}.npz", **z2)
    for t in ("Hero","LOD0"):
        s = r[f"{t}_after_sum"]; b = r[f"{t}_before_sum"]
        print(cls, t, "after", " ".join(f"{k}={s[k]}" for k in K), "| before", " ".join(f"{k}={b[k]}" for k in K), flush=True)
