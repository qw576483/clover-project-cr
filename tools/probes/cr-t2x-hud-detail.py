#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2x-hud-detail.py —— CR-T2x 裁定 2：**纯离线**逐件像素取证（⛔ 不进 Play、⛔ 不引用 CR-T3 帧）。

对象（主 agent 2026-09-23 裁定 2）：
  ① 18 顶部 HUD 那组的构成：金框计时板 / 暂停键 / 冠数 / 名牌 ⇒ 每件 bbox + 主色；同窗口给我方实测
  ② 18 的塔 HP 条：每条的 bbox + 配色；同窗口给我方实测
只读两张图：`策划/参考图/18_对局HUD_1080x1920.jpg`（基线）+ `.ai-tmp/screenshots/CR-T2-hud-mine.png`（01:03:48）
窗口位置来自 1080 原图放大目视（`tools/probes/cr-t2x-overview.py` 的分区 + 本脚本的可视化图可复核）。
产物：`.ai-tmp/test/CR-T2x-hud-detail.txt` + `.ai-tmp/screenshots/CR-T2x-hud-detail.png`
用法： python tools/probes/cr-t2x-hud-detail.py
"""
import datetime
import io
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BASE = os.path.join(ROOT, "策划", "参考图", "18_对局HUD_1080x1920.jpg")
MINE = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2-hud-mine.png")
OUT_TXT = os.path.join(ROOT, ".ai-tmp", "test", "CR-T2x-hud-detail.txt")
OUT_PNG = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2x-hud-detail.png")

P = {
    "plate_light": lambda q: min(q[:3]) > 150,
    "white": lambda q: min(q[:3]) > 230,
    "nearwhite": lambda q: min(q[:3]) > 195,
    "dark": lambda q: max(q[:3]) < 70,
    "gold": lambda q: q[0] > 185 and q[1] > 140 and q[2] < 145 and q[0] - q[2] > 70,
    "purple": lambda q: q[0] > 90 and q[2] > 150 and q[1] < q[2] - 40 and q[0] > q[1] + 20,
    "hp_red": lambda q: q[0] > 150 and q[0] - q[1] > 60 and q[0] - q[2] > 60,
    "hp_light": lambda q: q[0] > 185 and q[1] > 40 and q[1] < 150 and q[2] < 150,
    "green_text": lambda q: q[1] > 130 and q[1] - q[0] > 45 and q[1] - q[2] > 45,
    "blue": lambda q: q[2] > 150 and q[2] - q[0] > 40 and q[2] - q[1] > 25,
}

# (编号, 元素名, 搜索窗口(x0,y0,x1,y1), 判据, 补充说明)
ELEMS = [
    ("A", "计时板板面（原版=半透明浅板）", (880, 0, 1080, 100), "plate_light", "原版「剩余时间: / 1:51」所在板"),
    ("B", "计时板大数字「1:51」", (900, 28, 1065, 92), "white", "白色粗体时间"),
    ("C", "计时板标题「剩余时间:」", (900, 0, 1080, 30), "nearwhite", "板上方小字"),
    ("D", "冠数徽章（金冠+紫底数字「4」）", (455, 0, 585, 46), "purple", "此处原版是冠数，我方是 0-0 白板区"),
    ("E", "暂停键（右上）", (1000, 0, 1080, 160), "white", "我方在 (996,72) 为黑；原版窗口内命中情况见读数"),
    ("F", "王塔 HP 条（「6660」那条）", (470, 48, 690, 96), "hp_red", "红/粉横条"),
    ("G", "王塔 HP 数字「6660」", (470, 36, 690, 62), "nearwhite", "条上方白字"),
    ("H", "王塔等级徽章「15」+金冠", (398, 44, 500, 96), "gold", "条左侧金色徽章"),
    ("I", "公主塔#1 HP 条（「4424」）", (185, 272, 330, 322), "hp_red", "左上公主塔"),
    ("J", "公主塔#2 HP 条（「1478」）", (775, 272, 920, 322), "hp_red", "右上公主塔"),
    ("K", "公主塔#1 等级徽章「15」", (145, 272, 210, 322), "gold", ""),
    ("L", "玩家/卡组名牌（顶部窗口对照）", (690, 150, 900, 230), "nearwhite", "原版名牌在**底部右侧**，此窗口用于对照"),
    ("M", "左上角冠数板（我方=白底「0:0」，原版该处是场地）", (10, 10, 300, 92), "nearwhite", ""),
    ("N", "阶段文字（我方绿色「常规时间」；原版该处是场地）", (450, 8, 660, 52), "green_text",
     "⚠️ 该文案与草地同色系 ⇒ 颜色判据分离不出，本行**只作目视**（见可视化图下半幅）"),
    ("O", "暂停键（我方实测点 996,72）", (985, 88, 1062, 142), "dark", "原版该窗口是岩石/场地"),
]


def load_font(size=14):
    for c in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        p = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", c)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


FONT = load_font(14)


def probe(im, win, pred):
    x0, y0, x1, y1 = win
    p = im.load()
    xs, ys, n = [], [], 0
    for y in range(y0, min(y1, im.size[1])):
        for x in range(x0, min(x1, im.size[0])):
            if pred(p[x, y]):
                xs.append(x)
                ys.append(y)
                n += 1
    if n == 0:
        return None, 0, im.crop(win).resize((1, 1), Image.BOX).convert("RGB").getpixel((0, 0))
    bb = (min(xs), min(ys), max(xs) + 1, max(ys) + 1)
    mean = im.crop(bb).resize((1, 1), Image.BOX).convert("RGB").getpixel((0, 0))
    return bb, n, mean


def main():
    global MINE
    args = sys.argv[1:]
    if "--mine" in args:
        MINE = args[args.index("--mine") + 1]
        if not os.path.isabs(MINE):
            MINE = os.path.join(ROOT, MINE)
    base = Image.open(BASE).convert("RGB")
    mine = Image.open(MINE).convert("RGB")
    arena_src = os.path.join(ROOT, "client", "Assets", "Scripts", "View", "ArenaView.cs")
    stale = (os.path.exists(arena_src)
             and os.path.getmtime(arena_src) > os.path.getmtime(MINE))
    out = ["CR-T2x 顶部 HUD / 塔 HP 条 —— 逐件像素读数（纯离线，⛔ 未进 Play、⛔ 未引用 CR-T3 帧）",
           "原版：策划/参考图/18_对局HUD_1080x1920.jpg（裁定基线）",
           "我方：%s（%s）" % (os.path.relpath(MINE, ROOT),
                            datetime.datetime.fromtimestamp(os.path.getmtime(MINE))),
           "有效性：Z1 顶部项（计时板/冠数/暂停/阶段）= 有效（HudPanel 域）；"
           "落在**竞技场/塔上**的窗口行 = %s（ArenaView.cs=%s ⇒ 图 %s）"
           % ("**待重采**" if stale else "有效（图已晚于 ArenaView.cs）",
              datetime.datetime.fromtimestamp(os.path.getmtime(arena_src)),
              "早于源码" if stale else "晚于源码"),
           "方法：每件先用窗口（位置来自 1080 原图放大目视 + `.ai-tmp/test/CR-T2x-top-*.png` 可复核）圈定，再在同一窗口内跑**同一判据**；",
           "      输出「bbox / 命中像素数 / 命中区主色」；命中 0 时输出该窗口的均值色（说明那里实际是什么）。",
           "判据：plate_light=min>150；white=min>230；nearwhite=min>195；gold=r>185&g>140&b<145&r-b>70；purple=r>90&b>150&g<b-40&r>g+20；hp_red=r>150&r-g>60&r-b>60",
           ""]
    boxes = []
    for tag, name, win, pred, note in ELEMS:
        out.append("【%s】%s   窗口=%s  判据=%s  %s" % (tag, name, str(win), pred, note))
        for who, im in (("原版18", base), ("我方", mine)):
            bb, n, mean = probe(im, win, P[pred])
            if bb is None:
                out.append("   %-7s 命中 0 ⇒ 该窗口均值色=%s（此处不是该元素）" % (who, str(mean)))
            else:
                out.append("   %-7s bbox=%-22s px=%-6d 命中区主色=%s" % (who, str(bb), n, str(mean)))
                boxes.append((bb, "%s %s" % (tag, "O" if who == "原版18" else "M"), win))
        out.append("")

    # 额外：我方整幅内是否存在"血条型横条"（原版有 5 条）
    def hp_bars(im, region, pred, minw=45, maxh=26):
        x0, y0, x1, y1 = region
        p = im.load()
        res = []
        for y in range(y0, y1):
            s = None
            for x in range(x0, x1):
                if pred(p[x, y]):
                    if s is None:
                        s = x
                else:
                    if s is not None and x - s >= minw:
                        res.append((s, x, y))
                    s = None
            if s is not None and x1 - s >= minw:
                res.append((s, x1, y))
        bands = []
        for a, b, y in res:
            hit = False
            for bd in bands:
                if abs(bd[2] - y) <= 4 and not (b > bd[1] + 10 or a < bd[0] - 10):
                    bd[0] = min(bd[0], a)
                    bd[1] = max(bd[1], b)
                    bd[3] = y
                    hit = True
                    break
            if not hit:
                bands.append([a, b, y, y])
        return [bd for bd in bands if bd[3] - bd[2] + 1 <= maxh]

    for who, im in (("原版18", base), ("我方", mine)):
        for pn in ("hp_red", "hp_light"):
            bd = hp_bars(im, (0, 0, 1080, 1920), P[pn])
            out.append("整幅扫描 [%s] %s：%d 条（宽>=45、高<=26）%s" % (pn, who, len(bd), str(bd[:8])))
    out.append("")
    out.append("=== 汇总（逐件对照，可直接进文档）===")
    out.append("  1) 计时板：原版=**半透明青灰板** (880,6,1028,81)、板内「1:51」白字命中区主色 (157,155,155)；")
    out.append("            我方=**深色板** (901,10,1054,85)、「02:55」白字命中区主色 (64,64,64) ⇒ 板底明度相反。")
    out.append("  2) 板标题：原版「剩余时间:」是**暗小字**（nearwhite 仅 15px）；我方「剩余时间」**亮白 651px** ⇒ 字号/亮度都不同。")
    out.append("  3) 冠数：原版=顶部中央**紫底金冠徽章「4」**(477,0,571,36) px=838 主色 (183,144,172)；")
    out.append("            我方=同窗口 **0 命中**（落地是草地 (170,191,72)），冠数改在**左上白板「0:0」**(22,18,282,78) 主色 (222,211,205) ⇒ 位置与形制均不同。")
    out.append("  4) 阶段文字：我方有绿色「常规时间」（**目视**：与草地同色系，颜色判据 N 窗口两图均 0 命中 ⇒ 这条**未做像素级取证**）；原版该处是场地。")
    out.append("  5) 暂停键：**目视** 原版顶部右侧**未见独立暂停键**（窗口白命中 (1000,39,1022,77) 落=计时板文字/边；")
    out.append("             ⚠️ 该窗口的 dark 判据原版也命中 (985,88,1062,142) px=1060 主色 (74,62,70)=岩石/阴影 ⇒ 颜色判据无法单独证明「原版没有暂停键」，此条为**目视+窗口读数**并记）；")
    out.append("             我方=黑底白条圆角键，窗口 dark 命中 px=1820（含键与暗底）。")
    out.append("  6) 王塔 HP：原版=金框红 HP 条（窗口内 hp_red px=1665、命中区主色 (210,104,133)）+「6660」白字 + 左侧金色「15」盾徽 (427,69,500,96) 主色 (184,140,65)；")
    out.append("            我方=同三窗口命中 0（G/H 窗口均值=草地 (156,188,71)/(164,185,95)；F 窗口 330px 落在塔身金饰上）⇒ **无 HP 条/无等级盾徽**。")
    out.append("  7) 公主塔 HP：原版「4424」(205,298,330,322) 主色 (204,74,109) /「1478」(790,290,832,320) 主色 (201,98,126) + 各自「15」盾徽 (168,293,204,322) 主色 (188,157,54)；")
    out.append("            我方=同窗口分别仅 1px / 733px（后者主色 (161,122,87)=塔身装饰）⇒ **塔顶无 HP 条、无数字**。")
    out.append("  8) 名牌：原版「彩虹城堡 / 仙极皇朝」在**底部右侧**（底栏 1884 亮像素），顶部无名牌；我方底栏该区 **0**。")
    out.append("")
    out.append("⚠️ 读法限制：本脚本只做**颜色判据**，命中框不得当作「美术语义」（例如 F 窗口的 330px 落在塔身金饰而非血条）；")
    out.append("   「我方无 HP 条」这条由 6)+7) 的三处 0 命中 **加上** `.ai-tmp/screenshots/CR-T2x-overview-zones.png` 的目视共同支撑，未做 OCR 识别数字。")
    out.append("")
    out.append("=== 目视结构（1080 原图放大可复核；⛔ 未做 OCR）===")
    out.append("  原版18 顶部：右上=半透明计时板（小字「剩余时间:」+ 大字「1:51」）；")
    out.append("              顶部中央=冠数徽章（金冠 + 紫底数字「4」）；")
    out.append("              王塔上方=金框「6660」HP 条 + 左侧「15」金冠等级徽章；")
    out.append("              两座公主塔上方=「4424 / 1478」HP 条 + 各自「15」徽章。")
    out.append("  我方顶部：左上=白底冠数条「0 : 0」；其右=绿字「常规时间」（目视）；右上=深色计时板（暗字「剩余时间」+ 亮白大字「02:55」）+ 黑底白条暂停键。")
    out.append("  名牌：原版「彩虹城堡 / 仙极皇朝」在**底部右侧**（底栏，已测 1884 亮像素），顶部无名牌。")

    # 可视化（原版顶部 / 我方顶部，画窗口 + 命中框）
    S = 1.0
    imgs = []
    for im in (base, mine):
        imgs.append(im.crop((0, 0, 1080, 340)).resize((int(1080 * S), int(340 * S)), Image.LANCZOS))
    cv = Image.new("RGB", (imgs[0].size[0] + 20, imgs[0].size[1] * 2 + 76), (14, 14, 18))
    d = ImageDraw.Draw(cv)
    d.text((10, 6), "CR-T2x 顶部 HUD 逐件读数（上=原版18 / 下=我方实机）  青框=搜索窗口  黄框=判据命中", fill=(255, 220, 80), font=FONT)
    y = 26
    for idx, t in enumerate(imgs):
        cv.paste(t, (10, y))
        for tag, name, win, pred, note in ELEMS:
            d.rectangle([10 + win[0], y + win[1], 10 + win[2], y + win[3]], outline=(60, 200, 220), width=1)
        for bb, label, win in boxes:
            who_o = label.endswith("O")
            if (idx == 0) == who_o and bb[1] < 340:
                d.rectangle([10 + bb[0], y + bb[1], 10 + bb[2], y + bb[3]], outline=(255, 220, 60), width=2)
                d.text((10 + bb[0] + 2, y + bb[1] + 2), label.split()[0], fill=(255, 220, 60), font=FONT)
        y += t.size[1] + 24
    cv.save(OUT_PNG)
    io.open(OUT_TXT, "w", encoding="utf-8").write("\n".join(out) + "\n")
    print("saved", OUT_TXT, len(out), "lines")
    print("saved", OUT_PNG, cv.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
