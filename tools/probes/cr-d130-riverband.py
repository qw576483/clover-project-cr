# cr-d130-riverband.py -- 判定 frame_022 两块实心区「靠河那一端」各自的横带结构。
# 目的：① 到底是「素材只画了一侧」（素材缺陷）还是「我把裁剪窗口选反了」。
# 做法：在场地横窗口 px 99..918 内逐行分类（T/G 草/M 泥/石 S/木 D/水 W/O），只打印 run>=3 的段。
# 只读，不改任何产品文件。
from PIL import Image
import numpy as np

F22 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_022.png"
a = np.array(Image.open(F22).convert("RGBA"))
H, W = a.shape[:2]
X0, X1 = 99, 918
print(f"frame_022 {W}x{H}  window_x=[{X0},{X1}]  (row px)")


def classify(row):
    m = row[:, 3] > 0
    n = int(m.sum())
    if n == 0:
        return "T", None, 0
    r, g, b = (int(row[m][:, 0].mean()), int(row[m][:, 1].mean()), int(row[m][:, 2].mean()))
    mx, mn = max(r, g, b), min(r, g, b)
    sat = mx - mn
    if b > r + 18 and b > g + 2:
        c = "W"
    elif g > r + 8 and g > b + 20:
        c = "G"
    elif r > g + 6 and r > b + 16:
        # 暖色：按亮度分「木栏（暗）」「泥（中）」
        c = "D" if mx < 150 else "M"
    elif sat <= 18:
        c = "S"
    else:
        c = "O"
    return c, (r, g, b), n


runs = []
prev = None
for y in range(H):
    c, col, n = classify(a[y, X0:X1])
    key = (c, col)
    if key != prev:
        runs.append([y, y, c, col, n])
        prev = key
    else:
        runs[-1][1] = y

print("--- runs (only len>=3, plus all T-runs) ---")
for y0, y1, c, col, n in runs:
    ln = y1 - y0 + 1
    if ln >= 3 or c == "T":
        print("  y=%4d..%4d  len=%4d  %s  rgb=%s" % (y0, y1, ln, c, col))

print()
print("--- 河岸敏感窗口细看（每行 n + 颜色） ---")
for lo, hi, tag in [(520, 660, "BLUE 侧靠河 536..606 附近"),
                    (1040, 1180, "RED 块首段 1083.."),
                    (1460, 1620, "RED 块末段 ..1600")]:
    print(f"[{tag}]")
    for y in range(lo, hi):
        c, col, n = classify(a[y, X0:X1])
        print("   y=%4d n=%4d %s rgb=%s" % (y, n, c, col))
