# CR-V1: measure the grass checkerboard period (in arena tiles) for the original ref03 vs ours.
# Method: along a grass row (no lane, no unit), take the green-channel profile, count sign changes of
# its first difference above a threshold -> period = row_length / (edges/2). Reported in TILES.
# Re-run: python tools/probes/cr-v1-checker.py
import os, sys, hashlib
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
REF = os.path.join(ROOT, r"策划\参考图\03_对局_1320x2868.jpg")
OUR = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-V1-arena-nohud.png")


def period_in_tiles(path, y_from, y_to, x_from, x_to, ppt):
    raw = open(path, "rb").read()
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    print("[read] %-26s bytes=%-9d sha256_16=%s  ppt=%.2f px/tile" %
          (os.path.basename(path), len(raw), hashlib.sha256(raw).hexdigest()[:16].upper(), ppt))
    H, W = a.shape[:2]
    for y in range(y_from, y_to, max(1, (y_to - y_from) // 6)):
        prof = a[y, x_from:x_to, 1] * 1.0 - a[y, x_from:x_to, 1].mean()
        d = np.diff(prof)
        thr = max(6.0, float(np.std(d)) * 1.5)
        edges = np.where(np.abs(d) > thr)[0]
        # keep well-separated edges
        keep, last = [], -99
        for e in edges:
            if e - last > 8:
                keep.append(e)
                last = e
        if len(keep) >= 4:
            per_px = (x_to - x_from) / (len(keep) / 2.0)
            print("   y=%4d  edges=%-3d  周期=%.1f px = %.2f 格" % (y, len(keep), per_px, per_px / ppt))
        else:
            print("   y=%4d  edges=%-3d  !! 不足 -> 本行不判定" % (y, len(keep)))


print("== ORIGINAL ref03 (ppt = 91.5, documented cr-t1i-fit.py) ==")
period_in_tiles(REF, 1700, 2400, 60, 1260, 91.5)
print("\n== OURS CR-V1-arena-nohud.png (ppt = 60.545, measured cr-v1-cmp.py) ==")
period_in_tiles(OUR, 1100, 1750, 120, 960, 60.545)
