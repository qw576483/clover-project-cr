#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d133-verify-ours.py -- D133 判据资产：**对我们自己的实机截图做像素判据**（可复跑）
======================================================================================
为什么必须有这个脚本（不是"多余的一层"）：
  Unity 的 `Renderer.enabled` 与 `Renderer.isVisible` **都不能表达"被别的 Quad 盖住"**。
  第 4 链实测：把 6 座塔血条的 `sortingOrder` 改成 0（引擎默认 / 修复前的值）后，5 条在框内的塔条
  `enabled=True` 且 `isVisible=True`，**渲染帧里却一条都没有**（被 `SortingOrder.ArenaBase+1/+2`
  的场地底图盖死）。⇒ 「塔血条到底看不看得见」**只能用像素判**，本脚本就是那条像素判据。

输入（全部由 `.ai-tmp/test/D133-run.ps1` 的第 4 链产出）：
  .ai-tmp/screenshots/D133-order0-cam.png      6 座塔血的 sortingOrder 被强制改成 0（负控）
  .ai-tmp/screenshots/D133-order2000-cam.png   同一机位、改回 2000（正控）
  .ai-tmp/screenshots/D133-tower-hpbar-cam.png 正式产物：6 座塔满血 ⇒ 血条必须可见
  .ai-tmp/screenshots/D133-tower-ruin-cam.png  正式产物：红方左公主塔阵亡 ⇒ 塔体灭 / 废墟亮 / 无血条

判定（写死的机械阈值，出处见下）：
  · 塔血条填充色 = 引擎 `WorldHpBar.HighColor` = (0.35, 0.85, 0.30)（`UIWidgets.cs:1068`）
    ⇒ 绿掩膜 `g>150 & g>r+40 & g>b+40`（0.95 alpha 与场地混合后仍远离草地绿：草地是 r≈g 的偏黄绿）。
  · 塔条 vs 单位条**只能按宽度分**：塔条外宽 = (1.40+0.04)×1.65×60 = **142.6 px**（公主）
    / (1.80+0.04)×1.70×60 = **187.7 px**（国王）；单位条 = (1.0+0.04)×60 = **62.4 px**
    （60 px/格 = 1080 px ÷ 18 格，`ArenaView.SetupCamera` 在 1080×1920 下 orthographicSize=16）
    ⇒ 阈值 **≥100 px** 干净地只留塔条（本项目场地纯色底图，不会出现长绿条干扰）。
  · 每行允许 1 px 抖动 ⇒ 同一 y 带内取最长段；同一 y 带只算一条。

输出：`.ai-tmp/test/CR-D133-verify-ours.txt`（同时打印到 stdout）。

