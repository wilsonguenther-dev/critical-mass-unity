# bible_fit: turn the bible art into measurable targets

The owner's idea: "a tool like a mesh tool you can drop on the original asset so you can tell yourself where to stop
painting, where to stop bloating ... how much colour goes over here". These scripts turn each bible crop into a
**guide** (silhouette width at every 1 % of height, the flat colour regions with where they sit, landmarks), compare a
render of our model against it in plain words, and project the art onto the posed mesh inside Blender.

| File | Runs in | What it does |
|---|---|---|
| `bible_guide.py` | system python3 (numpy, Pillow, scipy, OpenCV) | bible crop -> `guide-<prefix>-<view>.json/.npz/.jpg` |
| `fit_report.py` | system python3 | guide + our render -> `<out>.jpg` (<= 960 px) + `<out>.json` with the ranked to-do list |
| `project_paint.py` | Blender (numpy only) | bible view -> per-vertex guide colours + bloat/missing report; camera setup; depth numbers |
| `paint_preview.py` | system python3 | picture of a project_paint result |
| `trellis_fetch.py`, `depth_hints.py` | a venv with gradio_client, huggingface_hub, trimesh, scipy, Pillow | optional depth hints from one hosted TRELLIS.2 generation |
| `examples/warrior_fit_run.py` | Blender | the Warrior proof run: renders + project_paint for 4 views in one launch |
| `bible_common.py` | system python3 | shared measuring code |

Ready-made guides for all twelve class views (06 Warrior, 07 Farmer, 08 Miner: front, three-quarter, profile,
back) are in `guides/` (the Warrior's carry depth hints); previews and the Warrior proof report are in the Obsidian
folder `Projects/Kaiju-Demo/unity-port/bible-fit-tool/`. Regenerate only if a bible crop changes.

`Characters/_source~/cm_art/project_paint.py` is a shim, so a class script can `from cm_art import project_paint`.

## Coordinates (every number in a guide or report)
`y` = height above the feet as a fraction of the character height (0 = lowest silhouette pixel, 1 = top of the hair).
`x` = sideways offset from the **head anchor** (centroid of the silhouette between 75 and 95 % height) in character
heights, screen right positive. Our render is scaled to the bible's character height, feet on the same row, then slid
sideways to the best overlap (the slide is reported).

## The camera fit_report needs
Orthographic, from the view's angle (the guide's `camera_suggested`: front 0, three-quarter -35, profile -90, back 180
degrees azimuth, 4 to 6 degrees elevation; azimuth 0 = camera at -Y in front of a character facing -Y, negative =
camera toward his right). Tune the azimuth to your drawing's turn, as the Warrior's `render_warrior.py` POSES do (front
8). Framing and resolution are free; the whole character must be in frame; transparent film, and ideally an ID mask
render (all-white material, outline hull included) passed with `--mask`. `project_paint.setup_guide_camera(scene,
meshes, azimuth, elevation, height_px=720)` builds exactly that camera.

## The loop for a class script (guide once, render, report, fix the top items, repeat)
```bash
T=Assets/_Game/Art/_tools~/bible_fit
REF=~/critical-mass/loop/parity/ref
# 1. once per class: the guides (prefix 06 Warrior, 07 Farmer, 08 Miner)
python3 $T/bible_guide.py $REF 07 /path/to/your/scratch/guides
# 2. render the bible framings (your render script; orthographic, transparent, plus an ID mask), through the limiter:
~/bin/cm-heavy blender perl -e 'alarm 900; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
    --factory-startup --python-exit-code 1 -P render_farmer.py -- bible /path/to/renders 720
# 3. one report per view
for v in front three-quarter profile back; do
  python3 $T/fit_report.py guides/guide-07-$v.json renders/$v.png reports/farmer-$v --mask renders/$v.mask.png
done
# 4. fix the top items of reports/farmer-*.json "todo" (big shapes first), rebuild, go to 2
```
Inside a Blender run (same launch as the renders, never a separate probe launch):
```python
from cm_art import project_paint as pp
res = pp.project_view(meshes, "guides/guide-07-front.json", azimuth_deg=0, elevation_deg=6, outline_m=0.0135)
pp.save_result(res, "reports/project-front")         # then: python3 $T/paint_preview.py guide.json reports/project-front out.jpg
print("\n".join(res["report"]["summary"]))           # bloat per height band / side / part (feat_* groups) / bone
pp.write_guide_attributes(res)                        # _bible_guide_rgb / _conf / _label point attributes (paint GUIDE)
pp.apply_guide_paint(body, res, regions=["team teal"], min_conf=0.7)   # optional starting paint, RGB only
pp.clear_guide_attributes(meshes)                     # before export
print(pp.depth_profile(meshes, 0, 0))                 # compare with the guide's depth_hints
```
**Team-mask contract:** `Col` alpha = team-mask code (cm_art/palette.py). project_paint never writes alpha: the guide
lives in generic (non-colour) attributes the FBX exporter does not export, and `apply_guide_paint` writes RGB only and
raises if alpha changed.

## How far to trust each number
| Measurement | Reliability | Why |
|---|---|---|
| Width profile, IoU, bloat/missing map | high | pixel masks; +-1 px is about 0.3 % of height. Pose differences (a weapon held differently) show up as one-sided "edge" items: read them as pose or prop, not body |
| Colour share and where it sits (10 % bands) | medium | self-test floor about 1.5 points (bible k-means labels vs nearest-palette labels on ours); items under 2 points of total variation are dropped |
| Dark tones (navy, hair, painted shadow, L < 25) | low | painted shading and ink merge with them; flagged in the text and given half weight |
| Chin, eye line (front, three-quarter, profile) | medium-high | from the face's skin region |
| Shoulder line, head width | medium | from the silhouette's core run; armour that rides up beside the head counts |
| Leg split | medium | only exists when the legs show a gap |
| Waist; anything in a back view from the face | low | not ranked |
| project_paint per-vertex colour | medium on flat front-facing areas | confidence = neighbourhood agreement x facing; dark shading regions win on edges |
| Depth hints (TRELLIS) | low-medium | one generated guess; see the guide's `depth_hints.notes` and `check_vs_profile_view` |

## Depth hints (optional)
```bash
python3 -m venv /path/to/scratch/venv && /path/to/scratch/venv/bin/pip install gradio_client huggingface_hub trimesh scipy pillow
/path/to/scratch/venv/bin/python -I $T/trellis_fetch.py $REF/06-front.png /path/to/scratch/trellis   # ONE generation
/path/to/scratch/venv/bin/python -I $T/depth_hints.py /path/to/scratch/trellis/trellis.glb guides/guide-06-front.json \
    --profile-guide guides/guide-06-profile.json --into guides/guide-06-front.json,guides/guide-06-profile.json
```
The token is read from `~/.cache/huggingface/token` by huggingface_hub and never printed. About 8 to 10 free
generations a day. The generated mesh is a shape reference only: never an asset.
