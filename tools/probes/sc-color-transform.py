# -*- coding: utf-8 -*-
"""sc-color-transform.py -- 判据资产：解 `.sc` 的 `09` 颜色变换，并把「件 → 变换 → 最终色」算出来。

字段口径（`09` payload 恒 7 字节，**顺序**）：
    r_add, g_add, b_add, alpha, r_mul, g_mul, b_mul
应用（出处 `sc-workshop/SupercellFlash` 的 `flash/transform/ColorTransform.cpp`，见 README §1）：
    out_rgb   = clamp(src_rgb * mul / 255 + add)
    out_alpha = clamp(src_alpha * alpha / 255)
⛔ 字节序**不是**「mul 在前」：`--selftest` 用 `darken`(元件名，原版数据) 与刻度件
（D116 实测为**压暗**）两条正控把顺序钉死 —— 若 mul 在前，这两个件都会变成**更亮**（纯白/白覆盖）。

复跑
----
  python tools/probes/sc-color-transform.py --selftest                      # 4 组结构断言（exit 0/1）
  python tools/probes/sc-color-transform.py --sc ui --clip HUD_player --depth 3
  python tools/probes/sc-color-transform.py --sc ui --clip 1080             # elixir_bar
  python tools/probes/sc-color-transform.py --find-shape 43                 # 谁放了 frame_043 + 用什么变换
  python tools/probes/sc-color-transform.py --sc ui --colors                # 09 bank 全量（序号 = ctId）
  python tools/probes/sc-color-transform.py --sc ui --bank-stats            # 各字节组的众数 / 恒等值命中
  python tools/probes/sc-color-transform.py --png 155,156,160               # 逐帧 PNG 像素报告

未解（本脚本不解、也不猜）
------------------------
  · `alpha` 字节 0 的语义（= 全透明 vs = 不改）。`mul`/`add` 均**非恒等**却有 260 条 α=0 的记录。
  · `mul` 的归一化是 /255（本脚本采用）还是 /256（`0` 即恒等）。
  两条都不影响本工程已落地的那几个件（刻度 α=77、`darken` α=90 均非 0 且 `mul=255`）。
"""
import argparse
import collections
import importlib.util
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
_spec = importlib.util.spec_from_file_location('sts', os.path.join(ROOT, 'tools', 'probes', 'sc-tag-scan.py'))
sts = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sts)

SC_DIR = os.path.join(ROOT, '原版资源', 'sc')
PNG_ROOT = os.path.join(ROOT, '原版资源', 'cr-assets-png', 'assets', 'sc')
NONE = 0xFFFF
FIELDS = ('r_add', 'g_add', 'b_add', 'alpha', 'r_mul', 'g_mul', 'b_mul')
CACHE = {}


def sc_file(name):
    for suf in ('_v215.sc', '.sc'):
        p = os.path.join(SC_DIR, name + suf)
        if os.path.isfile(p):
            return p
    raise FileNotFoundError(name)


class Res:
    def __init__(self, name):
        self.name = name
        self.data = sts.load(sc_file(name))
        hdr, exp_ids, exp_names, recs = sts.parse(self.data)
        self.hdr, self.exp_ids, self.exp_names = hdr, exp_ids, exp_names
        self.colors, self.mats, self.shape_ids = [], [], []
        self.clips = {}
        for tag, size, _off, poff in recs:
            pl = self.data[poff:poff + size]
            if tag == '09':
                self.colors.append(pl)
            elif tag == '08':
                self.mats.append(struct.unpack('<6i', pl))
            elif tag == '12':
                self.shape_ids.append(struct.unpack_from('<H', pl, 0)[0])
            elif tag == '0c':
                self.clips[struct.unpack_from('<H', pl, 0)[0]] = self._clip(pl)
        self.clip_ids = set(self.clips)
        self.shape_index = {sid: i for i, sid in enumerate(self.shape_ids)}

    @staticmethod
    def _clip(pl):
        cid = struct.unpack_from('<H', pl, 0)[0]
        cnt1 = struct.unpack_from('<i', pl, 5)[0]
        triples = [struct.unpack_from('<3H', pl, 9 + i * 6) for i in range(cnt1)]
        o = 9 + cnt1 * 6
        cnt2 = struct.unpack_from('<h', pl, o)[0]
        o += 2
        oids = list(struct.unpack_from('<%dh' % cnt2, pl, o))
        o += 2 * cnt2 + cnt2
        names = []
        for _ in range(cnt2):
            L = pl[o]
            o += 1
            if L >= 255:
                names.append(None)
            else:
                names.append(pl[o:o + L].decode('utf-8', 'replace'))
                o += L
        return dict(id=cid, triples=triples, oids=oids, names=names)

    def find(self, export_name):
        if export_name in self.exp_names:
            return self.exp_ids[self.exp_names.index(export_name)]
        if export_name.lstrip('-').isdigit():
            return int(export_name)
        return None

    def kind_of(self, oid):
        if oid in self.clip_ids:
            return 'clip'
        if oid in self.shape_index:
            return 'shape'
        return '?'

    def ct(self, cid):
        return None if cid == NONE or cid >= len(self.colors) else self.colors[cid]

    def mat(self, mi):
        if mi == NONE or mi >= len(self.mats):
            return (1024, 0, 0, 1024, 0, 0)
        return self.mats[mi]


