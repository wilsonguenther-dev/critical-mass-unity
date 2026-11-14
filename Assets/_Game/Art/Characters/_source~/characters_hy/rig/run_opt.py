import sys, io, contextlib, numpy as np, evaluate as ev, opt
K = ("max","p99","inv","inv_total","vis_sum","col","follow_mm","rigid_mm")
cls, variant = sys.argv[1], sys.argv[2]
kw = eval("dict(%s)" % (sys.argv[3] if len(sys.argv) > 3 else ""))
ev.INIT_MIX = kw.pop("mix", 0.0)
noopt = kw.pop("noopt", False)
save = "--save" in sys.argv
every = 1 if save else 3
clips = lambda dd: [str(c) for c in dd["clip_names"]]
buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    r = ev.evaluate(cls, variant, every=every, weights_fn=None if noopt else opt.make_fn(clips, **kw), save=save)[0]
print("\n".join(l for l in buf.getvalue().splitlines() if l.startswith("OPT")))
for t in ("Hero", "LOD0"):
    s = r[f"{t}_after_sum"]; b = r[f"{t}_before_sum"]
    print(cls, variant, ev.INIT_MIX, kw, t, " ".join(f"{k}={s[k]}" for k in K), "| base", " ".join(f"{k}={b[k]}" for k in K[:6]), flush=True)
    if t == "Hero":
        per = r[f"{t}_after"]; pb = r[f"{t}_before"]
        worst = sorted(per, key=lambda c: -pb[c]["vis"] if isinstance(pb[c], dict) and "vis" in pb[c] else 0)[:6]
        for c in worst:
            if isinstance(per[c], dict):
                print("   ", c, "before max/p99/inv/vis", pb[c]["max"], pb[c]["p99"], pb[c]["inv"], pb[c]["vis"], "after", per[c]["max"], per[c]["p99"], per[c]["inv"], per[c]["vis"])
