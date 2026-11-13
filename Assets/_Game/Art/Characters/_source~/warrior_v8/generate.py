"""ONE generation on the hosted TRELLIS.2 page (microsoft/TRELLIS.2 on Hugging Face, MIT), free ZeroGPU quota.
  <venv>/bin/python -I generate.py <input.png> <out_dir> [--res 1024] [--faces 200000] [--tex 2048] [--seed 0]
The token is read by huggingface_hub from its own cache; it is never printed or copied.
Exit 3 = service down or quota spent (never retried, never worked around).
"""
import json, os, shutil, sys, time


def opt(k, d):
    return type(d)(sys.argv[sys.argv.index(k) + 1]) if k in sys.argv else d


def main():
    img, out = sys.argv[1], os.path.abspath(sys.argv[2])
    res, faces, tex, seed = opt("--res", "1024"), opt("--faces", 200000), opt("--tex", 2048), opt("--seed", 0)
    os.makedirs(out, exist_ok=True)
    log = {"input_px": None, "res": res, "faces": faces, "tex": tex, "seed": seed}
    from PIL import Image
    log["input_px"] = Image.open(img).size
    try:
        from gradio_client import Client, handle_file
        from huggingface_hub import get_token
        t0 = time.time()
        c = Client("microsoft/TRELLIS.2", token=get_token(), download_files=os.path.join(out, "dl"), verbose=False)
        try:
            c.predict(api_name="/start_session")
        except Exception as e:
            print("start_session:", type(e).__name__, str(e)[:100])
        pre = c.predict(handle_file(img), api_name="/preprocess_image")
        pp = pre if isinstance(pre, str) else (pre.get("path") if isinstance(pre, dict) else pre)
        shutil.copy(pp, os.path.join(out, "preprocessed.png"))
        log["t_pre"] = round(time.time() - t0, 1)
        c.predict(handle_file(pp), seed, res, api_name="/image_to_3d")
        log["t_gen"] = round(time.time() - t0, 1)
        g = c.predict(faces, tex, api_name="/extract_glb")
        path = g[0] if isinstance(g, (list, tuple)) else g
        if isinstance(path, dict):
            path = path.get("value") or path.get("path")
        shutil.copy(path, os.path.join(out, "raw.glb"))
        log["t_total"] = round(time.time() - t0, 1)
        log["glb_bytes"] = os.path.getsize(os.path.join(out, "raw.glb"))
        print(json.dumps(log))
        json.dump(log, open(os.path.join(out, "gen.json"), "w"), indent=1)
    except Exception as e:
        print("GENERATION FAILED:", type(e).__name__, str(e).replace(os.path.expanduser("~"), "~")[:300])
        log["error"] = type(e).__name__ + ": " + str(e)[:200]
        json.dump(log, open(os.path.join(out, "gen.json"), "w"), indent=1)
        sys.exit(3)


if __name__ == "__main__":
    main()
