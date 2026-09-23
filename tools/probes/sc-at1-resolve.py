#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sc-at1-resolve.py -- AT1 判据资产：越界 shapeID 的根因修复（解析层）
====================================================================
根因（AT1 结论，判据见 .ai-tmp/test/AT1-根因与恢复.md）
--------------------------------------------------------
Supercell `.sc` 用**一个共享 id 空间**装三类对象：shape(0x12) / animation(0x0c) /
texture(0x01)。0x0C 记录尾部的 id 数组是**对象 id 列表**，不是「shape 编号」——
里面既可能是 shape，也可能是**嵌套 movieclip**（子动画），于是：

  H1（部分成立）id 是全局计数器的值；`frame_NNNN.png` 的 NNNN 是 **shape 记录的
     文件内序号**，两者只在「id 连续无删减」的文件里相等。id 稀疏时（如
     chr_barbarian: 1029 个 shape 记录，id 最大 1136）必须按 `sid -> 记录序` 查表。
  H2（成立）id 数组里含**嵌套 clip id**（例 chr_minion 的 `minion1_run1_9` 全帧
     就是一个指向 clip 270 的引用），需要**递归展开**成 shape 记录序。
  H3（不成立）PNG dump **不是**按 id 重编号的：目录内文件名是连续 `_sprite_0000..`
     到「记录数-1」（实测 chr_barbarian 1029 张 = 0000..1028，无 1029/1136）。
  H4（成立、且是 H1/H2 的同一条）"另一套 frame 表" 的真身 = 同一个 id 空间里的
     **clip 表**；旧解析把 clip id 当 shape id ⇒ 越界。

本脚本做的事
------------
1. 对每个 `原版资源/cr-assets-png/assets/sc/*_out`：
   - 建 `sid -> shape 记录序` 表 与 `clip_id -> clip` 表；
   - 把每条 clip 的 id 数组**递归展开**成 shape 记录序列表（带环保护、记忆化）；
   - 无法落到任何表里的 id 记为「悬空引用」（单独统计、单独落盘）。
2. 把展开结果**改写回 res['clips'][*]['sids']**（值 = shape 记录序 = `frame_NNNN` 的
   NNNN），随后复用 AS1 的生成链（`--write-docs` 时 monkey-patch `parse_unit` 后
   直接调 `sc-as1-index.py` 的 main），于是 AS1 全部下游（档位、文档、指纹）自动
   按新映射重算 —— **不改 AS1 脚本本身**。
3. 产出：
   - `.ai-tmp/test/AT1-resolve.json`（逐目录：越界前/后、悬空 id、映射表样例）
   - `.ai-tmp/test/AT1-档位可用性.tsv`（新口径，取代 `AS1-档位可用性.tsv`）
   - `--write-docs` 时重出 `策划/单位帧段表.md` / `策划/单位动画分组表.md`

复跑
----
  python tools/probes/sc-at1-resolve.py --root .                 # 只算，落 TSV/JSON
  python tools/probes/sc-at1-resolve.py --root . --write-docs    # 再重出两份策划文档
