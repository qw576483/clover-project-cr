# cr-d130-sc-place.py -- 把 arena_training_v215.sc 的**放置矩阵**解出来（复用 tools/probes/sc-layout.py 的
# parse/placements，口径见 策划/原版UI布局坐标.md §1：tag08 = 6×int32，a,b,c,d ÷1024、tx,ty ÷20）。
#
# 目的（用户判词 6 ①「河道上下不对称」/ ②「木栏重复」的**权威依据**）：
#   .sc 的 23 个导出里有 training_cliffs_left/right、fence_left_front/back、fence_right_front/back
#   ⇒ 原版是**把灰石/木栏当独立元件摆上去的**。本脚本给出它们各自的 (tx,ty,sx,sy) 与所在的父 clip。
# 只读；不改任何产品文件。
import importlib.util
import os
import struct
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
spec = importlib.util.spec_from_file_location('sl', os.path.join(ROOT, 'tools', 'probes', 'sc-layout.py'))
sl = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sl)

SCPATH = os.path.join(ROOT, '原版资源', 'sc', 'arena_training_v215.sc')
raw = open(SCPATH, 'rb').read()
print("sc = %s  bytes=%d  head=%s" % (SCPATH, len(raw), raw[:2]))

data = sl.load.__wrapped__ if hasattr(sl.load, '__wrapped__') else None
if raw[:2] == b'\x53\x43':
    data = sl.sai.lzma_hack(raw[10 + int.from_bytes(raw[6:10], 'big'):])
else:
    data = raw

res = sl.parse(data)
h = res['hdr']
print("hdr = %s" % h)
print("矩阵条数(tag08) = %d   shape 记录(tag12) = %d   clip(0c) = %d   export = %d"
      % (len(res['mats']), len(res['sids']), len(res['clips']), len(res['exp_names'])))
print()

print("=== 导出表 (export name -> clip id) ===")
pair = list(zip(res['exp_names'], res['exp_ids']))
for i, (nm, cid) in enumerate(pair):
    c = res['clips'].get(cid)
    print("  [%2d] %-38s clip=%-4s frames=%s children=%s"
          % (i, nm, cid, (c['frames'] if c else '?'), (len(c['children']) if c else '?')))

clip_ids = set(res['clips'])
export_cids = set(res['exp_ids'])
print()
print("=== 各 clip 的 children / 放置 ===")
for cid in sorted(res['clips']):
    c = res['clips'][cid]
    names = [x[1] for x in c['children']]
    tris = c['tris']
    uniq = []
    seen = set()
    for (ci, mi, coi) in tris:
        if ci in seen:
            continue
        seen.add(ci)
        uniq.append((ci, mi, coi))
    print("clip %-4d fps=%-3d frames=%-3d cnt1=%-3d cnt2=%-3d uniq=%-3d  names=%s"
          % (cid, c['fps'], c['frames'], len(tris), len(c['children']), len(uniq), names))
    for (ci, mi, coi) in uniq:
        if ci >= len(c['children']):
            print("      !child oob %d" % ci)
            continue
        oid, nm, op = c['children'][ci]
        m = sl.matrix(res['mats'], mi)
        kind = 'clip' if oid in clip_ids else ('shape' if oid in res['sids'] else '?')
        ex = ''
        if oid in export_cids:
            ex = '  <= EXPORT ' + res['exp_names'][res['exp_ids'].index(oid)]
        print("      child=%-3d obj=%-5d %-5s op=%-3d  sx=%.4f sy=%.4f tx=%8.3f ty=%8.3f (raw=%s)%s"
              % (ci, oid, kind, op, m[0] / sl.SCALE_DIV, m[3] / sl.SCALE_DIV,
                 m[4] / sl.TRANS_DIV, m[5] / sl.TRANS_DIV, m, ex))
