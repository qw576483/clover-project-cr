# cr-d130-water-xcal.py -- 量 frame_006 水带的行/列结构，回答一个问题：
#   frame 6 的「场地左沿」到底在哪一列（= 水面裁条左沿 WaterPxLeft 该取多少）？
# 判据：水带内每一列的像元数 + 均色；车道中心列 = 水带上「均色突变」处（桥板/水色边界）。
# 只读，不改任何产物。输出到 stdout。
import sys
from PIL import Image
import numpy as np

F6 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_006.png"
im = Image.open(F6).convert("RGBA")
a = np.array(im)
H, W = a.shape[:2]
print(f"frame_006 {W}x{H}")

# 1) 找水带的行区间（蓝色行 = B>G 且 像素数 > 100）
def is_water_row(y):
    row = a[y]
    m = (row[:, 3] > 0) & (row[:, 2].astype(int) > row[:, 1].astype(int) + 20)
    return int(m.sum())

ws = [y for y in range(H) if is_water_row(y) > 100]
print("water rows:", (min(ws), max(ws)) if ws else None, "count", len(ws))

ytop, ybot = (min(ws), max(ws)) if ws else (806, 853)
# 2) 行剖面：每 8 行打印一行的均色（仅不透明像元）
print("--- row profile (opaque-only mean RGB, x window 60..1040) ---")
for y in range(ytop, ybot + 1, 6):
    row = a[y, 60:1040]
    m = row[:, 3] > 0
    if m.sum() == 0:
        print(f"  y={y} opaque=0")
        continue
    print(f"  y={y} opaque={int(m.sum()):4d} bbox_x=[{60+int(np.argmax(m))},{60+len(m)-1-int(np.argmax(m[::-1]))}] mean=({int(row[m][:,0].mean())},{int(row[m][:,1].mean())},{int(row[m][:,2].mean())})")

# 3) 列剖面：水带中段（避开上下渐变边），逐列像元数 + 均色分组
ymid0, ymid1 = ytop + 12, ybot - 12
band = a[ymid0:ymid1]
opq = (band[:, :, 3] > 0).sum(axis=0)
xs = [x for x in range(W) if opq[x] > 0]
print(f"--- column profile rows {ymid0}..{ymid1}: opaque x-range = [{min(xs)},{max(xs)}] ---")
# 打印不透明列的连续段
segs = []
s = None
for x in range(W):
    if opq[x] > 0 and s is None:
        s = x
    elif opq[x] == 0 and s is not None:
        segs.append((s, x - 1)); s = None
if s is not None:
    segs.append((s, W - 1))
print("  opaque column segments:", segs)
# 4) 在列段内按均色识别「车道/桥板」列（与纯水色不同）
print("--- per-40px color buckets inside band ---")
for x0 in range(0, W, 40):
    x1 = min(x0 + 40, W)
    sub = band[:, x0:x1]
    m = sub[:, :, 3] > 0
    if m.sum() < 10:
        print(f"  x{x0:4d}..{x1:4d}  (empty)")
        continue
    print(f"  x{x0:4d}..{x1:4d} n={int(m.sum()):5d} mean=({int(sub[m][:,0].mean()):3d},{int(sub[m][:,1].mean()):3d},{int(sub[m][:,2].mean()):3d})")
