# -*- coding: utf-8 -*-
# direct-fix: D151 原版 UI 复刻 —— 逐带**数值**差值（判据资产，非交付物）
"""d151-band-diff.py -- 把原版 `07` 的每条带边界与我们 `DeckEditPanel` 的常量**逐条**对上，
算差值，并按容差给 PASS/FAIL。⛔ 不开 Unity，⛔ 不靠肉眼。

为什么要有它
------------
`d151-layout-preview.py` 只给**并排图**（人看，会有"看着差不多"的错觉）。
本脚本给**数字**：每条带的上/下边界在原版里落在哪、我们写的是哪、差多少像素。

口径（★ 第三片整段重写，前两版都栽在口径上）
--------------------------------------------
**一切量取都在原分辨率 1242×2208 上做，⛔ 不缩放。** 理由：缩放到 1080 会**重采样**，
把 1~2px 的边糊成 3~4px 的渐变，平台阶跃的幅度掉一半以上（本片实测：`底行钮板下缘`
在缩放图上找到的是 1449@1242 而不是 1476，差 27px）。我们的常量是 @1080，
进比较前一律 **÷ (1080/1242)** 换回 @1242 —— 这样读数与 `几何量取.md` 是**同一个坐标系**，
容差也就是那里的 **±5px@1242**，不再绕一圈换算。

原版边界怎么来（**逐带声明通道**）
----------------------------------
⛔ 一条通道打天下必然产生两类错：**死判据**（永远 FAIL）或**被邻居抢走**（找到隔壁的硬边）。
所以每条带声明自己由哪条通道看见，见 `bands` 上面的注释。

输出：控制台 + `.ai-tmp/test/D151-band-diff.txt`
复跑：`python tools/probes/d151-band-diff.py [--tol 5] [--dump]`
"""

import io
import os
import re
import sys

import numpy as np
from PIL import Image

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PANEL = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'UI', 'Panels', 'DeckEditPanel.cs')
STYLE = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'UI', 'CrUiStyle.cs')
REF = os.path.join(ROOT, u'策划', u'参考图', '07_卡组编辑_1242x2208.jpg')
OUTDIR = os.path.join(ROOT, '.ai-tmp', 'test')
OUTTXT = os.path.join(OUTDIR, 'D151-band-diff.txt')

REF_W, REF_H = 1242, 2208
DW, DH = 1080, 1920
INV = REF_W / float(DW)        # 1080 -> 1242
TOL = 5.0                      # 读数容差 @1242（`几何量取.md` 的既定口径）

IDENT = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')


# ---------------------------------------------------------------- 常量读取
def eval_consts(consts):
    """迭代求值：常量互相引用（CardStepX = CardW + ColGap）。"""
    out, pending = {}, dict(consts)
    for _ in range(14):
        if not pending:
            break
        for k in list(pending):
            e = re.sub(r'//.*$', '', pending[k]).strip()
            e = re.sub(r'(?<=[0-9])f\b', '', e)
            e = e.replace('new Color32', 'Color32').replace('new Color', 'Color')
            if IDENT.match(e):
                continue
            try:
                out[k] = eval(e, {'__builtins__': {}}, dict(out))
                del pending[k]
            except Exception:
                pass
    return out


def read_all_consts():
    pat = (
        r'private\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?'
        r'(?:float|int|double)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([^;]+);'
        r'|public\s+const\s+(?:float|int|double)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([^;]+);'
        r'|public\s+static\s+readonly\s+Vector4\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([^;]+);'
    )
    c = {}
    for p in (STYLE, PANEL):
        for m in re.finditer(pat, io.open(p, encoding='utf-8').read().replace('\r\n', '\n')):
            g = [x for x in m.groups() if x]
            if len(g) == 2:
                c.setdefault(g[0], g[1])
    return eval_consts(c)


# ---------------------------------------------------------------- 通道
def make_channels(a):
    """六条量取通道（**每条带必须声明自己由哪条通道看见**）。"""
    med = np.median(a, axis=1)                        # (H,3) 整行中位色
    v = a.max(axis=2)                                 # (H,W) 亮度 V
    mn = a.min(axis=2)
    sat = (v - mn) / np.maximum(v, 1.0) * 255.0       # (H,W) 饱和度
    gold = np.maximum(0.0, a[:, :, 0] - a[:, :, 2])   # (H,W) "含金量" R−B
    g = a[:, :, 1]                                    # (H,W) G 通道（软边块用的那一条）
    return {
        'median': lambda xr: med,
        'val': lambda xr: v[:, xr[0]:xr[1]].mean(axis=1)[:, None],
        'sat': lambda xr: sat[:, xr[0]:xr[1]].mean(axis=1)[:, None],
        'gold': lambda xr: gold[:, xr[0]:xr[1]].mean(axis=1)[:, None],
        'green': lambda xr: g[:, xr[0]:xr[1]].mean(axis=1)[:, None],
        'bright': lambda xr: (v[:, xr[0]:xr[1]] > xr[2]).sum(axis=1).astype(np.float32)[:, None],
    }


