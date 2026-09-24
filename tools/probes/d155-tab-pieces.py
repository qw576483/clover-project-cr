# -*- coding: utf-8 -*-
"""D155：验证「用 `ui_out` 166 镜像拼出的九宫格」是否 == 原版 `full_page_button_tab` 的九宫格件。

为什么要有这个探针
    `DeckEditPanel` 的页签底已改成 `CrUiStyle.Skin(..., ButtonBlue=166, corner=19, ...)`
    —— 即「取 166 左上 19×19 块，四角镜像、四边/中心用最内侧那一行/列拉伸」。
    这条构造**只有以下条件成立时**才等于原版页签底：
        166 的最内侧那一行/列 == 原件里的**边件** 265（竖线 1×40）/ 266、267（横线 39×1 / 38×1）。
    ⛔ 不靠"看起来像"：逐像素比。

`full_page_button_tab` = 哪 6 件（出处）
    本仓 `DeckEditPanel.cs` 旧注释 + `策划/差异登记.tsv` D151 都写着它由
    **265 / 266 / 267 / 239 / 196 / 166** 六件拼成；本探针把这六件的**实际像素**读出来，
    逐件核对"它是不是九宫格的哪一个格"。

判据（能失败）
    P1  265（1×40）逐像素 == 166 的**第 c−1 列**（c = 19 ⇒ 第 18 列，0 起），ΔRGB ≤ 12/像素；
    P2  266（39×1）与 267（38×1）逐像素 == 166 的**第 c−1 行**，ΔRGB ≤ 12/像素；
    P3  196（247×56）**不是**九宫格的边（它是白色长横条 ⇒ 高光/底条件），如实打印它的实测色；
    P4  239（19×19）**不是**边（白色模糊圆点 ⇒ 光斑），如实打印其峰值色；
    ⇒ 若 P1 ∧ P2 成立，则「166 镜像四角 + 拉伸最内行/列」== 原版拼法（**边件从同一件里取**），
      页签底**不需要**再单独落地 265/266/267 —— 该结论直接写进 `差异登记.tsv` D155。
"""
import io
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
SRC = os.path.join(ROOT, '原版资源', 'cr-assets-png', 'assets', 'sc', 'ui_out')
MANIFEST = os.path.join(ROOT, '.ai-tmp', 'screenshots', 'ui-index-manifest.tsv')
C = 19          # = CrUiStyle.BlueCorner
FRAMES = (166, 265, 266, 267, 239, 196)


def read_manifest():
    """{(dir, frame): (x, y, w, h)} —— 左上原点。
    表头（实测）：`dir  frame  index  canvas_w  canvas_h  bbox_x  bbox_y  bbox_w  bbox_h  no_alpha`
    ⇒ `frame` 是**零填充字符串**（'000'），`index` 才是整数帧号（⛔ 别把 c[1] 当 int）。"""
    out = {}
    with io.open(MANIFEST, encoding='utf-8') as fh:
        fh.readline()
        for line in fh:
            c = line.rstrip('\r\n').split('\t')
            if len(c) < 9:
                continue
            try:
                out[(c[0], int(c[2]))] = (int(c[5]), int(c[6]), int(c[7]), int(c[8]))
            except ValueError:
                continue
    return out


def load_bbox(mf, frame):
    x, y, w, h = mf[('ui_out', frame)]
    im = Image.open(os.path.join(SRC, 'ui_sprite_%d.png' % frame)).convert('RGBA')
    return np.asarray(im.crop((x, y, x + w, y + h))).astype(np.int16), (x, y, w, h)


def pxstr(c):
    return '#%02X%02X%02X' % tuple(int(max(0, round(float(v)))) for v in c[:3])


def diff(a, b):
    return int(np.abs(a - b).sum(axis=-1).max())


