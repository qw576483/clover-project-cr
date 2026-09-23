#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
end-frames.py -- AR2 判据资产：`ui_battle_end`（战斗结算）240 帧的联络图 + 权威用途映射
=======================================================================================
做两件事（都可复跑）：

1. **权威用途映射**：解析 `原版资源/sc/ui_battle_end_v215.sc`，把 **export 名 → clip →
   shape 记录序** 展开（复用 `sc-at1-resolve.py` 的 `resolve_unit`，已解共享 id 空间 +
   嵌套 movieclip），得到「帧号 `frame_NNN` ↔ 原版作者给的用途名」——⛔ 不是我们自己起的名。
2. **联络图**：把 `<root>/原版资源/cr-assets-png/assets/sc/ui_battle_end_out/` 的 240 张
   PNG 按帧号切成 5 页（每页 50 格 ≤ 100），每格 = 该帧**非透明 bbox 裁剪**（小的放大以便
   肉眼看清），格下印 **帧号 + 原始画布尺寸 + bbox 尺寸**，落
   `<root>/.ai-tmp/screenshots/AR2-end-pNN_fXXX-YYY.png`。

复跑
----
  python tools/probes/end-frames.py --root .
  python tools/probes/end-frames.py --root . --dump-tag19      # 另打印 40 条文本字段原始字节

