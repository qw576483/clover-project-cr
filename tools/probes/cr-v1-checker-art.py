# CR-V1: measure the grass checkerboard period in the ORIGINAL ART (f022) and convert it to on-screen
# tiles at our render scale. Deterministic: no HUD, no units in the source texture.
# f022 calibration (cr-v1-calib-tex_.py): field x 99..912 = 18 tiles -> 45.2 art px/tile (x);
# on-device render scale x = 2.214 (L3 ARTNODE GroundNear localScale) -> 60.545 px/tile measured.
# Re-run: python tools/probes/cr-v1-checker-art.py
import os, sys, hashlib
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
P = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc",
                 "arena_training_out", "arena_training_sprite_22.png")
ART_PPT_X = 45.2
RENDER_SCALE_X = 2.214
OUR_PPT = 60.545

raw = open(P, "rb").read()
a = np.asarray(Image.open(P).convert("RGB")).astype(np.float32)
print("[read] arena_training_sprite_22.png bytes=%d sha256_16=%s"
      % (len(raw), hashlib.sha256(raw).hexdigest()[:16].upper()))

x0, x1 = 110, 900
res = []
for y in range(1460, 1640, 20):
    row = a[y, x0:x1, 1]
    d = np.diff(row)
    thr = max(4.0, float(np.std(d)) * 1.5)
    e = np.where(np.abs(d) > thr)[0]
    keep, last = [], -99
    for x in e:
        if x - last > 4:
            keep.append(x)
            last = x
    if len(keep) >= 8:
        per = (x1 - x0) / (len(keep) / 2.0)
        scr = per * RENDER_SCALE_X
        res.append(per)
        print("  y=%4d edges=%-3d period=%.1f art_px = %.2f tiles(art) -> %.1f render px = %.2f tiles on screen"
              % (y, len(keep), per, per / ART_PPT_X, scr, scr / OUR_PPT))
    else:
        print("  y=%4d edges=%-3d  !! too few -> NOT JUDGED" % (y, len(keep)))

if res:
    m = float(np.median(res))
    print("\nmedian art period = %.1f px = %.2f tiles(art)  ->  ON SCREEN %.2f tiles"
          % (m, m / ART_PPT_X, m * RENDER_SCALE_X / OUR_PPT))
else:
    print("\nNO reliable row -> NOT JUDGED")
print("reference: original ref03 measured ~1.0-1.2 tiles (cr-v1-checker.py multi-row median)")
