#!/usr/bin/env python3
"""CR-T4 judged asset - offline simulator of BattleViewRoot's interpolation clock.

WHY
---
The render clock is arithmetic: clock = base + (real - baseReal) * 1000 * rate, and
rate = SteerRate(err) with err = (ServerNow - TargetLagMs) - clock. That is a few lines of
pure math whose OUTPUT is the per-frame position of every unit. Running it here costs
milliseconds, so the candidate fix can be checked (and rejected) BEFORE any edit, and the
same run explains the numbers seen in the real capture.

WHAT IT REPRODUCES
------------------
Inputs: server snapshot interval (with jitter), frame time (with hitches), network arrival
delay, unit speed. Server side: a unit moving at a constant speed, sampled every interval --
i.e. the ground truth is perfectly uniform motion, so ANY variation in the rendered speed is
produced by the client clock, not by the game.

Variants (--variant, repeatable):
  current      exact current code: clockRate steered every frame by SteerRate(err),
               clamped to [0.85, 1.15] in the steady band, catch-up band beyond 300 ms.
  rate1        clock follows ServerNow - TargetLagMs with rate EXACTLY 1 (no per-frame
               modulation); a bounded catch-up only while |err| > 300 ms.

Reported per variant: mean/median rendered speed, speed CV, speed max/min, zero-speed frame
fraction, lag range, t range, and the mean forward hop at snapshot boundaries. Judge: a
uniformly moving unit must have speed CV ~ 0 and t sweeping 0..1 every interval.

Usage:
  python tools/probes/jitter-sim.py                       # all variants, default scenario
  python tools/probes/jitter-sim.py --variant current --frames 900 --snap 100 --fps 30
ASCII-only output on purpose.
"""

import math
import random
import sys

# ── constants mirrored from client/Assets/Scripts/View/BattleViewRoot.cs (cite in report) ──
SNAP_INTERVAL_MS = 100.0     # GameConst.SnapshotIntervalMs
TARGET_LAG_MS = SNAP_INTERVAL_MS          # BattleViewRoot.TargetLagMs (:221)
LAG_STEER_GAIN = 0.5                      # :227
LAG_STEER_MAX_RATE = 0.15                 # :233
CATCH_UP_THRESHOLD_MS = 3.0 * SNAP_INTERVAL_MS   # :209
CATCH_UP_MAX_RATE = 1.0                   # :248


def steer_rate(err):
    """Verbatim mirror of BattleViewRoot.SteerRate (client .../View/BattleViewRoot.cs:193)."""
    if abs(err) > CATCH_UP_THRESHOLD_MS:
        return 1.0 + CATCH_UP_MAX_RATE if err > 0 else 1.0 - CATCH_UP_MAX_RATE * 0.5
    r = 1.0 + err / max(1.0, TARGET_LAG_MS) * LAG_STEER_GAIN
    return min(max(r, 1.0 - LAG_STEER_MAX_RATE), 1.0 + LAG_STEER_MAX_RATE)


def millis_to_world(mm):
    """Unit speed: 1 tile per second == 0.001 tile per ms."""
    return mm * 0.001


HIST_SLOTS = 4


