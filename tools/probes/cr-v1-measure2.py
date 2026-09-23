# CR-V1: 河 / 桥 / 地面 定量对比（原版 03 / 20 / 18 vs 我方实机图）
# 复跑: python .ai-tmp/test/cr-v1-measure2.py
import os, sys, hashlib
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"

IMGS = [
    ("原版03(1320)", os.path.join(ROOT, r"策划\参考图\03_对局_1320x2868.jpg"), (1100, 1700)),
    ("原版20(1080)", os.path.join(ROOT, r"策划\参考图\20_对局_1080x1920.jpg"), (700, 980)),
    ("原版18(1080)", os.path.join(ROOT, r"策划\参考图\18_对局HUD_1080x1920.jpg"), (760, 980)),
    ("我方CR-T6", os.path.join(ROOT, r".ai-tmp\screenshots\CR-T6-arena-nohud.png"), (800, 1060)),
]


def rpt(tag, path, ywin):
    # 纪律三（team-lead 2026-09-23）：本脚本的全部输出都是"数字"，而"0 / 空"是最容易伪装成正确的形态
    #   ⇒ 先 loud 地证明"我确实读到了"（字节数 + sha256_16），读不到就直接抛（不判定）。
    raw = open(path, "rb").read()
    print("\n" + "=" * 78)
    print("[read] %s bytes=%d sha256_16=%s" % (os.path.basename(path), len(raw),
                                              hashlib.sha256(raw).hexdigest()[:16].upper()))
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.int16)
    H, W = a.shape[:2]
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    print("%s  %dx%d  y 搜索窗 %s" % (tag, W, H, ywin))
    water = (b - r > 20) & (b > 90) & (g - r > 8)
    y0w, y1w = ywin
    sub = water[y0w:y1w]
    rows = sub.sum(axis=1)
    hit = np.where(rows > W * 0.25)[0]
    if not hit.size:
        # loud 且**不判定**（纪律三）：区分"图里真没有水"与"我的窗口/掩膜选错了"是本脚本做不到的 ⇒ 不许报 0。
        print("  !! 未找到水带（y 搜索窗 %s 内无任何一行满足 >%.0f%%W）—— 本项**不判定**，"
              "请先核对 y 搜索窗与掩膜，⛔ 不要把本项读成 0" % (ywin, 25.0))
        return
    # 取最厚连通行带
    runs, cur = [], None
    for i in range(sub.shape[0]):
        if rows[i] > W * 0.25:
            cur = [i, i] if cur is None else [cur[0], i]
        else:
            if cur:
                runs.append(tuple(cur))
            cur = None
    if cur:
        runs.append(tuple(cur))
    y0, y1 = max(runs, key=lambda t: t[1] - t[0])
    y0 += y0w
    y1 += y0w
    m = water[y0:y1 + 1]
    vs = a[y0:y1 + 1].reshape(-1, 3)[m.reshape(-1)]
    xs = np.where(m.any(axis=0))[0]
    print("  水带 y %d..%d 厚 %d (%.1f%%H)  x %d..%d 宽 %d (%.1f%%W)"
          % (y0, y1, y1 - y0 + 1, 100.0 * (y1 - y0 + 1) / H, xs.min(), xs.max(), xs.max() - xs.min() + 1,
             100.0 * (xs.max() - xs.min() + 1) / W))
    print("  均色 RGB%s  按行（每 1/8）:" % (tuple(int(vs[:, i].mean()) for i in range(3)),))
    for k in range(9):
        yy = y0 + (y1 - y0) * k // 8
        # ⛔ 掩膜必须与取色窗口**切同一个 x 区间**（原版 18 那张图的水带 x 只占 22..1057 ⇒ 掩膜是全宽、
        #    数据被切成 1036 宽 ⇒ 上一版在这里 IndexError 崩掉）。修法 = 先切掩膜再索引。
        seg = a[yy, xs.min():xs.max() + 1]
        mm = m[k * (y1 - y0) // 8, xs.min():xs.max() + 1]
        v = seg[mm]
        print("     y=%4d (%.2f) 水均色 RGB%s" % (yy, k / 8.0, tuple(int(x) for x in v.mean(axis=0)) if len(v) else "-"))

    # 车道（金/土黄）列带：水带下方 200px
    gl = ((r > 200) & (g > 150) & (b < 170)) | ((r > 150) & (g > 120) & (b < 130) & (r - b > 50))
    lo, hi = min(y1 + 40, H - 1), min(y1 + 260, H)
    prof = gl[lo:hi].sum(axis=0)
    runs2, c = [], None
    for x in range(W):
        if prof[x] > (hi - lo) * 0.4:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 20:
                runs2.append((c[0], c[1], c[1] - c[0] + 1))
            c = None
    if c and c[1] - c[0] > 20:
        runs2.append((c[0], c[1], c[1] - c[0] + 1))
    print("  车道列带(y%d..%d) = %s" % (lo, hi, runs2))
    lane = (runs2[0][0] + runs2[0][1]) // 2 if runs2 else W // 2
    mid = W // 2
    for nm, x in (("车道列", lane), ("中列", mid)):
        print("  [%s x=%d] 纵向取色（水带上下各 36px）:" % (nm, x))
        out = []
        for yy in range(y0 - 36, y1 + 40, 6):
            if 0 <= yy < H:
                out.append("y%d=%s" % (yy, tuple(int(v) for v in a[yy, x])))
        print("     " + "  ".join(out))
    # 地面上/下取色
    for nm, yy in (("水上草(30px)", y0 - 30), ("水下草(30px)", y1 + 30), ("水上草(120px)", y0 - 120),
                   ("水下草(120px)", y1 + 120)):
        if 0 <= yy < H:
            print("  %-14s y=%4d 行均色 RGB%s" % (nm, yy, tuple(int(a[yy, :, i].mean()) for i in range(3))))


for tag, p, yw in IMGS:
    if os.path.isfile(p):
        rpt(tag, p, yw)
    else:
        print("MISSING", p)