def step_edge(prof, y, win, n, lam=0.035):
    """在 [y-win, y+win] 里找**平台阶跃**：score = |左 n 行均值 − 右 n 行均值|₁ − λ·|k−y|。

    prof: (H, C) 的**原始**剖面（⛔ 不是差分 —— 差分会被单行噪声主导）。
    """
    h = len(prof)
    lo, hi = max(0, int(y - win)), min(h - 1, int(y + win))
    best, bv = None, -1.0
    for k in range(lo + 1, hi + 1):
        a0, b0 = prof[max(0, k - n):k], prof[k:min(h, k + n)]
        if len(a0) == 0 or len(b0) == 0:
            continue
        d = float(np.abs(a0.mean(axis=0) - b0.mean(axis=0)).sum())
        sc = d - lam * abs(k - y)
        if sc > bv:
            bv, best = sc, (k, d)
    return best if best else (None, 0.0)


def hruns(mask, minw, maxgap=30):
    """横向连续段（允许 maxgap 的空洞 —— 文字描边会把块打断）。"""
    idx = np.where(mask)[0]
    if len(idx) == 0:
        return []
    segs, st, prev = [], idx[0], idx[0]
    for i in idx[1:]:
        if i - prev <= maxgap:
            prev = i
        else:
            segs.append((int(st), int(prev)))
            st = prev = i
    segs.append((int(st), int(prev)))
    return [t for t in segs if t[1] - t[0] >= minw]


