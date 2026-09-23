#!/usr/bin/env python3
"""T3 judged asset - contact sheet for the "unit model jitters" fix (BEFORE vs AFTER).

Input : the two per-frame TSVs written by FlowProbe.SampleFlow:
          <root>/.ai-tmp/test/T3-flow-before.tsv   (13 columns)
          <root>/.ai-tmp/test/T3-flow-after.tsv    (20 columns: + ref*, lagMs, t, rate)
Output: one PNG holding BOTH runs, so the verdict is read off a single image:
          <root>/.ai-tmp/screenshots/T3-flow-before-after.png

The three panels per run, and why exactly these:
  1. ARTWORK HOP (px/frame) = |per-frame change of the artwork's offset from the transform
     origin|. This is the defect-specific quantity for root cause 4 and it is independent of
     the unit's movement AND of the frame time: with a per-frame crop-centre pivot the artwork
     sits at a different place inside the quad on every frame, so this hop spikes whenever the
     sprite changes (BEFORE). With one shared anchor it is a constant, so the hop is exactly 0
     (AFTER). It is computed from the recorded `refWorldY - worldY`, or - for the BEFORE file,
     which predates those columns - from the frame's crop rect read out of the Unity .meta files.
  2. EYE-VISIBLE POSITION (screen px) = where a FIXED point of the original canvas lands.
     `SpriteRenderer.bounds.center` cannot be used for this: with a centre pivot it equals
     `transform.position` exactly (verified: bcY == worldY on every BEFORE row), so it is blind
     to the artwork hop and only shows interpolation.
  3. SPEED (px/s) = panel 2's step divided by that frame's dt, with the frame's dt printed.
     Needed because a single huge step is NOT necessarily jitter: measured, the AFTER run's
     biggest step (50.1 px) sits on a 333 ms frame (Unity clamps `Time.deltaTime` to
     `Time.maximumDeltaTime` = 0.33 s, which is why dtMs tops out at 333.3 in the data) - the
     screen simply did not update for 333 ms. Spikes are therefore counted ONLY on frames
     whose dt is <= 2x the median dt; the steps on hitched frames are reported separately.

Selection rule (deterministic, stated in the caption): among the (instId, spriteDir) groups in
one file, take the one with the most sampled frames, tie-broken by largest displacement, with a
minimum of MIN_ROWS frames. One group = "the same unit", which is what the task asks for.

ASCII-only output on purpose (console code page safety).
"""

import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEST_DIR = os.path.join(ROOT, ".ai-tmp", "test")
SHOT_DIR = os.path.join(ROOT, ".ai-tmp", "screenshots")
SPRITE_ROOT = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites")

MIN_ROWS = 40
PANEL_W, PANEL_H = 560, 290
PAD = 16
HDR = 128
ROW_GAP = 74
FOOT = 76

INT_COLS = ("frame", "snaps", "instId")
FLOAT_COLS = ("timeMs", "dtMs", "worldX", "worldY", "bcX", "bcY", "screenX", "screenY",
              "refWorldX", "refWorldY", "refScreenX", "refScreenY", "lagMs", "t", "rate")

RECT_RE = re.compile(
    r"^\s*name:\s*(\S+)\s*\n"
    r"\s*rect:\s*\n"
    r"\s*serializedVersion:\s*\d+\s*\n"
    r"\s*x:\s*(-?\d+)\s*\n"
    r"\s*y:\s*(-?\d+)\s*\n"
    r"\s*width:\s*(-?\d+)\s*\n"
    r"\s*height:\s*(-?\d+)\s*\n",
    re.M,
)
PPU_RE = re.compile(r"^\s*spritePixelsToUnits:\s*([\d.]+)\s*$", re.M)


# ───────────────────────────── inputs ─────────────────────────────