⛔ 本脚本只读、只落 `.ai-tmp/screenshots/` 与 `.ai-tmp/test/`；不碰 `client/**`。
"""
import argparse
import importlib.util
import json
import os
import struct
import sys
from collections import OrderedDict

from PIL import Image, ImageDraw, ImageFont

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

COLS = 8           # 每页列数
ROWS = 7           # 每页行数   ⇒ 56 格/页（≤100，且 240 帧共 5 页）
CELL = 300         # 每格绘制区边长（像素）
LABEL_H = 34       # 每格标签高度
PAD = 6

FONT_CAND = [
    r'C:\Windows\Fonts\consola.ttf',
    r'C:\Windows\Fonts\msyh.ttc',
    r'C:\Windows\Fonts\simsun.ttc',
]


def _load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def font(size):
    for f in FONT_CAND:
        if os.path.isfile(f):
            try:
                return ImageFont.truetype(f, size)
            except Exception:
                pass
    return ImageFont.load_default()


def parse_sc(root):
    """返回 (res, exp_frames, frame2exps, textfields)。frame 号 = shape 记录序。"""
    R = _load(os.path.join(root, 'tools', 'probes', 'sc-anim-index.py'), 'sai')
    AT1 = _load(os.path.join(root, 'tools', 'probes', 'sc-at1-resolve.py'), 'at1')
    sc = os.path.join(root, '原版资源', 'sc', 'ui_battle_end_v215.sc')
    data = open(sc, 'rb').read()
    res = R.parse_unit(data)
    sid2idx, unres, _dang = AT1.resolve_unit(res)
    clip_by_id = {c['id']: c for c in res['clips']}
    exp_frames = OrderedDict()
    frame2exps = {}
    for nm, eid in zip(res['exp_names'], res['exp_ids']):
        c = clip_by_id.get(eid)
        fr = sorted(set(c['sids'])) if c else []
        exp_frames[nm] = dict(clip_id=eid, fps=(c['fps'] if c else None),
                             frames=fr, raw=(list(c['_ids_raw']) if c else []))
        for f in fr:
            frame2exps.setdefault(f, []).append(nm)
    # 文本字段（tag 19，被 parse_unit 归到 unknown）
    tfs = [(pos, sz, data[pos + 5:pos + 5 + sz]) for pos, tag, sz in res['unknown'] if tag == '19']
    return res, exp_frames, frame2exps, tfs, sorted(unres)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--frames-dir', default=os.path.join('原版资源', 'cr-assets-png', 'assets', 'sc', 'ui_battle_end_out'))
    ap.add_argument('--out-sheets', default='.ai-tmp/screenshots')
    ap.add_argument('--out-json', default='.ai-tmp/test/AR2-sc-map.json')
    ap.add_argument('--out-tsv', default='.ai-tmp/test/AR2-帧表.tsv')
    ap.add_argument('--dump-tag19', action='store_true')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    res, exp_frames, frame2exps, tfs, dangling = parse_sc(root)
    fdir = os.path.join(root, a.frames_dir)
    files = sorted(f for f in os.listdir(fdir) if f.lower().endswith('.png'))
    print('ShapeCount=%d  PNG=%d  ExportCount=%d  clips=%d  shapes=%d  dangling=%d'
          % (res['ShapeCount'], len(files), len(res['exp_names']), len(res['clips']),
             len(res['shapes']), len(dangling)))

    # ---- 联系：frame_NNN.png 数 == ShapeCount ----
    assert len(files) == res['ShapeCount'], 'PNG 数 != ShapeCount'

    # ---- 逐帧：尺寸 + bbox ----
    rows = []
    for f in files:
        n = int(f.rsplit('_', 1)[1].split('.')[0])
        im = Image.open(os.path.join(fdir, f))
        canvas = im.size
        bbox = im.getbbox() or (0, 0, 0, 0)
        rows.append(dict(frame=n, file=f, canvas='%dx%d' % canvas,
                         bbox='%dx%d' % (bbox[2] - bbox[0], bbox[3] - bbox[1]),
                         _bbox=bbox, _im=im))
    by_frame = {r['frame']: r for r in rows}
    assert sorted(by_frame) == list(range(len(rows))), '帧号不连续'

    # ---- 联络图 ----
    os.makedirs(os.path.join(root, a.out_sheets), exist_ok=True)
    fnt = font(15)
    fnt2 = font(13)
    per_page = COLS * ROWS
    pages = []
    for p0 in range(0, len(rows), per_page):
        chunk = rows[p0:p0 + per_page]
        W = COLS * (CELL + PAD) + PAD
        H = ROWS * (CELL + LABEL_H + PAD) + PAD
        sheet = Image.new('RGB', (W, H), (28, 28, 32))
        d = ImageDraw.Draw(sheet)
        for i, r in enumerate(chunk):
            cx = PAD + (i % COLS) * (CELL + PAD)
            cy = PAD + (i // COLS) * (CELL + LABEL_H + PAD)
            im = r['_im']
            bb = r['_bbox']
            crop = im.crop(bb) if (bb[2] > bb[0] and bb[3] > bb[1]) else im.crop((0, 0, 1, 1))
            w, h = crop.size
            f = max(1, min(16, int(CELL / max(w, h)))) if max(w, h) else 1
            disp = crop.resize((w * f, h * f), Image.NEAREST)
            if disp.mode != 'RGBA':
                disp = disp.convert('RGBA')
            bg = Image.new('RGBA', disp.size, (70, 70, 78, 255))
            bg.alpha_composite(disp)
            ox = cx + (CELL - disp.size[0]) // 2
            oy = cy + (CELL - disp.size[1]) // 2
            sheet.paste(bg.convert('RGB'), (ox, oy))
            d.rectangle([cx, cy, cx + CELL, cy + CELL], outline=(120, 120, 130))
            d.rectangle([cx, cy + CELL, cx + CELL, cy + CELL + LABEL_H], fill=(16, 16, 18))
            exps = frame2exps.get(r['frame'], [])
            d.text((cx + 4, cy + CELL + 2), 'f%03d  canvas %s' % (r['frame'], r['canvas']),
                   font=fnt, fill=(255, 235, 120))
            tag = ('bbox ' + r['bbox'] + ('  x%d' % f if f > 1 else ''))
            d.text((cx + 4, cy + CELL + 18), tag, font=fnt2, fill=(200, 200, 210))
            if exps:
                d.text((cx + CELL - 4, cy + CELL + 18), (exps[0][:22]), font=fnt2,
                       fill=(120, 230, 140), anchor='rs')
        name = 'AR2-end-p%02d_f%03d-%03d.png' % (p0 // per_page + 1, chunk[0]['frame'], chunk[-1]['frame'])
        sheet.save(os.path.join(root, a.out_sheets, name))
        pages.append((name, chunk[0]['frame'], chunk[-1]['frame'], len(chunk)))
        print('sheet %s  %d 格' % (name, len(chunk)))

    # ---- 落 TSV / JSON ----
    os.makedirs(os.path.dirname(os.path.join(root, a.out_tsv)), exist_ok=True)
    tsv = ['帧号\t画布尺寸\tbbox尺寸\t导出名(原版作者给的用途)\tbbox_x0\ty0\tx1\ty1']
    for r in rows:
        e = ','.join(frame2exps.get(r['frame'], []))
        tsv.append('%03d\t%s\t%s\t%s\t%d\t%d\t%d\t%d' % (r['frame'], r['canvas'], r['bbox'], e,
                                                         r['_bbox'][0], r['_bbox'][1],
                                                         r['_bbox'][2], r['_bbox'][3]))
    open(os.path.join(root, a.out_tsv), 'w', encoding='utf-8').write('\n'.join(tsv) + '\n')
    led = dict(ShapeCount=res['ShapeCount'], png_count=len(files), ExportCount=len(res['exp_names']),
               TextFieldCount=res['TextFieldCount'], TotalsAnim=res['TotalsAnim'],
               dangling_ids=dangling, tag_hist=res['tag_hist'],
               exp_frames={k: v['frames'] for k, v in exp_frames.items()},
               sheets=[dict(name=n, f0=f0, f1=f1, cells=c) for n, f0, f1, c in pages])
    json.dump(led, open(os.path.join(root, a.out_json), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)
    used = sorted(frame2exps)
    print('被 export 引用的帧 %d / 240 ；未被任何 export 引用的帧 %d'
          % (len(used), 240 - len(used)))
    print('TSV  -> %s' % a.out_tsv)
    print('JSON -> %s' % a.out_json)

    if a.dump_tag19:
        print('---- 文本字段(tag 19) %d 条 ----' % len(tfs))
        for pos, sz, pl in tfs[:8]:
            asc = ''.join(chr(b) if 32 <= b < 127 else '.' for b in pl)
            print('@%-7d size=%-3d %s' % (pos, sz, pl.hex()))
            print('            %s' % asc)
    return 0


if __name__ == '__main__':
    sys.exit(main())
