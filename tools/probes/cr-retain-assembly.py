# Freeze-instant assembly retention + per-chain baseline check.
#
# RULE ADOPTED FROM THE LEAD (my practice, now a project rule):
#   BEFORE re-running or inspecting another slice's artifact, PROVE your run cannot perturb it.
#   Here: nothing is written outside --dest, and index.tsv is appended with retries, never rewritten.
# RULE ADOPTED FROM MY OWN INCIDENT:
#   a negative control must run against a SCRATCH copy -- never against a real input.
#
# index.tsv is the SINGLE AUTHORITATIVE SOURCE of the baseline; no runner may hardcode it.
# Fields (terminal): sha16 | dll-file | bytes | frozen-at | declared-by | mtime-snapshot
#   mtime-snapshot is encoded name=ISO;name=ISO with '/'-separated repo-relative names, fixed order,
#   and is MECHANICALLY BUILT as the UNION of two sources -- never hand-written:
#     (1) product files this session changed (git status if this is a work tree, plus an mtime scan)
#     (2) the product files/dirs the registered judgement rows name as their object/dependency
#   Each entry carries a side tag (client|server|asset) in the printed list, because a cross-side
#   dependency that looks like a client dependency is how "change arena.go => conclusion dies" becomes
#   invisible.
import argparse
import datetime
import hashlib
import os
import re
import shutil
import subprocess
import sys
import time

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
SRC = os.path.join(ROOT, "client", "Library", "ScriptAssemblies", "CR.dll")
DEF_DEST = os.path.join(ROOT, "tools", "probes", "assemblies")
CEHUA = os.path.join(ROOT, "\u7b56\u5212")
SESSION_START = datetime.datetime(2026, 9, 23, 0, 0, 0)
DOC_EXTS = (".md", ".tsv", ".txt")
PAT_PRODUCT = re.compile(r"(?:client|server)/[A-Za-z0-9_./\-]+?\.(?:cs|go)\b")
PAT_ASSET_DIR = re.compile(r"client/Assets/[A-Za-z0-9_./\-]+/")


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as fh:
        for c in iter(lambda: fh.read(1 << 20), b""):
            h.update(c)
    return h.hexdigest()


def append_retry(path, line):
    for _ in range(8):
        try:
            with open(path, "a", encoding="utf-8", newline="\n") as fh:
                fh.write(line)
            return True
        except OSError:
            time.sleep(0.4)
    return False


STOPCHARS = "\u3002\uff0c\uff1b\uff1a\uff08\uff09\u3010\u3011\u300a\u300b\u2026\uff01\uff1f"
TREE_EXPAND_CAP = 40   # a cited tree with at most this many files is expanded to its leaves


def side_of(rel):
    n = rel.replace("\\", "/")
    if n.startswith("client/Assets/Resources/"):
        return "asset"
    if n.startswith("client/"):
        return "client"
    if n.startswith("server/"):
        return "server"
    if n.startswith("\u539f\u7248\u8d44\u6e90/"):     # original-game material tree
        return "asset"
    if n.startswith("\u7b56\u5212/"):                   # reference images / tables
        return "data"
    return "data"


# ------------------------------------------------------------------------------------------------
# DIRECTORY ENTRIES ARE CONTENT MANIFESTS, NOT MTIMES.
# CR-U5R measured the mechanism that forced this: rewriting a PNG in place leaves the PARENT DIRECTORY
# mtime unchanged, so a bare "dir=mtime" entry cannot witness "the material was edited => the conclusion
# dies".  A manifest hash does change.  The recipe below is THE single authoritative one (the lead ruled
# that a second implementation would be the next "two numbers for one fact" trap), and it is pinned in
# the index header:
#     entry name : <dir>/**            (recursive scope, stated explicitly)
#     entry value: #<file-count>#<sha256[:16]>
#     hash input : lines "<path-relative-to-the-subtree>:<size>:<mtime>" joined by "\n"
#                  mtime = yyyy-MM-ddTHH:mm:ss.fff  (LOCAL time, fraction TRUNCATED to milliseconds)
#     line order : case-INSENSITIVE ascending by absolute path (PowerShell's default string sort is
#                  culture/case-insensitive; an ordinal sort silently produces a different hash)
# Cross-check against CR-U5R's six published values: originally 4/6, and the residual was NOT a collation
# subtlety -- diffing its exported raw lines against mine reduced it to ONE line in ONE file:
#     mine   effects_sprite_456.png:5277:2026-09-20T00:36:06.401
#     theirs effects_sprite_456.png:5277:2026-09-20T00:36:06.400
# The filesystem says st_mtime_ns = 1789835766400999800 (400.9998 ms): .NET's 'fff' truncated to .400,
# while this implementation went through a FLOAT and rounded up to .401.  That was MY defect.  With the
# integer-nanosecond path all SIX reproduce (6/6) and the cross-implementation gap is closed.
# KNOWN BLIND SPOT (shared with file-level entries, so a manifest is not weaker than a file entry):
# a same-size in-place rewrite that also restores the mtime is invisible.
MANIFEST_MTIME_FORMAT = "%Y-%m-%dT%H:%M:%S."  # + milliseconds, truncated, from INTEGER nanoseconds


