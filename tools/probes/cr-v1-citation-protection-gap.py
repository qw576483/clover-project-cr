#!/usr/bin/env python3
# -*- coding: ascii -*-
"""
CR-V1 citation-protection-gap census  --  DISCREPANCY PROBE, not a producer.

WHAT IT ASKS (one question): a script name that a document under the planning dir cites BY BARE
NAME, and that
resolves to an existing file INSIDE the cleanup zone, is it present in the published
cited-evidence whitelist?  If not, the cleanup will delete a file a delivery doc names.

WHY THIS EXISTS: the whitelist generator's bare-name branch gates on $extOk, which does NOT contain
py/ps1, while its PATH branch gates only on the '.ai-tmp/' prefix and never looks at the extension.
Same file type, two shapes -> one half is invisible.  Measured 2026-09-23 13:5x: 16 such names.
The 4th landing was approved to fix it; THIS script is how the fix is checked ("16 -> 0") instead of
asserted.

WHAT IT IS NOT: it never writes the whitelist, never hashes any judged object, and does not
re-implement the whitelist's VALUE side.  It is the other side of a two-source disagreement.

FAIL-CLOSED DESIGN (the reason the number can be trusted):
  * PATH-FORM POSITIVE CONTROL is computed in the same run: every path-form citation that RESOLVES
    to an existing in-zone file must be in-register.  If that control does not come out clean, this
    census prints NOT-JUDGED and refuses to report a gap count (a broken extractor must not look
    like a clean sweep).
  * --selftest plants a real gap in a temp zone, asserts it is FOUND, then protects it and asserts it
    is GONE: the probe must be able to BOTH fire and stay silent.
  * self-anchoring: every run prints this file's own triple plus the whitelist's triple + AS-OF.

USAGE:
    python tools/probes/cr-v1-citation-protection-gap.py                 # census
    python tools/probes/cr-v1-citation-protection-gap.py --expect-gap 0  # exit 3 unless gaps == 0
    python tools/probes/cr-v1-citation-protection-gap.py --selftest      # negative control
"""
import os
import re
import sys
import hashlib
import shutil
import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
WHITELIST = os.path.join(ROOT, "tools", "probes", "cited-evidence-whitelist-all.txt")
DOCS_DIR = os.path.join(ROOT, "\u7b56\u5212")          # the planning dir, spelled without escapes
ZONE_DIR = os.path.join(ROOT, ".ai-tmp")
ZONE_RE = re.compile(r"^\.ai-tmp[\\/]")
GENERATOR = os.path.join(ROOT, "tools", "probes", "cited-evidence-whitelist.ps1")
EXTOK_RE = re.compile(r"\$extOk\s*=\s*@\(([^)]*)\)")


def read_extok():
    """Read $extOk FROM THE GENERATOR SOURCE at run time -- ONE source of truth, never copied here.
    If this file's copy of the rule ever drifts from the generator's, that drift would be invisible;
    reading it means the fix makes this probe's second section go empty BY ITSELF."""
    if not os.path.isfile(GENERATOR):
        return None
    with open(GENERATOR, encoding="utf-8", errors="replace") as f:
        text = f.read()
    m = EXTOK_RE.search(text)
    if not m:
        return None
    out = set()
    for x in m.group(1).split(","):
        x = x.strip().strip("'\"").lower()
        if x:
            out.add(x)
    return out
SCRIPT_EXT = ("py", "ps1", "cs")

# bare-name token: no slash inside; the generator's own bare branch shape, one extension family wider
BARE_RE = re.compile(r"(?<![A-Za-z0-9_\-./\\])([A-Za-z0-9_][A-Za-z0-9_\-.]*\.(?:" + "|".join(SCRIPT_EXT) + r"))(?![A-Za-z0-9_])")
# path-form token: '.ai-tmp/...' is the ONLY path shape the generator extracts
PATH_RE = re.compile(r"\.ai-tmp/[^\s`|<>\"']+")
# a citation may hang a line/range suffix on the path (`.txt:144-166`, `.txt:2,8,9`, `.py:29(<junk>`);
# recognisable file endings, used to decide whether the head before ':' is a real path
PATH_EXT_RE = re.compile(r"\.(?:py|ps1|cs|txt|tsv|json|md|log|out|png|jpg|jpeg|dll|ogg|asset|unity|meta|ini|xml)$")


