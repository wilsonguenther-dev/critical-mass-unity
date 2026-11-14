# gen-models-2: one multi-view shape on CPU.  hy_mv_cpu.py <char> <seed> <octree> <views comma list> <out_dir>
import json, os, resource, sys, time
import torch
from PIL import Image
from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline, FloaterRemover, DegenerateFaceRemover
char, seed, octree, views, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4].split(","), sys.argv[5]
os.makedirs(out, exist_ok=True); here = os.path.dirname(os.path.abspath(__file__))
torch.set_num_threads(os.cpu_count() or 4)
log = {"char": char, "seed": seed, "octree": octree, "views": views, "threads": torch.get_num_threads()}
t0 = time.time()
imgs = {v: Image.open(os.path.join(here, "inputs", char, v + ".png")).convert("RGBA") for v in views}
pipe = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained("tencent/Hunyuan3D-2mv", subfolder="hunyuan3d-dit-v2-mv-turbo",
                                                        variant="fp16", device="cpu", dtype=torch.float32)
pipe.enable_flashvdm(mc_algo="mc")
pipe.vae.to(device="cpu", dtype=torch.float32); pipe.model.to(dtype=torch.float32); pipe.conditioner.to(dtype=torch.float32)
pipe.dtype = torch.float32
log["t_load"] = round(time.time() - t0, 1); print(json.dumps(log), flush=True)
mesh = pipe(image=imgs, num_inference_steps=5, octree_resolution=octree, num_chunks=20000,
            generator=torch.manual_seed(seed), output_type="trimesh")[0]
log["t_gen"] = round(time.time() - t0, 1)
mesh = FloaterRemover()(mesh); mesh = DegenerateFaceRemover()(mesh)
mesh.export(os.path.join(out, f"{char}_s{seed}.glb"))
log.update(faces=int(len(mesh.faces)), verts=int(len(mesh.vertices)), t_total=round(time.time() - t0, 1),
           peak_rss_gb=round(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1e6, 2))
json.dump(log, open(os.path.join(out, f"{char}_s{seed}.json"), "w"), indent=1); print(json.dumps(log))
