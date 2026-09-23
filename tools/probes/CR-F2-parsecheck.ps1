# CR-F2 scratch helper: report the first parse errors of a PowerShell file (no execution).
# Usage: powershell -NoProfile -File tools/probes/CR-F2-parsecheck.ps1 <path>
#   (authoritative copy lives in tools/probes; a byte-identical copy may exist in .ai-tmp/test, which is
#    the one-time area -- cite the tools/probes path, it survives delivery)
param([Parameter(Mandatory = $true)][string]$Path)
$errs = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $Path).Path, [ref]$null, [ref]$errs)
if ($errs -and $errs.Count -gt 0) {
  $errs | Select-Object -First 6 | ForEach-Object {
    Write-Output ($_.Extent.StartLineNumber.ToString() + ': ' + $_.Message + '  <<< ' + $_.Extent.Text)
  }
  # EXIT CODE (defect self-caught 2026-09-23 by its own negative control): the first version PRINTED the
  # errors and still exited 0, so any caller checking $LASTEXITCODE -- or a gate wired to it -- read "OK" on
  # a broken file. That is the "read failure swallowed" / "assertion that cannot fail" family. A checker that
  # reports by stdout only is unusable as a gate; the count is echoed so a human sees it too.
  Write-Output ('PARSE FAILED: ' + $errs.Count + ' error(s) -- file NOT usable')
  exit 1
} else { Write-Output 'PARSE OK'; exit 0 }
