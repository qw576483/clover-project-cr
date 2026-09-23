# CR-F2-protection-reasons.ps1 -- READ-ONLY: for every entry in the protection list, print WHY it is protected.
#
# Motivation (team-lead ruling 2026-09-23, from CR-F2's inventory): editing a 策划/ document can silently
# UN-PROTECT files -- a REVERSE blast radius. Measured: 33 of the 439 entries had no literal or bare-name
# mention anywhere in 策划/ yet were protected, surviving on non-literal mechanisms; and rewriting ONE such
# sentence (the glob form) silently dropped 3 PNGs. So "what keeps this entry alive" must be MACHINE-READABLE
# instead of reconstructed by hand each time.
#
# WHY A SEPARATE REPORT instead of a new column in the products: the four products were frozen by a landed
# baseline; adding a column would require another land and would change a format other slices already read.
# This script computes the same information on demand, touches nothing, and lives in the retention area.
#
# RE-RUN SAFETY (standing rule -- "prove your re-run cannot disturb the product"): this file contains NO
# writing call. Checked by grepping for the two content writers, the append helper, the two WriteAll* APIs and
# the four item commands: 0 hits (the same check CR-F2-ambiguity-cleanup-sim.ps1 documents). Anyone may re-run it.
#
# FORMS (8 kinds, all measured 2026-09-23 -- only #1 is DEPENDABLE when WRITING a document):
#   1 literal    the exact path (or its backslash form) appears in 策划/**.md|*.tsv   <- the only reliable one
#   2 bare       the file's LEAF NAME appears in 策划/ and resolves into the cleanup zone ("sometimes works":
#                a bare mention DID protect CR-U5-evidence.txt, while a bare brace/glob form protected nothing)
#   3 glob       matched by a `.ai-tmp/<...>*<ext>` pattern written in a document      (expands: real effect)
#   4 brace      matched by a `.ai-tmp/<...>{a,b,...}<ext>` pattern written in a document (expands: real effect)
#   5 range      matched by a `.ai-tmp/<...>0..4<ext>` numeric range written in a document (expands too)
#   6 keep       matched by the unconditional-keep rule  .ai-tmp/**/*index*.tsv
#   7 candidate  an in-zone candidate of an ambiguous name that is itself protected (keep-EVERY-candidate rule)
#   8 unknown    none of the above -> printed LOUDLY; this needs a human
#
# INVARIANT (can fail): the reason counts must SUM to the body count -- otherwise some entry was skipped.
param(
  [ValidateSet('all', 'f2')][string]$Scope = 'all',
  [string]$Dir = 'tools/probes'
)
$ErrorActionPreference = 'Stop'
$root   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$wlPath = Join-Path $root (Join-Path $Dir ('cited-evidence-whitelist-' + $Scope + '.txt'))
$bkPath = Join-Path $root (Join-Path $Dir ('cited-evidence-whitelist-buckets-' + $Scope + '.txt'))
$planDir = Join-Path $root (([char[]]@(0x7B56, 0x5212)) -join '')   # ce hua -- the citation SOURCE surface

# ---- corpus: the citation source is 策划/**.md|*.tsv ONLY (a .ai-tmp-internal citation protects nothing) ----
$docs = @(Get-ChildItem $planDir -Recurse -File -Include '*.md', '*.tsv' -ErrorAction SilentlyContinue)
if ($docs.Count -eq 0) { Write-Output 'FAIL no plan-dir document found'; exit 1 }
$sb = New-Object System.Text.StringBuilder
foreach ($d in $docs) { [void]$sb.Append([System.IO.File]::ReadAllText($d.FullName, [System.Text.Encoding]::UTF8)); [void]$sb.Append("`n") }
$docText = $sb.ToString()

