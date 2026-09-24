#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
d134-jitter.py —— 用户第 8 条（「人物又飘又抖 / 苍蝇海打人时抽搐」）的**逐帧**判据。

输入（全部由 `.ai-tmp/drivers/D134-sample-on.cs` 在被测程序里逐帧写出，L3/L2 级证据）：
  .ai-tmp/test/D134-units.tsv   每帧每单位一行
  .ai-tmp/test/D134-clock.tsv   每帧一行（插值时钟）
  client/Assets/Resources/Sprites/Units/<dir>/frame_NNN.png  （算"可见脚线"要读 alpha）

判据（每条都能失败；`--corrupt` 是配套负控，见文件末尾）：
  A1 速度尖峰比  p95(v)/median(v) ≤ 1.60      （只在**运动帧**上取分位；换窗口抖动会把它顶起来）
  A2 反向次数    行走中相邻位移反向的次数 / 行走时长(10s) ≤ 2.0
  A3 攻击段打断  每个 attack 连续段内 `pos` 回退次数 == 0（段首除外）
  A4 视角抖动    vstep 的"**短游程被同值夹住**"（X→Y→X，Y 长 ≤3 帧）次数 == 0
  A5 贴图闪回    同上口径用在 sprite 上（X→Y→X，Y 长 ≤3 帧）次数 == 0
  A6 行走动画推进率  行走段内换帧速率 ≥ 4.0 次/秒（"滑行不迈腿"= 飘）
  B1 锚点对齐    同一目录所有帧的 `rect.min + pivot`（画布坐标）必须**恒定** == 1 个取值
  B2 锚点常量偏移 只报告（正 = 可见最低像素高于逻辑格中心；需原版渲染对照才有结论）
  C1 时钟连续    ΔrenderMs/Δ真实时间 == 1.00 ± 0.05，且窗口内 ratio 不回退（同一 prev/curr 对）
  D1 堆叠次序    同帧内位置差 <0.01 格的一组单位，`(sortingOrder, z)` 必须互不相同
用法：
  python tools/probes/d134-jitter.py --root .                    # 判据全跑
  python tools/probes/d134-jitter.py --root . --corrupt speed    # 负控：必须 FAIL, rc=1

⚠️ 2026-09-23 的口径订正（两处**判据自身的事故**，都已修）：
  ① 旧 A4/A5 写成"过去 3 帧内出现过同值就计数"⇒ 在一个长游程里**每一帧都命中**，
     曾在 12/12 个单位上假红（vflip=1048/1049）。现改为**游程口径**。实测真值：A5=0（无闪回），
     A4 只剩 2 个单位共 4 处（真缺陷，根因见 `UnitView.FacingMinMoveTiles`）。
  ② 旧 B1 写"同段可见脚线极差 ≤0.06 格"⇒ 量到的是**素材自身**的腿部循环（knight 极差 0.23 格），
     **修代码永远不可能通过**。现改为断言真正属于本工程的不变量：锚点画布坐标恒定。
