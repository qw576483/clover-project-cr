#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2x-overview.py —— CR-T2x：出「原版 18 整屏 vs 我方实机整屏」的同机位总图 + 逐区差值表。

口径（主 agent 2026-09-23 派活）：
  · 基线 = `策划/参考图/18_对局HUD_1080x1920.jpg`（裁定基线；⛔ 20 图不作几何基线）
  · 我方 = `.ai-tmp/screenshots/CR-T2-hud-mine.png`（01:03:48，`CR.dll` 01:02:18 ⇒ 含 HudPanel 01:00:20）
  · 只出图 + 表，⛔ 不改代码/文档；⛔ 不引用 CR-T3 旧帧
产物：
  .ai-tmp/screenshots/CR-T2x-overview.png        （上下排列总图：原版在上、我方在下，含分区编号）
  .ai-tmp/screenshots/CR-T2x-overview-zones.png  （三个分区各自左右并排，看细节）
  .ai-tmp/test/CR-T2x-overview.txt               （逐区差值表：原版值(出处) / 我方值 / 差；差值≠0 一条不落）
用法： python tools/probes/cr-t2x-overview.py
"""
import datetime
import io
import os
import sys

from PIL import Image, ImageDraw, ImageFont


def load_font(size=13, bold=False):
    """PIL 默认位图字体画不出中文（会成方块）⇒ 必须挂系统 CJK 字体。"""
    cands = ["msyhbd.ttc" if bold else "msyh.ttc", "msyh.ttc", "simhei.ttf", "deng.ttf", "simsun.ttc", "arial.ttf"]
    for c in cands:
        p = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", c)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                continue
    return ImageFont.load_default()


FONT = load_font(13)
FONT_S = load_font(12)
FONT_L = load_font(15, bold=True)

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BASE = os.path.join(ROOT, "策划", "参考图", "18_对局HUD_1080x1920.jpg")
MINE = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2-hud-mine.png")
OUT_MAIN = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2x-overview.png")
OUT_ZONE = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2x-overview-zones.png")
OUT_TXT = os.path.join(ROOT, ".ai-tmp", "test", "CR-T2x-overview.txt")
OUT_USER = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2x-foruser.png")

# 分区"有效性"判定用的源码（采集即冻结：图必须比**该区**的源码新）
SRC = {"1": os.path.join(ROOT, "client", "Assets", "Scripts", "UI", "Panels", "HudPanel.cs"),
       "3": os.path.join(ROOT, "client", "Assets", "Scripts", "UI", "Panels", "HudPanel.cs"),
       "2": os.path.join(ROOT, "client", "Assets", "Scripts", "View", "ArenaView.cs")}

# 给用户看的分区说明（⛔ 不含内部术语/文件名）
USER_CAP = {
    "1": ["计时板：原版=半透明青灰底、字白；我方=深色底 ⇒ 板底明度相反",
          "冠数：原版=顶部中央（金色王冠徽章）；我方=左上角白底「0 : 0」⇒ 位置与形制都不同",
          "我方多出绿色「常规时间」字样与黑底暂停键"],
    "2": ["地面：原版=浅灰石板 + 金道 + 金边 + 灌木墙；我方=草地 + 土黄路",
          "根因是整幅底图的选型不同（不是河/桥局部画错）",
          "相机/排版也不同：原版场地被纵向压缩、格子更小",
          "塔：原版塔顶有血条 + 数字；我方没有"],
    "3": ["卡框：原版=≈1px 深描边 + 浅灰卡体；我方=6px 深描边 + 纯白卡体",
          "圣水条：原版能看见条底（深蓝槽）；我方槽底没画出来",
          "卡组名：原版手牌右侧有「彩虹城堡 / 仙极皇朝」；我方该处为空"],
}

ZONES = [("1", "顶部 HUD（计时板 / 冠数 / 暂停 / 阶段）", (0, 0, 1080, 340)),
         ("2", "竞技场（地面 / 河 / 桥 / 塔 / 场地边界）", (0, 340, 1080, 1600)),
         ("3", "底部 HUD（手牌 / 圣水条 / 下一张 / 卡组名）", (0, 1600, 1080, 1920))]
ZONE_COL = {"1": (255, 96, 96), "2": (255, 200, 60), "3": (110, 220, 255)}

# 同坐标探针（两图都是 1080×1920 同机位）；只取"两边都应是同一物"的点
PROBES = [("1", "Z1 计时板内点 (540,110)", (540, 110)),
          ("1", "Z1 左上信息组 (150,40)", (150, 40)),
          ("1", "Z1 暂停按钮 (996,72)", (996, 72)),
          ("2", "Z2 上方车道地面 (540,400)", (540, 400)),
          ("2", "Z2 下方区域 (540,1300)", (540, 1300)),
          ("3", "Z3 手牌#1 左缘 (146,1700)", (146, 1700)),
          ("3", "Z3 手牌#1 内部 (210,1700)", (210, 1700)),
          ("3", "Z3 圣水条空段/槽底处 (900,1815)", (900, 1815))]


def px(im, x, y):
    p = im.load()
    return p[max(0, min(im.size[0] - 1, x)), max(0, min(im.size[1] - 1, y))]


def mean_rgb(im, box):
    return im.crop(box).resize((1, 1), Image.BOX).convert("RGB").getpixel((0, 0))


def count_pred(im, box, pred):
    p = im.load()
    n = 0
    for y in range(box[1], box[3], 2):
        for x in range(box[0], box[2], 2):
            if pred(p[x, y]):
                n += 1
    return n * 4


def is_water(q):
    """水色（CR-T6b 实测原版水 (55,196,205)、我方水 (0,152,183) 共用的判据）：
    ⛔ 旧判据 `b>r+25 and b>110` 会把浅灰石板也判成"水"——那是本片早期错测（主 agent 已更正）。"""
    r, g, b = q[:3]
    return g > 140 and b > 150 and r < g - 60


def river_bands(im, y0=340, y1=1600, x0=80, x1=1040, step=40):
    """**多列**扫描水色连续段再聚类 ⇒ 抗"桥/单位压住某一列"。
    ⛔ 单列（尤其 x=540）会踩到中央石桥或单位：本片早先就因此把浅灰石板误判成水。
    返回 [{top,bot,n,xs}]，按支持列数降序。"""
    p = im.load()
    hits = []
    for x in range(x0, x1, step):
        cur = None
        for y in range(y0, y1):
            w = is_water(p[x, y])
            if w and cur is None:
                cur = y
            if not w and cur is not None:
                if y - cur >= 8:
                    hits.append((cur, y, x))
                cur = None
        if cur is not None and y1 - cur >= 8:
            hits.append((cur, y1, x))
    groups = []
    for top, bot, x in hits:
        for g in groups:
            if abs(g["top0"] - top) <= 14 and abs(g["bot0"] - bot) <= 14:
                g["n"] += 1
                g["xs"].append(x)
                g["top"] = min(g["top"], top)
                g["bot"] = max(g["bot"], bot)
                break
        else:
            groups.append({"top0": top, "bot0": bot, "top": top, "bot": bot, "n": 1, "xs": [x]})
    groups.sort(key=lambda g: -g["n"])
    return groups


def band_color(im, g):
    """在水色带的中段取均值色（只取支持该带的那几列）。"""
    if not g:
        return None
    y = (g["top"] + g["bot"]) // 2
    xs = g["xs"]
    r = sum(im.getpixel((x, y))[0] for x in xs) / float(len(xs))
    gg = sum(im.getpixel((x, y))[1] for x in xs) / float(len(xs))
    b = sum(im.getpixel((x, y))[2] for x in xs) / float(len(xs))
    return (int(r), int(gg), int(b))


def magenta_box(im, box):
    """品红/紫（圣水填充）bbox：r>120 且 b>90 且 g<min(r,b)-40。"""
    p = im.load()
    pts = []
    for y in range(box[1], box[3], 2):
        for x in range(box[0], box[2], 2):
            r, g, b = p[x, y]
            if r > 120 and b > 90 and g < min(r, b) - 40:
                pts.append((x, y))
    if len(pts) < 40:
        return None
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def bridge_runs(im, band):
    """河道中点行上的"非水色"连续段（宽>=20）⇒ 桥/结构物的 x 段。"""
    if not band:
        return []
    p = im.load()
    y = (band[0] + band[1]) // 2
    runs, s = [], None
    for x in range(300, 800):
        water = is_water(p[x, y])
        if not water and s is None:
            s = x
        if water and s is not None:
            if x - s >= 20:
                runs.append((s, x))
            s = None
    if s is not None and 800 - s >= 20:
        runs.append((s, 800))
    return runs


def bright_text_px(im, box):
    return count_pred(im, box, lambda q: min(q) > 200 and max(q) - min(q) < 40)


def dark_bbox(im, box, thr=120):
    p = im.load()
    xs, ys = [], []
    for y in range(box[1], box[3], 2):
        for x in range(box[0], box[2], 2):
            if max(p[x, y]) < thr:
                xs.append(x)
                ys.append(y)
    if not xs:
        return None
    return min(xs), min(ys), max(xs), max(ys)


def build_figure(base, mine, facts, zstat=None):
    S = 0.55
    w, h = int(1080 * S), int(1920 * S)
    b1, b2 = base.resize((w, h), Image.LANCZOS), mine.resize((w, h), Image.LANCZOS)
    lh = 18
    head, gut, foot = 74, 46, 30 + lh * (len(facts) + 5)   # 预留「待重采」告警行 + 注行
    cv = Image.new("RGB", (w + 24, head + h + gut + h + foot), (14, 14, 18))
    d = ImageDraw.Draw(cv)
    d.text((10, 6), "CR-T2x  同机位总览（上=原版18 / 下=我方实机）  scale=%.2f" % S, fill=(255, 220, 80), font=FONT_L)
    d.text((10, 28), "原版: 18_对局HUD_1080x1920.jpg（裁定基线）    我方: %s（%s）"
           % (os.path.basename(MINE), datetime.datetime.fromtimestamp(os.path.getmtime(MINE))),
           fill=(160, 220, 255), font=FONT_S)
    d.text((10, 48), "①红 ②黄 ③蓝 = 分区；逐项数值见 .ai-tmp/test/CR-T2x-overview.txt", fill=(200, 200, 200), font=FONT_S)
    cv.paste(b1, (12, head))
    cv.paste(b2, (12, head + h + gut))
    for tag, label, (x0, y0, x1, y1) in ZONES:
        col = ZONE_COL[tag]
        for oy, cap in ((head, "ORIGINAL"), (head + h + gut, "OURS")):
            bx = [12 + int(x0 * S), oy + int(y0 * S), 12 + int(x1 * S), oy + int(y1 * S)]
            d.rectangle(bx, outline=col, width=2)
            d.text((bx[0] + 4, bx[1] + 3), "%s %s" % (tag, cap), fill=col, font=FONT_S)
    fy = head + h + gut + h + 6
    d.text((10, fy), "差值摘要（逐项数值 + 出处见 txt）:", fill=(255, 220, 80), font=FONT_L)
    for i, t in enumerate(facts):
        d.text((10, fy + 20 + i * lh), "· " + t, fill=(210, 210, 210), font=FONT)
    bad = [t for t in ("1", "2", "3") if zstat and not zstat.get(t, True)]
    if bad:
        d.text((10, fy + 20 + len(facts) * lh + 4),
               "注意：分区 %s 的源码晚于本截图 ⇒ 该区行【待重采】，不作当前值（详见 txt 的分区有效性块）。"
               % "/".join("Z" + t for t in bad), fill=(255, 120, 120), font=FONT_S)
        off = 20
    else:
        off = 4
    d.text((10, fy + 20 + len(facts) * lh + off + (18 if bad else 0)),
           "注：差值≠0 的项一条不落；未取证项在 txt 里标「未取证」，不写作一致。",
           fill=(255, 160, 160), font=FONT_S)
    cv.save(OUT_MAIN)
    return cv.size


def build_user_sheet(base, mine, zstat):
    """给用户看的终版拼版：每区「原版 | 我方」并排 + 右侧中文说明（⛔ 不含内部术语/文件名）。"""
    SC = 0.42
    capw = 470
    lh = 19
    rows = []
    for tag, label, (x0, y0, x1, y1) in ZONES:
        b = base.crop((x0, y0, x1, y1))
        m = mine.crop((x0, y0, x1, y1))
        bw, bh = int(b.size[0] * SC), int(b.size[1] * SC)
        b = b.resize((bw, bh), Image.LANCZOS)
        m = m.resize((bw, bh), Image.LANCZOS)
        caps = USER_CAP[tag]
        rows.append((tag, label, b, m, caps))
    ADV = 48          # 每区实际行距（22 标题 + 18 角标 + 图高 + 余量），H 必须按同一个值算，否则页脚压在图上
    W = 20 + int(1080 * SC) * 2 + 20 + capw + 20
    H = 84 + sum(max(r[2].size[1], 22 + lh * (len(r[4]) + 1)) + ADV for r in rows) + 62
    cv = Image.new("RGB", (W, H), (16, 16, 20))
    d = ImageDraw.Draw(cv)
    d.text((20, 12), "Clover 对局界面 —— 与原版对照（左=原版参考图 / 右=当前实机）", fill=(255, 220, 80), font=FONT_L)
    d.text((20, 36), "同机位竖屏截图；分区说明在每行右侧。差异逐项数值另见交付文档。", fill=(180, 200, 220), font=FONT_S)
    d.text((20, 56), "说明：原版参考图为游戏原版截图（含未灰化/灰化的不同对局状态）；我方 = 本工程实机截图。",
           fill=(150, 150, 150), font=FONT_S)
    y = 84
    for tag, label, b, m, caps in rows:
        ok = zstat.get(tag, True)
        head = "Z%s %s%s" % (tag, label, "" if ok else "   ［该区待重采：场地相关源码更新晚于本截图］")
        d.text((20, y), head, fill=ZONE_COL[tag] if ok else (255, 140, 140), font=FONT_L)
        y += 22
        d.text((20, y), "原版", fill=(255, 220, 80), font=FONT_S)
        d.text((20 + b.size[0] + 20, y), "我方实机", fill=(120, 255, 160), font=FONT_S)
        cv.paste(b, (20, y + 18))
        cv.paste(m, (20 + b.size[0] + 20, y + 18))
        cx = 20 + b.size[0] * 2 + 40
        for i, c in enumerate(caps):
            d.text((cx, y + 18 + i * lh), "· " + c, fill=(215, 215, 215), font=FONT)
        y += max(b.size[1], 0) + 44
    d.text((20, H - 44), "注：本图由「原版参考图」与「本工程实机截图」同坐标裁剪拼版，非美术手绘；",
           fill=(255, 170, 170), font=FONT_S)
    d.text((20, H - 24), "     图中未列的差异项与全部数值读数见逐区差值表（含出处）。", fill=(255, 170, 170), font=FONT_S)
    cv.save(OUT_USER)
    return cv.size


def build_zone_figure(base, mine):
    rows = []
    for tag, label, (x0, y0, x1, y1) in ZONES:
        b, m = base.crop((x0, y0, x1, y1)), mine.crop((x0, y0, x1, y1))
        S = min(520.0 / b.size[0], 1.0)
        rows.append((tag, label,
                     b.resize((int(b.size[0] * S), int(b.size[1] * S)), Image.LANCZOS),
                     m.resize((int(m.size[0] * S), int(m.size[1] * S)), Image.LANCZOS)))
    W = max(r[2].size[0] for r in rows) * 2 + 40
    H = sum(max(r[2].size[1], r[3].size[1]) + 34 for r in rows) + 10
    cv = Image.new("RGB", (W + 20, H), (14, 14, 18))
    d = ImageDraw.Draw(cv)
    y = 6
    for tag, label, b, m in rows:
        d.text((10, y), "Z%s %s    左=原版18 / 右=我方实机（同坐标裁剪）" % (tag, label), fill=ZONE_COL[tag], font=FONT_L)
        y += 20
        cv.paste(b, (10, y))
        cv.paste(m, (10 + b.size[0] + 20, y))
        y += max(b.size[1], m.size[1]) + 18
    cv.save(OUT_ZONE)
    return cv.size


def main():
    global MINE
    args = sys.argv[1:]
    do_final = "--final" in args
    if "--mine" in args:
        MINE = args[args.index("--mine") + 1]
        if not os.path.isabs(MINE):
            MINE = os.path.join(ROOT, MINE)
    base = Image.open(BASE).convert("RGB")
    mine = Image.open(MINE).convert("RGB")
    assert base.size == (1080, 1920) and mine.size == (1080, 1920), (base.size, mine.size)

    def mt(p):
        return datetime.datetime.fromtimestamp(os.path.getmtime(p)) if os.path.exists(p) else None

    m_mine = mt(MINE)
    zstat = {}
    for z, src in SRC.items():
        m_src = mt(src)
        zstat[z] = bool(m_mine and m_src and m_src <= m_mine)
    rows = []

    def add(z, item, o, m, prov):
        rows.append((z, item, o, m, prov))

    # ---- 分区均值 ----------------------------------------------------------
    for tag, label, box in ZONES:
        add(tag, "分区均值 RGB", str(mean_rgb(base, box)), str(mean_rgb(mine, box)),
            "本探针（同坐标裁剪均值，步长=全幅缩放）")

    # ---- 同坐标探针点 ------------------------------------------------------
    for z, name, (x, y) in PROBES:
        prov = "本探针（同坐标取像素）"
        if "槽底处" in name:
            prov = "本探针（同坐标取像素）⇒ 原版此处是圣水条**槽底**（深蓝）；我方槽底不画、露出背后底图"
        add(z, "探针点 %s" % name, str(px(base, x, y)), str(px(mine, x, y)), prov)

    # ---- Z1 暗区（粗测：含面板/文字，非精确计时板） -------------------------
    bb_o, bb_m = dark_bbox(base, (300, 0, 800, 175)), dark_bbox(mine, (300, 0, 800, 175))
    add("1", "顶部暗区 bbox（粗测，含面板/文字）", str(bb_o), str(bb_m),
        "本探针（x300..800/y0..175 内 max(RGB)<120，非精确计时板轮廓）")

    # ---- Z2 地面 / 河 / 桥 / 血条 / 灌木 -----------------------------------
    gs_o, gs_m = river_bands(base), river_bands(mine)

    def fmt(gs):
        if not gs:
            return "无（多列扫描未取到水色带）"
        g = gs[0]
        return "y%d..%d（%d 列支持）" % (g["top"], g["bot"], g["n"])

    add("2", "河水带 y 范围（多列扫描 + 聚类，水色判据 g>140 且 b>150 且 r<g-60）", fmt(gs_o), fmt(gs_m),
        "本探针。⛔ 早先单列（x=540）口径既踩到中央石桥、又把浅灰石板误判成水 ⇒ 两个错都已更正；"
        "CR-T6b 给的原版河域 = y828..915")
    add("2", "河水颜色（水色带中段、按支持列取样均值）", str(band_color(base, gs_o[0] if gs_o else None)),
        str(band_color(mine, gs_m[0] if gs_m else None)),
        "本探针 ⇒ 与 CR-T6b 一致：原版 (55,196,205) / 我方 (0,152,183) **同族**，只标「偏暗偏饱和」，"
        "⛔ 不写成「不是这版美术」")
    add("2", "河道中点行「非水色」连续段 x（宽>=20）",
        str(bridge_runs(base, (gs_o[0]["top"], gs_o[0]["bot"]) if gs_o else None)),
        str(bridge_runs(mine, (gs_m[0]["top"], gs_m[0]["bot"]) if gs_m else None)),
        "本探针（单行横切，仅供定位参考）")
    add("2", "饱和红像素数（r>150 且 r-g>60 且 r-b>60）", str(count_pred(base, (0, 340, 1080, 1600), lambda q: q[0] > 150 and q[0] - q[1] > 60 and q[0] - q[2] > 60)),
        str(count_pred(mine, (0, 340, 1080, 1600), lambda q: q[0] > 150 and q[0] - q[1] > 60 and q[0] - q[2] > 60)),
        "本探针 ⇒ 原版含「塔 HP 条 + 红砖塔」；我方无 HP 条（塔为灰石）")
    add("2", "饱和绿像素数（g>150 且 g-r>40 且 g-b>40）", str(count_pred(base, (0, 340, 1080, 1600), lambda q: q[1] > 150 and q[1] - q[0] > 40 and q[1] - q[2] > 40)),
        str(count_pred(mine, (0, 340, 1080, 1600), lambda q: q[1] > 150 and q[1] - q[0] > 40 and q[1] - q[2] > 40)),
        "本探针 ⇒ 原版 = 两侧灌木墙（密集绿）；我方 = 大面积草地（另一种绿）")

    # ---- Z3 手牌 / 圣水 / 卡组名 -------------------------------------------
    add("3", "圣水条品红填充 bbox", str(magenta_box(base, (0, 1770, 1080, 1860))),
        str(magenta_box(mine, (0, 1770, 1080, 1860))),
        "本探针（⚠️ 填充长度随圣水值变化：原版 2/10、我方 6/10 ⇒ 长度差**不是**缺陷，只登记状态）")
    add("3", "手牌右侧「卡组名」区亮像素数 (x800..1080,y1650..1770)", str(bright_text_px(base, (800, 1650, 1080, 1770))),
        str(bright_text_px(mine, (800, 1650, 1080, 1770))),
        "本探针 ⇒ 原版有「彩虹城堡 / 仙极皇朝」白字，我方该区 0")
    add("3", "底栏暗底像素数 (max(RGB)<70)", str(count_pred(base, (0, 1600, 1080, 1920), lambda q: max(q) < 70)),
        str(count_pred(mine, (0, 1600, 1080, 1920), lambda q: max(q) < 70)),
        "本探针 ⇒ 底栏底板覆盖面积不同（原版暗底更满）")

    # ---- 引用行（现成结论，非本探针） -------------------------------------
    add("1", "【引用】顶部 HUD 图元组",
        "圣水数「10」+ 冠数图标 + 深底金边计时板 + 左侧「胜利条件/剩余时间」面板",
        "「0-0」冠数 + 绿字「剩余时间」+ 浅色边计时板（面积更小）",
        "总图目视（未做像素级逐件取证）；同坐标探针已给 3 点色差")
    add("2", "【引用·CR-T6b 更正】竞技场根因", "浅灰石板 + 金道 + 金框 + 灌木墙；相机纵向压缩比 **0.81**、格尺寸 **53.7×43.5**、场地纵向不铺满画布",
        "草地 + 土黄路；相机纵向压缩比 **1.0**、格尺寸 **60×60**",
        "CR-T6b 结论（主 agent 2026-09-23 转达）⇒ **整幅底图选型不同**，不是河/桥局部")
    add("2", "【引用·CR-T6b 更正】水色", "原版 (55,196,205)", "我方 (0,152,183)",
        "CR-T6b：同族（ΔR55/ΔG44/ΔB22）⇒ 只标「偏暗偏饱和」，⛔ 不是「另一版美术」")
    add("3", "【已知缺口·CR-T2g 登记】手牌卡体描边厚度", "≈1px（18 灰度态实测）", "6px（ui_out/43 本体自带）",
        "CR-T2g 上报；主 agent 裁定「接受（素材集内无更细卡体件）」")
    add("3", "【已知缺口·CR-T2g 登记】圣水条槽底 frame_155", "槽底可见（探针 (900,1815) 深蓝）", "实机不画（低 alpha 94 + Sliced）",
        "与 AV2 D-AV2-1 同症状；CR-T2g 上报；主 agent 裁定「本批不修」")
    add("3", "【已撤销·不列差】卡顶紫帽", "—", "—", "CR-T2 已撤销接线（不作为差值）")
    add("3", "手牌卡面帧表覆盖（CR-T2 已验收）", "60 张卡池全有帧号", "60/60（59 直查 + 1 已知缺口哨兵，missing=[]）",
        "CR-T2 编辑器断言 total=60 hit=59 knownGap=1")
    add("3", "手牌几何 / 卡面内缩（CR-T2 已验收）", "单卡 140×171 @x144；卡面四周露卡体 8px+",
        "140×171 @x144；内缩 左5.7/右5.7/上7.3/下6.3（`INSET 手牌#1` 行）",
        "CR-T2 实机 dump + 18 图量取")
    add("3", "手牌卡名文字（CR-T2 已验收）", "原版不显示卡名文字", "0 个卡名文本节点（hand_name_text_nodes=0）",
        "CR-T2 实机断言")
    add("3", "卡面裁切（CR-T2 已验收）", "每张卡面为独立图元", "60 帧逐帧裁透明包围盒（已修：此前固定窗口裁所有帧）",
        "CR-T2 裁切根因修复 + 帧表唯一真源（CrUiStyle.CardArtFrameTable）")

    facts = [
        "① 顶部：原版为「深底金边计时板 + 左侧面板 + 圣水数/冠数图标」，我方为「浅边小计时板 + 绿字剩余时间 + 0-0」",
        "② 地面：原版浅灰石板 + 金道 + 金框 + 灌木墙 / 我方草地 + 土黄路 ⇒ 整幅底图选型不同（CR-T6b 定性）",
        "② 相机/排版：纵向压缩比 原版 0.81 vs 我方 1.0；格尺寸 53.7×43.5 vs 60×60；原版场地纵向不铺满画布",
        "② 水色：同族（原版 (55,196,205) / 我方 (0,152,183)）⇒ 偏暗偏饱和，不是另一版美术（CR-T6b 更正）",
        "② 塔：原版含塔 HP 条+数字（饱和红 31080 px）/ 我方无 HP 条（20840 px，红塔改灰石）",
        "③ 卡框：原版 ≈1px 深描边 + 浅卡体 / 我方 6px 深描边 + 纯白卡体（素材集内无更细卡体件）",
        "③ 圣水条：原版槽底可见 / 我方槽底不画（frame_155 低 alpha+Sliced）",
        "③ 卡组名：原版手牌右侧有「彩虹城堡/仙极皇朝」1884 亮像素 / 我方 0",
    ]
    sz1 = build_figure(base, mine, facts, zstat)
    sz2 = build_zone_figure(base, mine)

    lbl = dict((t, l) for t, l, _ in ZONES)
    rel = os.path.relpath(MINE, ROOT)
    out = ["CR-T2x 逐区差值表  原版 18 vs 我方实机（同机位 1080x1920）",
           "基线：策划/参考图/18_对局HUD_1080x1920.jpg（裁定基线，20 图不作几何基线）",
           "我方：%s（%s）" % (rel, m_mine),
           "图：.ai-tmp/screenshots/CR-T2x-overview.png %dx%d；CR-T2x-overview-zones.png %dx%d" % (sz1 + sz2),
           "口径：同坐标探针 = 两图同一点取像素/裁剪；[引用]/[已知缺口] = 现成结论（出处见末列）；差值≠0 的项一条不落；未取证项标「未取证」，不写作一致。",
           ""]
    out.append("=== 分区有效性（采集即冻结：我方图必须晚于该区源码）===")
    for tag in ("1", "2", "3"):
        src = os.path.relpath(SRC[tag], ROOT)
        out.append("  Z%s  %s  源码 %s=%s  ⇒ %s" % (
            tag, lbl[tag], os.path.basename(src), mt(SRC[tag]),
            "有效" if zstat[tag] else "**待重采（源码比截图新，本区行不作当前值）**"))
    out.append("  我方图时间戳：%s" % m_mine)
    out.append("")

    def tag_of(z):
        return "" if zstat.get(z, True) else "【待重采】"

    for tag in ("1", "2", "3"):
        out.append("=== Z%s %s ===%s" % (tag, lbl[tag], "" if zstat[tag] else "   ⚠ 本区行全部待重采"))
        out.append("%-4s %-52s %-34s %-34s %s" % ("区", "项", "原版值", "我方值", "出处/口径"))
        for z, item, o, m, prov in rows:
            if z == tag:
                out.append("%-4s %-52s %-34s %-34s %s" % (tag_of(z) + "Z" + z, item, o, m, prov))
        out.append("")
    out.append("=== TSV（便于粘贴）===")
    out.append("zone\titem\torig\tours\tprovenance")
    for z, item, o, m, prov in rows:
        out.append("\t".join([tag_of(z) + "Z" + z, item, o, m, prov]))
    io.open(OUT_TXT, "w", encoding="utf-8").write("\n".join(out) + "\n")
    print("saved", OUT_MAIN, sz1)
    print("saved", OUT_ZONE, sz2)
    print("saved", OUT_TXT, len(out), "lines | rows =", len(rows))
    print("zone valid:", zstat, "| mine:", rel)
    if do_final:
        sz3 = build_user_sheet(base, mine, zstat)
        print("saved", OUT_USER, sz3)
    return 0


if __name__ == "__main__":
    sys.exit(main())
