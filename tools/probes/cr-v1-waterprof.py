# CR-V1: f006 水带逐行剖面 + f022 水带逐行剖面 + tex_ 右侧水条剖面 + 我方现渲染水色
# 复跑: python .ai-tmp/test/cr-v1-waterprof.py
import os, sys
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
D = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc")


def prof(tag, path, x0, x1, y0, y1, step=3):
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.int16)
    print("\n=== %s  x %d..%d  y %d..%d ===" % (tag, x0, x1, y0, y1))
    for y in range(y0, y1 + 1, step):
        row = a[y, x0:x1 + 1]
        med = tuple(int(np.median(row[:, i])) for i in range(3))
        p10 = tuple(int(np.percentile(row[:, i], 10)) for i in range(3))
        p90 = tuple(int(np.percentile(row[:, i], 90)) for i in range(3))
        print("  y=%4d  中位%s  10%%%s  90%%%s" % (y, med, p10, p90))


prof("f006 水带(全场宽)", os.path.join(D, "arena_training_out", "arena_training_sprite_06.png"), 200, 1000, 795, 862, 3)
prof("f022 水带(场地内 x500..900)", os.path.join(D, "arena_training_out", "arena_training_sprite_22.png"), 500, 900, 520, 620, 4)
prof("tex_ 右侧水条(x802..847)", os.path.join(D, "arena_training_tex_.png"), 802, 847, 100, 160, 4)

# 我方现渲染（CR-T6 图）水带剖面
p = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T6-arena-nohud.png")
if os.path.isfile(p):
    prof("我方 CR-T6 水带", p, 300, 800, 850, 1000, 6)