def sha16(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()[:16].upper()


def triple(path):
    if not os.path.isfile(path):
        return (os.path.relpath(path, ROOT).replace(os.sep, "/"), "ABSENT", -1, "-")
    return (os.path.relpath(path, ROOT).replace(os.sep, "/"), sha16(path), os.path.getsize(path),
            datetime.datetime.fromtimestamp(os.path.getmtime(path)).strftime("%Y-%m-%d %H:%M:%S"))


def load_whitelist(wl_path):
    """Return the SET of whitelist DATA entries, normalized to forward slashes."""
    if not os.path.isfile(wl_path):
        return None
    out = set()
    with open(wl_path, encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if not s or s.startswith("#"):
                continue
            out.add(s.replace("\\", "/"))
    return out


def load_docs(docs_dir):
    docs = []
    for dirpath, _d, filenames in os.walk(docs_dir):
        for fn in filenames:
            if fn.lower().endswith((".md", ".tsv")):
                docs.append(os.path.join(dirpath, fn))
    return sorted(docs)


def zone_index(zone_dir):
    """basename -> [rel-paths] for every file inside the cleanup zone."""
    idx = {}
    for dirpath, _d, filenames in os.walk(zone_dir):
        for fn in filenames:
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, ROOT).replace(os.sep, "/")
            if not ZONE_RE.match(rel):
                continue
            idx.setdefault(fn, []).append(rel)
    return idx


def census(wl_set, docs, zidx):
    """Return a dict with bare tokens, gaps, and the path-form positive control."""
    bare = {}          # token -> set(doc names)
    path = {}          # token -> set(doc names)
    for d in docs:
        try:
            with open(d, encoding="utf-8") as f:
                text = f.read()
        except (OSError, UnicodeDecodeError) as e:
            return {"read_error": "%s: %s" % (os.path.basename(d), e)}
        base = os.path.basename(d)
        for m in BARE_RE.finditer(text):
            tok = m.group(1)
            if "/" in tok or "\\" in tok:
                continue
            bare.setdefault(tok, set()).add(base)
        for m in PATH_RE.finditer(text):
            path.setdefault(m.group(0), set()).add(base)

    gaps = {}
    for tok, srcs in bare.items():
        cands = zidx.get(tok, [])
        if not cands:
            continue                                     # nothing to protect
        if all(c not in wl_set for c in cands):
            gaps[tok] = (sorted(srcs), sorted(cands))

    # positive control: path-form citations that RESOLVE to an existing in-zone file must be in-register
    #
    # !! LINE/RANGE SUFFIXES, and a Windows trap that fooled this probe's own first version:
    # the docs cite paths as `.ai-tmp/test/x.txt:144-166` / `:13` / `:2,8,9` / `:29(<junk>`.
    # Stripping only trailing punctuation left the `:NN` attached, and `os.path.isfile()` then
    # returned TRUE anyway, because on Windows `path:stream` is parsed as an NTFS ALTERNATE DATA
    # STREAM: GetFileAttributes resolves it to the BASE FILE.  So the existence test passed on a
    # name that is not a file -- a false TRUE, the mirror of "unreadable != value 0".
    # Fix: cut at the first ':' and accept the head only if it ends with a known file extension;
    # anything else colon-shaped is skipped (not counted), never silently treated as a file.
    pc_total = pc_ok = 0
    pc_bad = []
    for tok, srcs in path.items():
        t = tok.rstrip(".,;:)]}")
        if ":" in t:
            head = t.split(":", 1)[0]
            if PATH_EXT_RE.search(head):
                t = head
            else:
                continue
        if not ZONE_RE.match(t):
            continue
        full = os.path.join(ROOT, t.replace("/", os.sep))
        if not os.path.isfile(full):
            continue
        pc_total += 1
        if t in wl_set:
            pc_ok += 1
        else:
            pc_bad.append((t, sorted(srcs)))

    # extra probe of the same family: bare tokens whose extension IS in the generator's $extOk
    extok_safe = {}
    for tok, srcs in bare.items():
        if tok.rsplit(".", 1)[-1] in ("cs",):
            cands = zidx.get(tok, [])
            if cands and all(c not in wl_set for c in cands):
                extok_safe[tok] = (sorted(srcs), sorted(cands))

    # ---- SECOND QUESTION (per FILE, not per name) ----------------------------------------
    # "Which FILES would a fix that admits py/ps1 newly protect?"  This is NOT the same question
    # as "which cited names have no protection today": when a cited name has SEVERAL in-zone
    # candidates and only SOME are protected, the name-level rule stays silent while the fix will
    # still add the unprotected twin(s).  Measured example: sc_decode.py has .ai-tmp/web/sc_decode.py
    # and .ai-tmp/test/scdfull/sc_decode.py; one of them is already in-register, so the name-level
    # count excludes the name entirely, yet the other twin is exactly what the fix adds.
    # The $extOk set is READ FROM THE GENERATOR SOURCE at run time (never copied here), so after the
    # fix this section must go EMPTY by itself -- which makes it a mechanical check of the fix.
    newly = {}
    ext_ok = read_extok()
    if ext_ok is None:
        return {"read_error": "$extOk could not be read from the generator source"}
    for tok, srcs in bare.items():
        if tok.rsplit(".", 1)[-1].lower() not in ext_ok:
            continue                              # extension the tool CANNOT extract => never added
        cands = zidx.get(tok, [])
        bad = [c for c in cands if c not in wl_set]
        if bad:
            newly[tok] = (sorted(srcs), sorted(bad), len(cands))

    return {"bare": bare, "gaps": gaps, "pc_total": pc_total, "pc_ok": pc_ok, "pc_bad": pc_bad,
            "extok_safe": extok_safe, "newly": newly, "ext_ok": sorted(ext_ok)}


def report(wl_path, docs_dir, zone_dir, expect_gap=None):
    t, th, tb, tm = triple(os.path.abspath(__file__))
    wt, wh, wb, wm = triple(wl_path)
    print("# CR-V1 citation-protection-gap census  --  DISCREPANCY PROBE, NOT a producer")
    print("# tool      = %s  sha256_16=%s  bytes=%d  mtime=%s" % (t, th, tb, tm))
    print("# AS-OF     = %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    print("# whitelist = %s  sha256_16=%s  bytes=%d  mtime=%s" % (wt, wh, wb, wm))
    print("# rule      = bare script name cited in <docs>**, resolving to an existing in-zone file,")
    print("#             that is ABSENT from the whitelist => the cleanup would delete a cited file")
    wl_set = load_whitelist(wl_path)
    if wl_set is None:
        print("RESULT: NOT-JUDGED (whitelist not readable: %s)" % wl_path)
        return 2
    if not docs_dir or not os.path.isdir(docs_dir):
        print("RESULT: NOT-JUDGED (docs dir not readable: %s)" % docs_dir)
        return 2
    docs = load_docs(docs_dir)
    zidx = zone_index(zone_dir)
    r = census(wl_set, docs, zidx)
    if "read_error" in r:
        print("RESULT: NOT-JUDGED (a doc could not be read: %s) -- a read failure must never look" % r["read_error"])
        print("        like '0 citations found' (the 5th false-green branch).")
        return 2
    print("# subject   = whitelist_entries=%d  docs=%d  in_zone_files=%d  bare_script_tokens=%d"
          % (len(wl_set), len(docs), sum(len(v) for v in zidx.values()), len(r["bare"])))
    pc_ok, pc_total = r["pc_ok"], r["pc_total"]
    print("# positive control (path-form cites that RESOLVE in-zone, must be in-register) = %d/%d"
          % (pc_ok, pc_total))
    if pc_ok != pc_total:
        print("RESULT: NOT-JUDGED  the positive control is NOT clean (%d bad) -- refusing to report a gap"
              % len(r["pc_bad"]))
        for t2, srcs in r["pc_bad"][:5]:
            print("   bad: %s   cited by %s" % (t2, ",".join(srcs)))
        return 2
    gt, gh, gb, gm = triple(GENERATOR)
    print("# generator = %s  sha256_16=%s  bytes=%d  mtime=%s" % (gt, gh, gb, gm))
    print("# extOk read from the generator: %d entries | py present=%s | ps1 present=%s"
          % (len(r["ext_ok"]), "py" in r["ext_ok"], "ps1" in r["ext_ok"]))
    print("# consistency probe: bare tokens with extension IN $extOk that are unprotected = %d"
          % len(r["extok_safe"]))
    newly = r["newly"]
    print("# WILL BE NEWLY PROTECTED AT THE NEXT LANDING (per FILE) = %d"
          % sum(len(v[1]) for v in newly.values()))
    print("#   ^ the quantity comparable to a landing's 'added'.  NOT the same question as GAPS below:")
    print("#     an ambiguous name (several in-zone candidates, only SOME protected) stays silent in")
    print("#     GAPS while the landing still adds its unprotected twin(s).  $extOk is READ FROM THE")
    print("#     GENERATOR SOURCE each run, so this number tracks the TOOL, not my memory of it:")
    print("#     with py/ps1 already present in the source the section is non-empty and its total is")
    print("#     what a correct landing should add.")
    for tok in sorted(newly):
        srcs, bad, tot = newly[tok]
        print("  [NEW] %-34s cited by %-46s (%d in-zone candidate(s), %d unprotected)"
              % (tok, ",".join(srcs), tot, len(bad)))
        for c in bad:
            print("        would be newly protected: %s" % c)
    gaps = r["gaps"]
    print("# GAPS = %d" % len(gaps))
    # GAP split by zone sub-tree. WHY: the generator's own KINDS note says bare names resolve
    # "under .ai-tmp/test or .ai-tmp/screenshots", while this census resolves any in-zone path.
    # If a fix covers only some sub-trees, a residual confined to .ai-tmp/hosts or .ai-tmp/test/r5
    # is a COVERAGE difference between the two resolutions, NOT proof that the fix failed -- so the
    # split is printed every run and the expectation is stated against it, not against 0 alone.
    split = {}
    for tok, (_srcs, cands) in gaps.items():
        for c in cands:
            key = os.path.dirname(c) or c        # group by the DIRECTORY a candidate lives in
            split[key] = split.get(key, 0) + 1
    print("# GAP split by zone sub-tree: %s"
          % (" ".join("%s=%d" % (k, split[k]) for k in sorted(split)) if split else "(none)"))
    for tok in sorted(gaps):
        srcs, cands = gaps[tok]
        print("  [GAP] %-34s cited by %s" % (tok, ",".join(srcs)))
        for c in cands:
            print("        candidate (unprotected): %s" % c)
    if expect_gap is not None:
        if len(gaps) == expect_gap:
            print("RESULT: expect-gap=%d MATCHED (exit 0)" % expect_gap)
            return 0
        print("RESULT: expect-gap=%d but measured %d (exit 3)" % (expect_gap, len(gaps)))
        return 3
    return 0


def selftest():
    """Plant a real gap in a temp zone; assert FOUND; protect it; assert GONE."""
    probe = os.path.join(ROOT, ".ai-tmp", "test", "_cr-v1-gap-probe")
    if os.path.isdir(probe):
        shutil.rmtree(probe)
    zone = os.path.join(probe, "zone")
    docs = os.path.join(probe, "docs")
    os.makedirs(os.path.join(zone, "test"))
    os.makedirs(docs)
    planted = os.path.join(zone, "test", "planted_script.py")
    with open(planted, "w", encoding="utf-8") as f:
        f.write("x = 1\n")
    doc = os.path.join(docs, "doc.md")
    with open(doc, "w", encoding="utf-8") as f:
        f.write("run `planted_script.py` now\n")
    wl = os.path.join(probe, "wl.txt")
    checks = []
    try:
        with open(wl, "w", encoding="utf-8") as f:
            f.write("# empty whitelist\n")
        # NOTE: the probe's own ROOT-relative math must see the temp zone, so we call census directly
        wl_set = load_whitelist(wl)
        zidx = {"planted_script.py": [os.path.relpath(planted, ROOT).replace(os.sep, "/")]}
        r1 = census(wl_set, [doc], zidx)
        checks.append(("gap FOUND when unprotected", "planted_script.py" in r1["gaps"], str(list(r1["gaps"]))))
        wl_set.add(os.path.relpath(planted, ROOT).replace(os.sep, "/"))
        r2 = census(wl_set, [doc], zidx)
        checks.append(("gap GONE once protected", "planted_script.py" not in r2["gaps"], str(list(r2["gaps"]))))
        r3 = census(wl_set, [doc], {})
        checks.append(("no in-zone candidate => not a gap", not r3["gaps"], str(list(r3["gaps"]))))
        r4 = census(wl_set, [], zidx)
        checks.append(("no docs => 0 tokens", len(r4["bare"]) == 0, str(len(r4["bare"]))))
        # line/range-suffix control (the Windows-ADS trap that fooled this probe's own v1):
        # a citation written as <path>:12 must resolve to its HEAD, be matched against the whitelist
        # as that head, and NOT be reported as a bad path.
        rel = os.path.relpath(planted, ROOT).replace(os.sep, "/")
        doc2 = os.path.join(docs, "doc2.md")
        with open(doc2, "w", encoding="utf-8") as f:
            f.write("see %s:12 for the details\n" % rel)
        r5 = census(set([rel]), [doc2], {"planted_script.py": [rel]})
        checks.append(("':12' suffix resolves to its head, control clean",
                       r5["pc_total"] == 1 and r5["pc_ok"] == 1 and not r5["pc_bad"],
                       "total=%d ok=%d bad=%s" % (r5["pc_total"], r5["pc_ok"], r5["pc_bad"])))
    finally:
        shutil.rmtree(probe, ignore_errors=True)
    bad = 0
    print("selftest (negative control, temp zone under .ai-tmp/test, removed afterwards):")
    for name, ok, detail in checks:
        print("  %-4s %-38s %s" % ("PASS" if ok else "FAIL", name, detail))
        bad += 0 if ok else 1
    print("RESULT: checks=%d fails=%d  ->  %s" % (len(checks), bad,
          "OK (fires on a planted gap, stays silent once protected)" if bad == 0 else "BROKEN"))
    return 1 if bad else 0


def main():
    args = sys.argv[1:]
    if "--selftest" in args:
        return selftest()
    expect = None
    if "--expect-gap" in args:
        try:
            expect = int(args[args.index("--expect-gap") + 1])
        except (IndexError, ValueError):
            print("usage: --expect-gap N")
            return 2
    return report(WHITELIST, DOCS_DIR, ZONE_DIR, expect_gap=expect)


if __name__ == "__main__":
    sys.exit(main())
