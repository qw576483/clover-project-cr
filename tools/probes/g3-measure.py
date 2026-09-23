# G3 measure helper: run-length colour profile of a baseline image (read-only judgement asset).
# Usage:
#   python tools/probes/g3-measure.py row  <img> <y> [x0] [x1] [quant]
#   python tools/probes/g3-measure.py col  <img> <x> [y0] [y1] [quant]
#   python tools/probes/g3-measure.py band <img> <y0> <y1>   # per-row mean brightness (segments)
# Prints compressed runs: start..end  w  (R,G,B)
import sys
from PIL import Image

def runs(pixels, quant):
    out = []
    start = 0
    prev = None
    for i, p in enumerate(pixels):
        q = tuple(v >> quant for v in p[:3])
        if prev is None:
            prev = q
            continue
        if q != prev:
            out.append((start, i - 1, prev))
            start = i
            prev = q
    out.append((start, len(pixels) - 1, prev))
    return out

def main():
    mode = sys.argv[1]
    im = Image.open(sys.argv[2]).convert('RGB')
    W, H = im.size
    print('image=%s size=%dx%d' % (sys.argv[2], W, H))

    if mode == 'row':
        y = int(sys.argv[3]); x0 = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        x1 = int(sys.argv[5]) if len(sys.argv) > 5 else W - 1
        q = int(sys.argv[6]) if len(sys.argv) > 6 else 4
        px = [im.getpixel((x, y)) for x in range(x0, x1 + 1)]
        for s, e, c in runs(px, q):
            if e - s < 1:
                continue
            print('  x=%4d..%4d w=%4d  rgb=%s' % (s + x0, e + x0, e - s + 1, c))
    elif mode == 'col':
        x = int(sys.argv[3]); y0 = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        y1 = int(sys.argv[5]) if len(sys.argv) > 5 else H - 1
        q = int(sys.argv[6]) if len(sys.argv) > 6 else 4
        px = [im.getpixel((x, y)) for y in range(y0, y1 + 1)]
        for s, e, c in runs(px, q):
            if e - s < 1:
                continue
            print('  y=%4d..%4d h=%4d  rgb=%s' % (s + y0, e + y0, e - s + 1, c))
    elif mode == 'band':
        y0 = int(sys.argv[3]); y1 = int(sys.argv[4])
        q = int(sys.argv[5]) if len(sys.argv) > 5 else 4
        rows = []
        for y in range(y0, y1 + 1):
            s = 0
            for x in range(0, W, 3):
                p = im.getpixel((x, y))
                s += max(p)
            rows.append((y, s / (W / 3.0)))
        prev = None; start = 0
        for y, v in rows:
            b = v > 60
            if prev is None:
                prev = b; continue
            if b != prev:
                print('  y=%4d..%4d h=%4d  %s' % (start, y - 1, y - start, 'BRIGHT' if prev else 'dark'))
                start = y; prev = b
        print('  y=%4d..%4d h=%4d  %s' % (start, rows[-1][0], rows[-1][0] - start + 1, 'BRIGHT' if prev else 'dark'))
    elif mode == 'colblobs':
        # per-column mean brightness over a band -> report bright/dark segments (card columns)
        y0 = int(sys.argv[3]); y1 = int(sys.argv[4]); th = float(sys.argv[5])
        step = int(sys.argv[6]) if len(sys.argv) > 6 else 1
        prof = []
        for x in range(0, W):
            s = 0; n = 0
            for y in range(y0, y1 + 1, step):
                s += max(im.getpixel((x, y)))
                n += 1
            prof.append(s / float(n))
        seg(prof, th, 'x')
    elif mode == 'rowblobs':
        x0 = int(sys.argv[3]); x1 = int(sys.argv[4]); th = float(sys.argv[5])
        step = int(sys.argv[6]) if len(sys.argv) > 6 else 1
        prof = []
        for y in range(0, H):
            s = 0; n = 0
            for x in range(x0, x1 + 1, step):
                s += max(im.getpixel((x, y)))
                n += 1
            prof.append(s / float(n))
        seg(prof, th, 'y')
    elif mode == 'seg2':
        # seg2 <img> <axis x|y> <a0> <a1> <b0> <b1> <min|max> <thresh> <mincount>
        axis = sys.argv[3]
        a0 = int(sys.argv[4]); a1 = int(sys.argv[5]); b0 = int(sys.argv[6]); b1 = int(sys.argv[7])
        how = sys.argv[8]; th = float(sys.argv[9]); mc = int(sys.argv[10])
        counts = []
        for a in range(a0, a1 + 1):
            c = 0
            for b in range(b0, b1 + 1):
                p = im.getpixel((a, b)) if axis == 'x' else im.getpixel((b, a))
                v = min(p) if how == 'min' else max(p)
                if v > th:
                    c += 1
            counts.append(c)
        start = None
        for i, c in enumerate(counts):
            if c >= mc and start is None:
                start = i
            elif c < mc and start is not None:
                print('  %s=%4d..%4d  w=%4d' % (axis, start + a0, i - 1 + a0, i - start))
                start = None
        if start is not None:
            print('  %s=%4d..%4d  w=%4d' % (axis, start + a0, a1, a1 - start + 1))
    elif mode == 'grid':
        from PIL import ImageDraw
        out = sys.argv[3]
        x0 = int(sys.argv[4]); y0 = int(sys.argv[5]); x1 = int(sys.argv[6]); y1 = int(sys.argv[7])
        st = int(sys.argv[8])
        crop = im.crop((x0, y0, x1 + 1, y1 + 1))
        d = ImageDraw.Draw(crop)
        for x in range(x0 - x0 % st, x1 + 1, st):
            d.line([(x - x0, 0), (x - x0, y1 - y0)], fill=(255, 0, 255), width=1)
            d.text((x - x0 + 2, 2), str(x), fill=(255, 0, 255))
        for y in range(y0 - y0 % st, y1 + 1, st):
            d.line([(0, y - y0), (x1 - x0, y - y0)], fill=(0, 255, 255), width=1)
            d.text((2, y - y0 + 2), str(y), fill=(0, 255, 255))
        crop.save(out)
        print('saved %s crop=(%d,%d)-(%d,%d) step=%d' % (out, x0, y0, x1, y1, st))
    elif mode == 'grad':
        # grad <img> <axis y|x> <scan0> <scan1> <band0> <band1> <thresh>
        # Sum of |max-channel difference| across the band for each row (axis y) / column (axis x).
        axis = sys.argv[3]
        s0 = int(sys.argv[4]); s1 = int(sys.argv[5])
        b0 = int(sys.argv[6]); b1 = int(sys.argv[7]); th = float(sys.argv[8])
        def at(u, v):
            return max(im.getpixel((u, v)) if axis == 'y' else im.getpixel((v, u)))
        start = None; best = []
        for a in range(s0, s1 + 1):
            t = 0
            for b in range(b0, b1 + 1):
                t += abs(at(b, a + 1) - at(b, a - 1))
            if t > th:
                best.append((a, t))
        for a, t in best:
            print('  %s=%4d  gradsum=%6d' % (axis, a, t))
    elif mode == 'diff':
        # diff <img> <axis y|x> <scan0> <scan1> <inA0> <inA1> <gapB0> <gapB1> <thresh>
        # per row (axis y): mean(brightness in [inA]) - mean(in [gapB]); runs where > thresh.
        axis = sys.argv[3]
        s0 = int(sys.argv[4]); s1 = int(sys.argv[5])
        a0 = int(sys.argv[6]); a1 = int(sys.argv[7]); b0 = int(sys.argv[8]); b1 = int(sys.argv[9])
        th = float(sys.argv[10])
        def m(a, lo, hi):
            t = 0; n = 0
            for k in range(lo, hi + 1):
                p = im.getpixel((k, a)) if axis == 'y' else im.getpixel((a, k))
                t += max(p); n += 1
            return t / float(n)
        vals = [m(a, a0, a1) - m(a, b0, b1) for a in range(s0, s1 + 1)]
        start = None
        for i, v in enumerate(vals):
            if v > th and start is None:
                start = i
            elif v <= th and start is not None:
                print('  %s=%4d..%4d  w=%4d' % (axis, start + s0, i - 1 + s0, i - start))
                start = None
        if start is not None:
            print('  %s=%4d..%4d  w=%4d' % (axis, start + s0, s1, s1 - start + 1))
    elif mode == 'px':
        # px <img> <axis x|y> <a0> <a1> <b0> <b1> <agg max|mean> <thresh>
        # per-column (axis x) or per-row (axis y) aggregate over the OTHER axis; report runs above threshold.
        axis = sys.argv[3]
        a0 = int(sys.argv[4]); a1 = int(sys.argv[5]); b0 = int(sys.argv[6]); b1 = int(sys.argv[7])
        agg = sys.argv[8]; th = float(sys.argv[9])
        vals = []
        for a in range(a0, a1 + 1):
            best = 0.0; tot = 0.0; n = 0
            for b in range(b0, b1 + 1):
                p = im.getpixel((a, b)) if axis == 'x' else im.getpixel((b, a))
                v = max(p)
                if v > best:
                    best = v
                tot += v; n += 1
            vals.append(best if agg == 'max' else tot / n)
        start = None
        for i, v in enumerate(vals):
            if v > th and start is None:
                start = i
            elif v <= th and start is not None:
                print('  %s=%4d..%4d  w=%4d' % (axis, start + a0, i - 1 + a0, i - start))
                start = None
        if start is not None:
            print('  %s=%4d..%4d  w=%4d' % (axis, start + a0, a1, a1 - start + 1))
    elif mode == 'dumpprof':
        axis = sys.argv[3]
        a0 = int(sys.argv[4]); a1 = int(sys.argv[5]); b0 = int(sys.argv[6]); b1 = int(sys.argv[7])
        step = int(sys.argv[8]) if len(sys.argv) > 8 else 6
        out = []
        rng = range(a0, a1 + 1, step)
        for a in rng:
            s = 0; n = 0
            for b in range(b0, b1 + 1, 4):
                p = im.getpixel((a, b)) if axis == 'x' else im.getpixel((b, a))
                s += max(p)
                n += 1
            out.append('%d:%d' % (a, s // n))
        print(' '.join(out))
    else:
        print('unknown mode')

def seg(prof, th, axis):
    prev = None; start = 0
    for i, v in enumerate(prof):
        b = v > th
        if prev is None:
            prev = b; continue
        if b != prev:
            if i - start >= 2:
                print('  %s=%4d..%4d w=%4d  mean=%6.1f  %s' % (axis, start, i - 1, i - start, sum(prof[start:i]) / (i - start), 'BRIGHT' if prev else 'dark'))
            start = i; prev = b
    if len(prof) - start >= 2:
        print('  %s=%4d..%4d w=%4d  mean=%6.1f  %s' % (axis, start, len(prof) - 1, len(prof) - start, sum(prof[start:]) / (len(prof) - start), 'BRIGHT' if prev else 'dark'))

main()
