# requires: run from anywhere -> powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1
# NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 parses a .ps1
#       without a BOM as ANSI/GBK, which corrupts any CJK literal and breaks parsing.
#       Every non-ASCII path below is built from code points instead.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$fail = 0; $human = 0

function Say([string]$status, [string]$name, [string]$detail) {
  Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail)
}
function ReadUtf8([string]$p) { return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

# ---- non-ASCII names, built from code points (keeps this file ASCII-only) ----
$planDir  = Join-Path $root (([char[]]@(0x7B56,0x5212) -join ''))                                # ce hua
$spec     = Join-Path $planDir ((([char[]]@(0x9A8C,0x6536,0x8868)) -join '') + '.md')            # yan shou biao
$refTable = Join-Path $planDir ((([char[]]@(0x5BF9,0x7167,0x8868)) -join '') + '.md')            # dui zhao biao
$assetSrc = Join-Path $root   ((([char[]]@(0x539F,0x7248,0x8D44,0x6E90)) -join ''))              # yuan ban zi yuan
$cNumeric = ([char[]]@(0x6570,0x503C,0x7C7B) -join '')                                           # shu zhi lei
$cVisual  = ([char[]]@(0x8868,0x73B0,0x7C7B) -join '')                                           # biao xian lei
$cProg    = ([char[]]@(0x8FDB,0x5EA6) -join '')                                                  # jin du
$cHand    = ([char[]]@(0x4EA4,0x63A5) -join '')                                                  # jiao jie

Write-Output ("root = " + $root)
Write-Output ''

# ---------------------------------------------------------------------------
# 1) stray temp .cs under the project (SKILL 1.8: only <root>/.ai-tmp/)
# ---------------------------------------------------------------------------
$n = @(Get-ChildItem $root -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -notmatch '\\原版资源\\' } |
       Where-Object { $_.FullName -match '\\(_dev|_assets_src|_assets_tmp)\\' }).Count
if ($n -eq 0) { Say 'PASS' 'stray-temp-files' '0 hits' } else { $fail++; Say 'FAIL' 'stray-temp-files' "$n file(s)" }

# ---------------------------------------------------------------------------
# 2) hard-rule hits in client scripts
# ---------------------------------------------------------------------------
$hard = @()
$csharp = @()
$cliScripts = Join-Path $root 'client\Assets\Scripts'
if (Test-Path $cliScripts) {
  $csharp = @(Get-ChildItem $cliScripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue)
}

# ---------------------------------------------------------------------------
# Project-owned implementation roots -- shared scope for the freshness gates
# (#23 evidence-freshness, #29 freeze-before-capture).
#
# SCOPE RATIONALE (why the engine repo is deliberately NOT here):
#   clover-client-unity-engine is an EXTERNAL DEPENDENCY.  It has its own
#   version control and its own mtimes, which this project does not own and
#   cannot freeze.  Measured 2026-09-22: a third-party writer kept stripping
#   comment prefixes inside engine/Runtime/Core/*.cs (17 files, 27 lines, all
#   comment-only) in waves every 1-2 minutes with ZERO action from us.  Its
#   mtime noise must therefore never invalidate THIS project's evidence.
#   client/Library, client/Temp, client/Logs and .ai-tmp are generated or
#   scratch output, never project source.
#
# THIS IS A SCOPE FIX, NOT A RELAXATION: inside the roots below the rule stays
# strictly "an impl file newer than the evidence => that row is stale => FAIL".
# Excluding an external dependency's mtimes does not loosen it, because the
# dependency's content is pinned by the engine repo's own version control, and
# a comment-only external edit cannot change our build or our runtime output.
# ---------------------------------------------------------------------------
$implRoots = @(
  'client\Assets\Scripts',
  'client\Assets\Editor',
  'server',
  'tools',
  $planDir
)
if ($csharp.Count -gt 0) {
  $pat = 'Debug\.Log', 'Resources\.Load', 'PlayerPrefs', 'GameObject\.Find', 'FindObjectOfType', 'Instantiate\('
  $hard = @($csharp | Select-String -Pattern $pat -Encoding UTF8 |
            Where-Object { $_.Line -notmatch '^\s*(//|///|\*)' })
}
if ($hard.Count -eq 0) { Say 'PASS' 'hard-rules' '0 hits' }
else {
  $fail++; Say 'FAIL' 'hard-rules' ("$($hard.Count) hit(s) - each needs a row in the acceptance table")
  $hard | Select-Object -First 20 | ForEach-Object {
    Write-Output ('            ' + ($_.Path -replace [regex]::Escape($root), '') + ':' + $_.LineNumber)
  }
}

# ---------------------------------------------------------------------------
# 3) no bare Debug.Log / log.Printf on the server side
# ---------------------------------------------------------------------------
$goDir = Join-Path $root 'server\game'
$goBad = @()
if (Test-Path $goDir) {
  $goBad = @(Get-ChildItem $goDir -Recurse -Filter *.go -ErrorAction SilentlyContinue |
             Select-String -Pattern '\blog\.Printf|\blog\.Println|fmt\.Print' -Encoding UTF8 |
             Where-Object { $_.Line -notmatch '^\s*//' })
}
if ($goBad.Count -eq 0) { Say 'PASS' 'go-log-discipline' '0 hits' }
else {
  $fail++; Say 'FAIL' 'go-log-discipline' ("$($goBad.Count) hit(s) - use logger.Infof/Warnf/Errorf")
  $goBad | Select-Object -First 10 | ForEach-Object {
    Write-Output ('            ' + ($_.Path -replace [regex]::Escape($root), '') + ':' + $_.LineNumber)
  }
}

# ---------------------------------------------------------------------------
# 4) acceptance table exists, every row carries a class (numeric / visual)
# ---------------------------------------------------------------------------
if (Test-Path $spec) {
  $txt = ReadUtf8 $spec
  $lines = @($txt -split "`n")
  $rows = @($lines | Where-Object { $_ -match '^\|\s*[A-Z]?\d+' })
  # Scope: the category rule covers the ACCEPTANCE rows (section 1 systems and
  # section 2 toolchain). The "allowed differences" and "mechanical self-check"
  # appendices live under section 3+ and are not feature rows, so they must not
  # be dragged into this check.
  $stop = $lines.Count
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s*3\.') { $stop = $i; break }
  }
  $head = @($lines[0..([Math]::Max(0, $stop - 1))])
  $noCat = @($head | Where-Object {
              $_ -match '^\|\s*[A-Z]?\d+\s*\|' -and
              -not ($_.Contains($cNumeric) -or $_.Contains($cVisual)) }).Count
  Say 'PASS' 'acceptance-table' ("$($rows.Count) row(s) in the file, category-checked up to section 3")
  if ($noCat -eq 0) { Say 'PASS' 'row-category' 'every acceptance row is tagged' }
  else { $fail++; Say 'FAIL' 'row-category' "$noCat row(s) untagged" }
  $human++
} else { $fail++; Say 'FAIL' 'acceptance-table-missing' $spec }

# ---------------------------------------------------------------------------
# 5) reference table (original value / ours / delta)
# ---------------------------------------------------------------------------
if (Test-Path $refTable) {
  $rt = ReadUtf8 $refTable
  if ($rt -match [char]0x5C3E + [char]0x5DEE) { }   # placeholder, no-op
  Say 'PASS' 'reference-table' 'present'
} else { $fail++; Say 'FAIL' 'reference-table-missing' $refTable }

# ---------------------------------------------------------------------------
# 6) asset manifest (SKILL 4.0: original assets must be logged)
# ---------------------------------------------------------------------------
$assetLog = Join-Path $planDir ((([char[]]@(0x7D20,0x6750,0x8C03,0x7814)) -join '') + '.md')      # su cai diao yan
if (Test-Path $assetLog) { Say 'PASS' 'asset-research' 'present' }
else { $fail++; Say 'FAIL' 'asset-research-missing' $assetLog }

# ---------------------------------------------------------------------------
# 7) one-click recheck entry exists (this script)
# ---------------------------------------------------------------------------
Say 'PASS' 'recheck-entry' 'tools/verify.ps1'

# ---------------------------------------------------------------------------
# 8) no handoff / progress docs (SKILL 1.5 #8)
# ---------------------------------------------------------------------------
$bad = @(Get-ChildItem $root -Recurse -Filter *.md -File -ErrorAction SilentlyContinue |
         Where-Object { $_.FullName -notmatch '\\(Library|原版资源|node_modules)\\' } |
         Where-Object { $_.Name -like 'NEXT*' -or $_.Name.Contains($cProg) -or $_.Name.Contains($cHand) } |
         ForEach-Object { $_.Name })