复跑：py tools/probes/cr-d133-verify-ours.py
"""
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

from PIL import Image
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHOTS = os.path.join(ROOT, '.ai-tmp', 'screenshots')
OUT = os.path.join(ROOT, '.ai-tmp', 'test', 'CR-D133-verify-ours.txt')

MIN_RUN_W = 40                 # px：绿色长段的**下限**（区分"条"与零散绿色像素）
BAR_ROWS = 40                  # 一条血条最多占多少行（0.20+0.035 格 × 1.65 × 60 px/格 ≈ 23 px）
EXPECTED_FPS = 60.0            # 60 px/格（1080/18，见 ArenaView.SetupCamera）

# ⛔ 不能用"宽度"当塔条的唯一判据：`WorldHpBar` 的 Fill **左对齐且按 ratio 缩放**
#   （`UIWidgets.cs:1226-1227`）⇒ 掉血的塔条会短于满血宽度（实测：下方左公主塔掉到 87% 时
#   fill 只有 120 px、掉到 ~50% 时只有 ~60 px，与单位条的 62 px 重叠）。
#   塔条与单位条**唯一稳定的差别是 y 位置**：塔位是固定几何（`GameConst`），血条 y 由
#   `worldY + 离地 offset` 定死。下面三行 = `D133-evidence.txt` 里各塔 HPQUAD 的 `screen=` 反算出的
#   "距顶 y"（1920 - screenY）：上方两座公主塔 192 / 下方两座公主塔 1332 / 下方国王塔 1475。
TOWER_BAR_Y = (192, 1332, 1475)
Y_TOL = 6                      # px


def green_mask(im):
    a = np.asarray(im.convert('RGB')).astype(int)
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    return (g > 150) & (g > r + 40) & (g > b + 40)


def long_runs(mask, min_w):
    """返回 [(y0, y1, x0, x1, w)]。

    ⚠️ 必须把同一 y 带里的**每一条**水平段都报出来，不能只报最长的那条：
    左右对称的两座公主塔血条在**同一组行**上（同一 ymid），只取最长会把其中一条吃掉
    —— 本脚本第一版就是这么错的（order=2000 只报 3 条，实际有 5 条）。
    """
    rows = [y for y in range(mask.shape[0]) if mask[y].sum() > 0]
    if not rows:
        return []
    bands, cur = [], [rows[0]]
    for y in rows[1:]:
        if y - cur[-1] <= 2:
            cur.append(y)
        else:
            bands.append(cur)
            cur = [y]
    bands.append(cur)

    out = []
    for band in bands:
        sub = mask[band[0]:band[-1] + 1]
        cols = np.where(sub.any(axis=0))[0]
        # 断段（允许 4 px 缝：数字/徽章会把条切开；本项目我们自己没有数字，留余量）
        segs, s = [], cols[0]
        p = cols[0]
        for x in cols[1:]:
            if x - p <= 4:
                p = x
            else:
                segs.append((s, p))
                s = x
                p = x
        segs.append((s, p))
        for x0, x1 in segs:
            w = x1 - x0 + 1
            if w >= min_w:
                out.append((band[0], band[-1], int(x0), int(x1), int(w)))
    return out


def is_tower_bar(y0, y1):
    ymid = (y0 + y1) / 2.0
    return min(abs(ymid - t) for t in TOWER_BAR_Y) <= Y_TOL


def report(name, path):
    lines = []
    if not os.path.isfile(path):
        lines.append('MISS %s' % path)
        return lines, []
    im = Image.open(path)
    m = green_mask(im)
    allruns = long_runs(m, MIN_RUN_W)
    bars = [b for b in allruns if is_tower_bar(b[0], b[1])]
    lines.append('%-28s %dx%d  绿色长段(≥%d px)=%d 条，其中**塔条**(y∈%s±%d)=%d 条'
                 % (os.path.basename(name), im.width, im.height, MIN_RUN_W,
                    len(allruns), list(TOWER_BAR_Y), Y_TOL, len(bars)))
    for y0, y1, x0, x1, w in allruns:
        ymid = (y0 + y1) / 2.0
        lines.append('    %s x=%d..%d (w=%d) rows %d..%d ymid=%.1f'
                     % ('TWR ' if is_tower_bar(y0, y1) else 'unit', x0, x1, w, y0, y1, ymid))
    return lines, bars


def main():
    out = []
    out.append('== cr-d133-verify-ours.py : 对**我们自己的实机截图**的像素判据 ==')
    out.append('判据：绿色长段 ≥%d px，且 ymid 落在固定塔条 y 带 %s±%d（塔条与单位条只能靠 y 分，'
               '掉血塔条会变短，见文件头）' % (MIN_RUN_W, list(TOWER_BAR_Y), Y_TOL))
    out.append('')

    # 1) 负控 vs 正控（同一链、同机位、唯一变量 = 排序层）
    l0, b0 = report('order0 (排序层被改回 0)', os.path.join(SHOTS, 'D133-order0-cam.png'))
    l2, b2 = report('order2000 (改回 SortingOrder.HpBar)', os.path.join(SHOTS, 'D133-order2000-cam.png'))
    out += l0 + [''] + l2 + ['']
    out.append('负控结论：order=0 塔条 %d 条 / order=2000 塔条 %d 条 ⇒ %s'
               % (len(b0), len(b2),
                  '排序层就是"塔血条看不见"的直接原因（受控实验成立）' if len(b0) == 0 and len(b2) > 0
                  else '⛔ 与预期不符，需要复查'))
    out.append('')

    # 2) 正式产物：满血 6 座塔
    lh, bh = report('D133-tower-hpbar-cam (满血)', os.path.join(SHOTS, 'D133-tower-hpbar-cam.png'))
    out += lh + ['']
    # 期望：5 条在框内（上方国王塔的条被顶到画外，见回报的"第 4 个缺陷"），
    #       公主条 ymid 应≈1920-588=1332（下半场）/ 1920-1728=192（上半场），
    #       国王条 ymid 应≈1920-445=1475（下方国王塔；**上方国王塔的条落在画外** screen=2005，
    #       见回报的"第 4 个缺陷" ⇒ 期望只有 5 条）。screen 读数来自 D133-evidence.txt。
    out.append('满血截图塔条数 = %d（期望 5 = 6 座塔里"上方国王塔"的条落在画外 screen=2005）⇒ %s'
               % (len(bh), '合格' if len(bh) == 5 else '⛔ 与期望不符，需复查'))
    out.append('（注：下方左公主塔此刻已被 AI 打到约 87% ⇒ 它的条宽 120 px < 满血 138 px，'
               '这正是"条按 ratio 左对齐缩放"的表现，不是缺条）')
    exp = {1332: '公主塔条(下半场, 期望 1332)', 192: '公主塔条(上半场, 期望 192)',
           1475: '国王塔条(下方, 期望 1475)'}
    near = []
    for y0, y1, x0, x1, w in bh:
        ymid = (y0 + y1) / 2.0
        tag = min(exp, key=lambda k: abs(k - ymid))
        near.append('%s(实测 ymid=%.1f, 期望 %d)' % (exp[tag], ymid, tag))
    out.append('满血截图的塔条 y 与 `D133-evidence.txt` 的 HPQUAD `screen=` 对照：')
    for s in near:
        out.append('    %s' % s)
    out.append('')

    # 3) 正式产物：阵亡废墟（红方左公主塔 world(-5.5,+9.5) ⇒ 屏幕 x=210 / 条 y 上沿 1332，
    #    同机位的存活态见 D133-order2000-cam.png 同一处）
    lr, br = report('D133-tower-ruin-cam (阵亡)', os.path.join(SHOTS, 'D133-tower-ruin-cam.png'))
    out += lr + ['']
    # 阵亡塔 = 红方左公主塔 world(-5.5, +9.5) ⇒ 它若有条，必然落在 x 中心≈210、y≈192
    # （它**上方**那条 y≈192 的带里只应有右公主塔那一根；下方左公主塔在 y≈1332，不能算进来）
    hit = [b for b in br if abs((b[2] + b[3]) / 2.0 - 210) < 90
           and abs((b[0] + b[1]) / 2.0 - 192) <= Y_TOL]
    out.append('阵亡塔位置(x≈210, y≈192)上的塔条数 = %d ⇒ %s'
               % (len(hit), '合格（阵亡后不留血条）' if not hit else '⛔ 阵亡塔上仍有血条，需复查'))
    out.append('本截图塔条数 = %d（期望 4 = 满血态 5 条里去掉被注入阵亡的那一座）⇒ %s'
               % (len(br), '合格' if len(br) == 4 else '⛔ 与期望不符，需复查'))

    txt = '\n'.join(out) + '\n'
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write(txt)
    print(txt)
    print('TXT -> %s' % OUT)
    return 0


if __name__ == '__main__':
    sys.exit(main())
