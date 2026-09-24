# ★ CR-F2S 2026-09-23 判据资产：`CR-F2-promote.ps1`（升格入口）＋ `promotions.tsv`（append-only 账本）的自检。
#
# 验的三件事（每条都写明"能失败"的方式）：
#   A 交付账本首块 = 已裁定的三行署名（逐字符 `-ceq`），列名行恰好 6 段 ⇒ 署名没有被改写／没有打错字；
#   B 正控：连升【两件不同文件】⇒ 账本 +2 数据行、每行恰好 6 段、时刻**由命令生成**（落在本次运行的
#     [t0,t1] 区间内，且等于目标文件的逐字节 sha256）⇒ 手写的时间戳/写错的 hash 都会被抓红；
#   C 负控：对**已存在的目标**再升一次 ⇒ 停手报错（退出码非 0）＋ 账本**不加行** ＋ 目标字节**不变**；
#     另一条负控：源不存在 ⇒ 同样停手、账本不加行。
#
# ⚠️ 自检跑在**临时账本**上（`-Ledger <sandbox>\ledger.tsv`），交付账本 `promotions.tsv` 只被【只读】核对：
#    append-only 的交付物里不留"指向一次性目录、事后又被删掉"的测试行。同一段代码、只差一个账本参数。
param(
    [string]$Promote = 'C:\Work\Server\f-v2\clover-project-cr\tools\probes\CR-F2-promote.ps1',
    [string]$Ledger = 'C:\Work\Server\f-v2\clover-project-cr\tools\probes\promotions.tsv',
    [string]$DeliveredLedger = 'C:\Work\Server\f-v2\clover-project-cr\tools\probes\promotions.tsv',
    [string]$ProjectRoot = 'C:\Work\Server\f-v2\clover-project-cr',
    [switch]$ShowChildOutput
)
$ErrorActionPreference = 'Stop'
$fails = 0
function Ck($name, $got, $want) {
    $ok = ("$got" -eq "$want")
    if (-not $ok) { $script:fails++ }
    Write-Host ("  {0}  {1}  got={2} want={3}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $got, $want))
}
function Get-Sha256Hex([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash([IO.File]::ReadAllBytes($Path)) | ForEach-Object { $_.ToString('x2') }) -join '') }
    finally { $sha.Dispose() }
}
function Read-DataRows([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $enc = New-Object Text.UTF8Encoding($false)
    return @([IO.File]::ReadAllLines($Path, $enc) | Where-Object { $_.Trim().Length -gt 0 -and -not $_.StartsWith('#') })
}
function Invoke-Promote([string[]]$Extra) { return (Invoke-ChildScript $script:Promote $Extra) }
function Invoke-ChildScript([string]$ScriptPath, [string[]]$Extra) {
    $PSExe = (Get-Command powershell.exe).Source
    $argv = New-Object System.Collections.Generic.List[string]
    foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath)) { $argv.Add($a) }
    foreach ($a in $Extra) { $argv.Add($a) }
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # 原生程序 ＋ 2>&1 ＋ Stop ⇒ NativeCommandError 会终止宿主（本片踩过）
    $out = (& $PSExe @argv 2>&1 | Out-String)
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prevEap
    [pscustomobject]@{ Exit = $code; Out = $out }
}

# ── A. 交付账本首块（只读、⛔ 不写） ──
Write-Host '=== A. 交付账本首块署名（只读核对）==='
$wantL1 = '# relay-land = copy by CR-V2 @14:13:51.614（源保留、无删源拍）'
$wantL2 = '# relay2/relay4 = copy by CR-V2 @14:13:51.629/.637 ＋ 校验逐字节相同 ＋ 删源 by CR-F2（依自报，删源后不可复核）'
$wantL3 = '# relay3 = 执行者未确认（created 13:29:15.737）'
$delivered = [IO.File]::ReadAllLines($DeliveredLedger, (New-Object Text.UTF8Encoding($true)))
Ck '交付账本 L1 逐字符 = 裁定原文' ([bool]($delivered[0] -ceq $wantL1)) 'True'
Ck '交付账本 L2 逐字符 = 裁定原文' ([bool]($delivered[1] -ceq $wantL2)) 'True'
Ck '交付账本 L3 逐字符 = 裁定原文' ([bool]($delivered[2] -ceq $wantL3)) 'True'
Ck '交付账本 列名行列数 = 6' (@($delivered[3] -split "`t").Count) 6
Ck '交付账本 全部非注释行都是 6 段（含列名行；结构性判据=按 TAB 分段）' `
    (@($delivered | Where-Object { $_.Trim().Length -gt 0 -and -not $_.StartsWith('#') } | Where-Object { @($_ -split "`t").Count -ne 6 }).Count) 0
Write-Host ("  （信息读数）交付账本当前数据行 = " + (Read-DataRows $DeliveredLedger).Count + " 行")

