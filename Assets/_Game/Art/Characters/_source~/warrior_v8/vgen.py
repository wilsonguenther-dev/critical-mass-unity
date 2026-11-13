# Vertex image generation, one call. usage: vgen.py MODEL OUTPREFIX PURPOSE PROMPTFILE REF1 [REF2...]
import ssl, certifi
import sys, json, base64, subprocess, os, datetime, urllib.request, mimetypes
model, outp, purpose, pfile = sys.argv[1:5]; refs = sys.argv[5:]
COST = {"gemini-2.5-flash-image": 0.039, "gemini-3-pro-image-preview": 0.134, "gemini-3-pro-image": 0.134}
env = dict(os.environ, CLOUDSDK_CONFIG=os.path.expanduser("~/.config/critical-mass/gcloud"))
tok = subprocess.run(["gcloud", "auth", "application-default", "print-access-token"], env=env,
                     capture_output=True, text=True).stdout.strip()
parts = []
for r in refs:
    mt = mimetypes.guess_type(r)[0] or "image/png"
    parts.append({"inlineData": {"mimeType": mt, "data": base64.b64encode(open(r, "rb").read()).decode()}})
parts.append({"text": open(pfile).read()})
size = os.environ.get("VSIZE", "2K")
gc = {"responseModalities": ["TEXT", "IMAGE"], "imageConfig": {"aspectRatio": os.environ.get("VASPECT", "1:1")}}
if size != "none": gc["imageConfig"]["imageSize"] = size
body = json.dumps({"contents": [{"role": "user", "parts": parts}], "generationConfig": gc}).encode()
url = f"https://aiplatform.googleapis.com/v1/projects/critical-mass-art/locations/global/publishers/google/models/{model}:generateContent"
req = urllib.request.Request(url, data=body, headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"})
status = "ok"
try:
    resp = json.load(urllib.request.urlopen(req, timeout=300, context=ssl.create_default_context(cafile=certifi.where())))
except urllib.error.HTTPError as e:
    resp = None; status = f"http{e.code}"; print("ERR", e.code, e.read().decode()[:600])
n = 0
if resp:
    for c in resp.get("candidates", []):
        for p in c.get("content", {}).get("parts", []):
            if "inlineData" in p:
                n += 1; fn = f"{outp}_{n}.png"
                open(fn, "wb").write(base64.b64decode(p["inlineData"]["data"])); print("saved", fn)
            elif "text" in p:
                print("TEXT", p["text"][:300])
    if not n: print("NOIMAGE", json.dumps(resp)[:600]); status = "noimage"
    print("usage", resp.get("usageMetadata", {}).get("candidatesTokenCount"))
est = COST.get(model, 0.134) if status in ("ok", "noimage") else 0.0
line = f"{datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')} {model} {size} ${est:.3f} warrior-v8 {purpose} [{status}]\n"
open(os.path.expanduser("~/.config/critical-mass/vertex-spend.log"), "a").write(line); print("LOG", line.strip())
