# cr-d130-lane-xcal.py -- 量 frame_022 的两条车道中心列（横向定标 45.545 px/格 / 左沿 99 的依据复核）。
# 车道 = 草地里的土黄色路（R>G 且 R>B，且非台子/王台的深色）；在 BLUE 半场行带内逐列统计 "road-like" 像元数。
# 只读，不改任何产物。
from PIL import Image
import numpy as np

F22 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_022.png"
im = Image.open(F22).convert("RGBA")
a = np.array(im)
H, W = a.shape[:2]
print(f"frame_022 {W}x{H}")

def roadlike(band):
    # 土路：不透明、R>G+8、R>B+25（草地是 G>R，王台/公主台是灰白或深蓝）
    m = (band[:, :, 3] > 0) & (band[:, :, 0].astype(int) > band[:, :, 1].astype(int) + 8) \
        & (band[:, :, 0].astype(int) > band[:, :, 2].astype(int) + 25)
    return m

for (y0, y1, tag) in [(620, 900, "BLUE half-field rows 620..900"), (1150, 1560, "RED half-field rows 1150..1560")]:
    band = a[y0:y1]
    m = roadlike(band)
    cnt = m.sum(axis=0)
    xs = [x for x in range(W) if cnt[x] > 3]
    segs = []
    s = None
    for x in range(W):
        if cnt[x] > 3 and s is None:
            s = x
        elif cnt[x] <= 3 and s is not None:
            segs.append((s, x - 1, int(cnt[s:x].sum()))); s = None
    if s is not None:
        segs.append((s, W - 1, int(cnt[s:].sum())))
    # 只保留宽度 > 20 的段
    segs = [t for t in segs if t[1] - t[0] > 20]
    print(f"--- {tag}: road column segments (x0,x1,px) ---")
    for (x0, x1, n) in segs:
        print(f"    [{x0:4d},{x1:4d}] w={x1-x0+1:4d} px={n:6d} center={(x0+x1)/2:.1f}")
    if len(segs) >= 2:
        c = [(x0 + x1) / 2 for (x0, x1, _) in segs]
        print(f"    -> centers={['%.1f' % v for v in c]}  span={max(c)-min(c):.1f}  px/tile(11 tiles)={(max(c)-min(c))/11:.3f}")
