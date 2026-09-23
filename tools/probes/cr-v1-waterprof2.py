# CR-V1: river BANK/WATER structure profile - upgrades D-V1-2 from "mean colour differs by DR=47"
# to a STRUCTURAL difference (does the original have a brown bank? a light-to-dark gradient? does ours?).
#
# Method: for each row across the river band, take the MEDIAN RGB over the full image width (median is
# robust to the two bridges and to units). Then classify each row as bank / water / grass by hue and
# print the run structure in tiles.
#
# Re-run: python tools/probes/cr-v1-waterprof2.py
import os, sys, hashlib
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
REF = os.path.join(ROOT, r"策划\参考图\03_对局_1320x2868.jpg")
OUR = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-V1-arena-nohud.png")
REF_PPT, OUR_PPT = 91.5, 60.545


def read_proof(p):
    raw = open(p, "rb").read()
    print("[read] %-30s bytes=%-9d sha256_16=%s" % (os.path.basename(p), len(raw),
          hashlib.sha256(raw).hexdigest()[:16].upper()))
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)


def klass(c):
    r, g, b = c
    if g - r > 20 and g - b > 40:
        return "grass"
    if r - b > 35 and r - g > 10 and r < 215:
        return "BANK(brown)"
    if b - r > 25 and b > 90:
        return "water"
    if r - b > 35 and g - b > 30:
        return "bank/tan"
    return "other"


def profile(tag, a, y0, y1, ppt, step):
    print("\n-- %s  rows %d..%d  (median over full width)  ppt=%.2f px/tile" % (tag, y0, y1, ppt))
    rows = list(range(y0, y1, step))
    seq = []
    for y in rows:
        med = np.median(a[y], axis=0)
        c = tuple(int(v) for v in med)
        k = klass(c)
        seq.append((y, k, c))
        print("   y=%4d  RGB%-16s %s" % (y, c, k))
    # compress into runs
    print("   -- run structure (in tiles) --")
    runs, cur = [], None
    for y, k, c in seq:
        if cur and cur[1] == k:
            cur[2] = y
        else:
            if cur:
                runs.append(cur)
            cur = [y, k, y]
    if cur:
        runs.append(cur)
    for y0r, k, y1r in runs:
        tile_a = (y0r - seq[0][0]) / ppt
        tile_b = (y1r - seq[0][0]) / ppt
        print("     %-11s y%4d..%-4d  = %.2f..%.2f tiles below band top  (%.2f tiles thick)"
              % (k, y0r, y1r, tile_a, tile_b, (y1r - y0r) / ppt))
    return seq


A = read_proof(REF)
profile("ref03 ORIGINAL river+banks", A, 1300, 1460, REF_PPT, 4)
O = read_proof(OUR)
profile("OURS CR-V1 river+banks", O, 860, 1040, OUR_PPT, 4)