"""
from __future__ import print_function
import os
import re
import sys
import math
import argparse

try:
    from PIL import Image
except Exception:
    Image = None

# ── 阈值（全部写死在这里，便于审计） ──
TH_A1_SPEED_RATIO = 1.60
# 单位首次出现后的"部署瞬态"帧数：实测（id=54/55 帧 2820..2826）叠同格后 4 帧内散开完、
# 第 5 帧起进入稳态 ⇒ 取 8 = 2× 实测瞬态长度。A1 把它剔除，A1b 单独判它（⛔ 不是隐藏它）。
TH_A1_SPAWN_SKIP_FRAMES = 8
# 部署瞬态比上限：瞬态峰值 ÷ 稳态中位速度。实测"叠同格再炸开"达 5.0×、正常散列 <1.5× ⇒ 取 3.0。
TH_A1B_TRANSIENT_RATIO = 3.0
TH_A2_REV_PER_10S = 2.0
TH_A4_LOOKBACK = 3          # "立刻切回" 的帧窗
TH_A5_SPRITE_FLICKER = 0    # 贴图 A→B→A（≤3 帧内回到同一张）允许次数
TH_A6_MIN_ADV_PER_S = 4.0   # 行走中动画必须推进的最小速度（次/秒）
TH_C1_RATE_TOL = 0.05
TH_C2_MIN_MID_FRAC = 0.50   # `t` 落在 (0.05,0.95) 的帧占比下限（低于此 = 插值退化成阶梯）
TH_C2_TARGET_LAG_MS = 200   # 设计目标落后量（= RenderLagIntervals × SnapshotIntervalMs）
TH_C2_HIST_MARGIN_NOTE = '2'
# C1a/C2 只在"快照流还活着"的帧上判：`newestms` 与本帧前 N 帧不同（N≈3 帧 = 100 ms 内来过快照）。
TH_C_ALIVE_LOOKBACK = 3
# C3 时钟超前最新快照的上限（ms）。时钟超前上限 `ClockMaxLeadMs` = 1 个间隔 = 100 ms，留 50 ms 容差。
TH_C3_MAX_LEAD_MS = 150.0
MIN_EPISODE_FRAMES = 5      # 连续段太短不判（噪声）


def read_tsv(path):
    rows = []
    with open(path, 'r') as f:
        head = f.readline().rstrip('\n').split('\t')
        for ln in f:
            ln = ln.rstrip('\n')
            if not ln:
                continue
            p = ln.split('\t')
            if len(p) != len(head):
                continue
            d = dict(zip(head, p))
            rows.append(d)
    return head, rows


def fnum(d, k, default=float('nan')):
    v = d.get(k)
    if v is None or v == '' or v == 'nan':
        return default
    try:
        return float(v)
    except ValueError:
        return default


def inum(d, k, default=-1):
    v = d.get(k)
    if v is None or v == '':
        return default
    try:
        return int(float(v))
    except ValueError:
        return default


def parse_frame_no(sprite_name):
    """`gen_frame_303_0` / `frame_303_0` / `gen_frame_303` → 303。"""
    if not sprite_name:
        return None
    m = re.search(r'frame_(\d+)', sprite_name)
    return int(m.group(1)) if m else None


class PngCache(object):
    """每个目录：帧号 → (最低不透明行 v_bottom, 最高不透明行 v_top)，**Unity 纹理坐标（底部为 0）**。"""

    def __init__(self, root):
        self.root = root
        self.cache = {}
        self.miss = {}
        if Image is None:
            print('[WARN] 没有 PIL ⇒ B1/B2 判据跳过（脚线需要读 PNG 的 alpha）')

    def get(self, dirname, frame_no):
        if Image is None or not dirname or frame_no is None:
            return None
        key = dirname
        if key not in self.cache:
            self.cache[key] = {}
        tbl = self.cache[key]
        if frame_no in tbl:
            return tbl[frame_no]
        p = os.path.join(self.root, 'client', 'Assets', 'Resources', 'Sprites', 'Units', dirname,
                         'frame_%03d.png' % frame_no)
        if not os.path.exists(p):
            tbl[frame_no] = None
            self.miss[(dirname, frame_no)] = p
            return None
        im = Image.open(p).convert('RGBA')
        w, h = im.size
        a = im.split()[3]
        bbox = a.getbbox()          # (l, t, r, b) 以**顶**为 0
        if bbox is None:
            tbl[frame_no] = None
            return None
        t, b = bbox[1], bbox[3]     # b 是"最后一行+1"
        # 转 Unity 纹理坐标（底为 0）：行 r（顶为 0）↔ v = h-1-r
        v_bottom = h - b            # 最低不透明像素的 v
        v_top = h - 1 - t
        tbl[frame_no] = (v_bottom, v_top, w, h)
        return tbl[frame_no]


def group_series(rows):
    """→ {id: [(frame, rec), ...] 按帧号升序}（同 id 的帧序；跨 id 不混）"""
    g = {}
    for r in rows:
        i = inum(r, 'id')
        g.setdefault(i, []).append(r)
    for k in g:
        g[k].sort(key=lambda x: inum(x, 'frame'))
    return g


def run_lengths(seq):
    """→ [(值, 起始下标, 连续长度), ...]。A4/A5 的口径基础：抖动 = 被同值夹住的**短游程**。"""
    out = []
    i = 0
    n = len(seq)
    while i < n:
        j = i
        while j < n and seq[j] == seq[i]:
            j += 1
        out.append((seq[i], i, j - i))
        i = j
    return out


def split_episodes(recs, field='anim', max_gap=5):
    """把同一 id 的连续采样切成 (值, 记录列表) 段；帧号中断 > max_gap 也断开。"""
    eps = []
    cur = None
    prev_frame = None
    for r in recs:
        fr = inum(r, 'frame')
        v = inum(r, field)
        if cur is None or v != cur[0] or (prev_frame is not None and fr - prev_frame > max_gap):
            cur = (v, [r])
            eps.append(cur)
        else:
            cur[1].append(r)
        prev_frame = fr
    return eps


# ───────────────────────── 判据 A：帧级抖动 ─────────────────────────
def crit_A(series, corrupt=None, rms_by_frame=None):
    fail = []
    stat = []
    for uid, recs in sorted(series.items()):
        dirn = recs[0].get('dir', '?')
        # --- A1 速度剖面（按**屏幕时间**归一化；**只在运动帧上取分位**） ---
        # ⚠️ 口径（2026-09-23 修）：静止帧的 spd 恒为 0，会把中位数拉到 0，进而让整条判据 SKIP
        #    （实测第一份数据 A1 就是这样被"拉低"而 SKIP 的，等于判据失效）。
        #    正确口径 = 先剔除 spd<=1e-3 的静止帧，再在**运动帧**上取 med/p95；
        #    "换窗口跳变"造成的尖峰仍然会把 p95 顶起来 ⇒ 判据依旧能失败（见 --corrupt speed）。
        # ⚠️ 分母口径（2026-09-23 再修，实测事故）：**原用 `dt` = `Time.deltaTime`，而它被 Unity 夹在
        #    `Time.maximumDeltaTime`（默认 1/3 s）**；渲染时钟走的却是**不被夹取**的
        #    `Time.realtimeSinceStartup`。一次卡顿（实测 frame=1847 `dt=0.3333` 正好等于夹取值）
        #    时钟推进了真实的一段时间，而分母只记了 333 ms ⇒ 算出来的瞬时速度**凭空放大 2 倍以上**。
        #    真值对照（同一份数据，只换分母）：id=11 的 p95/med **1.928 → 1.444**、
        #    id=6 **1.892 → 1.328**、id=9 **1.666 → 1.444**（「分母 = rms」与「分母 = dt」两栏见
        #    `.ai-tmp/test/D134-a1-denominator.tsv`）。改用 Δ`renderMs`（时钟表 join）=
        #    **这一帧屏幕上真的过了多少服务端时间**，与位移同源。
        # ⚠️ 第三处口径（2026-09-23）：**剔除"单位首次出现后 8 帧"**。多单位同牌部署时服务端把几只
        #    放在**同一格**（实测 minions 卡 `summon_radius_mt=0` ⇒ id=54/55 帧 2820..2822 坐标
        #    完全相同 `(-5.5000,2.5000)`），随后靠"互相推开"解算在 4 帧内散开 ~1.0 格
        #    （≈7 格/s，是稳态 1.5 格/s 的 5 倍）⇒ 那 4 帧会把 p95 顶出一个尖峰。
        #    该瞬态**不被隐藏**：单列 A1b 判它（见下）。
        n_skip = TH_A1_SPAWN_SKIP_FRAMES
        spd = []
        for a, b in zip(recs, recs[1:]):
            dt = fnum(b, 'dt')
            if rms_by_frame:
                f0, f1 = inum(a, 'frame'), inum(b, 'frame')
                r0, r1 = rms_by_frame.get(f0), rms_by_frame.get(f1)
                if r0 is not None and r1 is not None and r1 > r0:
                    dt = (r1 - r0) / 1000.0
            if not (dt > 1e-4):
                continue
            dx = fnum(b, 'wx') - fnum(a, 'wx')
            dy = fnum(b, 'wy') - fnum(a, 'wy')
            spd.append((math.sqrt(dx * dx + dy * dy) / dt, inum(b, 'frame')))
        # 剔除"首次出现后 n_skip 帧"（只对**前 8 行**生效：recs 是按帧排序的）
        spd_ss = [(v, f) for (v, f) in spd if f - inum(recs[0], 'frame') > n_skip]
        spd_mov = [v for (v, _f) in (spd_ss if len(spd_ss) >= 5 else spd) if v > 1e-3]
        # A1b：部署瞬态 = 首 8 帧内的最大逐帧速度 ÷ 同单位稳态中位速度
        trans = [v for (v, f) in spd if f - inum(recs[0], 'frame') <= n_skip]
        trans_ratio = (max(trans) / median(spd_mov)) if (trans and spd_mov and median(spd_mov) > 1e-3) else float('nan')
        # --- A2 方向反转（只看行走段，且位移足够大） ---
        rev = 0
        walk_frames = 0
        walk_span = 0.0
        for anim_val, ep in split_episodes(recs, 'anim'):
            if anim_val != 1:
                continue
            walk_frames += len(ep)
            for a, b, c in zip(ep, ep[1:], ep[2:]):
                v1 = (fnum(b, 'wx') - fnum(a, 'wx'), fnum(b, 'wy') - fnum(a, 'wy'))
                v2 = (fnum(c, 'wx') - fnum(b, 'wx'), fnum(c, 'wy') - fnum(b, 'wy'))
                n1 = math.hypot(*v1)
                n2 = math.hypot(*v2)
                if n1 < 1e-4 or n2 < 1e-4:
                    continue
                if (v1[0] * v2[0] + v1[1] * v2[1]) / (n1 * n2) < -0.5:
                    rev += 1
            if len(ep) >= 2:
                walk_span += fnum(ep[-1], 't') - fnum(ep[0], 't')
        # --- A3 攻击段打断 ---
        resets = 0
        atk_eps = 0
        atk_frames = 0
        for anim_val, ep in split_episodes(recs, 'anim'):
            if anim_val != 2:
                continue
            atk_eps += 1
            atk_frames += len(ep)
            ps = [inum(x, 'pos') for x in ep]
            for a, b in zip(ps, ps[1:]):
                if b < a:
                    resets += 1
        # --- A4 视角抖动（立刻切回） ---
        # ⚠️ 口径（2026-09-23 修）：**必须基于游程**，不能写成"过去 N 帧出现过同值就 +1"。
        #    旧写法在一个长度 862 帧的稳定游程里会**每一帧都命中**（因为它跟上一帧同值），
        #    于是 vflip≈帧数、12/12 个单位全部假红 —— 那是判据事故，不是缺陷。
        #    正确语义 = "值从 X 切到 Y，又在 ≤LOOKBACK 帧内切回 X"（短游程被同值夹住）。
        flips = 0
        rv = run_lengths([inum(r, 'vstep') for r in recs])
        for j in range(1, len(rv) - 1):
            if rv[j][2] <= TH_A4_LOOKBACK and rv[j - 1][0] == rv[j + 1][0] and rv[j][0] != rv[j - 1][0]:
                flips += 1
        # --- A5 贴图闪回（A→B→A，B 的连续长度 ≤3 帧）---
        sp_flick = 0
        rs = run_lengths([(r.get('sprite') or '').strip() for r in recs])
        for j in range(1, len(rs) - 1):
            if rs[j][2] <= TH_A4_LOOKBACK and rs[j - 1][0] == rs[j + 1][0] and rs[j][0] != rs[j - 1][0]:
                sp_flick += 1
        # --- A6 行走中的动画推进率（"滑行不迈腿"= 飘）---
        adv_rates = []
        for anim_val, ep in split_episodes(recs, 'anim'):
            if anim_val != 1 or len(ep) < MIN_EPISODE_FRAMES:
                continue
            # 该段平均速度（格/秒），低于阈值说明其实站着，不判
            d = 0.0
            for a, b in zip(ep, ep[1:]):
                d += math.hypot(fnum(b, 'wx') - fnum(a, 'wx'), fnum(b, 'wy') - fnum(a, 'wy'))
            span = fnum(ep[-1], 't') - fnum(ep[0], 't')
            if span <= 0.2 or d / span < 0.30:
                continue
            changes = 0
            for a, b in zip(ep, ep[1:]):
                if inum(b, 'pos') != inum(a, 'pos'):
                    changes += 1
            adv_rates.append(changes / span)
        stat.append(dict(id=uid, dir=dirn, n=len(recs),
                         spd_med=median(spd_mov), spd_p95=p95(spd_mov),
                         spd_max=max(spd_mov) if spd_mov else 0.0,
                         spd_nmov=len(spd_mov), trans_ratio=trans_ratio,
                         rev=rev, walk_s=walk_span, atk_eps=atk_eps, atk_frames=atk_frames,
                         resets=resets, vstep_flips=flips, sp_flick=sp_flick,
                         adv_min=min(adv_rates) if adv_rates else float('nan')))

    # 汇总（把每个单位当独立样本；只有真的有行走/攻击的单位才进判）
    sp_ratios = [s['spd_p95'] / s['spd_med'] for s in stat
                 if s['spd_med'] > 1e-3 and s['spd_nmov'] >= 10]
    rev_per10 = [(s['rev'] / s['walk_s'] * 10.0) for s in stat if s['walk_s'] > 1.0]
    resets = [(s['id'], s['resets']) for s in stat if s['atk_frames'] >= MIN_EPISODE_FRAMES and s['resets'] > 0]
    flips = [(s['id'], s['vstep_flips']) for s in stat if s['vstep_flips'] > 0]
    flicks = [(s['id'], s['sp_flick']) for s in stat if s['sp_flick'] > TH_A5_SPRITE_FLICKER]
    slow_adv = [(s['id'], round(s['adv_min'], 2)) for s in stat
                if s['adv_min'] == s['adv_min'] and s['adv_min'] < TH_A6_MIN_ADV_PER_S]

    if corrupt == 'speed' and stat:
        # 负控：人为把某个单位的速度剖面塞一个尖峰（在被判对象上动手，判据必须红）
        # 必须挑一个**真的在动**的单位，否则 spd_med 为 0 会被 A1 的过滤条件排除，负控就成了空转
        pool = [x for x in stat if x['spd_med'] > 1e-3 and x['spd_nmov'] >= 10] or stat
        s = max(pool, key=lambda x: x['n'])
        s['spd_p95'] = s['spd_med'] * 3.0
        sp_ratios = [x['spd_p95'] / x['spd_med'] for x in stat
                     if x['spd_med'] > 1e-3 and x['spd_nmov'] >= 10]
    if corrupt == 'spawnburst' and stat:
        # 负控：注入"部署瞬态"（叠同格再炸开）⇒ A1b 必须红
        s = max(stat, key=lambda x: x['n'])
        s['trans_ratio'] = 9.9
    if corrupt == 'reset' and stat:
        s = max(stat, key=lambda x: x['n'])
        s['resets'] = 1
        resets = [(s['id'], 1)]
    if corrupt == 'vflip' and stat:
        s = max(stat, key=lambda x: x['n'])
        s['vstep_flips'] = 1
        flips = [(s['id'], 1)]
    if corrupt == 'spflick' and stat:
        s = max(stat, key=lambda x: x['n'])
        s['sp_flick'] = 1
        flicks = [(s['id'], 1)]
    if corrupt == 'noadv' and stat:
        s = max(stat, key=lambda x: x['n'])
        s['adv_min'] = 0.0
        slow_adv = [(s['id'], 0.0)]
    if corrupt == 'reversal' and stat:
        s = max(stat, key=lambda x: x['n'])
        s['rev'] = 30
        rev_per10 = [(30 / 1.0)]

    print('== 判据 A：帧级抖动 ==')
    print('   单位数(有采样过的 id)=%d' % len(stat))
    for s in sorted(stat, key=lambda x: -x['n'])[:24]:
        print('   id=%-5d %-22s n=%-5d 动帧=%-4d spd med/p95/max=%.4f/%.4f/%.4f  rev=%-3d walk=%.1fs atk(eps=%d,frames=%d,resets=%d) vflip=%d spflick=%d adv=%s'
              % (s['id'], s['dir'], s['n'], s['spd_nmov'], s['spd_med'], s['spd_p95'], s['spd_max'],
                 s['rev'], s['walk_s'], s['atk_eps'], s['atk_frames'], s['resets'], s['vstep_flips'],
                 s['sp_flick'], ('%.2f' % s['adv_min']) if s['adv_min'] == s['adv_min'] else '-'))
    if len(stat) > 24:
        print('   …（其余 %d 个单位略）' % (len(stat) - 24))

    if sp_ratios:
        worst = max(sp_ratios)
        print('   A1 稳态速度尖峰比 worst=%.3f（×%d 个单位，阈值 ≤%.2f，分母=ΔrenderMs，已剔除首 %d 帧）⇒ %s'
              % (worst, len(sp_ratios), TH_A1_SPEED_RATIO, TH_A1_SPAWN_SKIP_FRAMES,
                 'PASS' if worst <= TH_A1_SPEED_RATIO else 'FAIL'))
        if worst > TH_A1_SPEED_RATIO:
            fail.append('A1 速度尖峰比 %.3f > %.2f' % (worst, TH_A1_SPEED_RATIO))
    else:
        print('   A1 无足够行走样本 ⇒ SKIP')
    # A1b 部署瞬态（**不隐藏**上面被剔除的那 8 帧）：多单位同牌部署时服务端是否把几只放在同一格、
    #   再由"互相推开"解算炸开。阈值 3.0 = 稳态速度的 3 倍（实测叠同格时达 5 倍，正常散列时 <1.5）。
    trans = [(s['id'], round(s['trans_ratio'], 2)) for s in stat
             if s['trans_ratio'] == s['trans_ratio'] and s['trans_ratio'] > TH_A1B_TRANSIENT_RATIO]
    if any(s['trans_ratio'] == s['trans_ratio'] for s in stat):
        print('   A1b 部署瞬态比（首 %d 帧内峰值 ÷ 稳态中位速度）超过 %.1f 的单位：%s ⇒ %s'
              % (TH_A1_SPAWN_SKIP_FRAMES, TH_A1B_TRANSIENT_RATIO, trans,
                 'PASS' if not trans else 'FAIL'))
        if trans:
            fail.append('A1b 部署瞬态 %d 个单位（最差 %.2f×稳态）' % (len(trans), max(t[1] for t in trans)))
    else:
        print('   A1b 无样本 ⇒ SKIP')
    if rev_per10:
        worst = max(rev_per10)
        print('   A2 反向次数 worst=%.2f 次/10s（×%d 个单位，阈值 ≤%.2f）⇒ %s'
              % (worst, len(rev_per10), TH_A2_REV_PER_10S, 'PASS' if worst <= TH_A2_REV_PER_10S else 'FAIL'))
        if worst > TH_A2_REV_PER_10S:
            fail.append('A2 反向 %.2f 次/10s > %.2f' % (worst, TH_A2_REV_PER_10S))
    else:
        print('   A2 无足够行走样本 ⇒ SKIP')
    print('   A3 attack 段内 pos 回退：命中 %d 个单位 ⇒ %s'
          % (len(resets), 'PASS' if not resets else 'FAIL %s' % resets[:6]))
    if resets:
        fail.append('A3 attack 段内 pos 回退 %d 处' % len(resets))
    print('   A4 视角立刻切回：命中 %d 个单位 ⇒ %s'
          % (len(flips), 'PASS' if not flips else 'FAIL %s' % flips[:6]))
    if flips:
        fail.append('A4 视角立刻切回 %d 处' % len(flips))
    print('   A5 贴图闪回(A→B→A ≤%d 帧)：命中 %d 个单位 ⇒ %s'
          % (TH_A4_LOOKBACK, len(flicks), 'PASS' if not flicks else 'FAIL %s' % flicks[:6]))
    if flicks:
        fail.append('A5 贴图闪回 %d 处' % len(flicks))
    if slow_adv:
        print('   A6 行走动画推进率 <%.1f 次/秒：命中 %d 个单位 ⇒ FAIL %s'
              % (TH_A6_MIN_ADV_PER_S, len(slow_adv), slow_adv[:6]))
        fail.append('A6 行走动画推进过慢 %d 个单位' % len(slow_adv))
    else:
        print('   A6 行走动画推进率 ≥%.1f 次/秒（或行走样本不足）⇒ PASS/SKIP' % TH_A6_MIN_ADV_PER_S)
    return fail, stat


# ───────────────────────── 判据 B：锚点对齐（"飘"） ─────────────────────────
def crit_B(series, png, corrupt=None, sample_limit=24):
    """B1（**能失败**的对齐不变量）：同一目录的所有帧，锚点的**画布坐标**必须恒定。

    为什么判它 —— 以及为什么**不**判"脚线漂移 ≤0.06 格"：
      各帧 PNG 是**整张画布**（189×185 / 187×181 …），导入时 Unity 按各自的不透明包围盒**逐帧裁剪**，
      于是 `sprite.rect` 逐帧不同。`UnitView.SpriteBank.UnifyCanvasAnchor` 按
      `pivot = anchor − rect.min`（像素）重建精灵 ⇒ 渲染时"画布上的同一个点 `anchor`"始终落在
      `transform.position` 上。⇒ **只要 anchor 逐帧恒定，A 帧与 B 帧的画布内相对位置就是 1:1 保真的**。
      实测 `recty + pivy ≡ 90.5`（= 画布中心）覆盖全部 18 个不同的 `recty` 取值，B2 的 −0.51 格只是
      一个**常量**偏移（= 画布中心相对逻辑格中心的位置），与帧间抖动无关。
      ⚠️ 因此旧口径"同一段内可见脚线极差 ≤0.06 格"是**错的**：它量到的是**素材本身**的腿部循环
      （knight 原始帧最低不透明行 133..156 px，极差 23 px = 0.23 格，序列
      `147,142,140,140,142,145,146,148,…` 是平滑周期摆动）——**修代码永远不可能让它通过**，
      和 A4/A5 是同一类判据事故。现改为断言真正属于我们代码的不变量：anchor 恒定。
      负控 `--corrupt anchor` 注入一帧不同的 anchor ⇒ 必须 FAIL。
    """
    print('== 判据 B：锚点对齐（画布坐标恒定） ==')
    if Image is None:
        print('   [WARN] 无 PIL：脚线统计跳过（anchor 不变量不依赖 PNG，仍会判）')
    fail = []
    per_dir = {}       # dir -> {'ax': set, 'ay': set, 'feet': [...]}
    worst_ep = []
    for uid, recs in sorted(series.items()):
        dirn = recs[0].get('dir', '?')
        for anim_val, ep in split_episodes(recs, 'anim'):
            vals = []
            for r in ep:
                rx = fnum(r, 'rectx'); ry = fnum(r, 'recty')
                px = fnum(r, 'pivx'); py = fnum(r, 'pivy')
                ppu = fnum(r, 'ppu')
                if math.isnan(rx) or math.isnan(ry) or math.isnan(px) or math.isnan(py):
                    continue
                d = per_dir.setdefault(dirn, {'ax': set(), 'ay': set(), 'feet': []})
                d['ax'].add(round(rx + px, 3))
                d['ay'].add(round(ry + py, 3))
                if Image is None or not (ppu > 0):
                    continue
                fn = parse_frame_no(r.get('sprite'))
                if fn is None:
                    continue
                info = png.get(dirn, fn)
                if info is None:
                    continue
                v_bottom = info[0]
                # ⚠️ `Sprite.pivot` 是**像素**（相对该 sprite 的 rect 左下角），不是归一化值 ——
                #    实测 chr_knight_out: rect=(28,32,103,87) pivot=(65.5,58.5) ⇒ 纹理锚点 (93.5,90.5)
                #    = 画布中心 (187/2,181/2)。若误当归一化会得出 (32+0.5*87)=75.5 的错值。
                pivot_v = ry + py                          # 纹理像素（底为 0）
                feet_world = fnum(r, 'wy') + (v_bottom - pivot_v) / ppu
                vals.append(feet_world - fnum(r, 'wy'))
            if len(vals) >= MIN_EPISODE_FRAMES:
                # 报告值：可见脚线的世界 y 极差。**这是素材属性，不是通过/失败判据**（见函数注释）。
                worst_ep.append((uid, dirn, anim_val, max(vals) - min(vals),
                                 sum(vals) / len(vals), len(vals)))

    if corrupt == 'anchor' and per_dir:
        k = sorted(per_dir)[0]
        per_dir[k]['ay'] = set(list(per_dir[k]['ay']) + [max(per_dir[k]['ay']) + 0.5])

    print('   [报告值·非判据] 各段"可见脚线"世界 y 极差（= 素材自身腿部循环，非本工程缺陷）：')
    for uid, dirn, anim_val, drift, mean_off, n in sorted(worst_ep, key=lambda x: -x[3])[:sample_limit]:
        print('      id=%-5d %-22s tier=%d n=%-4d 极差=%.4f 格 均值偏移=%+.4f 格'
              % (uid, dirn, anim_val, n, drift, mean_off))

    print('   B1 锚点画布坐标（rect.min + pivot）逐目录恒定：')
    bad = []
    for k in sorted(per_dir):
        axs = sorted(per_dir[k]['ax']); ays = sorted(per_dir[k]['ay'])
        ok = len(axs) == 1 and len(ays) == 1
        print('      %-24s anchor.x 取值=%s  anchor.y 取值=%s  ⇒ %s'
              % (k, axs[:4], ays[:4], 'ok' if ok else 'FAIL'))
        if not ok:
            bad.append(k)
    if bad:
        fail.append('B1 锚点逐帧不恒定 %d 个目录' % len(bad))
    print('      ⇒ %s' % ('PASS' if not bad else 'FAIL %s' % bad))
    print('   B2 按目录的脚线常量偏移（正 = 可见最低像素高于逻辑格中心；需原版渲染对照，暂只报告）：')
    agg = {}
    for uid, dirn, anim_val, drift, mean_off, n in worst_ep:
        agg.setdefault(dirn, []).append(mean_off)
    for k in sorted(agg):
        v = agg[k]
        print('      %-24s 段数=%-3d 均值=%+.4f 格' % (k, len(v), sum(v) / len(v)))
    return fail, per_dir


# ───────────────────────── 判据 C：插值时钟连续性 ─────────────────────────
def crit_C(clock, corrupt=None):
    print('== 判据 C：插值时钟 ==')
    if not clock:
        print('   SKIP（无 clock 行）')
        return []
    fail = []
    rows = []
    for r in clock:
        t = fnum(r, 't'); rms = fnum(r, 'rms'); pms = fnum(r, 'prevms'); cms = fnum(r, 'currms')
        rows.append((inum(r, 'frame'), t, rms, pms, cms, fnum(r, 'ratio'), inum(r, 'nunits'),
                     fnum(r, 'newestms')))
    # 「快照流还活着吗」—— C1a / C2 只在**有数据可插**的帧上判。
    #   ⚠️ 口径（2026-09-23 修，实测事故）：快照**停推**（对局结束 / 断线）之后 `newestms` 不再前进，
    #      此时"插值有效性"根本无从谈起（没有可插的数据），而旧口径把这些帧也算进去 ⇒
    #      C2 从 0.884 掉到 0.312、看起来像"修坏了"。真值：12902 行里有 7891 行 `t` 被夹成 1.0，
    #      其中绝大多数落在停推段。⇒ 判据 = `newestms` 与 3 帧前不同（≈100 ms 内来过快照）。
    #   ⛔ 这不是"隐藏证据"：停推段的时钟行为改由 **C3** 单独判（见下）。
    alive = [False] * len(rows)
    for i, r in enumerate(rows):
        j = max(0, i - TH_C_ALIVE_LOOKBACK)
        alive[i] = (r[7] == r[7] and rows[j][7] == rows[j][7] and r[7] != rows[j][7])
    n_alive = sum(1 for v in alive if v)
    print('   快照流存活帧 = %d / %d（停推段 %d 帧不参与 C1a/C2；它们由 C3 单独判）'
          % (n_alive, len(rows), len(rows) - n_alive))
    # C1a: ΔrenderMs / Δt == 1（同一窗口内、且快照流活着）
    rates = []
    for k, (a, b) in enumerate(zip(rows, rows[1:])):
        if not alive[k + 1]:
            continue
        if b[0] - a[0] > 3:
            continue
        dt = b[1] - a[1]
        if dt < 1e-4:
            continue
        if not (b[2] == b[2] and a[2] == a[2]):
            continue
        rates.append(((b[2] - a[2]) / 1000.0) / dt)
    if corrupt == 'clock' and rates:
        # 负控必须真的能把 C1a 打红：只改 rates[0] 时中位数纹丝不动（7513 个样本里一个离群点
        # 不影响中位数）⇒ 判据其实"无法失败"。改为整体缩放 1.4（模拟 ΔrenderMs/Δt ≠ 1）。
        rates = [r * 1.4 for r in rates]
    r_med = median(rates) if rates else float('nan')
    print('   C1a ΔrenderMs/Δt 中位数=%.4f（n=%d，阈值 1±%.2f）⇒ %s'
          % (r_med, len(rates), TH_C1_RATE_TOL, 'PASS' if abs(r_med - 1.0) <= TH_C1_RATE_TOL else 'FAIL'))
    if not (abs(r_med - 1.0) <= TH_C1_RATE_TOL):
        fail.append('C1a 渲染时钟速率 %.4f' % r_med)
    # C1b: 同一 (prev,curr) 对内 ratio 不回退
    back = 0
    span_checks = 0
    for a, b in zip(rows, rows[1:]):
        if a[3] == b[3] and a[4] == b[4] and a[5] == a[5] and b[5] == b[5]:
            span_checks += 1
            if b[5] < a[5] - 1e-6:
                back += 1
    print('   C1b 同一窗口内 ratio 回退 %d 次（检查 %d 对）⇒ %s'
          % (back, span_checks, 'PASS' if back == 0 else 'FAIL'))
    if back:
        fail.append('C1b ratio 回退 %d 次' % back)
    # C1c/C2: **插值是否真的在跑** —— 这是"静默失效"的唯一可见指纹。
    #   `t = (renderMs - prevMs) / (currMs - prevMs)`。正常时 t 在一个间隔内从 0 连续爬到 1，
    #   30 fps + 100 ms 间隔 ⇒ 绝大多数帧的 t 落在 (0,1) 中间。若时钟掉出快照历史，
    #   `SelectWindow` 只能夹到最旧一对 ⇒ t 退化成**只有 0 和 1**（位置按快照周期阶梯跳），
    #   而画面上只是"动得一顿一顿"，**不报任何错**。
    #   出处：D134 实测 7953 帧 —— 退化时 t 取值统计 = 0(5150) / 1(2355) / 中间值(3)。
    valid = [r[5] for r, av in zip(rows, alive) if av]
    valid = [v for v in valid if v == v]
    mid = [v for v in valid if 0.05 < v < 0.95]
    frac_mid = (len(mid) / float(len(valid))) if valid else float('nan')
    if corrupt == 'clockdegen':
        frac_mid = 0.0
    lag = [(r[7] - r[2]) for r, av in zip(rows, alive) if av and r[7] == r[7] and r[2] == r[2]]
    lag_med = median(lag) if lag else float('nan')
    print('   C2 插值有效性 t∈(0.05,0.95) 占比 = %.3f（有效行 %d，阈值 ≥%.2f）⇒ %s'
          % (frac_mid, len(valid), TH_C2_MIN_MID_FRAC,
             'PASS' if frac_mid == frac_mid and frac_mid >= TH_C2_MIN_MID_FRAC else 'FAIL'))
    print('      帧内 t 取值分布 top5 = %s' % sorted(
        ((v, valid.count(v)) for v in set(valid)), key=lambda x: -x[1])[:5])
    print('      newest−renderMs 中位数 = %.0f ms（设计目标 %s ms = %s 个间隔）'
          % (lag_med, TH_C2_TARGET_LAG_MS, TH_C2_HIST_MARGIN_NOTE))
    if not (frac_mid == frac_mid and frac_mid >= TH_C2_MIN_MID_FRAC):
        fail.append('C2 插值退化：t 落在 (0,1) 的帧只占 %.3f' % frac_mid)
    # C3 时钟**超前**上限：`renderMs - newestMs` 不许超过 1 个间隔 + 容差。
    #   为什么单独判它：停推时时钟会跟着 `ServerNowMs` 一路跑飞（实测超前 **50.8 s**），
    #   而把 50 s 的偏差用 ±5%/s 的速率拉回来要上千秒 ⇒ 那段时间单位冻在最后一帧。
    #   修法 = 时钟超前上限 `ClockMaxLeadMs`（挂 `_newestMs`，快照恢复即松开）。
    #   负控：`--corrupt clockrunaway`（把几个超前量按 +10 s 注入）必须红。
    lead = [(r[2] - r[7]) for r in rows if r[2] == r[2] and r[7] == r[7]]
    if corrupt == 'clockrunaway' and lead:
        lead = [max(v, 10000.0) for v in lead]
    lead_max = max(lead) if lead else float('nan')
    n_bad = sum(1 for v in lead if v > TH_C3_MAX_LEAD_MS)
    print('   C3 时钟超前最新快照：最大 %.0f ms（阈值 ≤%.0f ms = %d 个间隔(100ms) + 容差），超限 %d 帧 ⇒ %s'
          % (lead_max, TH_C3_MAX_LEAD_MS, TH_C3_MAX_LEAD_MS // 100, n_bad,
             'PASS' if n_bad == 0 else 'FAIL'))
    if n_bad:
        fail.append('C3 时钟超前最新快照 %.0fms（%d 帧）' % (lead_max, n_bad))
    nus = [r[6] for r in rows if r[6] >= 0]
    print('   clock 行=%d  单位数 min/max=%s/%s  采样帧跨度=%d'
          % (len(rows), min(nus) if nus else '-', max(nus) if nus else '-',
             (rows[-1][0] - rows[0][0]) if rows else 0))
    return fail


def crit_D(series, corrupt=None):
    """D1：同一帧内**位置完全相同**的多个单位（堆叠）的绘制先后是否确定。

    为什么判它：苍蝇海（一次召唤 6 只）在服务端快照里可能拿到**完全相同的 x_milli/y_milli**；
    此时 `sortingOrder = Unit + round((16 - wy) * 16)` 对它们**取值相同** ⇒ 谁盖谁由渲染器枚举顺序决定，
    而每个单位的**档内帧下标不同**（各自播自己的动画）⇒ 同一像素上叠加不同贴图，观感是"一坨在抽搐"。

    排序键 = `(sortingOrder, z)`：
      ① `sortingOrder` 是 Unity 的主键（第 1 级）—— 本工程的层级预算见 `UnitView.Apply`；
      ② 主键相同（= 位置差 < 1/16 格）时，Unity 的透明队列按**离相机距离**排，而本工程所有精灵都在
         z=0 ⇒ 距离也相同 ⇒ 次序落到"枚举顺序"（不确定）。
      修法 = 给每个单位一个**只由 id 决定**的微小 z 偏移（`UnitView.DepthTiebreak`，步长 1e-4 格），
      正交相机下 z 不改投影位置、只当第二键 ⇒ 同格堆叠有确定先后，且逐帧稳定。
      判据（能失败）：同一帧内位置差 < 0.01 格的一组单位，其 `(sortingOrder, wz)` 必须**互不相同**。
    """
    print('== 判据 D：同位堆叠的绘制先后 ==')
    frames = {}
    for uid, recs in series.items():
        for r in recs:
            frames.setdefault(inum(r, 'frame'), []).append((uid, fnum(r, 'wx'), fnum(r, 'wy'),
                                                            inum(r, 'order'), fnum(r, 'wz'),
                                                            inum(r, 'vstep'), r.get('dir', '?')))
    groups = 0
    bad_groups = 0
    max_stack = 0
    dup_order_frames = 0
    detail = []
    for fr in sorted(frames):
        bucket = {}
        for uid, wx, wy, order, wz, vstep, dirn in frames[fr]:
            if math.isnan(wx) or math.isnan(wy):
                continue
            bucket.setdefault((round(wx, 2), round(wy, 2)), []).append((uid, order, wz, vstep, dirn))
        for key, lst in bucket.items():
            if len(lst) < 2:
                continue
            groups += 1
            max_stack = max(max_stack, len(lst))
            keys = [(o, round(z, 6) if z == z else None) for _, o, z, _, _ in lst]
            if len(set(keys)) != len(keys):
                bad_groups += 1
                dup_order_frames += 1
                if len(detail) < 8:
                    detail.append('frame=%d pos=%s n=%d (order,z)=%s vstep=%s dir=%s'
                                  % (fr, key, len(lst), keys, [v for _, _, _, v, _ in lst],
                                     [d for _, _, _, _, d in lst]))
    if corrupt == 'stack':
        bad_groups = max(1, bad_groups)
        detail = ['(corrupt) 注入一条"同位且 (order,z) 相同"的组']
    print('   同位组（同帧内位置差<0.01格且≥2单位）共 %d 个；最大堆叠 %d 只' % (groups, max_stack))
    for d in detail:
        print('     %s' % d)
    print('   其中 (sortingOrder, z) 有重复的组 = %d（涉及 %d 帧）⇒ %s'
          % (bad_groups, dup_order_frames, 'PASS' if bad_groups == 0 else 'FAIL'))
    fails = []
    if bad_groups:
        fails.append('D1 同位堆叠但 (sortingOrder,z) 重复 %d 组' % bad_groups)
    return fails


def median(xs):
    if not xs:
        return float('nan')
    s = sorted(xs)
    n = len(s)
    return s[n // 2] if n % 2 else 0.5 * (s[n // 2 - 1] + s[n // 2])


def p95(xs):
    if not xs:
        return float('nan')
    s = sorted(xs)
    i = int(round(0.95 * (len(s) - 1)))
    return s[i]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--corrupt', default=None,
                    choices=[None, 'speed', 'reset', 'vflip', 'reversal', 'anchor', 'clock', 'clockdegen',
                             'clockrunaway', 'spflick', 'noadv', 'stack', 'spawnburst'])
    a = ap.parse_args()
    root = a.root
    up = os.path.join(root, '.ai-tmp', 'test', 'D134-units.tsv')
    cp = os.path.join(root, '.ai-tmp', 'test', 'D134-clock.tsv')
    if not os.path.exists(up) or not os.path.exists(cp):
        print('[BLOCKED] 缺少逐帧采样产物：%s / %s' % (up, cp))
        return 3
    hu, units = read_tsv(up)
    hc, clock = read_tsv(cp)
    print('输入：units 行=%d  clock 行=%d  corrupt=%s' % (len(units), len(clock), a.corrupt))
    series = group_series(units)
    png = PngCache(root)
    rms_by_frame = {}
    for r in clock:
        try:
            rms_by_frame[int(r['frame'])] = float(r['rms'])
        except Exception:
            pass
    print('分母口径：ΔrenderMs（clock 表 join 到 %d 帧；缺帧时回退 ΔTime.deltaTime）' % len(rms_by_frame))
    fa, stat = crit_A(series, a.corrupt, rms_by_frame)
    fb, per_dir = crit_B(series, png, a.corrupt)
    fc = crit_C(clock, a.corrupt)
    fd = crit_D(series, a.corrupt)
    fails = fa + fb + fc + fd
    print()
    print('[判据] 失败 %d 条：%s' % (len(fails), '；'.join(fails) if fails else '（全部 PASS）'))
    print('rc=%d' % (1 if fails else 0))
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main())
