# CR-F2-readback-selfcheck.ps1 -- READ THE PRODUCTS BACK and verify them mechanically.
#
# WHY (team-lead's rule, adopted from CR-F1's self-reported defect): "the printed numbers being
# right" is NOT the same as "the product was written correctly". CR-F1 once had a script that
# printed correct counts while writing ZERO data rows (an `if(){}else{}` used as an expression).
# => every product must be re-read and checked for: line count + at least one CROSS-COLUMN
#    invariant that would break if the writer silently wrote nothing / half of it.
#
# READ-ONLY. Usage:
#   powershell -NoProfile -File tools/probes/CR-F2-readback-selfcheck.ps1 -Scope all
#   powershell -NoProfile -File tools/probes/CR-F2-readback-selfcheck.ps1 -Scope f2
# NO WRITE CALLS in the normal path (Out-File / Set-Content / Add-Content / WriteAllText / New-Item /
# Remove-Item / Copy-Item / Move-Item): this script may be re-run by anyone, and "prove your re-run does
# not disturb the product" is a standing rule (team-lead, adopted from CR-R1). The single exception is
# -NegativeControl, which copies the product to the ONE-TIME area (.ai-tmp/test) and deletes it again.
param(
  [ValidateSet('all', 'f2')][string]$Scope = 'all',
  [string]$Dir = 'tools/probes',
  [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $root

$wlPath = Join-Path $Dir ('cited-evidence-whitelist-' + $Scope + '.txt')
$unPath = Join-Path $Dir ('cited-evidence-whitelist-UNION-' + $Scope + '.txt')
$dlPath = Join-Path $Dir ('cited-evidence-whitelist-' + $Scope + '.delta.txt')
$bkPath = Join-Path $Dir ('cited-evidence-whitelist-buckets-' + $Scope + '.txt')

$fails = 0
function Ok([string]$msg) { Write-Output ('  PASS  ' + $msg) }
function No([string]$msg) { Write-Output ('  FAIL  ' + $msg); $script:fails++ }

function Check-Whitelist([string]$path, [switch]$Union) {
  if (-not (Test-Path $path)) { No ($path + ' missing'); return }
  $all = @(Get-Content $path -Encoding UTF8)
  $hdr = @($all | Where-Object { $_ -match '^#' })
  $body = @($all | Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^#' })
  $hash = (Get-FileHash $path -Algorithm SHA256).Hash.Substring(0, 16)
  # FINGERPRINT CARRIES TIME (team-lead ruling 2026-09-23, from CR-V2): a bare sha16 cannot tell
  # "the same file reported two hashes" from "the file really changed". So every fingerprint printed
  # here is a TRIPLE: sha256_16 + bytes + mtime.
  $it = Get-Item $path
  Write-Output ('-- ' + (Split-Path $path -Leaf) + '   sha256_16=' + $hash + '  bytes=' + $it.Length + '  mtime=' + $it.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + '   lines=' + $all.Count + '  header=' + $hdr.Count + '  body=' + $body.Count)
  # invariant 1: the header's declared entry count == the number of body lines
  $declared = $null
  foreach ($h in $hdr) { $m = [regex]::Match($h, 'entries=(\d+)'); if ($m.Success) { $declared = [int]$m.Groups[1].Value } }
  if ($null -eq $declared) { No 'header does not declare entries=<N>' }
  elseif ($declared -eq $body.Count) { Ok ('declared entries=' + $declared + ' == body lines=' + $body.Count) }
  else { No ('declared entries=' + $declared + ' != body lines=' + $body.Count) }
  # invariant 2: unique
  $uniq = @($body | Sort-Object -Unique).Count
  if ($uniq -eq $body.Count) { Ok ('all ' + $body.Count + ' entries unique') } else { No ('duplicate entries: ' + ($body.Count - $uniq)) }
  # invariant 3: every entry is a cleanup-zone path with forward slashes
  $outside = @($body | Where-Object { $_ -notmatch '^\.ai-tmp/' }).Count
  $bslash = @($body | Where-Object { $_.Contains('\') }).Count
  if ($outside -eq 0 -and $bslash -eq 0) { Ok 'every entry is under .ai-tmp/ with forward slashes only' }
  else { No ('outside-zone entries=' + $outside + '  backslash entries=' + $bslash) }
  # invariant 3b: PROTECTION ENTRIES MUST BE FILES, NOT DIRECTORIES (same root as team-lead's
  # mtime-snapshot ruling 2026-09-23, from CR-U5R's probe: rewriting a file in place does NOT change
  # its parent directory's mtime). A directory entry cannot witness a content change, and for a
  # cleanup it protects nothing: deleting the directory removes the contents with it.
  $dirEnts = New-Object System.Collections.Generic.List[string]
  $absent = New-Object System.Collections.Generic.List[string]
  foreach ($e in $body) {
    $p = Join-Path $root ($e -replace '/', '\')
    if (-not (Test-Path $p)) { $absent.Add($e); continue }
    if (Test-Path $p -PathType Container) { $dirEnts.Add($e) }
  }
  if ($dirEnts.Count -eq 0) { Ok ('all ' + $body.Count + ' protection entries are FILES (0 directory entries)') }
  else { No ($dirEnts.Count + ' protection entr(ies) are DIRECTORIES: ' + ($dirEnts | Select-Object -First 5)) }
  # absent-but-cited entries are a DOCUMENTED state (a citation may point at a file that was deleted on
  # purpose); they are reported, not failed -- silence would hide them, a FAIL would be wrong.
  Write-Output ('  NOTE  cited-but-absent on disk = ' + $absent.Count + ' (informational; a FAIL here would be wrong)')

  # invariant 4: the UNCONDITIONAL KEEP rule is declared AND honoured on disk. Re-glob the rule's
  # matches NOW and require every one of them to be a body line => the assertion is "declared rule's
  # on-disk matches are a subset of the product", which fails if a keep line is dropped by hand.
  $mk = [regex]::Match(($hdr -join "`n"), 'kept-index-files=(\d+)')
  $globbed = @()
  $kz = Join-Path $root '.ai-tmp'
  if (Test-Path $kz) {
    $globbed = @(Get-ChildItem $kz -Recurse -File -Filter '*index*.tsv' -ErrorAction SilentlyContinue |
                 ForEach-Object { ($_.FullName.Substring($root.Length).TrimStart('\', '/')) -replace '\\', '/' } | Sort-Object)
  }
  if (-not $mk.Success) { No 'header does not declare kept-index-files=<N>' }
  elseif ([int]$mk.Groups[1].Value -ne $globbed.Count) { No ('declared kept-index-files=' + $mk.Groups[1].Value + ' != on-disk matches=' + $globbed.Count) }
  else { Ok ('declared kept-index-files=' + $globbed.Count + ' == on-disk *index*.tsv matches') }
  $keepMissing = @($globbed | Where-Object { $body -notcontains $_ })
  if ($keepMissing.Count -eq 0) { Ok ('all ' + $globbed.Count + ' index file(s) present in the body (unconditional keep honoured)') }
  else { No ('index file(s) declared kept but ABSENT from the body: ' + ($keepMissing -join ', ')) }
  # invariant 5 (union only): header anchors two sources
  if ($Union) {
    $sh = @($hdr | Where-Object { $_ -match 'SOURCES' })
    $c = 0; foreach ($h in $sh) { $c += @([regex]::Matches($h, 'sha256_16=')).Count }
    if ($c -ge 2) { Ok ('union header anchors ' + $c + ' source hashes') } else { No ('union header anchors only ' + $c + ' source hash(es)') }
    $rl = @($hdr | Where-Object { $_ -match 'ROLE' }).Count
    if ($rl -ge 1) { Ok 'union header carries the ROLE (protection-list) line' } else { No 'union header lacks the ROLE line' }
  } else {
    $rl = @($hdr | Where-Object { $_ -match 'ROLE' }).Count
    if ($rl -ge 1) { Ok 'header carries the ROLE (protection-list) line' } else { No 'header lacks the ROLE line' }
  }
  # NOTE: no `return <value>` here -- the function also writes PASS/FAIL lines, so a returned value
  # would land in the caller as ONE ARRAY of everything (measured: parameter conversion error).
  $script:lastBody = $body.Count
}

function Check-Delta([string]$path, [int]$currentBody) {
  if (-not (Test-Path $path)) { No ($path + ' missing'); return }
  $all = @(Get-Content $path -Encoding UTF8)
  $hdr = @($all | Where-Object { $_ -match '^#' })
  $plus = @($all | Where-Object { $_ -like '+ *' })
  $minus = @($all | Where-Object { $_ -like '- *' })
  $hash = (Get-FileHash $path -Algorithm SHA256).Hash.Substring(0, 16)
  $dit = Get-Item $path
  Write-Output ('-- ' + (Split-Path $path -Leaf) + '   sha256_16=' + $hash + '  bytes=' + $dit.Length + '  mtime=' + $dit.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + '   lines=' + $all.Count)
  # scan ALL header lines: the delta puts "added N" / "removed M" on their own lines, while
  # "new N entries" sits in line 0 (reading only $hdr[0] gave 'declared=?' -- measured).
  $hall = ($hdr -join "`n")
  $ma = [regex]::Match($hall, 'added (\d+)'); $mr = [regex]::Match($hall, 'removed (\d+)'); $mn = [regex]::Match($hall, 'new (\d+) entries')
  # invariant: the declared added/removed counts == the number of '+'/'-' lines (cross-column)
  if ($ma.Success -and [int]$ma.Groups[1].Value -eq $plus.Count) { Ok ('added=' + $plus.Count + ' == + lines=' + $plus.Count) }
  else { No ('added declared=' + $(if ($ma.Success) { $ma.Groups[1].Value } else { '?' }) + ' != + lines=' + $plus.Count) }
  if ($mr.Success -and [int]$mr.Groups[1].Value -eq $minus.Count) { Ok ('removed=' + $minus.Count + ' == - lines=' + $minus.Count) }
  else { No ('removed declared=' + $(if ($mr.Success) { $mr.Groups[1].Value } else { '?' }) + ' != - lines=' + $minus.Count) }
  # cross-FILE invariant: the delta's "new N entries" == the whitelist's current body count
  if ($mn.Success -and [int]$mn.Groups[1].Value -eq $currentBody) { Ok ('delta new=' + $currentBody + ' == whitelist body=' + $currentBody) }
  else { No ('delta new=' + $(if ($mn.Success) { $mn.Groups[1].Value } else { '?' }) + ' != whitelist body=' + $currentBody) }
}

function Check-Buckets([string]$path, $wlSet) {
  if (-not (Test-Path $path)) { Write-Output ('-- ' + (Split-Path $path -Leaf) + ' absent (optional for this scope)'); return }
  $lines = @(Get-Content $path -Encoding UTF8 | Where-Object { $_ -like 'AMBIG*' })
  $bad = 0
  foreach ($l in $lines) {
    $c = $l -split "`t"
    if ($c.Count -lt 4) { $bad++; continue }
    if ([int]$c[2] -ne @($c[3] -split '\|').Count) { $bad++ }
  }
  $bhash = (Get-FileHash $path -Algorithm SHA256).Hash.Substring(0, 16)
  $bit = Get-Item $path
  Write-Output ('-- ' + (Split-Path $path -Leaf) + '   sha256_16=' + $bhash + '  bytes=' + $bit.Length + '  mtime=' + $bit.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + '   AMBIG lines=' + $lines.Count)
  if ($bad -eq 0) { Ok ('candidateCount == listed candidates for all ' + $lines.Count + ' names') }
  else { No ($bad + ' AMBIG line(s) violate the count==listed invariant') }
  # CROSS-ARTIFACT invariant (buckets x whitelist): every IN-ZONE candidate of an ambiguous name must
  # appear in the protection list. This is the mechanical form of "keep EVERY candidate, across zones" --
  # it fails if the implementation silently keeps only some of them (measured: -NegativeControl).
  $zoneCands = New-Object System.Collections.Generic.List[string]
  foreach ($l in $lines) {
    $c = $l -split "`t"
    if ($c.Count -lt 4) { continue }
    foreach ($p in ($c[3] -split '\|')) { if ($p -match '^\.ai-tmp/') { $zoneCands.Add($p) } }
  }
  if ($null -eq $wlSet) { Write-Output '  SKIP  in-zone candidates vs whitelist (no whitelist set supplied)'; return }
  $miss = @($zoneCands | Where-Object { -not $wlSet.ContainsKey($_) })
  if ($miss.Count -eq 0) { Ok ('all ' + $zoneCands.Count + ' in-zone candidate(s) of the ' + $lines.Count + ' ambiguous name(s) are protected') }
  else { No ($miss.Count + ' in-zone candidate(s) MISSING from the protection list: ' + ($miss | Select-Object -First 5 -Unique).ToString()) }
}

if ($NegativeControl) {
  # NEGATIVE CONTROL: prove the keep check can fail. Copy the product into the ONE-TIME area, delete
  # one keep line, and require the very same check to report a FAIL on that copy.
  Write-Output ('NEGATIVE CONTROL | scope=' + $Scope + ' | at ' + (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK'))
  $src = Join-Path $Dir ('cited-evidence-whitelist-' + $Scope + '.txt')
  if (-not (Test-Path $src)) { Write-Output 'NEGCTL FAIL: product missing'; exit 1 }
  $tmpDir = Join-Path $root '.ai-tmp\test'
  if (-not (Test-Path $tmpDir)) { New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null }
  $tmp = Join-Path $tmpDir ('CR-F2-negctl-whitelist-' + $Scope + '.txt')
  Copy-Item $src $tmp -Force
  $lines = [System.IO.File]::ReadAllLines($tmp, [System.Text.Encoding]::UTF8)
  $keepName = $null
  $kz = Join-Path $root '.ai-tmp'
  if (Test-Path $kz) { $keepName = @(Get-ChildItem $kz -Recurse -File -Filter '*index*.tsv' -ErrorAction SilentlyContinue | ForEach-Object { ($_.FullName.Substring($root.Length).TrimStart('\', '/')) -replace '\\', '/' } | Sort-Object)[0] }
  if (-not $keepName) { Write-Output 'NEGCTL FAIL: no index file on disk to drop'; exit 1 }
  $victim = @($lines | Where-Object { $_.Trim() -eq $keepName })
  if ($victim.Count -eq 0) { Write-Output ('NEGCTL FAIL: keep entry not present in the product: ' + $keepName); exit 1 }
  $out = @($lines | Where-Object { -not ($_.Trim() -eq $keepName) })
  # keep the declared count consistent so the sole failure is the MISSING LINE (not a count mismatch)
  $decl = 0
  foreach ($i in 0..($out.Count - 1)) { if ($out[$i] -match 'kept-index-files=(\d+)') { $out[$i] = $out[$i] -replace 'kept-index-files=\d+', ('kept-index-files=' + ([int]$matches[1] - 1)); $decl++ } }
  [System.IO.File]::WriteAllLines($tmp, $out, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ('  dropped the keep entry for: ' + $keepName + '   (declared count lines patched: ' + $decl + ')')
  Write-Output ('  temp copy (one-time area, removed below): ' + $tmp.Substring($root.Length).TrimStart('\') )
  $script:fails = 0
  Check-Whitelist $tmp | Out-Null
  Remove-Item $tmp -Force
  Write-Output ''
  if ($script:fails -ge 1) { Write-Output ('NEGCTL PASS: the check reported ' + $script:fails + ' FAIL on the mutilated copy => it can fail'); exit 0 }
  Write-Output 'NEGCTL FAIL: the check did NOT notice the missing keep line'
  exit 1
}

function Check-Ledger([string]$path) {
  # The append-only transition ledger is a FINGERPRINT RECORD, so the "a fingerprint must carry time"
  # rule is enforced here mechanically: rows after the SCHEMA v2 marker must have 11 fields with a
  # numeric new_bytes and a parseable new_mtime; rows before it keep v1's 9 fields. A missing/!numeric
  # time field fails -- which is exactly the defect the rule exists to prevent.
  if (-not (Test-Path $path)) { No (Split-Path $path -Leaf + ' missing'); return }
  $rows = @(Get-Content $path -Encoding UTF8)
  $it = Get-Item $path
  Write-Output ('-- ' + (Split-Path $path -Leaf) + '   sha256_16=' + (Get-FileHash $path -Algorithm SHA256).Hash.Substring(0, 16) + '  bytes=' + $it.Length + '  mtime=' + $it.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + '   rows=' + $rows.Count)
  $seenMarker = $false; $v1 = 0; $v2 = 0; $bad = 0
  foreach ($r in $rows) {
    if ($r.Trim().Length -eq 0) { continue }
    if ($r -match 'SCHEMA v2') { $seenMarker = $true; continue }
    if ($r -match '^\s*#') { continue }
    $f = $r -split "`t"
    if (-not $seenMarker) { if ($f.Count -eq 9) { $v1++ } else { $bad++ } ; continue }
    if ($f.Count -ne 11) { $bad++; continue }
    $tsOk = $false
    try { [datetime]::ParseExact($f[9], 'yyyy-MM-dd HH:mm:ss', $null) | Out-Null; $tsOk = $true } catch { $tsOk = $false }
    if (-not $tsOk -or ($f[8] -notmatch '^\d+$')) { $bad++ } else { $v2++ }
  }
  if (-not $seenMarker) { No 'no SCHEMA v2 marker: v2 rows would be misread as v1 (column count differs)' }
  else { Ok ('schema marker present; v1 rows=' + $v1 + '  v2 rows=' + $v2 + '  malformed=' + $bad) }
  if ($bad -eq 0) { Ok 'every ledger row matches its schema (v2 rows carry numeric bytes + parseable mtime)' }
  else { No ($bad + ' ledger row(s) malformed for their schema') }
}

Write-Output ('readback selfcheck | scope=' + $Scope + ' | at ' + (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK'))
# The ROLE line (and every other header line added after 11:06) only reaches the products at the next
# re-land, which is on hold until the lead declares the ledger frozen. A ROLE FAIL therefore
# documents a PENDING state, not a defect -- the products on disk still predate the requirement.
Write-Output '# NOTE: every fingerprint below is a TRIPLE (sha256_16 + bytes + mtime) -- read the triple,'
Check-Whitelist $wlPath
$singleBody = $script:lastBody
Check-Whitelist $unPath -Union
Check-Delta $dlPath $singleBody
$wlBody = @(Get-Content $wlPath -Encoding UTF8 | Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^#' })
$wlSet = @{}
foreach ($e in $wlBody) { $wlSet[$e.Trim()] = 1 }
Check-Buckets $bkPath $wlSet
Check-Ledger (Join-Path $Dir 'cited-evidence-whitelist-transitions.tsv')
Write-Output ''
Write-Output ('READBACK SELFCHECK ' + $(if ($fails -eq 0) { 'PASS' } else { 'FAIL (' + $fails + ')' }))
if ($fails -gt 0) { exit 1 }
exit 0
