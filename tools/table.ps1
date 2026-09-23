# clover-project-cr : one-click config-table pipeline
#
#   txt source tables --[table -pack]--> xlsx (planner edits in Excel)
#                     --[table]---------> tsv + typed code (server + client)
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/table.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/table.ps1 -Force
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/table.ps1 -ClientDir <path>
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/table.ps1 -NoPack   # keep planner xlsx
#
# The tool is run from source (`go run ./cmd/table` in <repo>/clover-tools/table/core):
# the prebuilt table.exe in that folder is an OLDER build that does not know
# -pack / -pack-dir / -batch, and without -batch it blocks waiting for Enter.
#
# NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 parses a .ps1
#       without a BOM as ANSI/GBK, which corrupts any CJK literal and breaks parsing.
#       Every non-ASCII path below is built from code points instead.

[CmdletBinding()]
param(
  [string]$Root = '',
  [string]$Config = '',
  [string]$ClientDir = '',
  [string]$TableSourceDir = '',
  [string]$TableExe = '',
  [switch]$Force,
  [switch]$NoPack,
  [switch]$NoGen
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
  $Root = Split-Path $PSScriptRoot -Parent
}
$Root = (Resolve-Path -LiteralPath $Root).Path

if ([string]::IsNullOrWhiteSpace($Config)) {
  $Config = Join-Path $Root 'tools\table.config.yaml'
}
$Config = (Resolve-Path -LiteralPath $Config).Path

if ([string]::IsNullOrWhiteSpace($TableSourceDir)) {
  $TableSourceDir = Join-Path $Root '..\clover-tools\table\core'
}

# ---- non-ASCII names, built from code points (keeps this file ASCII-only) ----
$planDirName = ([char[]]@(0x7B56, 0x5212) -join '')                    # ce hua
$numDocName  = ([char[]]@(0x6570, 0x503C, 0x6587, 0x6863) -join '')    # shu zhi wen dang
$mappingName = ([char[]]@(0x6253, 0x8868, 0x5BF9, 0x7167, 0x8868) -join '') + '.tsv'
$packSuffix  = '-pack'

# 表名（= sheet 名 = 源表 txt 的文件名去扩展名 = tsv 文件名去扩展名）。
# ASCII-only on purpose: the generator derives Go type/field names from the sheet name, and
# Go only treats a leading UPPERCASE ASCII letter as exported -- a CJK sheet name produced
# `table.Tables{卡牌 ...}` whose fields cannot be referenced (or reached via reflect) from any
# other package. The xlsx *file* keeps its Chinese name only if the txt keeps it too (the pack
# step names the sheet after the txt file), so both are ASCII here.
$cardName    = 'card_cs'
$unitName    = 'unit_cs'
$spellName   = 'spell_cs'

Write-Output ("root       = " + $Root)
Write-Output ("config     = " + $Config)

# ---- locate the table tool ---------------------------------------------------
$toolExe = ''
if (-not [string]::IsNullOrWhiteSpace($TableExe)) {
  $toolExe = (Resolve-Path -LiteralPath $TableExe).Path
} else {
  $src = (Resolve-Path -LiteralPath $TableSourceDir).Path
  $goCmd = Get-Command go -ErrorAction SilentlyContinue
  if ($null -ne $goCmd) {
    $toolExe = ''            # use: go run ./cmd/table   (cwd = $src)
  } else {
    $cand = Join-Path $src 'table.exe'
    if (Test-Path -LiteralPath $cand) {
      $toolExe = (Resolve-Path -LiteralPath $cand).Path
      Write-Warning "go not found: falling back to $toolExe (may be an older build without -pack)"
    } else {
      Write-Error "neither go nor table.exe available (looked in $src)"
    }
  }
  $TableSourceDir = $src
}

function Invoke-Table([string[]]$ToolArgs) {
  if ([string]::IsNullOrWhiteSpace($toolExe)) {
    Push-Location -LiteralPath $TableSourceDir
    try { & go run ./cmd/table @ToolArgs } finally { Pop-Location }
  } else {
    & $toolExe @ToolArgs
  }
}

