# -*- coding: utf-8 -*-
"""D130b 取证：**原版训练营**（策划/基线图/03_对局_1320x2868.jpg）河道区的定量剖面（只读）。

回答 3 个问题（本片 ⑥「地图不对 / 路连不上 / 乱七八糟」的判据来源）：
  Q1 水带（原版蓝水）占哪些行？（⇒ 原版 px/格，纵向）
  Q2 水带上沿以上 / 下沿以下的**泥带(MUD) + 石台(STONE)** 各占多少行、覆盖率多少？
     ⇒ 判定"岸带到底在哪一侧、有几侧"
  Q3 水带行区间内的**桥板(tan/木色)** 列区间 → 桥宽（格）与桥心（格）；
     并在水带**下方**同一批列区间内检查是否还有第二份木板（判我方"木板墙"是否为素材残留）

分类口径与 `lead-d130b-bands.py` 同源：
  T=透明(这里 jpg 无透明，恒 0) G=草  B=水/蓝  M=土/木(tan: r>g+8 且 r>b+25)
  S=灰/石(|r-g|<14 且 |g-b|<14 且 40<lum<215)  ?=其它
用法: python lead-orig03-river.py
"""
import numpy as np
from PIL import Image

SRC = r"C:\Work\Server\f-v2\clover-project-cr\策划\基线图\03_对局_1320x2868.jpg"
im = Image.open(SRC).convert("RGB")
a = np.array(im).astype(np.int16)
H, W = a.shape[:2]
print(f"# {SRC}\n# {W}x{H}")


def classify(arr):
    """arr: (N,3) int16 -> tag indices"""
    r, g, b = arr[:, 0], arr[:, 1], arr[:, 2]
    lum = (r + g + b) / 3.0
    out = np.full(arr.shape[0], "?", dtype="<U1")
    out[(g > r + 6) & (g > b + 6)] = "G"
    out[(b > r + 20) & (b > g + 5)] = "B"
    out[(r > g + 8) & (r > b + 25)] = "M"
    greyy = (np.abs(r - g) < 14) & (np.abs(g - b) < 14) & (lum > 40) & (lum < 215)
    out[greyy & (out == "?")] = "S"
    return out


# ── 全图逐行分类（x 全宽），找水带 ──
rows = np.array([classify(a[y]) for y in range(H)])          # (H, W) '<U1'
fracB = (rows == "B").mean(axis=1)
# 水带 = fracB > 0.35 的最长连续段
ys = np.where(fracB > 0.35)[0]
bands = []
if len(ys):
    s = ys[0]
    prev = ys[0]
    for y in ys[1:]:
        if y != prev + 1:
            bands.append((s, prev))
            s = y
        prev = y
    bands.append((s, prev))
bands.sort(key=lambda t: t[1] - t[0], reverse=True)
print("# 候选水带（fracB>0.35 的连续行段，按高度降序，前 5）:")
for s, e in bands[:5]:
    print(f"    rows {s}..{e}  ({e - s + 1} 行)  max fracB={fracB[s:e + 1].max():.3f}")

if not bands:
    raise SystemExit("未找到水带")
wTop, wBot = bands[0]
wH = wBot - wTop + 1
print(f"\n# 取最长水带 rows {wTop}..{wBot}（{wH} 行）")
print(f"# 若水带 = 2 格 ⇒ 原版 {wH / 2.0:.2f} px/格（纵向）")

# ── 水带内的横向范围（水带左/右沿）──
mid = (rows[wTop:wBot + 1] == "B").mean(axis=0)
xs = np.where(mid > 0.5)[0]
wx0, wx1 = int(xs.min()), int(xs.max())
print(f"# 水带横向（过半行是水）: x {wx0}..{wx1}（{wx1 - wx0 + 1} px）")