def decode(pl):
    b = struct.unpack('<7B', pl)
    return dict(zip(FIELDS, b))


def apply_ct(rgb, cts):
    """链式乘加（顺序 = 从根到叶）。"""
    r, g, b = rgb
    for ct in cts:
        r = min(255, max(0, int(round(r * ct['r_mul'] / 255.0)) + ct['r_add']))
        g = min(255, max(0, int(round(g * ct['g_mul'] / 255.0)) + ct['g_add']))
        b = min(255, max(0, int(round(b * ct['b_mul'] / 255.0)) + ct['b_add']))
    return (r, g, b)


def apply_alpha(a, cts):
    for ct in cts:
        a = int(round(a * ct['alpha'] / 255.0))
    return max(0, min(255, a))


def frame_of(res, oid):
    return 'frame_%03d' % res.shape_index[oid] if oid in res.shape_index else None


def frame_num(res, oid):
    return res.shape_index.get(oid)


def walk(res, cid, path, depth, maxdepth, stack, out):
    c = res.clips.get(cid)
    if c is None:
        return
    seen = set()
    for (ci, mi, coi) in c['triples']:
        if ci >= len(c['names']):
            continue
        seen.add(ci)
        nm = c['names'][ci] or '<无名>'
        oid = c['oids'][ci]
        ctg = decode(res.ct(coi)) if res.ct(coi) else None
        kid = res.kind_of(oid)
        m = res.mat(mi)
        newstack = stack + ([ctg] if ctg else [])
        out.append(dict(depth=depth, path=path + '/' + nm, name=nm, obj=oid, kind=kid,
                        frame=frame_of(res, oid), fn=frame_num(res, oid),
                        matidx=mi if mi != NONE else None, colidx=coi if coi != NONE else None,
                        sx=m[0] / 1024.0, sy=m[3] / 1024.0, tx=m[4] / 20.0, ty=m[5] / 20.0,
                        ct=ctg, stack=newstack))
        if kid == 'clip' and depth < maxdepth:
            walk(res, oid, path + '/' + nm, depth + 1, maxdepth, newstack, out)
    for i, nm in enumerate(c['names']):
        if i in seen:
            continue
        out.append(dict(depth=depth, path=path + '/' + (nm or '<无名>'), name=nm or '<无名>',
                        obj=c['oids'][i], kind=res.kind_of(c['oids'][i]),
                        frame=frame_of(res, c['oids'][i]), fn=frame_num(res, c['oids'][i]),
                        matidx=None, colidx=None, sx=None, sy=None, tx=None, ty=None,
                        ct=None, stack=stack))


def find_shape(res, fnum, only_named=True):
    """列出「哪些 clip 的哪个子件放了这个 shape（frame_NNN）」及其颜色变换。"""
    rows = []
    for cid, c in res.clips.items():
        cname = None
        if cid in res.exp_ids:
            cname = res.exp_names[res.exp_ids.index(cid)]
        for (ci, mi, coi) in c['triples']:
            if ci >= len(c['oids']) or c['oids'][ci] not in res.shape_index:
                continue
            if res.shape_index[c['oids'][ci]] != fnum:
                continue
            nm = c['names'][ci] if ci < len(c['names']) else None
            if only_named and not nm:
                pass
            ctg = decode(res.ct(coi)) if res.ct(coi) else None
            m = res.mat(mi)
            rows.append((cid, cname, ci, nm, coi if coi != NONE else None, ctg,
                         (m[0] / 1024.0, m[3] / 1024.0, m[4] / 20.0, m[5] / 20.0)))
    return rows


