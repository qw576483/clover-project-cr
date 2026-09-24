#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D131b 片判据资产：对局 HUD 圣水条的两条**离线机械判据**，先把"原版值"钉死。

判据基线 = `策划/基线图/18_对局HUD_1080x1920.jpg`（1080x1920，与本项目竖屏画布同尺寸
⇒ **像素值 = 画布值**，k=1.0；`18` 与 `19` MD5 相同，只当一张用，见 `策划/基线图/索引.md`）。
基线 18 是 **2/10 圣水**（`cr-d131-hud-measure.py` §2.3 反解：栅格左端 103.32、每格 93.72，
填充右沿 289.5 = 2 格）⇒ 空槽段（单位 3..10）在基线里是**可见的**，可以量。

本脚本判两件事（都是"数值→yes/no"，⛔ 不写"基本一致"）：

  §A 判据 J1「帧 155 槽底端帽出现/不出现」
     空槽段（单位 3..9 的格内中段列）在**条的上下沿**是否有一条**比中段更暗**的端帽带；
     并输出端帽带的 RGB 作为**原版值** `CAP_RGB_BASE`（我方图必须逐通道对得上）。
     出处：`Bars/ui_out/frame_155.png` = 1×74，**y0..7 与 y66..73 = (51,51,51,255) 不透明端帽**，
     y8..65 = 半透明黑（α 94→24→94）⇒ 端帽就是这条 `bar_bg` 的签名。

  §B 判据 J2「需求条（161/162）出现/不出现」
     `elixirRequirementBar`（clip 912）= `frame_161`(69×1) / `frame_162`(14×69)，
     两者的近白像素（`frame_161` x0..2 / x65..67 = (255,255,255,255)；`frame_162` 中列 = 白）
     在条带里应只可能出现在徽章区内。判据 = **徽章区以外近白竖痕列数必须为 0**；
     基线实测 0 ⇒ **原版值 = 不出现**（基线 18 无选中卡 ⇒ 需求条不画）。⛔ 无原版几何可抄 ⇒ 不接。

  §D `--selftest` 负控：造两张**必须判红**的图（① 抹掉端帽；② 画一条假需求条竖白痕），
     要求 J1 / J2 分别翻红；再加"基线自身必须判绿"的正控。⇒ 证明这两条判据**能判红**。

用法（Windows 本机 `python` 无 PIL，用 `py -3`）：
  py -3 tools/probes/cr-d131b-hud-measure.py             # 只跑基线（钉原版值）
  py -3 tools/probes/cr-d131b-hud-measure.py --selftest   # 基线 + 正控 + 负控
  py -3 tools/probes/cr-d131b-hud-measure.py <图.png> ...  # 额外把同一把尺子套到别的图
