# ★ CR-F2S 2026-09-23 判据资产：验证 `cr-v2-cleanup-block-selftest.ps1` 的**沙箱清理确实由 finally 负责**。
#
# 要证的命题（不是"我看了一眼代码觉得对"）：
#   P1 用例**中途抛异常**时，沙箱【仍被清掉】；
#   P2 该断言**能失败** —— 用"去掉 finally 的孪生体"在同一次注入下必须**残留沙箱**（否则 P1 是恒真断言）；
#   P3 正常路径仍然 FAILS=0（加固没把正常路径弄坏）。
#
# ⚠️ 为什么必须有 P2：只有 P1 时，"沙箱不存在"可能来自**别的**原因（例如注入根本没生效、或沙箱压根没建）。
#    ⇒ 三个读数同时成立才算数：`退出码 != 0`（注入确实生效）＋ `removed=True`（finally 确实跑了）
#       ＋ 该目录确实不存在（磁盘上核对，⛔ 不信 stdout）。
#    ⇒ 孪生体 = 把 `} finally {` 换成 `} if ($false) {`（清理段从此永不执行），其余逐字符相同。
#
# ⚠️ 孪生体只建在 `.ai-tmp/test/` 下、跑完即删（⛔ 不落进 tools/probes/，那会变成一份"坏样本"常驻保留区）。
param(
    [string]$Target = 'C:\Work\Server\f-v2\clover-project-cr\tools\probes\cr-v2-cleanup-block-selftest.ps1',
    [string]$ProjectRoot = '',
    # 诊断用：把子进程的原始输出整段打出来（失败时定位用；默认关，避免正常读数被淹没）
    [switch]$ShowChildOutput
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path (Split-Path (Split-Path $Target -Parent) -Parent) -Parent
}
$fails = 0
function Ck($name, $got, $want) {
    $ok = ("$got" -eq "$want")
    if (-not $ok) { $script:fails++ }
    Write-Host ("  {0}  {1}  got={2} want={3}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $got, $want))
}
function PathState([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return 'NO-PATH-PARSED' }
    return ([bool](Test-Path -LiteralPath $p))
}

function Invoke-Child([string]$script, [string[]]$extra) {
    $PSExe = (Get-Command powershell.exe).Source
    $argv = New-Object System.Collections.Generic.List[string]
    foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script)) { $argv.Add($a) }
    foreach ($a in $extra) { $argv.Add($a) }
    # ⚠️ 陷阱（本片实测踩到）：`$ErrorActionPreference='Stop'` ＋ 对**原生程序**做 `2>&1` ⇒ stderr 里任何一行
    #   都被包成 NativeCommandError（ErrorRecord）⇒ 在 Stop 下**直接终止宿主**（子脚本一抛异常，宿主先炸）。
    #   ⇒ 抓子进程输出期间必须临时降为 Continue，取完再恢复。
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $out = (& $PSExe @argv 2>&1 | Out-String)
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prevEap
    $m = [regex]::Match($out, 'sandbox=(?<p>[^\r\n]+)')
    [pscustomobject]@{
        Exit    = $code
        Out     = $out
        Sandbox = $(if ($m.Success) { $m.Groups['p'].Value.Trim() } else { '' })
    }
}

