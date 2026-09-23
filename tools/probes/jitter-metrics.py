#!/usr/bin/env python3
"""CR-T4 judged asset - decide WHICH KIND of jitter a per-frame displacement series is.

WHY THIS FILE EXISTS
--------------------
"the monster still jitters" is a symptom, not a diagnosis. Three mechanically different
defects produce a look that all gets called "jitter", and each needs a DIFFERENT fix:

  1. OSCILLATION   the position goes back and forth frame to frame (forward step, backward
                   step, forward step...). Fix = a smoother/1-pole filter, or removing a
                   competing writer (server push-apart, two writers on one transform).
  2. STAIRCASE     the position only moves on snapshot-arrival frames and is frozen in
                   between (10 Hz stepping seen at 60 FPS). Fix = interpolate; the metric
                   is the arrival-frame vs non-arrival-frame speed ratio.
  3. VELOCITY RIPPLE  the direction is right and it never stops, but the SPEED pulses --
                   fast frame, slow frame, fast frame. Fix = make the clock rate constant
                   (decouple the steady-state lag correction from the render-clock rate, or
                   move the correction to the position layer).

So: measure first, then fix. Tweaking a smoothing constant without naming the form is
forbidden (it "turns the metric green" without touching the cause).

WHY THESE METRICS
-----------------
For the sampled series the script projects every step onto the series' own net travel
direction, giving a signed per-frame step s[i] (grid units per frame). Then:

  zeroFrac        fraction of |s[i]| below NOISE * mean|s|   -> high for a staircase.
  signFlipFrac    fraction of consecutive pairs with s[i]*s[i+1] < 0 -> high for oscillation.
  autocorr1       lag-1 autocorrelation of s. ~ -0.5 = strict alternation (oscillation);
                  > 0 = runs of equal-ish steps (ripple); ~ 0 = white (nothing structured).
  cv              std(|s|)/mean(|s|). The "how much does the speed pulse" number.
  maxMinRatio     max|s| / min|s| over non-noise steps (the task asks for this ratio).
  arrivalRatio    mean speed on snapshot-arrival frames / mean speed on the others.
                  ~ 1 = healthy; >> 1 = the movement is concentrated at arrivals (staircase).
  rateMin/rateMax the rendered clock rate recorded in the `rate` column (form 3's cause:
                  the clock rate itself is moving, so the position's speed is modulated).

Usage:
  python tools/probes/jitter-metrics.py <tsv> [<tsv> ...]      # metrics + verdict per file
  python tools/probes/jitter-metrics.py --table <tsv> [n]      # + first n rows as a numeric table
ASCII-only output on purpose (console code page safety).
"""

import math
import os
import sys

MIN_ROWS = 40
NOISE = 0.02          # |step| below this fraction of mean|step| counts as "did not move"
HITCH_MS = 60.0       # a frame longer than this is the screen not updating (Unity's dt cap is 333ms),
                      # so its step says nothing about the renderer (see run_metrics)

INT_COLS = ("frame", "snaps", "instId")
FLOAT_COLS = ("timeMs", "dtMs", "worldX", "worldY", "bcX", "bcY", "screenX", "screenY",
              "refWorldX", "refWorldY", "refScreenX", "refScreenY", "lagMs", "t", "rate",
              # CR-T4 追加列（旧文件没有这几列，按 header 解析 ⇒ 兼容）
              "bufLagMs", "winStartMs", "winEndMs")


def read_tsv(path):
    """Header-driven (the two historical captures have 13 vs 20 columns)."""
    rows, header = [], None
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


def pick_series(rows):
    """Same deterministic rule as plot_flow.py: the (instId, spriteDir) group with the most
    frames, ties broken by the largest displacement. One group == one unit."""
    groups = {}
    for r in rows:
        groups.setdefault((r["instId"], r["dir"]), []).append(r)
    best = None
    for key, g in groups.items():
        g.sort(key=lambda r: r["frame"])
        if len(g) < MIN_ROWS:
            continue
        disp = math.hypot(g[-1]["worldX"] - g[0]["worldX"], g[-1]["worldY"] - g[0]["worldY"])
        score = (len(g), disp)
        if best is None or score > best[0]:
            best = (score, key, g)
    return (best[1], best[2]) if best else (None, None)


