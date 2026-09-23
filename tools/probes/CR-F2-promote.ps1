# ★ CR-F2S 2026-09-23：**升格入口**（judgement asset）。把一件产物从"一次性/工作区"升格到"保留区"，
#   并往 append-only 账本 `transitions.tsv` 记一行**六列**。⛔ 不做"悄悄覆盖"。
#
# 硬规则（与 team-lead 裁定一致）：
#   · **目标已存在 ⇒ 停手报错**（exit 3），⛔ 不覆盖、⛔ 不写账本行、目标字节一个都不改；
#   · 时间**由本命令生成**（⛔ 不许调用方手写一个时间进来 —— 没有这个参数）；
#   · 校验的是**升格后目标的逐字节 sha256**（copy 完再算，并跟源比对到字节），⛔ 不是"我记得它是同一份"；
#   · 是否删源按**实际观察到的结果**记（删源失败 ⇒ 记 `no(delete-failed…)`，⛔ 不许记成 yes）。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\probes\CR-F2-promote.ps1 `
#       -Source <源> -Target <目标> [-Actor <署名>] [-DeleteSource] [-Ledger <账本路径>]
#
# 退出码：0 成功 ／ 2 源不存在 ／ 3 **目标已存在（停手）** ／ 4 复制后逐字节校验不过（已撤回目标副本）
#         ／ 5 账本写入后回读核对失败 ／ 6 账本不可写
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$Actor = 'CR-F2S',
    [switch]$DeleteSource,
    [string]$Ledger = 'C:\Work\Server\f-v2\clover-project-cr\tools\probes\transitions.tsv',
    [string]$ProjectRoot = 'C:\Work\Server\f-v2\clover-project-cr'
)
$ErrorActionPreference = 'Stop'
$encNoBom = New-Object Text.UTF8Encoding($false)

# ── 账本首块：**照抄**已裁定的三行署名（⛔ 不许改写）＋ 一行列名（同样以 `#` 起，六列 TAB 对齐） ──
$ledgerHeader = @'
# relay-land = copy by CR-V2 @14:13:51.614（源保留、无删源拍）
# relay2/relay4 = copy by CR-V2 @14:13:51.629/.637 ＋ 校验逐字节相同 ＋ 删源 by CR-F2（依自报，删源后不可复核）
# relay3 = 执行者未确认（created 13:29:15.737）
'@

function Get-Sha256Hex([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash([IO.File]::ReadAllBytes($Path)) | ForEach-Object { $_.ToString('x2') }) -join '') }
    finally { $sha.Dispose() }
}
function Resolve-Abs([string]$p) {
    if ([IO.Path]::IsPathRooted($p)) { return $p }
    return (Join-Path $ProjectRoot $p)
}

$srcFull = Resolve-Abs $Source
$dstFull = Resolve-Abs $Target

# ── 前置闸门 ──
if (-not (Test-Path -LiteralPath $srcFull)) {
    Write-Output ('STOP 源不存在，未做任何事: ' + $srcFull)
    exit 2
}
if (Test-Path -LiteralPath $dstFull) {           # ★ 裁定：目标已存在 ⇒ 停手（⛔ 不覆盖、不写账本）
    $shaExist = Get-Sha256Hex $dstFull
    Write-Output ('STOP 目标已存在 ⇒ 停手、⛔ 不覆盖: ' + $dstFull)
    Write-Output ('     目标现状（未改动）sha256=' + $shaExist + ' bytes=' + (Get-Item -LiteralPath $dstFull).Length)
    Write-Output '     账本未加行。'
    exit 3
}
if (-not (Test-Path -LiteralPath $Ledger)) {
    try {
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $Ledger)
        # ⚠️ 列名行必须是**恰好 6 段**（`#` 挂在第一段）⇒ 与数据行的列数一致，便于机器核对
        [IO.File]::WriteAllText($Ledger, $ledgerHeader + "`n" + ("#时刻`t执行者`t源路径`t目标路径`tsha256`t是否删源") + "`n", (New-Object Text.UTF8Encoding($true)))
        Write-Output ('账本不存在 ⇒ 已建（含首块署名 3 行）: ' + $Ledger)
    } catch {
        Write-Output ('STOP 账本不可建/不可写: ' + $Ledger + ' :: ' + $_.Exception.Message)
        exit 6
    }
}

# ── 升格：复制 + **逐字节**校验 ──
$null = New-Item -ItemType Directory -Force -Path (Split-Path $dstFull)
[IO.File]::Copy($srcFull, $dstFull, $false)
$shaSrc = Get-Sha256Hex $srcFull
$shaDst = Get-Sha256Hex $dstFull
if ($shaSrc -cne $shaDst) {
    Remove-Item -LiteralPath $dstFull -Force -ErrorAction SilentlyContinue
    Write-Output ('STOP 复制后逐字节校验不过（已撤回目标副本）: src=' + $shaSrc + ' dst=' + $shaDst)
    exit 4
}

# ── 是否删源：按**实际结果**记 ──
$delFlag = 'no'
if ($DeleteSource) {
    Remove-Item -LiteralPath $srcFull -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $srcFull) { $delFlag = 'no(delete-failed)' } else { $delFlag = 'yes' }
}

# ── 记一行（时刻由本命令生成） ──
$stamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
$row = $stamp + "`t" + $Actor + "`t" + $srcFull + "`t" + $dstFull + "`t" + $shaDst + "`t" + $delFlag
$b = [IO.File]::ReadAllBytes($Ledger)
if ($b.Length -gt 0 -and $b[$b.Length - 1] -ne 10) { [IO.File]::AppendAllText($Ledger, "`n", $encNoBom) }
[IO.File]::AppendAllText($Ledger, $row + "`n", $encNoBom)

# ── 回读核对：账本最后一行必须逐字符等于刚写的 row（⛔ 不信"写过了"） ──
$all = [IO.File]::ReadAllLines($Ledger)
$last = $all[$all.Count - 1]
if ((@($all | Where-Object { $_.Trim().Length -eq 0 }).Count -gt 0)) {
    # 空行只允许出现在末尾（拼装时不会产生），这里不改写，只提示
    Write-Output 'NOTE 账本里出现空行（非本次写入造成），请人工查看。'
}
if ($last -cne $row.TrimEnd()) {
    Write-Output ('STOP 账本回读核对失败（最后一行 ≠ 刚写的行）: last=' + $last)
    exit 5
}
Write-Output 'OK 升格完成'
Write-Output ('  row   = ' + $row)
Write-Output ('  sha256(src)=' + $shaSrc)
Write-Output ('  sha256(dst)=' + $shaDst + '  （逐字节相同）')
Write-Output ('  是否删源 = ' + $delFlag + '  （源' + $(if ($delFlag -eq 'yes') { '已删除' } else { '保留' }) + '）')
Write-Output ('  账本 = ' + $Ledger + '  数据行=' + (@($all | Where-Object { $_.Trim().Length -gt 0 -and -not $_.StartsWith('#') }).Count))
exit 0