def simulate(variant, frames=900, fps=30.0, snap_ms=SNAP_INTERVAL_MS,
             jitter_ms=0.0, delay_ms=0.0, hitch_every=0, hitch_ms=333.0, speed=1.0,
             seed=7, dt_list=None):
    """Returns a dict of metrics + the raw series. `speed` = tiles/second of the server unit.

    Variant `lookup` = the candidate fix: the interpolation window is picked from a small
    snapshot history **by clock position** (so the window only changes when the clock crosses a
    timestamp, never on packet arrival) with a two-interval render lag (so the clock is always
    strictly inside a window and a late/early arrival cannot push it out of one).
    """
    rng = random.Random(seed)
    frame_ms = 1000.0 / fps

    # ── server: snapshot k has timestamp k*snap_ms and position = speed * t (uniform motion) ──
    # ── network: it reaches the client at (timestamp + delay + jitter) in real time ──
    snaps = []            # (server_ms, arrival_real_ms, world_pos)
    k = 0
    while True:
        s_ms = k * snap_ms
        if s_ms > frames * frame_ms + 4 * snap_ms:
            break
        arr = s_ms + delay_ms + (rng.uniform(-jitter_ms, jitter_ms) if jitter_ms > 0 else 0.0)
        snaps.append((s_ms, arr, speed * s_ms / 1000.0))
        k += 1

    # ── client state (same fields as BattleViewRoot) ──
    clock_base_ms = float("nan")
    clock_base_real = 0.0
    rate = 1.0
    hist_ms = [0.0] * HIST_SLOTS          # [0] oldest .. [HIST_SLOTS-1] newest
    hist_pos = [0.0] * HIST_SLOTS
    hist_len = 0
    prev_ms = curr_ms = 0.0
    arrival_real = 0.0
    has_prev = False
    next_snap = 0
    last_pos = None
    rows = []

    lag_ms = SNAP_INTERVAL_MS if variant == "lookup" else TARGET_LAG_MS

    cum = None
    if dt_list:
        cum = [0.0]
        for v in dt_list:
            cum.append(cum[-1] + v)

    for i in range(frames + 1):
        if cum:
            real = cum[min(i, len(cum) - 1)]
        else:
            real = i * frame_ms
            if hitch_every and i > 0 and i % hitch_every == 0:
                real += hitch_ms              # Unity clamps dt to maximumDeltaTime (0.333 s)
        dt_this = (real - rows[-1]["real"]) if rows else frame_ms

        while next_snap < len(snaps) and snaps[next_snap][1] <= real:
            s_ms, arr, pos = snaps[next_snap]
            arrival_real = arr
            next_snap += 1
            if variant == "lookup":
                # push into the history (drop the oldest)
                for j in range(HIST_SLOTS - 1):
                    hist_ms[j] = hist_ms[j + 1]
                    hist_pos[j] = hist_pos[j + 1]
                hist_ms[HIST_SLOTS - 1] = s_ms
                hist_pos[HIST_SLOTS - 1] = pos
                hist_len = min(hist_len + 1, HIST_SLOTS)
                if math.isnan(clock_base_ms):
                    clock_base_ms = s_ms - 2 * lag_ms
                    clock_base_real = real
                    rate = 1.0
            else:
                prev_ms = curr_ms
                curr_ms = s_ms
                has_prev = True
                if math.isnan(clock_base_ms):
                    clock_base_ms = curr_ms - TARGET_LAG_MS
                    clock_base_real = real
                    rate = 1.0
        if math.isnan(clock_base_ms):
            continue

        clock = clock_base_ms + (real - clock_base_real) * rate

        if variant == "lookup":
            newest = hist_ms[HIST_SLOTS - 1]
            # recovery: only a catastrophic gap may move the clock (bounded, and logged in the
            # runtime); the steady state never modulates the rate.
            if clock > newest:
                clock_base_ms = newest - 2 * lag_ms
                clock_base_real = real
                rate = 1.0
                clock = clock_base_ms
            lo = HIST_SLOTS - hist_len
            pi = ci = HIST_SLOTS - 1
            if hist_len >= 2:
                for j in range(lo, HIST_SLOTS - 1):
                    if hist_ms[j] <= clock <= hist_ms[j + 1]:
                        pi, ci = j, j + 1
                        break
                    if clock > hist_ms[j + 1]:
                        pi, ci = j, j + 1
            span = max(1.0, hist_ms[ci] - hist_ms[pi])
            t = min(max((clock - hist_ms[pi]) / span, 0.0), 1.0) if hist_len >= 2 else 1.0
            pos = hist_pos[pi] * (1.0 - t) + hist_pos[ci] * t
            prev_ms, curr_ms = hist_ms[pi], hist_ms[ci]
            err = (newest - lag_ms) - clock
        else:
            server_now = curr_ms + (real - arrival_real)
            target = server_now - TARGET_LAG_MS
            err = target - clock
            new_rate = 1.0
            if variant == "rate1":
                if abs(err) > CATCH_UP_THRESHOLD_MS:
                    new_rate = 2.0 if err > 0 else 0.5
            else:
                new_rate = steer_rate(err)
            if not _approx(new_rate, rate):
                clock_base_ms = clock
                clock_base_real = real
                rate = new_rate
            clock = clock_base_ms + (real - clock_base_real) * rate
            if clock > curr_ms:
                clock = curr_ms
            span = max(1.0, curr_ms - prev_ms)
            t = min(max((clock - prev_ms) / span, 0.0), 1.0) if has_prev else 1.0
            pos = (millis_to_world(prev_ms) * (1.0 - t) + millis_to_world(curr_ms) * t) if has_prev else millis_to_world(curr_ms)

        step = 0.0 if last_pos is None else pos - last_pos
        last_pos = pos
        rows.append({"i": i, "real": real, "curr": curr_ms, "clock": clock, "err": err,
                     "rate": rate, "t": t, "pos": pos, "step": step,
                     "dt": dt_this,
                     "lag": curr_ms - clock})
    return metrics(rows)


