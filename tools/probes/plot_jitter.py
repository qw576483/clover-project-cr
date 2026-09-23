#!/usr/bin/env python3
"""CR-T4 judged asset - ONE contact sheet for the "units/monsters still jitter" fix.

Panels (a grid; every cell is labelled and carries its own numbers, so the verdict is read off
a single image):
  row BEFORE / AFTER, columns:
    1 POSITION  rendered position (grid units) vs frame  - the thing the player sees;
    2 SPEED     |dP|/dt of that frame, with the mean line and the min/max/min-max ratio on the cell;
    3 INTERP t  the interpolation ratio t of every frame. THE process judge: the window must sweep
                0->1; the pre-fix capture ceilings at 0.707 (the window was swapped on packet arrival
                before it ever reached its end).
    4 CLOCK/LAG render lag (ms) vs frame. Pre-fix it runs past one snapshot interval (max 119.5);
                post-fix it stays inside [0, 100].
  bottom row: the offline A/B (tools/probes/jitter-sim.py) driven by the SAME recorded frame pacing -
               current algorithm vs the clock-selected window; ground truth is constant-speed motion.

Only ONE unit series per file is drawn: the longest **straight, teleport-free, stop-free** motion run
(the only stretch where a uniform renderer MUST show a constant speed). Selection rule is in
tools/probes/jitter-metrics.py (moving_runs).

Usage: python tools/probes/plot_jitter.py
ASCII-only output on purpose.
"""

import importlib.util
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
TEST_DIR = os.path.join(ROOT, ".ai-tmp", "test")
SHOT_DIR = os.path.join(ROOT, ".ai-tmp", "screenshots")

BEFORE = os.path.join(TEST_DIR, "CR4-flow-before.tsv")
AFTER = os.path.join(TEST_DIR, "T3-flow-cr4-after.tsv")
OUT = os.path.join(SHOT_DIR, "CR-T4-jitter-before-after.png")

W, H = 1560, 1120
PAD, CELLW, CELLH, GAP, HDR = 18, 360, 210, 16, 132


