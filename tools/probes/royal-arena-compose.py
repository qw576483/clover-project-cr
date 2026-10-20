#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""royal-arena-compose.py -- 把原版 `level_royal_arena_v215.sc` 的放置表 + 图集合成为一张场地底图。

产物
----
  client/Assets/Resources/Sprites/Arenas/royal/royal_field.png
  控制台打印 4 个常量（PNG 宽高 + 场地矩形在 PNG 里的左上角），供 `ArenaView` 的 RoyalField 常量使用。

复跑
----
  python tools/probes/royal-arena-compose.py
  python tools/probes/royal-arena-compose.py --check      # 只校验产物与几何，不重写

口径（全部有出处）
------------------
· `.sc` 头 + tag 流：见 `tools/probes/sc-placement.py` 的文档注释（`08` 矩阵 / `0c` clip / `12` shape）。
· 矩阵：a,b,c,d 定点 1/1024；tx,ty twips（1/20 px）。
· shape 的绘制命令（tag `16`）：u8 textureIndex、u8 verticesCount、逐点 i32 x/y（twips）、再逐点 u16 u/v
  —— 出处 `.ai-tmp/SupercellFlash/supercell-flash/source/flash/display_object/ShapeDrawBitmapCommand.cpp`
  （`load()` 第 6-21 行、`Shape::load()` 第 18-47 行）。每条命令是一个多边形，按扇形三角化后逐三角形做仿射 UV 采样。
· 逐帧放置 = clip 的 display list 顺序（先画的在下）。
· 每条放置命令带颜色变换索引：`(u16 instance, u16 matrix, u16 colorTransform)`，第三项索引 `09` 段；
  `0xFFFF` = 恒等。`09` payload 7 字节，顺序 = `r_add, g_add, b_add, alpha, r_mul, g_mul, b_mul`
  （出处 `tools/probes/sc-color-transform.py` 头部，`--selftest` 用 `darken` / 圣水刻度件两组正控把顺序钉死）：
      out_rgb = clamp(src_rgb * mul / 255 + add)；out_alpha = clamp(src_alpha * alpha / 255)
  嵌套时父件变换作用于整棵子树，链式合成（父 mul 乘子 mul，父 add 经父 mul 后再加子 add）。
· 合成对象 = export `royal_base_bgr`（clip 104）：整座皇家竞技场的底盘（草地、石墙、河道、双桥、塔基台）。

几何标定（本脚本据此把底图对齐到 18×32 格）
--------------------------------------------
· 场地横向 = 石墙内沿，实测 px 795..1336（542 px ÷ 18 格 = 30.1 px/格）。
· 场地纵向用两座国王塔台中心钉死：实测 y=531 / y=1155，对应格 y=3 / y=29
  ⇒ 24.0 px/格、场地纵向 459..1227（768 px ÷ 32 格）。
