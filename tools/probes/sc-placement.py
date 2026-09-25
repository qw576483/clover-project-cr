#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Supercell `.sc` placement-matrix 解析器（离线，零依赖除 stdlib）。

用途
----
把 `.sc` 里的两类记录解出来：
  1) tag `0x08` = **仿射矩阵表**（每条 payload = 24 字节 = 6 × i32 = a,b,c,d,tx,ty）；
     `a/d` = 1/1024 定点缩放（1024 = 1.0），`b/c` = 旋转/错切，`tx/ty` = twips（1/20 画布像素）。
  2) tag `0x0c` = **动画 clip**，其中 cnt1 条 (childIndex, matrixId, ctId) 三元组 = 逐帧放置表。
     65535 = 无矩阵 / 无颜色变换。

格式出处：`client/Assets/Scripts/View/ArenaView.cs` 的「层级配方：来自 `.sc` 的 placement（CR-T1c 解出）」
段 —— 该段逐字写明 `0x08` = 24 字节 6×i32、`a/d` 是 1/1024、`tx/ty` 是 twips、65535 = 无矩阵。
本脚本按同一口径实现，并用该段点名的已知矩阵做正向对照（见 --selftest）。

复跑
----
  C:\\Python312\\python.exe tools/probes/sc-placement.py --selftest
  C:\\Python312\\python.exe tools/probes/sc-placement.py --matrix <file.sc> --dump
  C:\\Python312\\python.exe tools/probes/sc-placement.py --clips <file.sc>
  C:\\Python312\\python.exe tools/probes/sc-placement.py --batch <dir>