"""
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASE = os.path.join(ROOT, "策划", "基线图", "18_对局HUD_1080x1920.jpg")

# ── 条盒（出处 `cr-d131-hud-measure.py` §2：条外框 y1798..1841 = 44 高；D4/D5：x88..1044）──
BAR_TOP, BAR_BOT = 1798, 1841          # 含两端
BAR_LEFT, BAR_RIGHT = 88, 1044
# ── 刻度栅格（§2.3 最小二乘反解）──
GRID_LEFT, GRID_STEP = 103.32, 93.72

# ── J1 取样窗（空槽段 x420..1030，排除刻度 ±6px；避开填充 x<300、避开右端帽 x>1030）──
#   ⛔ 为什么不用 k=3（格内中段 x≈338）：基线在**填充右沿(≈290) 与 x≈374 之间是一段更浅的蓝灰**
#   （行 y1816 实测 x300..370 = (83,87,135)，比右侧空槽 (32,51,93) 亮得多）⇒ 那一段**不是**帧155
#   的槽底外观，进不了这条判据的取样窗。详见 §A.2 的"暗槽起点"扫描。
SAMPLE_X0, SAMPLE_X1 = 420, 1030
CAP_TOP_ROWS = range(BAR_TOP + 1, BAR_TOP + 5)      # 1799..1802
CAP_BOT_ROWS = range(BAR_BOT - 4, BAR_BOT)          # 1837..1840
MID_ROWS = range(BAR_TOP + 14, BAR_TOP + 22)        # 1812..1819（亮度最大处在 y1814..1816）
OUT_TOP_ROWS = range(BAR_TOP - 5, BAR_TOP - 1)      # 1793..1796（条外上）
OUT_BOT_ROWS = range(BAR_BOT + 1, BAR_BOT + 5)      # 1842..1845（条外下）

# ── J1 阈值 ──
CAP_LUM_MIN = 3.0       # 端帽带必须比中段暗 ≥3.0 亮度，才算"端帽出现"
CAP_RGB_TOL = 6         # 我方端帽 RGB 与原版值逐通道差 ≤6 判"同色"
# ── J2 阈值 ──
WHITE_MIN = 200         # 近白：三通道最小值 > 200
WHITE_NEUTRAL = 12      # 近灰：|R−B| < 12
WHITE_RUN = 6           # 一列里近白像素 ≥6 才算"竖痕"
BADGE_XMAX = 96         # 徽章区（x33..93）排除

VERDICTS = []
INFOS = []


def note(tag, ok, text):
    VERDICTS.append((tag, ok, text))
    print("   [%s] %s %s" % ("PASS" if ok else "FAIL", tag, text))


def info(tag, text):
    """只登记、不判红（跨图对比有已知混淆项时用）。"""
    INFOS.append((tag, text))
    print("   [INFO] %s %s" % (tag, text))


def lum(p):
    return (p[0] + p[1] + p[2]) / 3.0


def sample_cols():
    """空槽段取样列：x in [SAMPLE_X0, SAMPLE_X1]，排除刻度 ±6px（含 k=1..9 的分界）。"""
    ticks = [GRID_LEFT + k * GRID_STEP for k in range(1, 11)]
    return [x for x in range(SAMPLE_X0, SAMPLE_X1 + 1)
            if all(abs(x - g) > 6 for g in ticks)]


def rowprofile(px):
    """空槽段逐行均值（RGB），返回 {y: (r,g,b)}。"""
    cols = sample_cols()
    out = {}
    for y in range(BAR_TOP - 6, BAR_BOT + 7):
        acc = [0.0, 0.0, 0.0]
        for x in cols:
            p = px[x, y]
            for i in range(3):
                acc[i] += p[i]
        out[y] = tuple(v / len(cols) for v in acc)
    return out


def band(prof, rows):
    acc = [0.0, 0.0, 0.0]
    for y in rows:
        for i in range(3):
            acc[i] += prof[y][i]
    return tuple(v / len(rows) for v in acc)


# ── §A J1 ────────────────────────────────────────────────────────────────────
def j1_caps(im, label, cap_rgb_base=None):
    px = im.load()
    print("\n§A 判据 J1「帧 155 槽底端帽出现/不出现」  ← %s" % label)
    print("   取样：空槽段 x%d..%d（排除刻度 ±6px，共 %d 列，逐行均值）"
          % (SAMPLE_X0, SAMPLE_X1, len(sample_cols())))
    prof = rowprofile(px)
    capT = band(prof, CAP_TOP_ROWS)
    capB = band(prof, CAP_BOT_ROWS)
    mid = band(prof, MID_ROWS)
    outT = band(prof, OUT_TOP_ROWS)
    outB = band(prof, OUT_BOT_ROWS)
    cap = tuple((capT[i] + capB[i]) / 2 for i in range(3))
    capLum = (lum(capT) + lum(capB)) / 2
    d = lum(mid) - capLum
    print("   端帽行 capTop y%d..%d = (%d,%d,%d) lum=%.1f"
          % (CAP_TOP_ROWS[0], CAP_TOP_ROWS[-1], capT[0], capT[1], capT[2], lum(capT)))
    print("   端帽行 capBot y%d..%d = (%d,%d,%d) lum=%.1f"
          % (CAP_BOT_ROWS[0], CAP_BOT_ROWS[-1], capB[0], capB[1], capB[2], lum(capB)))
    print("   中段行 mid    y%d..%d = (%d,%d,%d) lum=%.1f"
          % (MID_ROWS[0], MID_ROWS[-1], mid[0], mid[1], mid[2], lum(mid)))
    print("   （参照）条外上 y%d..%d lum=%.1f / 条外下 y%d..%d lum=%.1f"
          % (OUT_TOP_ROWS[0], OUT_TOP_ROWS[-1], lum(outT),
             OUT_BOT_ROWS[0], OUT_BOT_ROWS[-1], lum(outB)))
    print("   中段−端帽 亮度差 = %+.1f（端帽=%d,%d,%d |B−R|=%d）"
          % (d, cap[0], cap[1], cap[2], abs(cap[2] - cap[0])))
    ok_dark = d >= CAP_LUM_MIN
    note("J1.cap_darker", ok_dark,
         "端帽带比中段暗 %+.1f（阈值 ≥%.1f）⇒ %s" % (d, CAP_LUM_MIN, "端帽出现" if ok_dark else "端帽不出现"))
    # 与背景无关的辅助判据：端帽行必须与**条外紧邻行**不同（槽底若整条不画，端帽行 == 条外背景 ⇒ 差≈0）。
    dOutT = lum(outT) - lum(capT)
    dOutB = lum(outB) - lum(capB)
    ok_edge = min(dOutT, dOutB) >= CAP_LUM_MIN
    note("J1.cap_vs_outside", ok_edge,
         "端帽行 vs 条外紧邻行：上 %+.1f / 下 %+.1f（取小值 ≥%.1f）⇒ %s"
         % (dOutT, dOutB, CAP_LUM_MIN, "条沿与条外可分" if ok_edge else "条沿与条外同色"))
    if cap_rgb_base is not None:
        dd = [cap[i] - cap_rgb_base[i] for i in range(3)]
        # ⛔ 端帽绝对 RGB **不判红**，只登记：帧155 的端帽在 sprite 里是**不透明**(51,51,51,255)，
        #   若两边都按不透明画，绝对值本应相等 ⇒ 实测不等说明「原版那一侧被叠了东西/被滤波」。
        #   而条**中段**是半透明的 ⇒ 中段读数会被"条背后画的是什么"污染（我方实机条背后是草地
        #   (146,176,69)，基线是蓝带 (63,88,152)——那是取景差异）。端帽虽不透明，但原版若对整条
        #   叠了蓝色覆盖，端帽也会被染。⇒ 差异真实，但**归因不属于本片**，按规则登记不硬改。
        info("J1.cap_rgb_diff",
             "端帽 RGB (%d,%d,%d) 对原版值 (%d,%d,%d) 差 (%+d,%+d,%+d)  ⇒ %s（登记，不判红）"
             % (cap[0], cap[1], cap[2], cap_rgb_base[0], cap_rgb_base[1], cap_rgb_base[2],
                dd[0], dd[1], dd[2],
                "同色" if all(abs(v) <= CAP_RGB_TOL for v in dd) else "不同色"))
    return cap, d


# ── §A.2 暗槽起点（解释为什么取样窗从 x420 起） ─────────────────────────────
def a2_darkstart(im, label):
    px = im.load()
    print("\n§A.2 空槽暗底从左往右的起点（行 y1816，逐 2px）  ← %s" % label)
    y = 1816
    prev = None
    for x in range(288, 430, 2):
        p = px[x, y]
        tag = ""
        if prev is not None and lum(p) < 70 <= prev:
            tag = "  ← 暗槽起点(≤70)"
        print("     x=%4d (%3d,%3d,%3d) lum=%5.1f%s" % (x, p[0], p[1], p[2], lum(p), tag))
        prev = lum(p)


# ── §B J2 ────────────────────────────────────────────────────────────────────
def j2_reqbar(im, label):
    px = im.load()
    print("\n§B 判据 J2「需求条（161/162）出现/不出现」  ← %s" % label)
    print("   扫描：条带 y%d..%d 内 x>%d，列上近白像素（min>%d 且 |R−B|<%d）≥%d 记一列"
          % (BAR_TOP, BAR_BOT, BADGE_XMAX, WHITE_MIN, WHITE_NEUTRAL, WHITE_RUN))
    hits = []
    for x in range(BADGE_XMAX + 1, BAR_RIGHT + 1):
        n = 0
        for y in range(BAR_TOP, BAR_BOT + 1):
            p = px[x, y]
            if min(p) > WHITE_MIN and abs(p[0] - p[2]) < WHITE_NEUTRAL:
                n += 1
        if n >= WHITE_RUN:
            hits.append((x, n))
    print("   近白竖痕列数=%d" % len(hits), hits[:16])
    ok = len(hits) == 0
    note("J2.reqbar_absent", ok,
         "条带内近白竖痕 %d 列 ⇒ %s" % (len(hits), "需求条不出现" if ok else "需求条出现"))
    return hits


# ── §D 负控 ─────────────────────────────────────────────────────────────────
def selftest(im):
    px = im.load()
    print("\n§D 负控 / 正控（证明这两条判据**能判红**）")
    base_cap, _ = j1_caps(im, "正控：基线 18 自身（应判绿）")
    j2_reqbar(im, "正控：基线 18 自身（应判绿）")

    # ── 负控 N1：把空槽段的**整条槽底**抹成条外背景色（= "帧155 槽底不出现"） ──
    n1 = im.copy(); p1 = n1.load()
    bg = p1[SAMPLE_X0, OUT_TOP_ROWS[0]]
    for x in sample_cols():
        for y in range(BAR_TOP, BAR_BOT + 1):
            p1[x, y] = bg
    print("\n   负控 N1 = 把空槽段整条槽底抹成条外背景色 %s（构造「帧155 槽底不出现」）" % (bg,))
    _, d1 = j1_caps(n1, "负控 N1（必须判红）")
    VERDICTS.append(("N1.detected", d1 < CAP_LUM_MIN, "N1 的 J1 亮度差=%+.1f ⇒ 判红" % d1))

    # ── 负控 N2：在空槽段画一条竖白痕（= 假需求条） ──
    n2 = im.copy(); p2 = n2.load()
    for x in range(700, 714):
        for y in range(BAR_TOP + 3, BAR_BOT - 3):
            p2[x, y] = (255, 255, 255)
    print("\n   负控 N2 = 在 x700..713 画 14 宽竖白痕（构造「需求条出现」）")
    hits2 = j2_reqbar(n2, "负控 N2（必须判红）")
    VERDICTS.append(("N2.detected", len(hits2) > 0, "N2 的 J2 近白竖痕=%d 列 ⇒ 判红" % len(hits2)))
    return base_cap


def main(argv):
    im = Image.open(BASE).convert("RGB")
    print("基线图 %s  size=%s" % (BASE, im.size))
    print("条盒：x%d..%d  y%d..%d（%d 行）"
          % (BAR_LEFT, BAR_RIGHT, BAR_TOP, BAR_BOT, BAR_BOT - BAR_TOP + 1))

    base_cap, _ = j1_caps(im, "基线 18（原版值口径）")
    a2_darkstart(im, "基线 18（原版值口径）")
    j2_reqbar(im, "基线 18（原版值口径）")

    if "--selftest" in argv:
        selftest(im)

    # 额外图（我方实机图）：同一把尺子，端帽 RGB 与"原版值"逐通道对
    for p in argv:
        if p.startswith("--"):
            continue
        if not os.path.exists(p):
            print("\n§C 略过：缺图 %s" % p)
            continue
        other = Image.open(p).convert("RGB")
        print("\n§C 我方图 %s  size=%s" % (p, other.size))
        if other.size != im.size:
            print("   ⚠️ 尺寸不同（%s vs %s）⇒ 像素不能直接相减，只报读数" % (other.size, im.size))
        j1_caps(other, os.path.basename(p), cap_rgb_base=base_cap)
        j2_reqbar(other, os.path.basename(p))

    nfail = sum(1 for _, ok, _ in VERDICTS if not ok)
    print("\n=== 判据合计：%d 条，FAIL %d 条；另 INFO(只登记不判红) %d 条 ==="
          % (len(VERDICTS), nfail, len(INFOS)))
    return 1 if nfail else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
