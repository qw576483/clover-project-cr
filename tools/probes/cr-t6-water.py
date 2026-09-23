# CR-T6: 逐帧量化 arena_training_out（+ tex 图集）里的「水/草/铺装」，并给出各帧水面颜色与位置
# 复跑：python .ai-tmp/test/t6-water.py
import os
import numpy as np
from PIL import Image

SRC = r"C:\Work\Server\f-v2\clover-project-cr\原版资源\cr-assets-png\assets\sc"
OUTDIR = os.path.join(SRC, "arena_training_out")


def stats(a):
    alpha = a[:, :, 3] if a.shape[2] == 4 else None
    rgb = a[:, :, :3].astype(np.int16)
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    op = alpha > 10 if alpha is not None else np.ones(r.shape, bool)
    blue = op & (b > r + 20) & (b > g + 5)
    grass = op & (g > r + 12) & (g > b + 20)
    pave = op & (r > b + 20) & (r > 110) & (np.abs(r - g) < 60) & (g > b)
    return op, blue, grass, pave, rgb


def main():
    files = sorted(f for f in os.listdir(OUTDIR) if f.endswith(".png"))
    print(f"{'frame':>7} {'size':>11} {'op%':>6} {'bluePx':>8} {'blueRGB':>14} {'blueY':>12} {'grassPx':>8} {'pavePx':>8}")
    for f in files:
        p = os.path.join(OUTDIR, f)
        a = np.asarray(Image.open(p).convert("RGBA"))
        h, w = a.shape[:2]
        op, blue, grass, pave, rgb = stats(a)
        n = int(blue.sum())
        brgb = "-"
        by = "-"
        if n:
            brgb = str([int(rgb[:, :, c][blue].mean()) for c in range(3)])
            ys = np.where(blue.any(axis=1))[0]
            by = f"{ys.min()}..{ys.max()}"
        no = f.replace("arena_training_sprite_", "#").replace(".png", "")
        print(f"{no:>7} {f'{w}x{h}':>11} {100.0*op.sum()/(w*h):>6.1f} {n:>8} {brgb:>14} {by:>12} {int(grass.sum()):>8} {int(pave.sum()):>8}")

    print()
    for tf in ["arena_training_tex.png", "arena_training_tex_.png"]:
        p = os.path.join(SRC, tf)
        a = np.asarray(Image.open(p).convert("RGBA"))
        h, w = a.shape[:2]
        op, blue, grass, pave, rgb = stats(a)
        n = int(blue.sum())
        print(tf, f"{w}x{h}", "bluePx=", n)
        if n:
            print("   blue mean RGB =", [int(rgb[:, :, c][blue].mean()) for c in range(3)])
            ys = np.where(blue.any(axis=1))[0]
            xs = np.where(blue.any(axis=0))[0]
            print("   blue bbox x", xs.min(), xs.max(), "y", ys.min(), ys.max())
            # 只统计最大的连通列带
            colcnt = blue.sum(axis=0)
            big = np.where(colcnt > 0.5 * h)[0]
            if len(big):
                print("   full-height blue cols:", big.min(), "..", big.max())


main()
