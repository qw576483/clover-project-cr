# -*- coding: utf-8 -*-
# direct-fix: D151 原版 UI 复刻 —— 用**代码里的常量**离线画出卡组编辑页版式（判据资产 / 预览图）
"""d151-layout-preview.py -- 不开 Unity，把 `DeckEditPanel` 的版式按**代码里的常量**画出来，
再与原版基线图 `07_卡组编辑_1242x2208.jpg` **并排**输出。

为什么要有它
------------
用户 2026-09-24 原话：「**UI呢？？？？？ 你的UI都不是原版UI啊。**」

Unity 开不了 ⇒ 看不到真机效果 ⇒ 只能"我改了，你信不信"。本脚本把**同一个常量表**渲染成一张图：
  · 值**不重抄**：全部从 `DeckEditPanel.cs` / `CrUiStyle.cs` 里读（正则 + 迭代求值）。
  · 画的是**块**（底色 / 位置 / 尺寸 / 文字占位），⛔ 不画卡面、⛔ 不画素材 —— 它证明的是**版式**，
    不是"素材长什么样"（素材由 `ResPaths` 的原版帧负责）。
  · 因此它的用途 = 「整屏分带对不对」这一条判据的可视化，⛔ 不能当"实机截图"用。

输出
----
  .ai-tmp/test/D151-layout-mock.png      我们（按常量渲染）1080×1920
  .ai-tmp/test/D151-layout-compare.png   左 = 原版 07（缩放到 1080×1920）· 右 = 我们（同机位并排）

用法
----
  python tools/probes/d151-layout-preview.py
"""

import io
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

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


def num_consts(path, pat):
    with io.open(path, encoding='utf-8') as f:
        s = f.read().replace('\r\n', '\n')
    raw = {}
    for m in re.finditer(pat, s):
        raw[m.group(1)] = m.group(2).strip()
    return raw


def load():
    raw = num_consts(PANEL, r'private\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:float|int)\s+(\w+)\s*=\s*([^;]+);')
    for k, v in num_consts(STYLE, r'public\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:float|int)\s+(\w+)\s*=\s*([^;]+);').items():
        raw.setdefault(k, v)
    vals = {}
    for _ in range(8):
        for name, expr in raw.items():
            if name in vals:
                continue
            e = re.sub(r'\bCrUiStyle\.(\w+)\b', r'\1', expr)
            e = re.sub(r'(\d+(?:\.\d+)?)[fF]\b', r'\1', e)
            try:
                vals[name] = eval(e, {'__builtins__': {}}, dict(vals))
            except Exception:
                pass

    # Color32
    with io.open(PANEL, encoding='utf-8') as f:
        s = f.read()
    colors = {}
    for m in re.finditer(r'(\w+)\s*=\s*new\s+Color32\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)', s):
        colors[m.group(1)] = tuple(int(m.group(i)) for i in (2, 3, 4))
    # ★ D155：`new Color(rf, gf, bf, af)`（tint 用浮点）⇒ 存成 `__名字__` 供 mirror9 用
    for m in re.finditer(r'(\w+)\s*=\s*new\s+Color\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f', s):
        vals['__%s__' % m.group(1)] = tuple(float(m.group(i)) for i in (2, 3, 4))
    return vals, colors


def _gridpaste(img, rgba, x, y):
    """把 RGBA 贴到 RGB 画布上（带 alpha 合成），越界自动裁。"""
    rw, rh = rgba.size
    x2, y2 = max(0, x), max(0, y)
    cw = min(rw - (x2 - x), img.size[0] - x2)
    ch = min(rh - (y2 - y), img.size[1] - y2)
    if cw <= 0 or ch <= 0:
        return
    part = rgba.crop((x2 - x, y2 - y, x2 - x + cw, y2 - y + ch))
    img.paste(part, (x2, y2), part)


def font(size):
    for p in (u'C:/Windows/Fonts/msyhbd.ttc', u'C:/Windows/Fonts/msyh.ttc', u'C:/Windows/Fonts/simhei.ttf'):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


_TEXCACHE = {}


