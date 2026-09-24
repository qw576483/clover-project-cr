# -*- coding: utf-8 -*-
# direct-fix: D146 原版 UI 复刻 —— 原版截图分带量取（工具，非交付物）
"""ref07-bands.py -- 用「行/列**中位色差**」找原版界面截图里的横向 / 纵向分带边界。

为什么用中位数而不是均值（这是它比已有量法强的地方）
--------------------------------------------------
`策划/参考图/几何量取.md` §2 的 **C7** 记着一条量不到的项：

> 07 顶部 Tab 带（Decks/Collection）与卡组槽位行（1..5 + 交换/复制）的**精确**上下边界
> —— 这两条带与其下方卡池底**同色系深蓝**，边界不锐利；`rows` 扫描给的是宽区间
> （y≈28..115 / y≈158..330），边界误差 >±10px

根因 = 均值会被**带内的卡面内容**（高饱和色块）拉走。中位数只取"这一行最典型的颜色"，
对局部内容鲁棒 ⇒ 当整行**底色**换掉时中位数才跳，于是边界就出来了。
这正是 C7 自己给的消除条件「用亮描边做边检测」的等价做法（底色切换 = 最稳的那条通道）。

复跑
----
  python tools/probes/ref07-bands.py rows                        # 横向分带（默认 07 图）
  python tools/probes/ref07-bands.py cols 158 330                # 指定 y 带内的纵向分界
  python tools/probes/ref07-bands.py bright 340 690              # 每行亮像素的左右边界
  python tools/probes/ref07-bands.py sample 0 0 1242 28          # 区域中位色
  python tools/probes/ref07-bands.py bands                       # 分界 + 每个区间的中位色（一页看全）

输出：控制台（人看）。⛔ 本脚本不写任何文件（读数由人抄进 `策划/参考图/几何量取.md`）。
"""

import os
import sys
import argparse

import numpy as np
from PIL import Image

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DEFAULT_IMG = os.path.join(ROOT, '策划', '参考图', '07_卡组编辑_1242x2208.jpg')
K1080 = 1080.0 / 1242.0


def load(path):
    return np.asarray(Image.open(path).convert('RGB')).astype(np.float32)


def _pick_extrema(d, minsep, thr):
    """d = 相邻行/列的中位色差序列。返回 [(值, 下标), ...]（按下标升序，已做最小间隔去重）。"""
    cand = []
    for i in range(1, len(d) - 1):
        if d[i] >= d[i - 1] and d[i] >= d[i + 1] and d[i] > thr:
            cand.append((float(d[i]), i))
    cand.sort(reverse=True)
    picked = []
    for v, i in cand:
        if all(abs(i - j) >= minsep for _, j in picked):
            picked.append((v, i))
    picked.sort(key=lambda t: t[1])
    return picked


def rgb(t):
    return '(%3d,%3d,%3d)' % (t[0], t[1], t[2])


def cmd_rows(a, args):
    sub = a[:, args.x0:args.x1, :] if (args.x0 or args.x1 < 10 ** 9) else a
    med = np.median(sub, axis=1)
    d = np.abs(np.diff(med, axis=0)).sum(axis=1)
    picked = _pick_extrema(d, args.minsep, args.thr)
    scope = ('x∈[%d,%d)' % (args.x0, min(args.x1, a.shape[1]))) if (args.x0 or args.x1 < 10 ** 9) else '全宽'
    print('=== 横向分带（%s）：%d 条候选边界（minsep=%d thr=%.1f）===' % (scope, len(picked), args.minsep, args.thr))
    print('%6s %8s   %-16s %-16s %s' % ('y@1242', 'd', '上一行中位色', '下一行中位色', 'y@1080'))
    for v, i in picked[:args.top]:
        print('%6d %8.1f   %-16s %-16s %7.1f' % (i, v, rgb(med[i]), rgb(med[i + 1]), i * K1080))


def _col_profile(a, y0, y1):
    """在 y∈[y0,y1) 内按列取中位色。"""
    return np.median(a[y0:y1, :, :], axis=0)