def read_tsv(path):
    """Header-driven: BEFORE has 13 columns, AFTER 20. Parsing by the header line keeps both
    readable instead of silently dropping every row of one of them."""
    rows, header = [], None
    # utf-8-sig: the BEFORE capture was written by C# with `Encoding.UTF8`, i.e. WITH a BOM;
    # read as plain utf-8 the first "# ..." comment line looks like the header and every real
    # row is dropped (0 rows parsed). The C# side now writes without a BOM.
    with open(path, encoding="utf-8-sig") as fh:
        for line in fh:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            parts = line.split("\t")
            if header is None:
                header = parts
                continue
            if len(parts) != len(header):
                continue
            rec = {}
            try:
                for i, name in enumerate(header):
                    if name in INT_COLS:
                        rec[name] = int(parts[i])
                    elif name in FLOAT_COLS:
                        rec[name] = float(parts[i])
                    else:
                        rec[name] = parts[i]
            except ValueError:
                continue
            rows.append(rec)
    return rows


def load_meta_rects():
    """{spriteDir -> {...}} straight from the .meta files Unity wrote: ground truth for each
    frame's crop rect (and hence for the imported per-frame pivot = rect centre), independent of
    any project code. Keyed by directory because `frame_000_0` exists in every directory."""
    out = {}
    if not os.path.isdir(SPRITE_ROOT):
        return out
    for area in os.listdir(SPRITE_ROOT):
        area_dir = os.path.join(SPRITE_ROOT, area)
        if not os.path.isdir(area_dir):
            continue
        for sprite_dir in os.listdir(area_dir):
            d = os.path.join(area_dir, sprite_dir)
            if not os.path.isdir(d):
                continue
            frames, ppu = {}, None
            for fn in os.listdir(d):
                if not fn.endswith(".png.meta"):
                    continue
                text = open(os.path.join(d, fn), encoding="utf-8").read()
                m = RECT_RE.search(text)
                if not m:
                    continue
                if ppu is None:
                    p = PPU_RE.search(text)
                    ppu = float(p.group(1)) if p else 100.0
                frames[m.group(1)] = (int(m.group(2)), int(m.group(3)), int(m.group(4)), int(m.group(5)))
            if frames:
                xs0 = min(v[0] for v in frames.values())
                ys0 = min(v[1] for v in frames.values())
                xs1 = max(v[0] + v[2] for v in frames.values())
                ys1 = max(v[1] + v[3] for v in frames.values())
                out[sprite_dir] = {"frames": frames, "ppu": ppu or 100.0, "canvas": (xs0, ys0, xs1, ys1)}
    return out


# ───────────────────────────── series ─────────────────────────────

def pick_series(rows):
    groups = {}
    for r in rows:
        groups.setdefault((r["instId"], r["dir"]), []).append(r)
    best = None
    for key, g in groups.items():
        g.sort(key=lambda r: r["frame"])
        if len(g) < MIN_ROWS:
            continue
        disp = max(abs(g[-1]["screenY"] - g[0]["screenY"]), abs(g[-1]["screenX"] - g[0]["screenX"]))
        score = (len(g), disp)
        if best is None or score > best[0]:
            best = (score, key, g)
    return (best[1], best[2]) if best else (None, None)


def fit_camera(g):
    """Least-squares fit of screenY = a * worldY + b over ALL rows.

    An orthographic camera makes this map exactly linear, so the residuals are pure quantization
    (screenY is recorded with 1 decimal) - reported as `cam-check`.
    ⚠️ Do NOT fit from a single pair: the first two rows with different worldY gave a = 0.0,
    because a tiny world delta rounds to 0.0 px at the recorded precision."""
    n = len(g)
    if n < 2:
        return None, None
    mx = sum(r["worldY"] for r in g) / n
    my = sum(r["screenY"] for r in g) / n
    sxx = sum((r["worldY"] - mx) ** 2 for r in g)
    if sxx <= 1e-12:
        return None, None
    a = sum((r["worldY"] - mx) * (r["screenY"] - my) for r in g) / sxx
    return a, my - a * mx


def artwork_offset_px(g, info, cam_a):
    """Artwork's offset from the transform origin, in screen px, per frame (see module doc)."""
    out = []
    for r in g:
        if "refWorldY" in r:
            off = r["refWorldY"] - r["worldY"]
        elif info is not None and r["sprite"] in info["frames"]:
            _, y, _, h = info["frames"][r["sprite"]]
            cy0, cy1 = info["canvas"][1], info["canvas"][3]
            off = ((cy0 + cy1) / 2.0 - (y + h / 2.0)) / info["ppu"]
        else:
            out.append(None)
            continue
        out.append(off * (cam_a if cam_a else 1.0))
    return out