# ── B/C. 正/负控：跑在临时沙箱 ＋ 临时账本 ──
$sb = Join-Path $ProjectRoot ('.ai-tmp\test\crf2s-promote-selftest-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$null = New-Item -ItemType Directory -Force -Path $sb
$ledgerTemp = Join-Path $sb 'ledger.tsv'
$srcA = Join-Path $sb 'src-alpha.txt'
$srcB = Join-Path $sb 'src-beta.txt'
$dstDir = Join-Path $sb 'dst'
$dstA = Join-Path $dstDir 'a.txt'
$dstB = Join-Path $dstDir 'b.txt'
$encNoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($srcA, 'alpha-payload-CR-F2S', $encNoBom)
[IO.File]::WriteAllText($srcB, 'beta-payload-CR-F2S', $encNoBom)
# ⚠️ 源的 sha 必须在**升格前**取好：第 2 件带 `-DeleteSource`（源会被删掉），
#    第二版直接在升格后去算 src-beta 的 sha ⇒ 自己抛 FileNotFoundException（本自检当场抓红，已改）
$shaA0 = Get-Sha256Hex $srcA
$shaB0 = Get-Sha256Hex $srcB
Write-Host ("sandbox=" + $sb)
try {
    Write-Host '=== B. 正控：连升两件不同文件 ⇒ 账本 +2 行、六列齐、时刻由命令生成 ==='
    $rowsBefore = (Read-DataRows $ledgerTemp).Count
    $t0 = Get-Date
    $r1 = Invoke-Promote @('-Source', $srcA, '-Target', $dstA, '-Ledger', $ledgerTemp, '-Actor', 'CR-F2S-selftest')
    $r2 = Invoke-Promote @('-Source', $srcB, '-Target', $dstB, '-Ledger', $ledgerTemp, '-Actor', 'CR-F2S-selftest', '-DeleteSource')
    $t1 = Get-Date
    if ($ShowChildOutput) { Write-Host '---- child1 ----'; Write-Host $r1.Out; Write-Host '---- child2 ----'; Write-Host $r2.Out }
    Ck 'B 第一次升格 退出码 = 0' $r1.Exit 0
    Ck 'B 第二次升格（-DeleteSource）退出码 = 0' $r2.Exit 0
    $rowsAfter = Read-DataRows $ledgerTemp
    Ck 'B 账本数据行 = 升格前 + 2' $rowsAfter.Count ($rowsBefore + 2)
    $f1 = @($rowsAfter[0] -split "`t")
    $f2 = @($rowsAfter[1] -split "`t")
    Ck 'B 第1行段数 = 6' $f1.Count 6
    Ck 'B 第2行段数 = 6' $f2.Count 6
    $isoRe = '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$'
    Ck 'B 第1行时刻 = ISO8601 带偏移（结构化判据：行首形态）' ([bool]($f1[0] -cmatch $isoRe)) 'True'
    Ck 'B 第2行时刻 = ISO8601 带偏移' ([bool]($f2[0] -cmatch $isoRe)) 'True'
    $d1 = [datetime]::Parse($f1[0])
    $d2 = [datetime]::Parse($f2[0])
    Ck 'B 两行时刻都落在本次运行 [t0-2s, t1+2s] 内 ⇒ 由命令生成（⛔ 非手写）' `
        ([bool](($d1 -ge $t0.AddSeconds(-2)) -and ($d1 -le $t1.AddSeconds(2)) -and ($d2 -ge $t0.AddSeconds(-2)) -and ($d2 -le $t1.AddSeconds(2)))) 'True'
    Ck 'B 执行者列 = 传入的 -Actor' $f1[1] 'CR-F2S-selftest'
    Ck 'B 第1行源列 = 绝对源路径' $f1[2] $srcA
    Ck 'B 第1行目标列 = 绝对目标路径' $f1[3] $dstA
    Ck 'B 第1行 sha256 = 目标文件逐字节重算值' $f1[4] (Get-Sha256Hex $dstA)
    Ck 'B 第2行 sha256 = 目标文件逐字节重算值' $f2[4] (Get-Sha256Hex $dstB)
    Ck 'B 第1行 是否删源 = no（未传 -DeleteSource）' $f1[5] 'no'
    Ck 'B 第2行 是否删源 = yes' $f2[5] 'yes'
    Ck 'B 目标 a.txt 已生成且逐字节 = 升格前的源' (Get-Sha256Hex $dstA) $shaA0
    Ck 'B 目标 b.txt 已生成且逐字节 = 升格前的源（源已删，故用升格前记下的 sha）' (Get-Sha256Hex $dstB) $shaB0
    Ck 'B 第1件源保留（未删源）' (Test-Path -LiteralPath $srcA) 'True'
    Ck 'B 第2件源已删（-DeleteSource）' (Test-Path -LiteralPath $srcB) 'False'

    Write-Host '=== C 负控：目标已存在 ⇒ 停手报错、账本不加行、目标字节不变 ==='
    $shaBefore = Get-Sha256Hex $dstA
    $bytesBefore = (Get-Item -LiteralPath $dstA).Length
    $rn = Invoke-Promote @('-Source', $srcA, '-Target', $dstA, '-Ledger', $ledgerTemp, '-Actor', 'CR-F2S-selftest')
    if ($ShowChildOutput) { Write-Host '---- child-neg ----'; Write-Host $rn.Out }
    Ck 'C 退出码 ≠ 0（停手）' ([bool]($rn.Exit -ne 0)) 'True'
    Ck 'C 正文含 STOP 且点明"目标已存在"' ([bool]($rn.Out -cmatch 'STOP 目标已存在')) 'True'
    Ck 'C 账本数据行数不变（仍为 2）' (Read-DataRows $ledgerTemp).Count 2
    Ck 'C 目标 sha256 不变' (Get-Sha256Hex $dstA) $shaBefore
    Ck 'C 目标 bytes 不变' (Get-Item -LiteralPath $dstA).Length $bytesBefore

    Write-Host '=== C2 负控：源不存在 ⇒ 停手、账本不加行 ==='
    $rm = Invoke-Promote @('-Source', (Join-Path $sb 'nope.txt'), '-Target', (Join-Path $dstDir 'c2.txt'), '-Ledger', $ledgerTemp)
    if ($ShowChildOutput) { Write-Host '---- child-neg2 ----'; Write-Host $rm.Out }
    Ck 'C2 退出码 ≠ 0' ([bool]($rm.Exit -ne 0)) 'True'
    Ck 'C2 账本数据行数不变（仍为 2）' (Read-DataRows $ledgerTemp).Count 2
    Ck 'C2 目标未生成' (Test-Path -LiteralPath (Join-Path $dstDir 'c2.txt')) 'False'

    Write-Host '=== D. 「能红」演示：拆掉"目标已存在⇒停手"守卫的孪生体 ⇒ C 的三条断言必须全部为假 ==='
    # 孪生体 = 把 `exit 3` 注释掉 ＋ 把 Copy 的"不覆盖"开关翻成覆盖。其余逐行相同（还原后逐字符校验）。
    $twinDir = Join-Path $sb 'twin'
    $null = New-Item -ItemType Directory -Force -Path $twinDir
    $twin = Join-Path $twinDir 'CR-F2-promote-twin-overwrite.ps1'
    $srcLines = [IO.File]::ReadAllLines($Promote)
    $twinLines = New-Object System.Collections.Generic.List[string]
    $nExit = 0
    $nFlag = 0
    foreach ($ln in $srcLines) {
        if ($ln -ceq '    exit 3' -and $nExit -eq 0) { $twinLines.Add('    # TWIN-DISABLED exit 3'); $nExit++; continue }
        if ($ln -cmatch '\[IO\.File\]::Copy\(\$srcFull, \$dstFull, \$false\)') { $twinLines.Add($ln.Replace(', $false)', ', $true)')); $nFlag++; continue }
        $twinLines.Add($ln)
    }
    Ck 'D 孪生体构造：停手行命中恰好 1 次' $nExit 1
    Ck 'D 孪生体构造：Copy 覆盖开关命中恰好 1 次' $nFlag 1
    $restored = @($twinLines | ForEach-Object { $_ -replace '^    # TWIN-DISABLED exit 3$', '    exit 3' } | ForEach-Object { $_.Replace(', $true)', ', $false)') })
    Ck 'D 孪生体 = 源（还原后逐行大小写敏感相同）' ([bool]((($restored -join "`n") -ceq ($srcLines -join "`n")))) 'True'
    [IO.File]::WriteAllLines($twin, $twinLines, (New-Object Text.UTF8Encoding($true)))
    $srcC = Join-Path $sb 'src-gamma.txt'
    $dstC = Join-Path $dstDir 'c.txt'
    [IO.File]::WriteAllText($srcC, 'gamma-NEW-payload', $encNoBom)
    [IO.File]::WriteAllText($dstC, 'gamma-OLD-payload', $encNoBom)   # 目标**预先存在**且内容不同
    $shaCOld = Get-Sha256Hex $dstC
    $rowsD = (Read-DataRows $ledgerTemp).Count
    $rd = Invoke-ChildScript $twin @('-Source', $srcC, '-Target', $dstC, '-Ledger', $ledgerTemp, '-Actor', 'CR-F2S-twin')
    if ($ShowChildOutput) { Write-Host '---- child-twin ----'; Write-Host $rd.Out }
    Ck 'D 孪生体退出码 = 0（守卫被拆 ⇒ 它照样"成功"，与 C 的"退出码≠0"相反）' $rd.Exit 0
    Ck 'D 孪生体把已存在的目标覆盖了（sha 与覆盖前不同，与 C 的"字节不变"相反）' ([bool]((Get-Sha256Hex $dstC) -cne $shaCOld)) 'True'
    Ck 'D 孪生体账本 +1 行（与 C 的"行数不变"相反）' (Read-DataRows $ledgerTemp).Count ($rowsD + 1)
    Write-Host '  ⇒ 结论：C 的三条断言在孪生体上全部为假 ⇒ 那三条是**能红**的判据，不是恒真断言。'
} finally {
    if (Test-Path -LiteralPath $sb) { Remove-Item -LiteralPath $sb -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host ("FINALLY  sandbox=" + $sb + "  removed=" + (-not (Test-Path -LiteralPath $sb)))
}
Write-Host ''
Write-Host ("SELF-ASSERT(promote+ledger) fails=" + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
