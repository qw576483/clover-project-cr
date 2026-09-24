#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130c 取景定量：在**同一坐标口径**下量「战斗区（场地）纵向范围」与「出牌区（手牌栏）上沿」。

用途（用户第 1 条「出牌区挤压战斗区，我记得好像是分离的」）：
  ⛔ 不许盲改相机/取景常量。先把**原版**与**本实现**量到同一张表上，
     才有资格动 `ArenaView` 的取景。

两把尺子（都在 1080x1920，若图不是该尺寸则先报告实际尺寸）：
  A. 草地行剖面 —— 每行在给定 x 窗口内的「草色像素占比」。
     场地（可玩草地）的上下沿 = 该占比持续 > TH_GRASS 的首/末行。
  B. 暗带剖面 —— 每行的平均亮度（Y = 0.299R+0.587G+0.114B）。
     出牌区（手牌栏）是暗底面板 ⇒ 上沿 = 从下往上第一段「持续很暗」的顶。

只读脚本：不改任何产品代码。
用法： python lead-d130c-frame.py <img> [--x0 0] [--x1 1080] [--tag NAME]
"""
import os
import sys

from PIL import Image

TH_GRASS = 0.30   # 草色占比阈值
TH_DARK = 95.0    # 暗带亮度阈值
MIN_RUN = 12      # 连续行数下限（防噪点）


def is_grass(r, g, b):
    return (g > 90) and (g > r * 1.06) and (g > b * 1.10)


def lum(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def profile(path, x0, x1, tag):
    im = Image.open(path).convert("RGB")
    W, H = im.size
    px = im.load()
    x0 = max(0, min(x0, W - 1))
    x1 = max(x0 + 1, min(x1, W))
    grass = []
    dark = []
    for y in range(H):
        ng = 0
        sl = 0.0
        n = 0
        for x in range(x0, x1):
            r, g, b = px[x, y]
            if is_grass(r, g, b):
                ng += 1
            sl += lum(r, g, b)
            n += 1
        grass.append(ng / float(n))
        dark.append(sl / float(n))

    # A. 草地纵向范围（允许 MIN_RUN 行的空洞，取最长的连续段）
    runs = []
    s = None
    for y in range(H):
        if grass[y] > TH_GRASS:
            if s is None:
                s = y
        else:
            if s is not None:
                runs.append((s, y - 1))
                s = None
    if s is not None:
        runs.append((s, H - 1))
    runs = [r for r in runs if (r[1] - r[0] + 1) >= MIN_RUN]
    runs.sort(key=lambda r: r[1] - r[0], reverse=True)

    # B. 出牌区（暗带）上沿：从底部往上找第一段持续暗的行
    bar_top = None
    y = H - 1
    if dark[y] < TH_DARK:
        # 最底一行通常是圣水条（亮），跳过
        while y > 0 and dark[y] >= TH_DARK * 0.85:
            y -= 1
    # 现在 y 处应是暗带；继续上探其顶端
    while y > 0 and dark[y] < TH_DARK:
        y -= 1
    # 回退到暗带真正的第一行
    while y < H - 1 and dark[y] >= TH_DARK:
        y += 1
    bar_top = y

    print("=" * 74)
    print("图        : %s" % path)
    print("标签      : %s" % tag)
    print("尺寸      : %dx%d" % (W, H))
    print("x 窗口    : [%d, %d)" % (x0, x1))
    print("-" * 74)
    if runs:
        top, bot = runs[0]
        print("草地主段  : rows %d..%d  (高 %d px)" % (top, bot, bot - top + 1))
        if len(runs) > 1:
            others = ", ".join("%d..%d(%d)" % (a, b, b - a + 1) for a, b in runs[1:5])
            print("草地次段  : %s" % others)
        print("  ⇒ 32 格定标 = %.3f px/格" % ((bot - top + 1) / 32.0))
        print("  ⇒ 场地底沿 = row %d（自上而下）" % bot)
    else:
        print("草地主段  : 无（阈值 %.2f 下无 >= %d 行的连续段）" % (TH_GRASS, MIN_RUN))
    print("出牌区上沿: row %d（自上而下）" % bar_top)
    if runs:
        print("重叠量    : %d px = %.2f 格（草地主段底沿 − 出牌区上沿）"
              % (runs[0][1] - bar_top + 1, (runs[0][1] - bar_top + 1) / 60.0))
    print("-" * 74)
    print("行剖面抽样（每 %d 行）：row : grass%% : lum" % max(1, H // 24))
    step = max(1, H // 24)
    for y in range(0, H, step):
        print("  %4d : %5.2f : %6.1f%s"
              % (y, grass[y], dark[y],
                 "   <-bar_top" if y == bar_top else ("")))
    return {"size": (W, H), "grass_runs": runs, "bar_top": bar_top}


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[1]
    x0, x1 = 0, 99999
    tag = os.path.basename(path)
    i = 2
    while i < len(argv):
        if argv[i] == "--x0":
            x0 = int(argv[i + 1]); i += 2
        elif argv[i] == "--x1":
            x1 = int(argv[i + 1]); i += 2
        elif argv[i] == "--tag":
            tag = argv[i + 1]; i += 2
        else:
            print("unknown arg: %s" % argv[i]); return 2
    profile(path, x0, x1, tag)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
