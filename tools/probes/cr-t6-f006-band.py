# CR-T6: 量 f006 的「水带内车道跨越处（=桥）」与「草地上车道」的 x 位置，判定二者是否同一套横向换算
# 复跑：python .ai-tmp/test/t6-f006-band.py
import os
import numpy as np
from PIL import Image

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
F6 = os.path.join(ROOT, r"client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_006.png")
F22 = os.path.join(ROOT, r"client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_022.png")

a = np.asarray(Image.open(F6).convert("RGB")).astype(np.int16)


def tan_cols(a, y0, y1, thr=0.5, need=150):
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    tan = (r >= g - 6) & (r > b + 30) & (r > need)
    prof = tan[y0:y1].mean(axis=0)
    runs, cur = [], None
    for x in range(a.shape[1]):
        if prof[x] > thr:
            cur = [x, x] if cur is None else [cur[0], x]
        else:
            if cur and cur[1] - cur[0] > 4:
                runs.append((cur[0], cur[1]))
            cur = None
    if cur and cur[1] - cur[0] > 4:
        runs.append((cur[0], cur[1]))
    return runs


print("f006 草地上车道列（py 950..1050 之间草地区）：", tan_cols(a, 950, 1040))
print("f006 水带内跨越处列（py 806..851）：        ", tan_cols(a, 806, 851, thr=0.35))
print("f006 水带下方车道列（py 860..940）：        ", tan_cols(a, 860, 940))
print("f006 场地上车道列（py 1100..1200）：        ", tan_cols(a, 1100, 1200))
print()
r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
blue = (b > r + 20) & (b > g + 5)
ys = np.where(blue.any(axis=1))[0]
xs = np.where(blue.any(axis=0))[0]
print(f"f006 蓝水 bbox x {xs.min()}..{xs.max()} y {ys.min()}..{ys.max()}")
# 水带逐列是否全水
colblue = blue[ys.min():ys.max() + 1].mean(axis=0)
runs, cur = [], None
for x in range(a.shape[1]):
    if colblue[x] > 0.8:
        cur = [x, x] if cur is None else [cur[0], x]
    else:
        if cur and cur[1] - cur[0] > 3:
            runs.append((cur[0], cur[1]))
        cur = None
if cur and cur[1] - cur[0] > 3:
    runs.append((cur[0], cur[1]))
print("f006 水带里「整列都是水」的列带 =", runs)
print("   ⇒ 被车道切开的位置 = 相邻两个列带之间的缺口")

print()
print("=== f022（上一版河道来源）：水面 bbox 与泥土带 ===")
c = np.asarray(Image.open(F22).convert("RGB")).astype(np.int16)
r2, g2, b2 = c[:, :, 0], c[:, :, 1], c[:, :, 2]
bl2 = (b2 > r2 + 20) & (b2 > g2 + 5)
ys2 = np.where(bl2.any(axis=1))[0]
xs2 = np.where(bl2.any(axis=0))[0]
print(f"f022 蓝水 bbox x {xs2.min()}..{xs2.max()} y {ys2.min()}..{ys2.max()}  px={int(bl2.sum())}")
old = c[577:606, 106:912]
print("f022 上一版窗口 py577..606 x106..912 均色 =", tuple(int(old.reshape(-1, 3)[:, i].mean()) for i in range(3)))
new = c[536:583, 106:912]
print("f022 真水面窗口 py536..583 x106..912 均色 =", tuple(int(new.reshape(-1, 3)[:, i].mean()) for i in range(3)))
print("f022 py577..606 里土黄列（=上一版被当成桥的木板）:", tan_cols(c, 587, 682, thr=0.35))
print("f022 水带里整列是水的列带:", end=" ")
cb = bl2[ys2.min():ys2.max() + 1].mean(axis=0)
runs, cur = [], None
for x in range(c.shape[1]):
    if cb[x] > 0.8:
        cur = [x, x] if cur is None else [cur[0], x]
    else:
        if cur and cur[1] - cur[0] > 3:
            runs.append((cur[0], cur[1]))
        cur = None
if cur and cur[1] - cur[0] > 3:
    runs.append((cur[0], cur[1]))
print(runs)
