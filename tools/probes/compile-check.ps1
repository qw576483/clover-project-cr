<#
  cr/tools/probes/compile-check.ps1 -- clover-project-cr 的离线编译闸门（唯一入口）。

  它本身**不编译**：真正的编译单元是
      tools/probes/hosts/compilecheck/CrCompileCheck.csproj   （业务 + 引擎 Runtime/Editor 源码）
      tools/probes/hosts/enginecore/CloverEngineCoreHost.csproj（引擎 Runtime/Core，独立程序集）
  本脚本只是「先 restore 再 build + 把 error 数与 exit code 取出来」的薄封装，
  给 cr 与 cs16 一个**同形**的调用面（一条命令 ⇒ errors=<n> exit=<n>）。
  ⛔ 不要在这里另写一套编译实现 —— 编译口径只许有一处（那两个 csproj）。

  ---------------------------------------------------------------------------
  ① 判据命令（先 restore，再 build）：为什么 restore 必须在前
     `dotnet build` 默认带隐式还原，所以"第一次 build 必报 NETSDK1004"并不成立 ——
     但**一旦还原没做/没做全，被读到的就是编译失败**（2026-09-25 实测，删掉两个宿主的
     obj/project.assets.json 后跑到）：
         ...\Microsoft.NET.Sdk.targets(308,5): error NETSDK1004: 找不到资产文件
           "...\enginecore\obj\project.assets.json"。运行 NuGet 包还原以生成此文件。
         EXIT=1
     即：一条**环境问题**（没还原）伪装成**代码问题**（exit 1 + `error ...`）。
     本脚本把 restore 独立成一步：restore 自己失败 ⇒ exit 2（环境问题，不是编译失败），
     只有在 restore 成功之后才把 build 的失败算成编译失败。
     （`--no-restore` 是最容易踩出它的调用方式；要判代码，就别带它。）

  ② 判据只看 `0 个错误`；`-v q` 下 warning **仍会原样打出**
     `-v q`（quiet）只压 MSBuild 自己的噪声行，csc 的 warning 一条不少。
     ⇒ 闸门的通过条件 = **error 数 == 0**；warning 数**仅供参考**，不是判据。
       本基线 3 条 warning（CS0618 x2 + CS0105），**全部落在引擎源码**、
       业务与 Editor 源码 0 warning；刻意**不压制**它们 —— 压掉等于把「引擎用了过时 API」
       这类真实事实从证据里删掉。
     计数**不**解析中文本地化的「N 个错误」汇总行（控制台编码会让它变乱码），
     也**不**按控制台行数抓：dotnet 会把超过控制台宽度的长行**硬折行**（实测一条 warning
     的正文被拆到下一物理行 ⇒ 按行去重会把条数算错）。计数唯一来源 = MSBuild 自己的
     文件日志 `-flp:logfile=...;verbosity=minimal;encoding=UTF-8`（不折行），
     按 ASCII 标记 `error <CODE><数字>` / `warning <CODE><数字>` 逐行去重。
     文件日志没落地时会**明说**回退到控制台文本，而不是静默当作 0 error；
     若 exit!=0 而 error 行数为 0，也会把两者**一起**报出来（绝不把「说不清的失败」读成通过）。

  ③ 为什么不能只引 client/Library/ScriptAssemblies/*.dll
     那是 Unity **上一次**编译的产物，不是引擎源码的当前状态。2026-09-25 实测：
       引擎源 Runtime/Presentation/TextFit.cs            2026-09-25 01:35:57
       引擎源 Runtime/Presentation/RuntimePanelProvider.cs 2026-09-25 01:35:24
       而 client/Library/ScriptAssemblies/CloverEngine.Presentation.dll = 2026-09-23 18:43:29
     该 dll 的字节串检查里 `TextFit` / `RuntimePanelProvider` **都不存在**（False）。
     后果是实测出来的：用 skill 自带的 scripts/compile-check.ps1 跑本工程 ⇒
         FAIL CR  sources=46 refs=426 csc_exit=1
           Assets/Scripts/UI/PanelFactory.cs(62,24): error CS0246: 未能找到类型或命名空间名"RuntimePanelProvider"
           Assets/Scripts/UI/TextFit.cs(41,40):    error CS0234: 命名空间"CloverEngine"中不存在类型或命名空间名"TextFit"
     这 4 条**全部是旧 dll 造成的假红**（同一脚本自己还打了 7 条 STALE-REF），
     而业务代码本身没问题。⇒ 闸门必须让**引擎当前源码**参与编译；本工程从
     clover-client-unity-engine/Runtime/**、Editor/** 编源码，所以不存在这条假红。
     （注意：这不是"可以把红读成绿"的理由 —— 判据是「并入引擎源码后 0 error」。）

  ④ 为什么引擎源码必须按 asmdef 拆成独立工程（⛔ 不许合成一个编译单元）
     引擎 Runtime/Core、Network、Presentation、Resource、Data 是**5 个 asmdef**，彼此是程序集边界。
     Runtime/Core/Timer.cs:115 有 `internal class Timer : ITimer`（同命名空间 CloverEngine）。
     Unity 里 Network 程序集看不到它 ⇒ NetworkManager.cs / Quic/QuicConnection.cs 里裸写的
     `Timer` 绑到 `System.Threading.Timer`；一旦把 5 份源码并进同一个程序集，Core 那个 internal
     类型变成可见并**无条件遮蔽**它 ⇒ 实测**凭空多 9 个假错**：
       Network/NetworkManager.cs:  CS1729 Timer 不包含采用 4 个参数的构造函数 / CS1061 未包含 Change / Dispose
       Network/Quic/QuicConnection.cs: 同上
     所以 hosts/enginecore/ 单独把 Runtime/Core 编成 `CloverEngine.Core` 程序集并 ProjectReference。
     这是工程结构，不是压制：**没有**改引擎源码、**没有** NoWarn 掉这些错误。

  ---------------------------------------------------------------------------
  Usage:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\probes\compile-check.ps1
    powershell ... -File tools\probes\compile-check.ps1 -Quiet 1     # 只打一行汇总
    powershell ... -File tools\probes\compile-check.ps1 -MaxErrLines 80

  Exit codes: 0 = 编译通过（error 数 0）；1 = 编译报错；
              2 = 环境问题（找不到 dotnet / 找不到闸门工程 / restore 失败 / 一个源文件都没编到）。
  ⛔ 本脚本不往项目树里写任何东西：构建产物在宿主 obj\bin（.gitignore 已覆盖），
     完整输出日志写系统临时目录（build by-product 不占 .ai-tmp 文件预算）。
#>
param(
    [string]$Quiet = '',
    [string]$MaxErrLines = '40'
)

$ErrorActionPreference = 'Stop'

function Fail([string]$msg, [int]$code = 2) {
    Write-Output ('FAIL compile-check: ' + $msg)
    exit $code
}

# 项目根从脚本自身位置推导（本脚本在 <项目根>/tools/probes/）=> 任意 CWD 都能跑。
$projRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$hostProj = Join-Path $projRoot 'tools\probes\hosts\compilecheck\CrCompileCheck.csproj'
$tmp = Join-Path $env:TEMP 'cr-compilecheck'
if (-not (Test-Path -LiteralPath $tmp)) { New-Item -ItemType Directory -Path $tmp -Force | Out-Null }
$logPath = Join-Path $tmp 'compile-check.log'
$msbLog = Join-Path $tmp 'msbuild.log'

if (-not (Test-Path -LiteralPath $hostProj)) {
    Fail ('missing gate project -> ' + $hostProj + ' ; the gate lives in tools/probes/hosts/ and must exist first.', 2)
}
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd -eq $null) {
    Fail ('dotnet not found on PATH -- install the .NET SDK (the gate builds with the SDK''s MSBuild + the Unity editor''s managed DLLs).', 2)
}
$dotnet = $dotnetCmd.Source

# ---- 0) 源文件数自检：一个源文件都没编到 ⇒ 硬失败，绝不当作通过 ----
# （skill reference/fast-compile-loop.md 坑 1：MSBuild 展开成空 ⇒ 编了 0 个文件 ⇒ "没报错"被读成"没问题"。）
$listRaw = & $dotnet msbuild $hostProj -t:CloverListCompileSet -v:m -nologo 2>&1
$sources = @($listRaw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -match '\.cs$' })
$bizCount = @($sources | Where-Object { $_ -match 'clover-project-cr\\client\\Assets\\Scripts\\' }).Count
$edCount  = @($sources | Where-Object { $_ -match 'clover-project-cr\\client\\Assets\\Editor\\' }).Count
$engCount = @($sources | Where-Object { $_ -match 'clover-client-unity-engine\\' }).Count
if ($sources.Count -eq 0) {
    Fail ('the gate project lists 0 source files -- a gate that compiles nothing must not report success.', 2)
}

# ---- 1) restore（必须先做；否则首次 build 会报 NETSDK1004 = "没还原"被读成"编不过"）----
$restoreRaw = & $dotnet restore $hostProj 2>&1
$restoreCode = $LASTEXITCODE
if ($restoreCode -ne 0) {
    [IO.File]::WriteAllText($logPath, (($restoreRaw | Out-String)), (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ('      ' + (($restoreRaw | Select-Object -Last 5 | ForEach-Object { ([string]$_).Trim() }) -join "`n      "))
    Fail ('dotnet restore failed (exit ' + $restoreCode + ') -- this is an ENVIRONMENT problem, not a compile error. log=' + $logPath, 2)
}

# ---- 2) build：-t:Rebuild 保证每次都真编（-v q 的增量会把"没重编"读成"0 error"）----
# -v q 下 warning 仍会原样打出 => 通过条件只取 error 数 == 0。
# ⚠️ 计数**不能**从控制台输出里按行抓：dotnet 会把超过控制台宽度的长行**硬折行**
# （实测一条 warning 的正文被拆到下一物理行），按行去重会把条数算错。
# ⇒ 用 MSBuild 自己的文件日志（不折行、UTF-8）作为唯一计数来源，控制台输出只留作日志。
$buildRaw = & $dotnet build $hostProj -v q --nologo -t:Rebuild "-flp:logfile=$msbLog;verbosity=minimal;encoding=UTF-8" 2>&1
$code = $LASTEXITCODE
[IO.File]::WriteAllText($logPath, (($buildRaw | Out-String)), (New-Object System.Text.UTF8Encoding($false)))

if (Test-Path -LiteralPath $msbLog) {
    $lines = @(Get-Content -LiteralPath $msbLog | ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ -ne '' } | Sort-Object -Unique)
} else {
    # 文件日志没落地（不该发生）⇒ 退回控制台文本，并把这件事说出来，而不是静默当作 0 error。
    Write-Output ('      WARN  msbuild file log not found at ' + $msbLog + ' -- falling back to console text (line-wrapped).')
    $lines = @($buildRaw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne '' } | Sort-Object -Unique)
}
$errs = @($lines | Where-Object { $_ -match 'error [A-Z]{2}\d+' })
$warns = @($lines | Where-Object { $_ -match 'warning [A-Z]{2}\d+' })
$errCount = $errs.Count

if (($Quiet -eq '') -or ($errCount -ne 0) -or ($code -ne 0)) {
    Write-Output ('sources=' + $sources.Count + ' (business=' + $bizCount + ' editor=' + $edCount + ' engine=' + $engCount + ')')
}
Write-Output ('errors=' + $errCount + ' warnings=' + $warns.Count + ' exit=' + $code)
Write-Output ('log=' + $logPath)

if (($code -eq 0) -and ($errCount -eq 0)) {
    if ($Quiet -eq '') { Write-Output 'PASS  compile-check: 0 error' }
    exit 0
}

if ($errCount -eq 0) {
    Write-Output 'FAIL  build exited non-zero but printed no `error <CODE><digits>` line -- not a pass (see log).'
    exit 1
}

$max = 40
if ($MaxErrLines -ne '') { $max = [int]$MaxErrLines }
foreach ($e in ($errs | Select-Object -First $max)) { Write-Output ('      ' + $e) }
if ($errs.Count -gt $max) { Write-Output ('      ... and ' + ($errs.Count - $max) + ' more error line(s); full log = ' + $logPath) }
Write-Output ('===== compile-check: FAIL (' + $errCount + ' error line(s)) =====')
exit 1