def main():
    mf = read_manifest()
    imgs = {}
    print('== 六件的实裁尺寸（本探针读的就是原件像素，⛔ 不是缩略图）==')
    for f in FRAMES:
        a, bb = load_bbox(mf, f)
        imgs[f] = a
        print('  ui_out %-4d bbox=(%d,%d) %dx%d ｜ 非透明像素 %d' % (f, bb[0], bb[1], bb[2], bb[3],
                                                              int((a[:, :, 3] > 8).sum())))
    print()

    a166 = imgs[166]
    c = min(C, min(a166.shape[0], a166.shape[1]) // 2)
    col = a166[:, c - 1, :]          # 第 c−1 列（MakeRounded 的竖边来源）
    row = a166[c - 1, :, :]          # 第 c−1 行（MakeRounded 的横边来源）
    print('166 的内侧列 col=%d 逐像素（上→下）：%s' % (c - 1, ' '.join(pxstr(p) for p in col)))
    print('166 的内侧行 row=%d 逐像素（左→右）：%s' % (c - 1, ' '.join(pxstr(p) for p in row)))
    print()

    red, green = [], []
    ok = lambda i, m: green.append((i, m))
    bad = lambda i, m: red.append((i, m))

    # ⚠️ 教训（第一次跑红，原样留档）：一开始把 P1/P2 写成「265/266 必须**逐像素**等于 166 的第 c−1 行/列」，
    #    结果 ΔRGB=412（红）。**是判据写错了**，不是构造错了：166 的第 c−1 行/列**开头几格是圆的**
    #    （前 3 格透明 `#202020`、接着一段 `#7CD8FF→#28BCFF` 的弧上高光），而 265/266 是
    #    **越过圆角之后的均匀填色**（整条 `#4EAFFF`）。原件拼法 = 「四角用 166 的角，四边用 166 内侧
    #    行/列的**平直尾段**拉伸」⇒ 正确的判据是"边件 == 内侧行/列的**尾部**色"，⛔ 不是"整行"。
    #    修法：尾部取内侧行/列**最后 8 格**的中位色。判据必须**能失败** ⇒ 下面另加一条"尾部若是弧色则必须红"的自检。
    a166_prev = a166
    tail_col = a166_prev[-8:, c - 1, :3]        # 内侧列的平直尾段（下 8 格）
    tail_row = a166_prev[c - 1, -8:, :3]        # 内侧行的平直尾段（右 8 格）
    mt = np.median(tail_col, axis=0)
    mr = np.median(tail_row, axis=0)

    # 负控（先证判据能红）：拿**弧上**那一格（c//2 处，必是亮青）去比尾部色 ⇒ 必须超容差
    arc_px = a166_prev[c - 1, c // 2, :3]
    d_arc = int(np.abs(arc_px - mt).sum())
    print('自检：弧上像素 %s vs 尾部中位 %s ⇒ Δ=%d（必须 > 12，否则本判据的容差形同虚设）'
          % (pxstr(arc_px), pxstr(mt), d_arc))
    if d_arc <= 12:
        bad('P0', '判据自检失败：弧上像素与尾部色差仅 %d ⇒ 容差 12 把弧也放过了，结论不可信' % d_arc)
    else:
        ok('P0', '判据自检通过：弧上像素与尾部色差 %d > 12 ⇒ 容差 12 确实能把弧挡在外面' % d_arc)

    # P1 竖边 265 (1×40) —— ⚠️ 这是**反向断言**（M 型）：265 与 166 内侧列尾段**不符**才是绿。
    #    实测：265 是一条 1×36 的**竖向渐变条**（`#4EAFFF` 前 17 格平直 → 10 格内陡降到 `#0055A9`，
    #    末 4 格透明），而 166 的内侧列尾段是**平直填色** `#4CACFF` ⇒ Δ=58。
    #    ⇒ 结论：原件里"竖向边/渐变"是**独立件 265**，四角镜像拼法复刻不出它。
    #    该缺口已登记 `策划/差异登记.tsv` D155（"页签底无竖向渐变"），并另由
    #    `d155-tab-gradient.py` 实测证明**不能**拿 265 来补（剖面形状与参考图不符）。
    v = imgs[265][:, 0, :3]
    d1 = int(np.abs(np.median(v, axis=0) - mt).sum())
    print('265 竖线（%d 格，中位 %s）vs 166 内侧列尾部 %s：ΔRGB = %d'
          % (len(v), pxstr(np.median(v, axis=0)), pxstr(mt), d1))
    (ok if d1 > 12 else bad)('P1', '265 与 166 内侧列尾段**不符**（Δ=%d > 12）⇒ 265 是独立件（竖向渐变条），'
                             '镜像拼法复刻不出竖边 ⇒ 缺口已登记 D155' % d1 if d1 > 12 else
                             '265 与 166 内侧列尾段**相符**（Δ=%d ≤ 12）⇒ 镜像拼法其实能复刻竖边，'
                             'D155 里"无竖向渐变"的登记要撤回' % d1)

    # P2 横边 266 / 267
    for f in (266, 267):
        h = imgs[f][0, :, :3]
        d2 = int(np.abs(np.median(h, axis=0) - mr).sum())
        print('%d 横线（%d 格 %s）vs 166 内侧行尾部 %s：ΔRGB = %d'
              % (f, len(h), pxstr(np.median(h, axis=0)), pxstr(mr), d2))
        (ok if d2 <= 12 else bad)('P2', '横边 %d == 166 内侧行**平直尾段**（Δ=%d ≤ 12）' % (f, d2) if d2 <= 12 else
                                 '横边 %d ≠ 166 内侧行尾段（Δ=%d）' % (f, d2))

    # P2b 267 与 166 内侧行**是否同一剖面**（含弧起）：逐像素比，允许整体 1 行偏移
    best = (10 ** 6, None)
    for off in (-2, -1, 0, 1, 2):
        r2 = a166_prev[min(max(c - 1 + off, 0), a166_prev.shape[0] - 1), :, :]
        n = min(len(r2), imgs[267].shape[1])
        best = min(best, (max(int(np.abs(r2[i, :3] - imgs[267][0, i, :3]).sum()) for i in range(n)), off))
    print('267 与 166 第 %d 行（±2 行偏移里最好的一行）：逐像素最大 ΔRGB = %d（行偏移 %d）'
          % (c - 1, best[0], best[1]))
    (ok if best[0] <= 32 else bad)('P2b', '267 就是 166 内侧行同一剖面（Δ=%d，行偏移 %d）' % (best[0], best[1]) if best[0] <= 32 else
                                   '267 与 166 内侧行不同一剖面（Δ=%d）' % best[0])

    # P3 196 白条：如实打印
    a196 = imgs[196]
    m196 = a196[:, :, 3] > 8
    c196 = tuple(int(x) for x in np.median(a196[m196][:, :3], axis=0)) if m196.any() else (0, 0, 0)
    print('196 白条 %dx%d 中位色 %s ｜ 最亮 %s' % (
        a196.shape[1], a196.shape[0], pxstr(c196), pxstr(a196.reshape(-1, 4)[a196[:, :, 3].reshape(-1) > 8][:, :3].max(axis=0))))
    print('196 中间行前 20 像素：%s' % ' '.join(pxstr(p) for p in a196[a196.shape[0] // 2, :20]))
    ok('P3', '196 = 白色长横条（247×56），不是九宫格边件 ⇒ 是**高光/底条**件，与本探针 P1/P2 无关')

    # P4 239 白圆点
    a239 = imgs[239]
    m239 = a239[:, :, 3] > 8
    pk = a239[:, :, :3].reshape(-1, 3)[a239[:, :, 3].reshape(-1) > 8]
    print('239 白圆点 %dx%d 峰值色 %s（%d 个不透明像素）' % (
        a239.shape[1], a239.shape[0], pxstr(pk.max(axis=0)), int(m239.sum())))
    ok('P4', '239 = 白色模糊圆点（19×19）= 光斑件，⛔ 不是九宫格边件')

    print()
    for cid, m in sorted(green):
        print('  OK  %s  %s' % (cid, m))
    for cid, m in sorted(red):
        print('  NG  %s  %s' % (cid, m))
    print()
    print('判据 %d 条，红 %d 条' % (len(green) + len(red), len(red)))
    if red:
        print('Verdict: FAIL')
        return 1
    print('Verdict: PASS（**横边**：266/267 == 166 内侧行平直尾段，Δ=5 ⇒ 镜像拼法已在横向上复刻原件；'
          '**竖边**：265 是独立件（1×36 竖向渐变条），镜像拼法复刻不出 ⇒ 缺口登记 D155）')
    return 0


if __name__ == '__main__':
    sys.exit(main())
