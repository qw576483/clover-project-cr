#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D129b：从 2.1.5 权威 `.sc` 解析出**每一个视角（`export 名 _N`）自己的 clip**，
产出「目录 × 档位 × 视角 → 单 clip 帧段」的表（TSV + JSON），供 UnitAnimTable 重生成用。

为什么需要这个（根因，见 .ai-tmp/test/D129-unit-recon.md 的 Q2）：
  `策划/单位帧段表.md:2717-2720` 明写「同一组内的 `_1 … _9` = **9 个不同视角**」、
  「每档帧区间 = 该档 9 条 clip 的 shapeID **并集**」，而 `:2758` 的处置建议 ① 是
  **「按 clip 播（推荐）」**。旧数据用了并集 ⇒ 动画会一帧帧"转视角"。
  本脚本把**单条 clip**（= 单一视角、clip 内按时序）的帧号序列原样导出，
  让下游可以"每档只取一个视角"。

数据来源与口径（与 AS1/AT1 完全一致，不新造）：
  · `.sc` = `原版资源/sc/<name>_v215.sc`（QuickBMS 解密后的明文）
  · 逐帧 PNG = `原版资源/cr-assets-png/assets/sc/<dir>/`
  · `frame_NNN` 的 NNN = `.sc` 里 shape 的**记录序**（AT1 口径，见 单位帧段表.md §0）
  · clip 内的 `sids` **就是播放顺序**（`0c` 记录的 shape id 列表，逐帧一条）

复跑（一条命令重出本表）：
  cd <项目根>
  python tools/probes/d129b-clip-segments.py --root . --all
