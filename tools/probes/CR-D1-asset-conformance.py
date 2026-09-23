# -*- coding: utf-8 -*-
"""CR-D1 criterion-asset conformance check (read-only; reusable by any slice).

Implements the two rules the lead enrolled at 12:5x that this slice must satisfy:
  R1  「检查器必须与载体类型匹配」  => a PS AST parser must NOT be run on C# files, and vice versa.
      - .ps1  : parsecheck (PS AST) + extract-real-code-and-execute is the caller's job; here: parse.
      - .cs   : compliance form = non-ASCII bytes == 0 + the real file was run in a chain (no PS parse).
      - .py   : parsecheck via compile().
  R2  「parsecheck 通过 ⛔ 推不出 运行期不会炸」 => the banned PS 5.1 form `"t" + (if (...) {...})`
      PARSES but fails at runtime. Static scan with a look-behind so that the LEGAL forms are excluded:
        ⛔ ( if (                      <- ordinary parens => runtime failure
        ✅ $( if (                     <- sub-expression
        ✅ $x = if ( ... ) { } else { } <- statement assigned to a variable
  Also reports, per file: BOM presence + non-ASCII byte count (the encoding rule only bites when the
  file has non-ASCII), so a "potential non-conformance + observed-not-materialised" note can be
  written the way the lead asked for.

Read-only: opens files, runs a parse, prints. Writes nothing.
"""
import io
import os
import re
import subprocess
import sys

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
TARGETS = [
    r"tools\probes\CR-D1-run.ps1",
    r"tools\probes\CR-D1-stall-probe.cs",
    r"tools\probes\CR-D1-asset-conformance.py",
    r".ai-tmp\test\CR-D1-manifest.py",
    r".ai-tmp\test\CR-D1-impact-enum.py",
    r".ai-tmp\test\CR-D1-playlog-pairing.py",
    r".ai-tmp\test\CR-D1-residualstate.py",
]

BANNED = re.compile(r"(?<!\$)\(\s*if\s*\(")          # ordinary-paren if => runtime failure
LEGAL_SUBEXPR = re.compile(r"\$\(\s*if\s*\(")        # $( if ( ) ) => legal
LEGAL_ASSIGN = re.compile(r"^\s*\$[\w:]+\s*=\s*if\s*\(", re.M)


def ps_parse_errors(path):
    """PS AST parse via a real PowerShell process; argv list => no shell-quoting layer involved."""
    code = ("$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile("
            "'" + path + "',[ref]$null,[ref]$e); Write-Output ('COUNT=' + $e.Count)")
    p = subprocess.run(["powershell", "-NoProfile", "-Command", code],
                       capture_output=True, text=True)
    out = (p.stdout or "").strip()
    m = re.search(r"COUNT=(\d+)", out)
    return (int(m.group(1)) if m else None), out.replace("\r", "").replace("\n", " | ")


