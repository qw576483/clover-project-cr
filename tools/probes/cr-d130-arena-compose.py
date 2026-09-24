#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d130-arena-compose.py -- D130 判据资产：用 `arena_training` 的**原始层**离线拼出竞技场
========================================================================================
用户判词 6「地图不对，路连不上」的判据脚本。

结论（本脚本自己量、不靠记忆；量法见 print 输出）
------------------------------------------------
1. `arena_training_v215.sc` 的导出表 23 条里，**竞技场地面 = `training_area_bg`**（export id 48，
   唯一子件 = shape 22 ⇒ `arena_training_out/arena_training_sprite_22.png` = 工程 `frame_022.png`）。
   它的 alpha 把内容分成 3 块（画布行区间）：`(0,55)` 后沿外草边 / `(492,996)` 近半场 / `(1083,1600)` 远半场。
   ⛔ 旧实现把 `py 646..1642` **跨过 (996,1083) 那道透明缝**整条裁下来 ⇒ 画面里那两条横向接缝的根因。
2. 两块半场的**车道中心 x 都是 259 / 760**（间距 501 px = 11 格，与 `GameConst.BridgeCxATile=3.5` /
   `BridgeCxBTile=14.5` 同源）⇒ **45.545 px/格(x)**，格 x=0 ⇔ px 99（18 格 = 99..918，与远半场草地实测
   bbox 99..912 互证）。
3. 唯一的**蓝色水面**素材 = `atlasgenerator_texture_rgb565`（= shape 6 ⇒ `frame_006.png`）：
   水面行 805..852、横向 px 208..1027，均色自上而下 RGB(0,127,141)→(0,166,205)（带渐变）。
   shape 22 的河道**不带蓝水**（那 47 行蓝只在场地左右之外，px 33..972 的两端），
   场地内是「远岸草 + 木栏/灰石 + 桥板」——正是原版河岸那一层装饰。
4. 河口装饰（木栏 / 灰石 / 桥板）取自 shape 22 近半场块顶部：
   木栏+灰石行 574..612、桥板行 583..692（左车道 x 226..302）。
   ⚠️ D130 后续修正（以 `ArenaView.cs` 落地值为准，本脚本头注保留原始读数不删）：桥板**上沿由 583 改 604**
   —— `cr-d130-rows2.py` 复量得 583..606 是**河岸泥带 MUD**，取进去会在河面上叠出一条棕色横带；
   竖板束的第一行才是 604。另 `RedFieldPyBottom` 由 1634 改 **1600**（行 1600 仍有 804 个不透明像元、
   1601 起骤降到 26 ⇒ 场地内容止于 1600），否则河上方留一条 0.5 格透明黑带。
   六段配方与行/格定标见 `ArenaView.cs:357-441` 的 D130 长注释。

复跑
----
  C:/Python312/python tools/probes/cr-d130-arena-compose.py --root . --out .ai-tmp/test/D130-compose