def load_jm():
    spec = importlib.util.spec_from_file_location("jm", os.path.join(HERE, "jitter-metrics.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def panel(d, box, title, xs, ys, color, ylabel, hline=None):
    x0, y0, x1, y1 = box
    d.rectangle(box, outline=(70, 70, 70))
    d.text((x0 + 5, y0 + 4), title, fill=(0, 0, 0))
    if not xs:
        d.text((x0 + 5, y0 + 22), "(no data)", fill=(150, 0, 0))
        return
    lo, hi = min(ys), max(ys)
    if hline is not None:
        lo, hi = min(lo, hline), max(hi, hline)
    if hi - lo < 1e-9:
        hi = lo + 1.0
    sx = (x1 - x0 - 8) / max(1e-9, (xs[-1] - xs[0]))
    sy = (y1 - y0 - 30) / (hi - lo)
    pts = [(x0 + 4 + (x - xs[0]) * sx, y1 - 6 - (y - lo) * sy) for x, y in zip(xs, ys)]
    d.line(pts, fill=color, width=2)
    if hline is not None:
        hy = y1 - 6 - (hline - lo) * sy
        d.line([(x0 + 4, hy), (x1 - 4, hy)], fill=(190, 190, 190))
    d.text((x0 + 5, y1 - 18), "x %d..%d" % (xs[0], xs[-1]), fill=(90, 90, 90))
    d.text((x0 + 5, y0 + 18), "%s  max=%.3f min=%.3f" % (ylabel, hi, lo), fill=(70, 70, 70))


def main():
    jm = load_jm()
    from PIL import Image, ImageDraw, ImageFont
    try:
        font = ImageFont.truetype("consola.ttf", 12)
        big = ImageFont.truetype("consola.ttf", 14)
    except Exception:
        font = big = ImageFont.load_default()

    rows = []
    for tag, path in (("BEFORE", BEFORE), ("AFTER", AFTER)):
        if not os.path.exists(path):
            print("missing %s" % path)
            return 2
        all_rows = jm.read_tsv(path)
        key, g = jm.pick_series(all_rows)
        runs = jm.moving_runs(g)
        if not runs:
            print("%s: no usable motion run" % tag)
            return 2
        run = runs[0]
        rm = jm.run_metrics(g, run)
        seq = [g[i] for i in run]
        tvals = [r["t"] for r in seq]
        lags = [r["lagMs"] for r in seq]
        rows.append((tag, key, seq, rm, tvals, lags))
        print("[%s] unit=%s rows=%d travel=%.2f grid  cv(speed)=%.4f max/min=%.3f min=%.3f max=%.3f  t=%.3f..%.3f lag=%.1f..%.1f"
              % (tag, key, rm["rows"], rm["travel"], rm["cvSpeed"], rm["maxMinRatio"],
                 rm["speedMin"], rm["speedMax"], min(tvals), max(tvals), min(lags), max(lags)))

    # offline A/B on the recorded pacing
    sim = None
    try:
        spec = importlib.util.spec_from_file_location("js", os.path.join(HERE, "jitter-sim.py"))
        js = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(js)
        dt_list = js._read_dt_column(BEFORE)
        sim = {}
        for v in ("current", "lookup"):
            st = js.simulate(v, frames=min(600, len(dt_list) - 1), dt_list=dt_list,
                             jitter_ms=30, delay_ms=12)
            sim[v] = st
            print("[sim %s] cv(speed)=%.4f min=%.4f max=%.4f max/min=%.3f" % (
                v, st["normalSpeedCv"], st["normalMin"], st["normalMax"], st["normalRatio"]))
    except Exception as e:
        print("sim A/B skipped: %s" % e)

    os.makedirs(SHOT_DIR, exist_ok=True)
    img = Image.new("RGB", (W, H), (250, 250, 250))
    d = ImageDraw.Draw(img)
    d.text((PAD, 8), "CR-T4  units/monsters still jitter - ONE unit's straight walk, per frame (BEFORE vs AFTER)",
           fill=(0, 0, 0), font=big)
    d.text((PAD, 30), "source: FlowProbe.SampleFlow -> .ai-tmp/test/CR4-flow-before.tsv (pre-fix code) | T3-flow-cr4-after.tsv (post-fix code)",
           fill=(60, 60, 60), font=font)
    d.text((PAD, 48), "unit = the longest STRAIGHT, teleport-free, stop-free motion run (pool reuse + walk->attack stops would otherwise",
           fill=(60, 60, 60), font=font)
    d.text((PAD, 64), "be read as jitter; they are the game, see tools/probes/jitter-metrics.py moving_runs). SPEED unit: grid/s (1 tile/s = 1.0).",
           fill=(60, 60, 60), font=font)
    d.text((PAD, 84), "col3 INTERP t is the process judge: a window must sweep 0->1. col4 LAG must stay inside [0, one snapshot interval=100ms].",
           fill=(60, 60, 60), font=font)
    d.text((PAD, 102), "bottom row = offline A/B (jitter-sim.py, driven by the recorded frame pacing): gt is CONSTANT-speed motion, so any spread is client-made.",
           fill=(60, 60, 60), font=font)

    y = HDR
    for tag, key, seq, rm, tvals, lags in rows:
        color = (200, 60, 60) if tag == "BEFORE" else (30, 120, 60)
        d.text((PAD, y - 16), "%s   unit instId=%s dir=%s  rows=%d travel=%.2f grid" % (tag, key[0], key[1], rm["rows"], rm["travel"]),
               fill=color, font=big)
        xs = [r["frame"] for r in seq]
        pos = [(r["worldX"], r["worldY"]) for r in seq]
        d.text((PAD + CELLW * 0, y + CELLH + 6), "", fill=(0, 0, 0))
        panels = [
            (xs, [p[1] for p in pos], "1 POSITION y (grid)", None),
            (xs[1:], rm["speed"], "2 SPEED grid/s", rm["meanSpeed"]),
            (xs, tvals, "3 INTERP t", 1.0),
            (xs, lags, "4 RENDER LAG ms", float(jm_mean(tvals) is not None and 0 or 0)),
        ]
        for c, (px, py, title, hl) in enumerate(panels):
            box = (PAD + c * (CELLW + GAP), y, PAD + c * (CELLW + GAP) + CELLW, y + CELLH)
            panel(d, box, "%s / %s" % (tag, title), px, py, color, title.split()[-1], hline=hl)
        d.text((PAD, y + CELLH + 8),
               "speed (normal-dt frames only, hitches %.1f%% excluded): mean=%.3f cv=%.4f min=%.3f max=%.3f max/min=%.2f"
               % (100.0 * rm["hitchCount"] / max(1, len(rm["dtms"])), rm["meanSpeedNorm"], rm["cvSpeedNorm"],
                  rm["minSpeedNorm"], rm["maxSpeedNorm"], rm["maxMinNorm"]),
               fill=(0, 0, 0), font=font)
        d.text((PAD, y + CELLH + 26),
               "...same run incl. hitched frames: cv=%.4f max/min=%.2f   |   t: %.3f..%.3f   lag ms: %.1f..%.1f   |   verdict: %s"
               % (rm["cvSpeed"], rm["maxMinRatio"], min(tvals), max(tvals), min(lags), max(lags), jm.verdict_run(rm)),
               fill=(0, 0, 0), font=font)
        y += CELLH + 54

    if sim:
        d.text((PAD, y - 16), "OFFLINE A/B (same recorded frame pacing) - gt = constant-speed unit", fill=(40, 40, 120), font=big)
        for c, (name, col) in enumerate((("current", (200, 60, 60)), ("lookup", (30, 120, 60)))):
            st = sim[name]
            xs = [r["i"] for r in st["rows"][1:]]
            ys = [st["rows"][i]["step"] / (st["rows"][i]["dt"] / 1000.0) for i in range(1, len(st["rows"]))]
            box = (PAD + c * (CELLW + GAP), y, PAD + c * (CELLW + GAP) + CELLW, y + 150)
            panel(d, box, "sim %s / speed grid/s   cv=%.4f  max/min=%.3f" % (name, st["normalSpeedCv"], st["normalRatio"]),
                  xs, ys, col, "speed", hline=1.0)
        d.text((PAD + 2 * (CELLW + GAP), y + 20),
               "current: cv=%.4f" % sim["current"]["normalSpeedCv"], fill=(200, 60, 60), font=font)
        d.text((PAD + 2 * (CELLW + GAP), y + 40),
               "lookup:  cv=%.4f" % sim["lookup"]["normalSpeedCv"], fill=(30, 120, 60), font=font)
        d.text((PAD + 2 * (CELLW + GAP), y + 60),
               "same arrival jitter +-30ms,", fill=(60, 60, 60), font=font)
        d.text((PAD + 2 * (CELLW + GAP), y + 76),
               "same frame pacing as the capture.", fill=(60, 60, 60), font=font)
        d.text((PAD + 2 * (CELLW + GAP), y + 100),
               "current = rate-steered clock, window", fill=(60, 60, 60), font=font)
        d.text((PAD + 2 * (CELLW + GAP), y + 116),
               "swapped on ARRIVAL.  lookup = clock-", fill=(60, 60, 60), font=font)
        d.text((PAD + 2 * (CELLW + GAP), y + 132),
               "selected window (this fix).", fill=(60, 60, 60), font=font)

    img.save(OUT)
    print("wrote %s (%dx%d)" % (OUT, W, H))
    return 0


def jm_mean(v):
    return sum(v) / len(v) if v else None


if __name__ == "__main__":
    sys.exit(main())