def boundary_analysis(g, cam_a):
    """Root cause 1 discriminator, measured on `transform.position` ONLY (no artwork offset).

    The old code reset the interpolation ratio to 0 every time a snapshot arrived, so the
    movement concentrated into the arrival frame (a forward hop) whenever the snapshot interval
    was shorter than the hard-coded 100 ms. A render-clock-driven interpolation spreads the same
    movement evenly over the interval. So compare the median SPEED (step/dt) of
      * frames where a new snapshot arrived (the `snaps` counter increased), vs
      * frames where none did.
    Healthy => the two are about equal (ratio ~ 1). Broken => the arrival frames are much faster.
    Using speed instead of raw step removes the confound that a long frame is also a long step."""
    bound, other = [], []
    for i in range(1, len(g)):
        dt = g[i]["dtMs"] / 1000.0
        if dt <= 1e-6:
            continue
        v = abs(g[i]["worldY"] - g[i - 1]["worldY"]) * (cam_a or 1.0) / dt
        (bound if g[i]["snaps"] != g[i - 1]["snaps"] else other).append(v)
    # Use the MEAN, not the median: a unit that is stationary for most of the window makes the
    # median 0 for both classes and the ratio degenerates to inf/0 - a useless statistic (hit
    # on the very first BEFORE run). The mean still shows where the movement is concentrated.
    mean = lambda a: (sum(a) / len(a)) if a else 0.0
    mb, mo = mean(bound), mean(other)
    return mb, mo, (mb / mo if mo > 1e-9 else float("inf")), len(bound), len(other)


