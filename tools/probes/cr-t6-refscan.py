# CR-T6: 参考图 20 的逐区颜色/边界量取（原版值出处）
# 复跑：python .ai-tmp/test/t6-refscan.py
import os
import numpy as np
from PIL import Image

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
REF = os.path.join(ROOT, r"策划\参考图\20_对局_1080x1920.jpg")
OURS = os.path.join(ROOT, r".ai-tmp\screenshots\CR-T1-towers-nohud.png")


def A(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)


def colprof(a, name, x0, x1, y0, y1):
    sub = a[y0:y1, x0:x1]
    med = np.median(sub.reshape(-1, 3), axis=0).astype(int)
    print(f"[{name}] x{x0}..{x1} y{y0}..{y1} 中位色 = {tuple(med)}  均色 = {tuple(sub.reshape(-1,3).mean(axis=0).astype(int))}")


def rowscan(a, name, y, x0, x1, step):
    print(f"[{name}] y={y}:")
    out = []
    for x in range(x0, x1, step):
        px = a[y, x]
        out.append(f"{x}({px[0]},{px[1]},{px[2]})")
    print("   " + " ".join(out))


def colscan(a, name, x, y0, y1, step):
    print(f"[{name}] x={x}:")
    out = []
    for y in range(y0, y1, step):
        px = a[y, x]
        out.append(f"{y}({px[0]},{px[1]},{px[2]})")
    print("   " + " ".join(out))


def edge_scan(a, name, y, x0, x1):
    """在给定行上找「显著变色」的 x 边界（用于找路径/桥的左右沿）"""
    row = a[y, x0:x1].astype(int)
    d = np.abs(np.diff(row, axis=0)).sum(axis=1)
    idx = np.where(d > 60)[0]
    print(f"[{name}] y={y} 强边界 x = {[int(x0+i+1) for i in idx][:40]}")


ref = A(REF)
our = A(OURS)
print("REF", ref.shape, "OURS", our.shape)

print("\n===== 参考图：草地质感（棋盘格）=====")
colprof(ref, "ref grass 亮格", 300, 360, 200, 260)
colprof(ref, "ref grass 暗格", 360, 420, 200, 260)
rowscan(ref, "ref 草地棋盘行扫描", 250, 200, 560, 20)

print("\n===== 参考图：河道（水面 + 岸）=====")
colscan(ref, "ref 竖扫河道(左桥右侧 x=700)", 700, 790, 940, 5)
colscan(ref, "ref 竖扫河道(x=380)", 380, 790, 940, 5)
colprof(ref, "ref 水面", 600, 700, 832, 856)
colprof(ref, "ref 岸上石墙", 600, 700, 790, 830)
colprof(ref, "ref 岸下缘", 600, 700, 858, 890)

print("\n===== 参考图：铺装路（竖向）=====")
edge_scan(ref, "ref y=1300 边界", 1300, 0, 1080)
colprof(ref, "ref 左路", 220, 260, 1250, 1350)
colprof(ref, "ref 右路", 830, 870, 1250, 1350)

print("\n===== 参考图：场地左右边沿 =====")
rowscan(ref, "ref y=400 左沿", 400, 0, 80, 8)
rowscan(ref, "ref y=400 右沿", 400, 1000, 1080, 8)

print("\n===== 我方：河道 =====")
colscan(our, "our 竖扫河道(x=700)", 700, 820, 1060, 5)
colprof(our, "our 河带(棕)", 500, 620, 940, 1000)
colprof(our, "our 青色线", 300, 400, 884, 898)
colscan(our, "our 竖扫河道(x=380)", 380, 820, 1060, 5)

print("\n===== 我方：草地/铺装 =====")
colprof(our, "our 草地亮格", 300, 360, 200, 260)
colprof(our, "our 草地暗格", 360, 420, 200, 260)
edge_scan(our, "our y=1300 边界", 1300, 0, 1080)
colprof(our, "our 左路", 220, 260, 1250, 1350)
