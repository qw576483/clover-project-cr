# CR-V1: grass-checkerboard square size, with a KNOWN-GOOD reverse calibration.
#   (team-lead approved this approach 2026-09-23: "用已知好样本校准量法")
#
# Estimator = TWO-TONE RUN LENGTH (robust for a checkerboard; the earlier autocorrelation version
# picked up fine grass mottling instead and was rejected by its own known-good gate - see
# CR-V1-checker2-out.txt history):
#   1. take a long CLEAN grass row (no lane / plaza / unit),
#   2. box-smooth with a kernel whose width scales with ppt (so the estimator is scale-consistent),
#   3. classify each pixel light/dark against the row median -> the pattern alternates every square,
#   4. the median run length (of runs >= kernel) = ONE CHECKER SQUARE, reported in tiles.
#
# Known-good chain: the on-device ground is f022's crop x99..912 / y646..1642 rendered at
# localScale (2.214, 1.506) (L3 ARTNODE GroundNear in CR-V1-evidence.txt). We BUILD that image and
# require  square_synth == square_art * 2.214  within 8%. Only then is the real shot measured.
#
# Re-run: python tools/probes/cr-v1-checker2.py
import os, sys, hashlib
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
F022 = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc",
                    "arena_training_out", "arena_training_sprite_22.png")
REF = os.path.join(ROOT, r"策划\参考图\03_对局_1320x2868.jpg")
OUR = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-V1-arena-nohud.png")
SYN = os.path.join(ROOT, ".ai-tmp", "test", "CR-V1-synth-ground.png")

CROP = (99, 646, 912, 1642)
SCALE = (2.214, 1.506)
ART_PPT, REF_PPT, OUR_PPT = 45.2, 91.5, 60.545


def read_proof(p):
    raw = open(p, "rb").read()
    print("[read] %-30s bytes=%-9d sha256_16=%s" % (os.path.basename(p), len(raw),
          hashlib.sha256(raw).hexdigest()[:16].upper()))
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)


def square_tiles(a, y, x0, x1, ppt):
    prof = a[y, x0:x1, 1]
    k = max(3, int(round(ppt / 6.0)))
    sm = np.convolve(prof, np.ones(k) / k, mode="same")
    st = sm > np.median(sm)
    if st.all() or (~st).all():
        return None
    runs, cur = [], 1
    for i in range(1, len(st)):
        if st[i] == st[i - 1]:
            cur += 1
        else:
            runs.append(cur)
            cur = 1
    runs.append(cur)
    runs = [r for r in runs if r >= k]
    if len(runs) < 2:
        return None
    return float(np.median(runs)) / ppt, len(runs)


def scan(tag, a, rows, x0, x1, ppt):
    print("\n-- %s  rows=%s  x=%d..%d  ppt=%.2f px/tile" % (tag, list(rows)[:2], x0, x1, ppt))
    out = []
    for y in rows:
        r = square_tiles(a, y, x0, x1, ppt)
        if r is None:
            print("   y=%4d  !! no two-tone alternation -> NOT JUDGED" % y)
            continue
        tiles, n = r
        print("   y=%4d  square=%5.2f tiles  (runs=%d)" % (y, tiles, n))
        out.append(tiles)
    if out:
        m = float(np.median(out))
        print("   median = %.2f tiles  (n=%d/%d)" % (m, len(out), len(list(rows))))
    else:
        print("   NO reliable row -> NOT JUDGED")
    return out


A = read_proof(F022)
art = scan("f022 ART (between lanes)", A, range(1000, 1120, 12), 300, 700, ART_PPT)

crop = Image.open(F022).convert("RGB").crop(CROP)
syn = crop.resize((int(round(crop.size[0] * SCALE[0])), int(round(crop.size[1] * SCALE[1]))), Image.LANCZOS)
os.makedirs(os.path.dirname(SYN), exist_ok=True)
syn.save(SYN)
print("\n[write] synthetic known-good ground -> %s %s" % (os.path.basename(SYN), syn.size))
S = np.asarray(syn).astype(np.float64)
syn_rows = [int(round((y - CROP[1]) * SCALE[1])) for y in range(1000, 1120, 12)]
synp = scan("SYNTHETIC = f022 x 2.214 (known-good)", S, syn_rows,
            int(round((300 - CROP[0]) * SCALE[0])), int(round((700 - CROP[0]) * SCALE[0])),
            ART_PPT * SCALE[0])

ok = None
if art and synp:
    ma, ms = float(np.median(art)), float(np.median(synp))
    # !! CORRECTION (CR-V1 self-fix): the measure is expressed in TILES, and a pure rescale scales the
    # pixels AND the tiles-per-pixel by the same factor -> the tile measure is SCALE-INVARIANT.
    # So the correct known-good assertion is  synthetic_tiles == art_tiles  (NOT art_tiles * 2.214;
    # multiplying was my own gate bug - it compared a scale-invariant quantity against a scaled one).
    exp = ma
    ok = abs(ms - exp) / exp
    print("\nKNOWN-GOOD CHECK (scale-invariance): art %.3f tiles -> synthetic measured %.3f tiles, "
          "err=%.1f%% => %s" % (ma, ms, ok * 100.0, "PASS" if ok < 0.08 else "FAIL"))
if ok is None or ok >= 0.08:
    print("!! NOT calibrated -> refusing to quote a square size for the real shot (no silent wrong number)")
    sys.exit(0)

O = read_proof(OUR)
ours = scan("OURS CR-V1-arena-nohud (enemy half, between lanes)", O, range(300, 620, 20), 250, 830, OUR_PPT)
R = read_proof(REF)
ref = scan("ref03 ORIGINAL (own half, between lanes)", R, range(1700, 2300, 24), 60, 500, REF_PPT)

print("\n===== RESULT (checker square, in arena tiles) =====")
if ours:
    print("  OURS  : %.2f tiles (median, n=%d, ppt %.2f)" % (float(np.median(ours)), len(ours), OUR_PPT))
if ref:
    print("  ref03 : %.2f tiles (median, n=%d, ppt %.2f)" % (float(np.median(ref)), len(ref), REF_PPT))
if ours and ref:
    mo, mr = float(np.median(ours)), float(np.median(ref))
    print("  ratio ours/ref03 = %.2fx  (1.00 = identical)" % (mo / mr))
print("  analytic (art x scale): %.2f tiles" % (float(np.median(art)) * SCALE[0]))
