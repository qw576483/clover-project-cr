# -*- coding: utf-8 -*-
"""D155：量「参考图 vs 原版图集」的**色偏方向**。

为什么要量这个
    第三片把页签底从"纯色方块"换成了**原版图元 + 实测 tint**（`TabOnTint` / `TabOffTint`），
    口径沿用本仓既有约定（`CrUiStyle.ButtonBlueTint` = 参考图读数 ÷ 源帧内填色）。
    这条口径成立的前提是：参考图与图集之间的差异是**同向的**（否则 tint 只是"换个错法"）。
    ⇒ 本探针把三处**已知同族**的部位各自均值与图集 `ui_out` 166 的内填色 `(76,172,255)` 比，
       看比值是不是**同向**（G / B 一律下压）。

⛔ 方法与边界（第一版是错的，原样留档）
    第一版写成"逐像素找图集里最近的像素（min|ΔRGB| ≤ 60）来判'这个部位是哪颗图元'" ⇒ 报红。
    **是方法错了**：一个部位的均值色在源帧里总能找到某个更暗/更亮的像素当"最近邻"
    （实测 7 个采样点里 4 个的最近邻落在帧上更暗的斜面上），逐像素最近邻判不出"是哪个图元"。
    该问题已由 `d155-tab-pieces.py` 用**原件剖面逐像素对比**正确回答（166 内侧行 == 267，Δ=10）。
    ⚠️ 本探针因此**只**回答色偏方向，⛔ 不再对"是哪个图元"下结论。

判据（能失败）
    P1  三处部位的**均值色**相对 166 内填色 `(76,172,255)`，G 通道比值一律 ≤ 0.90（下压）；
    P2  同上，B 通道比值一律 ≤ 0.90；
    P3  反控：166 **自身**的比值必须是 (1.00, 1.00, 1.00)（否则说明取样口径错了）。
"""
import os
import sys

import numpy as np
from PIL import Image

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
REF = os.path.join(ROOT, '策划', '参考图', '07_卡组编辑_1242x2208.jpg')
F166 = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Ui', 'Buttons', 'ui_out', 'frame_166.png')

FILL = np.array([76.0, 172.0, 255.0])      # 166 的**内填像素** = 源帧 (c−1, c−1)、c = BlueCorner = 19
#   （= `CrUiStyle.ButtonBlueTint` 的分母，⛔ 不是全帧不透明像素均值 —— 后者含四角高光与描边）
C_BLUE = 19
#   部位（@1242，**只取钮板/签体的纯填充带**，避开文字与图标；出处见 `几何量取.md` §1.6.8 E65..E69）
SPOTS = [
    ('页签 选中(Decks) 签体', (150, 290, 45, 150)),
    ('页签 未选中(Collection) 签体', (660, 725, 100, 150)),
    ('底行 工具钮1 钮板（左缘内）', (786, 800, 1390, 1440)),
]


def mean_region(a, x0, x1, y0, y1):
    p = a[y0:y1, x0:x1].reshape(-1, 3)
    return p.mean(axis=0)


def main():
    ref = np.asarray(Image.open(REF).convert('RGB')).astype(float)
    f166 = np.asarray(Image.open(F166).convert('RGBA')).astype(float)
    own = f166[C_BLUE - 1, C_BLUE - 1, :3]          # 内填像素（与 FILL 同口径）

    print('166 内填像素 (c−1,c−1)=(%d,%d) = #%02X%02X%02X %s；探针常数 FILL = %s'
          % (C_BLUE - 1, C_BLUE - 1, int(own[0]), int(own[1]), int(own[2]),
             np.round(own, 0).astype(int), FILL.astype(int)))
    print()
    red, green = [], []

    print('%-30s %-22s %s' % ('部位', '参考图均值', '÷ 166 内填色 的比值'))
    print('-' * 84)
    for lab, box in SPOTS:
        m = mean_region(ref, *box)
        r = m / FILL
        print('%-30s #%02X%02X%02X %-12s (%.3f, %.3f, %.3f)' % (
            lab, int(m[0]), int(m[1]), int(m[2]), np.round(m, 0).astype(int), r[0], r[1], r[2]))
        if r[1] > 0.90:
            red.append('%s 的 G 比值 %.3f > 0.90（没下压）' % (lab, r[1]))
        if r[2] > 0.90:
            red.append('%s 的 B 比值 %.3f > 0.90（没下压）' % (lab, r[2]))

    # P3 反控：166 内填像素自己 / FILL == 1.000（若 ≠ 1 说明常数与帧口径不一致）
    r_own = own / FILL
    print('%-30s #%02X%02X%02X %-12s (%.3f, %.3f, %.3f)  ← P3 反控' % (
        '（反控）166 内填像素自己', int(own[0]), int(own[1]), int(own[2]),
        np.round(own, 0).astype(int), r_own[0], r_own[1], r_own[2]))
    if max(abs(r_own - 1.0)) > 0.05:
        red.append('反控失败：166 自己对自己的比值 %.3f 不等于 1.000 ⇒ 取样口径错了' % r_own.max())

    print()
    if red:
        for m in red:
            print('  NG  %s' % m)
        print()
        print('判据 3 条，红 %d 条' % len(red))
        print('Verdict: FAIL')
        return 1
    print('  OK  P1/P2：三处部位的 G、B 比值**一律 ≤ 0.90**（参考图相对图集系统性下压）')
    print('  OK  P3  反控：166 对自己的比值 = 1.000 ⇒ 取样口径正确')
    print()
    print('判据 3 条，红 0 条')
    print('Verdict: PASS（参考图与图集之间是**同向**色偏 ⇒ 「原版图元 + 实测 tint」这条口径成立；'
          '⛔ 本探针不对"是哪个图元"下结论）')
    return 0


if __name__ == '__main__':
    sys.exit(main())