# ---- resolve the effective config (client_dir override -> temp copy) --------
$cfgTmpDir = Join-Path $Root '.ai-tmp\test'
$packDir   = Join-Path (Join-Path $Root $planDirName) $numDocName
$effConfig = $Config
if (-not [string]::IsNullOrWhiteSpace($ClientDir)) {
  if (-not (Test-Path -LiteralPath $cfgTmpDir)) { New-Item -ItemType Directory -Path $cfgTmpDir -Force | Out-Null }
  $effConfig = Join-Path $cfgTmpDir 'table-config.effective.yaml'
  $lines = [System.IO.File]::ReadAllLines($Config, [System.Text.Encoding]::UTF8)
  $out = New-Object System.Collections.Generic.List[string]
  $hit = $false
  foreach ($ln in $lines) {
    if ($ln -match '^\s*client_dir\s*:') {
      $out.Add('client_dir: "' + ($ClientDir -replace '\\', '/') + '"')
      $hit = $true
    } else {
      $out.Add($ln)
    }
  }
  if (-not $hit) { Write-Error "config has no client_dir line: $Config" }
  [System.IO.File]::WriteAllLines($effConfig, $out.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ("client_dir = " + $ClientDir + "   (via " + $effConfig + ")")
}

# ---- step 1: pack source txt -> xlsx (planner-facing) -----------------------
if (-not $NoPack) {
  if (-not (Test-Path -LiteralPath $packDir)) { Write-Error "planning dir not found: $packDir" }
  $txts = @(Get-ChildItem -LiteralPath $packDir -Filter *.txt -File)
  if ($txts.Count -eq 0) { Write-Error "no *.txt source tables in $packDir" }

  # a planner-edited <name>-pack.xlsx is authoritative: only repack it with -Force
  $todo = @()
  foreach ($t in $txts) {
    $base = [System.IO.Path]::GetFileNameWithoutExtension($t.Name)
    $target = Join-Path $packDir ($base + $packSuffix + '.xlsx')
    if ((Test-Path -LiteralPath $target) -and (-not $Force)) {
      Write-Output ("  skip   " + $t.Name + "  (planner copy exists: " + [System.IO.Path]::GetFileName($target) + "; use -Force to repack)")
    } else {
      $todo += $t
    }
  }

  if ($todo.Count -gt 0) {
    Write-Output ''
    Write-Output '--- step 1/2 : pack source tables (txt -> xlsx) ---'
    $packArgs = @('-pack', '-pack-dir', $packDir, '-batch')
    Invoke-Table $packArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "table -pack failed (exit $LASTEXITCODE)" }
    foreach ($t in $todo) {
      $base = [System.IO.Path]::GetFileNameWithoutExtension($t.Name)
      $plain = Join-Path $packDir ($base + '.xlsx')
      $target = Join-Path $packDir ($base + $packSuffix + '.xlsx')
      if (-not (Test-Path -LiteralPath $plain)) { Write-Error "pack produced no xlsx for $($t.Name)" }
      Move-Item -LiteralPath $plain -Destination $target -Force
      Write-Output ("  pack   " + $t.Name + " -> " + [System.IO.Path]::GetFileName($target))
    }
  }
}

# ---- step 2: xlsx -> tsv + typed code --------------------------------------
if (-not $NoGen) {
  Write-Output ''
  Write-Output '--- step 2/2 : generate tsv + typed code ---'
  $genArgs = @('-config', $effConfig, '-batch')
  if ($Force) { $genArgs += '-force' }
  Invoke-Table $genArgs
  if ($LASTEXITCODE -ne 0) { Write-Error "table generation failed (exit $LASTEXITCODE)" }
}

# ---- verify the deliverables ------------------------------------------------
Write-Output ''
Write-Output '--- outputs ---'
$srvDir = Join-Path $Root 'server\game\table'
foreach ($n in @($cardName, $unitName, $spellName)) {
  $logical = $n.Substring(0, $n.Length - 3)
  $tsv = Join-Path (Join-Path $srvDir 'tsv') ($logical + '.tsv')
  if (Test-Path -LiteralPath $tsv) {
    $n_rows = ([System.IO.File]::ReadAllLines($tsv, [System.Text.Encoding]::UTF8)).Count - 1
    Write-Output ("  " + $n + " : " + $n_rows + " data row(s)  " + $tsv)
  } else {
    Write-Error ("missing generated tsv: " + $tsv)
  }
}
$map = Join-Path $packDir $mappingName
if (Test-Path -LiteralPath $map) { Write-Output ("  mapping : " + $map) }
Write-Output 'table pipeline OK'