# ── 0. 孪生体（broken twin）：把 finally 里那两行动作【注释掉】⇒ 异常路径不再清沙箱 ──
#   ⚠️ 构造方式走过一次弯路（记在明处）：第一版想把 `} finally {` 换成 `} if ($false) {`，
#      结果 PowerShell **语法非法**（`try` 必须跟 catch/finally）⇒ 孪生体自己解析失败、退出码也是 != 0，
#      差点让 P2 "看起来通过了"（假绿）。⇒ 现改为**逐行注释**：行级操作，且构造后做"去注释即还原"的等价校验。
$twDir = Join-Path $ProjectRoot '.ai-tmp\test\crv2-finally-broken'
if (Test-Path -LiteralPath $twDir) { Remove-Item -LiteralPath $twDir -Recurse -Force }
$null = New-Item -ItemType Directory -Force -Path $twDir
$twin = Join-Path $twDir 'cr-v2-cleanup-block-selftest-twin-disabled.ps1'
$srcLines = [IO.File]::ReadAllLines($Target)      # 自动剥 BOM
$twinLines = New-Object System.Collections.Generic.List[string]
$neutered = 0
foreach ($ln in $srcLines) {
    if ($ln -cmatch '^\s*Remove-Item -LiteralPath \$sb -Recurse -Force -ErrorAction SilentlyContinue\s*$' -or
        $ln -cmatch '^\s*Write-Host \("FINALLY') {
        $twinLines.Add('# TWIN-DISABLED ' + $ln); $neutered++; continue
    }
    $twinLines.Add($ln)
}
Ck '孪生体构造：被注释掉的动作恰好 2 行（否则该判据本身失效）' $neutered 2
$restored = @($twinLines | ForEach-Object { $_ -replace '^# TWIN-DISABLED ', '' })
Ck '孪生体 = 源（去掉注释标记后逐行大小写敏感相同）' ([bool]((($restored -join "`n") -ceq ($srcLines -join "`n")))) 'True'
Ck '孪生体仍含 finally 关键字（= 只废掉动作，不是废掉守卫语法）' ([bool](($twinLines -join "`n") -cmatch '\} finally \{')) 'True'
[IO.File]::WriteAllLines($twin, $twinLines, (New-Object Text.UTF8Encoding($true)))

$sbLeft = @()
try {
    # ── P2 先跑（负控）：同一次注入下，去掉 finally 的孪生体【必须残留沙箱】 ──
    Write-Host '=== P2 负控：孪生体（无 finally）＋ -InjectThrow mid ⇒ 沙箱必须残留（证明 P1 能红）==='
    $b = Invoke-Child $twin @('-InjectThrow', 'mid')
    if ($ShowChildOutput) { Write-Host '---- child(孪生体) raw ----'; Write-Host $b.Out }
    Ck 'P2 注入确实生效（孪生体退出码 != 0）' ([bool]($b.Exit -ne 0)) 'True'
    Ck 'P2 沙箱路径已被解析出（非空 ⇒ 否则路径为空时 Test-Path 会误报 False）' ([bool]($b.Sandbox.Length -gt 0)) 'True'
    Ck 'P2 沙箱【仍在】（False ⇒ P1 是恒真断言，本判据作废）' (PathState $b.Sandbox) 'True'
    if ($b.Sandbox.Length -gt 0) { $sbLeft += $b.Sandbox }

    # ── P1 正控：真件在【同一注入】下，沙箱必须被 finally 清掉 ──
    Write-Host '=== P1 正控：真件 ＋ -InjectThrow mid ⇒ finally 必须清掉沙箱 ==='
    $a = Invoke-Child $Target @('-InjectThrow', 'mid')
    Ck 'P1 注入确实生效（真件退出码 != 0，⛔ 不吞错）' ([bool]($a.Exit -ne 0)) 'True'
    Ck 'P1 stdout 里 finally 自报 removed=True' ([bool]($a.Out -cmatch 'FINALLY\s+sandbox=.*removed=True')) 'True'
    Ck 'P1 磁盘核对：沙箱目录已不存在' (PathState $a.Sandbox) 'False'

    # ── P3 正常路径没被加固弄坏 ──
    Write-Host '=== P3 正常路径：真件（无注入）⇒ FAILS=0 ==='
    $c = Invoke-Child $Target @()
    Ck 'P3 退出码 = 0' $c.Exit 0
    Ck 'P3 正文出现 FAILS=0' ([bool]($c.Out -cmatch 'FAILS=0')) 'True'
    Ck 'P3 磁盘核对：沙箱目录已不存在' (PathState $c.Sandbox) 'False'
} finally {
    foreach ($s in $sbLeft) {
        if ($s.Length -gt 0 -and (Test-Path -LiteralPath $s)) { Remove-Item -LiteralPath $s -Recurse -Force -ErrorAction SilentlyContinue }
    }
    if (Test-Path -LiteralPath $twDir) { Remove-Item -LiteralPath $twDir -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host ("FINALLY-CHECK 清理：孪生体目录仍在? " + (Test-Path -LiteralPath $twDir))
}
Write-Host ''
Write-Host ("SELF-ASSERT(cleanup-finally) fails=" + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