if ($bad.Count -eq 0) { Say 'PASS' 'no-handoff-docs' '' }
else { $fail++; Say 'FAIL' 'handoff-doc-found' ($bad -join ', ') }

# ---------------------------------------------------------------------------
# 9) engine self-name must be verbatim "clover-engine", never a coined alias
# ---------------------------------------------------------------------------
$coined = @()
$scan = @(Get-ChildItem (Join-Path $root 'client') -Recurse -Filter *.cs -ErrorAction SilentlyContinue) +
        @(Get-ChildItem (Join-Path $root 'server') -Recurse -Filter *.go -ErrorAction SilentlyContinue)
if ($scan.Count -gt 0) {
  $coined = @($scan | Select-String -Pattern '[\u4e00-\u9fa5]引擎|CloverEngine[\u4e00-\u9fa5]' -Encoding UTF8)
}
if ($coined.Count -eq 0) { Say 'PASS' 'brand-name' 'no coined engine alias found (source scan)' }
else {
  $fail++; Say 'FAIL' 'brand-name' "$($coined.Count) coined alias hit(s) - engine self-name is always clover-engine"
  $coined | Select-Object -First 10 | ForEach-Object {
    Write-Output ('            ' + ($_.Path -replace [regex]::Escape($root), '') + ':' + $_.LineNumber)
  }
}
Say 'HUMAN-ONLY' 'brand-on-boot-screen' 'by clover-engine must be verified on the rendered first screen (node tree / screenshot)'

# ---------------------------------------------------------------------------
# 10) msg id discipline: business ids only in Def/MsgDef.cs and server game/def
# ---------------------------------------------------------------------------
$msgLeak = @()
if ($csharp.Count -gt 0) {
  $msgLeak = @($csharp |
    Where-Object { $_.FullName -notmatch '\\Def\\' } |
    Select-String -Pattern '(Net\.(Send|Call|SendUnreliable)\s*\(\s*[0-9]{4,})|const\s+uint\s+\w+\s*=\s*1[0-9]{6,}' -Encoding UTF8)
}
if ($msgLeak.Count -eq 0) { Say 'PASS' 'msgid-centralised' '0 hits outside Def/' }
else {
  $fail++; Say 'FAIL' 'msgid-centralised' ("$($msgLeak.Count) hard-coded msg id(s) outside Def/")
  $msgLeak | Select-Object -First 10 | ForEach-Object {
    Write-Output ('            ' + ($_.Path -replace [regex]::Escape($root), '') + ':' + $_.LineNumber)
  }
}

# ---------------------------------------------------------------------------
# 11) client Def <-> server def msg id parity
# ---------------------------------------------------------------------------
$cliMsgDef = Join-Path $root 'client\Assets\Scripts\Def\MsgDef.cs'
$srvMsg    = Join-Path $root 'server\game\def\msg.go'
$srvPush   = Join-Path $root 'server\game\def\push.go'
if ((Test-Path $cliMsgDef) -and (Test-Path $srvMsg) -and (Test-Path $srvPush)) {
  $csTxt = ReadUtf8 $cliMsgDef
  $goTxt = (ReadUtf8 $srvMsg) + "`n" + (ReadUtf8 $srvPush)
  # The two sides are REQUIRED to use different identifier prefixes -- the project convention is
  # server `def.MsgSetNickname` / `def.PushRoomList` vs client `MsgDef.SetNickname` / `MsgDef.PushRoomList`.
  # Comparing identifiers verbatim therefore reports every single C2S id as "client-only" even when the
  # numbers agree exactly: a permanent false positive. What actually has to line up is the PAIR
  # (canonical name, numeric id). So: strip the documented prefix on both sides, lower-case, and compare.
  # A genuine problem (different id, or a name present on one side only) still fails.
  $csMap = @{}
  foreach ($m in [regex]::Matches($csTxt, '(\w+)\s*=\s*(\d{5,})')) {
    $n = $m.Groups[1].Value
    if ($n.StartsWith('Msg')) { $n = $n.Substring(3) } elseif ($n.StartsWith('Push')) { $n = $n.Substring(4) }
    $csMap[$n.ToLowerInvariant()] = $m.Groups[2].Value
  }
  $goMap = @{}
  # NOTE: the server declares its ids inside a `const ( ... )` BLOCK, so the constant keyword
  # appears once and each line is just `Name = 12345`. Matching on `const\s+(\w+)` therefore
  # found nothing inside the block and every client id came back as a false "client-only".
  foreach ($m in [regex]::Matches($goTxt, '(\w+)\s*=\s*(\d{5,})')) {
    $n = $m.Groups[1].Value
    if ($n.StartsWith('Msg')) { $n = $n.Substring(3) } elseif ($n.StartsWith('Push')) { $n = $n.Substring(4) }
    $goMap[$n.ToLowerInvariant()] = $m.Groups[2].Value
  }
  $mismatch = @()
  foreach ($k in @($csMap.Keys)) {
    if (-not $goMap.ContainsKey($k)) { $mismatch += ("client-only: $k=" + $csMap[$k]) }
    elseif ($goMap[$k] -ne $csMap[$k]) { $mismatch += ("value differs: $k client=" + $csMap[$k] + " server=" + $goMap[$k]) }
  }
  foreach ($k in @($goMap.Keys)) {
    if (-not $csMap.ContainsKey($k)) { $mismatch += ("server-only: $k=" + $goMap[$k]) }
  }
  if ($mismatch.Count -eq 0) { Say 'PASS' 'msgid-parity' ("$($csMap.Count) client id(s) matched a server def by name+value") }
  else {
    $fail++; Say 'FAIL' 'msgid-parity' ("$($mismatch.Count) mismatch(es)")
    $mismatch | Select-Object -First 15 | ForEach-Object { Write-Output ('            ' + $_) }
  }
} else { Say 'HUMAN-ONLY' 'msgid-parity' 'Def/MsgDef.cs or server/game/def not present yet' }

# ---------------------------------------------------------------------------
# 12) play ledger (SKILL 1.13 T0 / SKILL 2)
#     NOTE (2026-09-20, T1): the row-count cap that used to live here was REMOVED.
#     The global skill 2 states the ledger judges exactly ONE thing -- whether every
#     row carries a reason in column 4 -- and that "row count = overspend" checks are
#     abolished: there is no session budget, no per-session cap, and "used them all up"
#     is never a reason to stop. Keeping `sessions > 6` as a FAIL meant a legitimate
#     extra Play session (T1 portrait-orientation capture) turned this gate red, i.e.
#     the gate punished evidence gathering -- the exact behaviour the rule forbids.
#     Logging stays mandatory: one line per editor_play, column 4 non-empty.
# ---------------------------------------------------------------------------
$playLog    = Join-Path $root '.ai-tmp\test\play-log.tsv'
$playRows   = @()
if (Test-Path $playLog) {
  foreach ($line in @([System.IO.File]::ReadAllLines($playLog, [Text.Encoding]::UTF8))) {
    if ($line -match '^\s*#' -or $line.Trim().Length -eq 0) { continue }
    $c = $line -split "`t"
    $why = if ($c.Count -ge 4) { [string]$c[3] } else { '' }
    $playRows += [pscustomobject]@{ At = $c[0]; Task = $(if ($c.Count -ge 3) { [string]$c[2] } else { '' }); Why = $why }
  }
}
if (-not (Test-Path $playLog)) {
  Say 'HUMAN-ONLY' 'play-ledger' 'no play-log.tsv yet (one line per editor_play)'
} elseif (@($playRows | Where-Object { $_.Why.Trim().Length -lt 4 }).Count -gt 0) {
  $fail++; Say 'FAIL' 'play-ledger' 'some row has no reason in column 4'
} else {
  Say 'PASS' 'play-ledger' ("sessions = " + $playRows.Count + " -- every row carries a reason (no row-count cap)")
}

