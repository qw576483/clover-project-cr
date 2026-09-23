#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""CR-V2 对局 HUD 可见元素量取（离线 · 像素级 · 可复跑）。

判据基线 = `策划/参考图/18_对局HUD_1080x1920.jpg`（1080x1920，与本项目竖版画布同尺寸 ⇒ 像素值 = 画布值）。
被测量   = 我方实机合成屏（`capture_game_view --source screen` 采出、拷进 `.ai-tmp/screenshots/`）。

本脚本只做**颜色/连通域/剖面**判据，⛔ 不做 OCR、⛔ 不把命中框当"美术语义"。

用法：
  python tools/probes/cr-v2-hud-measure.py <原版图> <我方图> <输出目录>
"""
import os
import sys
from collections import Counter

from PIL import Image

# ── 判据 ──────────────────────────────────────────────────────────────────────
def pred_light(p):
    return min(p[0], p[1], p[2]) > 150


def pred_near_white(p):
    return min(p[0], p[1], p[2]) > 195


def pred_purple(p):
    return p[0] > 90 and p[2] > 150 and p[1] < p[2] - 40 and p[0] > p[1] + 20


def pred_gold(p):
    return p[0] > 185 and p[1] > 140 and p[2] < 160 and (p[0] - p[2]) > 45


def pred_dark(p):
    return max(p[0], p[1], p[2]) < 80


def bbox_hits(img, box, pred):
    x0, y0, x1, y1 = box
    xs, ys, n = [], [], 0
    cnt = Counter()
    for y in range(y0, y1):
        for x in range(x0, x1):
            p = img.getpixel((x, y))
            if pred(p):
                xs.append(x); ys.append(y); n += 1
                cnt[(p[0] // 8 * 8, p[1] // 8 * 8, p[2] // 8 * 8)] += 1
    if n == 0:
        return None, 0, None
    return (min(xs), min(ys), max(xs) + 1, max(ys) + 1), n, cnt.most_common(1)[0][0]


def window_mean(img, box):
    x0, y0, x1, y1 = box
    r = g = b = n = 0
    for y in range(y0, y1):
        for x in range(x0, x1):
            p = img.getpixel((x, y))
            r += p[0]; g += p[1]; b += p[2]; n += 1
    return (r // n, g // n, b // n)


def row_profile(img, y, x0, x1, step=1):
    return [(x, img.getpixel((x, y))) for x in range(x0, x1, step)]


def col_profile(img, x, y0, y1, step=1):
    return [(y, img.getpixel((x, y))) for y in range(y0, y1, step)]


def zoom(img, box, factor, out_path):
    c = img.crop(box)
    c = c.resize((c.width * factor, c.height * factor), Image.NEAREST)
    c.save(out_path)
    return c.size


def run_len(items):
    """把 [(idx,(r,g,b)), ...] 压成 [(start,end,color)]，丢弃长度 < 2 的段。"""
    out, prev, start = [], None, None
    for i, p in items:
        q = (p[0] // 8 * 8, p[1] // 8 * 8, p[2] // 8 * 8)
        if q != prev:
            if prev is not None and i - start >= 2:
                out.append((start, i, prev))
            prev, start = q, i
    return out


def main():
    # ⚠️ 本机控制台是 GBK ⇒ 任何**不在 GBK 里的字符**（如 U+2212 减号）会让 print 抛
    #    `UnicodeEncodeError` 并**把后面所有段一起带走**（实机实测：§7b 之后整段没跑，
    #    而前面已写进重定向文件的那些行**看起来是完整的** = "输出看着对、产物是坏的"同族）。
    #    ⇒ ① 显式把 stdout 的错误策略设成 replace（不再因编码炸掉整段）；
    #      ② 打印串里一律用 ASCII（`(1-alpha)` 而不是 U+2212）。
    #   ③ 顺带把 stdout 编成 UTF-8（重定向出去的文件就是 UTF-8，读回时用 -Encoding UTF8），
    #      这样 `⇒`/`≈` 这类**不在 GBK 里**的字符也不会被替换成 `?`，判据资产里的读数不打折。
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    base_path, mine_path, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(outdir, exist_ok=True)
    base = Image.open(base_path).convert("RGB")
    mine = Image.open(mine_path).convert("RGB")
    print("base = %s  size=%s" % (base_path, base.size))
    print("mine = %s  size=%s" % (mine_path, mine.size))
    imgs = (("base18", base), ("mine", mine))

    # 桌面几何（出处 HudPanel：CardW140 / CardGap3 / HandRowLeft144 / CardH171 / 卡底 y=1785）
    card_x = [144, 287, 430, 573]
    CARD_W, CARD_H, CARD_TOP, CARD_BOT = 140, 171, 1614, 1785

    print("\n=== [1] 手牌：卡缘剖面（第 2 张卡 x287..427，y=1700 横切）===")
    for tag, img in imgs:
        print("-- %s --" % tag)
        for s, e, c in run_len(row_profile(img, 1700, 285, 432)):
            print("   x %4d..%4d w=%2d  %s" % (s, e, e - s, c))

    print("\n=== [2] 手牌：卡顶剖面（第 2 张卡 y1616 横切 = 框带上沿）===")
    for tag, img in imgs:
        print("-- %s --" % tag)
        for s, e, c in run_len(row_profile(img, 1616, 285, 432)):
            print("   x %4d..%4d w=%2d  %s" % (s, e, e - s, c))

    print("\n=== [3] 手牌：费用泡所在窗口（第 2 张卡，泡=圣水水滴）===")
    box_purple = (287, 1614, 427, 1800)
    box_drop = (320, 1725, 400, 1800)   # 卡底中央（原版泡挂在下沿）
    for tag, img in imgs:
        bb, n, c = bbox_hits(img, box_purple, pred_purple)
        print("   %-6s 卡内 purple bbox=%s px=%s mode=%s" % (tag, bb, n, c))
        bb, n, c = bbox_hits(img, box_drop, pred_dark)
        print("   %-6s 卡底中央 dark  bbox=%s px=%s mode=%s 窗口均值=%s"
              % (tag, bb, n, c, window_mean(img, box_drop)))

    print("\n=== [4] 顶部时间板（窗口 870,0..1080,100）===")
    box = (870, 0, 1080, 100)
    for tag, img in imgs:
        bb, n, c = bbox_hits(img, box, pred_light)
        print("   %-6s light(>150) bbox=%s px=%s mode=%s 窗口均值=%s"
              % (tag, bb, n, c, window_mean(img, box)))
    for tag, img in imgs:
        bb, n, c = bbox_hits(img, (890, 0, 1080, 34), pred_near_white)
        print("   %-6s 板内标题 near_white bbox=%s px=%s mode=%s" % (tag, bb, n, c))
    for tag, img in imgs:
        bb, n, c = bbox_hits(img, (890, 34, 1080, 92), pred_near_white)
        print("   %-6s 板内数字 near_white bbox=%s px=%s mode=%s" % (tag, bb, n, c))
    for tag, img in imgs:
        print("   %-6s 板面横切 y=50:" % tag, [(x, img.getpixel((x, 50))) for x in range(876, 1080, 24)])

    print("\n=== [5] 顶部中央冠数徽章（窗口 455,0..585,46）===")
    box = (455, 0, 585, 46)
    for tag, img in imgs:
        bb, n, c = bbox_hits(img, box, pred_purple)
        print("   %-6s purple bbox=%s px=%s mode=%s" % (tag, bb, n, c))
        bb, n, c = bbox_hits(img, box, pred_gold)
        print("   %-6s gold   bbox=%s px=%s mode=%s" % (tag, bb, n, c))
        bb, n, c = bbox_hits(img, box, pred_near_white)
        print("   %-6s white  bbox=%s px=%s mode=%s 窗口均值=%s"
              % (tag, bb, n, c, window_mean(img, box)))

    print("\n=== [6] 顶部左块（我方 0:0 板所在窗口 10,10..300,92）===")
    box = (10, 10, 300, 92)
    for tag, img in imgs:
        bb, n, c = bbox_hits(img, box, pred_near_white)
        print("   %-6s near_white bbox=%s px=%s mode=%s 窗口均值=%s"
              % (tag, bb, n, c, window_mean(img, box)))
        bb, n, c = bbox_hits(img, box, pred_purple)
        print("   %-6s purple    bbox=%s px=%s mode=%s" % (tag, bb, n, c))

    # ── [7b] 计时板「板自身 vs 背景」分离（team-lead 2026-09-23 批准的分离实验，纯离线） ──
    #
    # 为什么需要：板面件是**半透明**的 ⇒ 实机看到的是 `face = α·tint_sprite + (1−α)·背景`。
    #   `TimerPlateTint` 是**反解**出来的（目标 = 让合成值 ≈ 原版采样），式里的"背景"用的是**当时那张图上的
    #   草地/金路** ⇒ 那只证明"拟合成立"，**不证明"板自身对"**。要分离，必须拿到**同一装配、同一帧、
    #   同一块板**压在**两种不同背景**上的两个读数 —— 一个方程消掉 α·C，直接解出 (1−α)。
    #
    # 取法（不需要额外进 Play）：板矩形 = 右上角 198×100（`TimerBoxW/H`）⇒
    #   · face 行 = 板内**文字上方**的干净横带（避开标题 bbox 与数字 bbox）；默认 y=6..9；
    #   · 背景参照 = 板**正下方**紧邻的横带（同一 x、同一场景纹理），默认 y=104..111；
    #   · 按背景参照的颜色把 x 分类成两类（金光路面 / 草地），每类求均值，再解 (1−α)。
    # ⚠️ 边界（不许藏）：板**遮挡**了它下面的背景 ⇒ 用"板正下方"当参照，隐含**局部平坦**假设
    #   ⇒ 必须打印该参照带的**逐 x 离散度**（极差）作为该假设的自证；极差过大就不出结论、只报"未分离"。
    print("\n=== [7b] 计时板 板自身 / 背景 分离（离线，不需要额外进 Play）===")
    PLATE_X0, PLATE_X1 = 882, 1080      # TimerBoxW=198 贴右 ⇒ 882..1080
    FACE_Y0, FACE_Y1 = 6, 10            # 板内、标题 bbox(11..31) 之上
    BG_Y0, BG_Y1 = 104, 112             # 板正下方紧邻

    def strip_mean(img, x0, x1, y0, y1):
        r = g = b = n = 0
        for y in range(y0, y1):
            for x in range(x0, x1):
                p = img.getpixel((x, y))
                r += p[0]; g += p[1]; b += p[2]; n += 1
        return (r / n, g / n, b / n)

    def is_gold(p):
        return p[0] > 130 and (p[0] - p[2]) > 45 and p[1] > 110

    for tag, img in imgs:
        try:
            # 逐列取 (face, bg)
            cols = []
            for x in range(PLATE_X0 + 2, PLATE_X1 - 2):
                face = strip_mean(img, x, x + 1, FACE_Y0, FACE_Y1)
                bg = strip_mean(img, x, x + 1, BG_Y0, BG_Y1)
                cols.append((x, face, bg, is_gold(bg)))
            gold = [(f, b) for _, f, b, g in cols if g]
            green = [(f, b) for _, f, b, g in cols if not g]
            print("   %-6s 分类：金光背景 %d 列 / 草地背景 %d 列" % (tag, len(gold), len(green)))
            if len(gold) < 5 or len(green) < 5:
                print("   %-6s 未分离：两类之一的列数 < 5（本帧背景不够两类）" % tag)
                continue
            # 参照带局部平坦性自证（极差）
            bg_all = [b for _, _, b, _ in cols]
            rng = max(max(c) for c in bg_all) - min(min(c) for c in bg_all)
            gb = [b for b in bg_all if is_gold(b)]
            nb = [b for b in bg_all if not is_gold(b)]
            rng_g = max(max(c) for c in gb) - min(min(c) for c in gb)
            rng_n = max(max(c) for c in nb) - min(min(c) for c in nb)
            print("   %-6s 参照带极差：全体 %d / 金光类内 %d / 草地类内 %d（>40 则平坦假设不成立）"
                  % (tag, rng, rng_g, rng_n))

            def avg(items, idx):
                n = len(items)
                return tuple(sum(v[idx][k] for v in items) / n for k in range(3))

            Fg, Bg = avg(gold, 0), avg(gold, 1)
            Fn, Bn = avg(green, 0), avg(green, 1)
            print("   %-6s face(金光底)=%s  bg=%s" % (tag, tuple(round(v, 1) for v in Fg), tuple(round(v, 1) for v in Bg)))
            print("   %-6s face(草地底)=%s  bg=%s" % (tag, tuple(round(v, 1) for v in Fn), tuple(round(v, 1) for v in Bn)))
            # face1 − face2 = (1−α)(B1 − B2) ⇒ 逐通道估 (1−α)，取"分母够大"的通道
            est = []
            for k, nm in enumerate("RGB"):
                den = Bg[k] - Bn[k]
                if abs(den) > 25:
                    est.append((nm, (Fg[k] - Fn[k]) / den))
            print("   %-6s 逐通道估 (1-alpha) = %s" % (tag, [(nm, round(v, 3)) for nm, v in est]))
            if len(est) >= 2:
                one_minus_a = sum(v for _, v in est) / len(est)
                a = 1.0 - one_minus_a
                # α·C = face − (1−α)·bg ⇒ C = (face − (1−α)·bg)/α（两种背景各解一次，互证）
                for lbl, F, B in (("金光", Fg, Bg), ("草地", Fn, Bn)):
                    if a > 0.02:
                        C = tuple((F[k] - one_minus_a * B[k]) / a for k in range(3))
                        print("   %-6s 解出 板自身色 C（%s底）= %s   α=%.2f"
                              % (tag, lbl, tuple(round(v) for v in C), a))
            else:
                print("   %-6s 未分离：两种背景的差异不足以解出 (1-alpha)（需要 >=2 个通道背景差 > 25）" % tag)
        except Exception as ex:  # 读失败必须 loud、不判定
            print("   %-6s 分离失败（loud）：%r" % (tag, ex))

    # ── [7c] 遮挡分离（**真值法**）：screen vs camera 两采 —— 不需要改探针、不需要动任何节点状态 ──
    #
    # 依据（`unity command --format tsv` 里 `capture_game_view` 自述原文）：
    #   `source=camera (default)` renders a camera and **misses Screen Space - Overlay UI**；
    #   `source=screen` captures the composited backbuffer incl. overlay canvases (Play Mode only)。
    # ⇒ 同一条链里同机位采两张：`S`(screen，含 HUD) 与 `B`(camera，**不含 Overlay UI** = 板下背景真值)
    #   ⇒ `S = α·C + (1−α)·B` 可解，**不需要任何平坦假设**，也**不需要把板 alpha 置 0**
    #     （因此不存在"改了状态忘了还原"的风险）。
    #
    # 解法：对板内像素**逐通道线性回归** `S_c = α·C_c + (1−α)·B_c`
    #   ⇒ 斜率 = (1−α)、截距 = α·C_c ⇒ α = 1 − 斜率、C_c = 截距/α。
    # **两条会失败的自证**（不通过就判失败、不出结论）：
    #   ① 非 UI 区域 S 与 B 必须逐像素一致 —— 证明"两采同机位同构图同尺寸"；
    #   ② 三个通道必须给出**同一个 α**（α 与通道无关）⇒ 判据 = 三通道 α 的极差。
    print("\n=== [7c] 遮挡分离（真值法，screen vs camera 两采）===")
    # ★ 结论依赖的**前提必须出现在读数里**（team-lead 2026-09-23 要求）—— 只写在代码注释里，
    #   读结果的人看不到（"结论依赖未写下的前提"那一族）。本段把画布/图像 1:1 的证据打进输出。
    print("   CANVAS/IMAGE 1:1 (evidence: captured PNG = 1080x1920; CrUiStyle.DesignW/H = 1080/1920; "
          "card-edge measured 290..293 vs constant-derived 287 => 3px)")
    # ⚠️ 变量名必须叫 `bgimg`：`bg` 在本文件 §7b 里已被"逐列背景均值"占用 ⇒ 实测踩过一次
    #    （`AttributeError: 'tuple' object has no attribute 'size'`，整段被带走）。正/负控宿主
    #    `.ai-tmp/hosts/cr-v2-sep-control.py` 抓到的就是这个，不是靠读代码发现的。
    bg_path = sys.argv[4] if len(sys.argv) > 4 else None
    bgimg = Image.open(bg_path).convert("RGB") if (bg_path and os.path.isfile(bg_path)) else None
    if bgimg is None:
        print("   未提供背景图（argv[4]）：跳过。用法 = … <原版> <我方screen> <出图目录> <我方camera>")
    elif bgimg.size != mine.size:
        print("   判失败（loud）：两采尺寸不同 screen=%s camera=%s ⇒ 不是同一次构图" % (mine.size, bgimg.size))
    else:
        # ① 同机位自证：取三块**确定性不在 HUD 上**的区域（左上角内 40x40 / 画布中线偏下 40x40 / 左下角内 40x40）
        probes = [(8, 8), (520, 1200), (8, 1872)]
        bad = []
        for (px, py) in probes:
            for dy in range(0, 40, 4):
                for dx in range(0, 40, 4):
                    a = mine.getpixel((px + dx, py + dy))
                    b = bgimg.getpixel((px + dx, py + dy))
                    if max(abs(a[k] - b[k]) for k in range(3)) > 6:
                        bad.append((px + dx, py + dy, a, b))
        print("   自证① 非 UI 区域 screen==camera：不一致像素 %d 个（>0 则两采不同构图 ⇒ 判失败）" % len(bad))
        for t in bad[:4]:
            print("      diff %s screen=%s camera=%s" % (t[0:2], t[2], t[3]))

        # 板矩形 = 右上 198x100（`TimerBoxW/H` 贴右贴顶 = 882..1080 x 0..100）；
        # 只用"确实被 UI 覆盖"且"不是文字/描边"的像素（|Δ| 落在板面档内），把同一 tint 的像素聚在一起。
        def fit(U, V):
            """逐通道一元回归 U=bg -> V=screen；返回 {通道: (斜率, 截距, R2, 残差list)}"""
            out = {}
            m = len(U)
            for k, nm in enumerate("RGB"):
                uz = [U[i][k] for i in range(m)]
                vz = [V[i][k] for i in range(m)]
                mu = sum(uz) / m
                mv = sum(vz) / m
                var = sum((uz[i] - mu) ** 2 for i in range(m))
                if var < 1e-6:
                    out[nm] = None
                    continue
                slope = sum((uz[i] - mu) * (vz[i] - mv) for i in range(m)) / var
                inter = mv - slope * mu
                res = [vz[i] - (inter + slope * uz[i]) for i in range(m)]
                ss_res = sum(r * r for r in res)
                ss_tot = sum((vz[i] - mv) ** 2 for i in range(m)) or 1e-9
                out[nm] = (slope, inter, 1 - ss_res / ss_tot, res)
            return out

        # 只取"确实被 UI 覆盖"的像素（|Δ| > 8）。
        # ⛔ 刻意**不再用 |Δ| 上限**去猜"哪些像素是文字" —— 实测缺陷：金光路面 (251,199,64) 与 C≈(35,34,77)
        #    的 Δ_R = -173，旧规则（|Δ|>120 剔除）会把**正当的板面像素**一起剔掉 ⇒ 拟合被系统性偏置，
        #    而三条自证**照样通过**（= 静默偏置）。改用**残差迭代剔除**：文字/描边是另一种 tint ⇒ 自然成离群点。
        # ★ 测量窗收进**板内侧**（默认 18px 边距）——实测发现必须这么做：
        #   该矩形里其实叠了**两层**：`TimerPlateFill`（alpha 0.80 的实心件）+ `TimerBox`
        #   （`ui_out/193`，alpha 0.35 的**空心圆角环**，环压在矩形边沿上）。
        #   "单层线性模型" `S=alpha*C+(1-alpha)*B` 只在**单层覆盖**的像素上成立 ⇒ 边沿一圈两层像素会
        #   把回归拉垮（实测：全矩形跑出 R2≈0.02~0.05、三通道 alpha 0.450/0.731/0.525、极差 0.28 ⇒ 自证②判失败）。
        #   收进内侧后仍可能有文字层（由下面的稳健剔除处理）。
        INSET = 18
        # ★ 用**图元自身的 alpha** 判"这像素被几层盖住" —— 离线可做，⛔ 不需要改任何节点状态。
        #   实测（`.ai-tmp/hosts/cr-v2-layer-check.py`）：`frame_193` **不是空心的环** ——
        #   其中心区(20..80%) alpha 均值 **30.6**、**12.2% 的像素 alpha>0**；而 `frame_531` 中心区
        #   alpha 均值 **255.0**、100% 不透明 ⇒ 内区其实也是**两层**(fill 0.80 + 193 的低 alpha 黑)。
        #   ⇒ 单层模型只能建立在 **193.alpha == 0** 的像素上；这些像素由 sprite 自己的 alpha 图给出。
        #   ⚠️ 映射：两个 Image 都是 `border=(0,0,0,0)` 的**整幅拉伸** ⇒
        #     画布 x = 882 + sx*198/212 ；图像 y（从上往下）= sy*100/124（PNG 第 0 行 = 顶）。
        spr_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                               "client", "Assets", "Resources", "Sprites", "Ui")
        a193 = None
        p193 = os.path.join(spr_dir, "Panels", "ui_out", "frame_193.png")
        try:
            a193 = Image.open(p193).convert("RGBA")
            print("   193 alpha 图：%s" % p193)
        except Exception as ex:
            print("   ⚠️ 取不到 193 alpha 图（%s）：%r ⇒ 退回『只按被 UI 覆盖』筛选" % (p193, ex))

        covered = []
        n193zero = n193pos = 0
        for y in range(2 + INSET, 98 - INSET, 2):
            for x in range(PLATE_X0 + INSET, PLATE_X1 - INSET, 2):
                s = mine.getpixel((x, y))
                b = bgimg.getpixel((x, y))
                if max(abs(s[k] - b[k]) for k in range(3)) < 8:
                    continue
                if a193 is not None:
                    sx = int(round((x - 882.0) * 212.0 / 198.0))
                    sy = int(round(y * 124.0 / 100.0))
                    if 0 <= sx < a193.width and 0 <= sy < a193.height:
                        if a193.getpixel((sx, sy))[3] > 0:
                            n193pos += 1
                            continue          # 这像素被 fill **和** 193 两层盖住 ⇒ 不入单层回归
                        n193zero += 1
                covered.append((x, y))
        if a193 is not None:
            print("   193 覆盖统计（INSET=%d 窗口内）：alpha==0 的像素 %d / alpha>0 的像素 %d"
                  % (INSET, n193zero, n193pos))
        print("   板矩形 x %d..%d / y 2..98：被 UI 覆盖像素 %d 个（⛔ 未做平坦假设、⛔ 无 |Δ| 上限）"
              % (PLATE_X0, PLATE_X1, len(covered)))
        if len(covered) < 30:
            print("   判失败（loud）：覆盖像素 < 30 ⇒ 不出结论")
        else:
            U = [bgimg.getpixel(p) for p in covered]
            V = [mine.getpixel(p) for p in covered]
            keep = list(range(len(covered)))
            for it in range(4):
                f = fit([U[i] for i in keep], [V[i] for i in keep])
                # ★ 剔除阈值改用 **MAD（中位绝对偏差）** 而不是 3*RMS ——实测发现 RMS 是**循环论证**：
                #   文字层（另一种 tint）混在里面 ⇒ RMS 被它抬高 ⇒ 阈值过松 ⇒ 文字像素**剔不掉** ⇒ 拟合被污染
                #   （实测：全矩形跑 4 轮只剔掉 413/4104≈10%，而文字占比约 29% ⇒ 阈值明显没在干活）。
                #   MAD 对离群点不敏感 ⇒ 阈值由"主体像素"决定。
                worst_res = []
                for v in f.values():
                    if v is None:
                        continue
                    worst_res.extend(abs(x) for x in v[3])
                if worst_res:
                    srt = sorted(worst_res)
                    med = srt[len(srt) // 2]
                    mad = sorted(abs(x - med) for x in worst_res)[len(worst_res) // 2]
                    floor = max(6.0, med + 4.5 * 1.4826 * mad)
                else:
                    floor = 6.0
                # 逐像素"跨通道最大残差"（残差按 keep 内位置索引）
                posof = {idx: i for i, idx in enumerate(keep)}
                newkeep = []
                for idx in keep:
                    i = posof[idx]
                    worst = 0.0
                    for v in f.values():
                        if v is None:
                            continue
                        if abs(v[3][i]) > worst:
                            worst = abs(v[3][i])
                    if worst <= floor:
                        newkeep.append(idx)
                dropped = len(keep) - len(newkeep)
                # ⚠️ 这里的标签必须与**实现**一致：阈值早已从 `3*RMS` 改成 **MAD**
                #    （见上方注释：RMS 是循环论证）。原来标签仍写 `3*rms` ⇒ **读数在自述一件没发生的事**
                #    （属"标注与实现不一致 ⇒ 读者得到错误结论"那一族）。现改为打印 `med + k*MAD`。
                print("   迭代 %d：保留 %d / %d（剔除 %d；稳健阈值 med+4.5*1.4826*MAD = %.1f）"
                      % (it + 1, len(newkeep), len(keep), dropped, floor))
                if dropped == 0:
                    keep = newkeep
                    break
                keep = newkeep
                if len(keep) < 30:
                    break
            if len(keep) < 30:
                print("   判失败（loud）：剔除后像素 < 30 ⇒ 不出结论")
            else:
                Ku = [U[i] for i in keep]
                Kv = [V[i] for i in keep]
                f = fit(Ku, Kv)
                # ★ 逐通道**条件数**（背景动态范围）必须打印并参与判定 —— 实测教训：本场景板下背景是
                #   **金光/草地**，两者 B 通道只差 ~8（金 (251,199,64) vs 草 (152,184,72)）⇒ B 通道回归
                #   **病态**（斜率被噪声主导：实测 alpha_B=0.845、R2=0.61，而 R/G 的 R2≈0.98/0.99 且
                #   alpha 一致）⇒ 若不按条件数筛，会把一个好通道 \+ 一个坏通道平均成一个**错的结论**。
                #   ⇒ 判据：背景范围 < 25 的通道**不入自证②**（并把它单独印出来，⛔ 不静默丢弃）。
                CH_RANGE_MIN = 25.0
                alphas = []
                illcond = []
                for nm in "RGB":
                    v = f.get(nm)
                    k = "RGB".index(nm)
                    rng_ch = max(u[k] for u in Ku) - min(u[k] for u in Ku)
                    if v is None:
                        print("   通道 %s：背景方差≈0 ⇒ 给不出斜率（背景范围=%.0f）" % (nm, rng_ch))
                        illcond.append(nm)
                        continue
                    slope, inter, r2, _ = v
                    a = 1.0 - slope
                    c = inter / a if abs(a) > 0.02 else None
                    cond = "OK" if rng_ch >= CH_RANGE_MIN else "ILL-COND"
                    if rng_ch >= CH_RANGE_MIN:
                        alphas.append((nm, a))
                    else:
                        illcond.append(nm)
                    print("   通道 %s：背景范围=%.0f(%s)  斜率(1-alpha)=%.3f => alpha=%.3f  截距=%.1f => 板自身 C_%s=%s  R2=%.4f"
                          % (nm, rng_ch, cond, slope, a, inter, nm,
                             ("%.0f" % c) if c is not None else "n/a(alpha~0)", r2))
                if len(alphas) >= 2:
                    lo = min(v for _, v in alphas)
                    hi = max(v for _, v in alphas)
                    used = "+".join(n for n, _ in alphas)
                    print("   自证② 有效通道(%s)的 alpha 极差 = %.3f（<=0.06 视为一致；否则判失败）" % (used, hi - lo))
                    if illcond:
                        print("   ⚠️ 条件不足通道 %s 已排除出自证②（背景范围 < %.0f）—— 它们的 alpha 不可用于结论"
                              % (",".join(illcond), CH_RANGE_MIN))
                    print("   结论：alpha=%s；%s" % (round((hi + lo) / 2, 3),
                          "通过两条自证，可报板自身色" if (hi - lo) <= 0.06 and not bad
                          else "未通过自证 => D114 保持『板/背景贡献未分离』"))
                else:
                    print("   判失败（loud）：有效通道 < 2 => 不出结论")

    print("\n=== [7] 放大图（人工目视用）===")
    jobs = [
        ("CR-V2-base-hand-2x.png", base, (0, 1580, 790, 1810), 2),
        ("CR-V2-mine-hand-2x.png", mine, (0, 1580, 790, 1810), 2),
        ("CR-V2-base-topright-3x.png", base, (860, 0, 1080, 110), 3),
        ("CR-V2-mine-topright-3x.png", mine, (860, 0, 1080, 110), 3),
        ("CR-V2-base-topleft-4x.png", base, (430, 0, 640, 60), 4),
        ("CR-V2-mine-topleft-4x.png", mine, (0, 0, 320, 100), 4),
    ]
    for name, img, box, f in jobs:
        print("   %s -> %s" % (name, zoom(img, box, f, os.path.join(outdir, name))))


if __name__ == "__main__":
    main()