def load_png(frame, scname='ui'):
    key = (scname, frame)
    if key in CACHE:
        return CACHE[key]
    p = os.path.join(PNG_ROOT, scname + '_out', '%s_sprite_%03d.png' % (scname, frame))
    if not os.path.isfile(p):
        p = os.path.join(PNG_ROOT, scname + '_out', '%s_sprite_%s.png' % (scname, frame))
    if not os.path.isfile(p):
        CACHE[key] = None
        return None
    from PIL import Image
    im = Image.open(p).convert('RGBA')
    CACHE[key] = im
    return im


def png_dominant(frame, scname='ui', min_alpha=200):
    im = load_png(frame, scname)
    if im is None:
        return None
    w, h = im.size
    px = im.load()
    cnt = collections.Counter()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= min_alpha:
                cnt[(r, g, b, a)] += 1
    if not cnt:
        return None
    (r, g, b, a), n = cnt.most_common(1)[0]
    return (r, g, b, a, n, sum(cnt.values()))


def png_report(frames, scname='ui'):
    for f in frames:
        im = load_png(int(f), scname)
        if im is None:
            print('frame_%03d  缺失' % int(f))
            continue
        w, h = im.size
        px = im.load()
        cnt = collections.Counter()
        rows = collections.defaultdict(collections.Counter)
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                if a > 0:
                    cnt[(r, g, b, a)] += 1
                    if len(rows) < 999:
                        rows[y][(r, g, b, a)] += 1
        nz = sorted({y for y in range(h) if any(px[x, y][3] > 0 for x in range(w))})
        print('frame_%03d 画布 %dx%d 非透明像素 %d 行范围 %s..%s 行数 %d'
              % (int(f), w, h, sum(cnt.values()),
                 nz[0] if nz else '-', nz[-1] if nz else '-', len(nz)))
        for (r, g, b, a), n in cnt.most_common(6):
            ys = [y for y in nz if rows[y].get((r, g, b, a))]
            print('    rgba=(%3d,%3d,%3d,%3d) x%-5d 行 %s..%s'
                  % (r, g, b, a, n, ys[0] if ys else '-', ys[-1] if ys else '-'))
        if nz:
            first = [y for y in nz][:6]
            for y in (nz[0], nz[1] if len(nz) > 1 else nz[0], nz[-2] if len(nz) > 1 else nz[0], nz[-1]):
                print('    行 %3d: %s' % (y, dict(list(rows[y].items())[:4])))


