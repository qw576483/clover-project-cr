# cr-d130-water-top.py -- 复核 frame_006 水带的上沿到底在哪一行（806 还是 815？）。
# 逐行打印 rows 795..860 在 x=300..950 内的不透明像元数 + 均色。只读。
from PIL import Image
import numpy as np

F6 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_006.png"
a = np.array(Image.open(F6).convert("RGBA"))

for y in range(795, 862):
    row = a[y, 300:950]
    m = row[:, 3] > 0
    if m.sum() == 0:
        print(f"y={y} opaque=0")
    else:
        print(f"y={y} opaque={int(m.sum()):4d} mean=({int(row[m][:,0].mean()):3d},{int(row[m][:,1].mean()):3d},{int(row[m][:,2].mean()):3d})")
