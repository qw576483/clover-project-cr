# -*- coding: utf-8 -*-
"""离线仿真：用**真实的快照点**重放「线性插值 vs centripetal Catmull-Rom」，
跑与 d134-jitter.py 同口径的 A1/A1b/A2 判据，看修复前后的读数。

做法：
 1. units.tsv 的 wx/wy 是**已插值**的位置，所以先按 (prevms,currms) 窗口分组、
    用 (t, wx) 与 (t, wy) 最小二乘拟合出该窗口两端快照的真实位置（t=0 ⇒ pL，t=1 ⇒ pR）。
 2. 相邻窗口共享端点（窗口 i 的 pR 就是窗口 i+1 的 pL）⇒ 拼成快照位置序列 S[i]。
 3. 对每一帧按它所属窗口 i 与 t，分别用
      linear  : pL + (pR-pL)*t
      spline  : centripetal Catmull-Rom(S[i-1], S[i], S[i+1], S[i+2], t)
    重建位置序列，再各自跑 A1/A1b/A2。
只读。"""
from __future__ import print_function
import math
import os
from collections import defaultdict

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
UP = os.path.join(ROOT, r".ai-tmp\test\D134-units.tsv")
CP = os.path.join(ROOT, r".ai-tmp\test\D134-clock.tsv")
TH_A1 = 1.60
TH_A1B = 3.0
TH_A2 = 2.0
SPAWN_SKIP = 8


def read(p):
    rows = []
    with open(p, encoding="utf-8-sig") as f:
        head = f.readline().rstrip("\n").split("\t")
        for ln in f:
            p2 = ln.rstrip("\n").split("\t")
            if len(p2) != len(head):
                continue
            rows.append(dict(zip(head, p2)))
    return head, rows


def catmull(p0, p1, p2, p3, t):
    t0 = 0.0
    t1 = t0 + math.sqrt(math.hypot(p1[0] - p0[0], p1[1] - p0[1]))
    t2 = t1 + math.sqrt(math.hypot(p2[0] - p1[0], p2[1] - p1[1]))
    t3 = t2 + math.sqrt(math.hypot(p3[0] - p2[0], p3[1] - p2[1]))
    if t1 <= t0 or t2 <= t1 or t3 <= t2:
        return (p1[0] + (p2[0] - p1[0]) * t, p1[1] + (p2[1] - p1[1]) * t)
    tt = t1 + min(1.0, max(0.0, t)) * (t2 - t1)
    def lin(a, b, ta, tb, x):
        return ((tb - x) / (tb - ta) * a[0] + (x - ta) / (tb - ta) * b[0],
                (tb - x) / (tb - ta) * a[1] + (x - ta) / (tb - ta) * b[1])
    a1 = lin(p0, p1, t0, t1, tt)
    a2 = lin(p1, p2, t1, t2, tt)
    a3 = lin(p2, p3, t2, t3, tt)
    b1 = lin(a1, a2, t0, t2, tt)
    b2 = lin(a2, a3, t1, t3, tt)
    return lin(b1, b2, t1, t2, tt)


def fit2(samples):
    """样本 = [(t, x)]，返回 (x_at_0, x_at_1)；样本 <2 时返回 None。"""
    n = len(samples)
    if n < 2:
        return None
    st = sum(s[0] for s in samples)
    sx = sum(s[1] for s in samples)
    stt = sum(s[0] * s[0] for s in samples)
    stx = sum(s[0] * s[1] for s in samples)
    den = n * stt - st * st
    if abs(den) < 1e-12:
        return (sx / n, sx / n)
    b = (n * stx - st * sx) / den
    a = (sx - b * st) / n
    return (a, a + b)


ch, crows = read(CP)
clk = {}
for r in crows:
    try:
        fr = int(r["frame"])
    except Exception:
        continue
    def g(k):
        v = r.get(k)
        try:
            return float(v)
        except Exception:
            return float("nan")
    clk[fr] = (g("rms"), g("prevms"), g("currms"))

uh, urows = read(UP)
per = defaultdict(list)
for r in urows:
    per[r["id"]].append(r)


def build(uid):
    """返回 [(frame, rms, t, winKey, wx, wy)]，只保留能算出 t 的帧。"""
    out = []
    for r in per[uid]:
        try:
            fr = int(r["frame"])
            wx, wy = float(r["wx"]), float(r["wy"])
        except Exception:
            continue
        if fr not in clk:
            continue
        rms, pv, cu = clk[fr]
        if not (rms == rms and pv == pv and cu == cu) or cu <= pv:
            continue
        t = min(1.0, max(0.0, (rms - pv) / (cu - pv)))
        out.append((fr, rms, t, (pv, cu), wx, wy))
    out.sort()
    return out


