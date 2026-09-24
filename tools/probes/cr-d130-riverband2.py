# cr-d130-riverband2.py -- frame_022 逐行「分类计数」（不是均色），看横带边界。
# 判据：某行的 泥(M)+灰石(S) 计数、木(D) 计数、水(W) 计数 从 0 变正 / 从正变 0 = 横带边界。
# 只读。
from PIL import Image
import numpy as np

F22 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_022.png"
a = np.array(Image.open(F22).convert("RGBA")).astype(np.int16)
H, W = a.shape[:2]
X0, X1 = 99, 918
sub = a[:, X0:X1, :]
R, G, B, A = sub[:, :, 0], sub[:, :, 1], sub[:, :, 2], sub[:, :, 3]
op = A > 0
mx = np.maximum(np.maximum(R, G), B)
mn = np.minimum(np.minimum(R, G), B)
sat = mx - mn
water = op & (B > R + 18) & (B > G + 2)
grass = op & (G > R + 8) & (G > B + 20)
warm = op & (R > G + 6) & (R > B + 16)
wood = warm & (mx < 150)
mud = warm & (mx >= 150)
stone = op & (sat <= 18)


def cnt(m):
    return int(m.sum())


print(f"frame_022 {W}x{H}  window_x=[{X0},{X1}]")
print(" y0   y1  len |   mud  stone  wood  water  grass   op")
prev = None
seg = None
rows = []
for y in range(H):
    v = (cnt(mud[y]), cnt(stone[y]), cnt(wood[y]), cnt(water[y]), cnt(grass[y]), cnt(op[y]))
    rows.append((y, v))

# 只打印「mud+stone+wood > 12」或 water>200 的行区间（横带才有这种量级）
KEY = lambda v: v[0] + v[1] + v[2] > 12 or v[3] > 200
cur = None
for y, v in rows:
    k = KEY(v)
    if k and cur is None:
        cur = [y, y]
    elif k:
        cur[1] = y
    elif cur is not None:
        y0, y1 = cur
        mid = rows[(y0 + y1) // 2][1]
        print(" %4d %4d %4d | %5d %6d %5d %6d %6d %5d" % (y0, y1, y1 - y0 + 1, *mid))
        cur = None

print()
print("--- 逐行全量（水面带 490..700 与红块靠河端 1450..1660） ---")
for lo, hi in [(490, 700), (1450, 1660)]:
    print(f"[{lo}..{hi}]")
    for y in range(lo, min(hi, H)):
        m, s, w, wa, g, o = rows[y][1]
        if o == 0:
            print("   y=%4d  (transparent)" % y)
        else:
            print("   y=%4d mud=%4d stone=%4d wood=%4d water=%4d grass=%4d op=%4d" % (y, m, s, w, wa, g, o))