"""

import argparse
import collections
import importlib.util
import json
import os
import re
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

# 与 sc-as1-index.py 逐字一致的档位判定（同一套语义规则；改一处必须同时改另一处）
TIER_OF = [
    ('idle', lambda n: 'idle' in n),
    ('walk', lambda n: 'run' in n or 'walk' in n),
    ('attack', lambda n: 'attack' in n),
    ('die', lambda n: 'die' in n or 'death' in n),
    ('spawn', lambda n: 'spawn' in n or 'deploy' in n),
    ('hit', lambda n: 'hit' in n or 'hurt' in n),
]


def tier_of(name):
    n = name.lower()
    for tier, pred in TIER_OF:
        if pred(n):
            return tier
    return None


def side_of(name):
    """配色侧。原版命名有三套：`enemy_` / `_red_`（红）、`_blue_`（蓝）、无前缀（主套=蓝）。
    ⚠️ 只认 `enemy_` 会把 `princess_red_run1` / `axe_man_red_run1` 这类**红方套**误判成主套，
    下游选视角时会跨配色组取帧（档位间闪色）。"""
    n = name.lower()
    if 'enemy_' in n or '_red_' in n or n.endswith('_red') or n.startswith('red_'):
        return 'red'
    if '_blue_' in n or n.endswith('_blue') or n.startswith('blue_'):
        return 'blue'
    return 'main'   # 主套（蓝方）


def load_parser(root):
    p = os.path.join(root, 'tools', 'probes', 'sc-anim-index.py')
    spec = importlib.util.spec_from_file_location('scanim_d129b', p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def load_resolver(root):
    p = os.path.join(root, 'tools', 'probes', 'sc-at1-resolve.py')
    spec = importlib.util.spec_from_file_location('scat1_d129b', p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def load_any(R, path):
    raw = open(path, 'rb').read()
    if raw[:2] == b'SC':
        try:
            return R.load_sc(path)
        except Exception:
            pass
    return raw


def fmt_ranges(sids):
    """把已排序的帧号列表压成 `a-b c d-e` 形式（与 sc-anim-index.fmt_ranges 同款）。"""
    if not sids:
        return ''
    out, start, prev = [], sids[0], sids[0]
    for s in sids[1:]:
        if s == prev + 1:
            prev = s
            continue
        out.append('%d' % start if start == prev else '%d-%d' % (start, prev))
        start = prev = s
    out.append('%d' % start if start == prev else '%d-%d' % (start, prev))
    return ' '.join(out)


def runs_of(sids):
    """连续段表 [起,长, 起,长, ...]（UnitAnimTable.Clip.Runs 的语义）。"""
    if not sids:
        return []
    out, start, prev = [], sids[0], sids[0]
    for s in sids[1:]:
        if s == prev + 1:
            prev = s
            continue
        out += [start, prev - start + 1]
        start = prev = s
    out += [start, prev - start + 1]
    return out


def viewpoint_no(export_name):
    """末段 `_N` 里的 N（视角号）。判不出 → None（例如 `Knight_attack1` 无 `_N`）。"""
    mm = re.search(r'_(\d+)$', export_name)
    return int(mm.group(1)) if mm else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--dirs', default='chr_',
                    help='只处理目录名前缀匹配的目录（默认 chr_）；传 all 处理全部 *_out')
    ap.add_argument('--out', default='.ai-tmp/test/D129b-clip-segments')
    args = ap.parse_args()

    root = os.path.abspath(args.root)
    R = load_parser(root)
    AT1 = load_resolver(root)
    sc_dir = os.path.join(root, '原版资源', 'sc')
    png_root = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc')

    dirs = []
    for d in sorted(os.listdir(png_root)):
        if not d.endswith('_out'):
            continue
        if args.dirs != 'all' and not d.startswith(args.dirs):
            continue
        dirs.append(d)

    rows = []
    summary = []
    for d in dirs:
        name = d[:-4]
        sc = os.path.join(sc_dir, name + '_v215.sc')
        fd = os.path.join(png_root, d)
        if not os.path.isfile(sc):
            summary.append(dict(dir=d, status='no .sc'))
            continue
        res = R.parse_unit(load_any(R, sc))
        # ⚠️ 必须先用 AT1 的「sid → shape 记录序 + 递归展开嵌套 clip」把 clip 的 id 数组
        #    改写成 `frame_NNN` 的 NNN（否则 minion 这类含嵌套 clip 的目录会给出越界帧号，
        #    例：minion1_run1_9 全帧是对 clip 270 的引用，旧口径下会变成 313 这种 ≥ PNG 数的值）。
        _sid2idx, _unres, dangling_by_clip = AT1.resolve_unit(res)
        mapping, groups = R.analyse(res)
        shape_count = res['ShapeCount']
        png_n = len([f for f in os.listdir(fd) if f.lower().endswith('.png')])
        # 帧号 → PNG 是否存在：逐目录建一次索引（不假设恒等）
        have = set()
        for f in os.listdir(fd):
            mm = re.match(r'^.*_0*(\d+)\.png$', f)
            if mm:
                have.add(int(mm.group(1)))

        n_tier = collections.Counter()
        for m in mapping:
            t = tier_of(m['name'])
            if t is None:
                continue
            sids = list(m['sids'])
            missing = [s for s in sids if s not in have]
            clip = next((c for c in res['clips'] if c['id'] == m['clip_id']), None)
            raw = len(clip['_ids_raw']) if clip is not None else len(sids)
            rows.append(dict(
                dir=d, tier=t, side=side_of(m['name']),
                group=m['name'].rsplit('_', 1)[0], export=m['name'],
                view=viewpoint_no(m['name']), clip_id=m['clip_id'],
                fps=m['fps'], declared_frames=m['frames'],
                n_frames=len(sids),
                n_raw=raw,
                expanded=(raw != len(sids)),
                n_dangling=len(dangling_by_clip.get(m['clip_id'], [])),
                sid_min=min(sids) if sids else None,
                sid_max=max(sids) if sids else None,
                contiguous=(sorted(set(sids)) == list(range(min(sids), max(sids) + 1))) if sids else False,
                sids=sids, runs=runs_of(sids),
                n_missing_png=len(missing)))
            n_tier[t] += 1
        summary.append(dict(dir=d, status='ok', ShapeCount=shape_count, png_n=png_n,
                            equal=(shape_count == png_n),
                            tiers=dict(n_tier)))

    os.makedirs(os.path.dirname(os.path.join(root, args.out)), exist_ok=True)
    tsv_path = os.path.join(root, args.out + '.tsv')
    json_path = os.path.join(root, args.out + '.json')
    with open(tsv_path, 'w', encoding='utf-8', newline='') as f:
        f.write('dir\ttier\tside\tgroup\texport\tview\tclip_id\tfps\tdeclared_frames\t'
                'n_frames\tn_raw\texpanded\tn_dangling\tframe_ranges\truns\tcontiguous\tn_missing_png\n')
        for r in rows:
            f.write('%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%d\t%d\t%s\t%d\t%s\t%s\t%s\t%d\n' % (
                r['dir'], r['tier'], r['side'], r['group'], r['export'],
                '' if r['view'] is None else r['view'], r['clip_id'], r['fps'],
                r['declared_frames'], r['n_frames'], r['n_raw'], r['expanded'], r['n_dangling'],
                fmt_ranges(sorted(set(r['sids']))),
                ','.join(str(x) for x in r['runs']), r['contiguous'], r['n_missing_png']))
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(dict(rows=rows, summary=summary), f, ensure_ascii=False, indent=1)

    print('dirs=%d rows=%d' % (len(dirs), len(rows)))
    print('-> %s' % tsv_path)
    print('-> %s' % json_path)
    bad = [r for r in rows if r['n_missing_png'] > 0]
    print('clip 里有缺 PNG 的行 = %d' % len(bad))
    # 自检 1：每个 (dir,tier) 有多少个"视角"（=多少条 clip）
    per = collections.Counter((r['dir'], r['tier']) for r in rows)
    multi = {k: v for k, v in per.items() if v > 1}
    print('(dir,tier) 有 >1 条 clip 的组数 = %d / %d' % (len(multi), len(per)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
