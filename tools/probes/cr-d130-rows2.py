# cr-d130-rows2.py -- 精确复核 frame_022 在「场地横窗口 px 99..918」内逐行的不透明像元数 + 均色。
# 目的：确认 997..1075 到底是不是「只剩左沿草须」、以及块内容的真实末行（1600 还是 1634）与首行（1083）。
# 只读。
from PIL import Image
import numpy as np

F22 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_022.png"
a = np.array(Image.open(F22).convert("RGBA"))
H, W = a.shape[:2]
X0, X1 = 99, 918
print(f"frame_022 {W}x{H}  window_x=[{X0},{X1}]")

prev = None
for y in range(H):
    row = a[y, X0:X1]
    m = row[:, 3] > 0
    n = int(m.sum())
    if n == 0:
        cls = "T"
        col = None
    else:
        r, g, b = (int(row[m][:, 0].mean()), int(row[m][:, 1].mean()), int(row[m][:, 2].mean()))
        if g > r + 8 and g > b + 20:
            cls = "G"
        elif r > g + 8 and r > b + 20:
            cls = "M"
        elif b > r + 20 and b > g + 5:
            cls = "W"
        else:
            cls = "O"
        col = (r, g, b)
    key = (cls, col)
    if key != prev:
        print("  y=%4d  n=%4d  %s  rgb=%s" % (y, n, cls, col))
        prev = key