def main():
    print("cr-d1-asset-conformance  (host PS: see below)")
    try:
        v = subprocess.run(["powershell", "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"],
                           capture_output=True, text=True).stdout.strip()
        print("  host powershell version = %s" % v)
    except Exception as e:
        print("  host powershell version = UNKNOWN (%s)" % e)
    print("%-46s %-5s %-9s %-6s %-6s %-6s %s" % ("file", "kind", "nonASCII", "BOM", "banned", "legal$(", "parsecheck"))
    bad = 0
    notjudged = 0   # enrolled 2026-09-23: an existence/parse judgement must be THREE-state.
                    # "could not read / parse did not run" must NEVER fall into the 'clean' branch.
    selfpath = os.path.abspath(__file__)
    for rel in TARGETS:
        p = os.path.join(ROOT, rel)
        if not os.path.exists(p):
            print("%-46s MISSING" % rel)
            bad += 1
            continue
        raw = io.open(p, "rb").read()
        non_ascii = sum(1 for b in raw if b > 0x7F)
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig", errors="replace")
        kind = os.path.splitext(p)[1]
        banned = len(BANNED.findall(text))
        legal = len(LEGAL_SUBEXPR.findall(text))
        # SELF-REFERENCE (registered, not papered over): this checker's own source necessarily contains
        # the banned exemplar inside its documentation and the pattern literal itself. A checker cannot
        # be its own clean sample. Raw counts are still printed; only the VERDICT excludes this file.
        is_self = (os.path.abspath(p) == selfpath)
        if kind == ".ps1":
            n, note = ps_parse_errors(p)
            if n is None:
                # three-state rule: the parse did not run => NOT-JUDGED (never 'clean')
                parse = "NOT-JUDGED (parser produced no COUNT: %s)" % note
                notjudged += 1
            else:
                parse = "parseErrors=%d" % n
                if n > 0:
                    bad += 1
        elif kind == ".py":
            try:
                compile(text, p, "exec")
                parse = "compile OK"
            except SyntaxError as e:
                parse = "SyntaxError line %s" % e.lineno
                bad += 1
        else:
            # .cs : per R1 a PS parser must NOT be used here; the compliance form is byte-level + "it ran"
            parse = "n/a by design (C#; not PS-parsed)"
        if kind == ".ps1" and non_ascii and not bom:
            parse += "  <-- POTENTIAL non-conformance (non-ASCII without BOM): must be written with its observed-not-materialised evidence"
        if is_self:
            parse += "  [SELF: raw counts shown, excluded from verdict by design]"
        elif banned:
            bad += 1
        print("%-46s %-5s %-9d %-6s %-6d %-6d %s" % (rel, kind, non_ascii, bom, banned, legal, parse))
    # ---- SELF-MADE COUNTEREXAMPLES (enrolled 2026-09-23: make the judgement fail BEFORE trusting it) ----
    # CR-V1's counterexample: ONE line containing BOTH a legal `$(if …)` and an illegal `(if …)`.
    # A line-level judgement ("this line contains (if( and does not contain $(if") would let it pass.
    mult = 'y = "a" + $(if (1) {2} else {3}) + "b" + (if (1) {2} else {3})'
    legal_only = 'y = "a" + $(if (1) {2} else {3}) + "b"'
    legacy_line_level = ("(if (" in mult.replace("$(if (", ""))          # the rejected shape
    print()
    print("SELF-MADE COUNTEREXAMPLES (the judgement must be able to fail):")
    print("  [mixed legal+illegal on ONE line]  legacy line-level -> %s (FALSE GREEN, rejected); current lookahead -> %d hit(s)"
          % (legacy_line_level, len(BANNED.findall(mult))))
    print("  [legal only]                       current lookahead -> %d hit(s) (expect 0)"
          % len(BANNED.findall(legal_only)))
    # three-state control: point the parser at a path that cannot be parsed => it must answer NOT-JUDGED,
    # NOT "clean". (Before this change the checker printed '?' and still said 'all checks clean'.)
    n_bad, note_bad = ps_parse_errors(os.path.join(ROOT, "tools", "probes", "__control_no_such_file__.ps1"))
    print("  [unreadable subject]               -> parseErrors=%s  =>  %s"
          % ("?" if n_bad is None else n_bad,
             "FAIL branch (correct: an unreadable subject is not 'clean')" if (n_bad or 0) > 0
             else "NOT-JUDGED branch"))
    print("  [NOT-JUDGED branch]                -> NOT EXERCISED: ParseFile on a missing file returns COUNT=1")
    print("                                        (a real parse error => FAIL branch), so the 'no COUNT'")
    print("                                        branch stays DEFENSIVE and UNVERIFIED by any control")
    print("                                        I could construct. Registered, not claimed as tested.")
    print()
    print("banned-form occurrences total (incl. this checker's own docstring/pattern) = %d; excluding self = %d"
          % (sum(len(BANNED.findall(io.open(os.path.join(ROOT, r), encoding="utf-8-sig", errors="replace").read()))
                 for r in TARGETS if os.path.exists(os.path.join(ROOT, r))),
             sum(len(BANNED.findall(io.open(os.path.join(ROOT, r), encoding="utf-8-sig", errors="replace").read()))
                 for r in TARGETS if os.path.exists(os.path.join(ROOT, r))
                 and os.path.abspath(os.path.join(ROOT, r)) != selfpath)))
    if bad:
        print("VERDICT: %d item(s) need attention (see above)" % bad)
        return 1
    if notjudged:
        print("VERDICT: nothing failed, but %d judgement(s) NOT-JUDGED => NOT a clean run (exit 3)" % notjudged)
        return 3
    print("VERDICT: all checks clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