def stats(g, eye, offsets, cam_a=None):
    ys = [y for _, y in eye]
    d = [ys[i] - ys[i - 1] for i in range(1, len(ys))]
    nz = [abs(v) for v in d if abs(v) > 1e-9]
    med = sorted(nz)[len(nz) // 2] if nz else 0.0
    mx = max((abs(v) for v in d), default=0.0)

    dts = [r["dtMs"] for r in g]
    med_dt = sorted(dts)[len(dts) // 2] if dts else 0.0
    # A frame whose dt is far above the median is a hitch (the screen did not update at all for
    # that long), so its step is not evidence of jitter -> count spikes only on normal-dt frames.
    normal = set(i for i in range(1, len(g)) if g[i]["dtMs"] <= 2 * med_dt)
    spikes = sum(1 for i in range(1, len(d)) if i in normal and med > 0 and abs(d[i - 1]) > 3 * med)
    spikes_all = sum(1 for v in d if med > 0 and abs(v) > 3 * med)
    hitch_max = max([abs(d[i - 1]) for i in range(1, len(d)) if i not in normal], default=0.0)

    hop = hop_series(offsets)
    hop_nz = [h for h in hop if h is not None and h > 1e-6]
    hop_rms = (sum(h * h for h in hop_nz) / len(hop_nz)) ** 0.5 if hop_nz else 0.0

    idx = [sprite_index(r["sprite"]) for r in g]
    st = {
        "rows": len(g), "medStep": med, "maxStep": mx,
        "ratio": (mx / med) if med > 0 else float("inf"),
        "spikes": spikes, "spikesAll": spikes_all, "hitchMax": hitch_max, "medDt": med_dt,
        "distinctSprites": len(set(idx)),
        "spriteMonotonic": all(idx[i] >= idx[i - 1] for i in range(1, len(idx))),
        "travel": abs(ys[-1] - ys[0]),
        "frames": g[-1]["frame"] - g[0]["frame"] + 1,
        "dts": sorted(dts),
        "hopMax": max(hop_nz) if hop_nz else 0.0, "hopRms": hop_rms, "hopCount": len(hop_nz),
        "lag": None, "t": None, "rate": None, "eye": ys,
        "bound": boundary_analysis(g, cam_a),
    }
    if g and "lagMs" in g[0]:
        lags = [r["lagMs"] for r in g if r["lagMs"] == r["lagMs"]]
        if lags:
            st["lag"] = (min(lags), max(lags), sum(lags) / len(lags))
    if g and "t" in g[0]:
        ts = [r["t"] for r in g if r["t"] == r["t"]]
        if ts:
            st["t"] = (min(ts), max(ts))
    if g and "rate" in g[0]:
        rs = [r["rate"] for r in g if r["rate"] == r["rate"]]
        if rs:
            st["rate"] = (min(rs), max(rs))
    return st


def hop_series(offsets):
    step = [0.0]
    for i in range(1, len(offsets)):
        if offsets[i] is None or offsets[i - 1] is None:
            step.append(0.0)
        else:
            step.append(abs(offsets[i] - offsets[i - 1]))
    return step


def sprite_index(name):
    """Second-to-last numeric underscore segment (same rule as the fixed C# parser)."""
    segs = name.split("_")
    for i in range(len(segs) - 2, -1, -1):
        if segs[i].isdigit():
            return int(segs[i])
    if segs and segs[-1].isdigit():
        return int(segs[-1])
    return -1


# ───────────────────────────── drawing ─────────────────────────────

def draw_panel(d, box, title, xs, ys, color, ylabel, hline=None):
    # Panel labels are drawn INSIDE the box: drawing the column title just above it collided with
    # the row title (both land near row_y-13/row_y-15), which made the judged image unreadable.
    x0, y0, x1, y1 = box
    d.rectangle(box, outline=(80, 80, 80))
    d.text((x0 + 5, y0 + 4), title, fill=(0, 0, 0))
    if not xs:
        return
    lo, hi = min(ys), max(ys)
    if hline is not None:
        lo, hi = min(lo, hline), max(hi, hline)
    if hi - lo < 1e-9:
        hi = lo + 1.0
    sx = (x1 - x0 - 8) / max(1e-9, (xs[-1] - xs[0]))
    sy = (y1 - y0 - 8) / (hi - lo)
    pts = [(x0 + 4 + (x - xs[0]) * sx, y1 - 4 - (y - lo) * sy) for x, y in zip(xs, ys)]
    d.line(pts, fill=color, width=2)
    if hline is not None:
        hy = y1 - 4 - (hline - lo) * sy
        d.line([(x0 + 4, hy), (x1 - 4, hy)], fill=(190, 190, 190))
    d.text((x0 + 5, y1 - 16), "x: %d..%d" % (xs[0], xs[-1]), fill=(90, 90, 90))
    d.text((x0 + 5, y0 + 18), "%s  max=%.2f  min=%.2f" % (ylabel, hi, lo), fill=(90, 90, 90))


def build(runs, meta):
    panels = []
    for tag, rows in runs:
        key, g = pick_series(rows)
        if not g:
            panels.append((tag, key, g, None))
            print("[%s] NO usable series (rows=%d, need >= %d)" % (tag, len(rows), MIN_ROWS))
            continue
        info = meta.get(key[1])
        cam_a, cam_b = fit_camera(g)
        rec_err = 0.0
        if "refScreenY" in g[0] and cam_a is not None and info is not None:
            for r in g:
                if r["sprite"] in info["frames"]:
                    _, y, _, h = info["frames"][r["sprite"]]
                    cy0, cy1 = info["canvas"][1], info["canvas"][3]
                    rec = cam_a * (r["worldY"] + ((cy0 + cy1) / 2.0 - (y + h / 2.0)) / info["ppu"]) + cam_b
                    rec_err = max(rec_err, abs(rec - r["refScreenY"]))
        offsets = artwork_offset_px(g, info, cam_a)
        eye = [(r["frame"], r["refScreenY"] if "refScreenY" in r
                else (cam_a * (r["worldY"] + (offsets[i] / (cam_a or 1.0))) + cam_b
                      if offsets[i] is not None else r["screenY"]))
               for i, r in enumerate(g)]
        st = stats(g, eye, offsets, cam_a)
        st["cam"] = (cam_a, cam_b)
        st["reconErr"] = rec_err
        st["hop"] = hop_series(offsets)
        panels.append((tag, key, g, st))
        print("[%s] series instId=%s dir=%s rows=%d travelPx=%.1f" % (tag, key[0], key[1], len(g), st["travel"]))
        print("    ARTWORK HOP (root cause 4): max=%.3f px  rms=%.3f px  frames with hop>0: %d/%d"
              % (st["hopMax"], st["hopRms"], st["hopCount"], st["rows"]))
        print("    EYE step px: med=%.3f max=%.3f  spikes(>3xmed, normal-dt frames only)=%d  [all frames=%d]  biggest step on a hitched frame=%.1f px"
              % (st["medStep"], st["maxStep"], st["spikes"], st["spikesAll"], st["hitchMax"]))
        print("    sprites=%d spriteIdxNonDecreasing=%s frames=%d dtMs min/med/max=%.1f/%.1f/%.1f"
              % (st["distinctSprites"], st["spriteMonotonic"], st["frames"],
                 st["dts"][0], st["medDt"], st["dts"][-1]))
        print("    SNAPSHOT-BOUNDARY SPEED (root cause 1, position only): mean arrival-frame=%.1f px/s vs other=%.1f px/s  ratio=%.2f  (n=%d/%d)"
              % (st["bound"][0], st["bound"][1], st["bound"][2], st["bound"][3], st["bound"][4]))
        print("    cameraFit a=%.4f b=%.2f  cam-check max|recon-recorded|=%.4f px" % (cam_a, cam_b, rec_err))
        if st["lag"]:
            print("    renderLagMs min/avg/max=%.1f/%.1f/%.1f   interpRatio t %.3f..%.3f   clockRate %.2f..%.2f"
                  % (st["lag"][0], st["lag"][2], st["lag"][1], st["t"][0] if st["t"] else 0.0,
                     st["t"][1] if st["t"] else 0.0,
                     st["rate"][0] if st["rate"] else 0.0, st["rate"][1] if st["rate"] else 0.0))
    return panels


def main():
    before = os.path.join(TEST_DIR, "T3-flow-before.tsv")
    after = os.path.join(TEST_DIR, "T3-flow-after.tsv")
    for p in (before, after):
        if not os.path.exists(p):
            print("missing input: %s" % p)
            return 2
    meta = load_meta_rects()
    print("meta rects loaded for %d dirs" % len(meta))

    panels = build([("BEFORE (per-frame crop-centre pivot)", read_tsv(before)),
                    ("AFTER (one shared canvas anchor)", read_tsv(after))], meta)

    os.makedirs(SHOT_DIR, exist_ok=True)
    out = os.path.join(SHOT_DIR, "T3-flow-before-after.png")
    W = 3 * PANEL_W + 4 * PAD
    H = HDR + 2 * (PANEL_H + ROW_GAP) + FOOT
    img = Image.new("RGB", (W, H), (252, 252, 252))
    d = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype("consola.ttf", 12)
        big = ImageFont.truetype("consola.ttf", 14)
    except Exception:
        font = ImageFont.load_default()
        big = font

    d.text((PAD, 8), "T3 battle-jitter fix - per-frame sequence of ONE unit (BEFORE vs AFTER)", fill=(0, 0, 0), font=big)
    d.text((PAD, 28), "source: FlowProbe.SampleFlow -> .ai-tmp/test/T3-flow-{before,after}.tsv   series = the (instId, spriteDir) group with most frames", fill=(60, 60, 60), font=font)
    d.text((PAD, 46), "col1 ARTWORK HOP px/frame = how far the artwork jumps inside its quad on a frame change (root cause 4). It must be a flat 0 line.", fill=(60, 60, 60), font=font)
    d.text((PAD, 64), "col2 EYE-VISIBLE screen position of a FIXED artwork point (what the player sees).   col3 its SPEED px/s (step / dt of that frame).", fill=(60, 60, 60), font=font)
    d.text((PAD, 82), "BEFORE's columns are reconstructed from the runtime world position + the crop rect in the Unity .meta files (that run predates the ref*", fill=(60, 60, 60), font=font)
    d.text((PAD, 100), "columns); the reconstruction is cross-checked on the AFTER run against its recorded pixel values (cam-check line under each panel).", fill=(60, 60, 60), font=font)

    row_y = HDR
    for tag, key, g, st in panels:
        color = (200, 60, 60) if tag.startswith("BEFORE") else (30, 120, 60)
        d.text((PAD, row_y - 15), "%s   unit instId=%s dir=%s" % (tag, key[0] if key else "-", key[1] if key else "-"),
               fill=color, font=big)
        if g and st:
            xs = [r["frame"] for r in g]
            eye = st["eye"]
            steps = [0.0] + [eye[i] - eye[i - 1] for i in range(1, len(eye))]
            speed = [0.0] + [steps[i] / max(1e-6, g[i]["dtMs"] / 1000.0) for i in range(1, len(g))]
            for c, (px, py, ylab, hl) in enumerate(((xs, st["hop"], "hop px/frame", 0.0),
                                                    (xs, eye, "screen px", None),
                                                    (xs, speed, "speed px/s", None))):
                box = (PAD + c * (PANEL_W + PAD), row_y, PAD + c * (PANEL_W + PAD) + PANEL_W, row_y + PANEL_H)
                draw_panel(d, box, "%s / %s" % (tag.split()[0], ylab), px, py, color, ylab, hline=hl)
            d.text((PAD, row_y + PANEL_H + 18),
                   "ARTWORK HOP max=%.3f px rms=%.3f px  (frames with hop>0: %d/%d)   |   eye step med=%.3f max=%.3f px; spikes(normal-dt)=%d [all=%d]; hitched-frame step max=%.1f px"
                   % (st["hopMax"], st["hopRms"], st["hopCount"], st["rows"],
                      st["medStep"], st["maxStep"], st["spikes"], st["spikesAll"], st["hitchMax"]),
                   fill=(0, 0, 0), font=font)
            d.text((PAD, row_y + PANEL_H + 50),
                   "SNAPSHOT-BOUNDARY SPEED (root cause 1; position only): mean speed on snapshot-arrival frames = %.1f px/s vs other frames = %.1f px/s  ->  ratio %.2f   (must be ~1)"
                   % (st["bound"][0], st["bound"][1], st["bound"][2]), fill=(0, 0, 0), font=font)
            extra = "sprites=%d ordered=%s  dtMs med/max=%.1f/%.1f  cam-check=%.4f px" % (
                st["distinctSprites"], st["spriteMonotonic"], st["medDt"], st["dts"][-1], st["reconErr"])
            if st["lag"]:
                extra += "   renderLagMs min/avg/max=%.1f/%.1f/%.1f  t=%.3f..%.3f  rate=%.2f..%.2f" % (
                    st["lag"][0], st["lag"][2], st["lag"][1],
                    st["t"][0] if st["t"] else 0.0, st["t"][1] if st["t"] else 0.0,
                    st["rate"][0] if st["rate"] else 0.0, st["rate"][1] if st["rate"] else 0.0)
            else:
                extra += "   renderLagMs NOT INSTRUMENTED in that run"
            d.text((PAD, row_y + PANEL_H + 34), extra, fill=(0, 0, 0), font=font)
        row_y += PANEL_H + ROW_GAP

    d.text((PAD, H - FOOT + 10),
           "re-run:  python tools/probes/plot_flow.py   (after FlowProbe.SampleFlowBefore / SampleFlowAfter wrote the TSVs)",
           fill=(90, 90, 90), font=font)
    d.text((PAD, H - FOOT + 28),
           "note: spriteIdxNonDecreasing=False is the registered whole-directory-clip difference (no frame-segment data ships with",
           fill=(90, 90, 90), font=font)
    d.text((PAD, H - FOOT + 46),
           "the assets), not a frame-order defect: within one clip the index rises strictly.",
           fill=(90, 90, 90), font=font)
    img.save(out)
    print("wrote %s (%dx%d)" % (out, W, H))
    return 0


if __name__ == "__main__":
    sys.exit(main())
