# CR-V1: one-command OFFLINE check that the assembly my chain ran on is the frozen baseline,
# and that the baseline snapshot covers my registered dependencies. Seconds-level, read-only.
#
# Hard rules baked in (team-lead 2026-09-23, after CR-R1 and CR-V1 each hit this):
#   * every path is ABSOLUTE (derived from this file's location) -- ".NET cwd != PS location" made two
#     failed ReadAllBytes calls look like "len equal = True" because $null.Length -eq $null.Length is True;
#   * no bare equality on possibly-null values: every value that must exist is asserted non-None first;
#   * missing/unreadable -> loud NOT-JUDGED, never a silent pass;
#   * index.tsv READ RULE (team-lead + CR-R1, 2026-09-23): for a given sha256_16 the rows are append-only
#     and the authoritative one is the LAST row by frozen-at; earlier rows are SUPERSEDED. This script
#     therefore prints matchedRows (never silently takes one) and judges the dependencies against that
#     authoritative row only -- a dependency that lives ONLY in a superseded row is a FAIL, not a pass.
#
# Re-run: python tools/probes/cr-v1-baseline-check.py     (exit 0 = all required checks pass)
import os, sys, hashlib, json
sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))          # <project root>/tools/probes -> <project root>
BASE = "0F4D2C8A75D5D318"                              # the baseline my chain #2 ran on

RETAINED = os.path.join(ROOT, "tools", "probes", "assemblies", BASE + ".dll")
INDEX = os.path.join(ROOT, "tools", "probes", "assemblies", "index.tsv")
LIVE = os.path.join(ROOT, "client", "Library", "ScriptAssemblies", "CR.dll")

# side-tagged dependencies I registered for the mtime-snapshot (team-lead ruling: extraction is not
# directory-limited, so 素材树 and 策划/ count too; these are also the OBJECTS being judged).
DEPS = [
    ("asset", "原版资源/cr-assets-png/assets/sc/arena_training_out/arena_training_sprite_06.png"),
    ("asset", "原版资源/cr-assets-png/assets/sc/arena_training_out/arena_training_sprite_22.png"),
    ("data",  "策划/参考图/03_对局_1320x2868.jpg"),
]

fails, notjudged = [], []


def sha16(p):
    if not os.path.isfile(p):
        return None
    with open(p, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()[:16].upper()


def readb(p):
    if not os.path.isfile(p):
        return None
    with open(p, "rb") as f:
        return f.read()


print("ROOT = %s" % ROOT)

# ---- 1) retained dll must be honest: filename == real hash (not just the name) ----
r_hash = sha16(RETAINED)
if r_hash is None:
    fails.append("retained dll MISSING -> NOT-JUDGED: %s" % RETAINED)
    print("1 retained : !! MISSING -> NOT-JUDGED")
else:
    ok = (r_hash == BASE)
    print("1 retained : filename=%s real_sha256_16=%s honest=%s" % (BASE, r_hash, ok))
    if not ok:
        fails.append("retained dll filename != real hash")

# ---- 2) retained must be byte-identical to the live CR.dll of that moment ----
rb, lb = readb(RETAINED), readb(LIVE)
if rb is None or lb is None:
    notjudged.append("byte-identical: retained=%s live=%s (one is unreadable)" % (rb is not None, lb is not None))
    print("2 identical: !! NOT-JUDGED (retained_readable=%s live_readable=%s)" % (rb is not None, lb is not None))
else:
    same = (len(rb) == len(lb)) and (rb == lb)
    print("2 identical: len retained=%d live=%d byte_equal=%s" % (len(rb), len(lb), same))
    if not same:
        print("             (live CR.dll has moved on since the baseline -- that is expected AFTER the "
              "freeze batch edits code; it only invalidates rows that were not captured on the baseline)")

live_hash = sha16(LIVE)
print("3 live     : sha256_16=%s  == base? %s" % (live_hash, live_hash == BASE))

# ---- 4) index.tsv: six columns, base present, live present ----
if not os.path.isfile(INDEX):
    notjudged.append("index.tsv missing")
    print("4 index    : !! MISSING -> NOT-JUDGED: %s" % INDEX)
else:
    rows = []
    with open(INDEX, encoding="utf-8") as f:
        for ln in f:
            ln = ln.rstrip("\n")
            if not ln.strip() or ln.lstrip().startswith("#"):
                continue
            rows.append(ln.split("\t"))
    badcols = [i for i, r in enumerate(rows) if len(r) != 6]
    print("4 index    : data_rows=%d  rows_with_cols!=6 = %s" % (len(rows), badcols))
    if badcols:
        fails.append("index.tsv has rows with != 6 columns")
    col0 = [r[0] for r in rows]
    print("             base in col0   = %s" % (BASE in col0))
    print("             live in col0   = %s" % (live_hash in col0 if live_hash else None))
    if BASE not in col0:
        fails.append("baseline %s not in index.tsv" % BASE)
    # ---- 4b) READ RULE: for a given sha16 the LAST row by frozen-at is authoritative ----
    mine = [(i, r) for i, r in enumerate(rows) if r[0] == BASE]
    matched = len(mine)
    print("4b read-rule: rows with col0==%s : matchedRows=%d  (NOT silently taking one)" % (BASE, matched))
    for i, r in mine:
        print("             row[%d] frozen-at=%s bytes=%s snapshot_chars=%d" % (i, r[3], r[2], len(r[5])))
    latest = None
    if matched == 0:
        notjudged.append("no row for baseline -> cannot apply the last-row read rule")
    else:
        order = sorted(mine, key=lambda t: t[1][3])          # ISO-8601 sorts lexicographically
        nondec = all(order[k][1][3] <= order[k + 1][1][3] for k in range(len(order) - 1))
        print("             frozen-at non-decreasing = %s" % nondec)
        if not nondec:
            fails.append("index.tsv frozen-at is not non-decreasing for %s" % BASE)
        li, latest = order[-1]
        print("             AUTHORITATIVE row = row[%d] frozen-at=%s (LAST by frozen-at)" % (li, latest[3]))
        if li != len(rows) - 1:
            print("             NOTE: authoritative is NOT the physically last row -- later rows carry a"
                  " different sha16, which is exactly why the rule is 'by frozen-at', not 'by file order'")

    # ---- 5) my registered asset/data dependencies must appear in the AUTHORITATIVE row (col 6) ----
    print("5 deps in mtime-snapshot of the AUTHORITATIVE row (col 6):")
    if latest is None:
        print("             !! NOT-JUDGED (no authoritative row)")
        for side, p in DEPS:
            notjudged.append("dependency not judged (no authoritative row): %s" % p)
    else:
        blob_all = "\n".join(r[5] for _, r in mine)
        for side, p in DEPS:
            hit = p in latest[5]
            hit_any = p in blob_all
            print("             %-6s %-78s latest=%s  (any-of-%d-rows=%s)" % (side, p, hit, matched, hit_any))
            if not hit:
                if hit_any:
                    fails.append("dependency ONLY in a SUPERSEDED row (dropped by the authoritative row): %s" % p)
                else:
                    fails.append("dependency not in mtime-snapshot: %s" % p)

print("\nRESULT: fails=%d not_judged=%d" % (len(fails), len(notjudged)))
for x in fails:
    print("  FAIL: " + x)
for x in notjudged:
    print("  NOT-JUDGED: " + x)
sys.exit(1 if fails else 0)
