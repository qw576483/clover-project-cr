# cr-d130-lane-rowscan.py -- 在 frame_022 的若干行上打印「颜色分段」，用于人工读出车道中心列。
# 只读。
from PIL import Image
import numpy as np

F22 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_022.png"
im = Image.open(F22).convert("RGBA")
a = np.array(im)

def tag(px):
    r, g, b, al = int(px[0]), int(px[1]), int(px[2]), int(px[3])
    if al < 16: return "T"
    if g > r + 10 and g > b + 10: return "G"          # grass
    if r > g + 8 and r > b + 25: return "R"           # road/dirt (tan)
    if b > r + 20 and b > g + 5: return "W"           # water/blue
    if abs(r - g) < 14 and abs(g - b) < 14: return "S" # stone/grey/white
    return "?"

for y in [640, 700, 760, 880, 960, 1180, 1260, 1400, 1500]:
    row = a[y]
    segs = []
    cur = None; s = 0
    for x in range(row.shape[0]):
        t = tag(row[x])
        if t != cur:
            if cur is not None:
                segs.append((cur, s, x - 1))
            cur = t; s = x
    segs.append((cur, s, row.shape[0] - 1))
    # 只打印宽度 >= 8 的段
    segs = [t for t in segs if t[2] - t[1] + 1 >= 8]
    print(f"y={y}: " + " ".join(f"{t}[{x0}-{x1}]c{(x0+x1)/2:.0f}" for (t, x0, x1) in segs))