def cmd_cols(a, args):
    band = a[args.y0:args.y1, :, :]
    med = _col_profile(a, args.y0, args.y1)
    d = np.abs(np.diff(med, axis=0)).sum(axis=1)
    picked = _pick_extrema(d, args.minsep, args.thr)
    print('=== 纵向分界（y 带 = [%d,%d)，%d 行）=== 共 %d 条候选'
          % (args.y0, args.y1, band.shape[0], len(picked)))
    print('%6s %8s   %-16s %-16s %s' % ('x@1242', 'd', '左列中位色', '右列中位色', 'x@1080'))
    for v, i in picked[:args.top]:
        print('%6d %8.1f   %-16s %-16s %7.1f' % (i, v, rgb(med[i]), rgb(med[i + 1]), i * K1080))


def cmd_bright(a, args):
    """每行的"亮像素"左右边界：亮 = max(r,g,b) > thr（卡面/亮边框通道）。"""
    sub = a[args.y0:args.y1, :, :]
    mx = sub.max(axis=2)
    mask = mx > args.thr
    print('=== 亮像素左右边界（y=[%d,%d) thr=%.0f）===' % (args.y0, args.y1, args.thr))
    print('%6s %6s %6s %6s   %s' % ('y@1242', 'x左', 'x右', '宽', 'y@1080'))
    for i in range(0, sub.shape[0], max(1, args.step)):
        row = mask[i]
        idx = np.nonzero(row)[0]
        y = args.y0 + i
        if len(idx) == 0:
            print('%6d %6s %6s %6s' % (y, '—', '—', '—'))
        else:
            print('%6d %6d %6d %6d' % (y, idx[0], idx[-1], idx[-1] - idx[0] + 1))


def cmd_sample(a, args):
    sub = a[args.y0:args.y1, :, :][:, args.x0:args.x1, :]
    flat = sub.reshape(-1, 3)
    print('=== 区域中位色 x∈[%d,%d) y∈[%d,%d) (%d px) ===' % (args.x0, args.x1, args.y0, args.y1, len(flat)))
    print('  中位 %s   均值 %s' % (rgb(np.median(flat, axis=0)), rgb(flat.mean(axis=0))))


def cmd_bands(a, args):
    """分界 + 逐区间的中位色（一次看全，用来填台账）。"""
    med = np.median(a, axis=1)
    d = np.abs(np.diff(med, axis=0)).sum(axis=1)
    picked = _pick_extrema(d, args.minsep, args.thr)
    ys = [0] + [i for _, i in picked] + [a.shape[0]]
    print('=== 07 图纵向分带（%d 段）—— 每段的中位色 + 折算 @1080 ===' % (len(ys) - 1))
    print('%14s %10s %10s   %-16s %s' % ('y@1242 区间', 'h@1242', 'h@1080', '该段中位色', '段内最亮'))
    for j in range(len(ys) - 1):
        y0, y1 = ys[j], ys[j + 1]
        if y1 <= y0:
            continue
        seg = a[y0:y1]
        col = np.median(seg.reshape(-1, 3), axis=0)
        bright = int(seg.max(axis=2).max())
        print('%6d .. %-6d %10d %10.1f   %-16s %d' %
              (y0, y1, y1 - y0, (y1 - y0) * K1080, rgb(col), bright))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('mode', choices=['rows', 'cols', 'bright', 'sample', 'bands'])
    ap.add_argument('--img', default=DEFAULT_IMG)
    ap.add_argument('--top', type=int, default=40, help='最多列几条候选')
    ap.add_argument('--minsep', type=int, default=6, help='候选之间的最小间隔（px）')
    ap.add_argument('--thr', type=float, default=3.0, help='色差阈值 / 亮像素阈值（bright 模式）')
    ap.add_argument('--step', type=int, default=10, help='bright 模式的采样步长')
    ap.add_argument('--y0', type=int, default=0)
    ap.add_argument('--y1', type=int, default=0)
    ap.add_argument('--x0', type=int, default=0)
    ap.add_argument('--x1', type=int, default=0)
    args = ap.parse_args()
    args.y1 = args.y1 or 10 ** 9
    args.x1 = args.x1 or 10 ** 9

    if not os.path.isfile(args.img):
        print('!! 图不存在：%s' % args.img)
        return 2
    a = load(args.img)
    print('图：%s  %dx%d' % (os.path.basename(args.img), a.shape[1], a.shape[0]))
    print()
    if args.mode == 'rows':
        cmd_rows(a, args)
    elif args.mode == 'cols':
        cmd_cols(a, args)
    elif args.mode == 'bright':
        cmd_bright(a, args)
    elif args.mode == 'sample':
        cmd_sample(a, args)
    elif args.mode == 'bands':
        cmd_bands(a, args)
    return 0


if __name__ == '__main__':
    sys.exit(main())
