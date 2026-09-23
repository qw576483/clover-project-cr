# CR-V1: same-camera side-by-side (original ref03 | ours) + numeric readouts.
# Alignment: each image is rescaled so that ONE ARENA TILE = 60 px, i.e. the same camera as ours
# (our viewport is 32 tiles over 1920 px => 60 px/tile). Original px/tile is MEASURED from lane spacing.
# Re-run: python tools/probes/cr-v1-cmp.py
import os, sys, hashlib
import numpy as np
from PIL import Image, ImageDraw
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
REF = os.path.join(ROOT, r"策划\参考图\03_对局_1320x2868.jpg")
OUR = os.path.join(ROOT, r".ai-tmp\screenshots\CR-V1-arena-nohud.png")
OUT = os.path.join(ROOT, r".ai-tmp\screenshots")
TILE = 60.0


def load(p):
    raw = open(p, "rb").read()
    print("[read] %-28s bytes=%-9d sha256_16=%s" % (os.path.basename(p), len(raw),
          hashlib.sha256(raw).hexdigest()[:16].upper()))
    return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)


def lanes(a):
    """tan/gold lane columns in the lower half -> (pxPerTile, xOfTile0)."""
    H, W = a.shape[:2]
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    tan = (r > 165) & (r - b > 45) & (g > 120) & (r - g > 10) & (r - g < 80)
    # window = the own half, below the river; x restricted to the central 86% so the arena's side
    # decorations (which are also tan) cannot form a fake "lane run".
    y0w, y1w = int(H * 0.55), int(H * 0.92)
    x0w, x1w = int(W * 0.07), int(W * 0.93)
    prof = tan[y0w:y1w, :].sum(axis=0)
    need = (y1w - y0w) * 0.30
    runs, c = [], None
    for x in range(W):
        if x0w <= x <= x1w and prof[x] > need:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 12:
                runs.append(((c[0] + c[1]) / 2.0, c[1] - c[0] + 1))
            c = None
    if c and c[1] - c[0] > 12:
        runs.append(((c[0] + c[1]) / 2.0, c[1] - c[0] + 1))
    if len(runs) < 2:
        return None, None, runs
    c1, c2 = runs[0][0], runs[-1][0]
    ppt = (c2 - c1) / 11.0
    return ppt, c1 - 3.5 * ppt, runs


def water_band(a):
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    w = (b - r > 20) & (b > 90) & (g - r > 8)
    rows = w.sum(axis=1)
    hit = np.where(rows > a.shape[1] * 0.25)[0]
    if not hit.size:
        return None
    runs, c = [], None
    for y in range(a.shape[0]):
        if rows[y] > a.shape[1] * 0.25:
            c = [y, y] if c is None else [c[0], y]
        else:
            if c:
                runs.append(tuple(c))
            c = None
    if c:
        runs.append(tuple(c))
    y0, y1 = max(runs, key=lambda t: t[1] - t[0])
    m = w[y0:y1 + 1]
    vs = a[y0:y1 + 1].reshape(-1, 3)[m.reshape(-1)]
    mean = tuple(int(x) for x in vs.mean(axis=0))
    return (y0, y1, y1 - y0 + 1, mean)


def resample_to_60(a, ppt, y_anchor_px, anchor_tile):
    """Scale so 1 tile = 60 px, keeping (y_anchor_px) at tile `anchor_tile` (river = 16)."""
    H, W = a.shape[:2]
    sc = TILE / ppt
    im = Image.fromarray(a.astype(np.uint8)).resize((int(round(W * sc)), int(round(H * sc))), Image.LANCZOS)
    tileY = 16.0 + (np.arange(im.size[1]) - y_anchor_px * sc) / TILE * -1.0
    # top of the 32-tile arena = tile 32
    top = int(round(y_anchor_px * sc - 16 * TILE))
    return im, tileY, top


def strip(im, top, tile_from, tile_to, tile0x):
    y0 = top + int((32 - tile_to) * TILE)
    y1 = top + int((32 - tile_from) * TILE)
    x0 = int(tile0x)
    y0c, y1c, x0c = max(0, y0), min(im.size[1], y1), max(0, x0)
    return im.crop((x0c, y0c, min(im.size[0], x0c + int(18 * TILE)), y1c))


