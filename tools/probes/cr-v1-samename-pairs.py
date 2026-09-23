#!/usr/bin/env python3
# -*- coding: ascii -*-
"""
CR-V1 samename-pairs census  --  ONE implementation, re-runnable, self-anchoring.

WHY: the same basename existing twice with DIFFERENT content is a repeated hazard tonight (a stale
     copy is read as if it were the authoritative one).  A hand-written list of such pairs ROTS: new
     pairs appear after the scan (this file's own first finding was created 2.5 h after the manual
     scan).  So the deliverable is a RE-RUNNABLE census, not a number.

WHAT IT IS NOT: it produces NO hash of any directory/manifest and judges NO evidence.  It only
     groups existing files by basename and reports groups whose members differ byte-wise (sha256).

SELF-ANCHORING (CR-F2 pattern): every run prints a "# tool" triple (this file's own sha16/bytes/
     mtime) and a "# subject" triple (scanned files / groups / DIFFERENT / SAME), so a reader never
     has to guess WHICH BYTES and WHICH MOMENT produced the list.

ROOTS and the extension set are printed in the header on every run, so two runs are comparable.
Outside-cleanup-zone files are included on purpose: the hazard is a reader picking the wrong copy,
and that hazard does not care which directory the copies live in.

USAGE:
    python tools/probes/cr-v1-samename-pairs.py                 # scan, print, write the TSV
    python tools/probes/cr-v1-samename-pairs.py --selftest      # negative control, temp dir, cleaned
    python tools/probes/cr-v1-samename-pairs.py --check         # re-run and FAIL if the TSV is stale
"""
import os
import sys
import hashlib
import shutil
import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))                 # .../clover-project-cr
OUT_TSV = os.path.join(HERE, "CR-V1-samename-pairs.tsv")

ROOTS = ["tools/probes", ".ai-tmp/test", ".ai-tmp/drivers", ".ai-tmp/hosts"]
EXTS = (".cs", ".py", ".ps1", ".txt", ".tsv", ".json", ".md", ".dll")