def moving_runs(g, use_screen=True, teleport_factor=8.0, stall_frac=0.12, min_run=MIN_ROWS,
                turn_deg=25.0):
    """Split the sampled series into **continuous motion runs**.

    Why this is mandatory and not cosmetic: `instId` is a Unity `siblingIndex`, so when a unit dies the
    pooled `UnitView` is recycled for a *different* unit -- the group shares an id but not an identity, and
    the frame where the swap happens carries a teleport (measured: 57.9 px vs a 1.9 px median step). And a
    unit that reached its target stops to attack. Both are REAL behaviour that the task's metric ("N frames
    of the same entity, covering one full advance") must exclude, otherwise the reported speed spread is
    the game playing, not the renderer jittering.
    """
    steps, _, _ = series_steps(g, use_screen)      # steps[i-1] = the step from g[i-1] to g[i]
    if not steps:
        return []
    med = sorted(abs(s) for s in steps)[len(steps) // 2]
    # 1) cut at teleports (a recycled pooled view switching to another unit)
    bounds = [0] + [i for i in range(1, len(g))
                    if abs(steps[i - 1]) > teleport_factor * max(med, 1e-6)] + [len(g)]
    runs = [list(range(bounds[k], bounds[k + 1])) for k in range(len(bounds) - 1)]
    # 2) cut at stalls (>= 3 consecutive near-zero steps = the unit reached its target and attacks)
    out = []
    for run in runs:
        if len(run) < min_run:
            continue
        slow = [abs(steps[i - 1]) <= stall_frac * med for i in run[1:]]
        start = 0
        for k in range(len(slow) - 2):
            if slow[k] and slow[k + 1] and slow[k + 2]:
                seg = run[start:k + 1]
                if len(seg) >= min_run:
                    out.append(seg)
                start = k + 3
        seg = run[start:]
        if len(seg) >= min_run:
            out.append(seg)
    # 3) cut at TURNS (>= turn_deg between consecutive step vectors).
    #    A unit turning a corner genuinely moves a shorter *chord* in that frame, so |dP|/dt dips --
    #    that is the game pathfinding, not a renderer defect. The renderer's own contribution can only be
    #    judged on straight-line motion, where a uniform clock MUST give a constant |dP|/dt.
    cosmax = math.cos(math.radians(turn_deg))
    straight = []
    for run in out:
        start = 0
        for k in range(2, len(run)):
            a = (g[run[k - 1]]["worldX"] - g[run[k - 2]]["worldX"],
                 g[run[k - 1]]["worldY"] - g[run[k - 2]]["worldY"])
            b = (g[run[k]]["worldX"] - g[run[k - 1]]["worldX"],
                 g[run[k]]["worldY"] - g[run[k - 1]]["worldY"])
            na = math.hypot(*a)
            nb = math.hypot(*b)
            if na > 1e-9 and nb > 1e-9 and (a[0] * b[0] + a[1] * b[1]) / (na * nb) < cosmax:
                seg = run[start:k]
                if len(seg) >= min_run:
                    straight.append(seg)
                start = k
        seg = run[start:]
        if len(seg) >= min_run:
            straight.append(seg)
    straight.sort(key=len, reverse=True)
    return straight


def run_metrics(g, idx):
    """Speed/step statistics of one continuous run, using the **measured real-time delta** between rows
    (`timeMs` is `Time.realtimeSinceStartup*1000`) rather than the `dtMs` field: the field is read in the
    probe's own Update, so pairing it with a position captured by another component is only valid if the
    two Updates share a frame -- the real delta has no such assumption."""
    steps, axis, travel = [], None, 0.0
    xs = [(g[i]["worldX"], g[i]["worldY"]) for i in idx]
    ax = xs[-1][0] - xs[0][0]
    ay = xs[-1][1] - xs[0][1]
    nrm = math.hypot(ax, ay)
    travel = nrm
    ux, uy = (ax / nrm, ay / nrm) if nrm > 1e-9 else (0.0, 1.0)
    for k in range(1, len(idx)):
        a, b = xs[k - 1], xs[k]
        dx, dy = b[0] - a[0], b[1] - a[1]
        steps.append((dx * ux + dy * uy, math.hypot(dx, dy)))
    proj = [s[0] for s in steps]
    mag = [s[1] for s in steps]
    dtms = [g[idx[k]]["timeMs"] - g[idx[k - 1]]["timeMs"] for k in range(1, len(idx))]
    speed = [mag[k] / (dtms[k] / 1000.0) if dtms[k] > 1e-6 else 0.0 for k in range(len(mag))]
    good = [v for v in speed if v > 1e-9]
    m = mean(good)
    sd = stdev(good)
    nz = [v for v in speed if v > NOISE * m]
    # "normal-dt" subset: a frame whose real dt is far above the median is the SCREEN not updating
    # (a hitch, Unity clamps Time.deltaTime to maximumDeltaTime = 0.333 s but the wall clock runs on),
    # so its step is not evidence about the renderer. Reported separately, and the headline cv is this one.
    # both the hitched frame AND the frame right after it are excluded: the hitch itself is the screen
    # not updating, and the recovery frame carries the travel that was skipped during the hitch.
    norm = [speed[k] for k in range(len(speed))
            if dtms[k] <= HITCH_MS and (k == 0 or dtms[k - 1] <= HITCH_MS)]
    nm = mean(norm) if norm else 0.0
    nz2 = [v for v in norm if v > NOISE * nm] if nm > 0 else []
    return {
        "rows": len(idx),
        "travel": travel,
        "meanStep": mean(mag), "stdStep": stdev(mag),
        "meanSpeed": m, "cvSpeed": (sd / m) if m > 1e-12 else float("inf"),
        "speedMin": min(nz) if nz else 0.0,
        "speedMax": max(nz) if nz else 0.0,
        "maxMinRatio": (max(nz) / min(nz)) if nz and min(nz) > 1e-12 else float("inf"),
        "zeroFrac": (1.0 - len(nz) / len(speed)) if speed else 0.0,
        "flipFrac": (sum(1 for i in range(1, len(proj)) if proj[i] * proj[i - 1] < 0) / (len(proj) - 1)) if len(proj) > 1 else 0.0,
        "autocorr1": autocorr1(proj),
        "speedAutocorr1": autocorr1(speed),
        "speed": speed,
        "dtms": dtms,
        "hitchCount": sum(1 for v in dtms if v > HITCH_MS),
        "speedNorm": norm,
        "meanSpeedNorm": nm,
        "cvSpeedNorm": (stdev(norm) / nm) if nm > 1e-12 else float("inf"),
        "maxMinNorm": (max(nz2) / min(nz2)) if nz2 and min(nz2) > 1e-12 else float("inf"),
        "minSpeedNorm": min(nz2) if nz2 else 0.0,
        "maxSpeedNorm": max(nz2) if nz2 else 0.0,
        "frames": g[idx[-1]]["frame"] - g[idx[0]]["frame"] + 1,
        "index": idx,
    }


def series_steps(g, use_screen):
    """Signed per-frame step projected onto the net travel direction.
    For the position we use the *eye-visible* quantity when the capture has refScreenX/Y
    (that is "what the player sees"), else the transform world position."""
    xs, ys = [], []
    for r in g:
        if use_screen and "refScreenX" in r and r["refScreenX"] == r["refScreenX"]:
            xs.append(r["refScreenX"])
            ys.append(r["refScreenY"])
        else:
            xs.append(r["worldX"])
            ys.append(r["worldY"])
    n = len(xs)
    dx, dy = xs[-1] - xs[0], ys[-1] - ys[0]
    norm = math.hypot(dx, dy)
    if norm < 1e-9:                      # standing unit: use the dominant recorded axis
        spanx = max(xs) - min(xs)
        spany = max(ys) - min(ys)
        dx, dy, norm = (1.0, 0.0, 1.0) if spanx >= spany else (0.0, 1.0, 1.0)
    ux, uy = dx / norm, dy / norm
    steps = []
    for i in range(1, n):
        steps.append((xs[i] - xs[i - 1]) * ux + (ys[i] - ys[i - 1]) * uy)
    return steps, (ux, uy), norm


def mean(a):
    return sum(a) / len(a) if a else 0.0


def stdev(a):
    if len(a) < 2:
        return 0.0
    m = mean(a)
    return math.sqrt(sum((v - m) ** 2 for v in a) / (len(a) - 1))


def autocorr1(a):
    if len(a) < 3:
        return 0.0
    m = mean(a)
    num = sum((a[i] - m) * (a[i - 1] - m) for i in range(1, len(a)))
    den = sum((v - m) ** 2 for v in a)
    return num / den if den > 1e-12 else 0.0


def pearson(a, b):
    """Correlation of two equal-length series (used to attribute speed pulsing to a cause)."""
    n = min(len(a), len(b))
    if n < 3:
        return 0.0
    a, b = a[:n], b[:n]
    ma, mb = mean(a), mean(b)
    num = sum((a[i] - ma) * (b[i] - mb) for i in range(n))
    da = math.sqrt(sum((v - ma) ** 2 for v in a))
    db = math.sqrt(sum((v - mb) ** 2 for v in b))
    return num / (da * db) if da * db > 1e-12 else 0.0


def analyse(g, use_screen=True):
    steps, axis, travel = series_steps(g, use_screen)
    absS = [abs(s) for s in steps]
    mabs = mean(absS)
    big = [v for v in absS if v > NOISE * mabs]
    zero_frac = 1.0 - (len(big) / len(absS)) if absS else 0.0
    flips = sum(1 for i in range(1, len(steps)) if steps[i] * steps[i - 1] < 0.0)
    flip_frac = flips / (len(steps) - 1) if len(steps) > 1 else 0.0

    # arrival-frame vs other-frame SPEED (raw step / dt; step is grid units, dt in ms)
    arr, oth = [], []
    for i in range(1, len(g)):
        dt = g[i]["dtMs"] / 1000.0
        if dt <= 1e-6:
            continue
        v = abs(steps[i - 1]) / dt
        (arr if g[i]["snaps"] != g[i - 1]["snaps"] else oth).append(v)
    marr, moth = mean(arr), mean(oth)

    # ── SPEED series (step / dt of that frame) ────────────────────────────────
    # This is the quantity the eye integrates. The raw step is confounded by the frame
    # time (this project's captures run at ~30 FPS with 333 ms hitches), so the honest
    # "does the speed pulse" numbers must be computed on speed, not on step.
    speed, rateCol, spanCol, dtMsCol = [], [], [], []
    for i in range(len(g)):
        dt = g[i]["dtMs"] / 1000.0
        speed.append((abs(steps[i - 1]) / dt) if (i > 0 and dt > 1e-6) else 0.0)
        dtMsCol.append(g[i]["dtMs"])
        rateCol.append(g[i].get("rate", float("nan")))
    for i in range(1, len(g)):
        spanCol.append(g[i]["snaps"] - g[i - 1]["snaps"])   # snapshots consumed in this frame
    mSp = mean(speed)
    spCv = (stdev(speed) / mSp) if mSp > 1e-12 else float("inf")
    # speed of frames whose dt is "normal" (<= 2x median): a 333 ms hitch is the screen not
    # updating, not the unit speeding up, so it must not be read as jitter.
    dtsorted = sorted(dtMsCol)
    med = dtsorted[len(dtsorted) // 2]
    nrm = [speed[i] for i in range(1, len(g)) if dtMsCol[i] <= 2 * med]
    mNrm = mean(nrm)
    nrmCv = (stdev(nrm) / mNrm) if mNrm > 1e-12 else float("inf")
    nrmAc1 = autocorr1(nrm)

    st = {
        "rows": len(g),
        "frames": g[-1]["frame"] - g[0]["frame"] + 1,
        "travel": travel,
        "axis": axis,
        "meanStep": mabs,
        "stdStep": stdev(absS),
        "cv": (stdev(absS) / mabs) if mabs > 1e-12 else float("inf"),
        "maxStep": max(absS) if absS else 0.0,
        "minStep": min(big) if big else 0.0,
        "maxMinRatio": (max(absS) / min(big)) if big else float("inf"),
        "zeroFrac": zero_frac,
        "flipFrac": flip_frac,
        "autocorr1": autocorr1(steps),
        "arrSpeed": marr,
        "othSpeed": moth,
        "arrivalRatio": (marr / moth) if moth > 1e-9 else float("inf"),
        "nArr": len(arr), "nOth": len(oth),
        "steps": steps,
        "speed": speed,
        "speedMean": mSp,
        "speedCv": spCv,
        "speedNormCv": nrmCv,
        "speedNormAc1": nrmAc1,
        "speedNormMin": min(nrm) if nrm else 0.0,
        "speedNormMax": max(nrm) if nrm else 0.0,
        "speedNormRatio": (max(nrm) / min(nrm)) if nrm and min(nrm) > 1e-12 else float("inf"),
        "corrRateSpeed": pearson(rateCol, speed),
        "corrSnapSpeed": pearson(spanCol, speed[1:]) if spanCol else 0.0,
        "corrDtSpeed": pearson(dtMsCol, speed),
    }
    dts = [r["dtMs"] for r in g]
    st["dtMed"] = sorted(dts)[len(dts) // 2] if dts else 0.0
    st["dtMax"] = max(dts) if dts else 0.0
    if "rate" in g[0]:
        rs = [r["rate"] for r in g if r["rate"] == r["rate"]]
        st["rate"] = (min(rs), max(rs)) if rs else None
    if "t" in g[0]:
        ts = [r["t"] for r in g if r["t"] == r["t"]]
        st["t"] = (min(ts), max(ts)) if ts else None
    if "lagMs" in g[0]:
        ls = [r["lagMs"] for r in g if r["lagMs"] == r["lagMs"]]
        st["lag"] = (min(ls), max(ls)) if ls else None
    return st


def verdict_run(rm):
    """The three forms, judged on ONE continuous motion run (the only comparable unit of evidence)."""
    hits = []
    if rm["zeroFrac"] >= 0.15:
        hits.append("2-STAIRCASE(freeze frames)")
    if rm["flipFrac"] >= 0.15 and rm["autocorr1"] <= -0.20:
        hits.append("1-OSCILLATION")
    if rm["cvSpeed"] >= 0.15:
        hits.append("3-VELOCITY-RIPPLE")
    if not hits:
        return ("NONE (uniform: cv(speed)=%.4f < 0.15, zeroFrac=%.3f, signFlipFrac=%.3f)"
                % (rm["cvSpeed"], rm["zeroFrac"], rm["flipFrac"]))
    return " + ".join(hits)


def verdict(st):
    """The three forms must be separated; report which one fits and on what numbers.
    Judged on the SPEED series of normal-dt frames (a 333 ms hitch is the screen not
    updating, not the unit speeding up)."""
    hits = []
    if st["zeroFrac"] >= 0.40 and st["arrivalRatio"] >= 2.0:
        hits.append("2-STAIRCASE")
    if st["flipFrac"] >= 0.15 and st["autocorr1"] <= -0.20:
        hits.append("1-OSCILLATION")
    if st["speedNormCv"] >= 0.20:
        hits.append("3-VELOCITY-RIPPLE")
    if not hits:
        return ("NONE (speed is steady: zeroFrac=%.2f flipFrac=%.2f cv(speed,normal-dt)=%.3f ac1=%.2f)"
                % (st["zeroFrac"], st["flipFrac"], st["speedNormCv"], st["speedNormAc1"]))
    return " + ".join(hits)


def report(path, table_rows=0):
    rows = read_tsv(path)
    key, g = pick_series(rows)
    print("=" * 100)
    print("FILE %s" % path)
    print("  rows=%d  usable series=%s" % (len(rows), "no" if not g else "yes"))
    if not g:
        return None
    st = analyse(g)
    print("  series instId=%s dir=%s rows=%d frames=%d travel=%.2f (%s)" % (
        key[0], key[1], st["rows"], st["frames"], st["travel"],
        "screen px" if "refScreenX" in g[0] else "world grid"))
    print("  steps: mean=%.4f std=%.4f cv=%.3f  max=%.4f min(non-noise)=%.4f  max/min=%.2f" % (
        st["meanStep"], st["stdStep"], st["cv"], st["maxStep"], st["minStep"], st["maxMinRatio"]))
    print("  structure: zeroFrac=%.3f  signFlipFrac=%.3f  autocorr1=%.3f" % (
        st["zeroFrac"], st["flipFrac"], st["autocorr1"]))
    print("  arrival speed=%.4f/s vs other=%.4f/s  ratio=%.2f (n=%d/%d)" % (
        st["arrSpeed"], st["othSpeed"], st["arrivalRatio"], st["nArr"], st["nOth"]))
    print("  SPEED px/s: mean=%.2f  (normal-dt frames, n=%d) mean=%.2f cv=%.3f ac1=%.3f  min=%.2f max=%.2f max/min=%.2f" % (
        st["speedMean"], len(g) - 1, st["speedMean"], st["speedNormCv"], st["speedNormAc1"],
        st["speedNormMin"], st["speedNormMax"], st["speedNormRatio"]))
    print("  attrib: corr(clockRate,speed)=%.2f  corr(newSnapshotsInFrame,speed)=%.2f  corr(dtMs,speed)=%.2f" % (
        st["corrRateSpeed"], st["corrSnapSpeed"], st["corrDtSpeed"]))
    runs = moving_runs(g)
    if runs:
        rm = run_metrics(g, runs[0])
        print("  ── CLEAN MOTION RUN (the judged series: no teleport, no stop) ──")
        print("     rows=%d frames=%d travel=%.1f   units in file=%d, runs>=%d frames=%d"
              % (rm["rows"], rm["frames"], rm["travel"], len(rows), MIN_ROWS, len(runs)))
        print("     step: mean=%.5f std=%.5f   speed: mean=%.3f cv=%.4f min=%.3f max=%.3f max/min=%.3f zeroFrac=%.3f"
              % (rm["meanStep"], rm["stdStep"], rm["meanSpeed"], rm["cvSpeed"],
                 rm["speedMin"], rm["speedMax"], rm["maxMinRatio"], rm["zeroFrac"]))
        print("     structure: signFlipFrac=%.3f autocorr1(pos-step)=%.3f autocorr1(speed)=%.3f"
              % (rm["flipFrac"], rm["autocorr1"], rm["speedAutocorr1"]))
        print("     NORMAL-dt frames only (dtreal<=%.0fms; hitches=%.1f%% excluded - a 0.3~0.6s frame is the"
              " screen not updating): mean=%.3f cv=%.4f min=%.3f max=%.3f max/min=%.3f"
              % (HITCH_MS, 100.0 * rm["hitchCount"] / max(1, len(rm["dtms"])), rm["meanSpeedNorm"],
                 rm["cvSpeedNorm"], rm["minSpeedNorm"], rm["maxSpeedNorm"], rm["maxMinNorm"]))
        print("     VERDICT(clean run): %s" % verdict_run(rm))
        st["clean"] = rm
    print("  dtMs med=%.1f max=%.1f" % (st["dtMed"], st["dtMax"]))
    if st.get("rate"):
        print("  clockRate %.3f..%.3f   t %.3f..%.3f   lagMs %.1f..%.1f" % (
            st["rate"][0], st["rate"][1],
            st["t"][0] if st.get("t") else float("nan"), st["t"][1] if st.get("t") else float("nan"),
            st["lag"][0] if st.get("lag") else float("nan"),
            st["lag"][1] if st.get("lag") else float("nan")))
    print("  VERDICT: %s" % verdict(st))
    if table_rows:
        print("  frame  timeMs   dtMs  snaps   step    speed   rate      t    lagMs")
        for i in range(len(g)):
            r = g[i]
            s = st["steps"][i - 1] if i > 0 else 0.0
            v = (abs(s) / (r["dtMs"] / 1000.0)) if (i > 0 and r["dtMs"] > 1e-6) else 0.0
            print("  %5d %8.1f %6.1f %6d %7.4f %8.4f %6.3f %6.3f %7.1f" % (
                r["frame"], r["timeMs"], r["dtMs"], r["snaps"], s, v,
                r.get("rate", float("nan")), r.get("t", float("nan")), r.get("lagMs", float("nan"))))
    return st


def main(argv):
    args = argv[1:]
    table_rows = 0
    if args and args[0] == "--table":
        args = args[1:]
        table_rows = int(args[-1]) if args[-1].isdigit() else 40
        if args[-1].isdigit():
            args = args[:-1]
    if not args:
        print(__doc__)
        return 2
    for p in args:
        if not os.path.exists(p):
            print("missing input: %s" % p)
            return 2
        report(p, table_rows)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
