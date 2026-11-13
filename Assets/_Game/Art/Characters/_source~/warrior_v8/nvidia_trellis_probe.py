# DEAD END (2026-11-13): the hosted NVIDIA TRELLIS trial only accepts its 4 built-in example images
# ("data:image/png;example_id,N"); a real image returns HTTP 422 "Expected: example_id, got: base64",
# and the NVCF asset-upload API (api.nvcf.nvidia.com/v2/nvcf/assets) returns HTTP 410 Gone. Kept as a record.

import os, sys, json, base64, urllib.request, urllib.error, time, ssl, certifi
CTX = ssl.create_default_context(cafile=certifi.where())
img, out, seed = sys.argv[1], sys.argv[2], int(sys.argv[3])
key = os.environ['NVIDIA_API_KEY']
b64 = base64.b64encode(open(img,'rb').read()).decode()
mime = 'image/png' if img.endswith('.png') else 'image/jpeg'
body = {"image": f"data:{mime};base64,{b64}", "mode": "image", "seed": seed,
        "output_format": "glb", "samples": 1,
        "ss_sampling_steps": int(os.environ.get('SS',25)), "slat_sampling_steps": int(os.environ.get('SL',25)),
        "ss_cfg_scale": 7.5, "slat_cfg_scale": 3}
if os.environ.get('ASSET'):
    body["image"] = f"data:{mime};asset_id,{os.environ['ASSET']}"
req = urllib.request.Request("https://ai.api.nvidia.com/v1/genai/microsoft/trellis",
    data=json.dumps(body).encode(), method="POST",
    headers={"Authorization": "Bearer "+key, "Accept": "application/json", "Content-Type": "application/json",
             **({"NVCF-INPUT-ASSET-REFERENCES": os.environ['ASSET']} if os.environ.get('ASSET') else {})})
t=time.time()
try:
    r = urllib.request.urlopen(req, timeout=600, context=CTX); code=r.status; data=r.read()
except urllib.error.HTTPError as e:
    code=e.code; data=e.read()
print('HTTP', code, 'in', round(time.time()-t,1),'s', 'bytes', len(data))
if code != 200:
    print(data[:800].decode('utf-8','replace').replace(key,'***')); sys.exit(2)
j = json.loads(data)
for a in j.get('artifacts', []):
    print('finish', a.get('finishReason'), 'seed', a.get('seed'))
    if a.get('base64'):
        raw = base64.b64decode(a['base64']); open(out,'wb').write(raw); print('wrote', out, len(raw), raw[:4])