print("=" * 78)
A = load(REF)
O = load(OUR)
pptA, xA, runsA = lanes(A)
pptO, xO, runsO = lanes(O)
print("[lanes] ref03 runs=%s -> pxPerTile=%.3f  tile0x=%.1f" % (runsA, pptA or -1, xA or -1))
print("[lanes] ours  runs=%s -> pxPerTile=%.3f  tile0x=%.1f" % (runsO, pptO or -1, xO or -1))
if pptA is None:
    # fallback = the project's DOCUMENTED value for ref03 (策划/参考图/几何量取.md + tools/probes/cr-t1i-fit.py:
    # "桥心距 1019.5 px / 11 格 = 92.7, 顶部两公主塔中心距 993 / 11 = 90.3 -> 91.5"). NOT a new measurement.
    pptA = 91.5
    xA = A.shape[1] / 2.0 - 9.0 * pptA
    print("[lanes] ref03 auto-detect FAILED -> using DOCUMENTED 91.5 px/tile (cr-t1i-fit.py); NOT measured here")
if pptA is None or pptO is None:
    raise SystemExit("FATAL: pxPerTile unresolved -> refuse to emit a comparison (no silent wrong picture)")

wbA = water_band(A)
wbO = water_band(O)
print("[water] ref03 y%d..%d thick=%d mean=%s" % wbA)
print("[water] ours  y%d..%d thick=%d mean=%s" % wbO)

imA, _, topA = resample_to_60(A, pptA, (wbA[0] + wbA[1]) / 2.0, 16.0)
imO, _, topO = resample_to_60(O, pptO, (wbO[0] + wbO[1]) / 2.0, 16.0)
print("[scale] ref03 -> %s (pxPerTile %.2f -> 60)" % (str(imA.size), pptA))
print("[scale] ours  -> %s (pxPerTile %.2f -> 60)" % (str(imO.size), pptO))

# ---- full side-by-side (top 32 tiles) ----
hA = strip(imA, topA, 0, 32, xA * (TILE / pptA))
hO = strip(imO, topO, 0, 32, xO * (TILE / pptO))
H = max(hA.size[1], hO.size[1])
canvas = Image.new("RGB", (hA.size[0] + hO.size[0] + 12, H), (30, 30, 30))
canvas.paste(hA, (0, 0))
canvas.paste(hO, (hA.size[0] + 12, 0))
d = ImageDraw.Draw(canvas)
d.text((6, 4), "ORIGINAL ref03 (rescaled to 60px/tile)", fill=(255, 255, 0))
d.text((hA.size[0] + 18, 4), "OURS CR-V1 (1.4M) -- AS-OF 11:27:09", fill=(255, 255, 0))
p1 = os.path.join(OUT, "CR-V1-cmp-full.png")
canvas.save(p1)
print("saved", p1, canvas.size)

# ---- river zoom: tiles 12..20 ----
zA = strip(imA, topA, 12, 20, xA * (TILE / pptA))
zO = strip(imO, topO, 12, 20, xO * (TILE / pptO))
zA = zA.resize((zA.size[0] * 2, zA.size[1] * 2), Image.LANCZOS)
zO = zO.resize((zO.size[0] * 2, zO.size[1] * 2), Image.LANCZOS)
c2 = Image.new("RGB", (zA.size[0] + zO.size[0] + 12, max(zA.size[1], zO.size[1]) + 18), (30, 30, 30))
c2.paste(zA, (0, 18))
c2.paste(zO, (zA.size[0] + 12, 18))
d2 = ImageDraw.Draw(c2)
d2.text((6, 2), "ORIGINAL ref03  tiles 12..20 (river+bridge)", fill=(255, 255, 0))
d2.text((zA.size[0] + 18, 2), "OURS CR-V1 tiles 12..20", fill=(255, 255, 0))
p2 = os.path.join(OUT, "CR-V1-cmp-river.png")
c2.save(p2)
print("saved", p2, c2.size)