"""
import argparse
import importlib.util
import json
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

TIER_ORDER = ['idle', 'walk', 'attack', 'die']


def _load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def tier_of(name):
    n = name.lower()
    if 'idle' in n:
        return 'idle'
    if 'run' in n or 'walk' in n:
        return 'walk'
    if 'attack' in n:
        return 'attack'
    if 'die' in n or 'death' in n:
        return 'die'
    if 'spawn' in n or 'deploy' in n:
        return 'spawn'
    if 'hit' in n or 'hurt' in n:
        return 'hit'
    return None


def side_of(name):
    n = name.lower()
    if 'enemy_' in n:
        return '红(enemy_)'
    if '_blue_' in n:
        return '蓝(_blue_)'
    return '蓝(主套)'


def resolve_unit(res):
    """把 res 的每条 clip 的 id 数组展开成 shape 记录序。返回 (sid2idx, 悬空 id 集合)。"""
    sid2idx = {}
    for i, s in enumerate(res['shapes']):
        sid2idx.setdefault(s['sid'], i)
    clip_by_id = {c['id']: c for c in res['clips']}
    unres = set()
    dangling_by_clip = {}
    memo = {}

    def expand(cid, seen):
        if cid in memo:
            return memo[cid]
        c = clip_by_id[cid]
        out, dang = [], []
        for s in c['sids']:
            if s in sid2idx:                    # shape：按记录序取图
                out.append(sid2idx[s])
            elif s in clip_by_id:               # 嵌套 movieclip：递归展开
                if s in seen:
                    continue
                sub, sd = expand(s, seen | {s})
                out += sub
                dang += sd
            else:                               # 悬空（本文件里既无 shape 也无 clip）
                unres.add(s)
                dang.append(s)
        memo[cid] = (out, dang)
        return out, dang

    for c in res['clips']:
        c['_ids_raw'] = list(c['sids'])
        ind, dang = expand(c['id'], {c['id']})
        c['sids'] = ind
        dangling_by_clip[c['id']] = dang
    return sid2idx, unres, dangling_by_clip


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--write-docs', action='store_true',
                    help='复用 AS1 生成链重出 策划/单位帧段表.md + 单位动画分组表.md')
    ap.add_argument('--json', default='.ai-tmp/test/AT1-resolve.json')
    ap.add_argument('--tsv', default='.ai-tmp/test/AT1-档位可用性.tsv')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    R = _load(os.path.join(root, 'tools', 'probes', 'sc-anim-index.py'), 'scanim')
    A = _load(os.path.join(root, 'tools', 'probes', 'sc-as1-index.py'), 'scas1')
    A.load_parser = lambda r: R                      # 让 AS1 的生成链复用同一份解析器
    sc_dir = os.path.join(root, '原版资源', 'sc')
    png_root = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc')

    report, tsv = {}, ['目录\tidle\twalk\tattack\tdie\t三档结论']
    old_c, new_c = {'全': 0, '部': 0, '不可': 0}, {'全': 0, '部': 0, '不可': 0}
    residual = {}
    for d in sorted(x for x in os.listdir(png_root) if x.endswith('_out')):
        name = d[:-4]
        sc = os.path.join(sc_dir, name + '_v215.sc')
        if not os.path.isfile(sc) or os.path.getsize(sc) < 100:
            continue
        res = R.parse_unit(open(sc, 'rb').read())
        SC = res['ShapeCount']
        n_shapes = len(res['shapes'])
        raw_refs = sorted({s for c in res['clips'] for s in c['sids']})
        oob_before = [s for s in raw_refs if s >= SC]
        sid2idx, unres, dangling_by_clip = resolve_unit(res)
        if unres:
            residual[name] = sorted(unres)

        def tier_frames(resx, tier, extra=None):
            sids, dg = [], []
            for c in resx['clips']:
                names = [resx['exp_names'][i] for i, e in enumerate(resx['exp_ids']) if e == c['id']]
                if names and tier_of(names[0]) == tier:
                    sids += c['sids']
                    dg += (extra or {}).get(c['id'], [])
            return sorted(set(sids)), dg

        # 旧口径（id 当记录序）：有 id >= ShapeCount 或无对应文件即不可用
        def old_tier(t):
            sids = []
            for c in res['clips']:
                names = [res['exp_names'][i] for i, e in enumerate(res['exp_ids']) if e == c['id']]
                if names and tier_of(names[0]) == t:
                    sids += c['_ids_raw']
            sids = sorted(set(sids))
            if not sids:
                return '缺', 0, 0
            bad = [s for s in sids if s >= n_shapes]
            return ('OK' if not bad else '越界%d/%d' % (len(bad), len(sids))), len(sids), len(bad)

        cells = []
        states = {}
        for t in TIER_ORDER:
            ov, n, bad = old_tier(t)
            nf, ndg = tier_frames(res, t, dangling_by_clip)
            # 新口径：展开后的记录序必须都 < n_shapes，且该档位没有悬空引用
            ok_new = bool(nf) and not ndg and all(0 <= x < n_shapes for x in nf)
            states[t] = dict(old=ov, new=('OK' if ok_new else ('缺' if not nf else '部分悬空')),
                             frames=len(nf), dangling=len(ndg), oob_old=bad)
            cells.append('缺' if not nf else
                         ('OK' if ok_new else '悬空%d帧/%d帧' % (len(ndg), len(nf))))

        key3 = [cells[i] for i in range(3)]                     # idle/walk/attack
        okey3 = [states[t]['old'] for t in ('idle', 'walk', 'attack')]
        # 与 AS1 同口径：某档位「可用」= 该档位存在且无越界
        o_av = [x == 'OK' for x in okey3]
        o_cls = '全可用' if all(o_av) else ('部分可用' if any(o_av) else '三档全不可用')
        n_av = [states[t]['new'] == 'OK' for t in ('idle', 'walk', 'attack')]
        n_present = [states[t]['frames'] > 0 for t in ('idle', 'walk', 'attack')]
        n_cls = ('全可用' if all(n_av) else ('部分可用' if any(n_av) else
                                             ('部分可用' if any(n_present) else '三档全不可用')))
        old_c[{'全可用': '全', '部分可用': '部', '三档全不可用': '不可'}[o_cls]] += 1
        new_c[{'全可用': '全', '部分可用': '部', '三档全不可用': '不可'}[n_cls]] += 1
        tsv.append('%s\t%s\t%s' % (name, '\t'.join(cells), n_cls))
        report[name] = dict(dir=d, ShapeCount=SC, n_shapes=n_shapes, n_clips=len(res['clips']),
                            raw_refs_max=max(raw_refs) if raw_refs else -1,
                            oob_before=oob_before[:40], n_oob_before=len(oob_before),
                            dangling=residual.get(name, []),
                            sid2idx_sample={str(k): v for k, v in list(sid2idx.items())[:0]},
                            sid_not_idx=[{'sid': s, 'record': sid2idx[s]} for s in sorted(sid2idx)
                                         if sid2idx[s] != s][:20],
                            tiers={t: states[t] for t in TIER_ORDER}, cls_old=o_cls, cls_new=n_cls)

    os.makedirs(os.path.dirname(os.path.join(root, a.json)), exist_ok=True)
    json.dump(report, open(os.path.join(root, a.json), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)
    open(os.path.join(root, a.tsv), 'w', encoding='utf-8').write('\n'.join(tsv) + '\n')
    print('目录 %d ｜ 旧 %s ｜ 新 %s' % (len(report), old_c, new_c))
    print('仍有悬空引用的目录 %d 个：%s' % (len(residual), sorted(residual)))
    print('TSV  -> %s' % a.tsv)
    print('JSON -> %s' % a.json)

    if a.write_docs:
        # monkey-patch：AS1 生成链读到的 clip['sids'] 全部换成「shape 记录序」
        orig = R.parse_unit

        def patched(data):
            res = orig(data)
            resolve_unit(res)
            return res
        R.parse_unit = patched
        A.load_any = lambda RR, path: open(path, 'rb').read()
        # ⛔ 不带 AS1 的 --viewed（那份「看图确认」是在**错的映射**下看的，已失效）
        sys.argv = ['sc-as1-index.py', '--root', root, '--all',
                    '--viewed', '.ai-tmp/test/AT1-viewed-none.json']
        rc = A.main()
        A.load_any = A.__dict__.get('_orig_load_any', A.load_any)
        print('AS1 生成链 rc=%s（策划/单位帧段表.md + 单位动画分组表.md 已按新映射重出）' % rc)
    return 0


if __name__ == '__main__':
    sys.exit(main())