def sha16(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()[:16].upper()


def scan(roots):
    """Return (files, groups) where groups = {basename: [record, ...]} with >1 member."""
    files = []
    for r in roots:
        base = os.path.join(ROOT, r.replace("/", os.sep))
        if not os.path.isdir(base):
            continue
        for dirpath, _dirnames, filenames in os.walk(base):
            for fn in filenames:
                if not fn.lower().endswith(EXTS):
                    continue
                full = os.path.join(dirpath, fn)
                rel = os.path.relpath(full, ROOT).replace(os.sep, "/")
                try:
                    rec = {"rel": rel, "name": fn, "bytes": os.path.getsize(full),
                           "mtime": datetime.datetime.fromtimestamp(os.path.getmtime(full)).strftime("%Y-%m-%d %H:%M:%S"),
                           "sha16": sha16(full)}
                except OSError:
                    rec = {"rel": rel, "name": fn, "bytes": -1, "mtime": "UNREADABLE", "sha16": "UNREADABLE"}
                files.append(rec)
    by = {}
    for rec in files:
        by.setdefault(rec["name"], []).append(rec)
    groups = {k: v for k, v in by.items() if len(v) > 1}
    return files, groups


def classify(groups):
    """Split multi-member groups into DIFFERENT (>1 distinct sha16) and SAME (identical by sha16)."""
    diff, same = {}, {}
    for name, members in groups.items():
        shas = sorted(set(m["sha16"] for m in members))
        (diff if len(shas) > 1 else same)[name] = sorted(members, key=lambda m: m["mtime"])
    return diff, same


def tool_triple():
    p = os.path.abspath(__file__)
    return (os.path.relpath(p, ROOT).replace(os.sep, "/"), sha16(p), os.path.getsize(p),
            datetime.datetime.fromtimestamp(os.path.getmtime(p)).strftime("%Y-%m-%d %H:%M:%S"))


def render(files, groups):
    diff, same = classify(groups)
    lines = []
    t, th, tb, tm = tool_triple()
    lines.append("# CR-V1 same-basename census  --  MACHINE-GENERATED DERIVATIVE, NOT evidence; regenerate:")
    lines.append("#   python tools/probes/cr-v1-samename-pairs.py")
    lines.append("# tool    = %s  sha256_16=%s  bytes=%d  mtime=%s" % (t, th, tb, tm))
    lines.append("# AS-OF   = %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    lines.append("# roots   = %s" % ",".join(ROOTS))
    lines.append("# ext     = %s" % ",".join(EXTS))
    lines.append("# subject = scanned=%d  multi_member_groups=%d  DIFFERENT=%d  SAME=%d"
                 % (len(files), len(groups), len(diff), len(same)))
    lines.append("# READING RULE: 'DIFFERENT' = same basename, >1 distinct sha256 -> a reader may pick the")
    lines.append("#   wrong copy.  'SAME' = duplicates that are byte-identical (harmless, still listed).")
    lines.append("#   A pair that appeared AFTER a previous run will NOT be in that run's output -> re-run.")
    lines.append("# name\tsha16\tbytes\tmtime\tpath")
    for name in sorted(diff):
        for m in diff[name]:
            lines.append("%s\t%s\t%d\t%s\t%s" % (name, m["sha16"], m["bytes"], m["mtime"], m["rel"]))
    for name in sorted(same):
        for m in same[name]:
            lines.append("%s\t%s\t%d\t%s\t%s" % (name, m["sha16"], m["bytes"], m["mtime"], m["rel"]))
    return lines, diff, same


def selftest():
    """Negative control: a DIFFERENT pair must be reported, an identical pair must be classified SAME."""
    probe = os.path.join(ROOT, ".ai-tmp", "test", "_cr-v1-samename-probe")
    if os.path.isdir(probe):
        shutil.rmtree(probe)
    os.makedirs(os.path.join(probe, "a"))
    os.makedirs(os.path.join(probe, "b"))
    with open(os.path.join(probe, "a", "same_name_diff.py"), "w", encoding="utf-8") as f:
        f.write("x = 1\n")
    with open(os.path.join(probe, "b", "same_name_diff.py"), "w", encoding="utf-8") as f:
        f.write("x = 2\n")
    with open(os.path.join(probe, "a", "same_name_same.py"), "w", encoding="utf-8") as f:
        f.write("y = 1\n")
    with open(os.path.join(probe, "b", "same_name_same.py"), "w", encoding="utf-8") as f:
        f.write("y = 1\n")
    saved = list(ROOTS)
    try:
        ROOTS[:] = [os.path.relpath(probe, ROOT).replace(os.sep, "/")]
        files, groups = scan(ROOTS)
        diff, same = classify(groups)
    finally:
        ROOTS[:] = saved
        shutil.rmtree(probe, ignore_errors=True)
    checks = [
        ("scanned 4 probe files", len(files) == 4, str(len(files))),
        ("DIFFERENT contains the mutated pair", "same_name_diff.py" in diff, str(sorted(diff))),
        ("SAME contains the identical pair", "same_name_same.py" in same, str(sorted(same))),
        ("DIFFERENT count == 1", len(diff) == 1, str(len(diff))),
        ("probe dir removed", not os.path.isdir(probe), probe),
    ]
    bad = 0
    print("selftest (negative control):")
    for name, ok, detail in checks:
        print("  %-4s %-40s %s" % ("PASS" if ok else "FAIL", name, detail))
        bad += 0 if ok else 1
    print("RESULT: checks=%d fails=%d  ->  %s"
          % (len(checks), bad, "OK (it fires on a real pair and stays silent on identical copies)"
             if bad == 0 else "BROKEN"))
    return 1 if bad else 0


def main():
    args = sys.argv[1:]
    if "--selftest" in args:
        return selftest()
    files, groups = scan(ROOTS)
    lines, diff, same = render(files, groups)
    body = "\n".join(lines) + "\n"
    if "--check" in args:
        if not os.path.isfile(OUT_TSV):
            print("NOT-JUDGED (no published census to compare against): %s" % OUT_TSV)
            return 2
        old = open(OUT_TSV, encoding="utf-8").read()
        # compare only the DATA lines; the header carries AS-OF/tool and MUST differ between runs
        old_data = [l for l in old.splitlines() if l and not l.startswith("#")]
        new_data = [l for l in body.splitlines() if l and not l.startswith("#")]
        if old_data == new_data:
            print("RESULT: check=OK  the published census still matches this scan (data lines identical)")
            return 0
        print("RESULT: check=STALE  the published census does NOT match this scan")
        print("  published data lines=%d  now=%d" % (len(old_data), len(new_data)))
        for i in range(max(len(old_data), len(new_data))):
            a = old_data[i] if i < len(old_data) else "<missing>"
            b = new_data[i] if i < len(new_data) else "<missing>"
            if a != b:
                print("  first difference at data line %d:\n    published: %s\n    now      : %s" % (i + 1, a, b))
                break
        return 3
    with open(OUT_TSV, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)
    for l in lines[:7]:
        print(l)
    print("# --- DIFFERENT groups: %d (the actionable ones) ---" % len(diff))
    for name in sorted(diff):
        print("  [DIFFERENT] %s  x%d" % (name, len(diff[name])))
        for m in diff[name]:
            print("      %-10s %8d B  %s  %s" % (m["sha16"], m["bytes"], m["mtime"], m["rel"]))
    print("# --- SAME groups (byte-identical duplicates, harmless): %d ---" % len(same))
    for name in sorted(same):
        print("  [SAME] %s  x%d" % (name, len(same[name])))
    print("wrote %s" % os.path.relpath(OUT_TSV, ROOT).replace(os.sep, "/"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