def main():
    argv = sys.argv[1:]
    tol = TOL
    win = 46.0
    dump = '--dump' in argv
    if '--tol' in argv:
        tol = float(argv[argv.index('--tol') + 1])
    if '--win' in argv:
        win = float(argv[argv.index('--win') + 1])

    C = read_all_consts()
    need = ('TabDecksY', 'TabDecksX', 'TabW', 'TabCollectionX', 'TabWCollection',
            'TabOffY', 'NumRowY', 'NumRowH', 'GridTopY', 'BottomRowY', 'BottomRowH',
            'BannerTopY', 'NumBtnY', 'NumBtnH', 'NumBtnW', 'NumBtnPitch', 'NumBtnX0',
            'GridLeftX', 'CardW')
    missing = [n for n in need if n not in C]
    if missing:
        sys.stderr.write('FATAL 常量读不到：%s\n' % ', '.join(missing))
        return 2

    # ★ 原分辨率（⛔ 不缩放）
    a = np.asarray(Image.open(REF).convert('RGB')).astype(np.float32)
    CH = make_channels(a)

    def o1242(v1080):
        """我们 @1080 的常量 -> @1242（与原版读数同坐标系）。"""
        return v1080 * INV

    gx0 = int(C['GridLeftX'] * INV) + 4
    gx1 = int((C['GridLeftX'] + C['CardW']) * INV) - 4

    # 每条带： (标签, 我们@1080, 通道, 通道参数(@1242), 平台半宽 n, 出处)
    # 通道怎么选 —— **本探针最容易写错的地方**，逐条给理由：
    #   val    = 亮度 V 行均值  -> "块体比底色亮/暗"的边界（页签上缘）
    #   sat    = 饱和度行均值   -> "卡面内容进出"（卡阵顶边）。⚠️ 页签**不能**用 sat：亮蓝与深蓝的
    #                              饱和度几乎相同（0.936 vs 0.910）=> 找不到边界（第一版栽在这）
    #   gold   = R−B 行均值     -> **金按钮**上下缘（金 = 高 R 低 B；蓝底接近 0）
    #   bright = 亮像素数       -> 按钮**板**的进出。阈值必须按**钮板**取（板 V≈188 / 带 V≈140 => 165），
    #                              ⛔ 取 200 只圈到钮上的白色图标（第二版栽在这）
    #   median = 整行中位色     -> 只在边界是**整宽换色**时用（编号行上下缘 / 宣传区上缘）
    bands = [
        ('Tab 带 上缘 (Decks)',      C['TabDecksY'],                    'val',    (110, 500),        5,  'E22'),
        ('Tab 带 上缘 (Collection)', C['TabOffY'],                      'val',    (140, 430),        5,  'E25'),
        ('Tab 带 下缘 / 编号行上缘',  C['NumRowY'],                      'median', None,              14, 'E4'),
        ('编号行 下缘',              C['NumRowY'] + C['NumRowH'],       'median', None,              14, 'E7'),
        ('编号钮 上缘',              C['NumBtnY'],                      'gold',   (80, 150),         6,  'E31'),
        ('编号钮 下缘',              C['NumBtnY'] + C['NumBtnH'],       'gold',   (80, 150),         6,  'E31'),
        ('卡阵 第1行主体顶边',        C['GridTopY'],                     'sat',    (gx0, gx1),        14, 'A4/E10'),
        ('底行 钮板 上缘',            C['BottomRowY'],                   'bright', (1080, 1180, 165), 8,  'E41'),
        ('底行 钮板 下缘',            C['BottomRowY'] + C['BottomRowH'], 'bright', (1080, 1180, 165), 8,  'E41'),
        ('宣传区 上缘',              C['BannerTopY'],                   'median', None,              14, 'E54'),
    ]

    lines = []
    w = lines.append
    w(u'D151 逐带数值差值 —— 口径：**原版 1242×2208 原分辨率**，我们 @1080 ×%.4f 换回 @1242' % INV)
    w(u'原版 = 策划/参考图/07_卡组编辑_1242x2208.jpg（⛔ 不缩放；缩放到 1080 重采样会把 2px 的边糊成 4px）')
    w(u'我们 = client/Assets/Scripts/UI/Panels/DeckEditPanel.cs 的常量（正则 + 迭代求值读出，⛔ 不重抄）')
    w(u'容差 ±%.1f px @1242（= 几何量取.md 的既定读数容差）' % tol)
    w(u'')
    w(u'每条带**声明自己由哪条通道看见**：median=整行中位色 / val=指定 x 带内 V 行均值 /')
    w(u'  sat=指定 x 带内饱和度行均值 / gold=R−B 行均值 / bright=指定 x 带内亮像素数。')
    w(u'边界判定 = **平台阶跃**（两侧各 n 行均值比 L1 距离 + λ=0.035 的轻度位置先验）；')
    w(u'  ⛔ 不用"窗口内最大差分"：那会被邻近更强的内容边缘抢走（卡阵顶边 384 被卡内画面抢过）。')
    w(u'')

    if dump:
        _med = CH['median'](None)
        w(u'原版整行中位色差候选（值 = 跳变量，pos = y@1242）：')
        d = np.abs(np.diff(_med, axis=0)).sum(axis=1)
        for i in range(1, len(d) - 1):
            if d[i] >= d[i - 1] and d[i] >= d[i + 1] and d[i] > 18.0:
                w(u'   y=%-5d d=%.1f  下行中位色 #%02X%02X%02X' % (
                    i + 1, float(d[i]), int(_med[i + 1][0]), int(_med[i + 1][1]), int(_med[i + 1][2])))
        w(u'')

    w(u'── 逐带对位 ─────────────────────────────────────────────────────────────')
    w(u'%-24s %7s %11s %11s %9s %s' % (u'带边界', u'通道', u'我们@1242', u'原版@1242', u'差(dx)', u'判定'))
    rows_out = []
    # 少数带的窗口必须**收窄**：窗口一宽，"找最近平台阶跃"就会跳到几十像素外的另一条硬边上去。
    # 窗口本身就是判据的一部分，逐条给定。
    band_win = {
        'Tab 带 上缘 (Collection)': 16.0,
        '底行 钮板 上缘': 26.0,
        '底行 钮板 下缘': 26.0,
    }
    for label, y1080, ch, xr, n, src_id in bands:
        y = o1242(y1080)
        prof = CH[ch](xr)
        ry, _rv = step_edge(prof, y, band_win.get(label, win), n=n)
        if ry is None:
            w(u'%-24s %7s %11.1f %11s %9s %s' % (label, ch, y, '-', '-', 'NO-REF'))
            rows_out.append((label, y, None, None, 'NO-REF', src_id))
            continue
        dx = y - ry
        ok = abs(dx) <= tol
        w(u'%-24s %7s %11.1f %11d %+9.1f %s' % (
            label, ch, y, ry, dx, u'PASS' if ok else u'FAIL(>%.1f)' % tol))
        rows_out.append((label, y, ry, dx, u'PASS' if ok else u'FAIL', src_id))

    w(u'')
    w(u'── 页面骨架横向：页签左右缘（★ 第三片换算子） ────────────────────────────')
    w(u'  ⛔ 前两版都失败，根因是同一件事：Collection 页签是**软边块** —— 未选中态填充 `#08305B`(G=48)')
    w(u'     与背后的顶区面板 `#09275A`(G=39) 只差 9/通道 ⇒ "竖直边缘能量"被左侧 Decks 亮块的')
    w(u'     强边（x=598）压过（试过 col_step：在 ±44 窗口里找到的是 525）。')
    w(u'  ★ 第三片换**通道分段**：比的是**带内的填充色**，不是"边上那一两列的变化率"。')
    w(u'     · Decks 块：亮 ⇒ `V > 150` 的横向段（允许 30px 空洞，文字黑描边会把块打断）')
    w(u'     · Collection 签体：`G > 43`（面板 39 / 签体 48 的中值偏面板 2）的横向段')

    # ⛔ 横向扫描要的是**逐列**数组（沿 x），不是逐行剖面 —— `CH` 里那几条通道都是"按 x 带求行均值"的，
    #    直接拿来扫 x 会得到长度 1 的东西（踩过：段表恒为空）。这里现取两幅原图的逐列均值。
    vv = a.max(axis=2)
    gg = a[:, :, 1]
    y0, y1 = 48, 68
    mv = vv[y0:y1, :].mean(axis=0)
    gc = gg[y0:y1, :].mean(axis=0)
    decks_runs = hruns(mv > 150.0, 40)
    # ⛔ Collection 的 maxgap 必须取小（8）：Decks 亮块的**辉光**会往右溢到 x≈601..639，
    #    那里的 G 也有 45~53（> 43）⇒ 用 maxgap=30 会把两块**并成一段**（起点 118），
    #    再按 `t[0] > 620` 过滤就什么都不剩（踩过：段表恒为空）。面板真正的缝只有 ~13px 宽（640..652）。
    coll_runs = [t for t in hruns(gc > 43.0, 60, maxgap=8) if t[0] > 620]
    w(u'  Decks 亮块 (V>150, y48..68) 段 = %s' % (list(decks_runs),))
    w(u'  Collection 签体 (G>43, y48..68) 段 = %s' % (list(coll_runs),))

    horiz = []
    if decks_runs:
        horiz += [('Decks 左缘', float(decks_runs[0][0]), 'E5/E22'),
                  ('Decks 右缘', float(decks_runs[-1][1] + 1), 'E49/E6')]
    if coll_runs:
        horiz += [('Collection 左缘', float(coll_runs[0][0]), 'E50'),
                  ('Collection 右缘', float(coll_runs[-1][1] + 1), 'E51')]

    ours_of = {'Decks 左缘': C['TabDecksX'], 'Decks 右缘': C['TabDecksX'] + C['TabW'],
               'Collection 左缘': C['TabCollectionX'],
               'Collection 右缘': C['TabCollectionX'] + C['TabWCollection']}
    for nm, rx, sid in horiz:
        ours = o1242(ours_of[nm])
        dx = ours - rx
        ok = abs(dx) <= tol
        w(u'  %-18s 我们 %8.1f / 原版 %7.1f 差 %+6.1f  %s  [%s]' % (
            nm, ours, rx, dx, u'PASS' if ok else u'FAIL', sid))
        rows_out.append((nm, ours, rx, dx, u'PASS' if ok else u'FAIL', sid))

    w(u'')
    fails = [r for r in rows_out if r[4] != u'PASS']
    w(u'BANDDIFF-SUMMARY 条数=%d PASS=%d FAIL=%d verdict=%s' % (
        len(rows_out), len(rows_out) - len(fails), len(fails), u'PASS' if not fails else u'FAIL'))
    for label, y, ry, dx, _st, sid in fails:
        w(u'   !! %s [%s]  我们 %.1f / 原版 %s / 差 %s' % (label, sid, y, ry, dx))

    txt = u'\n'.join(lines) + u'\n'
    if not os.path.isdir(OUTDIR):
        os.makedirs(OUTDIR)
    io.open(OUTTXT, 'w', encoding='utf-8', newline='\n').write(txt)
    sys.stdout.write(txt)
    return 0 if not fails else 1


if __name__ == '__main__':
    sys.exit(main())