def selftest():
    """4 组结构 / 正向对照断言（exit 0/1）。全部读数来自 `原版资源/sc/ui_v215.sc`。"""
    ok = True

    def chk(cond, msg):
        nonlocal ok
        print(('  PASS  ' if cond else '  FAIL  ') + msg)
        if not cond:
            ok = False

    res = Res('ui')
    print('---- 1. 头部计数与解析条数自洽 ----')
    chk(len(res.colors) == res.hdr['ColorTransCount'],
        'tag09 条数(%d) == header ColorTransCount(%d)' % (len(res.colors), res.hdr['ColorTransCount']))
    chk(len(res.mats) == res.hdr['MatrixCount'], 'tag08 条数 == MatrixCount')

    print('---- 2. 字节组的众数 = 各组的恒等值（换个字节序就会出现「mul 众数 = 0」这种非恒等默认）----')
    rows = [decode(pl) for pl in res.colors]
    for k, ident in (('r_add', 0), ('g_add', 0), ('b_add', 0), ('alpha', 255),
                     ('r_mul', 255), ('g_mul', 255), ('b_mul', 255)):
        c = collections.Counter(r[k] for r in rows)
        chk(c.most_common(1)[0][0] == ident, '%s 的众数 == %d（实际 %s）' % (k, ident, c.most_common(3)))

    print('---- 3. 正向对照 a：`HUD_player` 的 `darken` 元件必须**压暗** ----')
    hid = res.find('HUD_player')
    walk_rows = []
    walk(res, hid, '', 0, 1, [], walk_rows)
    dk = [r for r in walk_rows if r['name'] == 'darken' and r['ct']]
    chk(len(dk) == 1, 'HUD_player 有 1 个带颜色变换的 `darken`（实际 %d）' % len(dk))
    if dk:
        ct = dk[0]['ct']
        dark = ct['r_add'] == 0 and ct['g_add'] == 0 and ct['b_add'] == 0 and ct['r_mul'] == 255
        chk(dark, '`darken` 的变换 = add 0 / mul 255（自身自色覆盖）⇒ 压暗；实际 %s' % (dk[0]['path'],))
        chk(ct['alpha'] < 255, '`darken` 的 α = %d/255 半透明覆盖' % ct['alpha'])

    print('---- 4. 正向对照 b：圣水刻度件 d1..d9 = α 77/255 覆盖在 (35,35,35) 上 ----')
    eb = res.find('1080')
    rows2 = []
    walk(res, eb, '', 0, 1, [], rows2)
    ds = [r for r in rows2 if r['name'] and r['name'].startswith('d') and r['ct']]
    chk(len(ds) == 9, 'elixir_bar 的刻度件 = 9 条（实际 %d）' % len(ds))
    if ds:
        a = {r['ct']['alpha'] for r in ds}
        same = all(r['ct']['r_add'] == 0 and r['ct']['r_mul'] == 255 for r in ds)
        chk(a == {77} and same, '9 条刻度件的变换全等 = add 0 / α 77 / mul 255（实际 α=%s）' % a)
    d = png_dominant(160, 'ui')
    chk(d is not None and d[:4] == (35, 35, 35, 255),
        'frame_160 的唯一不透明像素 = (35,35,35,255)（实际 %s）' % (d,))

    print('---- 5. 反证：`bar_bg`(frame_155) 链上没有任何颜色变换（D126 不能由变换解释）----')
    f155 = find_shape(res, 155)
    chk(len(f155) > 0, 'frame_155 有放置者 %d 处' % len(f155))
    chk(all(r[5] is None for r in f155), '全部放置项的 ctId 均为 65535（实际 %s）'
        % [r[4] for r in f155])

    print('---- 结论: %s ----' % ('全部 PASS' if ok else '存在 FAIL'))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sc', default='ui')
    ap.add_argument('--clip', default='HUD_player')
    ap.add_argument('--depth', type=int, default=3)
    ap.add_argument('--src', default=None, help='固定代入源色 r,g,b；缺省 = 该件 PNG 众数色')
    ap.add_argument('--png', default=None, help='逐帧 PNG 像素报告（逗号分隔）')
    ap.add_argument('--png-src', action='store_true', help='源色取该件 PNG 众数色')
    ap.add_argument('--colors', action='store_true')
    ap.add_argument('--out', default=None, help='表格另存路径')
    ap.add_argument('--find-shape', default=None, help='按 frame_NNN 反查放置者与其颜色变换')
    ap.add_argument('--bank-stats', action='store_true', help='09 bank 的各字节组「是否以恒等值为众数」统计')
    ap.add_argument('--selftest', action='store_true', help='4 组结构 / 正向对照断言（exit 0/1）')
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    if args.png:
        png_report(args.png.split(','), args.sc)
        return 0

    res = Res(args.sc)
    if args.bank_stats:
        rows = [decode(pl) for pl in res.colors]
        print('sc=%s  09 bank = %d 条' % (args.sc, len(rows)))
        for k in FIELDS:
            c = collections.Counter(r[k] for r in rows)
            print('  %-6s 众数 %s ; 恒等值命中 %d 条' % (k, c.most_common(5), c[255 if k != 'r_add' and k != 'g_add' and k != 'b_add' else 0]))
        plain = [r for r in rows if r['r_add'] == 0 and r['g_add'] == 0 and r['b_add'] == 0
                 and r['r_mul'] == 255 and r['g_mul'] == 255 and r['b_mul'] == 255]
        print('  「只改 α」的记录（add=0 且 mul=255）= %d 条；其 α 分布(前 20) = %s'
              % (len(plain), collections.Counter(r['alpha'] for r in plain).most_common(20)))
        solid = [r for r in rows if r['r_mul'] == 0 and r['g_mul'] == 0 and r['b_mul'] == 0]
        print('  「纯色填充」型（mul=0，取 add 为色）= %d 条；例 %s'
              % (len(solid), [(r['r_add'], r['g_add'], r['b_add'], r['alpha']) for r in solid[:8]]))
        print('  α=0 的记录 = %d 条；其中 add/mul 非恒等 = %d 条'
              % (sum(1 for r in rows if r['alpha'] == 0),
                 sum(1 for r in rows if r['alpha'] == 0 and not (r['r_add'] == 0 and r['g_add'] == 0 and r['b_add'] == 0
                                                                 and r['r_mul'] == 255 and r['g_mul'] == 255 and r['b_mul'] == 255))))
        return 0
    if args.find_shape is not None:
        fnum = int(args.find_shape)
        d = png_dominant(fnum, args.sc)
        print('frame_%03d PNG 众数色 = %s' % (fnum, d))
        for (cid, cname, ci, nm, coi, ctg, m) in find_shape(res, fnum):
            if ctg:
                cts = 'add=(%d,%d,%d) α=%d(%d/255) mul=(%d,%d,%d)' % (
                    ctg['r_add'], ctg['g_add'], ctg['b_add'], ctg['alpha'],
                    ctg['alpha'], ctg['r_mul'], ctg['g_mul'], ctg['b_mul'])
                fin = apply_ct((d[0], d[1], d[2]), [ctg]) if d else None
            else:
                cts, fin = '无', (d[0], d[1], d[2]) if d else None
            print('clip %-5d %-28s child=%-3d name=%-22s ctId=%-6s 矩阵(sx=%.3f sy=%.3f tx=%.1f ty=%.1f) %s → %s'
                  % (cid, cname or '—', ci, nm or '<无名>', coi, m[0], m[1], m[2], m[3], cts, fin))
        return 0
    if args.colors:
        for i, pl in enumerate(res.colors):
            print('%5d %s %s' % (i, pl.hex(), decode(pl)))
        return 0

    fixed = tuple(int(x) for x in args.src.split(',')) if args.src else None
    cid = res.find(args.clip)
    if cid is None:
        print('!! 找不到 %r' % args.clip)
        return 2
    out = []
    walk(res, cid, '', 0, args.depth, [], out)
    lines = ['sc=%s clip=%s id=%d 放置项=%d' % (args.sc, args.clip, cid, len(out))]
    lines.append('%-40s %-5s %-10s %-20s %-6s %-28s %-14s | %s' %
                 ('path', 'kind', 'frame', 'mat(sx,sy,tx,ty)', 'ctId', 'ct(7B) add/α/mul', '源色',
                  '最终色'))
    for o in out:
        ct = o['ct']
        cts = ('%02x%02x%02x %02x %02x%02x%02x' %
               (ct['r_add'], ct['g_add'], ct['b_add'], ct['alpha'],
                ct['r_mul'], ct['g_mul'], ct['b_mul'])) if ct else '—'
        if fixed:
            src, tag = fixed, 'fixed'
        elif o['fn'] is not None:
            d = png_dominant(o['fn'], args.sc)
            if d is None:
                src, tag = None, 'PNG缺失'
            else:
                src, tag = (d[0], d[1], d[2]), 'png众数×%d/α%d' % (d[4], d[3])
        else:
            src, tag = None, ''
        mat = '—' if o['sx'] is None else ('%.3f,%.3f,%.1f,%.1f' % (o['sx'], o['sy'], o['tx'], o['ty']))
        fin = '—' if src is None else '%s α=%d' % (apply_ct(src, o['stack']), apply_alpha(255, o['stack']))
        lines.append('%-40s %-5s %-10s %-20s %-6s %-28s %-14s | %s' %
                     (('  ' * o['depth']) + o['path'][:38], o['kind'], o['frame'] or '—', mat,
                      o['colidx'] if o['colidx'] is not None else '—', cts,
                      ('%s %s' % (src, tag)) if src else tag, fin))
    txt = '\n'.join(lines)
    print(txt)
    if args.out:
        with open(args.out, 'w', encoding='utf-8', newline='') as f:
            f.write(txt + '\n')
        print('已写 %s' % args.out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
