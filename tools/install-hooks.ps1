# Installs the mandatory gate as a git hook (SKILL section 0.5 item 2).
# ASCII-only on purpose: PowerShell 5.1 parses a BOM-less .ps1 as ANSI.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not (Test-Path (Join-Path $root '.git'))) {
  Write-Output 'not a git repository (clover-project-cr should own its own repo) - nothing installed'
  exit 0
}

git config core.hooksPath tools/hooks
$p = git config --local --get core.hooksPath
Write-Output ("core.hooksPath = " + $p)

$hook = Join-Path $root 'tools/hooks/pre-commit'
if (Test-Path $hook) { Write-Output ('hook present: ' + $hook) }
else { Write-Output 'WARNING: tools/hooks/pre-commit missing'; exit 1 }
