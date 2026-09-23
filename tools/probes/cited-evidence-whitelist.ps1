# cited-evidence-whitelist.ps1 -- build the "do not delete at delivery" list from the plan docs.
#
# WHY: the project's evidence area (.ai-tmp/screenshots for images, .ai-tmp/test for text audit
# files) is cited BY PATH from the documents under the plan dir. The delivery cleanup must not
# delete a cited file, otherwise every citing row becomes a dangling reference.
# SEMANTICS: "one-off area" DOES NOT MEAN "deletable" -- the delivery documents cite files that
# live in .ai-tmp/test, so those files must survive the cleanup. Only uncited files may be removed.
#
# SCOPE (team-lead ruling, from CR-R1's recount): the WHOLE plan dir by default, because the four
# ledger files hold only part of the references. `-Scope f2` reproduces the original 4-ledger
# subset. THE SCOPE IS PART OF THE OUTPUT FILE NAME (two scopes must never overwrite each other).
#
# FOUR EXTRACTION STEPS (a two-pass regex is NOT enough -- measured):
#   1) PATH form : `.ai-tmp/<...>` occurrences containing a slash
#   2) BARE form : `name.ext` inline-code tokens without a slash, resolved by basename
#   3) PATTERNS  : split by EXPANDABILITY (CR-U5R's A/B/C), never counted as MISSING:
#        A braces with explicit items (no ellipsis) -> expand NOW, existing members are whitelisted,
#          missing members are reported (not judged red)
#        B braces/globs containing an ellipsis        -> not mechanically expandable -> human
#        C `*` / `?` glob with a directory prefix     -> enumerate the directory, matches whitelisted
#      EXPANSION AND WHITELISTING ARE THE SAME STEP, and it runs before any cleanup: otherwise the
#      files behind `CR-T3-re-{00-baseline,...}.png` look unreferenced and get deleted.
#   A bare name resolving in >1 place is AMBIGUOUS: every candidate is listed and the HUMAN decides.
#   Only CANDIDATES INSIDE THE CLEANUP ZONE (.ai-tmp) are whitelisted -- sprite paths under
#   client/Assets are never deleted, and whitelisting them would only bloat the list. The full
#   candidate set is still printed for the reader.
#   Only a LITERAL path-form ref that (a) has no wildcard/brace/range/line suffix, (b) ends with a
#   file extension, (c) is absent from disk and (d) whose citing line does not say it was deleted,
#   is provably dangling -> that one IS a FAIL.
#
# READ-ONLY on every judged object. The only write is -OutFile / -Land (absolute path resolved:
# a .NET file API uses the PROCESS working directory, which Set-Location does not reliably update).
#
# USAGE
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/cited-evidence-whitelist.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/cited-evidence-whitelist.ps1 -SelfTest
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/cited-evidence-whitelist.ps1 -Scope all -Land
#
# NOTE 1: ASCII-only on purpose -- PowerShell 5.1 parses a .ps1 without a BOM as ANSI/GBK, so a CJK
#         literal here would be corrupted. CJK names/characters are built from code points.
# NOTE 2: DELIMITER: every whitelist entry is written with FORWARD SLASHES, and the header says so.
#         Mixing '\' and '/' made two thirds of a consumer's matches silently miss (CR-R1 measured
#         43 duplicate rows before normalising).
# NOTE 3: name lookups use a PowerShell hashtable (CASE-INSENSITIVE); `-contains`/`-in` are also
#         case-insensitive, so the extension check uses `-cnotcontains`. Note that
#         `$array -cnotcontains ''` is TRUE, i.e. an empty extension can never be accepted.
param(
  [ValidateSet('all', 'f2')][string]$Scope = 'all',
  [switch]$SelfTest,
  [switch]$Land,
  [switch]$Union,
  [string]$UnionWith,
  [string]$Partner,
  [string]$OutFile,
  [switch]$Quiet,
  [int]$MaxList = 25
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # tools/probes -> <root>
$here = $PSScriptRoot
Set-Location $root

$planDir  = Join-Path $root (([char[]]@(0x7B56,0x5212) -join ''))    # ce hua
$assetSrc = ([char[]]@(0x539F,0x7248,0x8D44,0x6E90) -join '')        # yuan ban zi yuan
$evidenceRe  = '^\.ai-tmp[\\/](test|screenshots)[\\/]'
$cleanupZone = '^\.ai-tmp[\\/]'
$ellipsis = ([char]0x2026).ToString()

function Doc([int[]]$cp, [string]$ext) { Join-Path $planDir ((([char[]]$cp) -join '') + $ext) }
if ($Scope -eq 'f2') {
  # NOTE: Get-Item, not the raw path strings -- the rest of the script reads $_.FullName, and a
  # string array made every path empty in the first version ("Empty path name is not legal").
  $docs = @((Doc @(0x9A8C,0x6536,0x8868) '.md'), (Doc @(0x5BF9,0x7167,0x8868) '.md'),
            (Doc @(0x72B6,0x6001,0x77E9,0x9635) '.tsv'), (Doc @(0x5B9E,0x4F53,0x6E05,0x5355) '.tsv')) |
          Where-Object { Test-Path $_ } | ForEach-Object { Get-Item $_ }
} else {
  $docs = @(Get-ChildItem $planDir -Recurse -File -Include '*.md', '*.tsv' -ErrorAction SilentlyContinue)
}
if ($docs.Count -eq 0) { Write-Output 'FAIL  no plan-dir document found'; exit 1 }

$pathRe  = [regex]'\.ai-tmp/[^\s`|<>"'']+'
$codeRe  = [regex]'`([^`\r\n]{1,160})`'
$lineRef = [regex]':\d+(-\d+)?$|#L\d+$'
$placeRe = [regex]'[*?{}]|\.\.'
$extOk   = @('txt','png','jpg','jpeg','json','tsv','log','out','cs','md','ogg','asset','unity','meta','ini','xml')

$cutAscii = [char[]]@(0x3B,0x3A,0x2C,0x29,0x28,0x5D,0x5B,0x7D,0x7B,0x60,0x27,0x22,0x3E,0x3C,0x21,0x7C)
$cutCjk   = [char[]]@(0xFF08,0xFF09,0xFF0C,0xFF1A,0xFF1B,0xFF01,0xFF1F,0x3001,0x3002,0x300A,0x300B,0x201C,0x201D,0x2018,0x2019,0x2014,0x2026)
$cutSet   = ($cutAscii + $cutCjk)
$tailSet  = ($cutSet + [char[]]@(0x2E))

function Trim-Bits([string]$s) {
  $t = $s.Trim()
  for ($i = 1; $i -lt $t.Length; $i++) {
    if ($cutSet -contains $t[$i]) { $t = $t.Substring(0, $i); break }
  }
  while ($t.Length -gt 0 -and $tailSet -contains $t[$t.Length - 1]) { $t = $t.Substring(0, $t.Length - 1) }
  return $t
}

function Get-BraceExpansions([string]$tok) {
  $m = [regex]::Match($tok, '\{([^{}]+)\}')
  if (-not $m.Success) { return @($tok) }
  $pre = $tok.Substring(0, $m.Index); $post = $tok.Substring($m.Index + $m.Length)
  $out = @()
  foreach ($part in ($m.Groups[1].Value -split ',')) { $out += Get-BraceExpansions ($pre + $part.Trim() + $post) }
  return $out
}
function Normalize([string]$p) { return ($p -replace '\\', '/') }

$indexRoots = @('.ai-tmp', 'client\Assets', 'client\logs', 'client\ProjectSettings', 'tools', 'server', 'client\Packages', ($planDir.Substring($root.Length + 1)), $assetSrc)
# cr-assets-png is the raw original sprite pack (tens of thousands of files, never cited by name).
$skipRe = '\\(Library|obj|bin|node_modules|\.git|__pycache__|Temp|cr-assets-png)\\|\.g\.cs$'

function Build-Index() {
  $idx = @{}
  foreach ($r in $indexRoots) {
    if (-not (Test-Path $r)) { continue }
    Get-ChildItem $r -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
      if ($_.FullName -match $skipRe) { return }
      if (-not $idx.ContainsKey($_.Name)) { $idx[$_.Name] = New-Object System.Collections.Generic.List[string] }
      $idx[$_.Name].Add((Normalize $_.FullName.Substring($root.Length).TrimStart('\', '/')))
    }
  }
  return $idx
}

function Invoke-Extract([string]$Text, $Index) {
  $pathRefs = @{}; $bareRefs = @{}; $patterns = @{}; $pathLine = @{}; $hadLineSuffix = @{}
  $whitelist = New-Object System.Collections.Generic.List[string]
  $missing   = New-Object System.Collections.Generic.List[string]
  $deleted   = New-Object System.Collections.Generic.List[string]
  $unresolved= New-Object System.Collections.Generic.List[string]
  $ambiguous = New-Object System.Collections.Generic.List[string]
  $outside   = New-Object System.Collections.Generic.List[string]
  $patA = New-Object System.Collections.Generic.List[string]
  $patB = New-Object System.Collections.Generic.List[string]
  $patC = New-Object System.Collections.Generic.List[string]
  $expMissing = New-Object System.Collections.Generic.List[string]

  $braceTokens = 0; $braceExpanded = @{}
  foreach ($m in ($pathRe.Matches($Text))) {
    $ls = $Text.LastIndexOf("`n", [Math]::Max(0, $m.Index - 1)); $le = $Text.IndexOf("`n", $m.Index)
    if ($le -lt 0) { $le = $Text.Length }
    $line = $Text.Substring([Math]::Max(0, $ls + 1), [Math]::Max(0, $le - $ls - 1))
    # ORDER MATTERS: expand braces on the RAW token first (Trim-Bits cuts at '{'), then trim.
    $hadLine = $lineRef.IsMatch($m.Value.Trim()) -or ($m.Value -match ':\d')
    if ($m.Value.Contains('{')) { $braceTokens++ }
    # ellipsis is checked on the RAW token: Trim-Bits cuts at U+2026, so after trimming the marker
    # is already gone (measured -- the first version lost all ellipsis patterns that way).
    if ($m.Value.Contains($ellipsis) -or $m.Value.Contains('...')) {
      # only a FILE pattern belongs in bucket B: without this gate the bucket filled up with prose
      # and code snippets that merely contain an ellipsis (measured: `Emit(..., 1f)`,
      # `CreateBoxRect(..., style.TrackColor)`, `Hint '...' size=24`), which inflated the count.
      # The shape is judged AFTER masking the ellipsis, so a real `…png` tail is still recognised.
      $shape = Trim-Bits ((($m.Value -replace [regex]::Escape($ellipsis), 'X') -replace '\.\.\.', 'X'))
      if ($shape.Length -gt 9 -and ($shape -match '\.[A-Za-z0-9]{1,6}$' -or $shape.Contains('{')) -and $shape -notmatch '["'':]') {
        if (-not $patB.Contains($m.Value.Trim())) { $patB.Add($m.Value.Trim()) }
      }
      continue
    }
    $fromBrace = $m.Value.Contains('{')
    foreach ($x in (Get-BraceExpansions $m.Value.Trim())) {
      $raw = Trim-Bits $x
      if ($raw.Length -le 9) { continue }
      if ($fromBrace) {
        # a brace-form PATH ref is resolved RIGHT HERE: existing members go into the whitelist
        # (ruling: expansion and whitelisting are the same step, before any cleanup) and missing
        # members are REPORTED -- never MISSING, because the pattern itself was never a literal.
        $braceExpanded[$raw] = 1
        if (Test-Path (Join-Path $root ($raw -replace '/', '\')) -PathType Leaf) { $whitelist.Add((Normalize $raw)) }
        else { $expMissing.Add(("{0}  (from brace pattern {1})" -f (Normalize $raw), $m.Value.Trim())) }
        continue
      }
      if ($placeRe.IsMatch($raw)) {
        if ($raw.Contains($ellipsis) -or $raw.Contains('...')) { if (-not $patB.Contains($raw)) { $patB.Add($raw) }; continue }
        if ($raw.Contains('{')) { if (-not $patA.Contains($raw)) { $patA.Add($raw) }; continue }
        if ($raw.Contains('*') -or $raw.Contains('?')) { if (-not $patC.Contains($raw)) { $patC.Add($raw) }; continue }
        if (-not $patterns.ContainsKey($raw)) { $patterns[$raw] = 0 }
        $patterns[$raw]++; continue
      }
      if (-not $pathRefs.ContainsKey($raw)) { $pathRefs[$raw] = 0; $pathLine[$raw] = $line; $hadLineSuffix[$raw] = $hadLine }
      $pathRefs[$raw]++
    }
  }
  foreach ($m in $codeRe.Matches($Text)) {
    $tok = $m.Groups[1].Value.Trim()
    if ($tok.Length -eq 0) { continue }
    if ($tok -match '[/\\]') { continue }
    if ($tok.Contains($ellipsis) -or $tok.Contains('...')) {
      # same file-shape gate as the path branch: prose snippets with an ellipsis are not citations
      $shape = ($tok -replace [regex]::Escape($ellipsis), 'X') -replace '\.\.\.', 'X'
      if (($shape -match '\.[A-Za-z0-9]{1,6}$' -or $shape.Contains('{')) -and $shape -notmatch '["'':]') {
        if (-not $patB.Contains($tok)) { $patB.Add($tok) }
      }
      continue
    }
    # a brace form only counts as a file pattern when it really looks like `name{a,b}.ext` -- prose
    # snippets such as `recompile_status {"failed":false}` are not patterns (measured noise).
    if ($tok.Contains('{')) {
      if ($tok -match '^[A-Za-z0-9_\-\.]+\{[^{}]+\}\.[A-Za-z0-9]{1,6}$') { if (-not $patA.Contains($tok)) { $patA.Add($tok) } }
      elseif ($tok -match '["'':]') { continue }   # prose / JSON snippet, not a citation at all
      else { if (-not $patB.Contains($tok)) { $patB.Add($tok) } }
      continue
    }
    if ($tok.Contains('*') -or $tok.Contains('?')) { if (-not $patC.Contains($tok)) { $patC.Add($tok) }; continue }
    $tok = $lineRef.Replace($tok, '')
    foreach ($n in (Get-BraceExpansions $tok)) {
      if ($n -match '[^\x20-\x7E\{\}]') { continue }
      if ($n -match '\.\.') { continue }
      if ($n -match '^[^A-Za-z0-9_]') { continue }
      $mm = [regex]::Match($n, '^([A-Za-z0-9_\-\.]+)\.([A-Za-z0-9]{1,6})$')
      if (-not $mm.Success) { continue }
      # extension must already be lowercase (`Debug.Log` is code, not a file); -cnotcontains is
      # CASE-SENSITIVE and an empty extension is never accepted.
      if ($extOk -cnotcontains $mm.Groups[2].Value) { continue }
      if ($mm.Groups[1].Value.Length -eq 0) { continue }
      if (-not $bareRefs.ContainsKey($n)) { $bareRefs[$n] = 0 }
      $bareRefs[$n]++
    }
  }

  # ---- PLAIN-TEXT bare names (CR-R1 found 6 files that ONLY their tool protected) ----
  # The table cells cite file names WITHOUT backticks (e.g. `panelStep5;AI2-04-roomlist.png`), so a
  # backtick-only extractor misses them and the cleanup would delete them. The scan is
  # RESOLUTION-GATED: a plain token is accepted only when it resolves to an existing file under the
  # cleanup zone, which keeps the false-positive rate at zero (source names like battle.go or asset
  # names like frame_015.png resolve elsewhere and are ignored).
  $plainNames = @{}
  $plainRe = [regex]('(?<![A-Za-z0-9_\-\./\\])([A-Za-z0-9_][A-Za-z0-9_\-\.]*\.(' + ($extOk -join '|') + '))')
  foreach ($m in $plainRe.Matches($Text)) {
    $n = $m.Groups[1].Value
    if ($n -match '\.\.') { continue }
    if (-not $Index.ContainsKey($n)) { continue }
    $ev = @($Index[$n] | Where-Object { $_ -match $cleanupZone })
    if ($ev.Count -eq 0) { continue }
    $plainNames[$n] = 1
    if (-not $bareRefs.ContainsKey($n)) { $bareRefs[$n] = 0 }
    $bareRefs[$n]++
  }

  $dirs = New-Object System.Collections.Generic.List[string]
  foreach ($p in ($pathRefs.Keys | Sort-Object)) {
    $full = Join-Path $root ($p -replace '/', '\')
    if (Test-Path $full -PathType Leaf) { $whitelist.Add((Normalize $p)); continue }
    if (Test-Path $full -PathType Container) { $dirs.Add(("{0}  (cited {1}x)" -f $p, $pathRefs[$p])); continue }
    if ($hadLineSuffix[$p]) { $patterns[$p] = $pathRefs[$p]; continue }   # ':line' -> pattern, not missing
    if ($p -notmatch '\.[A-Za-z0-9]{1,6}$') { $patterns[$p] = $pathRefs[$p]; continue }  # truncated -> pattern
    $ln = [string]$pathLine[$p]
    $mDel = ([char]0x5220).ToString() + ([char]0x9664).ToString()                              # shan chu
    $mOne = ([char]0x4E00).ToString() + ([char]0x6B21).ToString() + ([char]0x6027).ToString()  # yi ci xing
    if ($ln.Contains($mDel) -or $ln.Contains($mOne)) {
      $deleted.Add(("{0}  (cited {1}x)  [citing line says deleted/one-off]" -f $p, $pathRefs[$p]))
    } else {
      $missing.Add(("{0}  (cited {1}x)" -f $p, $pathRefs[$p]))
    }
  }

  $inEvidence = 0
  foreach ($n in ($bareRefs.Keys | Sort-Object)) {
    $hits = @(); if ($Index.ContainsKey($n)) { $hits = @($Index[$n]) }
    $ev = @($hits | Where-Object { $_ -match $evidenceRe })
    # only candidates inside the CLEANUP ZONE are whitelisted (CR-R1: the rest bloated the list by
    # 715 rows); every candidate is still printed for the reader.
    $keep = @($hits | Where-Object { $_ -match $cleanupZone })
    foreach ($k in $keep) { $whitelist.Add((Normalize $k)) }
    if ($ev.Count -ge 1) { $inEvidence++ }
    if ($hits.Count -ge 2) {
      $show = @($hits | Select-Object -First 5) -join ' | '
      if ($hits.Count -gt 5) { $show += ('  ... (+' + ($hits.Count - 5) + ' more, all inside the cleanup zone are whitelisted)') }
      $ambiguous.Add(("{0}  ({1} candidates)  -> {2}" -f $n, $hits.Count, $show))
    } elseif ($hits.Count -eq 1) {
      if ($ev.Count -eq 0) { $outside.Add(("{0}  -> {1}" -f $n, $hits[0])) }
    } else {
      $unresolved.Add(("{0}  (cited {1}x)" -f $n, $bareRefs[$n]))
    }
  }

  # ---- patterns A / C expand INTO the whitelist; B is human-only ----
  foreach ($k in ($patA | Sort-Object)) {
    $exp = @(Get-BraceExpansions $k) | Where-Object { $_ -match '^[A-Za-z0-9_\-\./\\]+$' }
    $okN = 0; $gone = @()
    foreach ($e in $exp) {
      if (Test-Path (Join-Path $root ($e -replace '/', '\')) -PathType Leaf) { $whitelist.Add((Normalize $e)); $okN++ }
      else { $gone += $e }
    }
    foreach ($g in $gone) { $expMissing.Add(("{0}  (from pattern {1})" -f (Normalize $g), $k)) }
  }
  # NOTE: a `{}` inside a PATH citation is expanded BEFORE bucketing, so those files enter STEP1 /
  # the whitelist as the real names they denote; $braceTokens/$braceExpanded keep that visible so
  # two implementations can compare "how many refs were brace-form" on the same footing.
  $expandedA = New-Object System.Collections.Generic.List[string]
  foreach ($k in ($patA | Sort-Object)) {
    $exp = @(Get-BraceExpansions $k) | Where-Object { $_ -match '^[A-Za-z0-9_\-\./\\]+$' }
    $okN = @($exp | Where-Object { Test-Path (Join-Path $root ($_ -replace '/', '\')) -PathType Leaf }).Count
    $bad = $exp.Count - $okN
    $expandedA.Add(("{0}  [{1} member(s) expanded, {2} whitelisted{3}]" -f $k, $exp.Count, $okN, $(if ($bad -gt 0) { ', ' + $bad + ' not on disk' } else { '' })))
  }
  foreach ($k in ($patC | Sort-Object)) {
    $dir = Split-Path ($k -replace '/', '\') -Parent
    $leaf = Split-Path ($k -replace '/', '\') -Leaf
    $hits = @()
    if ($dir -and (Test-Path (Join-Path $root $dir))) {
      $hits = @(Get-ChildItem (Join-Path $root $dir) -Filter $leaf -File -ErrorAction SilentlyContinue |
                ForEach-Object { Normalize $_.FullName.Substring($root.Length).TrimStart('\', '/') } |
                Where-Object { $_ -match $cleanupZone })
    }
    foreach ($h in $hits) { $whitelist.Add($h) }
    $expandedA.Add(("{0}  [{1} file(s) matched on disk and whitelisted]" -f $k, $hits.Count))
  }

  return [pscustomobject]@{
    PathRefs = $pathRefs; BareRefs = $bareRefs
    BraceTokens = $braceTokens; BraceExpanded = $braceExpanded.Count; PlainNames = $plainNames.Count
    InEvidence = $inEvidence; DirRefs = $dirs; Deleted = $deleted; PatA = $expandedA; PatB = @($patB | Sort-Object)
    Truncated = @($patterns.Keys | Sort-Object)
    Whitelist = @($whitelist | Sort-Object -Unique); Missing = $missing
    Unresolved = $unresolved; Ambiguous = $ambiguous; Outside = $outside; ExpMissing = $expMissing
  }
}

$index = Build-Index
$text  = ($docs | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8) }) -join "`n"
$withRefs = @($docs | Where-Object { [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8).Contains('.ai-tmp') }).Count
$r     = Invoke-Extract $text $index
$bsEntries = @($r.Whitelist | Where-Object { $_.Contains('\') }).Count
$outEntries = @($r.Whitelist | Where-Object { $_ -notmatch $cleanupZone }).Count

if (-not $Quiet) {
  Write-Output ('root      = ' + $root)
  Write-Output ('scope     = ' + $Scope + ' : ' + $docs.Count + ' file(s) scanned, ' + $withRefs + ' of them cite .ai-tmp')
  Write-Output ('index     = ' + $index.Count + ' distinct file name(s) across ' + $indexRoots.Count + ' root(s)')
  Write-Output ('delimiter = forward slash  (entries containing a backslash: ' + $bsEntries + ' ; entries outside the cleanup zone: ' + $outEntries + ')')
  Write-Output ''
  Write-Output ('STEP1 literal path refs    = ' + $r.PathRefs.Count + '  (exist ' + ($r.PathRefs.Count - $r.Missing.Count - $r.Deleted.Count) + ', absent-but-documented ' + $r.Deleted.Count + ', MISSING ' + $r.Missing.Count + ')')
  Write-Output ('STEP2 bare-name refs       = ' + $r.BareRefs.Count + '  (hit the evidence area ' + $r.InEvidence + ', single hit elsewhere ' + $r.Outside.Count + ', unresolved ' + $r.Unresolved.Count + ')')
  Write-Output ('STEP2b AMBIGUOUS bare      = ' + $r.Ambiguous.Count)
  Write-Output ('STEP2c plain-text bare     = ' + $r.PlainNames + '  (table cells cite names without backticks; accepted only when they resolve inside the cleanup zone)')
  Write-Output ('STEP3 pattern refs         = ' + ($r.PatA.Count + $r.PatB.Count) + '  (A expandable ' + $r.PatA.Count + ', B ellipsis/human ' + $r.PatB.Count + ') -- none counted as MISSING')
  Write-Output ('STEP3a brace-form PATH refs= ' + $r.BraceTokens + ' token(s) -> ' + $r.BraceExpanded + ' expanded name(s) (expanded BEFORE bucketing, so they enter STEP1 and the whitelist as real names)')
  Write-Output ('WHITELIST entries (uniq)   = ' + $r.Whitelist.Count + '  (files inside the cleanup zone only)')
  Write-Output ('INFO dir-form path refs    = ' + $r.DirRefs.Count)
  Write-Output ''
  Write-Output '--- FAIL: literal path citation absent from disk, no pattern, citing line does not say deleted ---'
  if ($r.Missing.Count -eq 0) { Write-Output '  (none)' } else { $r.Missing | Select-Object -First $MaxList | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- INFO: absent from disk but the citing line explicitly says deleted / one-off (NOT a defect) ---'
  if ($r.Deleted.Count -eq 0) { Write-Output '  (none)' } else { $r.Deleted | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- HUMAN: pattern refs A (brace list, expandable -- members already whitelisted) ---'
  if ($r.PatA.Count -eq 0) { Write-Output '  (none)' } else { $r.PatA | Select-Object -First $MaxList | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- INFO: expanded members that are NOT on disk (reported, never judged red) ---'
  if ($r.ExpMissing.Count -eq 0) { Write-Output '  (none)' } else { $r.ExpMissing | Select-Object -First $MaxList | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- HUMAN: truncated path refs (no file extension after trimming -- prose cut, not a file) ---'
  if ($r.Truncated.Count -eq 0) { Write-Output '  (none)' } else { $r.Truncated | Select-Object -First $MaxList | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- HUMAN: pattern refs B (ellipsis -> not mechanically expandable) ---'
  if ($r.PatB.Count -eq 0) { Write-Output '  (none)' } else { $r.PatB | Select-Object -First $MaxList | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- HUMAN: unresolved bare names (NOT a verdict -- source / asset / pattern / truly dangling) ---'
  if ($r.Unresolved.Count -eq 0) { Write-Output '  (none)' } else { $r.Unresolved | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- HUMAN: ambiguous bare names (every candidate listed; cleanup-zone candidates whitelisted) ---'
  if ($r.Ambiguous.Count -eq 0) { Write-Output '  (none)' } else { $r.Ambiguous | Select-Object -First $MaxList | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output '--- INFO: bare names with a single hit outside the cleanup zone (source / asset names) ---'
  if ($r.Outside.Count -eq 0) { Write-Output '  (none)' } else { $r.Outside | Select-Object -First $MaxList | ForEach-Object { Write-Output ('  ' + $_) } }
  Write-Output ''
  Write-Output ('WHITELIST (first 12 of ' + $r.Whitelist.Count + '):')
  $r.Whitelist | Select-Object -First 12 | ForEach-Object { Write-Output ('  ' + $_) }
}

if ($Land -and -not $OutFile) { $OutFile = Join-Path $here ('cited-evidence-whitelist-' + $Scope + '.txt') }
if ($OutFile) {
  $abs = if ([System.IO.Path]::IsPathRooted($OutFile)) { $OutFile } else { [System.IO.Path]::GetFullPath((Join-Path $root $OutFile)) }
  $asm = Join-Path $root 'client\Library\ScriptAssemblies\CR.dll'
  $asmSha = if (Test-Path $asm) { (Get-FileHash $asm -Algorithm SHA256).Hash.Substring(0, 16) } else { 'n/a' }
  # ONE header line on purpose: PowerShell flattens a string[] argument into one space-joined line
  # for some .NET overloads (measured: three '# ...' elements arrived as a single line).
  # SELF-DESCRIBING CONSUMPTION CONTRACT (team-lead ruling). Tonight's third "one thing, two shapes,
  # consumer sees one" trap -- delimiters, path-form vs bare-name, scope vs shape -- so the header
  # tells the future cleanup author EXACTLY how to consume this file.
  $cmd = 'powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/cited-evidence-whitelist.ps1 -Scope ' + $Scope + ' -Land'
  $hdr = New-Object System.Collections.Generic.List[string]
  $hdr.Add('# cited-evidence-whitelist  scope=' + $Scope + '  entries=' + $r.Whitelist.Count +
           '  --  MACHINE-GENERATED DERIVATIVE, NOT evidence, do NOT hand-edit; regenerate with: ' + $cmd)
  $hdr.Add('# KINDS: (a) path-form citations taken verbatim from the ledger docs; (b) bare-name citations resolved to their real path under .ai-tmp/test or .ai-tmp/screenshots;' +
           ' (c) files reached by expanding brace/glob patterns (pattern expansion happens before any cleanup). For an AMBIGUOUS bare name (same name in >1 place) EVERY cleanup-zone candidate is listed in this file.')
  $hdr.Add('# DELIMITER: forward slash only (0 entries contain a backslash).  CONSUMER CONTRACT: match these lines as FILE-NAME LITERALS -- do NOT re-derive citations with your own regex,' +
           ' because path-form and bare-name references are two different shapes and each implementation then silently misses half of them (measured: 40 bare-name cites + 43 duplicate rows).')
  # DEFINITIONS pinned in the header (CR-R1's suggestion): without them two implementations spend
  # rounds explaining number differences instead of comparing facts.
  $hdr.Add('# DEFINITIONS (pinned so two implementations never argue about numbers again): ' +
           'MISSING = a LITERAL path-form ref that ends with a file extension, contains no brace/glob/' +
           'range/:line form, is ABSENT from disk AND whose citing line does not say deleted/one-off. ' +
           'PATTERN BUCKETS (A/B) count pattern tokens found ANYWHERE in the documents (path-form AND ' +
           'bare-name), not only inside the .ai-tmp path tails. BARE NAMES = backticked inline-code tokens PLUS ' +
           'plain-text tokens (table cells cite names without backticks) that RESOLVE to an existing file ' +
           'inside the cleanup zone; the extension must be lowercase and in a fixed set, so code ' +
           'identifiers such as Debug.Log are excluded.')
  $hdr.Add('# cleanup at ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' | CR.dll sha256_16=' + $asmSha +
           ' | CLEANUP RULE: every EXISTING citation is protected, INCLUDING refs pointing into .ai-tmp/drivers (keep them)' +
           ' | WRITING RULE: a NEW row must NOT cite .ai-tmp/drivers' +
           ' | one-off area DOES NOT mean deletable: the delivery docs cite files under .ai-tmp/test')
  $allLines = New-Object System.Collections.Generic.List[string]
  foreach ($h in $hdr) { $allLines.Add($h) }
  foreach ($e in $r.Whitelist) { $allLines.Add($e) }
  [System.IO.File]::WriteAllLines($abs, $allLines, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ('WHITELIST written: ' + $abs + '  (' + $r.Whitelist.Count + ' entries, ' + $hdr.Count + ' contract lines)')
}

# ---------------------------------------------------------------------------
# UNION with a second, independently written implementation (CR-R1's).
# Rationale: the two tools are each conservative in DIFFERENT places (measured: 19 entries only
# here, 6 only there), and at cleanup time "keeping a file costs nothing, deleting a cited one
# dangles a reference" -- the cost is asymmetric, so the cleanup consumes the UNION.
# The union fails LOUDLY when the partner file is missing or suspiciously small: a silently
# halved union would be worse than none.
# ---------------------------------------------------------------------------
if ($Union) {
  # -UnionWith <path> is the documented switch; -Partner is kept as a legacy alias.
  $otherPath = if ($UnionWith) { $UnionWith } elseif ($Partner) { $Partner } else { Join-Path $here ('CR-R1-crosscheck-whitelist-' + $Scope + '.txt') }
  $partnerPath = $otherPath   # NOTE: do NOT name this local `$unionWith` -- it would BE the param
  if (-not (Test-Path $partnerPath)) { Write-Output ('UNION FAIL: partner whitelist not found: ' + $partnerPath); exit 3 }
  # NOTE: the local is $partnerEntries, NOT $partner -- `$partner` would BE the [string] parameter
  # `$Partner` (PowerShell variables are case-insensitive), so the array would be coerced to a
  # single string and the union would silently contain one entry. Same family as CR-R1's $p/$P.
  $partnerEntries = @(Get-Content $partnerPath -Encoding UTF8 -ErrorAction SilentlyContinue |
               Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^#' } |
               ForEach-Object { Normalize $_.Trim() })
  if ($partnerEntries.Count -lt 10) { Write-Output ('UNION FAIL: partner whitelist suspiciously small (' + $partnerEntries.Count + ' entries)'); exit 3 }
  $partnerHash = (Get-FileHash $partnerPath -Algorithm SHA256).Hash
  $onlyAList = @($r.Whitelist | Where-Object { $partnerEntries -notcontains $_ })
  $onlyBList = @($partnerEntries | Where-Object { $r.Whitelist -notcontains $_ })
  $onlyA = $onlyAList.Count
  $onlyB = $onlyBList.Count
  # per-class residual breakdown (team-lead ruling): the residuals are not errors, they are the two
  # implementations being conservative in DIFFERENT places, so they must be named, not just counted.
  $clsDrivers = @($onlyAList | Where-Object { $_ -match '^\.ai-tmp/drivers/' }).Count
  $clsSubdir  = @($onlyAList | Where-Object { $_ -match '^\.ai-tmp/(test|screenshots)/.+/' }).Count
  $clsOther   = $onlyA - $clsDrivers - $clsSubdir
  $partitionNote = 'A-only ' + $onlyA + ', B-only ' + $onlyB + ', shared ' + ($r.Whitelist.Count - $onlyA)
  $otherItem = Get-Item $partnerPath
  $otherAsof = $otherItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
  # explicit loop instead of array+array: concatenating two arrays inside @() with a pipeline threw
  # "MetadataError / ParentContainsErrorRecordException" (measured), and a silent union failure is
  # exactly the failure mode this file is supposed to prevent.
  # NAMING RULE LEARNED THE HARD WAY: do NOT name a local `$union` / `$partner` -- those ARE the
  # [switch]$Union / [string]$Partner parameters (PowerShell variables are case-insensitive), so the
  # assignment is coerced and dies with
  #   "Cannot convert value "System.Object[]" to type "System.Management.Automation.SwitchParameter""
  # reported at this very line. Twice in one file tonight; hence $unionEntries / $partnerEntries.
  $uSet = @{}
  foreach ($e in @($r.Whitelist)) { if ($e -match $cleanupZone) { $uSet[[string]$e] = 1 } }
  foreach ($e in @($partnerEntries)) { if ($e -match $cleanupZone) { $uSet[[string]$e] = 1 } }
  $unionEntries = @($uSet.Keys | Sort-Object)
  $uAbs = Join-Path $here ('cited-evidence-whitelist-UNION-' + $Scope + '.txt')
  $uCmd = 'powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/cited-evidence-whitelist.ps1 -Scope ' + $Scope + ' -Land -Union'
  $uh = New-Object System.Collections.Generic.List[string]
  $uh.Add('# cited-evidence-whitelist UNION  scope=' + $Scope + '  entries=' + $unionEntries.Count + '  -- MACHINE-GENERATED, do NOT hand-edit; regenerate: ' + $uCmd)
  $uh.Add('# SOURCES: (A) cited-evidence-whitelist-' + $Scope + '.txt  as-of ' + (Get-Item (Join-Path $here ('cited-evidence-whitelist-' + $Scope + '.txt'))).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') +
           '  sha256_16=' + (Get-FileHash (Join-Path $here ('cited-evidence-whitelist-' + $Scope + '.txt')) -Algorithm SHA256).Hash.Substring(0, 16) +
           '  [entries ' + $r.Whitelist.Count + ']' +
           ' || (B) independent implementation ' + (Split-Path $partnerPath -Leaf) + '  as-of ' + $otherAsof +
           '  sha256_16=' + $partnerHash.Substring(0, 16) + '  [entries ' + $partnerEntries.Count + ']')
  $uh.Add('# WHY THE UNION: at cleanup time keeping a file costs NOTHING while deleting a cited one creates a dangling reference --'
           + ' the cost is asymmetric, so the conservative direction is the union. It is also necessary because the two'
           + ' implementations are conservative in DIFFERENT places (measured: ' + $onlyA + ' entries only in A, ' + $onlyB + ' only in B).'
           + ' Evidence that this is not hypothetical: the independent implementation missed 15 real cited files whose citations sit in a'
           + ' SUBDIRECTORY of the evidence area (its tokenizer split at the path separator) and it could not have found that with its own'
           + ' implementation alone. One correction per side: a side that only fixes the criticised tool and not the critic lets the rule rot.')
  $uh.Add('# RESIDUAL CLASSES (A-only ' + $onlyA + '): refs into .ai-tmp/drivers ' + $clsDrivers + ' | files inside a subdirectory of the evidence area ' + $clsSubdir + ' | other ' + $clsOther +
           ' || (B-only ' + $onlyB + '): bare names the other extractor resolved into the evidence area')
  $uh.Add('# KINDS / DELIMITER / DEFINITIONS: identical to the single-source file (see cited-evidence-whitelist-' + $Scope + '.txt).')
  $uh.Add('# CONSUMER CONTRACT: match these lines as FILE-NAME LITERALS, forward slashes only -- do NOT re-derive citations with your own regex; two shapes (path-form vs bare-name) make each implementation silently miss half of them, and each extractor is conservative in a different place.')
  $uh.Add('# union at ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' | CR.dll sha256_16=' + $asmSha +
           ' | CLEANUP RULE: every EXISTING citation is protected | WRITING RULE: a NEW row must NOT cite .ai-tmp/drivers')
  $uAll = New-Object System.Collections.Generic.List[string]
  foreach ($h in $uh) { $uAll.Add($h) }
  foreach ($e in $unionEntries) { $uAll.Add($e) }
  [System.IO.File]::WriteAllLines($uAbs, $uAll, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ('UNION written: ' + $uAbs + '  (' + $unionEntries.Count + ' entries = A ' + $r.Whitelist.Count + ' + B ' + $partnerEntries.Count + ')')
}

# ---------------------------------------------------------------------------
# self-test: probes injected into the TEXT ONLY, then the very same pipeline is re-run.
# ---------------------------------------------------------------------------
if ($SelfTest) {
  Write-Output ''
  Write-Output '=== SELF-TEST (probes injected in memory only; the same pipeline is re-run) ==='
  $probeBare = 'cr-f2-zzz-no-such-file-probe.txt'
  $probePath = '.ai-tmp/test/cr-f2-zzz-no-such-path-probe.txt'
  $extAlt = ($extOk -join '|')
  $ambName = @($index.Keys | Where-Object {
      if (-not ($_ -cmatch ('^[A-Za-z0-9_][A-Za-z0-9_\-\.]*\.(' + $extAlt + ')$'))) { return $false }
      $h = @($index[$_]); if ($h.Count -lt 2) { return $false }
      @($h | Where-Object { $_ -match $evidenceRe }).Count -ge 1
    } | Sort-Object)[0]
  $probeAmb = if ($ambName) { $ambName } else { 'cr-f2-zzz-no-duplicate-found.md' }
  # A/C expansion probes: one brace pattern whose members DO exist (known-good) and one whose
  # members do NOT (known-bad) -- a test that cannot fail proves nothing.
  $probeGoodPat = 'CR-U5-units-bind-{0,7,14,21,28,35}.tsv'
  $goodPatFiles = @((Get-BraceExpansions ('.ai-tmp/test/' + $probeGoodPat))) |
                  Where-Object { Test-Path (Join-Path $root ($_ -replace '/', '\')) }
  $probeBadPat = 'cr-f2-zzz-{1,2}.txt'

  $r2 = Invoke-Extract ($text + "`n``$probeBare`` ``$probePath`` ``$probeAmb`` ``.ai-tmp/test/$probeGoodPat`` ``.ai-tmp/test/$probeBadPat``") $index
  $ok = $true

  if (@($r2.Unresolved | Where-Object { $_ -like ($probeBare + '*') }).Count -ge 1) {
    Write-Output ('  PASS  nonexistent BARE name is reported as unresolved: ' + $probeBare)
  } else { Write-Output ('  FAIL  nonexistent bare name was silently dropped: ' + $probeBare); $ok = $false }
  if (@($r2.Missing | Where-Object { $_ -like ($probePath + '*') }).Count -ge 1) {
    Write-Output ('  PASS  nonexistent literal PATH citation is reported as missing: ' + $probePath)
  } else { Write-Output ('  FAIL  nonexistent path citation was silently dropped: ' + $probePath); $ok = $false }

  $ambHit = @($r2.Ambiguous | Where-Object { $_ -like ($probeAmb + '  (*') })
  if ($ambHit.Count -ge 1) { Write-Output ('  PASS  AMBIGUOUS bare name lists candidates and picks none: ' + $probeAmb) }
  else { Write-Output ('  FAIL  ambiguity probe did not trigger: ' + $probeAmb); $ok = $false }

  # pattern A: known-good members must land IN THE WHITELIST (not merely "not missing")
  $inWl = @($r2.Whitelist | Where-Object { $_ -like ('*' + $probeGoodPat.Substring(0, 12) + '*') }).Count
  if ($goodPatFiles.Count -ge 1 -and $inWl -ge $goodPatFiles.Count) {
    Write-Output ('  PASS  brace pattern A expansion IS whitelisted: ' + $probeGoodPat + ' -> ' + $inWl + ' entr(ies)')
  } else { Write-Output ('  FAIL  brace pattern A expansion missing from the whitelist: ' + $probeGoodPat + ' (files=' + $goodPatFiles.Count + ' wl=' + $inWl + ')'); $ok = $false }

  # pattern A: known-bad members must be REPORTED and must NOT be whitelisted
  $badInWl = @($r2.Whitelist | Where-Object { $_ -like '*cr-f2-zzz-*' }).Count
  $badReported = @($r2.ExpMissing | Where-Object { $_ -like '*cr-f2-zzz-*' }).Count
  if ($badInWl -eq 0 -and $badReported -ge 2) {
    Write-Output ('  PASS  nonexistent brace members are reported (' + $badReported + ') and NOT whitelisted')
  } else { Write-Output ('  FAIL  brace bad-member handling: whitelisted=' + $badInWl + ' reported=' + $badReported); $ok = $false }

  # pattern B: ellipsis must be human-only -- never missing, never expanded
  $bProbe = 'cr-f2-zzz-' + ([char]0x2026).ToString() + 'x.png'
  $r3 = Invoke-Extract ($text + "`n``.ai-tmp/test/$bProbe``") $index
  $bHit = @($r3.PatB | Where-Object { $_ -like ('*cr-f2-zzz*' ) }).Count
  $bMissing = @($r3.Missing | Where-Object { $_ -like '*cr-f2-zzz*' }).Count
  if ($bHit -ge 1 -and $bMissing -eq 0) { Write-Output ('  PASS  ellipsis pattern goes to bucket B and never to MISSING: ' + $bProbe) }
  else { Write-Output ('  FAIL  ellipsis handling: B=' + $bHit + ' missing=' + $bMissing); $ok = $false }

  if (@($r2.Whitelist | Where-Object { $_ -like '*zzz-no-such*' }).Count -eq 0) {
    Write-Output '  PASS  no probe leaked a nonexistent file into the whitelist'
  } else { Write-Output '  FAIL  a nonexistent probe leaked into the whitelist'; $ok = $false }

  Write-Output ('SELF-TEST ' + $(if ($ok) { 'PASS' } else { 'FAIL' }))
  Write-Output ('CORPUS   : ' + $r.Missing.Count + ' literal citation(s) absent without a deleted-note (the exit code reflects THIS, not the self-test)')
  if (-not $ok) { exit 2 }
}

if ($r.Missing.Count -gt 0) { exit 1 }
exit 0
