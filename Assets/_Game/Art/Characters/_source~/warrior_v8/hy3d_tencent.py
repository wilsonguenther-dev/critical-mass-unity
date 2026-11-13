#!/usr/bin/env python3
"""Tencent HY 3D Global (international) client, standard library only.

Docs read 2026-11-13 (tencentcloud.com/document/api/1284/...):
  host hunyuan.intl.tencentcloudapi.com, service "hunyuan", version 2023-09-01,
  regions ap-singapore / eu-frankfurt / na-siliconvalley, TC3-HMAC-SHA256 signing.
  SubmitHunyuanTo3DProJob  (Professional): Model 3.0|3.1, ImageBase64, MultiViewImages[{ViewType,ViewImageBase64}]
      ViewType: left, right, back (+ top, bottom, left_front, right_front on 3.1). "left" = the object's
      left side, i.e. the character faces image-left (checked against Tencent's Hunyuan3D-2 mv examples).
  SubmitHunyuanTo3DRapidJob (Express): ImageBase64, ResultFormat (OBJ, GLB, ...), EnablePBR.
  Query...Job: Status WAIT|RUN|FAIL|DONE, ResultFile3Ds[{Type,Url(24 h),PreviewImageUrl}].
Credits (Billing Overview, doc 1284/75281): Pro Normal 25, +10 multi-view, +10 PBR, +10 custom FaceCount,
  LowPoly 30, Geometry 15; Express image 15 (+10 PBR); Smart Topology 50; Part 30; Texture Edit 30;
  UV 10; Auto Rigging 10; Convert 5. Failed tasks are not charged. Free package does not roll over to
  postpaid unless enabled in the console.

Credentials come from the environment only (TENCENTCLOUD_SECRET_ID / TENCENTCLOUD_SECRET_KEY);
load them in the same shell command:  set -a; . ~/.config/critical-mass/tencent.env; set +a; python3 -I hy3d_tencent.py ...
Nothing secret is ever printed.

Usage:
  hy3d_tencent.py pro   OUTDIR FRONT.png [--back B.png] [--left L.png] [--right R.png] [--left-front LF.png] [--model 3.1] [--tag name]
  hy3d_tencent.py rapid OUTDIR IMAGE.png [--tag name]
Every submitted job appends one line to the ledger; the run refuses if ledger + cost would pass the cap.
"""
import argparse, base64, datetime, hashlib, hmac, json, os, sys, time, urllib.request, urllib.error

HOST = "hunyuan.intl.tencentcloudapi.com"
SERVICE = "hunyuan"
VERSION = "2023-09-01"
REGION = os.environ.get("HY3D_REGION", "ap-singapore")
LEDGER = os.path.expanduser("~/.config/critical-mass/tencent-credits.log")
# HARD STOP 2026-11-13 14:25 UTC: the 200 free credits are spent and the package would roll into pay-as-you-go.
# The cap is pinned to the ledger total (200); the environment can no longer raise it, so every submit refuses.
CAP = min(float(os.environ.get("HY3D_CREDIT_CAP", "200")), 200.0)
STOP_WORDS = ("postpaid", "arrear", "overdue", "insufficient", "balance", "billing", "unpaid",
              "resourceunavailable", "notopen", "resourcepack")


def _sign(key, msg):
    return hmac.new(key, msg.encode("utf-8"), hashlib.sha256).digest()


def call(action, payload):
    sid = os.environ["TENCENTCLOUD_SECRET_ID"]
    skey = os.environ["TENCENTCLOUD_SECRET_KEY"]
    ts = int(time.time())
    date = datetime.datetime.fromtimestamp(ts, datetime.timezone.utc).strftime("%Y-%m-%d")
    body = json.dumps(payload, separators=(",", ":"))
    ct = "application/json; charset=utf-8"
    canonical_headers = f"content-type:{ct}\nhost:{HOST}\nx-tc-action:{action.lower()}\n"
    signed_headers = "content-type;host;x-tc-action"
    canonical = "\n".join(["POST", "/", "", canonical_headers, signed_headers,
                           hashlib.sha256(body.encode("utf-8")).hexdigest()])
    scope = f"{date}/{SERVICE}/tc3_request"
    sts = "\n".join(["TC3-HMAC-SHA256", str(ts), scope, hashlib.sha256(canonical.encode("utf-8")).hexdigest()])
    k = _sign(_sign(_sign(("TC3" + skey).encode("utf-8"), date), SERVICE), "tc3_request")
    sig = hmac.new(k, sts.encode("utf-8"), hashlib.sha256).hexdigest()
    auth = f"TC3-HMAC-SHA256 Credential={sid}/{scope}, SignedHeaders={signed_headers}, Signature={sig}"
    req = urllib.request.Request(f"https://{HOST}/", data=body.encode("utf-8"), method="POST", headers={
        "Authorization": auth, "Content-Type": ct, "Host": HOST, "X-TC-Action": action,
        "X-TC-Timestamp": str(ts), "X-TC-Version": VERSION, "X-TC-Region": REGION})
    with urllib.request.urlopen(req, timeout=120) as r:
        resp = json.loads(r.read().decode("utf-8"))["Response"]
    if "Error" in resp:
        err = resp["Error"]
        text = (err.get("Code", "") + " " + err.get("Message", "")).lower()
        print(f"API ERROR {action}: {err.get('Code')}: {err.get('Message')} (RequestId {resp.get('RequestId')})")
        if any(w in text for w in STOP_WORDS):
            print("STOP: the error mentions billing / balance / postpaid. Not retrying.")
            sys.exit(4)
        sys.exit(2)
    return resp


