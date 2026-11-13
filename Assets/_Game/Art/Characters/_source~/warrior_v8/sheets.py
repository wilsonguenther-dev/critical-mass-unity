import sys; from PIL import Image, ImageDraw, ImageFont
O = sys.argv[1]
F = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 20)
def strip(tiles, labels, title, H, out):
    ims = [t.resize((round(t.width * H / t.height), H), Image.LANCZOS) for t in tiles]
    W = sum(i.width for i in ims) + 12 * (len(ims) + 1)
    sheet = Image.new("RGB", (W, H + 76), (205, 205, 205)); d = ImageDraw.Draw(sheet)
    x = 12
    for im, lb in zip(ims, labels):
        sheet.paste(im.convert("RGB"), (x, 40)); d.text((x, H + 46), lb, fill=(24, 35, 59), font=F); x += im.width + 12
    d.text((12, 10), title, fill=(24, 35, 59), font=F)
    if sheet.width > 1400: sheet = sheet.resize((1400, round(sheet.height * 1400 / sheet.width)), Image.LANCZOS)
    sheet.save(out, quality=82, optimize=True)
g = lambda n: Image.open(f"gen/{n}.png")
strip([g("c_front"), g("c_tq"), g("c_side"), g("c_back")], ["front", "three-quarter", "side", "back"],
      "Warrior v8: neutral A-pose turnaround, generated from the bible page (Gemini 3 Pro Image). 2D modelling input, not a 3D model.", 760, f"{O}/turnaround.jpg")
page = Image.open("bible/p6-000.png").convert("RGB")
bible = page.crop((10, 60, 460, 545))
v7 = Image.open(sys.argv[2]); cur = v7.crop((v7.width // 2, 0, v7.width, v7.height))
strip([bible, cur, g("c_front")], ["BIBLE (artist page)", "CURRENT in game (v7, scripted)", "NEW v8 input (2D, no 3D model yet)"],
      "Warrior front: bible / current / new. The 3D step did not run (free TRELLIS quota spent), so 'new' is the A-pose reference image.", 640, f"{O}/compare.jpg")
