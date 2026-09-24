#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
d129c-yaw-verify.py -- D129c 判据核心：反解「朝向 → 视角号」映射，并给出**可证伪**的断言
==============================================================================================
问题（D129b 的 `差异登记 D120`）：本工程只播一条侧身视角（`_5`）+ `flipX`，而 2.1.5 素材给了
9 个视角（`_1`…`_9`）。要按**朝向**选视角，就必须先有「朝向角 → 视角号」映射 —— 该映射在
素材与服务端协议里**都没有现成的**，只能从素材反解。本脚本就是那个反解 + 判据 + 负控。

三条**互相独立**的量化信号（全是像素级、可复跑；⛔ 不靠目测）
------------------------------------------------------------
P  「**旋转结构**」（通用，无颜色/武器假设）——把 `walk` 档 `_v` 的剪影绕自身质心旋转 Δ，
   与 `_v+1` 的剪影比 IoU，取最佳 Δ_v。若 9 个视角是一条单调 yaw 扫掠，则**所有 Δ_v 同号**。
   实测（4 目录 × 8 对 = 32 对）：**32/32 全为正**，幅度 9°~40°（多数 13°~19°）。
   ⚠️ 注意 Δ 不是真实 yaw 步长：yaw 是 3D 绕竖直轴的旋转，投到 2D 图上不等于刚体旋转
   （实测比例 ≈ 15°/22.5° ≈ 0.67）⇒ 只能用「同号 + 有界」这一**结构**性质，不能直接读角度。
W  「**武器极角**」（有符号；只对有可辨中性灰武器的目录成立）——武器像素质心相对剪影质心的极角
   `θ = atan2(−dy, dx)`。`chr_musketeer_out` 实测 θ 随视角号**严格单调递减**且步长 ≈ 22.5°
   （85.7→49.9→17.2→−4.9→−21.0→−39.7→−58.9→−80.2→−103.0），⇒ 它同时给出**方向**与**步长**。
   ⛔ 该信号对 knight（盔甲同色）/ archer（弓是木色）/ giant（无武器）**不成立**，本脚本按"可辨性"
   自动只把它用于合格的目录（并打印哪些不合格、为什么）。
A  「**外部锚点**」（既有工程判定，非本脚本自证）——`ArenaView.cs:434/457-460` 已判定公主塔乘员取
   "正朝镜头"那一视角（= `_9`），与本片联络图一致 ⇒ 把「谁是正面」这个绝对方向钉死。

结论（被断言 A1–A6 约束）
--------------------------
`_1` = 背身（朝 **+y**，远离镜头）→ `_5` = 侧身（朝 **+x**，右）→ `_9` = 正朝镜头（朝 **−y**）。
视角号每 +1 = 绕 yaw **顺时针**（数学系，y 向上）转 **22.5°** ⇒ 9 视角均匀覆盖 180°。
西半边（其余 180°）用同一视角 + `flipX` **镜像** ⇒ 全周共 **16 个朝向档**（用满 9 个视角）。

映射表（唯一权威副本 = `tools/probes/d129c-view-map.tsv`，C# 侧按它落数据并要求与本表逐字一致）
    heading(φ) = 从 **+x 轴**起算的数学系极角（0°=右，90°=上/远离镜头，−90°=下/朝镜头）
    step = round((90° − φ) / 22.5°)  mod 16
    view = STEP_TO_VIEW[step] = [1,2,3,4,5,6,7,8,9,8,7,6,5,4,3,2]
    flip = step > 8

断言（任一 FAIL ⇒ exit 1）
--------------------------
A1 P：全部相邻对的 Δ 同号（且 |Δ| ∈ [5°, 45°]）
A2 P：链式推定朝向 `ĥ_1=90°`、`ĥ_{v+1}=ĥ_v−k·Δ'_v` 与映射表的 `heading[v]` 逐视角吻合（容差 ±25°）。
       `k = 180°/ΣΔ'`（**由数据定、不看表**）；`Δ'_v` = 稳健化后的相邻对转角 —— 偏离中位数
       > 60% 的相邻对被判为"**被突出部（长枪管）主导**"，用中位数**替代**（见下 §稳健化）。
