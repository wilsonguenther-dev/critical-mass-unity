# hud-art-1: Vertex image generation with the project's spend log and a hard budget.
# usage: python3 gen.py jobs.json [only-name ...]     jobs.json: [{"name", "model", "aspect", "size", "prompt", "refs": [paths]}]
# Every call (ok, error or no image) appends one line to ~/.config/critical-mass/vertex-spend.log tagged hud-art-1.
# Refuses to start a call once hud-art-1's logged image spend reaches BUDGET_IMAGES (the video tests have their own reserve).
import ssl, certifi, sys, json, base64, subprocess, os, datetime, urllib.request, urllib.error, mimetypes, io, threading
from concurrent.futures import ThreadPoolExecutor
from PIL import Image
LOG = os.path.expanduser("~/.config/critical-mass/vertex-spend.log")
COST = {"gemini-2.5-flash-image": 0.039, "gemini-3-pro-image": 0.134}
BUDGET_IMAGES = 23.0
OUT = os.environ.get("HUDART_OUT", "/tmp/hudart-gen")
lock = threading.Lock()

def spent():
    s = 0.0
    for ln in open(LOG):
        p = ln.split()
        if len(p) > 4 and p[4] == "hud-art-1":
            try: s += float(p[3].lstrip("$"))
            except ValueError: pass
    return s

def token():
    env = dict(os.environ, CLOUDSDK_CONFIG=os.path.expanduser("~/.config/critical-mass/gcloud"))
    return subprocess.run(["gcloud", "auth", "application-default", "print-access-token"], env=env,
                          capture_output=True, text=True).stdout.strip()

def ref_part(path):
    im = Image.open(path).convert("RGB"); im.thumbnail((1024, 1024))
    b = io.BytesIO(); im.save(b, "JPEG", quality=88)
    return {"inlineData": {"mimeType": "image/jpeg", "data": base64.b64encode(b.getvalue()).decode()}}

def run(job, tok):
    with lock:
        s = spent()
        if s + COST[job["model"]] > BUDGET_IMAGES:
            print("BUDGET STOP", job["name"], f"{s:.3f}"); return
    parts = [ref_part(r) for r in job.get("refs", [])] + [{"text": job["prompt"]}]
    gc = {"responseModalities": ["TEXT", "IMAGE"], "imageConfig": {"aspectRatio": job.get("aspect", "1:1")}}
    if job["model"] == "gemini-3-pro-image": gc["imageConfig"]["imageSize"] = job.get("size", "2K")
    body = json.dumps({"contents": [{"role": "user", "parts": parts}], "generationConfig": gc}).encode()
    url = ("https://aiplatform.googleapis.com/v1/projects/critical-mass-art/locations/global/publishers/google/models/"
           f"{job['model']}:generateContent")
    req = urllib.request.Request(url, data=body, headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"})
    status, n = "ok", 0
    try:
        resp = json.load(urllib.request.urlopen(req, timeout=300, context=ssl.create_default_context(cafile=certifi.where())))
    except urllib.error.HTTPError as e:
        resp = None; status = f"http{e.code}"; print("ERR", job["name"], e.code, e.read().decode()[:300])
    except Exception as e:
        resp = None; status = "neterr"; print("ERR", job["name"], str(e)[:200])
    if resp:
        for c in resp.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                if "inlineData" in p:
                    n += 1; fn = os.path.join(OUT, f"{job['name']}.png")
                    open(fn, "wb").write(base64.b64decode(p["inlineData"]["data"])); print("saved", fn)
        if not n: status = "noimage"; print("NOIMAGE", job["name"], json.dumps(resp)[:300])
    est = COST[job["model"]] if status in ("ok", "noimage") else 0.0
    ts = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    with lock:
        open(LOG, "a").write(f"{ts} {job['model']} {job.get('size','1K')} ${est:.3f} hud-art-1 {job['name']} [{status}]\n")

def run_retry(job, tok):
    import time
    for attempt in range(3):
        n0 = sum(1 for ln in open(LOG) if f" {job['name']} [http429]" in ln)
        run(job, tok)
        n1 = sum(1 for ln in open(LOG) if f" {job['name']} [http429]" in ln)
        if n1 == n0: return
        time.sleep(25 * (attempt + 1))

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    jobs = json.load(open(sys.argv[1]))
    only = set(sys.argv[2:])
    if only: jobs = [j for j in jobs if j["name"] in only]
    tok = token()
    with ThreadPoolExecutor(max_workers=int(os.environ.get("HUDART_PAR", "3"))) as ex:
        list(ex.map(lambda j: run_retry(j, tok), jobs))
    print(f"hud-art-1 image spend now ${spent():.3f}")
