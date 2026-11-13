"""Run a list of Vertex image jobs one after another, under the turnarounds-v1 spend cap.
usage: python3 batch.py <scratch> JOB [JOB...]   JOB = outname:promptname:ref1,ref2,...  (refs relative to <scratch>)
Outputs <scratch>/gen/<outname>_1.png. Waits 15 s between calls; an HTTP 429 (not billed) is retried up to 8 times after 45 s (run ONE batch at a time: parallel batches only collect 429s).
Stops before any call that would take the logged turnarounds-v1 spend past CAP (default $9.00)."""
import sys, os, subprocess, time
H = os.path.dirname(os.path.abspath(__file__)); S = sys.argv[1]; CAP = float(os.environ.get("CAP", "9.00"))
LOG = os.path.expanduser("~/.config/critical-mass/vertex-spend.log")
def spent():
    t = 0.0
    for l in open(LOG):
        if " turnarounds-v1 " in l and l.split()[3].startswith("$"): t += float(l.split()[3][1:])
    return t
os.makedirs(f"{S}/gen", exist_ok=True)
for i, job in enumerate(sys.argv[2:]):
    out, prompt, refs = job.split(":")
    for attempt in range(9):
        if spent() + 0.134 > CAP + 1e-9: print("CAP reached, stop. spent", round(spent(), 3)); sys.exit(2)
        r = subprocess.run([sys.executable, f"{H}/vgen.py", "gemini-3-pro-image", f"{S}/gen/{out}", out, f"{H}/prompts/{prompt}.txt"]
                           + [f"{S}/{x}" for x in refs.split(",")], capture_output=True, text=True,
                           env=dict(os.environ, VTAG="turnarounds-v1"))
        o = r.stdout + r.stderr
        lines = [l for l in o.splitlines() if l.startswith(("saved", "ERR", "NOIMAGE", "LOG", "Traceback")) or "Error" in l]
        print(out, "|", " ; ".join(x[:160] for x in lines))
        if "[http429]" in o or "ERR 5" in o: time.sleep(45); continue
        break
    time.sleep(15)
print("turnarounds-v1 spend so far: $%.3f" % spent())