def manifest_of(path):
    rows = []
    for dp, _dn, fns in os.walk(path):
        for fn in fns:
            p = os.path.join(dp, fn)
            if not os.path.isfile(p):
                continue
            st = os.stat(p)
            # INTEGER NANOSECONDS, NEVER A FLOAT.  st_mtime is a float; datetime.fromtimestamp rounds it
            # to the nearest MICROsecond, and then //1000 can land one millisecond HIGH when the real
            # value sits just below a ms boundary.  Measured: effects_sprite_456.png has
            # st_mtime_ns = 1789835766400999800 (= 400.9998 ms, so truncation = .400, which is what .NET's
            # 'fff' produces) while the float path rendered .401 -- enough to make a 612-file hash differ.
            # This was MY defect, found by diffing CR-U5R's raw lines against mine; with the integer path
            # all six of its reference values reproduce (6/6), closing the cross-implementation gap.
            ns = st.st_mtime_ns
            iso = datetime.datetime.fromtimestamp(ns // 1_000_000_000)
            stamp = iso.strftime(MANIFEST_MTIME_FORMAT) + "%03d" % ((ns // 1_000_000) % 1000)
            rel = os.path.relpath(p, path).replace(os.sep, "/")
            rows.append((os.path.abspath(p).lower(), "%s:%d:%s" % (rel, st.st_size, stamp)))
    rows.sort(key=lambda x: x[0])
    h = hashlib.sha256("\n".join(l for _k, l in rows).encode("utf-8")).hexdigest()[:16]
    return len(rows), h


def entry_value(rel):
    """The VALUE side of one snapshot entry: ISO mtime for a file, #count#hash for a directory tree.
    The '/**' suffix is part of the NAME, not of the path -- forgetting to strip it made every directory
    entry resolve to a non-existent path and report MISSING (caught by running the dry run, not by reading
    the code)."""
    is_tree = rel.endswith("/**")
    base = rel[:-3] if is_tree else rel
    p = os.path.join(ROOT, base.replace("/", os.sep))
    if is_tree:
        if os.path.isdir(p):
            n, h = manifest_of(p)
            return "#%d#%s" % (n, h)
        return "MISSING"
    if os.path.isfile(p):
        return datetime.datetime.fromtimestamp(os.path.getmtime(p)).isoformat(timespec="seconds")
    return "MISSING"


def mechanical_snapshot():
    found = {}
    _tree_notes = set()

    def add(rel, tag):
        rel = rel.replace("\\", "/")
        p = os.path.join(ROOT, rel.replace("/", os.sep))
        if not os.path.exists(p):
            return
        if os.path.isdir(p):
            rel = rel.rstrip("/") + "/**"       # recursive scope, said out loud
        if rel not in found:
            found[rel] = (side_of(rel), tag)

    # source 1: session-changed product files
    git = subprocess.run(["git", "-C", ROOT, "status", "--porcelain"], capture_output=True, text=True,
                         encoding="utf-8", errors="replace")
    if git.returncode == 0 and git.stdout.strip():
        print("  source1: git status reports %d changed path(s)" % len(git.stdout.strip().splitlines()))
        for line in git.stdout.splitlines():
            rel = line[3:].strip().strip('"')
            if rel.endswith((".cs", ".go")) and rel.startswith(("client/", "server/")):
                add(rel, "session-git")
    else:
        print("  source1: not a usable git work tree (rc=%s) -> mtime scan only" % git.returncode)
    for base in ("client/Assets/Scripts", "server"):
        for dp, _dn, fns in os.walk(os.path.join(ROOT, base.replace("/", os.sep))):
            for fn in fns:
                if not fn.endswith((".cs", ".go")):
                    continue
                p = os.path.join(dp, fn)
                if datetime.datetime.fromtimestamp(os.path.getmtime(p)) >= SESSION_START:
                    add(os.path.relpath(p, ROOT), "session-mtime")

    # source 2: what the REGISTERED JUDGEMENT ROWS name as their object or dependency.
    # FIRST ATTEMPT WAS TOO WIDE (85 entries): scanning all 35 cehua docs picked up every product path
    # merely mentioned somewhere -- an evidence inventory, not a dependency list.  The registry is
    # cehua/<state-matrix>.tsv, whose column 3 IS the object column ("HudPanel.cs:718-732",
    # "server/game/core/arena.go:26,29").  So: column 3 of the registry, plus explicit path-form product
    # tokens, plus explicitly written asset TREES (".../**").
    docs = []
    for dp, _dn, fns in os.walk(CEHUA):
        for fn in fns:
            if fn.lower().endswith(DOC_EXTS):
                docs.append(os.path.join(dp, fn))
    registry = [p for p in docs if p.endswith(".tsv") and os.path.getsize(p) > 100000]
    print("  source2: registry candidate(s) = %s" % [os.path.basename(p) for p in registry])
    idx = {}
    for base in ("client", "server"):
        for dp, _dn, fns in os.walk(os.path.join(ROOT, base)):
            for fn in fns:
                if fn.endswith((".cs", ".go")):
                    idx.setdefault(fn.lower(), []).append(os.path.relpath(os.path.join(dp, fn), ROOT))
    rowtok = re.compile(r"[A-Za-z0-9_./\-]+\.(?:cs|go)")
    for p in registry:
        for line in open(p, encoding="utf-8", errors="replace").read().splitlines():
            f = line.split("\t")
            if len(f) < 3:
                continue
            obj = re.sub(r"\([^)]*\)", " ", f[2])
            for m in rowtok.finditer(obj):
                t = re.sub(r"(:\d+(-\d+)?)$", "", m.group(0))
                if "/" in t:
                    add(t, "registry-col3")
                else:
                    cands = idx.get(t.lower(), [])
                    if len(cands) == 1:
                        add(cands[0], "registry-col3")
                    elif cands:
                        print("    note: ambiguous registry name %s -> %s (taking all)" % (t, cands))
                        for c in cands:
                            add(c, "registry-col3")
    # ---- data side (cehua reference images/tables) and material side (原版资源 tree) ----
    # The lead ruled the extraction must NOT be limited by directory: change-a-sprite must be visible in
    # the snapshot exactly like change-a-server-file.  Note the FINDING that produced this: the two
    # arena_training sprites are NOT cited by name anywhere in cehua -- what is cited is the GLOB TREE
    # "原版资源/cr-assets-png/assets/sc/*".  So the tree is what goes into the snapshot: strictly broader,
    # still drift-visible, and no hand-written leaf names (which the lead forbade).
    data_rx = re.compile(r"\u7b56\u5212/[^\s`()\[\]{}<>|\"'" + STOPCHARS + r"]+\.(?:jpg|jpeg|png|xlsx|csv|json)")
    mat_rx = re.compile(r"\u539f\u7248\u8d44\u6e90/[^\s`()\[\]{}<>|\"'" + STOPCHARS + r"]*")
    data_index = {}
    for dp, _dn, fns in os.walk(CEHUA):
        for fn in fns:
            if fn.lower().endswith((".jpg", ".jpeg", ".png", ".xlsx", ".csv", ".json")):
                data_index.setdefault(fn.lower(), []).append(os.path.relpath(os.path.join(dp, fn), ROOT))
    print("  source2: cehua non-document asset index = %d name(s)" % len(data_index))
    data_bare_rx = re.compile(r"(?<![A-Za-z0-9_/\\\-])([A-Za-z0-9_\u4e00-\u9fff\-]+"
                              r"\.(?:jpg|jpeg|png|xlsx|csv|json))")
    for p in docs:
        txt = open(p, encoding="utf-8", errors="replace").read()
        for m in PAT_PRODUCT.finditer(txt):
            add(m.group(0), "declared-path")
        for m in data_rx.finditer(txt):
            add(re.sub(r"(:\d+(-\d+)?)$", "", m.group(0)), "declared-data")
        # The FINDING's reference image is cited as a BARE NAME (`03_对局_1320x2868.jpg`), without the
        # cehua/ prefix, so a prefix-only rule cannot see it -- and it is a dependency of a registered row.
        # Resolve bare data-asset names against the real files under cehua.  Look-behind excludes only
        # ASCII word chars and separators, so a CJK-preceded name is still matched.
        for m in data_bare_rx.finditer(txt):
            for c in data_index.get(m.group(1).lower(), []):
                add(c, "declared-data-bare")
        for m in mat_rx.finditer(txt):
            tok = re.sub(r"(:[\d\-]+|#L\d+)$", "", m.group(0)).rstrip("*/").rstrip("/")
            if "*" in tok:                      # a glob tree: watch its nearest real ancestor directory
                parts = tok.split("/")
                cut = []
                for seg in parts:
                    if "*" in seg:
                        break
                    cut.append(seg)
                tok = "/".join(cut)
            if not tok:
                continue
            full = os.path.join(ROOT, tok.replace("/", os.sep))
            if os.path.isdir(full):
                add(tok + "/", "declared-material-tree")
                # A cited tree whose leaf count is MODEST is also expanded to its leaves, so that a change
                # to one specific frame is visible BY NAME and not only through the tree's newest mtime.
                # 40 is the cap; the cited "assets/sc/*" glob (199 files) therefore stays a tree, while
                # arena_training_out/ (23 files) expands -- which is how the two sprites the lead named
                # enter the snapshot mechanically, with no hand-written leaf list.
                leaves = []
                for dp2, _dn2, fns2 in os.walk(full):
                    for fn2 in fns2:
                        leaves.append(os.path.relpath(os.path.join(dp2, fn2), ROOT))
                if len(leaves) <= TREE_EXPAND_CAP:
                    for lf in leaves:
                        add(lf, "declared-tree-leaf")
                else:
                    # deduplicated: the same tree is cited on many lines, and a note repeated dozens of
                    # times buries a note that appears once (which is the one worth reading)
                    if tok not in _tree_notes:
                        _tree_notes.add(tok)
                        print("    note: tree %s has %d files (> %d) -> kept as a tree only"
                              % (tok, len(leaves), TREE_EXPAND_CAP))
            elif os.path.isfile(full):
                add(tok, "declared-material")
        for m in re.finditer(r"client/Assets/[A-Za-z0-9_./\-]*\*\*", txt):
            add(m.group(0).replace("/**", "/"), "declared-tree")
        # asset TREES are also written relative ("Sprites/Ui/**"), which the client/Assets-prefixed rule
        # above cannot see.  FIRST FIX WAS WRONG: I concatenated "client/Assets/" + token, which produced
        # client/Assets/Sprites/Ui/ -- a path that does not exist, so add() silently skipped it and the
        # tree the lead named never entered the snapshot.  Resolve the token against the real trees.
        for m in re.finditer(r"\b((?:Resources/)?(?:Sprites|Sound|Prefabs|Fonts)|Resources/[A-Za-z0-9_]+)"
                             r"/[A-Za-z0-9_./\-]*\*\*", txt):
            tok = m.group(0).replace("/**", "/").strip("/")
            for cand in ("client/Assets/Resources/" + tok + "/",
                         "client/Assets/" + tok + "/",
                         "client/" + tok + "/"):
                if os.path.isdir(os.path.join(ROOT, cand.replace("/", os.sep))):
                    add(cand, "declared-tree")
                    break
    return found


ap = argparse.ArgumentParser()
ap.add_argument("--dest", default=DEF_DEST)
ap.add_argument("--src", default=SRC)
ap.add_argument("--declared-by", default="lead")
ap.add_argument("--check", action="store_true")
ap.add_argument("--refresh-row", action="store_true",
                help="append a corrected snapshot row for the already-retained assembly")
ap.add_argument("--note", default="snapshot recomputed after a rule fix (see the MANIFEST RECIPE comment "
                                  "and the CR-R1 report for what changed)",
                help="why this correction exists; recorded verbatim in the index comment line")
ap.add_argument("--dry-run", action="store_true")
a = ap.parse_args()

full = sha(a.src)
s16 = full[:16].upper()
size = os.path.getsize(a.src)
idx = os.path.join(a.dest, "index.tsv")

print("SOURCE  %s" % os.path.relpath(a.src, ROOT))
print("  sha256_16=%s  bytes=%d  mtime=%s" % (s16, size,
      datetime.datetime.fromtimestamp(os.path.getmtime(a.src)).strftime("%Y-%m-%d %H:%M:%S")))
ents = mechanical_snapshot()
rel_sorted = sorted(ents)
vals = {r: entry_value(r) for r in rel_sorted}
snap = ";".join("%s=%s" % (r, vals[r]) for r in rel_sorted)
n_files = sum(1 for r in rel_sorted if not r.endswith("/**"))
n_trees = len(rel_sorted) - n_files
print("SNAPSHOT mechanically built from two sources: %d entries (%d file(s) + %d directory manifest(s))"
      % (len(rel_sorted), n_files, n_trees))
for r in rel_sorted:
    tag = ents[r][1]
    if r.endswith("/**") and any(r.startswith(o[:-3]) and o != r for o in rel_sorted if o.endswith("/**")):
        tag += ", range INCLUDES child tree entries"
    print("    [%-6s] %-58s %-24s (%s)" % (ents[r][0], r, vals[r], tag))


def snapshot_now():
    return ";".join("%s=%s" % (r, entry_value(r)) for r in rel_sorted)


# NOTE ON HISTORY: the first rewrite of this tool DROPPED this whole branch, so "--check" fell through to
# the retention path, did nothing, and exited 0 -- a check that can never fail, which is the one defect
# this project keeps hunting.  It is here again, and it is exercised by the smoke suite.
if a.check:
    print("### per-chain baseline check ###")
    if not os.path.isfile(idx):
        print("  ASSEMBLY WARN: %s does not exist -- no baseline declared yet" % os.path.relpath(idx, ROOT))
        sys.exit(3)
    rows = [l.rstrip("\n").split("\t") for l in open(idx, encoding="utf-8")
            if l.strip() and not l.lstrip().startswith("#")]
    # Validate the FORMAT, not a fixed semicolon count.  An earlier version demanded count(";") == 5,
    # which was only true while the fixture had six files; at the real scale every row was declared
    # malformed.  A hard-coded 5 is exactly the "looks plausible, is wrong" constant this project hunts.
    # frozen-at must be NON-DECREASING across rows for the same sha16: if a later row carried an earlier
    # timestamp, "take the last row" would stop being the same as "take the newest snapshot".
    order_bad = []
    by_sha = {}
    for r in rows:
        if len(r) == 6:
            by_sha.setdefault(r[0].strip().upper(), []).append(r[3])
    for k, stamps in by_sha.items():
        if stamps != sorted(stamps):
            order_bad.append((k, stamps))
    if order_bad:
        print("  !! frozen-at NOT non-decreasing for %s" % order_bad)

    tree_re = re.compile(r"^#\d+#[0-9a-f]{16}$")

    def entry_bad(name, value):
        # The lead's ruling, verbatim: a directory entry MUST carry #count#hash; a bare "dir=mtime" is
        # invalid.  That is the whole point of the change -- a directory mtime does not move when a file
        # inside it is rewritten in place, so a bare dir entry cannot witness an edited asset.
        if name.endswith("/**"):
            return None if tree_re.match(value) else "directory entry without #count#hash"
        try:
            datetime.datetime.fromisoformat(value)
        except ValueError:
            return "non-ISO value for a file entry"
        return None

    def row_ok(r):
        if len(r) != 6:
            return False
        for part in r[5].split(";"):
            name, sep, stamp = part.rpartition("=")
            if not sep or not name:
                return False
            if entry_bad(name, stamp):
                return False
        return True

    # The FORMAT rule decides on the NEWEST row -- that is the row a reader consumes.  Older rows predate
    # the manifest rule and are simply history; they are still REPORTED (never silently skipped), but they
    # do not fail the check, otherwise every historical row would make the check permanently red.
    newest = rows[-1] if rows else None
    older = rows[:-1]
    malformed_newest = [] if newest is None else ([] if row_ok(newest) else [newest])
    malformed_older = [r for r in older if not row_ok(r)]
    known = set(r[0].strip().upper() for r in rows if r)
    base = newest
    print("  current sha256_16=%s ; index rows=%d ; distinct sha16=%d ; newest malformed=%d ; "
          "historical rows before the manifest rule=%d"
          % (s16, len(rows), len(known), len(malformed_newest), len(malformed_older)))
    for r in malformed_newest:
        for part in r[5].split(";"):
            name, sep, stamp = part.rpartition("=")
            why = entry_bad(name, stamp) if sep and name else "malformed key=value"
            if why:
                print("  !! NEWEST ROW invalid snapshot entry: %s (=%s) -> %s" % (name[:70], stamp[:24], why))
    for r in malformed_older:
        bad = []
        for part in r[5].split(";"):
            name, sep, stamp = part.rpartition("=")
            why = entry_bad(name, stamp) if sep and name else "malformed key=value"
            if why:
                bad.append(name[:60])
        print("  (history) frozen-at=%s predates the manifest rule: %d bare directory entry(ies), e.g. %s"
              % (r[3], len(bad), bad[0] if bad else ""))
    ok = (s16 in known) and not malformed_newest and not order_bad
    if base:
        print("  newest row: frozen-at=%s declared-by=%s entries=%d"
              % (base[3], base[4], base[5].count(";") + 1))
        # TWO DIFFERENT THINGS MUST NOT SHARE ONE VERDICT (the lead's ruling):
        #   DEPENDENCY DRIFT  -- a value of an EXISTING dependency entry changed (or it vanished).  That is
        #                        the question a reader actually asks: did the thing my conclusion rests on
        #                        move?  => exit 3.
        #   SNAPSHOT EXTENDED -- entries were ADDED (back-filling a dependency).  Nothing that already
        #                        existed changed, so a chain that ran on this baseline is still on it.
        #                        => exit 0, with the additions printed.
        # Mixing them made every append by me look like a drift, i.e. the index maintaining itself turned
        # into noise that told every slice it was no longer on the baseline.
        def as_map(s):
            out = {}
            for part in s.split(";"):
                k, sep, v = part.rpartition("=")
                if sep and k:
                    out[k] = v
            return out

        rec = as_map(base[5])
        live = as_map(snapshot_now())
        drift = sorted(k for k in rec if k in live and rec[k] != live[k])
        gone = sorted(k for k in rec if k not in live)
        added = sorted(k for k in live if k not in rec)
        if drift or gone:
            print("  DEPENDENCY DRIFT: %d changed + %d missing vs the newest row => the sources this baseline "
                  "rests on are NOT what they were; do not claim a frozen-baseline result" % (len(drift), len(gone)))
            for k in drift[:15]:
                print("    CHANGED %s : %s -> %s" % (k[:64], rec[k][:26], live[k][:26]))
            for k in gone[:15]:
                print("    MISSING %s" % k[:70])
            ok = False
        if added:
            print("  SNAPSHOT EXTENDED: +%d entries -- additions ONLY, no existing dependency value changed "
                  "=> a chain that ran on this baseline is still on it" % len(added))
            for k in added[:12]:
                print("    ADDED   %s" % k[:70])
    if not ok:
        print("  ASSEMBLY WARN: log all three readings (pre hash, post hash, index row) and report it")
        sys.exit(3)
    print("  OK: current assembly is a declared baseline and every snapshot entry is unchanged")
    sys.exit(0)

if a.dry_run:
    print("DRY RUN: nothing written.")
    sys.exit(0)


os.makedirs(a.dest, exist_ok=True)
dst = os.path.join(a.dest, "%s.dll" % s16)
action = "RETAINED"
if os.path.isfile(dst):
    existing = sha(dst)
    if existing == full:
        action = "ALREADY-RETAINED (identical bytes, nothing written)"
    else:
        print("!! COLLISION: %s exists with DIFFERENT content (copy sha256_16=%s) -- refusing to overwrite"
              % (os.path.relpath(dst, ROOT), existing[:16].upper()))
        sys.exit(3)
else:
    shutil.copy2(a.src, dst)
    back = sha(dst)
    if back != full:
        print("!! READ-BACK FAILED: copy sha256_16=%s != source %s" % (back[:16].upper(), s16))
        sys.exit(4)
    print("  read-back verified: copy sha256_16=%s" % back[:16].upper())

fields = [s16, os.path.basename(dst), str(size),
          datetime.datetime.now().isoformat(timespec="seconds"), a.declared_by, snap]
if len(fields) != 6 or fields[5].count(";") != len(rel_sorted) - 1:
    print("!! ROW SHAPE ASSERTION FAILED: fields=%d semicolons=%d (expected %d)"
          % (len(fields), fields[5].count(";"), len(rel_sorted) - 1))
    sys.exit(5)

if not os.path.isfile(idx):
    open(idx, "w", encoding="utf-8", newline="\n").write(
        "# retained judged assemblies -- SINGLE AUTHORITATIVE SOURCE OF THE BASELINE.  No runner may\n"
        "# hardcode the baseline; read it from this file.  Fields:\n"
        "#   sha16 | dll-file | bytes | frozen-at | declared-by | mtime-snapshot\n"
        "# mtime-snapshot = mechanically built UNION of (1) product files this session changed and\n"
        "# (2) product files/dirs named as object-or-dependency by the registered judgement docs.\n"
        "# Encoded repo-relative-name=ISO;... with '/' separators, fixed order.  Side tags (client |\n"
        "# server | asset | data) are printed by the tool and recorded in its report, not in this column.\n")
if a.refresh_row:
    # A CORRECTED ROW, appended rather than rewritten (this file is append-only by design).  Needed
    # because fixing the snapshot RULE changes the computed snapshot: without this, --check would report
    # a drift that is really my own rule change.
    last = None
    if os.path.isfile(idx):
        rr = [l.rstrip("\n").split("\t") for l in open(idx, encoding="utf-8")
              if l.strip() and not l.lstrip().startswith("#")]
        last = rr[-1][5] if rr else None
    if last == snap:
        print("REFRESH no change (computed snapshot equals the newest row); nothing appended")
    else:
        # READ RULE (the lead pinned it): rows are append-only, so a reader MUST take the LAST row by
        # frozen-at for a given sha16 -- taking the first would return the snapshot from before a
        # correction.  Older rows are marked superseded BY COMMENT rather than by rewriting them, because
        # rewriting would erase the very history append-only exists to keep.
        append_retry(idx, "# READ RULE: for a given sha256_16 read the LAST row by frozen-at; earlier rows "
                          "are superseded (kept on purpose, never rewritten)\n")
        if "MANIFEST RECIPE" not in open(idx, encoding="utf-8").read():
            append_retry(idx, "# MANIFEST RECIPE (pinned; this file is the ONLY implementation -- a second\n"
                              "# one would report two hashes for the same unchanged directory):\n"
                              "#   entry name  <dir>/**   recursive scope\n"
                              "#   entry value #<file-count>#<sha256[:16]>\n"
                              "#   hash input  lines '<path-relative-to-the-subtree>:<size>:<mtime>' joined by \\n\n"
                              "#   mtime       yyyy-MM-ddTHH:mm:ss.fff, LOCAL time, fraction TRUNCATED to ms\n"
                              "#   line order  case-INSENSITIVE ascending by absolute path (PowerShell's default\n"
                              "#               string sort is case/culture-insensitive; ordinal sort gives a\n"
                              "#               different hash for the same directory)\n"
                              "#   mtime source INTEGER NANOSECONDS (st_mtime_ns), never a float: the float path\n"
                              "#   rounds to the nearest microsecond and landed ONE MS HIGH on\n"
                              "#   effects_sprite_456.png (real 400.9998 ms -> .400), which alone made a\n"
                              "#   612-file hash differ.  That was this implementation's defect; with the\n"
                              "#   integer path CR-U5R's six reference values reproduce 6/6 and the gap is CLOSED.\n"
                              "#   known blind spot (shared with file entries): same-size rewrite + restored mtime.\n"
                              "#   A directory entry exists because a directory MTIME does not move when a file\n"
                              "#   inside it is rewritten in place (measured by CR-U5R).\n")
        append_retry(idx, "# superseded %s: every row above with sha256_16=%s is superseded by the row at "
                          "frozen-at=%s\n" % (fields[3], fields[0], fields[3]))
        append_retry(idx, "# correction %s: %s\n"
                          % (fields[3], a.note))
        append_retry(idx, "\t".join(fields) + "\n")
        print("REFRESH appended a corrected row (%d entries, was %d)"
              % (len(rel_sorted), 0 if last is None else last.count(";") + 1))
    sys.exit(0)

okidx = append_retry(idx, "\t".join(fields) + "\n") if not action.startswith("ALREADY-RETAINED") else \
    "skipped (already retained)"
print("ACTION  %s" % action)
print("INDEX   %s  appended=%s" % (os.path.relpath(idx, ROOT), okidx))
print("NEXT    promote this tool to tools/probes/cr-retain-assembly.py, then run tools/verify.ps1 once; "
      "if it adds ANY new failure, report it -- no exceptions.")