"""
import argparse
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

from PIL import Image  # noqa: E402

# ── 原版像素常量（全部由本脚本 / cr-d130-arena-layers.py 实测） ──
PX_PER_TILE_X = 45.545       # 车道中心 259 / 760 间距 501 px = 11 格
FIELD_LEFT_PX = 99.0         # 格 x=0 ⇔ 画布 px 99
FIELD_RIGHT_PX = 918.0       # 格 x=18 ⇔ 画布 px 918（= 99 + 18×45.545，实测草地右沿 912）

# shape 22 的块（画布行区间，y 向下）
BLK_FAR = (1083, 1600)       # 远半场：首行 = 场地后沿（格 32）
BLK_NEAR = (645, 996)        # 近半场地面：首行 = 靠河侧（格 15 的上沿）
RAIL = (578, 608)            # 木栏 + 灰石（河道中央那一层）
BRIDGE = (583, 692)          # 桥板（左车道位那一束）
BRIDGE_TILE_Y = (14.2, 17.0) # 桥板落位：跨过河、并压住近半场自己那截桥板
BRIDGE_X = (226, 302)        # 桥板那一束的 x（中心 px 264 ≈ 格 3.62）

# shape 6 的蓝色水面
WATER_ROWS = (806, 853)
WATER_LEFT_PX = 208.0        # 车道中心 367/868 ⇒ 格 0 ⇔ 208
WATER_RIGHT_PX = 208.0 + 18 * PX_PER_TILE_X

RIVER_TOP_TILE = 17.0        # GameConst.RiverBottomTile（格 y 大 = RED 后方）
RIVER_BOT_TILE = 15.0        # GameConst.RiverTopTile
BRIDGE_CX_A = 3.5            # GameConst.BridgeCxATile
BRIDGE_CX_B = 14.5           # GameConst.BridgeCxBTile


def crop(img, px0, py0, px1, py1, tx0, tx1, ty0, ty1, ppt):
    """按画布矩形裁一块，铺到格矩形（px→格 两轴就地拉伸）。"""
    sub = img.crop((int(round(px0)), int(round(py0)), int(round(px1)), int(round(py1))))
    return sub.resize((max(1, int(round((tx1 - tx0) * ppt))), max(1, int(round((ty1 - ty0) * ppt)))),
                      Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--out', default='.ai-tmp/test/D130-compose')
    ap.add_argument('--px-per-tile', type=float, default=60.0)
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    out = os.path.join(root, a.out) + '.png'
    os.makedirs(os.path.dirname(out), exist_ok=True)

    fdir = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', 'arena_training_out')
    s22 = Image.open(os.path.join(fdir, 'arena_training_sprite_22.png')).convert('RGBA')
    s06 = Image.open(os.path.join(fdir, 'arena_training_sprite_06.png')).convert('RGBA')

    ppt = a.px_per_tile
    canvas = Image.new('RGBA', (int(18 * ppt), int(32 * ppt)), (0, 0, 0, 0))

    # 像素行 0 = 格 y=32（RED 后方，屏幕上方）；像素行 H = 格 y=0。
    def blit_top(im, tile_x0, tile_y_top):
        canvas.alpha_composite(im, (int(round(tile_x0 * ppt)), int(round((32 - tile_y_top) * ppt))))

    # ① 远半场（格 y 17..32）← shape22 (1083,1600)，块首行 = 后沿 = 格 32
    blit_top(crop(s22, FIELD_LEFT_PX, BLK_FAR[0], FIELD_RIGHT_PX, BLK_FAR[1],
                  0, 18, 17, 32, ppt), 0, 32)
    # ② 近半场地面（格 y 0..15）← shape22 (645,996)，块首行 = 格 15
    blit_top(crop(s22, FIELD_LEFT_PX, BLK_NEAR[0], FIELD_RIGHT_PX, BLK_NEAR[1],
                  0, 18, 0, 15, ppt), 0, 15)
    # ③ 蓝水（格 y 15..17）← shape6 水面行，块首行 = 河上沿 = 格 17
    blit_top(crop(s06, WATER_LEFT_PX, WATER_ROWS[0], WATER_RIGHT_PX, WATER_ROWS[1],
                  0, 18, RIVER_BOT_TILE, RIVER_TOP_TILE, ppt), 0, RIVER_TOP_TILE)
    # ④ 木栏 + 灰石（画在蓝水之上，居河道中央）← shape22 (574,612)
    blit_top(crop(s22, FIELD_LEFT_PX, RAIL[0], FIELD_RIGHT_PX, RAIL[1],
                  0, 18, 15.6, 16.4, ppt), 0, 16.4)
    # ⑤ 两座桥的桥板（原版左车道那一束，两次落位）← shape22 行 (583,692) × 列 (226,302)
    half = (BRIDGE_X[1] - BRIDGE_X[0]) / PX_PER_TILE_X / 2.0     # 1.67/2 = 0.835 格
    for cx in (BRIDGE_CX_A, BRIDGE_CX_B):
        blit_top(crop(s22, BRIDGE_X[0], BRIDGE[0], BRIDGE_X[1], BRIDGE[1],
                      cx - half, cx + half, BRIDGE_TILE_Y[0], BRIDGE_TILE_Y[1], ppt), cx - half, BRIDGE_TILE_Y[1])

    canvas.convert('RGB').save(out)
    print('COMPOSE -> %s  (%dx%d, %.1f px/tile)' % (out, canvas.width, canvas.height, ppt))
    return 0


if __name__ == '__main__':
    sys.exit(main())