def rebuild(uid):
    """重建快照序列 S，并给出每帧的 (rms, t, winIdx, linear_pos, spline_pos)。"""
    fr = build(uid)
    if len(fr) < 20:
        return None
    wins = []
    seen = {}
    for rec in fr:
        key = rec[3]
        seen.setdefault(key, []).append(rec)
    for key in sorted(seen.keys()):
        grp = seen[key]
        fx = fit2([(r[2], r[4]) for r in grp])
        fy = fit2([(r[2], r[5]) for r in grp])
        if fx is None or fy is None:
            continue
        wins.append((key, (fx[0], fy[0]), (fx[1], fy[1]), grp))
    if len(wins) < 5:
        return None
    # 快照序列：窗口 i 的左端 == 窗口 i+1 的右端；用后一个窗口的左端更贴近"同一快照"。
    S = [w[1] for w in wins] + [wins[-1][2]]
    # 窗口索引按 key 排序后，找每帧属于第几个窗口
    idx = {}
    for i, w in enumerate(wins):
        idx[w[0]] = i
    res = []
    for rec in fr:
        i = idx.get(rec[3])
        if i is None:
            continue
        pL, pR = wins[i][1], wins[i][2]
        lin = (pL[0] + (pR[0] - pL[0]) * rec[2], pL[1] + (pR[1] - pL[1]) * rec[2])
        if 1 <= i <= len(S) - 3:
            sp = catmull(S[i - 1], S[i], S[i + 1], S[i + 2], rec[2])
        else:
            sp = lin
        res.append((rec[0], rec[1], rec[2], lin, sp, rec[4], rec[5]))
    return res


def metrics(res, which):
    """which=3 取 linear（下标 3），=4 取 spline。返回 (a1_ratio, a1b_ratio, rev_per10)"""
    spd, trans = [], []
    f0 = res[0][0]
    for a, b in zip(res, res[1:]):
        dr = (b[1] - a[1]) / 1000.0
        if dr <= 1e-4:
            continue
        dx = b[which][0] - a[which][0]
        dy = b[which][1] - a[which][1]
        d = math.hypot(dx, dy)
        v = d / dr
        # 跳过"服务端时间轴上的大跳"（>450ms 的时钟推进 = 停推/卡顿）
        if dr > 0.45:
            continue
        if b[0] - f0 > SPAWN_SKIP:
            spd.append(v)
        else:
            trans.append(v)
    mov = [v for v in spd if v > 1e-3]
    if len(mov) < 10:
        return None
    mov.sort()
    med = mov[len(mov) // 2]
    p95 = mov[min(len(mov) - 1, int(0.95 * (len(mov) - 1)))]
    a1 = p95 / med if med > 1e-9 else float("nan")
    a1b = (max(trans) / med) if (trans and med > 1e-9) else float("nan")
    # A2：行走段内相邻位移 cos < -0.5
    rev = 0
    span = 0.0
    for a, b, c in zip(res, res[1:], res[2:]):
        v1 = (b[which][0] - a[which][0], b[which][1] - a[which][1])
        v2 = (c[which][0] - b[which][0], c[which][1] - b[which][1])
        n1 = math.hypot(*v1)
        n2 = math.hypot(*v2)
        if n1 < 1e-4 or n2 < 1e-4:
            continue
        if (v1[0] * v2[0] + v1[1] * v2[1]) / (n1 * n2) < -0.5:
            rev += 1
    span = (res[-1][1] - res[0][1]) / 1000.0
    return a1, a1b, (rev / span * 10.0 if span > 1 else float("nan"))


a1L, a1S, a1bL, a1bS, a2L, a2S = [], [], [], [], [], []
n_units = 0
for uid in sorted(per.keys(), key=lambda x: int(x)):
    res = rebuild(uid)
    if not res:
        continue
    mL = metrics(res, 3)
    mS = metrics(res, 4)
    if not mL or not mS:
        continue
    n_units += 1
    a1L.append(mL[0]); a1S.append(mS[0])
    if mL[1] == mL[1]:
        a1bL.append(mL[1])
    if mS[1] == mS[1]:
        a1bS.append(mS[1])
    if mL[2] == mL[2]:
        a2L.append(mL[2])
    if mS[2] == mS[2]:
        a2S.append(mS[2])
    if mL[0] > TH_A1 or mS[0] > TH_A1:
        print("  id=%-4s A1 linear=%.3f spline=%.3f | A1b lin=%.2f sp=%.2f | A2 lin=%.2f sp=%.2f"
              % (uid, mL[0], mS[0], mL[1], mS[1], mL[2], mS[2]))

print("\n单位数 = %d" % n_units)
for name, L, S, th in (("A1 稳态速度尖峰比", a1L, a1S, TH_A1),
                       ("A1b 部署瞬态比", a1bL, a1bS, TH_A1B),
                       ("A2 反向次数/10s", a2L, a2S, TH_A2)):
    if not L or not S:
        continue
    print("%-18s worst: linear=%7.3f (FAIL=%d/%d)   spline=%7.3f (FAIL=%d/%d)   阈值≤%.2f"
          % (name, max(L), sum(1 for v in L if v > th), len(L),
             max(S), sum(1 for v in S if v > th), len(S), th))
