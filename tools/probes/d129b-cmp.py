# -*- coding: utf-8 -*-
"""
D129b 原版同机位并排图生成器（可复跑）

用途：把「原版同机位参考图」与「本工程 Play 实拍」按**同一分辨率、同一相机取景**并排，
      作为 D129b A（血条常显）/ C（帧段口径）实机证据的对照物。

同机位定义（出处）：
  - 本工程 Play 实拍分辨率 = 1080x1920（`.ai-tmp/test/D129b-captured.txt` 里 `size=1080x1920`）。
  - 原版参考图里唯一 1080x1920 的对局图 = `策划/参考图/20_对局_1080x1920.jpg`
    （`策划/参考图/清单.md` 登记该图来自 1080x1920 机型）。
  两者同宽同高 ⇒ 竞技场地形占屏比例/相机取景可直接叠放比较，无需缩放。

产出：
  .ai-tmp/test/D129b-cmp-ref20-vs-mine.png   —— 左右并排 + 文字标签（整屏）
  .ai-tmp/test/D129b-cmp-zoom-units.png      —— 单位带血条区域放大对照（裁切同 y 区间）

复跑：
  /c/Python312/python tools/probes/d129b-cmp.py --root .
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont


def load_font(size):
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def label_panel(img, title, color):
    """在图片顶部加一条标题栏（不遮挡内容）。"""
    bar = 44
    out = Image.new("RGB", (img.width, img.height + bar), (24, 24, 24))
    out.paste(img, (0, bar))
    d = ImageDraw.Draw(out)
    d.rectangle([0, 0, img.width, bar], fill=color)
    d.text((12, 10), title, font=load_font(26), fill=(255, 255, 255))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=".")
    ap.add_argument("--ref", default="策划/参考图/20_对局_1080x1920.jpg")
    ap.add_argument("--mine", default=".ai-tmp/screenshots/D129b-battle-units.png")
    ap.add_argument("--outdir", default=".ai-tmp/test")
    args = ap.parse_args()

    ref = Image.open(os.path.join(args.root, args.ref)).convert("RGB")
    mine = Image.open(os.path.join(args.root, args.mine)).convert("RGB")
    if ref.size != mine.size:
        # 同机位图应当同尺寸；若不同则按高度对齐缩放（并在标签里写明实际尺寸）
        h = min(ref.height, mine.height)
        ref = ref.resize((int(ref.width * h / ref.height), h))
        mine = mine.resize((int(mine.width * h / mine.height), h))

    # ---- 1) 整屏并排 ----
    refp = label_panel(ref, "REF  original  %dx%d  (chunJie/参考图/20)" % (ref.width, ref.height), (140, 40, 40))
    minep = label_panel(mine, "MINE  clover-project-cr  %dx%d  (Play capture)" % (mine.width, mine.height), (30, 90, 160))
    gap = 16
    W = refp.width + gap + minep.width
    H = max(refp.height, minep.height)
    canvas = Image.new("RGB", (W, H), (12, 12, 12))
    canvas.paste(refp, (0, 0))
    canvas.paste(minep, (refp.width + gap, 0))
    d = ImageDraw.Draw(canvas)
    d.text((refp.width + 2, 4), "|<", font=load_font(30), fill=(255, 220, 0))
    out1 = os.path.join(args.root, args.outdir, "D129b-cmp-ref20-vs-mine.png")
    canvas.save(out1)
    print("wrote", out1, canvas.size)

    # ---- 2) 单位+血条区域放大对照（同 y 区间）----
    # 取竞技场中场（河道两侧）同一 y 条带：两图同为 1080x1920 同机位 ⇒ 同 y = 同世界坐标。
    # 本工程该带为「苍蝇海跨河 + 常显血条」；原版该带为「P.E.K.K.A 等混合兵种 + 血条 + 等级牌」。
    # 出处：`.ai-tmp/test/_probe-mine-band2.png` 实测苍蝇海+绿条落在 y≈760-1030。
    band = (0, 720, 1080, 1090)
    rc = ref.crop(band)
    mc = mine.crop(band)
    scale = 2
    rc = rc.resize((rc.width * scale, rc.height * scale), Image.LANCZOS)
    mc = mc.resize((mc.width * scale, mc.height * scale), Image.LANCZOS)
    rcp = label_panel(rc, "REF  mid-field units + HP bars", (140, 40, 40))
    mcp = label_panel(mc, "MINE mid-field units + HP bars (minions, bars always-on)", (30, 90, 160))
    W2 = rcp.width + gap + mcp.width
    H2 = max(rcp.height, mcp.height)
    c2 = Image.new("RGB", (W2, H2), (12, 12, 12))
    c2.paste(rcp, (0, 0))
    c2.paste(mcp, (rcp.width + gap, 0))
    out2 = os.path.join(args.root, args.outdir, "D129b-cmp-zoom-units.png")
    c2.save(out2)
    print("wrote", out2, c2.size)


if __name__ == "__main__":
    main()