def mirror9(tex_name, c, w, h, tint=None):
    """复刻 `CrUiStyle.MakeRounded` + `Image.Type.Sliced` 的**实际绘制结果**（★ D155）。

    为什么要复刻：第三片把页签底从「纯色方块」换成了
        `Skin(..., ButtonBlue = ui_out 166, corner = BlueCorner = 19, tint = TabOnTint)`
    ⇒ 离线预览若还画纯色块，就**看不出**这次改动到底长什么样。
    构造（与 C# 逐行同口径）：
      ① n = 2c+1 的镜像纹理：行 `oy` 取源帧第 `sy` 行（oy<c → oy；oy==c → c−1；否则 2c−oy），列同理；
      ② `Image.Type.Sliced` + border (c,c,c,c)：四角原尺寸、四边/中心拉伸（这里用双线性重采样模拟）。
    `tint` = 逐通道相乘（= uGUI `Image.color`）。
    """
    p = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Ui', 'Buttons', 'ui_out', tex_name)
    if p not in _TEXCACHE:
        _TEXCACHE[p] = Image.open(p).convert('RGBA')
    src = _TEXCACHE[p]
    key = (tex_name, c, tint)
    if key not in _TEXCACHE:
        n = 2 * c + 1
        t = Image.new('RGBA', (n, n))
        for oy in range(n):
            sy = oy if oy < c else (c - 1 if oy == c else 2 * c - oy)
            for ox in range(n):
                sx = ox if ox < c else (c - 1 if ox == c else 2 * c - ox)
                t.putpixel((ox, oy), src.getpixel((sx, sy)))
        if tint is not None:
            px = t.load()
            for oy in range(n):
                for ox in range(n):
                    r, g, b, a = px[ox, oy]
                    px[ox, oy] = (int(r * tint[0]), int(g * tint[1]), int(b * tint[2]), a)
        _TEXCACHE[key] = t
    t = _TEXCACHE[key]
    n = 2 * c + 1
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))

    def put(sx, sy, sw, sh, dx, dy, dw, dh):
        if dw <= 0 or dh <= 0:
            return
        part = t.crop((sx, sy, sx + sw, sy + sh))
        if (sw, sh) != (dw, dh):
            part = part.resize((dw, dh), Image.BILINEAR)
        out.alpha_composite(part, (dx, dy))

    put(0, 0, c, c, 0, 0, c, c)                                     # 左上角
    put(c + 1, 0, c, c, w - c, 0, c, c)                             # 右上角
    put(0, c + 1, c, c, 0, h - c, c, c)                             # 左下角
    put(c + 1, c + 1, c, c, w - c, h - c, c, c)                     # 右下角
    put(c, 0, 1, c, c, 0, w - 2 * c, c)                             # 上边
    put(c, c + 1, 1, c, c, h - c, w - 2 * c, c)                     # 下边
    put(0, c, c, 1, 0, c, c, h - 2 * c)                             # 左边
    put(c + 1, c, c, 1, w - c, c, c, h - 2 * c)                     # 右边
    put(c, c, 1, 1, c, c, w - 2 * c, h - 2 * c)                     # 中心
    return out