"""

import argparse
import importlib.util
import os

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SC = os.path.join(ROOT, '原版资源', 'sc', 'level_royal_arena_v215.sc')
ATLAS = os.path.join(ROOT, '原版资源', 'cr-assets-png', 'assets', 'sc', 'level_royal_arena_tex.png')
OUT = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas', 'royal', 'royal_field.png')

CLIP_SRC = 'royal_base_bgr'

# 强制按恒等处理的 `09` 序号（逐件可关：某个变换把草地/石墙弄脏时只回退那一件）。
CT_FORCE_IDENTITY = set()

# 图集原画与对局参考图 `策划/参考图/20_对局_1080x1920.jpg` 的配色不同材质各差一档，且逐通道不单调
# （草地蓝通道更低、砖路蓝通道更高）；`.sc` 的 `09` 段在 `royal_base_bgr` 里全是纯 alpha
# （RGB mul/add 恒等，见 `tools/probes/sc-color-transform.py --sc level_royal_arena --colors`），
# 所以按材质从参考图量目标色，再用「同材质偏移」把底图搬过去。
# 目标色 = 该参考图在框 (30,60)-(1050,1500) 内、按下面色相规则分出的同类像素中位数。
REF_PALETTE = {
    'grass': (173, 194, 55),            # 草地（含深浅棋盘两档的中位）
    'path': (243, 186, 137),            # 砖路 / 桥面 / 塔基台
    'wall': (225, 216, 217),            # 石墙 / 石台
    'water': (102, 139, 170),           # 河道水面（参考图河道带 y 780..1000 的水面中位数）
    'bank': (109, 64, 63),              # 水岸深红石沿
}

# 桥：桥板与两侧栏柱在底图里是同一档金色（比砖路更亮），加性偏移只能把暗部拉平、压不住高光，
# 台面上仍是一片金黄 ⇒ 桥单独成一簇，按「比值」映射到参考图桥面同区域中位色（对局里桥面就是砖路的延长段，
# 参考图量得 241,184,138）。比值映射把亮金高光一并压成米色，栏柱不会比桥板更黄。
BRIDGE_TARGET = (241, 184, 138)
# 桥在画布上的两块范围（含栏柱球头，按河道水面缺口定出，见 --check 输出的河道行）
BRIDGE_BOXES = ((845, 780, 958, 945), (1173, 780, 1286, 945))

# 逐个材质可关：关掉哪个，哪个材质的像素保持图集原色。
PALETTE_OFF = set()


def classify(r, g, b):
    """按色相把底图像素归到 5 个材质类（与量参考图时同一套规则）。"""
    mx = np.maximum(np.maximum(r, g), b)
    mn = np.minimum(np.minimum(r, g), b)
    warm = (r > g + 15) & (g > b + 35) & (r > 150)
    grass = (g > r + 5) & (g > b + 60)
    water = (b > g + 12) & (b > r + 12)
    bank = (r > g + 25) & (r < 160) & (g < 100)
    wall = ((mx - mn) < 32) & (((r + g + b) / 3.0) > 140)
    return {'warm': warm, 'grass': grass, 'water': water, 'bank': bank, 'wall': wall}


def palette_shift(canvas):
    """底图 → 参考图配色：逐材质取「目标中位数 − 本图中位数」当偏移，按距离平方反比软加权施加。

    保纹理：偏移是加性的，材质的明暗/棋盘/砖缝差异原样保留；材质交界处按距离平滑过渡。
    """
    px = canvas[..., :3].astype(np.float32)
    r, g, b = px[..., 0], px[..., 1], px[..., 2]
    # 本图中位数只在**裁剪框内**统计：框外是水岸外圈，会把同一材质的中位拉偏（框内才是实际入图的场地）。
    win = np.zeros(r.shape, bool)
    win[CROP_Y0:CROP_Y1, CROP_X0:CROP_X1] = True
    masks = classify(r, g, b)
    mask_of = {'path': masks['warm'], 'grass': masks['grass'], 'wall': masks['wall'],
               'water': masks['water'], 'bank': masks['bank']}
    centers, deltas = [], []
    for cls, tgt in REF_PALETTE.items():
        m = mask_of[cls] & win
        if cls in PALETTE_OFF or m.sum() < 200:
            continue
        src = np.median(px[m], axis=0)
        centers.append(src.astype(np.float32))
        deltas.append(np.array(tgt, np.float32) - src.astype(np.float32))
        print('  材质 %-6s 本图中位=(%3d,%3d,%3d) → 参考图 (%3d,%3d,%3d)  像素 %d'
              % (cls, src[0], src[1], src[2], tgt[0], tgt[1], tgt[2], int(m.sum())))
    if not centers:
        return canvas
    centers = np.stack(centers)
    deltas = np.stack(deltas)
    # 距离平方反比软加权（σ 等效 30/通道：类内主导、类间几乎不串）
    d2 = ((px[..., None, :] - centers) ** 2).sum(-1)
    w = np.exp(-d2 / (2.0 * 30.0 * 30.0))
    w /= np.maximum(w.sum(-1, keepdims=True), 1e-6)
    out = np.clip(px + (w @ deltas), 0.0, 255.0)
    res = canvas.copy()
    res[..., :3] = np.round(out).astype(np.uint8)
    return bridge_shift(canvas, res)


def bridge_shift(raw, res):
    """桥单独成簇：按比值把桥区的暖色（金）映射到参考图桥面中位色。"""
    if 'bridge' in PALETTE_OFF:
        return res
    tgt = np.array(BRIDGE_TARGET, np.float32)
    for (x0, y0, x1, y1) in BRIDGE_BOXES:
        box = raw[y0:y1, x0:x1, :3].astype(np.float32)
        r, g, b = box[..., 0], box[..., 1], box[..., 2]
        warm = (r > 150) & (r >= g) & (g >= b)
        if warm.sum() < 200:
            continue
        src = np.median(box[warm], axis=0)
        scale = tgt / np.maximum(src, 1.0)
        mapped = np.clip(box * scale, 0.0, 255.0)
        reg = res[y0:y1, x0:x1]
        reg[warm, :3] = np.round(mapped[warm]).astype(np.uint8)
        print('  材质 bridge  本图中位=(%3d,%3d,%3d) → 参考图 (%3d,%3d,%3d)  像素 %d'
              % (src[0], src[1], src[2], tgt[0], tgt[1], tgt[2], int(warm.sum())))
    return res


# 出征区虚线：原版把「可部署区域」虚线也烘进这张底盘（1164 段 3×98 px），游戏里由 UI 另画 ⇒ 这里不取。
EXCLUDE_UV = {(1219, 0, 1222, 98)}

FIELD_X0, FIELD_Y0, FIELD_X1, FIELD_Y1 = 795, 459, 1336, 1227   # 合成画布 px（左/上/右/下）
CROP_X0, CROP_Y0, CROP_X1, CROP_Y1 = 713, 350, 1421, 1351       # 带水岸外圈的裁剪框（同坐标系）

TILE_W_PX = 60          # 1080 宽基准：18 格 ÷ 1080
TILE_H_PX = 45          # 1080×1920 基准：32 格 ÷ 1440


def _load_parser():
    p = os.path.join(ROOT, 'tools', 'probes', 'sc-placement.py')
    spec = importlib.util.spec_from_file_location('sp', p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


sp = _load_parser()


def parse_sc(path):
    data = sp.load_sc(path)
    s = sp.R(data)
    s.read_u16(); s.read_u16(); s.read_u16(); s.read_u16()
    s.read_u16(); s.read_u16(); s.read(5)
    ec = s.read_u16()
    ids = [s.read_u16() for _ in range(ec)]
    names = []
    for _ in range(ec):
        n = s.read_byte()
        names.append(s.read(n).decode('utf-8', 'replace'))

    mats, clips, shapes, sheets, colors = [], {}, [], [], []
    while s.remain() > 0:
        tag = s.read(1).hex()
        size = s.read_u32()
        pl = s.read(size)
        if tag == '08':
            b = sp.R(pl)
            mats.append(tuple(b.read_i32() for _ in range(6)))
        elif tag == '0c':
            b = sp.R(pl)
            cid = b.read_u16(); fps = b.read_byte(); frames = b.read_u16()
            c1 = b.read_i32()
            tris = [(b.read_u16(), b.read_u16(), b.read_u16()) for _ in range(c1)]
            c2 = b.read_i16()
            children = [[b.read_i16(), None, None] for _ in range(c2)]
            for j in range(c2):
                children[j][2] = b.read_byte()
            for j in range(c2):
                ln = b.read_byte()
                children[j][1] = None if ln >= 255 else b.read(ln).decode('utf-8', 'replace')
            clips[cid] = dict(id=cid, fps=fps, frames=frames, tris=tris, children=children)
        elif tag == '12':
            b = sp.R(pl)
            sid = b.read_u16(); nreg = b.read_u16(); npts = b.read_u16()
            regs = []
            for _ in range(nreg):
                if b.read(1).hex() != '16':
                    break
                b.read_u32()
                sheet = b.read_byte()
                npnt = b.read_byte()
                pts = [(b.read_i32() / 20.0, b.read_i32() / 20.0) for _ in range(npnt)]
                uvs = []
                sw, sh = sheets[sheet] if sheet < len(sheets) else (0, 0)
                for _ in range(npnt):
                    uvs.append((b.read_u16() * sw / 65535.0, b.read_u16() * sh / 65535.0))
                regs.append(dict(sheet=sheet, pts=pts, uv=uvs))
            shapes.append(dict(sid=sid, nreg=nreg, npts=npts, regs=regs))
        elif tag == '09':
            colors.append(pl)
        elif tag in ('01', '18'):
            b = sp.R(pl)
            b.read_byte()
            sheets.append((b.read_u16(), b.read_u16()))
    return dict(mats=mats, clips=clips, shapes=shapes, sheets=sheets, colors=colors,
                ids=ids, names=names)


IDENT = (np.eye(2), np.zeros(2))

CT_NONE = 0xFFFF
CT_IDENTITY = (1.0, 1.0, 1.0, 0.0, 0.0, 0.0, 1.0)


def ct_of(res, idx):
    """`09` 索引 → (mul_r, mul_g, mul_b, add_r, add_g, add_b, alpha_mul)；恒等 / 缺索引 → None。"""
    if idx == CT_NONE or idx >= len(res['colors']) or idx in CT_FORCE_IDENTITY:
        return None
    r_add, g_add, b_add, alpha, r_mul, g_mul, b_mul = res['colors'][idx]
    ct = (r_mul / 255.0, g_mul / 255.0, b_mul / 255.0,
          float(r_add), float(g_add), float(b_add), alpha / 255.0)
    return None if ct == CT_IDENTITY else ct


def compose_ct(parent, ct):
    """父件变换先于子件：out = (src * parent + parent_add) * ct + ct_add。"""
    if parent is None:
        return ct
    if ct is None:
        return parent
    return (parent[0] * ct[0], parent[1] * ct[1], parent[2] * ct[2],
            parent[3] * ct[0] + ct[3], parent[4] * ct[1] + ct[4], parent[5] * ct[2] + ct[5],
            parent[6] * ct[6])


def apply_ct(px, ct):
    """对 (h, w, 4) 的 float 区域就地施加颜色变换（RGB 乘加，alpha 乘）。"""
    if ct is None:
        return
    px[..., 0] = px[..., 0] * ct[0] + ct[3]
    px[..., 1] = px[..., 1] * ct[1] + ct[4]
    px[..., 2] = px[..., 2] * ct[2] + ct[5]
    px[..., 3] = px[..., 3] * ct[6]
    np.clip(px, 0.0, 255.0, out=px)


def to_affine(raw):
    a, b, c, d, tx, ty = raw
    return (np.array([[a / 1024.0, c / 1024.0], [b / 1024.0, d / 1024.0]]),
            np.array([tx / 20.0, ty / 20.0]))


def compose(m1, m2):
    return (m1[0] @ m2[0], m1[0] @ m2[1] + m1[1])


def apply(m, p):
    return m[0] @ np.array(p, float) + m[1]


def shape_of(res, sid):
    for sh in res['shapes']:
        if sh['sid'] == sid:
            return sh
    return None


def walk(res, cid, m, cb, ct=None, depth=0, max_depth=8):
    clip = res['clips'][cid]
    for (ci, mi, coi) in clip['tris']:
        if ci >= len(clip['children']):
            continue
        oid = clip['children'][ci][0]
        mm = m if (mi == 65535 or mi >= len(res['mats'])) else compose(m, to_affine(res['mats'][mi]))
        cc = compose_ct(ct, ct_of(res, coi))
        if oid in res['clips'] and depth < max_depth:
            walk(res, oid, mm, cb, cc, depth + 1, max_depth)
        else:
            sh = shape_of(res, oid)
            if sh is None:
                continue
            for reg in sh['regs']:
                cb(mm, reg, cc)


def draw_region(canvas, atlas, dst, uv, ct=None):
    h, w = canvas.shape[:2]
    xs = [p[0] for p in dst]; ys = [p[1] for p in dst]
    x0 = max(0, int(np.floor(min(xs)))); x1 = min(w, int(np.ceil(max(xs))) + 1)
    y0 = max(0, int(np.floor(min(ys)))); y1 = min(h, int(np.ceil(max(ys))) + 1)
    if x1 - x0 < 1 or y1 - y0 < 1:
        return False
    mask_img = Image.new('L', (x1 - x0, y1 - y0), 0)
    ImageDraw.Draw(mask_img).polygon([(p[0] - x0, p[1] - y0) for p in dst], fill=255)
    mask = np.asarray(mask_img, bool)
    if not mask.any():
        return False

    yy, xx = np.mgrid[y0:y1, x0:x1]
    px = (xx + 0.5).astype(np.float64); py = (yy + 0.5).astype(np.float64)
    tmp = np.zeros((y1 - y0, x1 - x0, 4), np.float32)
    covered = np.zeros((y1 - y0, x1 - x0), bool)
    n = len(dst)
    for i in range(1, n - 1):
        d0, d1, d2 = dst[0], dst[i], dst[i + 1]
        den = ((d1[1] - d2[1]) * (d0[0] - d2[0]) + (d2[0] - d1[0]) * (d0[1] - d2[1]))
        if abs(den) < 1e-9:
            continue
        a = np.array([[d0[0], d0[1], 1], [d1[0], d1[1], 1], [d2[0], d2[1], 1]], float)
        try:
            cu = np.linalg.solve(a, np.array([uv[0][0], uv[i][0], uv[i + 1][0]], float))
            cv = np.linalg.solve(a, np.array([uv[0][1], uv[i][1], uv[i + 1][1]], float))
        except np.linalg.LinAlgError:
            continue
        su = cu[0] * px + cu[1] * py + cu[2]
        sv = cv[0] * px + cv[1] * py + cv[2]
        t0 = ((d1[0] - px) * (d2[1] - py) - (d2[0] - px) * (d1[1] - py)) / den
        t1 = ((d2[0] - px) * (d0[1] - py) - (d0[0] - px) * (d2[1] - py)) / den
        t2 = 1 - t0 - t1
        inside = (t0 >= -0.002) & (t1 >= -0.002) & (t2 >= -0.002)
        if not inside.any():
            continue
        iu = np.clip(su.astype(int), 0, atlas.shape[1] - 1)
        iv = np.clip(sv.astype(int), 0, atlas.shape[0] - 1)
        tmp[inside] = atlas[iv, iu][inside]
        covered |= inside
    if not covered.any():
        return False

    apply_ct(tmp, ct)
    alpha = np.repeat(tmp[..., 3:4] / 255.0, 4, axis=2)
    reg = canvas[y0:y1, x0:x1]
    blend = tmp * alpha + reg * (1 - alpha)
    sel = (covered & mask)[..., None] & (alpha > 0)
    reg[sel] = blend[sel]
    canvas[y0:y1, x0:x1] = reg
    return True


def uv_key(reg):
    uv = reg['uv']
    return (round(min(u for u, v in uv)), round(min(v for u, v in uv)),
            round(max(u for u, v in uv)), round(max(v for u, v in uv)))


def composite():
    """按 display list 合成整幅底图。

    返回 (配色后 RGBA uint8 画布, 配色前画布, 画布原点在 stage px 的偏移)。配色前画布只用于几何校验
    （配色是逐像素加性偏移，不改几何；校验里的「金色横带」判据在图集原色上成立）。
    """
    res = parse_sc(SC)
    atlas = np.asarray(Image.open(ATLAS).convert('RGBA'), np.float32)
    cid = res['ids'][res['names'].index(CLIP_SRC)]

    items = []
    walk(res, cid, IDENT, lambda m, reg, ct: items.append((m, reg, ct)))
    kept = [(m, reg, ct) for (m, reg, ct) in items if uv_key(reg) not in EXCLUDE_UV]
    if not kept:
        raise SystemExit('!! 合成项为 0：%s' % SC)

    boxes = np.vstack([[apply(m, p) for p in reg['pts']] for m, reg, _ct in kept])
    minx, miny = boxes.min(axis=0)
    w = int(np.ceil(boxes[:, 0].max() - minx)) + 2
    h = int(np.ceil(boxes[:, 1].max() - miny)) + 2
    canvas = np.zeros((h, w, 4), np.float32)
    drawn = 0
    for m, reg, ct in kept:
        dst = [(p[0] - minx, p[1] - miny) for p in (apply(m, q) for q in reg['pts'])]
        if draw_region(canvas, atlas, dst, reg['uv'], ct):
            drawn += 1
    print('合成：%s（clip %d）绘制 %d / 取件 %d，带颜色变换 %d 件，画布 %dx%d，原点 (%.1f, %.1f)'
          % (CLIP_SRC, cid, drawn, len(items), sum(1 for _m, _r, c in kept if c),
             w, h, minx, miny))
    raw = np.clip(canvas, 0, 255).astype(np.uint8)
    return palette_shift(raw), raw, float(minx), float(miny)


def build_asset():
    canvas, raw, ox, oy = composite()
    img = Image.fromarray(canvas)
    # FIELD_* / CROP_* 都是合成画布坐标（画布左上 = 0,0），不是 stage px。
    crop = img.crop((CROP_X0, CROP_Y0, CROP_X1, CROP_Y1))

    sx = 18.0 * TILE_W_PX / (FIELD_X1 - FIELD_X0)
    sy = 32.0 * TILE_H_PX / (FIELD_Y1 - FIELD_Y0)
    out_w = int(round(crop.width * sx))
    out_h = int(round(crop.height * sy))
    resized = crop.resize((out_w, out_h), Image.LANCZOS)
    left = (FIELD_X0 - CROP_X0) * sx
    top = (FIELD_Y0 - CROP_Y0) * sy
    return resized, left, top, raw, ox, oy


def king_platform_rows(canvas):
    """量画面中线附近（画布 x 980..1160）金色横带的中心 y，用来校验竖向定标。"""
    a = canvas.astype(int)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    gold = (al > 200) & (r > 180) & (g > 120) & (b < 110)
    band = gold[:, 980:1160]
    rows = np.nonzero(band.sum(axis=1) > 40)[0]
    if len(rows) == 0:
        return []
    groups, cur = [], [rows[0]]
    for y in rows[1:]:
        if y - cur[-1] <= 2:
            cur.append(y)
        else:
            groups.append(cur); cur = [y]
    groups.append(cur)
    return [float(np.mean(gp)) for gp in groups]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--check', action='store_true', help='只校验已有产物与几何，不重写')
    ap.add_argument('--ct-off', default='', help='逗号分隔的 `09` 序号：按恒等处理（逐件回退）')
    ap.add_argument('--palette-off', default='', help='逗号分隔的材质名（grass/path/wall/water/bank）：保持图集原色')
    args = ap.parse_args()

    CT_FORCE_IDENTITY.update(int(x) for x in args.ct_off.split(',') if x.strip())
    PALETTE_OFF.update(x.strip() for x in args.palette_off.split(',') if x.strip())

    img = None
    if not args.check:
        img, left, top, canvas, ox, oy = build_asset()
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        img.save(OUT)
        print('写出 %s（%dx%d）' % (OUT, img.width, img.height))
        print('ArenaView 常量：RoyalFieldPngW=%d RoyalFieldPngH=%d RoyalFieldPxLeft=%.1f RoyalFieldPxTop=%.1f'
              % (img.width, img.height, left, top))
    else:
        _canvas, canvas, ox, oy = composite()
        sx = 18.0 * TILE_W_PX / (FIELD_X1 - FIELD_X0)
        sy = 32.0 * TILE_H_PX / (FIELD_Y1 - FIELD_Y0)
        left = (FIELD_X0 - CROP_X0) * sx
        top = (FIELD_Y0 - CROP_Y0) * sy

    ok = True

    def chk(cond, msg):
        nonlocal ok
        print(('  PASS  ' if cond else '  FAIL  ') + msg)
        if not cond:
            ok = False

    if not os.path.isfile(OUT):
        print('FAIL 产物不存在：%s' % OUT)
        return 1
    png = Image.open(OUT).convert('RGBA')
    chk(png.width > 1000 and png.height > 1400, 'PNG 覆盖 18×32 格场地 + 水岸外圈（%dx%d）' % png.size)
    alpha = np.asarray(png)[..., 3]
    chk((alpha > 0).mean() > 0.98, 'PNG 不透明占比 %.3f（水岸外圈无空边）' % (alpha > 0).mean())

    anchors = king_platform_rows(canvas)
    chk(len(anchors) >= 2, '量到国王塔台中心行（%s）' % [round(v, 1) for v in anchors])
    if len(anchors) >= 2:
        sy_px = (FIELD_Y1 - FIELD_Y0) / 32.0
        tiles = sorted(32.0 - (y - FIELD_Y0) / sy_px for y in (anchors[0], anchors[-1]))
        chk(abs(tiles[0] - 3.0) < 0.35 and abs(tiles[1] - 29.0) < 0.35,
            '国王塔台落在格 y %.2f / %.2f（目标 3 / 29）' % (tiles[0], tiles[1]))

    print('  INFO  场地矩形在 PNG 里 = (%.1f, %.1f)..(%.1f, %.1f)'
          % (left, top, left + 18 * TILE_W_PX, top + 32 * TILE_H_PX))
    print('---- 结论: %s ----' % ('全部 PASS' if ok else '存在 FAIL'))
    return 0 if ok else 1


if __name__ == '__main__':
    raise SystemExit(main())
