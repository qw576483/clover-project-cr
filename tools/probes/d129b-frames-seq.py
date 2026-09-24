# -*- coding: utf-8 -*-
"""
d129b-frames-seq.py -- 把**已装机的** `UnitAnimTable.cs` 展开成「逐帧序列」离线读数
====================================================================================
用途：D129b 硬要求②要的「动画帧序列离线读数」。
  · 真机 Play 里对局是**随机卡组**，14 次采样只出现 giant / minion / pekka（见
    `.ai-tmp/test/D129b-live-evidence.txt`），knight / musketeer / archer 没被抽到。
  · 按 skill 的「能离线判的不许进 Play」原则，这些目录的逐帧序列**用装机表离线展开**即可，
    而且这才是"客户端真正会播的那串帧"（= 装机数据的直接展开，不含任何推测）。

口径：直接复用装机表的 `Runs`（[起始帧号, 长度, ...]）按序展开 ⇒ 帧号列表。
      断言：展开出的每一帧都必须落在「该档 Runs 所属的那一条 clip」的 `frame_ranges` 内
      （判据数据 = `.ai-tmp/test/D129b-clip-segments.tsv`，由生成链第①步从 2.1.5 权威 `.sc` 解出）。

产出：`.ai-tmp/test/D129b-frames-seq.tsv`
      dir / tier / scFps / hitSpeedMs / n_frames / frames(逗号分隔) / source

复跑：
  python tools/probes/d129b-frames-seq.py --root .
"""
import argparse
import csv
import importlib.util
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))


def load_gen():
    """复用生成器里的 parse_table（保证与断言同一套解析口径）。"""
    p = os.path.join(HERE, 'd129b-gen-anim-table.py')
    spec = importlib.util.spec_from_file_location('d129b_gen', p)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def expand(runs):
    """Runs = (start, count, start, count, ...) → 逐帧列表（按 Runs 顺序）。"""
    out = []
    v = list(runs)
    for i in range(0, len(v), 2):
        s, n = v[i], v[i + 1]
        out.extend(range(s, s + n))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--table', default='client/Assets/Scripts/View/UnitAnimTable.cs')
    ap.add_argument('--segments', default='.ai-tmp/test/D129b-clip-segments.tsv')
    ap.add_argument('--out', default='.ai-tmp/test/D129b-frames-seq.tsv')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    G = load_gen()
    tbl = G.parse_table(os.path.join(root, a.table))

    # 允许的帧号集合：dir → tier → 该档所有 clip 的帧号并集（用于校验）
    allow = {}
    with open(os.path.join(root, a.segments), encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            if r['tier'] not in G.TIERS:
                continue
            frames = set()
            # frame_ranges 口径：空格分隔的若干段；一段为 `lo-hi`（连续）或单个帧号。
            for part in (r['frame_ranges'] or '').split():
                part = part.strip()
                if not part:
                    continue
                if '-' in part:
                    lo, hi = part.split('-', 1)
                    frames.update(range(int(lo), int(hi) + 1))
                elif part.isdigit():
                    frames.add(int(part))
            allow.setdefault(r['dir'], {}).setdefault(r['tier'], set()).update(frames)

    rows, bad = [], []
    for d, tiers in tbl.items():
        sc = 0
        for t in G.TIERS:
            runs = tiers.get(t)
            if not runs:
                continue                      # Known=false → 无帧段
            fr = expand(runs)
            if d not in G.PRESERVE_AS_IS:
                out_of = [x for x in fr if x not in allow.get(d, {}).get(t, set())]
                if out_of:
                    bad.append((d, t, out_of[:8]))
            rows.append((d, t, len(fr), fr, runs))

    outp = os.path.join(root, a.out)
    with open(outp, 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['dir', 'tier', 'n_frames', 'frames', 'runs', 'in_clip'])
        for d, t, n, fr, runs in rows:
            ok = all(x in allow.get(d, {}).get(t, set()) for x in fr) or d in G.PRESERVE_AS_IS
            w.writerow([d, t, n, ','.join(str(x) for x in fr),
                        ','.join(str(x) for x in runs), 'yes' if ok else 'NO'])
    print('写出 -> %s（%d 行）' % (outp, len(rows)))

    # 抽样打印本片关心的 5 个目录
    focus = ('chr_knight_out', 'chr_musketeer_out', 'chr_archer_out', 'chr_minion_out', 'chr_giant_out')
    for d, t, n, fr, runs in rows:
        if d in focus:
            print('  %-20s %-6s n=%2d  %s' % (d, t, n, ','.join(str(x) for x in fr)))
    if bad:
        print('!! 有帧号落在该档任何 clip 之外（%d 处）:' % len(bad))
        for d, t, o in bad[:10]:
            print('   %s/%s -> %s' % (d, t, o))
        return 1
    print('校验：所有展开帧号都落在该档 clip 的帧段内 → PASS')
    return 0


if __name__ == '__main__':
    sys.exit(main())