def _approx(a, b):
    return abs(a - b) <= max(1e-6 * max(abs(a), abs(b)), 1e-9)


WARMUP_FRAMES = 15          # the first frames are the one-off alignment transient, not steady state


def metrics(rows):
    if len(rows) > 4 * WARMUP_FRAMES:
        rows = rows[WARMUP_FRAMES:]
    steps = [abs(r["step"]) for r in rows]
    speeds = [steps[i] / (rows[i]["dt"] / 1000.0) if rows[i]["dt"] > 1e-6 else 0.0
              for i in range(1, len(rows))]
    m = sum(speeds) / len(speeds) if speeds else 0.0
    sd = math.sqrt(sum((v - m) ** 2 for v in speeds) / (len(speeds) - 1)) if len(speeds) > 1 else 0.0
    nz = [v for v in speeds if v > 1e-9]
    t = [r["t"] for r in rows[1:]]
    lag = [r["lag"] for r in rows]
    rates = [r["rate"] for r in rows]
    dts = sorted(r["dt"] for r in rows)
    med_dt = dts[len(dts) // 2]
    nrm = [speeds[i] for i in range(len(speeds)) if rows[i + 1]["dt"] <= 2 * med_dt]
    mn = sorted(nrm)
    # forward hop at snapshot boundaries: step on the frame that consumed a snapshot vs the rest
    arr, oth = [], []
    for i in range(1, len(rows)):
        if rows[i]["dt"] <= 2 * med_dt:
            if rows[i]["curr"] != rows[i - 1]["curr"]:
                arr.append(steps[i] / (rows[i]["dt"] / 1000.0))
            else:
                oth.append(steps[i] / (rows[i]["dt"] / 1000.0))
    marr = sum(arr) / len(arr) if arr else 0.0
    moth = sum(oth) / len(oth) if oth else 0.0
    mean_nrm = sum(nrm) / len(nrm) if nrm else 0.0
    sd_nrm = (math.sqrt(sum((v - mean_nrm) ** 2 for v in nrm) / (len(nrm) - 1)) if len(nrm) > 1 else 0.0)
    return {
        "rows": rows,
        "meanSpeed": m, "speedCv": (sd / m if m > 1e-12 else float("inf")),
        "normalSpeedCv": (sd_nrm / mean_nrm if mean_nrm > 1e-12 else float("inf")),
        "speedMin": min(nz) if nz else 0.0, "speedMax": max(nz) if nz else 0.0,
        "normalMin": mn[0] if mn else 0.0, "normalMax": mn[-1] if mn else 0.0,
        "normalRatio": (mn[-1] / mn[0]) if mn and mn[0] > 1e-12 else float("inf"),
        "zeroFracN": (sum(1 for v in nrm if v <= 1e-9) / len(nrm)) if nrm else 0.0,
        "tMin": min(t), "tMax": max(t),
        "lagMin": min(lag), "lagMax": max(lag),
        "rateMin": min(rates), "rateMax": max(rates),
        "arrSpeed": marr, "othSpeed": moth,
        "arrRatio": (marr / moth) if moth > 1e-9 else float("inf"),
        "steps": steps,
    }


def main(argv):
    args = argv[1:]
    variants = []
    dt_file = None
    frames, fps, snap, jitter, delay, hitch_every, speed = 900, 30.0, 100.0, 0.0, 0.0, 0, 1.0
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--variant":
            variants.append(args[i + 1]); i += 2
        elif a == "--dt-file":
            dt_file = args[i + 1]; i += 2
        elif a == "--frames":
            frames = int(args[i + 1]); i += 2
        elif a == "--fps":
            fps = float(args[i + 1]); i += 2
        elif a == "--snap":
            snap = float(args[i + 1]); i += 2
        elif a == "--jitter":
            jitter = float(args[i + 1]); i += 2
        elif a == "--delay":
            delay = float(args[i + 1]); i += 2
        elif a == "--hitch-every":
            hitch_every = int(args[i + 1]); i += 2
        elif a == "--speed":
            speed = float(args[i + 1]); i += 2
        else:
            print("unknown arg %s" % a); return 2
    if not variants:
        variants = ["current", "rate1", "lookup"]

    dt_list = None
    if dt_file:
        dt_list = _read_dt_column(dt_file)
        frames = min(frames, len(dt_list) - 1)

    print("scenario: frames=%d fps=%.1f snap=%.0fms jitter=+-%.0fms delay=%.0fms hitch_every=%d(%.0fms) speed=%.2f tile/s%s"
          % (frames, fps, snap, jitter, delay, hitch_every, 333.0, speed,
             ("  dt from " + dt_file if dt_file else "")))
    print("ground truth: the server unit moves at a CONSTANT speed -> any speed variation below is client-made")
    print()
    for v in variants:
        st = simulate(v, frames=frames, fps=fps, snap_ms=snap, jitter_ms=jitter,
                      delay_ms=delay, hitch_every=hitch_every, speed=speed, dt_list=dt_list)
        print("[%s]" % v)
        print("  speed: mean=%.4f  cv(normal-dt)=%.4f  min=%.4f max=%.4f  max/min=%.3f  zeroFrac=%.3f"
              % (st["meanSpeed"], st["normalSpeedCv"], st["normalMin"], st["normalMax"],
                 st["normalRatio"], st["zeroFracN"]))
        print("  clock: rate %.3f..%.3f   lagMs %.1f..%.1f   t %.3f..%.3f   arrival/other speed ratio=%.2f"
              % (st["rateMin"], st["rateMax"], st["lagMin"], st["lagMax"], st["tMin"], st["tMax"], st["arrRatio"]))
        print("  frames with zero motion: %.1f%%   (uniform motion must be 0%%)"
              % (100.0 * sum(1 for s in st["steps"][1:] if s <= 1e-9) / len(st["steps"])))
    return 0


def _read_dt_column(path):
    """Read the frame-time column (`dtMs`) out of a FlowProbe.SampleFlow TSV, so the simulator can
    be driven by the REAL frame pacing of a captured session instead of an idealised 60 FPS."""
    dts, header = [], None
    with open(path, encoding="utf-8-sig") as fh:
        for line in fh:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            parts = line.split("\t")
            if header is None:
                header = parts
                continue
            try:
                i = header.index("dtMs")
            except ValueError:
                return []
            if len(parts) != len(header):
                continue
            dts.append(float(parts[i]))
    # keep the pace of ONE entity, not of every row (the TSV has one row per entity per frame)
    return dts


if __name__ == "__main__":
    sys.exit(main(sys.argv))
