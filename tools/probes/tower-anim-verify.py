#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""离线复检：把**生成物** `client/Assets/Scripts/View/TowerAnimTable.cs` 反解回逐帧序列，
与 `.sc` 的 `0x0C` 放置表逐帧比对（fps / 总帧数 / 每帧 shape / 每帧 sy,dy）。

用法: python tools/probes/tower-anim-verify.py
判据: 每条断言 PASS 且 summary FAIL=0。
"""
import importlib.util as u
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
CS = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'View', 'TowerAnimTable.cs')
SCDIR = os.path.join(ROOT, '原版资源', 'sc')


def _load(n, rel):
    s = u.spec_from_file_location(n, os.path.join(ROOT, rel))
    m = u.module_from_spec(s)
    s.loader.exec_module(m)
    return m


spc = _load('spc', r'tools\probes\sc-placement.py')
scai = _load('scai', r'tools\probes\sc-anim-index.py')


def derive(scfile, clip_id):
    path = os.path.join(SCDIR, scfile)
    res = spc.parse_sc(path)
    res2 = scai.parse_unit(spc.load_sc(path))
    clip = next(c for c in res['clips'] if c['id'] == clip_id)
    sids = next(list(c['sids']) for c in res2['clips'] if c['id'] == clip_id)
    seq = []
    for (ci, mi, _ct) in clip['triples']:
        shape = sids[ci] if ci < len(sids) else None
        sub = next((list(c['sids']) for c in res2['clips'] if c['id'] == shape), None)
        if sub:
            shape = sub[0]
        sy, dy = 1.0, 0.0
        if mi != 65535:
            a, b, cc, d, tx, ty = res['matrices'][mi]
            sy, dy = d / 1024.0, ty / 20.0
        seq.append((shape, round(sy, 6), round(dy, 6)))
    return clip['fps'], clip['frames'], seq


def parse_cs(path):
    """从 C# 生成物里取出 {字段名: (fps, [(frame, repeat, sy, dy)])}。"""
    src = open(path, encoding='utf-8').read()
    out = {}
    for m in re.finditer(r'private static readonly Anim (\w+) = new Anim\((\d+), new\[\]\s*\{(.*?)\}\)', src, re.S):
        name, fps, body = m.group(1), int(m.group(2)), m.group(3)
        keys = [(int(a), int(b), float(c.rstrip('f')), float(d.rstrip('f')))
                for a, b, c, d in re.findall(r'new Key\((-?\d+),\s*(\d+),\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\)', body)]
        out[name] = (fps, keys)
    return out


FAIL = 0


def chk(cond, msg):
    global FAIL
    print(('  PASS  ' if cond else '  FAIL  ') + msg)
    if not cond:
        FAIL += 1


def main():
    tables = parse_cs(CS)
    print('C# 生成物里解出 %d 条动画: %s' % (len(tables), sorted(tables)))
    cases = [('KingRed', 'building_tower_v215.sc', 244),
             ('KingBlue', 'building_tower_v215.sc', 251),
             ('OccupantRed', 'chr_princess_v215.sc', 753),
             ('OccupantBlue', 'chr_princess_v215.sc', 981)]
    for field, scfile, cid in cases:
        fps, frames, seq = derive(scfile, cid)
        chk(field in tables, '%s 在生成物里存在' % field)
        if field not in tables:
            continue
        cs_fps, keys = tables[field]
        chk(cs_fps == fps, '%s fps: C#=%d == .sc=%d' % (field, cs_fps, fps))
        total = sum(k[1] for k in keys)
        chk(total == frames, '%s 总帧数: C# 段和=%d == .sc frames=%d' % (field, total, frames))
        # 生成物按 4 位小数落盘（`%.4f`）⇒ 比对时同口径取整，避免"四舍五入"被当成不符。
        seq = [(f, round(sy, 4), round(dy, 4)) for (f, sy, dy) in seq]
        expanded = []
        for (f, r, sy, dy) in keys:
            for _ in range(r):
                expanded.append((f, round(sy, 4), round(dy, 4)))
        chk(len(expanded) == len(seq), '%s 展开帧数 %d == .sc placement 数 %d' % (field, len(expanded), len(seq)))
        bad = [i for i in range(min(len(expanded), len(seq))) if expanded[i] != seq[i]]
        chk(not bad, '%s 逐帧逐值一致（首个不符=%s）' % (field, bad[:3] or '无'))
        print('        keys=%d 首键=%s 末键=%s shapes=%s'
              % (len(keys), keys[0], keys[-1], sorted({k[0] for k in keys})))
    print('---- summary FAIL=%d ----' % FAIL)
    return 0 if FAIL == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
