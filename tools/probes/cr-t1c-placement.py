#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-t1c-placement.py -- CR-T1c 判据资产：解 `building_tower_v215.sc` 的 **placement**
=================================================================================
要做的事：把每条 `0c`(movieclip) 记录里「子件的放置信息」解出来，得到每一层的
**x / y 偏移、缩放、以及层序**，从而不再用「bbox 对齐」这种降级量法。

已知（本脚本打印、不靠记忆）
----------------------------
* 文件头自带 `MatrixCount` / `ColorTransCount` —— 矩阵表**存在**，但要看它以什么形式存。
* 本脚本把每条 `0c` 的 payload 按已知字段解析后，**打印剩余字节**（hex），
  并打印 header 的几个 count ⇒ 判断矩阵表是"独立的 08 记录"还是"藏在 0c payload 尾部"。
* 同时对指定 clip（默认 307=KingTower_red / 308=KingTower_blue / 247=turret /
  985=princess_tower_idle1_1 所在文件另跑）打印：cnt1 / cnt2 / 三元组前若干 / 子件表。

复跑
----
  python tools/probes/cr-t1c-placement.py --root . --unit building_tower
  python tools/probes/cr-t1c-placement.py --root . --unit chr_princess --clip 985
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
    """`原版资源/sc/*.sc` 是**已解压**的裸 `.sc`（与本项目其它 probe 口径一致：直接 parse_unit(raw)）。
    若拿到的是带 `SC` 头的压缩包，才走 lzma 那条路。"""
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
    ap.add_argument('--unit', default='building_tower')
    ap.add_argument('--clip', default='307,308,247,245,252,295,283')
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
    print('HEADER', hdr, 'exports', ExportCount)
    print('  ⇒ MatrixCount=%d / ColorTransCount=%d（表非空 ⇒ 放置矩阵确实存在）'
          % (hdr['MatrixCount'], hdr['ColorTransCount']))

    hist = collections.Counter()
    recs = []          # (tag, pos, size, payload)
    while len(data) - S.tell() > 0:
        pos = S.tell()
        tag = S.read(1).hex()
        size = S.read_u32()
        payload = S.read(size)
        hist[tag] += 1
        recs.append((tag, pos, size, payload))
    print('TAG_HIST', dict(hist))
    print('  ⇒ 若 08 计数为 0，矩阵必在 0c payload 尾部（下一步打印剩余字节）')

    mats = [(pos, size, payload) for tag, pos, size, payload in recs if tag == '08']
    cts = [(pos, size, payload) for tag, pos, size, payload in recs if tag == '09']
    print('\n--- 0x08 矩阵记录：%d 条（== MatrixCount）---' % len(mats))
    print('  尺寸直方图 = %s' % (collections.Counter(s for _, s, _ in mats),))
    for i in (0, 1, 2, 88, 89, 90, 91, 92):
        if i < len(mats):
            print('  matrix[%d] size=%-3d hex=%s' % (i, mats[i][1], mats[i][2].hex()))
    print('--- 0x09 颜色变换记录：%d 条（== ColorTransCount）---' % len(cts))
    print('  尺寸直方图 = %s' % (collections.Counter(s for _, s, _ in cts),))
    for i in (0, 6):
        if i < len(cts):
            print('  ct[%d] size=%-3d hex=%s' % (i, cts[i][1], cts[i][2].hex()))

    def mat_of(idx):
        if idx == 65535 or idx >= len(mats):
            return None
        b = mats[idx][2]
        v = [int.from_bytes(b[i * 4:i * 4 + 4], 'little', signed=True) for i in range(6)]
        return dict(a=v[0], b=v[1], c=v[2], d=v[3], tx=v[4], ty=v[5],
                    sx=v[0] / 1024.0, sy=v[3] / 1024.0,
                    px=v[4] / 20.0, py=v[5] / 20.0)     # tx/ty 单位 = twips(1/20 px)

    def ct_of(idx):
        if idx == 65535 or idx >= len(cts):
            return None
        b = cts[idx][2]
        return dict(add=(b[0], b[1], b[2], b[3]), mult=(b[4], b[5], b[6]),
                    identity=(b[0:4] == b'\x00\x00\x00\x00' and b[4:7] == b'\xff\xff\xff'))

    want = {int(x) for x in a.clip.split(',') if x.strip()}
    for tag, pos, size, payload in recs:
        if tag != '0c':
            continue
        B = R(payload)
        cid = B.read_u16()
        if cid not in want:
            continue
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
        left = payload[B.tell():]
        print('\n=== clip %d  pos=%d size=%d fps=%s frames=%s cnt1=%d cnt2=%s'
              % (cid, pos, size, fps, frames, cnt1, cnt2))
        print('  childTable ids=%s' % (sids,))
        print('  childTable names=%s' % (names,))
        print('  opacity=%s' % (opac,))
        print('  triples[0:%d]=%s' % (min(len(trip), 24), trip[:24]))
        print('  LEFTOVER bytes=%d  hex[0:120]=%s' % (len(left), left[:120].hex()))

        # ★ 第 1 帧的「绘制顺序 + 矩阵 + 颜色变换」——这就是层级配方
        n0 = 0
        while n0 < len(trip) and (n0 == 0 or trip[n0][0] != 0):
            n0 += 1
        print('  ---- 第 1 帧配方（顺序 = 绘制顺序；索引 → childTable 的 id）----')
        for k, (ci, mi, ti) in enumerate(trip[:max(1, n0)]):
            kid = sids[ci] if 0 <= ci < len(sids) else None
            nm = names[ci] if 0 <= ci < len(names) else None
            print('    #%d childTable[%d] id=%-5s name=%-12s matrix=%-6s ct=%-5s  %s'
                  % (k, ci, kid, nm, mi, ti,
                     ('无矩阵(画布原位)' if mi == 65535 else
                      ('scale=(%.3f,%.3f) dPx=(%.2f,%.2f)' % (mat_of(mi)['sx'], mat_of(mi)['sy'],
                                                              mat_of(mi)['px'], mat_of(mi)['py'])))))
            if ti != 65535:
                print('        ct: %s' % (ct_of(ti),))
    return 0


if __name__ == '__main__':
    sys.exit(main())