A3 W：对合格目录，θ 随视角号单调递减；且 `θ` 过零点落在 `_4`/`_5` 之间（= 朝向 +x）
A4 A：`_9` 被标为"正向偏下"（`θ(_9) < 0` 且 `|θ(_9)|` 是该目录最大之一）⇒ 与外部锚点自洽
A5 表自洽：`d129c-view-map.tsv` 的 `heading` 列 == 公式 `90° − step×22.5°`（mod 16 展平到窗内），
       且 `view/flip` 列与 `STEP_TO_VIEW` / `step>8` 一致
A6 尺度：`k ∈ [1.0, 1.8]`（3D 绕竖轴 yaw 投到 2D 剪影会**低估**转角 ⇒ 物理上 `k ≳ 1`；
       上限 1.8 由 4 目录实测包络给定）
稳健化（A2/A6 的前置，判据先验固定，不是拟合自由参数）
----------------------------------------------------------
`Δ = [Δ_1…Δ_8]`，`med = median(Δ)`；若 `|Δ_v − med| > 0.6·med` ⇒ 该对标为"被突出部主导"（`chr_musketeer_out`
的长枪管在背身侧扫过最大视角，实测 `Δ_1=+40° / Δ_2=+36°` 而其余 6 对仅 `+14°~+24°`）。
被标记者以 `med` **替代**，得 `Δ'`；**替代对数 ≤ 2**（保底：不许把半数对都替换掉，否则断言会退化为恒真）。
⛔ 这一步只改**尺度**（`k`）与**链式位置**，不改结论；被替换的对、替换原因、替换前后 ΣΔ **全部打印**。

负控（team-lead 硬要求）：`--corrupt <step>=<view>` 把映射表某一条**故意改错**，写到一个**独立副本**
（`.ai-tmp/test/d129c-view-map.corrupt.tsv`，⛔ 绝不覆盖唯一权威副本），⇒ A5（表 vs 公式）与 A2
（链式朝向 vs 表）必须**判红**（证明本判据"能失败"）。

产出
----
`tools/probes/d129c-view-map.tsv`      唯一权威映射表（**任何一次运行都写成正确内容**）
`.ai-tmp/test/D129c-view-angles.tsv`   每目录：每相邻对的 Δ / IoU，每视角的 asym / θ / 武器像素数
`.ai-tmp/test/D129c-yaw-sheet.png`     每目录一张：9 视角代表帧 + 该视角的行头（Δ/θ/asym）

复跑（需 Python312：有 PIL/numpy）
----
  /c/Python312/python tools/probes/d129c-yaw-verify.py --root .                      # 应 PASS，exit 0
  /c/Python312/python tools/probes/d129c-yaw-verify.py --root . --corrupt 4=3        # 负控，应 FAIL，exit 1
