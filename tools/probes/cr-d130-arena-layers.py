#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d130-arena-layers.py -- D130 判据资产：解 `arena_training_v215.sc` 的**层配方**
=================================================================================
目标（用户判词 6：「地图不对，路连不上」）
------------------------------------------
把原版竞技场地面**逐层的绘制顺序 / 每层用哪个 shape / 每层的放置矩阵（位置+缩放）**
从 .sc 里读出来，不靠目测。

已知结构（本脚本自己打印）
--------------------------
* `arena_training_v215.sc` 是**裸** .sc（非 SC 压缩）；`arena_training.sc` 是 SC 压缩包。
* 导出表 23 条：id 48..23 ↔ training_area_bg / fence_* / cliff01..07 / tree* /
  atlasgenerator_texture_rgb565 / training_cliffs_* / bush* / training_deco1..2
  ⇒ 每一条导出 = 一个 `0c`(movieclip) 记录；子件放在每帧的 (childIndex,matrixId,ctId) 三元组里。
* `arena_training_out/arena_training_sprite_NNN.png` 是**按 shape 记录序**编号的、
  画布尺寸 1090x1677 的整幅层图（透明处 = 该层不画）。

复跑
----
  C:/Python312/python tools/probes/cr-d130-arena-layers.py --root .
"""
import argparse
import collections
import io
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass


class R(io.BytesIO):
    def read_byte(self):
        return int.from_bytes(self.read(1), 'little')

    def read_u16(self):
        return int.from_bytes(self.read(2), 'little')

    def read_i16(self):
        return int.from_bytes(self.read(2), 'little', signed=True)

    def read_u32(self):
        return int.from_bytes(self.read(4), 'little')

    def read_i32(self):
        return int.from_bytes(self.read(4), 'little', signed=True)


def load_sc(fn):
    raw = open(fn, 'rb').read()
    if raw[:2] == b'\x53\x43':
        import lzma
        off = 10 + int.from_bytes(raw[6:10], 'big')
        d = raw[off:]
        return lzma.LZMADecompressor().decompress(d[0:9] + b"\x00" * 4 + d[9:])
    return raw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--unit', default='arena_training')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    data = load_sc(os.path.join(root, '原版资源', 'sc', a.unit + '_v215.sc'))
    S = R(data)
    hdr = dict(ShapeCount=S.read_u16(), TotalsAnim=S.read_u16(), TotalsTex=S.read_u16(),
               TextFieldCount=S.read_u16(), MatrixCount=S.read_u16(), ColorTransCount=S.read_u16())
    S.read(5)
    ExportCount = S.read_u16()
    exp_ids = [S.read_u16() for _ in range(ExportCount)]
    exp_names = []
    for _ in range(ExportCount):
        n = S.read_byte()
        exp_names.append(S.read(n).decode('utf-8', 'replace'))
    print('HEADER %s exports=%d' % (hdr, ExportCount))

    recs = []
    while len(data) - S.tell() > 0:
        pos = S.tell()
        tag = S.read(1).hex()
        size = S.read_u32()
        payload = S.read(size)
        recs.append((tag, pos, size, payload))
    print('TAG_HIST %s' % dict(collections.Counter(t for t, _, _, _ in recs)))

    mats = [p for t, _, _, p in recs if t == '08']
    print('matrix records=%d (== MatrixCount %d)' % (len(mats), hdr['MatrixCount']))

    def mat_of(idx):
        if idx == 65535 or idx >= len(mats):
            return None
        b = mats[idx]
        v = [int.from_bytes(b[i * 4:i * 4 + 4], 'little', signed=True) for i in range(6)]
        return dict(sx=v[0] / 1024.0, sy=v[3] / 1024.0,
                    px=v[4] / 20.0, py=v[5] / 20.0, raw=v)

    clips = {}
    for tag, pos, size, payload in recs:
        if tag != '0c':
            continue
        B = R(payload)
        cid = B.read_u16()
        fps = B.read_byte()
        frames = B.read_u16()
        cnt1 = B.read_i32()
        trip = [(B.read_u16(), B.read_u16(), B.read_u16()) for _ in range(max(0, cnt1))]
        cnt2 = B.read_i16()
        sids = [B.read_i16() for _ in range(max(0, cnt2))]
        opac = [B.read_byte() for _ in range(max(0, cnt2))]
        names = []
        for _ in range(max(0, cnt2)):
            L = B.read_byte()
            names.append(None if L >= 255 else B.read(L).decode('utf-8', 'replace'))
        clips[cid] = dict(id=cid, fps=fps, frames=frames, trip=trip, sids=sids,
                          names=names, opac=opac, pos=pos, size=size)

    print('\nclips=%d ids=%s' % (len(clips), sorted(clips)))

    # 每个 export 的逐帧配方
    for nm, cid in zip(exp_names, exp_ids):
        c = clips.get(cid)
        if c is None:
            print('!! export %s -> clip %d 不存在' % (nm, cid))
            continue
        print('\n=== export %-30s clip=%d fps=%s frames=%s children=%d triples=%d'
              % (nm, cid, c['fps'], c['frames'], len(c['sids']), len(c['trip'])))
        print('    childTable ids = %s' % (c['sids'],))
        print('    childTable names = %s' % (c['names'],))
        # 帧边界
        bounds = [i for i, t in enumerate(c['trip']) if t[0] == 0 and i != 0]
        print('    frame starts(by childIndex==0) = %s' % (bounds,))
        # 打印前 12 条三元组（含矩阵）
        lim = min(len(c['trip']), 12)
        for k in range(lim):
            ci, mi, ti = c['trip'][k]
            kid = c['sids'][ci] if 0 <= ci < len(c['sids']) else None
            m = mat_of(mi)
            print('      #%-3d child[%d]->shape=%-4s M=%-5s ct=%-6s %s'
                  % (k, ci, kid, mi, ti,
                     'canvas-origin' if m is None else
                     'scale=(%.4f,%.4f) d=(%.2f,%.2f) raw=%s' % (m['sx'], m['sy'], m['px'], m['py'], m['raw'])))
        if len(c['trip']) > lim:
            # 也打印最后 4 条
            for k in range(max(lim, len(c['trip']) - 4), len(c['trip'])):
                ci, mi, ti = c['trip'][k]
                kid = c['sids'][ci] if 0 <= ci < len(c['sids']) else None
                m = mat_of(mi)
                print('      #%-3d child[%d]->shape=%-4s M=%-5s ct=%-6s %s'
                      % (k, ci, kid, mi, ti,
                         'canvas-origin' if m is None else
                         'scale=(%.4f,%.4f) d=(%.2f,%.2f) raw=%s' % (m['sx'], m['sy'], m['px'], m['py'], m['raw'])))
    return 0


if __name__ == '__main__':
    sys.exit(main())