"""

import argparse
import collections
import lzma
import os
import sys


# ----------------------------------------------------------------------------
# 读流
# ----------------------------------------------------------------------------
class R:
    def __init__(self, d):
        self.d = d
        self.i = 0

    def read(self, n):
        b = self.d[self.i:self.i + n]
        self.i += n
        return b

    def tell(self):
        return self.i

    def remain(self):
        return len(self.d) - self.i

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


def lzma_hack(d):
    # supercell 的 lzma 头少了 4 字节 uncompressed-size（补 0）
    d = d[0:9] + b"\x00" * 4 + d[9:]
    return lzma.LZMADecompressor().decompress(d)


def load_sc(fn):
    """SC 头 + lzma 压缩体 ⇒ 解压；否则按原样返回（本工程 `原版资源/sc/*.sc` 是未压缩的裸体）。"""
    raw = open(fn, 'rb').read()
    if raw[:2] == b'\x53\x43':                     # 'SC'
        off = 10 + int.from_bytes(raw[6:10], 'big')
        return lzma_hack(raw[off:])
    return raw


# ----------------------------------------------------------------------------
# 解析
# ----------------------------------------------------------------------------
def parse_sc(fn):
    """返回 dict(matrices, clips, exp_ids, exp_names, counts, tag_hist)。"""
    data = load_sc(fn)
    S = R(data)

    ShapeCount = S.read_u16()
    TotalsAnim = S.read_u16()
    TotalsTex = S.read_u16()
    TextFieldCount = S.read_u16()
    MatrixCount = S.read_u16()
    ColorTransCount = S.read_u16()
    S.read(5)

    ExportCount = S.read_u16()
    exp_ids = [S.read_u16() for _ in range(ExportCount)]
    exp_names = []
    for _ in range(ExportCount):
        n = S.read_byte()
        exp_names.append(S.read(n).decode('utf-8', 'replace'))

    matrices = []          # list of (a,b,c,d,tx,ty)，顺序即 matrixId
    clips = []             # list of dict
    shapes = []            # list of dict(idx, sid, nreg, npts, ext, tex)，顺序即 frame_NNN
    sheets = []            # 纹理图集尺寸 (w,h) px，顺序即 sheetID
    tag_hist = collections.Counter()
    unknown = []

    while S.remain() > 0:
        pos = S.tell()
        tag = S.read(1).hex()
        size = S.read_u32()
        tag_hist[tag] += 1
        payload = S.read(size)

        if tag == '00':
            continue
        if tag == '08':
            B = R(payload)
            matrices.append(tuple(B.read_i32() for _ in range(6)))
            continue
        if tag == '0c':
            B = R(payload)
            cid = B.read_u16()
            fps = B.read_byte()
            frames = B.read_u16()
            cnt1 = B.read_i32()
            triples = [(B.read_u16(), B.read_u16(), B.read_u16()) for _ in range(cnt1)]
            clips.append(dict(id=cid, fps=fps, frames=frames, triples=triples))
            continue
        if tag == '12':
            B = R(payload)
            sid = B.read_u16()
            nreg = B.read_u16()
            npts = B.read_u16()
            ext = None
            tex = None
            for _ in range(nreg):
                if B.read(1).hex() != '16':
                    break
                B.read_u32()                       # 区域块 size（未用）
                sheet_id = B.read_byte()
                np_r = B.read_byte()
                for _ in range(np_r):
                    x, y = B.read_i32(), B.read_i32()
                    if ext is None:
                        ext = [x, y, x, y]
                    else:
                        ext[0] = min(ext[0], x); ext[1] = min(ext[1], y)
                        ext[2] = max(ext[2], x); ext[3] = max(ext[3], y)
                sw, sh = sheets[sheet_id] if sheet_id < len(sheets) else (0, 0)
                for _ in range(np_r):
                    sx = B.read_u16() * sw / 65535.0
                    sy = B.read_u16() * sh / 65535.0
                    if tex is None:
                        tex = [sx, sy, sx, sy]
                    else:
                        tex[0] = min(tex[0], sx); tex[1] = min(tex[1], sy)
                        tex[2] = max(tex[2], sx); tex[3] = max(tex[3], sy)
                B.read(5)
            shapes.append(dict(idx=len(shapes), sid=sid, nreg=nreg, npts=npts, ext=ext, tex=tex))
            continue
        if tag in ('01', '18'):
            B = R(payload)
            B.read_byte()                                   # pixelType
            sheets.append((B.read_u16(), B.read_u16()))     # 纹理图集尺寸 (w,h) px
            continue
        if tag in ('1a', '1e', '10', '1c'):
            continue
        unknown.append((pos, tag, size))

    return dict(matrices=matrices, clips=clips, shapes=shapes, sheets=sheets,
                exp_ids=exp_ids, exp_names=exp_names,
                ShapeCount=ShapeCount, TotalsAnim=TotalsAnim, TotalsTex=TotalsTex,
                TextFieldCount=TextFieldCount, MatrixCount=MatrixCount,
                ColorTransCount=ColorTransCount, tag_hist=dict(tag_hist), unknown=unknown)


# ----------------------------------------------------------------------------
# 汇总
# ----------------------------------------------------------------------------
def fmt_matrix(m):
    a, b, c, d, tx, ty = m
    return 'a=%d b=%d c=%d d=%d tx=%d ty=%d (sx=%.4f sy=%.4f dx=%.2fpx dy=%.2fpx)' % (
        a, b, c, d, tx, ty, a / 1024.0, d / 1024.0, tx / 20.0, ty / 20.0)


def clip_label(res, cid):
    for nm, i in zip(res['exp_names'], res['exp_ids']):
        if i == cid:
            return nm
    return '?'


def clip_matrix_usage(res):
    """每条 clip 用到的 matrixId 集合（去掉 65535）与其矩阵的 (sx, sy) 分布。"""
    out = []
    for c in res['clips']:
        mids = sorted({t[1] for t in c['triples'] if t[1] != 65535})
        scales = sorted({(res['matrices'][m][0] / 1024.0, res['matrices'][m][3] / 1024.0)
                         for m in mids if m < len(res['matrices'])})
        out.append(dict(id=c['id'], name=clip_label(res, c['id']), fps=c['fps'],
                        frames=c['frames'], n_placements=len(c['triples']),
                        matrix_ids=mids, scales=scales,
                        n_no_matrix=sum(1 for t in c['triples'] if t[1] == 65535)))
    return out


# ----------------------------------------------------------------------------
# 自检（正向对照）
# ----------------------------------------------------------------------------
def selftest(tower_sc):
    """正向对照：ArenaView.cs 点名的塔层已知矩阵。

    `client/Assets/Scripts/View/ArenaView.cs`「层级配方」段逐字写着：
      · 红王塔 clip 307 的 king_idle 层用 **matrix 89 = 0.5×**
      · 蓝王塔 clip 308 的 king_idle 层用 **matrix 185 = 0.5×**
    ⇒ 解析器读出的 matrices[89].a/1024 与 matrices[185].a/1024 必须都 = 0.5。
    """
    res = parse_sc(tower_sc)
    ok = True
    print('[selftest] %s' % tower_sc)
    print('[selftest] MatrixCount(header)=%d  解析出的矩阵条数=%d  tag 直方图=%s'
          % (res['MatrixCount'], len(res['matrices']), res['tag_hist']))
    if res['MatrixCount'] != len(res['matrices']):
        print('[selftest] FAIL: 矩阵条数与 header 不符')
        ok = False
    for mid in (89, 185):
        if mid >= len(res['matrices']):
            print('[selftest] FAIL: matrix %d 越界' % mid)
            ok = False
            continue
        m = res['matrices'][mid]
        sx = m[0] / 1024.0
        print('[selftest] matrices[%d] = %s' % (mid, fmt_matrix(m)))
        if abs(sx - 0.5) < 1e-6:
            print('[selftest]   OK: sx = %.4f = 0.5×（期待值）' % sx)
        else:
            print('[selftest]   FAIL: sx = %.4f ≠ 0.5' % sx)
            ok = False
    # 矩阵 1.0 的占比（ArenaView 段里的统计口径）
    ident = sum(1 for m in res['matrices'] if m[0] == 1024 and m[3] == 1024)
    no_rot = sum(1 for m in res['matrices'] if m[1] == 0 and m[2] == 0)
    print('[selftest] a=d=1024 条数 = %d / %d；b=c=0 条数 = %d / %d'
          % (ident, len(res['matrices']), no_rot, len(res['matrices'])))
    print('[selftest] RESULT: %s' % ('PASS' if ok else 'FAIL'))
    return 0 if ok else 1


# ----------------------------------------------------------------------------
# CLI
# ----------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--selftest', metavar='TOWER_SC', help='正向对照（塔层已知矩阵）')
    ap.add_argument('--matrix', metavar='SC', help='dump 该 .sc 的矩阵表')
    ap.add_argument('--clips', metavar='SC', help='dump 该 .sc 的 clip -> 矩阵用量')
    ap.add_argument('--shapes', metavar='SC', help='dump 该 .sc 的 shape 记录外接框（.sc 坐标单位）')
    ap.add_argument('--head', type=int, default=8, help='--shapes 只打印前 N 条（默认 8）')
    ap.add_argument('--stats', metavar='SC_OR_DIR',
                    help='逐 unit 汇总 units/px 的分布（判断「多边形 ↔ 图集矩形」是否为稳定仿射）')
    ap.add_argument('--batch', metavar='DIR', help='批量扫目录下的 .sc')
    ap.add_argument('--placements', metavar='DIR',
                    help='批量按「放置表」聚合：每单位 no-matrix 占比 + 非恒等矩阵的缩放直方图（按放置条数加权）')
    ap.add_argument('--unit-filter', default='chr_', help='批量时只扫文件名含该串的（默认 chr_）')
    args = ap.parse_args()

    if args.selftest:
        return selftest(args.selftest)

    if args.matrix:
        res = parse_sc(args.matrix)
        print('MatrixCount(header)=%d 解析=%d' % (res['MatrixCount'], len(res['matrices'])))
        for i, m in enumerate(res['matrices']):
            print('%5d  %s' % (i, fmt_matrix(m)))
        return 0

    if args.clips:
        res = parse_sc(args.clips)
        for u in clip_matrix_usage(res):
            print('clip %-5d %-28s fps=%-3d frames=%-4d placements=%-4d noMatrix=%-4d matrixIds=%s scales=%s'
                  % (u['id'], u['name'], u['fps'], u['frames'], u['n_placements'],
                     u['n_no_matrix'], u['matrix_ids'], u['scales']))
        return 0

    if args.shapes:
        res = parse_sc(args.shapes)
        print('ShapeCount(header)=%d 解析=%d  纹理图集(px)=%s' % (res['ShapeCount'], len(res['shapes']), res['sheets']))
        print('#  ext = 形状多边形的外包框（.sc 坐标单位）；tex = 该多边形在图集上占的矩形（px）')
        print('#  units/px = ext_W / tex_W；scale(PPU=100 口径，假设 1000 .sc 单位 = 1 格) = (units/px) / 10')
        for s in res['shapes'][:args.head]:
            if s['ext'] is None:
                print('rec %-4d sid=%-5d nreg=%d npts=%-3d ext=None' % (s['idx'], s['sid'], s['nreg'], s['npts']))
                continue
            x0, y0, x1, y1 = s['ext']
            ew, eh = x1 - x0 + 1, y1 - y0 + 1
            line = ('rec %-4d sid=%-5d nreg=%d npts=%-3d  ext %dx%d  ' % (s['idx'], s['sid'], s['nreg'], s['npts'], ew, eh))
            if s['tex']:
                tw = s['tex'][2] - s['tex'][0] + 1
                th = s['tex'][3] - s['tex'][1] + 1
                upp = ew / tw if tw else 0.0
                line += 'tex %.1fx%.1f px  units/px=%.2f  scale=%.3f' % (tw, th, upp, upp / 10.0)
            print(line)
        return 0

    if args.stats:
        for p in (args.stats,) if os.path.isfile(args.stats) else [
                os.path.join(args.stats, f) for f in sorted(os.listdir(args.stats))
                if f.endswith('.sc') and args.unit_filter in f]:
            res = parse_sc(p)
            xs = []
            ws = []
            for s in res['shapes']:
                if not s['ext'] or not s['tex']:
                    continue
                ew = s['ext'][2] - s['ext'][0] + 1
                tw = s['tex'][2] - s['tex'][0] + 1
                if tw > 0:
                    xs.append(ew / tw)
                    ws.append(ew)
            if not xs:
                print('%-32s 无可用 shape' % os.path.basename(p))
                continue
            xs.sort(); ws.sort()
            n = len(xs)
            print('%-32s n=%-5d units/px  min=%.2f p25=%.2f 中位=%.2f p75=%.2f max=%.2f | '
                  'ext_W(格) 中位=%.2f max=%.2f'
                  % (os.path.basename(p), n, xs[0], xs[n // 4], xs[n // 2], xs[3 * n // 4], xs[-1],
                     ws[n // 2] / 1000.0, ws[-1] / 1000.0))
        return 0

    if args.placements:
        files = sorted(f for f in os.listdir(args.placements)
                       if f.endswith('.sc') and args.unit_filter in f)
        for f in files:
            p = os.path.join(args.placements, f)
            try:
                res = parse_sc(p)
            except Exception as e:                       # noqa: BLE001
                print('%-32s PARSE-ERROR %s' % (f, e))
                continue
            n_all = 0
            n_no = 0
            hist = collections.Counter()
            examples = []
            for c in res['clips']:
                for (child, mid, ct) in c['triples']:
                    n_all += 1
                    if mid == 65535 or mid >= len(res['matrices']):
                        n_no += 1
                        continue
                    m = res['matrices'][mid]
                    key = '%.4f' % (m[0] / 1024.0)
                    hist[key] += 1
                    if len(examples) < 4 and abs(m[0] / 1024.0 - 1.0) > 1e-6:
                        examples.append((clip_label(res, c['id']), child, mid,
                                         m[0] / 1024.0, m[3] / 1024.0))
            print('%-32s placements=%-6d noMatrix=%-6d (%.1f%%)  matrixScales=%s'
                  % (f, n_all, n_no, 100.0 * n_no / max(1, n_all), hist.most_common(6)))
            for nm, child, mid, sx, sy in examples:
                print('    e.g. clip=%-26s child=%-4d matrix=%-4d sx=%.4f sy=%.4f'
                      % (nm, child, mid, sx, sy))
        return 0

    if args.batch:
        files = sorted(f for f in os.listdir(args.batch)
                       if f.endswith('.sc') and args.unit_filter in f)
        for f in files:
            p = os.path.join(args.batch, f)
            try:
                res = parse_sc(p)
            except Exception as e:                       # noqa: BLE001
                print('%-34s PARSE-ERROR %s' % (f, e))
                continue
            nonid = [(i, m[0] / 1024.0, m[3] / 1024.0) for i, m in enumerate(res['matrices'])
                     if m[0] != 1024 or m[3] != 1024]
            print('%-34s matrices=%-5d non-identity=%-5d clips=%-4d  scales(top10)=%s'
                  % (f, len(res['matrices']), len(nonid), len(res['clips']),
                     collections.Counter('%.4f' % s for _, s, _ in nonid).most_common(10)))
        return 0

    ap.print_help()
    return 0


if __name__ == '__main__':
    sys.exit(main())