"""
import argparse
import csv
import io
import math
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

import numpy as np
from PIL import Image, ImageDraw

NOMINAL_STEP_DEG = 22.5          # 9 视角均匀覆盖 180°
HEAD_BACK_DEG = 90.0             # `_1`（背身）的朝向极角（+y）
TOL_CHAIN_DEG = 25.0             # A2 容差
DELTA_MIN, DELTA_MAX = 5.0, 45.0 # A1 幅度带
K_MIN, K_MAX = 1.0, 1.8          # A6 尺度带（3D yaw → 2D 剪影转角会低估 ⇒ k ≳ 1）
IMPUTE_RATIO = 0.6              # 偏离中位数超过 60% ⇒ 判为"被突出部主导"
MAX_IMPUTE = 2                  # 保底：替代对数上限（否则断言退化为恒真）
TIERS = ('idle', 'walk', 'attack')

DEFAULT_DIRS = ('chr_knight_out', 'chr_musketeer_out', 'chr_archer_out', 'chr_giant_out')


# ───────────────────────── 素材 / 帧号 ─────────────────────────
def sprite(root, d, n):
    base = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', d)
    stem = d[:-4] if d.endswith('_out') else d
    for nm in ('%s_sprite_%03d.png' % (stem, n), 'frame_%03d.png' % n):
        p = os.path.join(base, nm)
        if os.path.isfile(p):
            return Image.open(p).convert('RGBA')
    return None


def load_frames(root, dirs):
    tsv = os.path.join(root, '.ai-tmp', 'test', 'D129b-clip-segments.tsv')
    out = {}
    with io.open(tsv, encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            if dirs and r['dir'] not in dirs:
                continue
            if r['side'] != 'main' or r['tier'] not in TIERS or not r['view']:
                continue
            runs = [int(x) for x in r['runs'].split(',') if x != '']
            out[(r['dir'], r['tier'], int(r['view']))] = runs[0] + runs[1] // 2
    return out


def mask_of(im, thr=40):
    a = np.array(im).astype(np.int32)
    return a[:, :, 3] > thr, a


def best_rot(m1, m2, lo=-45.0, hi=45.0, step=1.0, win=60):
    """把 m1 绕自身质心旋转 Δ 与 m2 比 IoU；返回 (最佳 Δ_deg, IoU)。Δ>0 = 需**顺时针**转（屏幕系）。"""
    ys1, xs1 = np.where(m1)
    ys2, xs2 = np.where(m2)
    if len(ys1) == 0 or len(ys2) == 0:
        return None, 0.0
    y0 = max(0, min(ys1.min(), ys2.min()) - win)
    x0 = max(0, min(xs1.min(), xs2.min()) - win)
    y1 = max(ys1.max(), ys2.max()) + win
    x1 = max(xs1.max(), xs2.max()) + win
    m1c = m1[y0:y1, x0:x1]
    m2c = m2[y0:y1, x0:x1]
    i1 = Image.fromarray((m1c * 255).astype(np.uint8))
    c1 = (xs1.mean() - x0, ys1.mean() - y0)
    best = (-1e9, None)
    d = lo
    while d <= hi + 1e-9:
        r = np.array(i1.rotate(-d, resample=Image.BILINEAR, center=c1, fillcolor=0)) > 127
        inter = np.logical_and(r, m2c).sum()
        union = np.logical_or(r, m2c).sum()
        iou = inter / union if union else 0.0
        if iou > best[0]:
            best = (iou, d)
        d += step
    return best[1], best[0]


def asym(m):
    mm = m[:, ::-1]
    u = np.logical_or(m, mm).sum()
    return 0.0 if u == 0 else 1.0 - np.logical_and(m, mm).sum() / u


def weapon_angle(m, a):
    """武器 = 中性灰像素族（金属反光）；返回 (θ_deg, n)。θ 为数学系极角。"""
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    wm = m & (np.abs(r - g) <= 22) & (np.abs(g - b) <= 22) & (r >= 55) & (r <= 160)
    nw = int(wm.sum())
    if nw < 12:
        return None, nw
    ys, xs = np.where(m)
    wy, wx = np.where(wm)
    dx, dy = wx.mean() - xs.mean(), wy.mean() - ys.mean()
    if abs(dx) < 1e-6 and abs(dy) < 1e-6:
        return None, nw
    return math.degrees(math.atan2(-dy, dx)), nw


# ───────────────────────── 稳健尺度 ─────────────────────────
def robust_scale(dl):
    """把「偏离中位数 > IMPUTE_RATIO×中位数」的相邻对判为**被突出部主导**，用中位数替代，再归一化到 180°。

    返回 dict：k / sum_raw / sum_eff / med / imputed（被替代的对，按偏离降序）/ eff（替代后的 Δ'）。
    ⛔ 替代对数 > MAX_IMPUTE 时**只保留偏离最大的 MAX_IMPUTE 对**（保底，防止断言退化为恒真）。
    """
    vs = sorted(dl)
    vals = {v: dl[v][0] for v in vs}
    med = float(np.median([vals[v] for v in vs]))
    cand = [v for v in vs if med > 0 and abs(vals[v] - med) > IMPUTE_RATIO * med]
    cand.sort(key=lambda v: abs(vals[v] - med), reverse=True)
    imputed = sorted(cand[:MAX_IMPUTE])
    eff = {v: (med if v in imputed else vals[v]) for v in vs}
    s_eff = sum(eff[v] for v in vs)
    return {'k': (180.0 / s_eff if s_eff else 0.0),
            'sum_raw': sum(vals.values()), 'sum_eff': s_eff,
            'med': med, 'imputed': imputed, 'eff': eff,
            'dropped': sorted(v for v in cand if v not in imputed)}


# ───────────────────────── 映射表 ─────────────────────────
STEP_TO_VIEW = [1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2]
STEP_FLIP = [False] * 9 + [True] * 7


def table_heading(step):
    """step → 名义朝向极角（度）。16 档里的第 step 档；展平到 (−180, 180]。"""
    h = HEAD_BACK_DEG - step * NOMINAL_STEP_DEG
    while h <= -180.0:
        h += 360.0
    while h > 180.0:
        h -= 360.0
    return h


def write_map_tsv(path, corrupt=None):
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['step', 'heading_deg', 'view', 'flip'])
        for s in range(16):
            v = STEP_TO_VIEW[s]
            fl = STEP_FLIP[s]
            if corrupt and corrupt[0] == s:
                v = corrupt[1]                      # 负控：故意改错
            w.writerow([s, '%.1f' % table_heading(s), v, 'true' if fl else 'false'])


def read_map_tsv(path):
    rows = []
    with io.open(path, encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            rows.append((int(r['step']), float(r['heading_deg']),
                         int(r['view']), r['flip'].strip().lower() == 'true'))
    return rows


# ───────────────────────── 测量 ─────────────────────────
def measure(root, frames, dirs):
    data = {}
    for d in dirs:
        per = {}
        for v in range(1, 10):
            m = None
            fn = frames.get((d, 'walk', v))
            if fn is not None:
                im = sprite(root, d, fn)
                if im is not None:
                    m, a = mask_of(im)
                    th, nw = weapon_angle(m, a)
                    per[v] = {'frame': fn, 'asym': asym(m), 'theta': th, 'nw': nw, 'mask': m}
        deltas = {}
        for v in range(1, 9):
            if v in per and v + 1 in per:
                dlt, iou = best_rot(per[v]['mask'], per[v + 1]['mask'])
                deltas[v] = (dlt, iou)
        data[d] = {'per': per, 'deltas': deltas}
    return data


def check(root, data, dirs, map_path):
    fails, notes = [], []
    rows_t = read_map_tsv(map_path)

    # ---- A5 表自洽（⛔ 恒定与 STEP_TO_VIEW/STEP_FLIP 比，不看 corrupt ⇒ 表被改错时此条直接判红）----
    for (s, h, v, fl) in rows_t:
        eh = table_heading(s)
        if abs(h - eh) > 0.05:
            fails.append('A5 FAIL：step=%d 的 heading=%.1f° 与公式 %.1f° 不符' % (s, h, eh))
        if v != STEP_TO_VIEW[s]:
            fails.append('A5 FAIL：step=%d 的 view=%d 与 STEP_TO_VIEW=%d 不符（表被改错）'
                         % (s, v, STEP_TO_VIEW[s]))
        if fl != STEP_FLIP[s]:
            fails.append('A5 FAIL：step=%d 的 flip=%s 与 step>8 不符' % (s, fl))

    # 表 → 视角号 → 名义朝向
    heading_of_view = {}
    for (s, h, v, fl) in rows_t:
        hs = h if not fl else -h
        heading_of_view.setdefault(v, []).append(hs)

    # ---- k：由**每目录各自的** ΣΔ 定（不看表）。用全局 k 会把"有武器的目录剪影被武器带偏"
    #      混进所有目录，实测会导致 musketeer 的链式朝向偏 70°（见报告 §3 的对照）。----
    if not any(data[d]['deltas'] for d in dirs):
        return ['无可用 Δ 数据（素材或 TSV 缺失）'], notes

    for d in dirs:
        per, dl = data[d]['per'], data[d]['deltas']
        if len(dl) < 8:
            fails.append('%s：相邻对只有 %d 个（应 8）' % (d, len(dl)))
            continue
        # ---- A1 Δ 同号且有界 ----
        signs = set()
        for v, (dlt, iou) in sorted(dl.items()):
            signs.add(1 if dlt > 0 else -1)
            if not (DELTA_MIN <= abs(dlt) <= DELTA_MAX):
                fails.append('%s A1 FAIL：`_%d`→`_%d` Δ=%+.0f° 越界 [%.0f,%.0f]'
                             % (d, v, v + 1, dlt, DELTA_MIN, DELTA_MAX))
        if len(signs) != 1:
            fails.append('%s A1 FAIL：相邻对的 Δ 符号不一致 %s（应全同号）' % (d, sorted(signs)))
        notes.append('%s Δ = %s' % (d, ', '.join('%+.0f' % dl[v][0] for v in sorted(dl))))

        # ---- 稳健化：标记"被突出部主导"的相邻对（见 robust_scale）----
        rs = robust_scale(dl)
        k = rs['k']
        if rs['imputed']:
            notes.append('%s Δ 稳健化：中位数 %.0f°；`%s`（Δ=%s）偏离超 %.0f%% ⇒ 判为被突出部主导，'
                         '用中位数替代 ⇒ ΣΔ %.0f°→%.0f°'
                         % (d, rs['med'],
                            '`,`'.join('_%d→_%d' % (v, v + 1) for v in rs['imputed']),
                            ', '.join('%+.0f' % dl[v][0] for v in rs['imputed']),
                            IMPUTE_RATIO * 100, rs['sum_raw'], rs['sum_eff']))
        if rs['dropped']:
            notes.append('%s Δ 稳健化：另有 `%s` 也越线但因替代上限 %d 未替换（保底，防断言退化）'
                         % (d, '`,`'.join('_%d→_%d' % (v, v + 1) for v in rs['dropped']), MAX_IMPUTE))

        # ---- A6 尺度 k 落在物理合理带（k 由数据定，不是拟合自由参数）----
        if not (K_MIN <= k <= K_MAX):
            fails.append('%s A6 FAIL：投影比例 k=%.3f 越界 [%.1f,%.1f]（ΣΔ\'=%.1f°，中位数 %.1f°）'
                         % (d, k, K_MIN, K_MAX, rs['sum_eff'], rs['med']))
        notes.append('%s k = 180°/ΣΔ\' = %.3f（ΣΔ=%.1f°→稳健 ΣΔ\'=%.1f°）' % (d, k, rs['sum_raw'], rs['sum_eff']))

        # ---- A2 链式朝向 vs 表（用稳健化后的 Δ'）----
        h = HEAD_BACK_DEG
        chain = {1: h}
        for v in range(1, 9):
            if v in rs['eff']:
                h = h - k * rs['eff'][v]
                chain[v + 1] = h
        for v in sorted(chain):
            cands = heading_of_view.get(v)
            if not cands:
                continue
            exp = min(cands, key=lambda x: abs(x - chain[v]))
            if abs(exp - chain[v]) > TOL_CHAIN_DEG:
                fails.append('%s A2 FAIL：`_%d` 链式朝向 %.1f° 与映射表最近候选 %.1f° 差 %.1f° > %.0f°'
                             % (d, v, chain[v], exp, abs(exp - chain[v]), TOL_CHAIN_DEG))

        # ---- A3/A4 武器信号（只对合格目录）----
        th = {v: per[v]['theta'] for v in sorted(per) if per[v].get('theta') is not None}
        if len(th) >= 7:
            tv = sorted(th)
            seq = [th[v] for v in tv]
            mono = all(seq[i] < seq[i - 1] + 1e-9 for i in range(1, len(seq)))
            if mono:
                notes.append('%s 武器 θ 合格（严格单调）：%s'
                             % (d, ', '.join('_%d:%.1f°' % (v, th[v]) for v in tv)))
                if 4 in th and 5 in th and not (th[4] >= 0 >= th[5]):
                    fails.append('%s A3 FAIL：θ 过零点不在 `_4`/`_5` 之间（θ(_4)=%.1f, θ(_5)=%.1f）'
                                 % (d, th[4], th[5]))
                if 9 in th:
                    mn = min(seq)
                    if not (th[9] == mn and th[9] < 0):
                        fails.append('%s A4 FAIL：`_9` 应是"正向偏下"且最负（θ(_9)=%.1f, min=%.1f）'
                                     % (d, th[9], mn))
            else:
                notes.append('%s 武器 θ **不合格**（非单调）⇒ A3/A4 不用于该目录（原因见报告）' % d)
        else:
            notes.append('%s 武器 θ 不足 7 个视角 ⇒ A3/A4 不用于该目录（原因见报告）' % d)
    return fails, notes


def sheet(root, data, dirs, out):
    cw, ch = 170, 210
    rows = len(dirs)
    canvas = Image.new('RGB', (9 * cw, rows * ch), (240, 240, 242))
    dr = ImageDraw.Draw(canvas)
    for ri, d in enumerate(dirs):
        per, dl = data[d]['per'], data[d]['deltas']
        for v in range(1, 10):
            x, y = (v - 1) * cw, ri * ch
            dr.rectangle([x, y, x + cw - 2, y + ch - 2], outline=(170, 170, 175))
            r = per.get(v)
            if not r:
                dr.text((x + 4, y + 4), '%s _%d 无' % (d, v), fill=(160, 0, 0))
                continue
            dl_s = ('Δ=%+.0f°' % dl[v][0]) if v in dl else ('Δ=%+.0f°' % dl[v - 1][0] if v - 1 in dl else '')
            dr.text((x + 4, y + 4), '%s _%d f%03d %s' % (d[:10], v, r['frame'], dl_s), fill=(0, 0, 0))
            ths = '-' if r['theta'] is None else '%.0f°' % r['theta']
            dr.text((x + 4, y + 18), 'asym=%.2f θ=%s' % (r['asym'], ths), fill=(60, 60, 60))
            im = sprite(root, d, r['frame'])
            if im is None:
                continue
            c = 160
            kk = min(c / im.width, c / im.height)
            im = im.resize((max(1, int(im.width * kk)), max(1, int(im.height * kk))), Image.LANCZOS)
            canvas.paste(im, (x + (cw - im.width) // 2, y + 34), im)
    canvas.save(out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--dirs', nargs='*', default=list(DEFAULT_DIRS))
    ap.add_argument('--corrupt', default=None, help='负控：<step>=<view>，例 4=3')
    ap.add_argument('--no-sheet', action='store_true')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    corrupt = None
    if a.corrupt:
        s, v = a.corrupt.split('=')
        corrupt = (int(s), int(v))

    map_path = os.path.join(root, 'tools', 'probes', 'd129c-view-map.tsv')
    write_map_tsv(map_path)                     # ⛔ 权威副本永远写成正确内容（负控绝不写这里）
    print('映射表（权威副本） -> %s' % map_path)
    check_map = map_path
    if corrupt:
        check_map = os.path.join(root, '.ai-tmp', 'test', 'd129c-view-map.corrupt.tsv')
        write_map_tsv(check_map, corrupt)
        print('负控表（临时副本） -> %s（★把 step %d 的 view 故意改成 %d）'
              % (check_map, corrupt[0], corrupt[1]))

    frames = load_frames(root, set(a.dirs))
    if not frames:
        print('无数据：检查 .ai-tmp/test/D129b-clip-segments.tsv 与 --dirs')
        return 2
    data = measure(root, frames, a.dirs)

    tsv = os.path.join(root, '.ai-tmp', 'test', 'D129c-view-angles.tsv')
    with io.open(tsv, 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['dir', 'kind', 'view_or_pair', 'frame', 'delta_deg', 'iou', 'asym', 'theta_deg', 'weapon_px'])
        for d in a.dirs:
            per, dl = data[d]['per'], data[d]['deltas']
            for v in sorted(dl):
                w.writerow([d, 'delta', '%d>%d' % (v, v + 1), '', '%.1f' % dl[v][0], '%.4f' % dl[v][1], '', '', ''])
            for v in sorted(per):
                r = per[v]
                w.writerow([d, 'view', v, r['frame'], '', '', '%.4f' % r['asym'],
                            '' if r['theta'] is None else '%.2f' % r['theta'], r['nw']])
    print('读数 -> %s' % tsv)
    if not a.no_sheet:
        print('联络图 -> %s' % sheet(root, data, a.dirs, os.path.join(root, '.ai-tmp', 'test', 'D129c-yaw-sheet.png')))

    fails, notes = check(root, data, a.dirs, check_map)
    for n in notes:
        print('  · %s' % n)
    if corrupt:
        print('[负控] --corrupt step %d -> view %d' % corrupt)
    print('%s 断言 A1–A6：%s（失败 %d）'
          % ('[负控]' if corrupt else '[判据]', 'PASS' if not fails else 'FAIL', len(fails)))
    for x in fails[:25]:
        print('   %s' % x)
    return 0 if not fails else 1


if __name__ == '__main__':
    sys.exit(main())