# ── Q2：水带上沿以上 / 下沿以下 各 90 行的分类覆盖 ──
print("\n# Q2 上沿以上 / 下沿以下 逐行分类覆盖（每 6 行汇总；窗口 x 取水带横向范围）")
hdr = "   row |   G     M     B     S     ?"
for label, r0, r1 in (("ABOVE(上沿以上, red 侧)", wTop - 90, wTop - 1),
                      ("BELOW(下沿以下, blue 侧)", wBot + 1, wBot + 90)):
    print(f"\n## {label}: rows {r0}..{r1}")
    print(hdr)
    for y in range(r0, r1 + 1):
        if y < 0 or y >= H:
            continue
        t = rows[y, wx0:wx1 + 1]
        n = len(t)
        c = {k: float((t == k).sum()) / n for k in "GMBS?"}
        if (y - r0) % 6 == 0:
            print(f"  {y:5d} | {c['G']:.3f} {c['M']:.3f} {c['B']:.3f} {c['S']:.3f} {c['?']:.3f}")
    # 带内汇总
    seg = rows[max(0, r0):min(H, r1 + 1), wx0:wx1 + 1].ravel()
    tot = len(seg)
    print(f"  -- 汇总: " + "  ".join(f"{k}={float((seg == k).sum()) / tot:.3f}" for k in "GMBS?"))


def runs_of(tags, ch, minw):
    out = []
    cur = None
    for i, t in enumerate(tags):
        if t == ch:
            if cur is None:
                cur = i
        else:
            if cur is not None and i - cur >= minw:
                out.append((cur, i - 1))
            cur = None
    if cur is not None and len(tags) - cur >= minw:
        out.append((cur, len(tags) - 1))
    return out


# ── Q3：水带行区间内的木色(tan)列区间 = 桥板 ──
print("\n# Q3 水带行区间内的木色(M)列区间（>=6px），按行汇总成束")
colM = None
for y in range(wTop, wBot + 1):
    t = rows[y]
    rr = runs_of(t, "M", 6)
    if colM is None:
        colM = [0] * W
    for p, q in rr:
        for x in range(p, q + 1):
            colM[x] += 1
colM = np.array(colM) if colM is not None else np.zeros(W)
sel = colM >= max(3, int(wH * 0.3))
# 连续段
segs = []
cur = None
for x in range(W):
    if sel[x]:
        if cur is None:
            cur = x
    else:
        if cur is not None:
            segs.append((cur, x - 1))
            cur = None
if cur is not None:
    segs.append((cur, W - 1))
print(f"  水带内木色柱（至少 {max(3, int(wH * 0.3))} 行命中）的列段:")
for p, q in segs:
    print(f"    x {p}..{q}  (w={q - p + 1})  center={(p + q) / 2:.1f}")

if len(segs) >= 2:
    c0 = (segs[0][0] + segs[0][1]) / 2.0
    c1 = (segs[-1][0] + segs[-1][1]) / 2.0
    pxtile_x = (c1 - c0) / 11.0
    print(f"\n  两桥心距 {c1 - c0:.1f} px ÷ 11 格 ⇒ 原版 {pxtile_x:.3f} px/格（横向）")
    print(f"  桥宽 = {(segs[0][1] - segs[0][0] + 1) / pxtile_x:.3f} 格（第 1 束）"
          f" / {(segs[-1][1] - segs[-1][0] + 1) / pxtile_x:.3f} 格（末束）")
    print(f"  桥心格 x = {c0 / pxtile_x:.2f} / {c1 / pxtile_x:.2f}（原版车道 3.5 / 14.5）")

    # 水带**下方**同列是否还有第二份木板
    below0, below1 = wBot + 1, min(H - 1, wBot + int(wH))
    print(f"\n  水带下方 rows {below0}..{below1} 内、各桥束列区间的木色命中行数"
          f"（判第二份垂进草地的木板是否存在）:")
    for p, q in segs:
        cnt = 0
        for y in range(below0, below1 + 1):
            t = rows[y, p:q + 1]
            if (t == "M").mean() > 0.5:
                cnt += 1
        print(f"    x {p}..{q}: {cnt}/{below1 - below0 + 1} 行过半是木色")
