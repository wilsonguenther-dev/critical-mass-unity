# hud-art-1 feasibility test: one Veo image-to-video clip (no audio), first frame = our own cut-out on a flat key colour.
# usage: python3 veo.py FIRST_FRAME.png OUT.mp4 SECONDS "prompt"     (model veo-3.1-fast-generate-001, 720p, 16:9)
# Logs to the project spend log at an assumed $0.15 per second (a ceiling: Google lists video-only Fast below that).
import ssl, certifi, sys, json, base64, subprocess, os, time, datetime, urllib.request, urllib.error
src, out, secs, prompt = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
MODEL = "veo-3.1-fast-generate-001"; RATE = 0.15
env = dict(os.environ, CLOUDSDK_CONFIG=os.path.expanduser("~/.config/critical-mass/gcloud"))
tok = subprocess.run(["gcloud", "auth", "application-default", "print-access-token"], env=env, capture_output=True, text=True).stdout.strip()
base = f"https://us-central1-aiplatform.googleapis.com/v1/projects/critical-mass-art/locations/us-central1/publishers/google/models/{MODEL}"
ctx = ssl.create_default_context(cafile=certifi.where())
def post(url, body):
    req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"})
    return json.load(urllib.request.urlopen(req, timeout=120, context=ctx))
body = {"instances": [{"prompt": prompt, "image": {"bytesBase64Encoded": base64.b64encode(open(src, "rb").read()).decode(), "mimeType": "image/png"}}],
        "parameters": {"aspectRatio": "16:9", "durationSeconds": secs, "sampleCount": 1, "generateAudio": False, "resolution": "720p"}}
status = "ok"
try:
    op = post(base + ":predictLongRunning", body)["name"]; print("op", op.split("/")[-1])
    for i in range(60):
        time.sleep(10)
        r = post(base + ":fetchPredictOperation", {"operationName": op})
        if r.get("done"):
            if "error" in r: status = "err"; print("ERR", json.dumps(r["error"])[:400]); break
            vids = r.get("response", {}).get("videos", [])
            if not vids: status = "novideo"; print("NOVIDEO", json.dumps(r)[:400]); break
            open(out, "wb").write(base64.b64decode(vids[0]["bytesBase64Encoded"])); print("saved", out, "after", (i + 1) * 10, "s"); break
    else: status = "timeout"
except urllib.error.HTTPError as e:
    status = f"http{e.code}"; print("ERR", e.code, e.read().decode()[:400])
cost = secs * RATE if status in ("ok", "novideo", "timeout") else 0.0
ts = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
open(os.path.expanduser("~/.config/critical-mass/vertex-spend.log"), "a").write(f"{ts} {MODEL} 720p-{secs}s ${cost:.3f} hud-art-1 veo:{os.path.basename(out)} [{status}] (estimate at ${RATE}/s)\n")
print("logged", cost)
