# cr-u5r-append.ps1 - THE ONLY append path for my ledger (tools/probes = KEEP, repeat-runnable entry point).
#
# WHY THIS EXISTS (CR-D1's live counter-example, 2026-09-23): I repaired a merged-record defect by appending
# a trailing LF - and my own next append, two minutes later, removed that condition again. So "the file ends
# with LF" is a PRECONDITION OF THE APPEND PATH, not a one-off property of the file: a criterion belongs at
# the step that can recur, otherwise every repair expires at the next write. Hence: pre-assert + post-assert
# live HERE, at the writing step, and every append through this tool re-proves them.
#
# FAILURE MODES, DECLARED (the team's rule for anything that touches disk):
#   (i)   empty record            => REFUSE, exit 2, nothing written
#   (ii)  record text contains CR/LF => REFUSE, exit 2 (a multi-line record must be split by the caller;
#                                        silently writing it would produce exactly the structure defect this
#                                        tool exists to prevent)
#   (iii) target missing          => REFUSE, exit 2
#   (iv)  any post-assert fails    => exit 3 and a loud line per failure; the record stays written (it is
#                                        append-only data) but the failure is NEVER swallowed
#   worst case: one extra LF byte when the precondition was absent (harmless, and reported on the PRE line).
# The write is wrapped in try/finally so the handle is released even if Write throws.
param(
    [Parameter(Mandatory = $false)][string]$Text = "",
    [Parameter(Mandatory = $false)][string]$TextFile = "",
    [Parameter(Mandatory = $false)][string]$Path = "C:\Work\Server\f-v2\clover-project-cr\.ai-tmp\test\heartbeat-CR-U5R.tsv"
)
$ErrorActionPreference = 'Continue'
$GLUED = '[^\s]\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?[+-]\d{2}:\d{2}\t'

# RESOLVE EVERY PATH TO AN ABSOLUTE ONE BEFORE ACTING (the team's rule for path handling): never operate on
# a relative literal - a relative literal silently means "relative to whatever the caller's CWD happens to
# be", which is exactly how a read turns into a confident wrong answer. Unresolvable => REFUSE, never guess.
if ($TextFile -ne "") {
    $resolvedText = $null
    try { $resolvedText = (Resolve-Path -LiteralPath $TextFile -ErrorAction Stop).ProviderPath } catch { $resolvedText = $null }
    if ($null -eq $resolvedText) { Write-Host ("REFUSE: cannot resolve text file: " + $TextFile); exit 2 }
    if (-not (Test-Path -LiteralPath $resolvedText)) { Write-Host ("REFUSE: text file not found: " + $resolvedText); exit 2 }
    $TextFile = $resolvedText
    $Text = [System.IO.File]::ReadAllText($TextFile, [System.Text.Encoding]::UTF8)
    $Text = $Text.TrimEnd([char[]]"`r`n")
}
if ($Text -eq "") { Write-Host "REFUSE: empty record"; exit 2 }
if ($Text -match "[`r`n]") { Write-Host "REFUSE: record text contains a line break - split it yourself"; exit 2 }
$resolvedTarget = $null
try { $resolvedTarget = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).ProviderPath } catch { $resolvedTarget = $null }
if ($null -eq $resolvedTarget) { Write-Host ("REFUSE: cannot resolve target: " + $Path); exit 2 }
if (-not (Test-Path -LiteralPath $resolvedTarget)) { Write-Host ("REFUSE: target not found: " + $resolvedTarget); exit 2 }
$Path = $resolvedTarget

$rawBefore = [System.IO.File]::ReadAllBytes($Path)
$txtBefore = [System.Text.Encoding]::UTF8.GetString($rawBefore)
$linesBefore = ($txtBefore -split "`n").Count
$gluedBefore = ([regex]::Matches($txtBefore, $GLUED)).Count
$endsLF = ($rawBefore[$rawBefore.Count - 1] -eq 10)
Write-Host ("PRE : resolved_target=" + $Path)
Write-Host ("PRE : bytes=" + $rawBefore.Count + " lines=" + $linesBefore + " ends_with_LF=" + $endsLF + " glued=" + $gluedBefore)

$preFix = $false
if (-not $endsLF) {
    $preFix = $true
    $fsFix = [System.IO.File]::Open($Path, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write)
    try { $fsFix.Write([byte[]]@(10), 0, 1) } finally { $fsFix.Close() }
    Write-Host "PRE : target did NOT end with LF => one LF was appended first (repairing the PRECONDITION, not the record)"
}

$record = (Get-Date -Format o) + "`t" + $Text
# NO leading newline is written: after the pre-assert above the file is GUARANTEED to end with LF, so a
# leading newline would insert a BLANK LINE between records (noise, and noise hides real structure - the
# first version of this tool did exactly that). The pre-assert is what makes the simple payload correct.
$bytes = [System.Text.Encoding]::UTF8.GetBytes($record + "`n")
$fsRec = [System.IO.File]::Open($Path, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write)
try { $fsRec.Write($bytes, 0, $bytes.Length) } finally { $fsRec.Close() }

$rawAfter = [System.IO.File]::ReadAllBytes($Path)
$txtAfter = [System.Text.Encoding]::UTF8.GetString($rawAfter)
$linesAfter = ($txtAfter -split "`n").Count
$gluedAfter = ([regex]::Matches($txtAfter, $GLUED)).Count
$endsLFAfter = ($rawAfter[$rawAfter.Count - 1] -eq 10)
$recordOwnLine = $txtAfter.Contains("`n" + $record + "`n")
# EXACT expected line delta: the record adds exactly one line, and the precondition repair adds exactly one
# (it terminates the previously unterminated last line). Asserting only "it grew" would let a DUPLICATE
# write pass - and a duplicate is precisely the failure mode of a retried append after a reporting glitch.
$expectedDelta = 1
if ($preFix) { $expectedDelta = 2 }
$blankBeforeRecord = $txtAfter.Contains("`n`n" + $record)
Write-Host ("POST: bytes=" + $rawAfter.Count + " lines=" + $linesAfter + " line_delta=" + ($linesAfter - $linesBefore) + " expected_delta=" + $expectedDelta + " ends_with_LF=" + $endsLFAfter + " glued=" + $gluedAfter + " record_on_own_line=" + $recordOwnLine + " blank_line_before_record=" + $blankBeforeRecord + " precondition_repaired=" + $preFix)

$fails = 0
if (-not $endsLFAfter) { Write-Host "ASSERT FAIL: the file does not end with LF after the write"; $fails++ }
if (-not $recordOwnLine) { Write-Host "ASSERT FAIL: the record is not delimited by newlines (merged!)"; $fails++ }
if ($gluedAfter -ne $gluedBefore) { Write-Host "ASSERT FAIL: the glued-record criterion count changed"; $fails++ }
if ($linesAfter -ne ($linesBefore + $expectedDelta)) { Write-Host ("ASSERT FAIL: line delta " + ($linesAfter - $linesBefore) + " != expected " + $expectedDelta + " (a duplicate or merged write looks exactly like this)"); $fails++ }
if ($blankBeforeRecord) { Write-Host "ASSERT FAIL: a blank line was inserted before the record"; $fails++ }
if ($fails -eq 0) { Write-Host "APPEND OK: precondition held (or was repaired), record on its own line, criterion unchanged."; exit 0 }
Write-Host ("APPEND FAILED: " + $fails + " assertion(s) failed")
exit 3