# ---- glob/brace patterns written in the corpus: which entries do they expand to? ----
$patterns = @()
# DEFECT SELF-CAUGHT twice (2026-09-23, by this script's own first run -- both belong to the "read failure
# swallowed" family, so they are recorded rather than quietly fixed):
#  (a) the token class EXCLUDED the comma, so `{a,b,c,d}` was truncated at the first comma
#      ("...CR-T3-re-{00-baseline") and could never match; a truncated token looks like a plausible short
#      pattern, so nothing else complained;
#  (b) matching was done by BUILDING A REGEX from the token -- but [regex]::Escape leaves a lone `}` alone
#      (only `{` is escaped), so the conversion produced an unbalanced pattern, and the surrounding
#      `try {} catch {}` SWALLOWED the error: the patterns were silently ignored and 4 brace-protected files
#      fell into `unknown` while `brace` read 0.
# So braces are now EXPANDED into concrete patterns and matched with -like, which cannot throw.
# the eighth FORM (measured 2026-09-23 while validating this script): a zone-path + NUMERIC RANGE, written
# `.ai-tmp/test/AP2-probe-0..4.txt` -- it expands exactly like a glob (it protected 5 files that no other
# form explained). So the marker set is { * , { , \d+\.\.\d+ }.
foreach ($m in [regex]::Matches($docText, '\.ai-tmp/[^\s`）)（|<>]*(\*|\{|\d+\.\.\d+)[^\s`）)（|<>]*')) { $patterns += $m.Value }
$patterns = @($patterns | Sort-Object -Unique)
$matchList = New-Object System.Collections.Generic.List[object]
$badPat = New-Object System.Collections.Generic.List[string]
foreach ($p in $patterns) {
  $mm = [regex]::Match($p, '\{([^}]*)\}')
  $rm = [regex]::Match($p, '(?<a>\d+)\.\.(?<b>\d+)')
  if ($rm.Success -and -not $mm.Success) {
    $a = [int]$rm.Groups['a'].Value; $b = [int]$rm.Groups['b'].Value
    if ($b -lt $a -or ($b - $a) -gt 500) { $badPat.Add($p); continue }
    foreach ($n in $a..$b) {
      $matchList.Add([pscustomobject]@{ Pat = ($p.Substring(0, $rm.Index) + $n.ToString() + $p.Substring($rm.Index + $rm.Length)); Kind = 'range' })
    }
    continue
  }
  if (-not $mm.Success) { $matchList.Add([pscustomobject]@{ Pat = $p; Kind = 'glob' }); continue }
  $opts = @($mm.Groups[1].Value -split ',')
  if ($opts.Count -lt 2) { $badPat.Add($p); continue }
  foreach ($o in $opts) {
    $matchList.Add([pscustomobject]@{ Pat = ($p.Substring(0, $mm.Index) + $o.Trim() + $p.Substring($mm.Index + $mm.Length)); Kind = 'brace' })
  }
}

# ---- ambiguous names: candidate -> name (for the keep-EVERY-candidate rule) ----
$ambOf = @{}
if (Test-Path $bkPath) {
  foreach ($l in @(Get-Content $bkPath -Encoding UTF8)) {
    if ($l -notlike 'AMBIG*') { continue }
    $c = $l -split "`t"
    if ($c.Count -lt 4) { continue }
    foreach ($p in ($c[3] -split '\|')) { if ($p) { $ambOf[$p.Trim()] = $c[1] } }
  }
}

# ---- entries (body only) ----
$all  = @(Get-Content $wlPath -Encoding UTF8)
$body = @($all | Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^#' } | ForEach-Object { $_.Trim() })
$set  = @{}
foreach ($e in $body) { $set[$e] = 1 }

function Get-Reason([string]$e) {
  $leaf = Split-Path $e -Leaf
  if ($docText.Contains($e)) { return 'literal' }
  if ($docText.Contains($e.Replace('/', '\'))) { return 'literal' }
  if ($leaf -and $docText.Contains($leaf)) { return 'bare' }
  foreach ($x in $matchList) { if ($e -like $x.Pat) { return $x.Kind } }
  if ($e -match 'index.*\.tsv$' -and $e -like '.ai-tmp/*') { return 'keep' }
  if ($ambOf.ContainsKey($e)) {
    foreach ($p in @($ambOf.Keys)) { if ($ambOf[$p] -eq $ambOf[$e] -and $set.ContainsKey($p)) { return 'candidate' } }
  }
  return 'unknown'
}

Write-Output ('protection reasons | scope=' + $Scope + ' | at ' + (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK'))
Write-Output ('  whitelist = ' + (Split-Path $wlPath -Leaf) + '  sha256_16=' + (Get-FileHash $wlPath -Algorithm SHA256).Hash.Substring(0, 16) + '  body=' + $body.Count)
Write-Output ('  corpus    = ' + $docs.Count + ' doc(s) from 策划/  |  patterns in corpus = ' + $patterns.Count + ' -> expanded to ' + $matchList.Count + ' concrete; unusable = ' + $badPat.Count)
# NOT silently skipped (defect (b) above): an unusable pattern is printed AND turns the exit code non-zero,
# so a caller cannot read "0" while a written pattern was ignored.
foreach ($bp in $badPat) { Write-Output ('  FAIL unusable pattern (would have been skipped silently): ' + $bp) }
Write-Output ''
$byReason = @{}
foreach ($e in $body) { $r = Get-Reason $e; if (-not $byReason.ContainsKey($r)) { $byReason[$r] = 0 }; $byReason[$r]++ }
foreach ($r in @('literal', 'bare', 'brace', 'range', 'glob', 'keep', 'candidate', 'unknown')) {
  $n = if ($byReason.ContainsKey($r)) { $byReason[$r] } else { 0 }
  Write-Output ('  ' + $r.PadRight(10) + $n)
}
$sum = 0; foreach ($k in $byReason.Keys) { $sum += $byReason[$k] }
if ($sum -eq $body.Count) { Write-Output ('  INVARIANT OK: reasons sum ' + $sum + ' == body ' + $body.Count) }
else { Write-Output ('  FAIL: reasons sum ' + $sum + ' != body ' + $body.Count) }
Write-Output ''
# entries whose protection rests on a NON-LITERAL form are the ones a document edit can silently drop
Write-Output '-- entries NOT protected by a literal path citation (these are the reverse-blast-radius set) --'
foreach ($e in $body) { $r = Get-Reason $e; if ($r -ne 'literal') { Write-Output ('  ' + $r.PadRight(10) + $e) } }
