"""Send ONE concept-art image to the hosted TRELLIS.2 Space (Microsoft, MIT) and download the GLB, as a SHAPE REFERENCE only.

  <venv>/bin/python trellis_fetch.py <image.png> <out_dir> [--upscale 2]

Needs a virtual environment with gradio_client + huggingface_hub (pip install gradio_client huggingface_hub trimesh).
The Hugging Face token is read by huggingface_hub from ~/.cache/huggingface/token; it is never printed or copied.
Quota: the ZeroGPU free tier is about 5 GPU-minutes a day (8 to 10 generations). One call = one generation.
The owner approved sending concept art to this public Space for shape reference (2026-11-12). Never use the mesh as an asset.
Exit code 3 = service down or over quota (skip depth hints and say so).
"""
import os
import shutil
import sys
import time


def main():
    img, out = sys.argv[1], os.path.abspath(sys.argv[2])
    up = 2
    if "--upscale" in sys.argv:
        up = int(sys.argv[sys.argv.index("--upscale") + 1])
    os.makedirs(out, exist_ok=True)
    from PIL import Image
    src = Image.open(img).convert("RGB")
    sent = os.path.join(out, "sent.png")
    src.resize((src.width * up, src.height * up), Image.LANCZOS).save(sent)
    try:
        from gradio_client import Client, handle_file
        from huggingface_hub import get_token
        t0 = time.time()
        dl = os.path.join(out, "dl")
        c = Client("microsoft/TRELLIS.2", token=get_token(), download_files=dl, verbose=False)
        try:
            c.predict(api_name="/start_session")
        except Exception as e:  # the Space sometimes answers start_session with an empty error
            print("start_session:", type(e).__name__, str(e)[:120])
        pre = c.predict(handle_file(sent), api_name="/preprocess_image")
        print("preprocess ok", round(time.time() - t0, 1), "s")
        c.predict(handle_file(pre), 0, "512", api_name="/image_to_3d")
        print("generate ok", round(time.time() - t0, 1), "s")
        g = c.predict(100000, 1024, api_name="/extract_glb")     # 100k is the Space's minimum decimation
        path = g[0] if isinstance(g, (list, tuple)) else g
        if isinstance(path, dict):
            path = path.get("value") or path.get("path")
        shutil.copy(path, os.path.join(out, "trellis.glb"))
        print("GLB", os.path.join(out, "trellis.glb"), os.path.getsize(os.path.join(out, "trellis.glb")), "bytes",
              round(time.time() - t0, 1), "s")
    except Exception as e:
        msg = str(e).replace(os.path.expanduser("~"), "~")[:300]
        print("TRELLIS FAILED:", type(e).__name__, msg)
        sys.exit(3)


if __name__ == "__main__":
    main()