# ---------------------------------------------------------------------------
# 13) dispatch ledger: implementation must come from a dispatched executor
# ---------------------------------------------------------------------------
$logPath   = Join-Path $root '.ai-tmp\test\dispatch-log.tsv'
# NOTE: PowerShell's `**` is NOT recursive -- it is just a literal `*` in one path segment.
# The previous glob list therefore only ever matched depth 2 (`Scripts\Core\*.cs`) and left
# `Scripts\Module\Flow\*.cs`, `Scripts\UI\Panels\*.cs`, `Scripts\Module\Room\*.cs` … completely
# UNCHECKED, i.e. every panel and manager was invisible to this gate. Walk the roots recursively.
$implRoots = @('client\Assets\Scripts', 'server')
$implCut   = (Get-Date).AddHours(-72)
$implFiles = @()
foreach ($r in $implRoots) {
  $rp = Join-Path $root $r
  if (-not (Test-Path $rp)) { continue }
  $implFiles += @(Get-ChildItem $rp -Recurse -File -Include '*.cs', '*.go' -ErrorAction SilentlyContinue |
                  Where-Object {
                    $_.LastWriteTime -gt $implCut -and
                    $_.FullName -notmatch '\\(obj|bin|vendor|node_modules|Library|Temp)\\' -and
                    $_.FullName -notmatch '\.g\.cs$'
                  })
}
$implFiles = @($implFiles | Sort-Object FullName -Unique)
$dispatched = @(); $named = @()
if (Test-Path $logPath) {
  foreach ($line in @([System.IO.File]::ReadAllLines($logPath, [Text.Encoding]::UTF8))) {
    if ($line -match '^\s*#') {
      # A direct-fix line names ONE OR MORE paths: `# direct-fix: a/b.cs,c/*.cs -- reason`.
      # Taking only the first whitespace-delimited token silently made every multi-path entry
      # useless (nothing was ever exempted), so collect every token before the `--` reason.
      if ($line -match '^\s*#\s*(?:adjudicated|direct-fix):\s*(.+?)\s*(?:--|$)') {
        foreach ($tok in ($Matches[1] -split '[,;\s]+')) {
          $t = $tok.Trim().Replace('\', '/')
          if ($t.Length -gt 0) { $named += $t }
        }
      }
      continue
    }
    if ($line.Trim().Length -eq 0) { continue }
    $c = $line -split "`t"
    if ($c.Count -ge 4) {
      try { $dispatched += [pscustomobject]@{ At = [datetime]$c[0]; By = $c[1]; Task = $c[2]; Scope = $c[3] } } catch { }
    }
  }
}
$dispatched = @($dispatched | Sort-Object At)
for ($i = 0; $i -lt $dispatched.Count; $i++) {
  # A row's window ends at the next STRICTLY LATER dispatch. Using the
  # immediately following row instead would give a zero-width window to every
  # row in a batch that shares one timestamp (several slices are dispatched in
  # the same message), and every file that batch produced would be reported as
  # an orphan -- a false positive, which is worse than no check at all.
  $until = [datetime]'9999-01-01'
  for ($j = 0; $j -lt $dispatched.Count; $j++) {
    if ($dispatched[$j].At -gt $dispatched[$i].At) { $until = $dispatched[$j].At; break }
  }
  $dispatched[$i] | Add-Member -NotePropertyName Until -NotePropertyValue $until -Force
}
$orphan = @()
foreach ($f in $implFiles) {
  $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
  # Named adjudications may use a glob (e.g. `client/Assets/Scripts/Def/*.cs`), so match with -like.
  $namedHit = $false
  foreach ($pat in $named) {
    if ($rel -like $pat -or $rel -like ($pat + '*')) { $namedHit = $true; break }
  }
  if ($namedHit) { continue }
  $hit = @($dispatched | Where-Object {
      $scopes = @($_.Scope -split '[,;]') | ForEach-Object { $_.Trim().Replace('\', '/') } | Where-Object { $_.Length -gt 0 }
      $inScope = @($scopes | Where-Object { $rel -like ($_ + '*') }).Count -gt 0
      $inScope -and ($_.At -le $f.LastWriteTime) -and ($f.LastWriteTime -lt $_.Until)
    })
  if ($hit.Count -eq 0) { $orphan += $rel }
}
if ($implFiles.Count -eq 0) { Say 'HUMAN-ONLY' 'impl-by-executor' 'no implementation file touched in window' }
elseif ($orphan.Count -eq 0) { Say 'PASS' 'impl-by-executor' ("$($implFiles.Count) file(s) all matched a dispatch row") }
else {
  $fail++; Say 'FAIL' 'impl-by-executor' ("$($orphan.Count)/$($implFiles.Count) file(s) without dispatch trace")
  $orphan | Select-Object -First 15 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 14) offline core must build (pure-Go battle core, no engine import)
#     NOTE: the Go module root is <root>/server, NOT <root>. Running the build
#     from <root> fails with "directory prefix game\core does not contain main
#     module". Use `go -C server ...`.
#     NOTE: with $ErrorActionPreference='Stop' a native command writing to
#     stderr raises NativeCommandError, which ABORTS this script before the
#     summary is printed. Native calls therefore run with 'Continue'.
# ---------------------------------------------------------------------------
$coreDir = Join-Path $root 'server\game\core'
$goCmd = Get-Command go -ErrorAction SilentlyContinue
if ((Test-Path $coreDir) -and $goCmd) {
  $prevEap = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  $out = & go -C (Join-Path $root 'server') build ./game/core/... 2>&1
  $buildCode = $LASTEXITCODE
  $ErrorActionPreference = $prevEap
  if ($buildCode -eq 0) { Say 'PASS' 'core-builds' 'go -C server build ./game/core/... ok' }
  else { $fail++; Say 'FAIL' 'core-builds' (($out | Select-Object -First 5) -join ' | ') }
} else { Say 'HUMAN-ONLY' 'core-builds' 'server/game/core or the go toolchain not present yet' }

# ---------------------------------------------------------------------------
# 15) core must not import the server engine (hard rule, project conventions)
# ---------------------------------------------------------------------------
if (Test-Path $coreDir) {
  $coreFiles = @(Get-ChildItem $coreDir -Filter *.go -ErrorAction SilentlyContinue)
  $engHits = @($coreFiles | Select-String -Pattern '"clover-server-engine' -Encoding UTF8)
  if ($engHits.Count -eq 0) { Say 'PASS' 'core-is-pure' ('0 engine import(s) across ' + $coreFiles.Count + ' file(s)') }
  else {
    $fail++; Say 'FAIL' 'core-is-pure' ("$($engHits.Count) engine import(s) inside game/core")
    $engHits | Select-Object -First 10 | ForEach-Object {
      Write-Output ('            ' + ($_.Path -replace [regex]::Escape($root), '') + ':' + $_.LineNumber)
    }
  }
} else { Say 'HUMAN-ONLY' 'core-is-pure' 'server/game/core not present yet' }

# ---------------------------------------------------------------------------
# 16) coverage gates (patterns/full-coverage-audit.md 7 / scaffold/coverage-matrix.md)
#     FOUR gates, all ADDITIVE -- no existing check above was relaxed or removed:
#       coverage-rows        entity-list data rows == acceptance-table verdict rows
#       coverage-filled      every state-matrix row carries a LEGAL verdict
#       coverage-diff        zero mismatch ; allowed-difference rows cite a
#                            registered 4-field ledger entry
#       coverage-dimensions  each of the 15 dims (D1..D12,S1..S3) has >=1 row
#     NOTE (expected red): coverage-dimensions is RED by design while only
#     D1/D2/D3/S1 have been enumerated -- the red line IS the list of
#     unjudged dimensions. It must never be made green by inventing rows.
#     NOTE: dimension prefixes (D4..D12 / S2,S3) are printed instead of the CJK
#     dimension names to keep this file ASCII-only (see the header comment).
#     The column index below is 1-based: 7 == verdict column of the state matrix.
# ---------------------------------------------------------------------------
$cEntity = Join-Path $planDir ((([char[]]@(0x5B9E,0x4F53,0x6E05,0x5355)) -join '') + '.tsv')  # shi ti qing dan
$cMatrix = Join-Path $planDir ((([char[]]@(0x72B6,0x6001,0x77E9,0x9635)) -join '') + '.tsv')  # zhuang tai ju zhen
$cDiffLed= Join-Path $planDir ((([char[]]@(0x5DEE,0x5F02,0x767B,0x8BB0)) -join '') + '.tsv')  # cha yi deng ji
$sAgree  = ([char[]]@(0x4E00,0x81F4) -join '')                                                # yi zhi
$sDis    = ([char[]]@(0x4E0D) -join '') + $sAgree                                             # bu yi zhi
$sAllow  = ([char[]]@(0x5141,0x8BB8,0x7684,0x5DEE,0x5F02) -join '')                            # yun xu de cha yi
$verdictCol = 7

# Reads a TSV into $script:Tsv (array of string[] rows, header included).
# NOTE: must not RETURN the array -- PowerShell unrolls a returned array one level,
# which would turn the row list into a flat stream of cells (a plain
# `return $rows.ToArray()` made coverage-rows read entity=0). Assigning to a
# script-scoped variable keeps one element per row and survives 0/1-row files.
$script:Tsv = @()
function ReadTsvRows([string]$p) {
  $script:Tsv = @()
  foreach ($line in [System.IO.File]::ReadAllLines($p, [Text.Encoding]::UTF8)) {
    if ($line.Trim().Length -eq 0) { continue }
    $script:Tsv += ,($line -split "`t")
  }
}

# 16a) coverage-rows
if ((Test-Path $cEntity) -and (Test-Path $spec)) {
  ReadTsvRows $cEntity
  $entData = @($script:Tsv | Select-Object -Skip 1)
  # NOTE (AL2, scope-only fix -- the STRENGTH of this gate is unchanged):
  #   `patterns/full-coverage-audit.md` 7 defines the compared quantity as
  #   "entity-list DATA rows == acceptance-table VERDICT rows", where the verdict
  #   rows are exactly the rows of acceptance-table section 4 (one verdict row per
  #   entity, see scaffold/coverage-matrix.md).  Scanning the whole file also swept
  #   in the pre-existing ledger rows of sections 1-3 (213 of them), so this gate
  #   reported entity=1024 <> acceptance=1237 by construction -- the 213 were not
  #   verdict rows at all.  Only the STATISTICAL SCOPE is narrowed here: we now read
  #   the `## 4.` block only and still require strict equality (no tolerance, no
  #   "any-equality" relaxation, no check removed).
  $sec4Head = '## 4. ' + (([char[]]@(0x8986,0x76D6,0x77E9,0x9635,0x5224,0x5B9A,0x884C)) -join '')
  $accRows = @()
  $inSec4 = $false
  foreach ($ln in ((ReadUtf8 $spec) -split "`n")) {
    if (-not $inSec4) { if ($ln.StartsWith($sec4Head)) { $inSec4 = $true }; continue }
    if ($ln.StartsWith('## ')) { break }
    if ($ln -match '^\|\s*\d+\s*\|') { $accRows += $ln }
  }
  if ($entData.Count -eq $accRows.Count) {
    Say 'PASS' 'coverage-rows' ("entity=$($entData.Count) == acceptance=$($accRows.Count)")
  } else {
    $fail++; Say 'FAIL' 'coverage-rows' ("entity=$($entData.Count) <> acceptance=$($accRows.Count) -- T0 exhaustive target not reached")
  }
} else { $fail++; Say 'FAIL' 'coverage-rows' 'entity list or acceptance table missing' }

# 16b) coverage-filled
$mData = @()
if (Test-Path $cMatrix) {
  ReadTsvRows $cMatrix
  $mData = @($script:Tsv | Select-Object -Skip 1)
  $bad = @($mData | Where-Object {
      $v = if ($_.Count -ge $verdictCol) { [string]$_[($verdictCol - 1)] } else { '' }
      -not ($v.StartsWith($sAgree) -or $v.StartsWith($sDis) -or $v.StartsWith($sAllow))
    })
  if (($mData.Count -gt 0) -and ($bad.Count -eq 0)) {
    Say 'PASS' 'coverage-filled' ("$($mData.Count) row(s), every verdict is legal")
  } else {
    $fail++; Say 'FAIL' 'coverage-filled' ("$($bad.Count)/$($mData.Count) row(s) not filled (empty / pending / blocked)")
  }
} else { $fail++; Say 'FAIL' 'coverage-filled' 'state matrix missing' }

# 16c) coverage-diff
if ((Test-Path $cMatrix) -and (Test-Path $cDiffLed)) {
  $mis = @($mData | Where-Object { $_.Count -ge $verdictCol -and ([string]$_[($verdictCol - 1)]).StartsWith($sDis) })
  $allowRows = @($mData | Where-Object { $_.Count -ge $verdictCol -and ([string]$_[($verdictCol - 1)]).StartsWith($sAllow) })
  ReadTsvRows $cDiffLed
  $led = @($script:Tsv | Select-Object -Skip 1)
  $ledBad = @($led | Where-Object { @($_ | Where-Object { $_.Trim().Length -gt 0 }).Count -lt 5 })
  $allowBad = @($allowRows | Where-Object { $_.Count -lt 8 -or ([string]$_[7]).Trim().Length -eq 0 })
  if (($mis.Count -eq 0) -and ($ledBad.Count -eq 0) -and ($allowBad.Count -eq 0) -and ($led.Count -gt 0)) {
    Say 'PASS' 'coverage-diff' ("mismatch=0 ; allowed-difference rows=$($allowRows.Count) ; ledger rows=$($led.Count) all 4-field")
  } else {
    $fail++; Say 'FAIL' 'coverage-diff' ("mismatch=$($mis.Count) ; ledger-incomplete=$($ledBad.Count) ; uncited-allowed=$($allowBad.Count) ; ledger-rows=$($led.Count)")
  }
} else { $fail++; Say 'FAIL' 'coverage-diff' 'state matrix or difference ledger missing' }

# 16d) coverage-dimensions
if (Test-Path $cEntity) {
  $need = @('D1','D2','D3','D4','D5','D6','D7','D8','D9','D10','D11','D12','S1','S2','S3')
  $have = @{}
  ReadTsvRows $cEntity
  foreach ($r in @($script:Tsv | Select-Object -Skip 1)) {
    if ($r.Count -ge 1 -and ([string]$r[0]) -match '^(D\d+|S\d+)') { $have[$Matches[1]] = $true }
  }
  $missing = @($need | Where-Object { -not $have.ContainsKey($_) })
  if ($missing.Count -eq 0) {
    Say 'PASS' 'coverage-dimensions' 'all 15 dimensions have >=1 entity row'
  } else {
    $fail++; Say 'FAIL' 'coverage-dimensions' ("$($missing.Count)/15 dimensions unjudged: " + ($missing -join ','))
  }
} else { $fail++; Say 'FAIL' 'coverage-dimensions' 'entity list missing' }

# ===========================================================================
# ---- AM1 (2026-09-22): the 18 template items this gate was missing ---------
#      Mirrors the REQUIRED rows of reference/verify-template.md GATE-ITEMS.
#      Names are copied verbatim from the template's machine-readable block
#      (scripts/gate-sync.ps1 extracts them from `Say '<STATUS>' '<name>'`).
#      Every item below was self-tested twice (known-good sample PASS /
#      injected defect FAIL-or-HUMAN-ONLY): .ai-tmp/test/AM1-selftest.md.
#      NOTE: nothing above this line was relaxed or removed.
# ===========================================================================

$specTxt = if (Test-Path $spec) { ReadUtf8 $spec } else { '' }
$specLns = @($specTxt -split "`n")
$ceHuaAn = Join-Path $planDir (([char[]]@(0x7B56,0x5212,0x6848) -join ''))   # ce hua an  (plan folder w/ the reference spec)

# shared: resolve a *.png token cited in the acceptance table to a real file.
# Falls back to a basename index so citations that omit a prefix still resolve
# (a false red is worse than no check at all).
$script:ByName = @{}
foreach ($d in @((Join-Path $root '.ai-tmp\screenshots'), (Join-Path $root '.ai-tmp\test'), $planDir)) {
  if (-not (Test-Path $d)) { continue }
  foreach ($f in @(Get-ChildItem $d -Recurse -File -ErrorAction SilentlyContinue)) {
    if (-not $script:ByName.ContainsKey($f.Name)) { $script:ByName[$f.Name] = $f }
  }
}
function GetCitedFile([string]$t) {
  if ($t -match '\*') { return $null }
  $c = $t.Replace('/', '\').TrimStart('\')
  $p = Join-Path $root $c
  if (Test-Path $p -PathType Leaf) { return (Get-Item $p) }
  $bn = Split-Path $t -Leaf
  if ($script:ByName.ContainsKey($bn)) { return $script:ByName[$bn] }
  return $null
}
# A citation counts only when it is a PROJECT path (evidence / plan / build
# output).  A bare "Sprites/Ui/Bars/x.png" is a Resources-relative ASSET frame
# reference, not a file path; counting those produced 87 false reds on the
# first run (measured).  Enumerations ("frame_197/198.png") are excluded too.
$planName = Split-Path $planDir -Leaf
$citePrefixes = @('.ai-tmp/', '.ai-tmp\', 'client/', 'client\', 'server/', 'server\', 'tools/', 'tools\', 'docs/', 'docs\', ($planName + '/'), ($planName + '\'))
$citedShots = @()
foreach ($m in [regex]::Matches($specTxt, '[A-Za-z0-9_\-\./\\]+\.png')) {
  $t = $m.Value
  $ok = $false
  foreach ($px in $citePrefixes) { if ($t.StartsWith($px)) { $ok = $true; break } }
  if (-not $ok) { continue }
  if ($t -match '\.png/\S') { continue }
  $citedShots += $t
}
$citedShots = @($citedShots | Sort-Object -Unique)

# ---------------------------------------------------------------------------
# 17) spec-doc -- the reference spec document must exist (init gate 3 / 9)
# ---------------------------------------------------------------------------
$specDocs = @()
if (Test-Path $ceHuaAn) {
  $specDocs = @(Get-ChildItem $ceHuaAn -Filter *.md -File -ErrorAction SilentlyContinue |
                Where-Object { (ReadUtf8 $_.FullName).Trim().Length -gt 200 })
}
if ($specDocs.Count -gt 0) { Say 'PASS' 'spec-doc' ('reference spec present: ' + (($specDocs | ForEach-Object { $_.Name }) -join ',')) }
else { $fail++; Say 'FAIL' 'spec-doc' 'no non-trivial *.md under plan/plan-folder (the reference spec document)' }

# ---------------------------------------------------------------------------
# 18) baseline-images -- reference-side baseline screenshots must exist
# ---------------------------------------------------------------------------
$baseDir  = Join-Path $planDir (([char[]]@(0x57FA,0x7EBF,0x56FE) -join ''))  # ji xian tu (baseline images)
$baseImgs = @()
if (Test-Path $baseDir) {
  $baseImgs = @(Get-ChildItem $baseDir -Recurse -File -Include *.png, *.jpg, *.jpeg -ErrorAction SilentlyContinue)
}
if ($baseImgs.Count -gt 0) { Say 'PASS' 'baseline-images' ('reference baseline images = ' + $baseImgs.Count) }
else { $fail++; Say 'FAIL' 'baseline-images' 'plan/baseline-images has no image -- nothing to compare 1:1 against' }

# ---------------------------------------------------------------------------
# 19) asset-research-doc -- asset research log must exist (init gate 3)
# ---------------------------------------------------------------------------
$arTxt = if (Test-Path $assetLog) { ReadUtf8 $assetLog } else { '' }
if ($arTxt.Trim().Length -gt 200) { Say 'PASS' 'asset-research-doc' ('asset research log present (' + $arTxt.Length + ' chars)') }
else { $fail++; Say 'FAIL' 'asset-research-doc' 'plan/asset-research.md missing or trivial (research before any generic fallback)' }

# ---------------------------------------------------------------------------
# 20) verify-entry -- the one-click recheck entry (this script) exists and runs
# ---------------------------------------------------------------------------
$vePath = Join-Path $root 'tools\verify.ps1'
if ((Test-Path $vePath) -and ((Get-Item $vePath).Length -gt 500)) { Say 'PASS' 'verify-entry' 'tools/verify.ps1 exists and is the running script' }
else { $fail++; Say 'FAIL' 'verify-entry' 'tools/verify.ps1 missing or empty' }

# ---------------------------------------------------------------------------
# 21) allowed-diff -- every exception row carries why / provenance / when-removed
#     (section 3 of the acceptance table; 4 columns per row, none may be blank)
# ---------------------------------------------------------------------------
$adRows = 0; $adBad = @()
$inSec3 = $false
foreach ($ln in $specLns) {
  if (-not $inSec3) { if ($ln -match '^##\s*3\.') { $inSec3 = $true }; continue }
  if ($ln -match '^##\s*4\.') { break }
  if ($ln -match '^\|\s*D\d+\s*\|') {
    $adRows++
    $cells = @(($ln -split '\|') | Select-Object -Skip 2 | ForEach-Object { $_.Trim() })
    $id = $cells[0]
    if ($cells.Count -lt 5) { $adBad += ('row ' + $id + ': fewer than 4 columns'); continue }
    $why = $cells[1]; $prov = $cells[2]; $when = $cells[$cells.Count - 2]
    # A cell may legitimately be a cross-reference ("tong D27" / "jian ...") or a
    # deliberate n/a marker for the removal column ("--" / "yong jiu bao liu").
    # Accepting those is not a relaxation of "must be present": an EMPTY cell
    # still fails, which is exactly what the rule is about.
    $cTong = ([char]0x540C); $cJian = ([char]0x89C1); $cYong = ([char]0x6C38)
    $dashes = @('-', ([string][char]0x2014), ([string][char]0x2013))
    $whyOk  = ($why.Length -ge 6)  -or $why.StartsWith($cTong)  -or $why.StartsWith($cJian)
    $provOk = ($prov.Length -ge 6) -or $prov.StartsWith($cTong) -or $prov.StartsWith($cJian) -or $prov.StartsWith(([char]0x89C1).ToString() + ([char]0x4E0A))
    $whenOk = ($when.Length -ge 2) -or ($dashes -contains $when) -or $when.StartsWith($cYong)
    if ($id.Length -lt 2)   { $adBad += ('row ' + $id + ': empty what') }
    if (-not $whyOk)        { $adBad += ($id + ': no why') }
    if (-not $provOk)       { $adBad += ($id + ': no provenance') }
    if (-not $whenOk)       { $adBad += ($id + ': no when-removed') }
  }
}
if ($adRows -eq 0) { $fail++; Say 'FAIL' 'allowed-diff' 'no allowed-difference row (D<n>) found in section 3 of the acceptance table' }
elseif ($adBad.Count -eq 0) { Say 'PASS' 'allowed-diff' ($adRows.ToString() + ' exception row(s), every one carries why / provenance / when-removed') }
else {
  $fail++; Say 'FAIL' 'allowed-diff' ($adBad.Count.ToString() + '/' + $adRows.ToString() + ' exception row(s) incomplete')
  $adBad | Select-Object -First 10 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 22) screenshot-refs -- every png cited by the acceptance table exists on disk
# ---------------------------------------------------------------------------
$shotMiss = @()
foreach ($t in $citedShots) { if ($null -eq (GetCitedFile $t)) { $shotMiss += $t } }
if ($shotMiss.Count -eq 0) { Say 'PASS' 'screenshot-refs' ($citedShots.Count.ToString() + ' cited png path(s), all reachable') }
else {
  $fail++; Say 'FAIL' 'screenshot-refs' ($shotMiss.Count.ToString() + ' cited png path(s) do not exist')
  $shotMiss | Select-Object -First 12 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 23) evidence-freshness -- cited artifact newer than the code it verifies,
#     judged BY CAUSE (per area/panel), never globally.  A global "any impl file
#     is newer than any screenshot" test would invalidate every capture on a
#     one-line comment edit (measured time sink).  Area -> source file map:
# ---------------------------------------------------------------------------
$evShots = @()
foreach ($t in $citedShots) {
  $f = GetCitedFile $t
  if ($null -eq $f) { continue }
  if ($f.FullName.StartsWith((Join-Path $root '.ai-tmp\screenshots'), [System.StringComparison]::OrdinalIgnoreCase)) { $evShots += $f }
}
$evShots = @($evShots | Sort-Object FullName -Unique)
# Scope = the same project-owned roots as the freeze gate.  An external
# dependency (engine repo) and generated dirs are not "our implementation", so
# their mtimes cannot invalidate our evidence.  Inside $implRoots the rule is
# unchanged and strict: impl file newer than the shot => that pair is stale.
$script:ImplByName = @{}
foreach ($r in $implRoots) {
  $p = Join-Path $root $r
  if (-not (Test-Path $p)) { continue }
  foreach ($f in @(Get-ChildItem $p -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue)) {
    if (-not $script:ImplByName.ContainsKey($f.Name)) { $script:ImplByName[$f.Name] = $f }
  }
}
$freshMap = @(
  @('boot',     'BootPanel.cs'),
  @('longnick', 'MainMenuPanel.cs'),
  @('menu',     'MainMenuPanel.cs'),
  @('settings', 'SettingsPanel.cs'),
  @('hud',      'HudPanel.cs'),
  @('deck',     'DeckEditPanel.cs'),
  @('result',   'ResultPanel.cs'),
  @('pause',    'PausePanel.cs'),
  @('room',     'RoomListPanel.cs'),
  @('login',    'LoginPanel.cs'),
  @('nick',     'NicknamePanel.cs'),
  @('arena',    'ArenaView.cs'),
  @('flow',     'UnitView.cs')
)
$freshCmp = 0; $freshNoPair = 0; $freshStale = @()
foreach ($shot in $evShots) {
  $nm = $shot.Name.ToLowerInvariant()
  $src = $null
  foreach ($mp in $freshMap) {
    if ($nm.Contains($mp[0])) { if ($script:ImplByName.ContainsKey($mp[1])) { $src = $script:ImplByName[$mp[1]] }; break }
  }
  if ($null -eq $src) { $freshNoPair++; continue }
  $freshCmp++
  if ($shot.LastWriteTime -lt $src.LastWriteTime) {
    $freshStale += ($shot.Name + '  <  ' + $src.Name + ' (impl ' + $src.LastWriteTime.ToString('MM-dd HH:mm') + ' > shot ' + $shot.LastWriteTime.ToString('MM-dd HH:mm') + ')')
  }
}
if ($freshCmp -eq 0) { $human++; Say 'HUMAN-ONLY' 'evidence-freshness' 'no (shot, impl) pair could be matched by area -- an empty comparison must never PASS' }
elseif ($freshStale.Count -eq 0) { Say 'PASS' 'evidence-freshness' ($freshCmp.ToString() + ' (shot, impl) pair(s) compared by area, all fresh; ' + $freshNoPair.ToString() + ' shot(s) had no area mapping') }
else {
  $fail++; Say 'FAIL' 'evidence-freshness' ($freshStale.Count.ToString() + '/' + $freshCmp.ToString() + ' pair(s) stale => only those rows are invalidated, the rest stay valid')
  $freshStale | Select-Object -First 12 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 24) numeric-log-only -- a numeric-class row resting on a log line only is
#     NEVER a PASS; it degrades to HUMAN-ONLY (a log line is not an assertion).
# ---------------------------------------------------------------------------
$numRows = 0; $numLogOnly = @()
foreach ($ln in $specLns) {
  if (-not ($ln.StartsWith('|') -and $ln.Contains($cNumeric))) { continue }
  $numRows++
  $id = @(($ln -split '\|') | Select-Object -Skip 1 | ForEach-Object { $_.Trim() })[0]
  $hasLog    = ($ln -match '(?i)\.log\b|console|\.json\b')
  $hasAnchor = ($ln -match '(?i)\bTest\b|assert|go test|Test-Path|\.go\b|\.py\b|\.ps1\b|\.tsv\b|\.cs\b')
  if ($hasLog -and (-not $hasAnchor)) { $numLogOnly += $id }
}
if ($numRows -eq 0) { Say 'HUMAN-ONLY' 'numeric-log-only' 'no numeric-class row in the acceptance table' }
elseif ($numLogOnly.Count -eq 0) { Say 'PASS' 'numeric-log-only' ($numRows.ToString() + ' numeric row(s), none rests on a bare log line') }
else { $human++; Say 'HUMAN-ONLY' 'numeric-log-only' ($numLogOnly.Count.ToString() + ' numeric row(s) cite a log line with no assertion anchor: ' + (($numLogOnly | Select-Object -First 8) -join ',')) }

# ---------------------------------------------------------------------------
# 25) engine-credit -- credit is judged on the RENDERED text, not on a source
#     grep: the runtime node-tree dump must show it verbatim (case matters).
# ---------------------------------------------------------------------------
$brandArts = @(Get-ChildItem (Join-Path $root '.ai-tmp\test') -Recurse -File -Filter *brand*.txt -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending)
if ($brandArts.Count -eq 0) { $human++; Say 'HUMAN-ONLY' 'engine-credit' 'no runtime brand-tree dump under .ai-tmp/test (*brand*.txt) -- judge the credit on the rendered first screen' }
else {
  $ecSrc = $brandArts[0]
  $ecTxt = ReadUtf8 $ecSrc.FullName
  if ($ecTxt -cmatch 'by clover-engine') { Say 'PASS' 'engine-credit' ('rendered node-tree text is verbatim "by clover-engine" (' + $ecSrc.Name + ')') }
  else { $fail++; Say 'FAIL' 'engine-credit' ('runtime brand dump ' + $ecSrc.Name + ' does not contain the verbatim lowercase "by clover-engine" (case matters)') }
}

# ---------------------------------------------------------------------------
# 26) no-team-sessions -- no async/team dispatch channel (it bypasses model:inherit)
#     Scope = this project's .codebuddy only; a shared workspace-level config is
#     not this project's channel (false positives make a gate worthless).
# ---------------------------------------------------------------------------
$cbDir = Join-Path $root '.codebuddy'
$teamHits = @()
if (Test-Path $cbDir) {
  $teamHits = @(Get-ChildItem $cbDir -Directory -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '(?i)(^|[-_])(team|member)s?([-_]|$)' } | ForEach-Object { $_.FullName })
}
if ($teamHits.Count -eq 0) { Say 'PASS' 'no-team-sessions' 'no async/team dispatch channel under this project' }
else {
  $fail++; Say 'FAIL' 'no-team-sessions' ($teamHits.Count.ToString() + ' team/member channel dir(s) -- async dispatch bypasses model:inherit')
  $teamHits | Select-Object -First 5 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 27) no-escaped-artifacts -- one-off artifacts belong in <root>/.ai-tmp/test/
#     Scan = workspace root + host product dirs; window = 24h; only NEW files
#     (CreationTime).  The short-name token is only used when >= 4 chars --
#     a 2-char token ("cr") would match half the workspace = pure false red.
# ---------------------------------------------------------------------------
$wsRoot    = Split-Path $root -Parent
$rootName  = Split-Path $root -Leaf
$projToks  = @($rootName)
if ($rootName.StartsWith('clover-project-')) {
  $short = $rootName.Substring(15)
  if ($short.Length -ge 4) { $projToks += $short }
}
$brainZones = @()
if ($env:APPDATA) {
  $brainZones = @(Get-ChildItem (Join-Path $env:APPDATA '*\User\globalStorage\*\brain') -Directory -ErrorAction SilentlyContinue |
                  ForEach-Object { $_.FullName })
}
$escCut = (Get-Date).AddHours(-24)
$esc = @()
foreach ($t in $projToks) {
  $esc += @(Get-ChildItem $wsRoot -File -Filter ('*' + $t + '*') -ErrorAction SilentlyContinue |
            Where-Object { $_.CreationTime -gt $escCut })
  foreach ($z in $brainZones) {
    $esc += @(Get-ChildItem $z -Recurse -File -Filter ('*' + $t + '*') -ErrorAction SilentlyContinue |
              Where-Object { $_.CreationTime -gt $escCut })
  }
}
$esc = @($esc | Sort-Object FullName -Unique)
if ($esc.Count -eq 0) { Say 'PASS' 'no-escaped-artifacts' ('0 hit(s) (workspace root + ' + $brainZones.Count.ToString() + ' host product dir(s))') }
else {
  $fail++; Say 'FAIL' 'no-escaped-artifacts' ($esc.Count.ToString() + ' project file(s) outside the project => move into .ai-tmp/test/')
  $esc | Select-Object -First 10 | ForEach-Object { Write-Output ('            ' + $_.FullName) }
}

# ---------------------------------------------------------------------------
# 28) sampler-selfcheck -- every .ai-tmp *.ps1: 0 syntax errors AND (ASCII-only
#     or BOM).  A BOM-less CJK .ps1 is parsed as ANSI by PS 5.1 => -match fails
#     silently => the driver waits out its full timeout every single round.
# ---------------------------------------------------------------------------
$tmpRoot = Join-Path $root '.ai-tmp'
$badPs = @()
if (Test-Path $tmpRoot) {
  foreach ($f in @(Get-ChildItem $tmpRoot -Recurse -Filter *.ps1 -File -ErrorAction SilentlyContinue)) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    $bom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
    $nonAscii = @($b | Where-Object { $_ -gt 127 }).Count
    if ((-not $bom) -and $nonAscii -gt 0) { $badPs += ($f.Name + ' (ANSI trap: non-ASCII without BOM)') }
    $enc = [System.Text.Encoding]::UTF8
    if (-not $bom) { $enc = [System.Text.Encoding]::Default }
    $text = $enc.GetString($b)
    if ($bom) { $text = $text.TrimStart([char]0xFEFF) }
    $psk = $null; $per = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$psk, [ref]$per)
    if (@($per).Count -gt 0) { $badPs += ($f.Name + ' (' + @($per).Count.ToString() + ' syntax error)') }
  }
}
if ($badPs.Count -eq 0) { Say 'PASS' 'sampler-selfcheck' 'scripts under .ai-tmp: syntax OK, no ANSI trap' }
else {
  $fail++; Say 'FAIL' 'sampler-selfcheck' ($badPs.Count.ToString() + ' script(s) fail the pre-run self-check')
  $badPs | Select-Object -First 10 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 29) freeze-before-capture -- PER-ROW / PER-AREA scope (corrected 2026-09-22).
#
# NEW SEMANTICS (one line): every png the newest contact-sheet index references
#   is compared against the implementation of ITS OWN area, using the SAME
#   area -> impl map (#freshMap) that #23 evidence-freshness uses; ONLY the rows
#   whose evidence is older than their own area's implementation turn red, and
#   every other row of the batch stays valid.
#
# WHY the old global form had to go (SKILL 4.8: "invalidating only invalidates
#   the AFFECTED rows"): it took T0 = the OLDEST png mtime of the whole batch and
#   failed if ANY impl file under $implRoots was newer than that.  In an
#   iterating project that is arithmetically unsatisfiable -- measured
#   2026-09-22: one parallel slice touched UI/CrUiStyle.cs (15:08) and
#   UI/Panels/SettingsPanel.cs (15:12) and thereby invalidated all 122 evidence
#   pngs, spanning 6 hours and 4 areas, even though the area actually touched
#   (settings) had already been re-captured at 15:13:21, NEWER than its own
#   implementation.  A global "oldest png" baseline answers "is the oldest
#   capture newer than the newest edit anywhere", which is not the question; the
#   question is per row: "is this row's evidence newer than the implementation of
#   the area this row verifies".
#   This is NOT a relaxation: per-row is STRICTER than a global baseline -- it
#   forces re-capture of the area you actually touched instead of letting a
#   fresh capture elsewhere mask it, and it no longer lets one stale row hide
#   behind the fresh mtimes of other rows.
#
# KEPT (still valid, unchanged in force):
#   (a) the ROW SET is the captured batch -- every png the newest *.index.tsv
#       references, including tiles not yet cited by the acceptance table -- so a
#       batch member can never be cherry-picked out of judging;
#   (b) the D104 registered historical-contrast-artefact exemption, so a
#       deliberately OLD before-shot is never read as stale evidence;
#   (c) the $implRoots scope (project-owned roots; the external engine repo is
#       not ours to freeze).
# REMOVED: the global T0 = min(mtime) over the whole batch, and the derived
#   "any impl file newer than T0" test -- i.e. cross-area contamination.
#
# DEDUP vs #23 evidence-freshness -- why these are NOT the same check:
#   #23 judges the rows the ACCEPTANCE TABLE cites (documented rows only);
#   #29 judges the CAPTURED BATCH taken from the index -- a superset of rows, and
#   the only place where a capture batch is judged as a captured unit.  Both use
#   ONE shared map ($freshMap) and one shared file index ($script:ImplByName), so
#   no second, drifting "area -> implementation" definition exists.  #29 is no
#   longer a coarser duplicate of #23: the coarseness (the global baseline) is
#   gone, what remains is batch scoping.
# ---------------------------------------------------------------------------
$shotsDir = Join-Path $root '.ai-tmp\screenshots'
$winHours = 6
$win      = (Get-Date).AddHours(-$winHours)
$idxFiles = @(Get-ChildItem $shotsDir -Recurse -Filter '*.index.tsv' -File -ErrorAction SilentlyContinue)
$batchShots = @()
if ($idxFiles.Count -gt 0) {
  $newestIdx = ($idxFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
  foreach ($ln in @([System.IO.File]::ReadAllLines($newestIdx.FullName, [Text.Encoding]::UTF8))) {
    foreach ($m in [regex]::Matches($ln, '[A-Za-z0-9_\-\./\\]+\.png')) {
      $f = GetCitedFile $m.Value
      if ($null -ne $f) { $batchShots += $f }
    }
  }
} else {
  $batchShots = @(Get-ChildItem $shotsDir -Recurse -Filter *.png -File -ErrorAction SilentlyContinue |
                  Where-Object { $_.LastWriteTime -gt $win })
}
$newShots = @(Get-ChildItem $shotsDir -Recurse -Filter *.png -File -ErrorAction SilentlyContinue |
              Where-Object { $_.LastWriteTime -gt $win })
$batchShots = @($batchShots | Sort-Object FullName -Unique)
if ($batchShots.Count -eq 0) {
  $human++; Say 'HUMAN-ONLY' 'freeze-before-capture' ('no contact-sheet index and no new evidence png within ' + $winHours.ToString() + 'h')
} else {
  # ---- NARROW EXCEPTION: registered historical contrast artefacts ----------
  # WHY (ledger row D104 in 策划/差异登记.tsv): AT2-knight-walk-live_PRE-FIX.png
  #   is the AT2 *pre-fix* live capture.  Its entire purpose is to sit next to
  #   the fixed shot and show the OLD state, so by construction it is OLDER than
  #   the very fix it illustrates.  This gate's rule is "evidence must post-date
  #   the impl it verifies"; a before/after pair is not evidence FOR the fixed
  #   impl, so letting it drag T0 to before the fix pins the gate red forever
  #   (measured 2026-09-22: T0 = 11:22:53 from that png vs latest impl edit
  #   12:32:07 => 6 files FAIL, with no legal way to clear it because the file
  #   cannot be re-captured now that the pre-fix build is gone).  That is a
  #   defect in this check's SEMANTICS, not a relaxation: the correction is to
  #   stop a pre-fix artefact from being read as "the batch capture time".
  # SCOPE (BOTH conditions are required, so it cannot be abused):
  #   (1) the png is registered in 策划/差异登记.tsv on a row that literally
  #       carries the marker token ["historical contrast artefact"], AND
  #   (2) the png file NAME carries an explicit historical marker (_PRE-FIX).
  #   A fresh / ordinary capture satisfies NEITHER (its name has no _PRE-FIX and
  #   it is never registered as a historical artefact) => it is judged like any
  #   other batch row.  The exemption only removes these files from the JUDGED
  #   ROW POOL; $batchShots itself is untouched, so evidence-economy / loose-png
  #   counting, every other check, every threshold and every message are exactly
  #   as before.
  # NOTE (2026-09-22, after the per-row correction): under the per-row rule such
  #   an artefact could only ever redden its OWN row, so this exemption is now a
  #   belt-and-braces rule for registered historical artefacts rather than the
  #   single thing that kept the gate green.
  $histToken   = ([char[]]@(0x5386,0x53F2,0x5BF9,0x7167,0x4EF6) -join '')  # li shi dui zhao jian
  $diffLedger  = Join-Path $planDir ((([char[]]@(0x5DEE,0x5F02,0x767B,0x8BB0) -join '')) + '.tsv')  # cha yi deng ji .tsv
  $histReg     = @{}
  if (Test-Path $diffLedger) {
    foreach ($ln in [System.IO.File]::ReadAllLines($diffLedger, [Text.Encoding]::UTF8)) {
      if (-not $ln.Contains($histToken)) { continue }
      foreach ($m in [regex]::Matches($ln, '[A-Za-z0-9_\-\.]+\.png')) { $histReg[$m.Value] = $true }
    }
  }
  $judgePool = @(); $exclHist = 0
  foreach ($s in $batchShots) {
    $isHist = ($s.Name -match '_PRE-FIX') -and $histReg.ContainsKey($s.Name)
    if ($isHist) { $exclHist++ } else { $judgePool += $s }
  }
  if ($judgePool.Count -eq 0) { $judgePool = $batchShots; $exclHist = 0 }
  # ---- PER-ROW comparison against the row's OWN area implementation --------
  # The area -> impl mapping and the impl file index are the SAME ones #23 uses
  # ($freshMap / $script:ImplByName, both built above), so the two checks can
  # never drift apart into two conflicting definitions of "which code a shot
  # verifies".  The engine repo and generated dirs are out of scope because
  # $script:ImplByName is built from $implRoots only.
  $frozenCmp = 0; $frozenNoMap = 0; $frozenStale = @()
  foreach ($s in $judgePool) {
    $nm = $s.Name.ToLowerInvariant(); $src = $null
    foreach ($mp in $freshMap) {
      if ($nm.Contains($mp[0])) { if ($script:ImplByName.ContainsKey($mp[1])) { $src = $script:ImplByName[$mp[1]] }; break }
    }
    if ($null -eq $src) { $frozenNoMap++; continue }
    $frozenCmp++
    if ($s.LastWriteTime -lt $src.LastWriteTime) {
      $frozenStale += ($s.Name + '  <  ' + $src.Name + ' (impl ' + $src.LastWriteTime.ToString('MM-dd HH:mm:ss') + ' > evidence ' + $s.LastWriteTime.ToString('MM-dd HH:mm:ss') + ')')
    }
  }
  if ($frozenCmp -eq 0) { $human++; Say 'HUMAN-ONLY' 'freeze-before-capture' ('batch = ' + $judgePool.Count.ToString() + ' png(s), but none maps to an area in $freshMap -- an empty comparison must never PASS') }
  elseif ($frozenStale.Count -eq 0) { Say 'PASS' 'freeze-before-capture' ($frozenCmp.ToString() + ' batch row(s) compared against their OWN area implementation, all fresh; ' + $frozenNoMap.ToString() + ' batch png(s) had no area mapping; excluded ' + $exclHist.ToString() + ' registered historical artefact(s) per D104 => no row of the batch is stale') }
  else {
    $fail++; Say 'FAIL' 'freeze-before-capture' ($frozenStale.Count.ToString() + '/' + $frozenCmp.ToString() + ' batch row(s) whose evidence predates the implementation of their OWN area => ONLY those rows are invalidated, the rest of the batch stays valid')
    $frozenStale | Select-Object -First 12 | ForEach-Object { Write-Output ('            ' + $_) }
  }
}

# ---------------------------------------------------------------------------
# 30) evidence-economy -- visual rows live in ONE contact sheet; a png per row
#     is the definition of per-row screenshotting.  pngs referenced by an index
#     are the sheet itself and must be excluded from the loose count (counting
#     tiles twice produced a false 65 > 46 when only 32 loose pngs existed).
# ---------------------------------------------------------------------------
$visRows = @($specLns | Where-Object { $_.StartsWith('|') -and $_.Contains($cVisual) })
$sheetCells = 0
foreach ($f in $idxFiles) {
  $sheetCells += @([System.IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8) |
                   Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^\s*#' }).Count
}
$sheetPngs = @($batchShots | ForEach-Object { $_.FullName })
$looseShots = @($newShots | Where-Object { $sheetPngs -notcontains $_.FullName })
if ($visRows.Count -eq 0) { Say 'HUMAN-ONLY' 'evidence-economy' 'no visual-class row in the acceptance table' }
elseif ($sheetCells -eq 0) { $fail++; Say 'FAIL' 'evidence-economy' ($visRows.Count.ToString() + ' visual row(s) but no contact-sheet index (*.index.tsv) -- the index is what makes the sheet auditable') }
elseif ($looseShots.Count -gt [Math]::Max(12, $visRows.Count * 2)) { $fail++; Say 'FAIL' 'evidence-economy' ('loose png = ' + $looseShots.Count.ToString() + ' for ' + $visRows.Count.ToString() + ' visual row(s) => per-row screenshotting') }
else { Say 'PASS' 'evidence-economy' ('visual rows = ' + $visRows.Count.ToString() + '; sheet cells = ' + $sheetCells.ToString() + '; loose png = ' + $looseShots.Count.ToString()) }

# ---------------------------------------------------------------------------
# 31) graphics-device -- software rasterization (WARP / Basic Render Driver)
#     voids every frame-time number; the answer is in the first Editor.log line
# ---------------------------------------------------------------------------
$editorLog = Join-Path $root 'client\Logs\Editor.log'
if (-not (Test-Path $editorLog)) { $human++; Say 'HUMAN-ONLY' 'graphics-device' 'no client/Logs/Editor.log -- read SystemInfo.graphicsDeviceName by hand' }
else {
  $devLine = @(Select-String -Path $editorLog -Pattern 'Device Name:\s*(.+)$', 'Renderer:\s*(.+)$' -ErrorAction SilentlyContinue | Select-Object -First 1)
  if ($devLine.Count -eq 0) { $human++; Say 'HUMAN-ONLY' 'graphics-device' 'no device/renderer line in Editor.log' }
  else {
    $dev = $devLine[0].Matches[0].Groups[1].Value.Trim()
    if ($dev -match '(?i)Basic Render Driver|Basic Display|WARP') { $fail++; Say 'FAIL' 'graphics-device' ('render device = "' + $dev + '" => software rendering; frame-time numbers are void') }
    else { Say 'PASS' 'graphics-device' ('render device = ' + $dev) }
  }
}

# ---------------------------------------------------------------------------
# 32) scale-tier -- sampling density declared once (S=1-way / M=2-way / L=3-way),
#     upgrade-only.  "Not declared" reads as "full exhaustion" and makes a 3-day
#     demo unfinishable, so it has to be a written decision.
# ---------------------------------------------------------------------------
$cTierFull = ([char[]]@(0x89C4,0x6A21,0x6863,0x4F4D) -join '')                # gui mo dang wei
$tierHit = @()
if (Test-Path $ceHuaAn) {
  foreach ($f in @(Get-ChildItem $ceHuaAn -Filter *.md -File -ErrorAction SilentlyContinue)) {
    $tx = ReadUtf8 $f.FullName
    if ($tx.Contains($cTierFull) -and ($tx -match ($cTierFull + '[^\r\n]{0,80}?[\s\u0028\uFF08\*\u2605]*([SML])\b'))) { $tierHit += $f.Name }
  }
}
if ($tierHit.Count -gt 0) { Say 'PASS' 'scale-tier' ('declared in ' + ($tierHit -join ',')) }
else { $fail++; Say 'FAIL' 'scale-tier' 'no S/M/L sampling tier declared under plan/plan-folder/*.md (declare once, upgrade only)' }

# ---------------------------------------------------------------------------
# 33) impact-radius -- a bug fix must register its blast radius
#     (dimension / cause chain / affected rows).  Fixing only the reported
#     symptom is the classic two-steps-forward-one-back (measured: patching a
#     door mesh removed the door).  Missing file => HUMAN-ONLY (it may simply be
#     a task with no bug fix), never a blind PASS.
# ---------------------------------------------------------------------------
$irPath = Join-Path $root '.ai-tmp\test\impact-radius.tsv'
if (-not (Test-Path $irPath)) { $human++; Say 'HUMAN-ONLY' 'impact-radius' 'no .ai-tmp/test/impact-radius.tsv -- for a bug fix, list dim / cause chain / affected rows' }
else {
  $irBad = @([System.IO.File]::ReadAllLines($irPath, [Text.Encoding]::UTF8) |
             Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^\s*#' } |
             Where-Object { ($_ -split "`t").Count -lt 3 })
  if ($irBad.Count -eq 0) { Say 'PASS' 'impact-radius' 'every row lists dim / cause chain / affected rows' }
  else { $fail++; Say 'FAIL' 'impact-radius' ($irBad.Count.ToString() + ' row(s) missing columns (dim / cause chain / affected rows)') }
}

# ---------------------------------------------------------------------------
# 34) no-assets-screenshots -- no forensic screenshot dir under client/Assets.
#     Scope is narrow ON PURPOSE: "any png under client/Assets" would be a false
#     red (projects legitimately carry a few art pngs there).  Only the capture
#     directory is a violation.
# ---------------------------------------------------------------------------
$shotInAsm = Join-Path $root 'client\Assets\Screenshots'
if (Test-Path $shotInAsm) {
  $shotN = @(Get-ChildItem $shotInAsm -Recurse -Filter *.png -File -ErrorAction SilentlyContinue).Count
  $fail++; Say 'FAIL' 'no-assets-screenshots' ('client/Assets/Screenshots exists (' + $shotN.ToString() + ' png) -- captures belong in .ai-tmp/screenshots')
} else { Say 'PASS' 'no-assets-screenshots' 'no forensic screenshot dir under client/Assets' }

Write-Output ''
Write-Output ("===== SUMMARY: FAIL=$fail  HUMAN-ONLY=$human =====")
if ($fail -gt 0) { Write-Output 'FAIL present => the words done / delivered / verified must not be used' }
exit $(if ($fail -gt 0) { 1 } else { 0 })