def main():
    C, COL = load()
    W, H = int(C['DesignW']), int(C['DesignH'])
    # 用 `DesignH` 缩放：画布是 1080×1920 ✅ 与原版 1242×2208 同比例
    img = Image.new('RGB', (W, H), COL['DeckBgColor'])
    d = ImageDraw.Draw(img)
    f_big, f_mid, f_small = font(34), font(26), font(20)
    f_banner = font(int(C['FontBanner']))     # ★ 第三片：宣传大字实测 170@1080（E64）

    def box(x, y, w, h, fill, outline=None):
        d.rectangle([x, y, x + w - 1, y + h - 1], fill=fill, outline=outline)

    def text(x, y, w, h, s, fill=(255, 255, 255), f=None, anchor='mm'):
        f = f or f_mid
        d.text((x + w / 2.0, y + h / 2.0), s, font=f, fill=fill, anchor=anchor)

    # ① 顶部安全区 + Tab 带
    box(0, 0, W, C['TabBarY'] + C['TabBarH'], COL['TopAreaColor'])
    # ② 两颗 Tab（★ D151 第二片：两个页签**不同宽**，实测 Decks 418.3 / Collection 394.0）
    #    ★ D155 第三片：底 = **原版蓝色圆角件 166 的镜像九宫格 + 实测 tint**（⛔ 不再是纯色方块）；
    #    顶部那条线两态不同：选中 = 亮线 3.5（E66）/ 未选中 = 暗带 10.4（E67）。
    _CORN = int(C['BlueCorner'])
    _gridpaste(img, mirror9('frame_166.png', _CORN, int(C['TabW']), int(C['TabDecksH']),
                            C['__TabOnTint__']), int(C['TabDecksX']), int(C['TabDecksY']))
    box(C['TabDecksX'], C['TabDecksY'], C['TabW'], C['TabEdgeH'], COL['TabOnEdgeColor'])
    text(C['TabDecksX'], C['TabDecksY'], C['TabW'], C['TabDecksH'], 'Decks', f=f_big)
    _gridpaste(img, mirror9('frame_166.png', _CORN, int(C['TabWCollection']), int(C['TabOffH']),
                            C['__TabOffTint__']), int(C['TabCollectionX']), int(C['TabOffY']))
    box(C['TabCollectionX'], C['TabOffY'], C['TabWCollection'], C['TabOffEdgeH'], COL['TabOffEdgeColor'])
    text(C['TabCollectionX'], C['TabOffY'], C['TabWCollection'], C['TabOffH'], 'Collection', f=f_big)

    # ③ 编号行（band + 上下亮边 + 7 颗钮）
    box(0, C['NumRowY'], W, C['NumRowH'], COL['NumBarColor'])
    box(0, C['NumRowY'], W, C['NumBarEdgeH'], COL['NumBarEdgeColor'])
    box(0, C['NumRowY'] + C['NumRowH'] - C['NumBarEdgeH'], W, C['NumBarEdgeH'], COL['NumBarEdgeColor'])
    labels = ['1', '2', '3', '4', '5', u'交换', u'复制']
    for i, lab in enumerate(labels):
        x = C['NumBtnX0'] + i * C['NumBtnPitch']
        y = C['NumBtnY']
        box(x, y, C['NumBtnW'], C['NumBtnH'], COL['NumBtnOnColor'] if i == 0 else COL['NumBtnColor'])
        text(x, y, C['NumBtnW'], C['NumBtnH'], lab, f=f_mid if i < 5 else f_small)

    # ④ ★ 第三片：**不画**「已选 N/8」标签 —— 原版 07 卡阵上方那条带是空的
    #    （代码侧由 DeckEditPanel.ShowDevChrome = false 控制）

    # ⑤ 4×2 大卡阵
    for i in range(8):
        col, row = i % 4, i // 4
        x = C['GridLeftX'] + col * C['CardStepX']
        y = C['GridTopY'] + row * C['CardStepY']
        box(x, y, C['CardW'], C['CardH'], (232, 236, 240), outline=(140, 150, 160))
        text(x, y, C['CardW'], C['CardH'], u'卡格 %d\n(原版卡面)' % (i + 1), fill=(70, 80, 95), f=f_small)

    # ⑥ 底行
    box(C['AvgPillX'], C['BottomRowY'], C['AvgPillW'], C['BottomRowH'], COL['AvgPillColor'])
    box(C['AvgPillX'] + 13, C['BottomRowY'] + 17, C['BottomRowH'] - 34, C['BottomRowH'] - 34,
        (183, 10, 188))                                   # 圣水水滴图标占位
    text(C['AvgPillX'] + 96, C['BottomRowY'], C['AvgPillW'] - 104, C['BottomRowH'], '3.8', f=f_big, anchor='lm')
    # ★ 第三片：右侧 = 原版那 3 颗方形工具钮（几何 E61..E63），⛔ 不是两颗大蓝块
    _bot = [u'放大镜', u'保 存', u'取 消']
    for i, lab in enumerate(_bot):
        bx = C['BottomBtnX0'] + i * C['BottomBtnPitch']
        box(bx, C['BottomRowY'], C['BottomBtnW'], C['BottomRowH'], (48, 112, 224))
        text(bx, C['BottomRowY'], C['BottomBtnW'], C['BottomRowH'], lab,
             f=f_small if i == 0 else f_mid)

    # ⑦ ★ D151 第二片：**Decks 页**底部 = 原版宣传区（E53..E56）；
    #    卡池是 **Collection 页**的内容 ⇒ 本图（Decks 页）不画卡池，另出一张 Collection 页的图。
    box(0, C['BannerTopY'], W, C['BannerH'], COL['BannerPlateColor'])
    text(0, C['BannerLine1Y'] - 110, W, 220, u'百张卡牌', f=f_banner)
    text(0, C['BannerLine2Y'] - 110, W, 220, u'组建牌组', f=f_banner)
    text(0, C['BannerTopY'] + 12, W, 34,
         u'↑ 原版这里是一张宣传插图（人像 + 卡背）—— 本工程无该图元，只落地底色与两行标题',
         f=f_small, fill=(200, 220, 240))

    # ⑧ ★ 第三片：**不画**状态行（原版屏幕最下沿是宣传插图的画面本身）

    mock = os.path.join(OUTDIR, 'D151-layout-mock.png')
    img.save(mock)

    # ⑨ 第二张：**Collection 页**（下半屏换成卡池）
    #    ⛔ 注意：上面的 `box()` / `text()` 闭包绑的是 `d`（= `img`）⇒ 这一段**必须**用只指向 img2 的
    #    `box2()` / `text2()`，否则会**把第一张图也改掉**（这个坑真踩过一次：Deck 页的图被
    #    Collection 页的内容覆盖，并排图里"我们"那半边看起来根本没画宣传区）。
    img2 = img.copy()
    d2 = ImageDraw.Draw(img2)

    def box2(x, y, w, h, fill, outline=None):
        d2.rectangle([x, y, x + w - 1, y + h - 1], fill=fill, outline=outline)

    def text2(x, y, w, h, s, fill=(255, 255, 255), f=None, anchor='mm'):
        d2.text((x + w / 2.0, y + h / 2.0), s, font=f or f_mid, fill=fill, anchor=anchor)

    box2(0, C['BannerTopY'], W, C['BannerH'], COL['DeckBgColor'])          # 擦掉宣传区
    # ★ D155：Collection 页里两签**互换**高亮 —— 用同一套镜像九宫格 + 对应 tint 重画
    box2(C['TabDecksX'], C['TabDecksY'], C['TabW'], C['TabDecksH'], COL['TopAreaColor'])
    _gridpaste(img2, mirror9('frame_166.png', _CORN, int(C['TabW']), int(C['TabDecksH']),
                             C['__TabOffTint__']), int(C['TabDecksX']), int(C['TabDecksY']))
    box2(C['TabDecksX'], C['TabDecksY'], C['TabW'], C['TabOffEdgeH'], COL['TabOffEdgeColor'])
    text2(C['TabDecksX'], C['TabDecksY'], C['TabW'], C['TabDecksH'], 'Decks', f=f_big)
    box2(C['TabCollectionX'], C['TabOffY'], C['TabWCollection'], C['TabOffH'], COL['TopAreaColor'])
    _gridpaste(img2, mirror9('frame_166.png', _CORN, int(C['TabWCollection']), int(C['TabOffH']),
                             C['__TabOnTint__']), int(C['TabCollectionX']), int(C['TabOffY']))
    box2(C['TabCollectionX'], C['TabOffY'], C['TabWCollection'], C['TabEdgeH'], COL['TabOnEdgeColor'])
    text2(C['TabCollectionX'], C['TabOffY'], C['TabWCollection'], C['TabOffH'], 'Collection', f=f_big)

    text2(C['GridLeftX'], C['PoolLabelY'], C['GridW'], 30,
          u'卡池：共 60 张 · 已选 8/8（按住卡片上下拖动翻看，共 15 行）', f=f_small, anchor='lm')
    box2(C['GridLeftX'], C['PoolTopY'], C['GridW'], C['PoolViewportH'], (3, 44, 104))
    for i in range(8):
        col, row = i % 4, i // 4
        x = C['GridLeftX'] + col * C['CardStepX']
        y = C['PoolTopY'] + row * C['CardStepY']
        if y + 20 > C['PoolTopY'] + C['PoolViewportH']:
            break
        h = min(C['CardH'], C['PoolTopY'] + C['PoolViewportH'] - y)
        box2(x, y, C['CardW'], h, (238, 240, 243), outline=(150, 158, 168))
        text2(x, y, C['CardW'], h, u'卡池格 %d' % (i + 1), fill=(70, 80, 95), f=f_small)

    mock2 = os.path.join(OUTDIR, 'D151-layout-mock-collection.png')
    img2.save(mock2)

    # 并排对比（Decks 页 vs 原版 07）
    ref = Image.open(REF).convert('RGB').resize((W, H))
    cmp_img = Image.new('RGB', (W * 2 + 12, H + 40), (20, 20, 26))
    cmp_img.paste(ref, (0, 40))
    cmp_img.paste(img, (W + 12, 40))
    dc = ImageDraw.Draw(cmp_img)
    dc.text((W / 2, 20), u'原版 07_卡组编辑（缩放到 1080×1920）', font=f_big, fill=(255, 230, 120), anchor='mm')
    dc.text((W + 12 + W / 2, 20), u'我们 · Decks 页（D151 第三片；按 DeckEditPanel 常量离线渲染）', font=f_big,
            fill=(150, 230, 255), anchor='mm')
    compare = os.path.join(OUTDIR, 'D151-layout-compare.png')
    cmp_img.save(compare)

    print('画布 %dx%d；常量 %d 个' % (W, H, len(C)))
    print('  %s' % os.path.relpath(mock, ROOT))
    print('  %s' % os.path.relpath(mock2, ROOT))
    print('  %s' % os.path.relpath(compare, ROOT))
    return 0


if __name__ == '__main__':
    sys.exit(main())
