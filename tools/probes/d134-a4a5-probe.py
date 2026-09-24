#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
d134-a4a5-probe.py —— 把判据 A4（视角立刻切回）/ A5（贴图闪回）从"计数"降到"逐帧序列"，
用来回答一个问题：**这两条 FAIL 是真缺陷，还是判据口径太严**。

为什么要单独开这一个脚本：
  A4/A5 在 `.ai-tmp/test/D134-units.tsv` 上对 **12/12** 个单位全部命中，且命中数 ≈ 帧数-1
  （vflip=1048/1049）。这种"几乎每帧都命中"的形状，既可能是"真在抖"，也可能是
  "一对相邻档位每帧交替"被 lookback≤3 的口径放大。口径之争不能靠嘴，只能把序列打出来看。

输出（每单位）：
  ① vstep 序列的**游程**（连续相同值的长度）+ 交替对统计；判定它是不是"逐帧 ABAB"
  ② sprite 序列的**游程** + 闪回三元组（A,B,A 且 B 游程长度）
  ③ 该单位的 anim 段构成（idle/walk/attack/die 各多少帧）
用法：
  python tools/probes/d134-a4a5-probe.py --root .
"""
from __future__ import print_function
import os
import sys
import math
import argparse


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
            rows.append(dict(zip(head, p)))
    return head, rows


def inum(d, k, default=-9999):
    v = d.get(k)
    if v is None or v == '':
        return default
    try:
        return int(float(v))
    except ValueError:
        return default


def runs(seq):
    """→ [(值, 起始idx, 长度)]"""
    out = []
    i = 0
    while i < len(seq):
        j = i
        while j < len(seq) and seq[j] == seq[i]:
            j += 1
        out.append((seq[i], i, j - i))
        i = j
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--top', type=int, default=6)
    a = ap.parse_args()
    up = os.path.join(a.root, '.ai-tmp', 'test', 'D134-units.tsv')
    if not os.path.exists(up):
        print('[BLOCKED] 缺 %s' % up)
        return 3
    head, rows = read_tsv(up)
    print('输入 %s：行=%d 列=%d' % (up, len(rows), len(head)))
    print('列序：%s' % ' | '.join(head))
    print()

    g = {}
    for r in rows:
        g.setdefault(inum(r, 'id'), []).append(r)
    for k in g:
        g[k].sort(key=lambda x: inum(x, 'frame'))

    # 按采样帧数排，取前 top 个
    order = sorted(g.items(), key=lambda kv: -len(kv[1]))[:a.top]
    for uid, recs in order:
        dirn = recs[0].get('dir', '?')
        vs = [inum(r, 'vstep') for r in recs]
        vno = [inum(r, 'vno') for r in recs]
        anims = [inum(r, 'anim') for r in recs]
        sps = [(r.get('sprite') or '').strip() for r in recs]
        frs = [inum(r, 'frame') for r in recs]

        print('=' * 100)
        print('id=%d  dir=%s  帧数=%d  frame %d..%d' % (uid, dirn, len(recs), frs[0], frs[-1]))
        # anim 构成
        ac = {}
        for x in anims:
            ac[x] = ac.get(x, 0) + 1
        print('  anim 构成：%s   （0=idle 1=walk 2=attack 3=die）' % sorted(ac.items()))

        # --- vstep ---
        rv = runs(vs)
        uniq_v = sorted(set(vs))
        len1 = sum(1 for _, _, L in rv if L == 1)
        print('  vstep: 取值集=%s  游程数=%d  其中长度=1的游程=%d (%.1f%%)  最长游程=%d'
              % (uniq_v, len(rv), len1, 100.0 * len1 / max(1, len(rv)), max(L for _, _, L in rv)))
        print('        前 40 帧 vstep 序列：%s' % vs[:40])
        print('        前 40 帧 vno   序列：%s' % vno[:40])
        if len(rv) <= 40:
            print('        全部游程 (值,起,长)：%s' % rv)
        else:
            print('        前 25 个游程 (值,起,长)：%s' % rv[:25])
        # 交替对：av, bv 相邻两游程组成的有序对频次
        pairs = {}
        for (va, _, _), (vb, _, _) in zip(rv, rv[1:]):
            pairs[(va, vb)] = pairs.get((va, vb), 0) + 1
        print('        相邻游程对（前 10）：%s' % sorted(pairs.items(), key=lambda kv: -kv[1])[:10])

        # --- sprite ---
        rs = runs(sps)
        uniq_s = sorted(set(sps))
        len1s = sum(1 for _, _, L in rs if L == 1)
        print('  sprite: 不同贴图数=%d  游程数=%d  长度=1的游程=%d (%.1f%%)  最长游程=%d'
              % (len(uniq_s), len(rs), len1s, 100.0 * len1s / max(1, len(rs)), max(L for _, _, L in rs)))
        print('        前 60 帧 sprite：%s' % sps[:60])
        # A→B→A 三元组：游程三元组 i,i+1,i+2 且 v[i]==v[i+2]
        tri = []
        for (v0, i0, l0), (v1, i1, l1), (v2, i2, l2) in zip(rs, rs[1:], rs[2:]):
            if v0 == v2 and v0 != v1:
                tri.append((i0, v0, l1, v1))
        print('        A→B→A 三元组数=%d（B 游程长度分布：%s）'
              % (len(tri), sorted(set(t[2] for t in tri))))
        for t in tri[:8]:
            print('           @idx=%d  A=%s  夹了 %d 帧的 B=%s' % t)
        # 同名贴图重复出现（非相邻）次数
        revisit = {}
        for (v, i, L) in rs:
            revisit[v] = revisit.get(v, 0) + 1
        multi = sorted([(k, c) for k, c in revisit.items() if c > 1], key=lambda kv: -kv[1])
        print('        被重复使用的贴图（出现游程数>1）前 10：%s' % multi[:10])
        print()

    return 0


if __name__ == '__main__':
    sys.exit(main())
