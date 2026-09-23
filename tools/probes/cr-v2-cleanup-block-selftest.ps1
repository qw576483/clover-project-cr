# ⚠️ 无 BOM 会按 GBK 读 => 本文件含中文，必须有 BOM（与本片口径一致；本文件由 write_to_file 落，已含 BOM）
# ★ CR-V2 自检宿主：把 runner 里【新加的清理段】原样抽出来执行（⛔ 不重写逻辑），带正/负控。
#   动机：那段代码**从没跑过**（它将在下一条链才执行）⇒ "没跑过的判据 = 没有判据"（恒真断言那族的镜像）。
#   抽出方式：取 runner 里**最后一次**出现的 `foreach ($d in @("$proj\Assets\Temp\CR-V2"` 块，逐字执行。
param(
    [string]$Runner = 'C:\Work\Server\f-v2\clover-project-cr\tools\probes\cr-v2-run.ps1',
    # ★ 2026-09-23 CR-F2S（team-lead 裁定 (乙)）：沙箱从 `$env:TEMP` 移入 <项目根>\.ai-tmp\test\。
    #   项目根由 $Runner（<root>\tools\probes\cr-v2-run.ps1）反推 ⇒ 不新增硬编码路径、项目搬家仍成立。
    [string]$ProjectRoot = '',
    # ★ 2026-09-23 CR-F2S（裁定 (甲) 的负控开关）：'mid' = 在用例中途抛异常 ⇒ 用来证明"清沙箱的是 finally"，
    #   而不是"末尾那行恰好在正常路径上"（旧版：异常路径必然残留）。
    [ValidateSet('', 'mid')][string]$InjectThrow = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path (Split-Path (Split-Path $Runner -Parent) -Parent) -Parent
}
$sb = Join-Path $ProjectRoot ('.ai-tmp\test\crv2-selftest-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$null = New-Item -ItemType Directory -Force -Path $sb
# ★ 沙箱路径先打出来：外部宿主（负控）要靠它断言"异常后沙箱是否仍在"，⛔ 不能只靠返回码
Write-Host ("sandbox=" + $sb)
$fails = 0
$results = New-Object System.Collections.ArrayList

function Ck($name, $actual, $expect) {
    $ok = ("$actual" -eq "$expect")
    if (-not $ok) { $script:fails++ }
    [void]$script:results.Add(("{0} {1} actual={2} expect={3}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $actual, $expect)))
}

# ⚠️ 2026-09-23 CR-F2S 加固（team-lead 裁定 (甲)＋(乙)，两处改动都在本段之外可核对）：
#   (甲) 下面【第 1~5 节全部用例】包进 try/finally ⇒ 任何中途抛异常（$ErrorActionPreference='Stop' 下
#        任何一个 cmdlet 失败、任何断言宿主自身出错）也必定清掉沙箱。旧版只在正常路径末尾 Remove-Item
#        ⇒ **异常路径必然残留**（沙箱当时还落在系统 TEMP，残留更隐蔽）。
#   (乙) 沙箱 = <项目根>\.ai-tmp\test\crv2-selftest-<guid>（见上方 param 段）⇒ 不再污染 $env:TEMP。
#   ⚠️ 用例段按**原字节保留、不重排缩进** ⇒ 可用 `git diff` / 逐行比对确认"只加了 try/finally 与沙箱路径"；
#      块由 `{}` 界定，缩进不影响 PowerShell 语义。
try {
# ── 1. 逐字抽出代码块（不重写） ──
$lines = [System.IO.File]::ReadAllLines($Runner, (New-Object System.Text.UTF8Encoding($false)))
$hit = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'foreach \(\$d in @\("\$proj\\Assets\\Temp\\CR-V2"') { $hit += $i }
}
# ⚠️ 自报（第一版我在这里写错了 3 条断言，已改）：
#   ① 我原来断言"该类清理段出现次数 >= 4"——**凭假设写的期望值**，实测 3（历史 2 + 新增 1）⇒ 断言错、不是产品错；
#   ② 两条静态断言我**极性写反**（用 `-notmatch` 却期望 False）⇒ 假红。
#   ⇒ 口径：**断言也要先量再写**，期望值不是"我觉得应该是几"。下面的"抽到的块确实是**我新加的那一段**"
#      用**标记串**判（`AFTER last capture`），而不是靠计数。
Ck 'runner 里该类清理段出现次数 >= 3' ([bool]($hit.Count -ge 3)) 'True'
$start = $hit[$hit.Count - 1]
$end = $start
while ($end -lt $lines.Count -and $lines[$end] -notmatch '^\s{4}\}\s*$') { $end++ }
$block = ($lines[$start..$end] -join "`n")
Write-Host "--- EXTRACTED BLOCK (verbatim, $($end - $start + 1) lines) ---"
Write-Host $block
Write-Host "--- END BLOCK ---"

# ★ 关键：抽到的必须是**我新加的那段**（标记串判定），否则"抽错块 ⇒ 测了别的代码"
Ck '抽到的块 = 新加段（含标记 AFTER last capture）' ([bool]($block -match 'AFTER last capture')) 'True'

# 静态边界：块内只允许出现 $proj 相对拼装，不允许出现绝对路径字面量（防"抽错块/块里藏绝对路径"）
$abs = @([regex]::Matches($block, '[A-Za-z]:\\') | ForEach-Object { $_.Value })
Ck '块内绝对盘符路径个数 = 0' $abs.Count '0'
# ⚠️⚠️ 这里踩过一个**真陷阱**（已定案，见 `.ai-tmp/hosts/cr-v2-match-vs-matches-probe.ps1`）：
#   `-match` 默认**大小写不敏感**，而 `[regex]::Matches` 默认**大小写敏感**
#   ⇒ 同一模式两种写法会给出**不同答案**；本例实质是 **`Remove-Item` 里含子串 `move-Item`**
#   （`Re|move-Item`）⇒ `-match 'Move-Item'` = True 而 `Matches(...)` = 0。
#   ⇒ 与"判据比对象宽"同族（`CR-V1` 的 tan 判据吃绿草）。修法 = **词边界 + IgnoreCase**，并按 token 逐项报数。
$writeTokens = @('Set-Content', 'New-Item', 'Move-Item', 'Copy-Item', 'Out-File', 'Remove-Item')
$counts = [ordered]@{}
foreach ($t in $writeTokens) {
    $pat = '\b' + [regex]::Escape($t) + '\b'
    $counts[$t] = [regex]::Matches($block, $pat, 'IgnoreCase').Count
}
Write-Host ("  per-token counts: " + (($counts.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join ' '))
foreach ($t in @('Set-Content', 'New-Item', 'Move-Item', 'Copy-Item', 'Out-File')) {
    Ck ("块内无其它写盘式子: " + $t) $counts[$t] '0'
}
# 反向断言：**预期的那个**写盘式子必须在、且恰好 2 次（否则"清理段被换掉/删空"会静默通过）
Ck '块内预期的 Remove-Item 恰好 2 次（反向断言）' $counts['Remove-Item'] '2'

# ── 2. 沙箱：正控（空目录 + 两枚 .meta ⇒ 必须全删） ──
$A = Join-Path $sb 'A'
$proj = $A
$null = New-Item -ItemType Directory -Force -Path (Join-Path $A 'Assets\Temp\CR-V2')
[System.IO.File]::WriteAllBytes((Join-Path $A 'Assets\Temp\CR-V2.meta'), (New-Object byte[] 172))
[System.IO.File]::WriteAllBytes((Join-Path $A 'Assets\Temp.meta'), (New-Object byte[] 172))
function Log($m) { Write-Host ("  [Log] " + $m) }
$null = Invoke-Expression $block
foreach ($suffix in @('Assets\Temp\CR-V2', 'Assets\Temp', 'Assets\Temp\CR-V2.meta', 'Assets\Temp.meta')) {
    Ck ("A(正控/空目录) 已删: " + $suffix) (Test-Path (Join-Path $A $suffix)) 'False'
}

# ── 3. 沙箱：负控（目录里有 1 个真文件 ⇒ ⛔ 一个都不许删） ──
$B = Join-Path $sb 'B'
$proj = $B
$null = New-Item -ItemType Directory -Force -Path (Join-Path $B 'Assets\Temp\CR-V2')
[System.IO.File]::WriteAllBytes((Join-Path $B 'Assets\Temp\CR-V2\pause.png'), (New-Object byte[] 64))
[System.IO.File]::WriteAllBytes((Join-Path $B 'Assets\Temp\CR-V2.meta'), (New-Object byte[] 172))
[System.IO.File]::WriteAllBytes((Join-Path $B 'Assets\Temp.meta'), (New-Object byte[] 172))
$null = Invoke-Expression $block
foreach ($suffix in @('Assets\Temp\CR-V2', 'Assets\Temp', 'Assets\Temp\CR-V2.meta', 'Assets\Temp.meta', 'Assets\Temp\CR-V2\pause.png')) {
    Ck ("B(负控/非空) 必须保留: " + $suffix) (Test-Path (Join-Path $B $suffix)) 'True'
}

# ── 3b. ★ 中途抛异常【注入点】（CR-F2S 2026-09-23 加）：此刻沙箱里已有 A/B 两个真实树 ──
#   动机：旧版"清沙箱"是**正常路径末尾的一行**⇒ 只要用例中途炸掉（断言宿主自己出错/写盘失败），
#         沙箱就留在盘上，而屏幕上看不出（⛔ 与"读失败被当成干净"同族）。
#   用法：powershell -NoProfile -ExecutionPolicy Bypass -File <本文件> -InjectThrow mid
#   预期：退出码非 0（异常照常上报，⛔ 不吞错）＋ stdout 末行 `FINALLY  ... removed=True` ＋ 沙箱路径已不存在。
if ($InjectThrow -eq 'mid') { throw 'INJECTED mid-run failure (BY CR-F2S SELFTEST): proves the finally block cleans the sandbox' }

# ── 4. 越界哨兵：沙箱外的一个金丝雀必须仍在；且 A 沙箱的兄弟目录没被动 ──
$canary = Join-Path $sb 'canary.txt'
[System.IO.File]::WriteAllText($canary, 'canary')
Ck '越界哨兵（沙箱外金丝雀文件）仍在' (Test-Path $canary) 'True'
Ck 'A 沙箱的 Assets 上级目录仍在' (Test-Path (Join-Path $A 'Assets')) 'True'

# ── 4b. ★ 破坏性分支的三测（采纳 `CR-F1` 的实测：`throw` 是终止错误、测不到"吞错"；且 `-ErrorAction` 对【注入的函数】不生效）
#    ⇒ 故本片改用 **.NET 枚举**（真异常）⇒ 下面用【真失败】＋【注入】两类各测一次 ──
# T1 真·失败：把 `Assets\Temp` 做成【文件】⇒ `[IO.Directory]::GetFileSystemEntries` 抛 DirectoryNotFound ⇒ NOT-JUDGED、⛔ 不许删
$F = Join-Path $sb 'F'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $F 'Assets')
[System.IO.File]::WriteAllText((Join-Path $F 'Assets\Temp'), 'not a directory')
$proj = $F
$null = Invoke-Expression $block
Ck 'T1 真失败（Assets\Temp 是文件）：该文件仍在 ⛔ 未被删' (Test-Path -LiteralPath (Join-Path $F 'Assets\Temp')) 'True'
# T2 注入 Get-ChildItem：新实现**结构上不使用该 cmdlet** ⇒ 空目录【仍应被删】= 正控（证明它不是"永不删"的守卫）
$G = Join-Path $sb 'G'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $G 'Assets\Temp\CR-V2')
& {
    param($proj)
    function Get-ChildItem { throw 'injected by CR-V2 selftest' }
    $null = Invoke-Expression $block
} $G
Ck 'T2 注入 Get-ChildItem：空目录仍被删（结构免疫 ＋ 非"永不删"）' (Test-Path -LiteralPath (Join-Path $G 'Assets\Temp\CR-V2')) 'False'

# ── 4c. ★ 成对对照：`known-bad`（旧聚合形态）在【非终止错误】注入下**必须真的坏**；我的实现在同一注入下**不坏** ──
#   依据（`CR-F1` 的精确化）：注入必须匹配【终止性 ＋ 产生上下文】两维 —— 本用例 = 影子化命令 ＋ `Write-Error`（非终止）
$H = Join-Path $sb 'H'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $H 'Assets\Temp\CR-V2')
[System.IO.File]::WriteAllBytes((Join-Path $H 'Assets\Temp\CR-V2\pause.png'), (New-Object byte[] 64))
# ★ 2026-09-23 CR-F2S：原名 `$I` 与本文件第 5 节的循环变量 `$i` **只差大小写**，而 PowerShell 变量名
#   大小写不敏感 ⇒ 第 5 节的 `for ($i = 0; ...)` 会**静默覆盖** `$I`。当前读写次序恰好安全（$I 的最后一次
#   使用在第 163 行，$i 第一次出现在第 169 行）⇒ 属"潜在形状"而非现行缺陷，但正是本片扫描器要抓的那一类。
#   改名 `$I` -> `$sbI`（沙箱 B 系列同族命名），连带其 scriptblock 形参名一起改，消除碰撞。
$sbI = Join-Path $sb 'I'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $sbI 'Assets\Temp\CR-V2')
[System.IO.File]::WriteAllBytes((Join-Path $sbI 'Assets\Temp\CR-V2\pause.png'), (New-Object byte[] 64))
& {
    param($H, $sbI, $block)
    # ⚠️ 影子函数必须能接公共参数（`-ErrorAction`），否则调用方会因"参数不存在"而【中止脚本】——
    #    这正是 `CR-F1` 指出的"`-ErrorAction` 对注入的函数不生效"；`[CmdletBinding()]` 让它合法接受该参数。
    function Get-ChildItem { [CmdletBinding()] param([Parameter(ValueFromRemainingArguments = $true)] $Rest) Write-Error 'injected non-terminating error (BY CR-V2 SELFTEST)' }
    # (k) known-bad 复刻：旧聚合形态（⛔ 坏样本，仅用于证明"这条守卫是必要的"、不是空守卫）
    foreach ($d in @("$H\Assets\Temp\CR-V2", "$H\Assets\Temp")) {
        if ((Test-Path $d) -and (Get-ChildItem $d -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
            Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    # (m) 我的实现：同一注入作用域内执行真块
    $proj = $sbI
    $null = Invoke-Expression $block
} $H $sbI $block
Ck '(k) known-bad 旧聚合形态：非终止注入下【确实删】(有内容也删) ⇒ 证明守卫必要' (Test-Path -LiteralPath (Join-Path $H 'Assets\Temp\CR-V2\pause.png')) 'False'
Ck '(m) 我的实现：同一注入下【不删】(有内容)' (Test-Path -LiteralPath (Join-Path $sbI 'Assets\Temp\CR-V2\pause.png')) 'True'

# ── 5. ★ 抽【CLEANPOST 三态块】原地执行（口径："parsecheck 通过 ⛔ 推不出 运行期不炸" ⇒ 必须真跑） ──
function Beat($a, $b, $c) { $script:lastBeat = $a + '|' + $b + '|' + $c }
$rl = [System.IO.File]::ReadAllLines($Runner, (New-Object System.Text.UTF8Encoding($false)))
$s2 = -1
for ($i = 0; $i -lt $rl.Count; $i++) { if ($rl[$i] -match '^\$cpDir = \$false') { $s2 = $i } }
if ($s2 -lt 0) { Ck 'CLEANPOST 块可被抽出' 'NOT-FOUND' 'FOUND' } else {
    $e2 = $s2
    while ($e2 -lt $rl.Count -and $rl[$e2] -notmatch "Beat 'cleanpost'") { $e2++ }
    $cpBlock = ($rl[$s2..$e2] -join "`n")
    Write-Host ("--- EXTRACTED CLEANPOST BLOCK (verbatim, " + ($e2 - $s2 + 1) + " lines) ---")
    Ck 'CLEANPOST 块三态齐全（NOT-JUDGED + ErrorAction Stop + FAIL）' ([bool](($cpBlock -match 'NOT-JUDGED') -and ($cpBlock -match 'ErrorAction Stop') -and ($cpBlock -match 'FAIL\('))) 'True'
    $sbCp = New-Object System.Collections.ArrayList
    foreach ($case in @('absent', 'leftover', 'readerror')) {
        $root = Join-Path $sb ('cp_' + $case)
        $null = New-Item -ItemType Directory -Force -Path $root
        if ($case -eq 'leftover') {
            $null = New-Item -ItemType Directory -Force -Path (Join-Path $root 'Assets\Temp\CR-V2')
            [System.IO.File]::WriteAllBytes((Join-Path $root 'Assets\Temp\CR-V2\pause.png'), (New-Object byte[] 8))
        }
        if ($case -eq 'readerror') { $root = $root + '|bad' }
        $proj = $root
        $script:lastBeat = ''
        $cp = ''
        $null = Invoke-Expression $cpBlock
        [void]$sbCp.Add(($case + ' => ' + $cp))
    }
    Write-Host ("  CLEANPOST 三态读数: " + ($sbCp -join ' | '))
    Ck 'CLEANPOST 态1（不存在）判 OK' ([bool]((($sbCp[0] -split '=> ')[1]) -eq 'OK')) 'True'
    Ck 'CLEANPOST 态2（有遗留）判 FAIL(' ([bool]((($sbCp[1] -split '=> ')[1]) -like 'FAIL(*')) 'True'
    Ck 'CLEANPOST 态3（读不到）判 NOT-JUDGED( ⛔ 不是 OK' ([bool]((($sbCp[2] -split '=> ')[1]) -like 'NOT-JUDGED(*')) 'True'
}

} finally {
    # ★ 唯一清理点（CR-F2S 2026-09-23）：正常走完 / 中途抛异常 两条路径都经过 here。
    #   打印 removed= 是**给外部宿主断言用的读数**（⛔ 不靠返回码"看起来对"）。
    Remove-Item -LiteralPath $sb -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("FINALLY  sandbox=" + $sb + "  removed=" + (-not (Test-Path -LiteralPath $sb)))
}

Write-Host ""
Write-Host ("sandbox removed: " + (-not (Test-Path -LiteralPath $sb)))
Write-Host "==== RESULT ===="
$results | ForEach-Object { Write-Host $_ }
Write-Host ("FAILS=" + $fails)
if ($fails -gt 0) { Write-Host 'SELF-ASSERT fails>0 => 本判据不成立' ; exit 1 }
Write-Host 'SELF-ASSERT ALL PASS'
exit 0