def ledger_total():
    tot = 0.0
    if os.path.exists(LEDGER):
        for line in open(LEDGER):
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 4 and parts[0] != "#":
                try:
                    tot += float(parts[3])
                except ValueError:
                    pass
    return tot


def ledger_add(action, options, credits, note):
    os.makedirs(os.path.dirname(LEDGER), exist_ok=True)
    new = not os.path.exists(LEDGER)
    with open(LEDGER, "a") as f:
        if new:
            f.write("# time_utc\taction\toptions\tcredits\tnote  (free package 200; agent cap %d)\n" % CAP)
        t = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        f.write(f"{t}\t{action}\t{options}\t{credits:g}\t{note}\n")


def b64(path):
    with open(path, "rb") as f:
        return base64.b64encode(f.read()).decode("ascii")


def poll_and_fetch(query_action, job, outdir, tag, max_wait=1800):
    t0 = time.time()
    while True:
        r = call(query_action, {"JobId": job})
        st = r.get("Status")
        if st == "DONE":
            break
        if st == "FAIL":
            print(f"JOB FAILED {job}: {r.get('ErrorCode')} {r.get('ErrorMessage')} (failed jobs are not charged)")
            return None
        if time.time() - t0 > max_wait:
            print(f"TIMEOUT waiting for {job}; status {st}. Re-run with: query {query_action} {job}")
            return None
        time.sleep(15)
    os.makedirs(outdir, exist_ok=True)
    files = []
    for i, f in enumerate(r.get("ResultFile3Ds", [])):
        for key in ("Url", "PreviewImageUrl"):
            url = f.get(key)
            if not url:
                continue
            base = url.split("?")[0].rsplit("/", 1)[-1] or f"file{i}"
            dst = os.path.join(outdir, f"{tag}-{'preview-' if key != 'Url' else ''}{base}")
            urllib.request.urlretrieve(url, dst)
            files.append((f.get("Type"), dst, os.path.getsize(dst)))
    with open(os.path.join(outdir, f"{tag}-job.json"), "w") as fh:
        json.dump({"job": job, "action": query_action, "types": [x[0] for x in files],
                   "files": [os.path.basename(x[1]) for x in files]}, fh, indent=1)
    for t, p, s in files:
        print(f"got {t} {os.path.basename(p)} {s} bytes")
    print(f"done in {time.time() - t0:.0f} s")
    return files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["pro", "rapid", "query"])
    ap.add_argument("outdir")
    ap.add_argument("image", nargs="?")
    ap.add_argument("--back"); ap.add_argument("--left"); ap.add_argument("--right")
    ap.add_argument("--left-front"); ap.add_argument("--right-front")
    ap.add_argument("--model", default="3.1")
    ap.add_argument("--tag", default="gen")
    ap.add_argument("--job"); ap.add_argument("--query-action")
    a = ap.parse_args()

    if a.mode == "query":
        poll_and_fetch(a.query_action, a.job, a.outdir, a.tag)
        return
    if a.mode == "pro":
        views = [(v, p) for v, p in (("back", a.back), ("left", a.left), ("right", a.right),
                                      ("left_front", a.left_front), ("right_front", a.right_front)) if p]
        payload = {"Model": a.model, "ImageBase64": b64(a.image)}
        cost = 25.0
        if views:
            payload["MultiViewImages"] = [{"ViewType": v, "ViewImageBase64": b64(p)} for v, p in views]
            cost += 10.0
        action, qaction = "SubmitHunyuanTo3DProJob", "QueryHunyuanTo3DProJob"
        opts = f"Model={a.model};Normal;views=front+{'+'.join(v for v, _ in views) or 'none'}"
    else:
        payload = {"ImageBase64": b64(a.image), "ResultFormat": "GLB"}
        cost = 15.0
        action, qaction = "SubmitHunyuanTo3DRapidJob", "QueryHunyuanTo3DRapidJob"
        opts = "image;GLB;noPBR"
    used = ledger_total()
    if used + cost > CAP:
        print(f"REFUSED: ledger {used:g} + {cost:g} credits would pass the cap {CAP:g}.")
        sys.exit(3)
    r = call(action, payload)
    job = r["JobId"]
    ledger_add(action, opts, cost, f"tag={a.tag} job={job}")
    print(f"submitted {action} job {job}; cost {cost:g}; ledger now {used + cost:g}/{CAP:g}")
    files = poll_and_fetch(qaction, job, a.outdir, a.tag)
    if files is None:
        ledger_add(action, opts, -cost, f"tag={a.tag} job={job} FAILED (not charged per docs)")


if __name__ == "__main__":
    main()
