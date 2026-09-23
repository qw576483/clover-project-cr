# CR-V1: locate the ORIGINAL asset that carries the reference frame's water colour, and check whether
# that candidate is UNIQUE.  Read-only.  Criterion pair from team-lead (2026-09-23):
#   (1) the candidate must be unique, (2) the original must actually use it.
#
# Reference water measured on 策划/参考图/20_对局_1080x1920.jpg  (row mean over x430..700, band
# y865..906) = RGB(132,191,202) -- a LIGHT desaturated cyan.  Our training-arena water is
# RGB(0,155,188) (fully saturated).  Known gap, already registered in ArenaView.cs L166-170.
#
#   python tools/probes/cr-v1-water-source.py
import os, sys, glob
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc")
TRAIN = os.path.join(SC, "arena_training_out")
REF_WATER = (132, 191, 202)


def is_light_cyan(p):
    r, g, b = p[:3]
    return g > r + 35 and b > r + 40 and b > 165 and r > 95


def bands_in(img):
    """contiguous row bands whose opaque part averages to a light cyan."""
    w, h = img.size
    px = img.load()
    ys = []
    for y in range(h):
        acc = [0, 0, 0]; n = 0
        for x in range(0, w, 2):
            p = px[x, y]
            if len(p) > 3 and p[3] < 200:
                continue
            if is_light_cyan(p):
                acc[0] += p[0]; acc[1] += p[1]; acc[2] += p[2]; n += 1
        if n >= max(8, w / 40):
            ys.append(y)
    if not ys:
        return []
    runs, cur = [], [ys[0]]
    for y in ys[1:]:
        if y == cur[-1] + 1:
            cur.append(y)
        else:
            runs.append(cur); cur = [y]
    runs.append(cur)
    out = []
    for r0 in runs:
        if len(r0) < 6:
            continue
        y0, y1 = r0[0], r0[-1]
        acc = [0, 0, 0]; n = 0
        for y in r0:
            for x in range(0, w, 2):
                p = px[x, y]
                if len(p) > 3 and p[3] < 200:
                    continue
                if is_light_cyan(p):
                    acc[0] += p[0]; acc[1] += p[1]; acc[2] += p[2]; n += 1
        mean = tuple(int(v / n) for v in acc)
        d = sum((a - b) ** 2 for a, b in zip(mean, REF_WATER)) ** 0.5
        out.append(dict(y0=y0, y1=y1, thick=y1 - y0 + 1, w=w, mean=mean, dist=d, px=n))
    return out


def main():
    files = []
    for pat in ("level_*_tex.png", "level_*_tex_.png", "arena_training_tex.png", "arena_training_tex_.png"):
        files += sorted(glob.glob(os.path.join(SC, pat)))
    files += sorted(glob.glob(os.path.join(TRAIN, "*.png")))
    print("scanning %d original files (arena textures + every arena_training_out frame)" % len(files))
    print("reference water (20_对局_1080x1920.jpg, y865..906, x430..700) = %s\n" % (REF_WATER,))
    hits = []
    for f in files:
        try:
            im = Image.open(f)
        except Exception as e:
            print("  skip %s (%s)" % (os.path.basename(f), e)); continue
        if im.mode != "RGBA":
            im = im.convert("RGBA")
        for b in bands_in(im):
            hits.append((b["dist"], os.path.relpath(f, ROOT), b))
    hits.sort(key=lambda t: t[0])
    print("LIGHT-CYAN bands found: %d  (sorted by distance to the reference water)\n" % len(hits))
    print("  dist  file / band-y / thickness / band mean RGB / cyan px")
    for d, rel, b in hits[:18]:
        tag = "  <== within 25" if d <= 25 else ""
        print("  %6.1f  %-58s y%-5d..%-5d th=%-4d mean=%-16s px=%-7d%s"
              % (d, rel, b["y0"], b["y1"], b["thick"], str(b["mean"]), b["px"], tag))
    close = [h for h in hits if h[0] <= 25]
    print("\nVERDICT (the two criteria are team-lead's):")
    print("  (1) uniqueness: %d candidate(s) within distance 25 of %s" % (len(close), REF_WATER))
    if close:
        seen = sorted({os.path.basename(h[1]) for h in close})
        print("      files: %s" % ", ".join(seen))
    print("  (2) 'the original actually uses it' is NOT decidable from colour alone -- it needs the")
    print("      reference frame to be traced to a specific arena.  NOT CLAIMED here.")

    # ---- discriminating test: can a WHITE overlay (a lighting/tint operation) explain the gap? ----
    # ArenaView.cs L166-170 rules out "compression / colour grading".  A white blend is a DIFFERENT
    # operation.  If some alpha does explain it, the next round's fix is NOT an asset swap.  This is a
    # TEST RESULT, not an explanation: report the best alpha and its residual, claim nothing.
    OUR_WATER = (0, 155, 188)          # measured on CR-V1-arena-nohud.png @12:06 (row mean x430..700)
    print("\nwhite-overlay test: is there an alpha with  (1-a)*OURS + a*255  ~=  REF ?")
    print("  OURS %s   REF %s" % (OUR_WATER, REF_WATER))
    num = sum((255 - o) * (r - o) for o, r in zip(OUR_WATER, REF_WATER))
    den = sum((255 - o) ** 2 for o in OUR_WATER)
    a = num / float(den)
    fit = tuple((1 - a) * o + a * 255 for o in OUR_WATER)
    res = tuple(round(f - r, 1) for f, r in zip(fit, REF_WATER))
    print("  best alpha = %.3f  ->  fit %s   residual (fit-ref) %s   |residual| = %.1f"
          % (a, tuple(round(v, 1) for v in fit), res, sum(v * v for v in res) ** 0.5))
    print("  => NOT a conclusion. If this residual is small the gap is a lighting/tint operation on the")
    print("     SAME art; if it is large the reference really is a different water asset. Decide by")
    print("     measuring an ORIGINAL frame that uses the training arena, not by eyeballing.")
    close2 = [h for h in hits if h[0] <= 25]
    if close2:
        b = close2[0][2]
        print("  for comparison, closest asset band %s in %s has residual |d| = %.1f to REF"
              % (b["mean"], os.path.basename(close2[0][1]), b["dist"]))


main()
