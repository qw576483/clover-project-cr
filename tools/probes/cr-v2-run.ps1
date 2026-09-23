# CR-V2 chain runner (evidence collection only - no product code is touched).
# Shape reused verbatim from .ai-tmp/test/CR-F1-run.ps1 (the proven one, 2026-09-23 09:54):
#   - every unity call goes through RunUnity (the "unity " prefix lives INSIDE it);
#   - driver = tools/probes/CR-U5-probe.cs (reused, NOT rewritten) fed by an ascii arg file;
#   - capture shape = double-dash long options + a project-RELATIVE save_path, then copy out
#     to .ai-tmp/screenshots/ and delete the Assets copy (+ .meta, + the now-empty dirs).
#
# v2 (2026-09-23 11:2x, team-lead 的口径):
#   * heartbeat 三列 `ISO <TAB> step <TAB> status(start|ok|fail|blocked) [<TAB> extra]`（team-lead 2026-09-23 要求）；
#   * 装配闸门三件（team-lead ≥11:2x 广播）：① 留存副本 `tools/probes/assemblies/<sha16>.dll` 存在
#     ② 链前/链后 CR.dll hash **相同** ③ 当前 sha16 **∈** `tools/probes/assemblies/index.tsv`。
#     任一不满足 ⇒ 打 `ASSEMBLY WARN` 并把三项读数一起写进链日志（⛔ 不硬写 unchanged）。
#     ⚠️ 本 runner **不做复制留存** —— 留存由"代码冻结那一刻"做一次（是 V1/V2 runner 跑之前的事）。
param([string]$Chain = 'hud')
$ErrorActionPreference = 'Continue'
$root  = 'C:\Work\Server\f-v2\clover-project-cr'
$proj  = "$root\client"
$tmp   = "$root\.ai-tmp\test"
$probe = "$root\tools\probes\CR-U5-probe.cs"
$argf  = "$tmp\U5-arg.txt"
$log   = "$tmp\CR-V2-$Chain.out.txt"
$hb    = "$tmp\heartbeat-CR-V2.tsv"
$play  = "$tmp\play-log.tsv"
$asmDir = "$root\tools\probes\assemblies"
$asmIdx = "$asmDir\index.tsv"
$u8    = New-Object System.Text.UTF8Encoding($false)

function App([string]$path, [string]$text) { [System.IO.File]::AppendAllText($path, $text, $u8) }

# ★ CR-V2 追加（采纳 team-lead 2026-09-23 ③ 的判别式：【判据要放在会复发的那一步】）：
#   「末尾 LF」是【追加路径的前置条件】，⛔ 不是文件的**一次性属性** —— 任何"补一次 LF"都会在下一次追加时失效
#   （实证：`heartbeat-CR-U5R.tsv` 14:12 被追加后末尾又没了 LF）。⇒ 本 runner 在**每次追加前**断言并修复。
#   背景（本片自己的实测缺陷，见 L87-91）：我的两条 play-log 记录曾漏掉换行 ⇒ 被挤进同一条物理行（959 字符），
#   而共享 play-log 是**按行**解析的 ⇒ 别人的"窗口 FREE?"检查会看到畸形记录；我因此还误判过"END 没落地"。
#   ⚠️ **失败模式声明**（团队口径）：最坏情况 = 在文件末尾**多一个空行**（长度 +1）；
#      ⛔ 不可能删改任何既有字节；读不到/打不开 ⇒ 记 NOT-JUDGED **但继续**（⛔ 不阻断链，它只是防粘行的尽力而为）。
#   ⚠️ **边界**：对 **UTF-16 文件**（末字节常为 0x00）会**误判** ⇒ 只用于本 runner 写的那两个 **UTF-8 无 BOM** 账本。
#   ⚠️ **纪律留痕**（team-lead 2026-09-23 ② 要求）：本条改动**在"同批"纪律之外**，理由 =
#      **它改的是【共享账本 `play-log.tsv`】的写入路径、而我下一条链随时会写它 ⇒ 越早生效越好**；
#      裁定：**认可"现在就改"、不回退**。⇒ 后续凡改共享写入路径，都照此"越早越好 ＋ 注释留痕"。
function EnsureLF([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return 'n/a' }
    try {
        $len = (Get-Item -LiteralPath $path -ErrorAction Stop).Length
        if ($len -eq 0) { return 'empty' }
        $last = -1
        $fs = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try { [void]$fs.Seek(-1, [System.IO.SeekOrigin]::End); $last = $fs.ReadByte() } finally { $fs.Close() }
        if ($last -eq 0x0A) { return 'OK' }
        [System.IO.File]::AppendAllText($path, "`n", $u8)
        return 'REPAIRED'
    } catch {
        return ('NOT-JUDGED(' + $_.Exception.Message + ')')
    }
}

function Log([string]$m) { App $log ("`n" + $m + "`n") }
function Beat([string]$step, [string]$status, [string]$extra = '') {
    # 心跳同样"追加前断言"（我自己是唯一写者，但外部截断/手工追加都会把前提弄没）
    $lh = EnsureLF $hb
    if ($lh -ne 'OK' -and $lh -ne 'n/a') { Log ("  LFGUARD(heartbeat): " + $lh) }
    $line = "{0}`t{1}`t{2}" -f (Get-Date -Format o), $step, $status
    if ($extra -ne '') { $line = $line + "`t" + $extra }
    App $hb ($line + "`n")
}
function RunUnity([string]$ua, [int]$t) {
    $cmd = 'unity ' + $ua
    $job = Start-Job -ScriptBlock {
        param($c, $p)
        Set-Location $p
        $ErrorActionPreference = 'Continue'
        (Invoke-Expression $c 2>&1 | Out-String)
    } -ArgumentList $cmd, $proj
    if (Wait-Job $job -Timeout $t) { $out = Receive-Job $job } else { Stop-Job $job; $out = "TIMEOUT-$t" }
    Remove-Job $job -Force
    return $out
}
function P([string]$a) {
    Set-Content -Path $argf -Value $a -Encoding ascii
    return (RunUnity ('command eval_file --file "' + $probe + '"') 240)
}

$dll = "$proj\Library\ScriptAssemblies\CR.dll"
function DllSha() { if (Test-Path $dll) { return (Get-FileHash $dll -Algorithm SHA256).Hash.Substring(0,16) } else { return 'NO-DLL' } }

Set-Content -Path $log -Value '' -Encoding utf8
$runStart = Get-Date
$shaPre = DllSha
$warn = @()
Log ("=== CR-V2 chain=$Chain start " + (Get-Date -Format o) + " ===")
Log ("ASSEMBLY pre-run : CR.dll sha256_16=" + $shaPre + " mtime=" + (Get-Item $dll -ErrorAction SilentlyContinue).LastWriteTime.ToString('o'))

# ---- 装配闸门三件（⛔ 只校验、不复制）----
if (-not (Test-Path $asmIdx)) {
    $warn += "index.tsv MISSING ($asmIdx)"
    Log ("ASM gate: index.tsv MISSING -> " + $asmIdx)
} else {
    $idxText = Get-Content $asmIdx -Encoding UTF8
    $inIdx = ($idxText | Select-String -SimpleMatch $shaPre | Measure-Object).Count
    Log ("ASM gate: sha16 in index.tsv = " + $inIdx + " hit(s)")
    if ($inIdx -eq 0) { $warn += "sha16 NOT in index.tsv: $shaPre" }
}
$keep = "$asmDir\$shaPre.dll"
$keepOk = Test-Path $keep
Log ("ASM gate: kept copy exists = " + $keepOk + " -> " + $keep)
if (-not $keepOk) { $warn += "kept copy MISSING: $keep" }
$src = Get-ChildItem "$proj\Assets\Scripts" -Recurse -File -Filter *.cs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Log ("newest source    : " + $src.LastWriteTime.ToString('o') + " " + $src.Name)
Beat 'chain-start' 'start' ("chain=" + $Chain + " sha16=" + $shaPre)

# ⚠️ 必须带**尾随换行** `+"`n"` —— 实测缺陷（CR-V2 11:58）：原来这两条没带，
#   于是本 runner 写的所有 play-log 记录**被挤在同一个物理行**里（959 字符、含 8 个 `CR-V2`），
#   共享日志是**按行**解析的 ⇒ 别人的"窗口 FREE?"检查会看到一条畸形记录；
#   更糟的是我因此**误判**"END 没落地"（其实 END 在，只是看不见），还追加了一条错误记录（现已作废）。
#   `Log`/`Beat` 两条本来就是带换行的（`App $log ("`n" + $m + "`n")` / `App $hb ($line + "`n")`），只有 play-log 这两条漏了。
$lfG1 = EnsureLF $play
Log ("  LFGUARD(start-append): play-log trailing-LF = " + $lfG1)
App $play ((Get-Date -Format o) + "`tCR-V2`t" + $Chain + "`tSTART - visual-class: the three HUD items (hand-card frame+art fit, top-right timer plate, top-centre crown badge) are judged on RENDERED PIXELS at the same camera as the original baseline; offline can only assert source constants, not what is on screen. One chain: stop/play -> login -> AI battle -> one 1080x1920 screen capture. sha16=" + $shaPre + "`n")

# ---- 0. enter Play ----
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Start-Sleep -Seconds 4
Log ("editor_play: " + (RunUnity 'command editor_play' 300))
Start-Sleep -Seconds 35
Log ("state(play): " + (P 'state'))
Beat 'play-entered' 'ok'

# ---- 1. login -> main menu -> AI battle ----
Log ("loginform: " + (P 'loginform'))
Log ("click LoginButton: " + (P 'click:LoginButton'))
Start-Sleep -Seconds 8
Log ("state(after login): " + (P 'state'))
Log ("click AiBattleButton: " + (P 'click:AiBattleButton'))
Start-Sleep -Seconds 25
$st = P 'state'
Log ("state(battle): " + $st)
Beat 'battle-entered' 'ok'

# ---- 3. AB2-1 节点树 dump（team-lead 2026-09-23 批：**只读复用**既有驱动） ----
# 载体 = .ai-tmp\drivers\ab2-hud.cs（11934 B / mtime 2026-09-21T20:11:04，⛔ 不是本片的产物、本片不改它）。
# 协议（读它的源码原文）：读 `.ai-tmp\test\ab2-step.txt` → 用完把步号 +1；step 1/2/3 各有分支；**step 4 = 节点树 audit**；
#   L251 `WriteAllText(OutDir + "ab2-audit-step" + step + ".txt")`，OutDir 硬编码 = `.ai-tmp\test\`。
# ⚠️ 因此本链**会原地覆盖** `ab2-audit-step*.txt`（那是 AB2-1 判定行的被验对象）⇒ 跑前必须已把旧件留副本
#    （本次 = `AB2-legacy-audit-step4.txt`，13575 B，**副本、非独立证据**），跑后再把新 dump 另存带片名的副本。
$ab2drv  = "$root\.ai-tmp\drivers\ab2-hud.cs"
$ab2step = "$tmp\ab2-step.txt"
$ab2len = 0
if (Test-Path $ab2drv) { $ab2len = (Get-Item $ab2drv).Length }
$ab2legacy = Test-Path "$tmp\AB2-legacy-audit-step4.txt"
Log ("AB2 driver: exists=" + (Test-Path $ab2drv) + " bytes=" + $ab2len + "  legacy copy exists=" + $ab2legacy)
# ⚠️ **只调一次、且直接把步号设成 4** —— 这是读它源码后刻意做的选择，理由：
#   它的 step 1/2/3 是**它自己的导航状态机**（L65 `step==1` ⇒ `ui.Open<LoginPanel>(null)`；
#   L74 `step==2` ⇒ 点 LoginButton；L78 `step==3` ⇒ 点 AiBattleButton）。
#   本链已经用 `loginform/click:LoginButton/click:AiBattleButton` 进到对局了 ⇒ 再跑 step1 会
#   **在对局中把 LoginPanel 重新打开**（破坏 HUD 状态、且与 step4 要 dump 的现场不符）。
#   ⇒ 只跑 step4（`audit=True` 的那一支，L92/L93）拿节点树；步号从 4 起 ⇒ 它只 +1、不会去走 1..3。
Set-Content -Path $ab2step -Value '4' -Encoding ascii
$r = RunUnity ('command eval_file --file "' + $ab2drv + '"') 150
Log ("AB2 step 4 (audit dump) : " + $r)
Start-Sleep -Seconds 3
$ab2new = "$tmp\ab2-audit-step4.txt"
Log ("AB2 dump: exists=" + (Test-Path $ab2new) + " bytes=" + ((Get-Item $ab2new -ErrorAction SilentlyContinue).Length))
if (Test-Path $ab2new) {
    Copy-Item $ab2new "$tmp\CR-V2-ab2-audit-step4.txt" -Force
    Log ("AB2 dump preserved as CR-V2-ab2-audit-step4.txt exists=" + (Test-Path "$tmp\CR-V2-ab2-audit-step4.txt"))
}
Beat 'ab2-dump' 'ok' ("single eval_file at step=4 (avoids its 1..3 navigation); dump -> .ai-tmp/test/ab2-audit-step4.txt")

# ---- 2. the visual evidence: one composited screen capture (overlay UI needs source=screen) ----
# ★ 两次采帧（同一个相机、同机位、同尺寸）—— 这是 [7c] 遮挡分离的**真值来源**：
#   `source=screen`  = 合成屏（含 Screen Space - Overlay 的 HUD）=> `face`
#   `source=camera`  = 只渲相机、**misses Screen Space - Overlay UI** => `bg`（板下背景真值）
# 依据 = `unity command --format tsv` 里 `capture_game_view` 的自述原文（不是我编的）。
# ⇒ 于是 `face = alpha*C + (1-alpha)*bg` 可**逐像素解**，不需要改任何 UI 节点状态、也不需要平坦假设。
$shots = @(
    @{ rel = 'Temp/CR-V2/hud.png';   src = 'screen'; dst = "$root\.ai-tmp\screenshots\CR-V2-hud-mine.png" },
    @{ rel = 'Temp/CR-V2/hudbg.png'; src = 'camera'; dst = "$root\.ai-tmp\screenshots\CR-V2-hud-bg.png" }
)
foreach ($sh in $shots) {
$rel  = $sh.rel
$cmdS = 'command capture_game_view --save_path "' + $rel + '" --source ' + $sh.src + ' --width 1080 --height 1920'
Log ("SHOT cmd : " + $cmdS)
$rS = RunUnity $cmdS 180
Log ("SHOT raw : " + $rS)
$srcP = "$proj\Assets\$rel"
$dstP = $sh.dst
Log ("SHOT png : exists=" + (Test-Path $srcP) + " size=" + ((Get-Item $srcP -ErrorAction SilentlyContinue).Length))
if (Test-Path $srcP) {
    Copy-Item $srcP $dstP -Force
    # ★ CR-V2 自捕（采纳 `CR-F1` 的"**拷完就删**"口径）：旧写法 `$shotOk = (Test-Path $dstP)` 只证明**目标存在**，
    #   随后**无条件删源件** ⇒ 拷贝被截断/静默失败时，源件（**唯一副本**）直接丢 ⇒ 与"读失败被聚合吞掉"同族的第三型。
    #   加固：**先证明拷贝成功（存在 ∧ 字节数相同）才删源件**；判不出来 ⇒ NOT-JUDGED 且【保留源件】。
    $srcLen = -1
    $dstLen = -1
    try { $srcLen = (Get-Item $srcP).Length } catch { $srcLen = -1 }
    try { $dstLen = (Get-Item $dstP).Length } catch { $dstLen = -1 }
    $copyOk = ((Test-Path $dstP) -and ($dstLen -ge 0) -and ($dstLen -eq $srcLen))
    $shotOk = $copyOk
    Log ("SHOT copy: -> " + $dstP + " exists=" + (Test-Path $dstP) + " srcBytes=" + $srcLen + " dstBytes=" + $dstLen + " copyOk=" + $copyOk)
    if ($copyOk) {
        Remove-Item $srcP -Force -ErrorAction SilentlyContinue
        Remove-Item ($srcP + '.meta') -Force -ErrorAction SilentlyContinue
    } else {
        Log ("SHOT KEEP-SOURCE (NOT-JUDGED: copy not proven): " + $srcP + " 保留源件、⛔ 不删")
    }
    $d1 = Split-Path $srcP
    $d0 = Split-Path $d1
    foreach ($d in @($d1, $d0)) {
        # 同三态：读不到 ⛔ 不许落到"空"那一支（此处也是破坏性分支）
        if (-not $d) { continue }
        if (-not (Test-Path -LiteralPath $d)) { continue }
        if (-not [System.IO.Path]::IsPathRooted($d)) { Log ("  dir cleanup SKIPPED (NOT-JUDGED: not rooted): " + $d); continue }
        $enumOk2 = $false
        $cnt2 = 0
        try {
            $entries2 = [System.IO.Directory]::GetFileSystemEntries($d, '*', [System.IO.SearchOption]::AllDirectories)
            $cnt2 = @($entries2).Count
            $enumOk2 = $true
        } catch { $enumOk2 = $false }
        if ($enumOk2 -and $cnt2 -eq 0) {
            Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath ($d + '.meta') -Force -ErrorAction SilentlyContinue
        }
        if (-not $enumOk2) { Log ("  dir cleanup SKIPPED (NOT-JUDGED: enumerate failed): " + $d) }
    }
    Log ("SHOT clean: Assets copy+dirs removed: file=" + (-not (Test-Path $srcP)) + " dir=" + (-not (Test-Path $d1)) + " copyOk=" + $copyOk)
}
}
# 两张都要在（⛔ 不拿"其中一张成功"当 ok）
$shotOk = (Test-Path "$root\.ai-tmp\screenshots\CR-V2-hud-mine.png") -and (Test-Path "$root\.ai-tmp\screenshots\CR-V2-hud-bg.png")
Log ("SHOT summary: screen=" + (Test-Path "$root\.ai-tmp\screenshots\CR-V2-hud-mine.png") + " camera=" + (Test-Path "$root\.ai-tmp\screenshots\CR-V2-hud-bg.png"))
Beat 'capture' ($(if ($shotOk) { 'ok' } else { 'fail' }))

# ---- 4. 端点法（team-lead 2026-09-23 批）：板面两层的 alpha 端点 + **断言还原** ----
# 依据：§7c 实测有效 alpha ≈0.545 而代码单层 tint alpha=0.80 ⇒ 用**两个精确端点**替代回归：
#   A) fill 与 193 **都设 0** ⇒ 板矩形内应当【等于 camera 图】（同时验证"除这两层外没有别的层"）
#   B) 只把 fill 设 1.0（193 仍 0） ⇒ 板矩形内直接读出**板自身色 C**
#   ＋ 按 `alphadump` 的**打印值**回写还原，再用一次 `alphadump` **断言还原**，最后采一张验证图。
# ⚠️ 还原值我写死 0.800 / 0.350（= 产品里 `TimerPlateTint` / `TimerFrameTint` 的 alpha）——
#    这是**镜像产品常量**（本片刻意避免的写法），所以：① 先 `alphadump` 出**真值证据**；
#    ② 还原后**再 dump 一次断言**；③ 若 dump 显示的不是这两个值 ⇒ **断言会失败，我会如实报**，⛔ 不硬写 OK。
if ($Chain -eq 'alpha') {
    Log "--- ENDPOINT EXPERIMENT (approved): layer alpha endpoints + assert-restore ---"
    $axnames = 'TimerPlateFill,TimerBox'
    Log ("dump-originals  : " + (P ('alphadump:' + $axnames)))
    $shot = {
        param([string]$rel, [string]$dst, [string]$src)
        $c = 'command capture_game_view --save_path "' + $rel + '" --source ' + $src + ' --width 1080 --height 1920'
        $r = RunUnity $c 180
        Log ("  shot(" + $src + ") " + $rel + " : " + $r)
        $sp = "$proj\Assets\$rel"
        if (Test-Path $sp) {
            Copy-Item $sp $dst -Force
            Log ("  copied -> " + $dst + " exists=" + (Test-Path $dst))
            Remove-Item $sp -Force -ErrorAction SilentlyContinue
            Remove-Item ($sp + '.meta') -Force -ErrorAction SilentlyContinue
        }
    }
    Log ("set fill+193 alpha=0 : " + (P ('alpha:0:' + $axnames)))
    & $shot 'Temp/CR-V2/a0.png'   "$root\.ai-tmp\screenshots\CR-V2-hud-a0.png"   'screen'
    & $shot 'Temp/CR-V2/a0bg.png' "$root\.ai-tmp\screenshots\CR-V2-hud-a0bg.png" 'camera'
    Log ("set fill alpha=1 (193 stays 0) : " + (P 'alpha:1:TimerPlateFill'))
    & $shot 'Temp/CR-V2/a1.png'   "$root\.ai-tmp\screenshots\CR-V2-hud-a1.png"   'screen'
    Log ("restore fill=0.800 : " + (P 'alpha:0.800:TimerPlateFill'))
    Log ("restore 193 =0.350 : " + (P 'alpha:0.350:TimerBox'))
    $dumped = P ('alphadump:' + $axnames)
    Log ("RESTORE-ASSERT (expect fill a=0.800 and 193 a=0.350) : " + $dumped)
    & $shot 'Temp/CR-V2/restored.png' "$root\.ai-tmp\screenshots\CR-V2-hud-restored.png" 'screen'
    foreach ($d in @("$proj\Assets\Temp\CR-V2", "$proj\Assets\Temp")) {
        # ★ CR-V2 自捕（团队新支⑤「读失败被聚合吞掉」，且这里是【破坏性分支】）：
        #   旧写法 `... -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0` 在【枚举失败】时同样得 0
        #   ⇒ 看起来像"空目录" ⇒ 会去删一个【内容根本没读到】的目录。⇒ 改三态：EMPTY / NOTEMPTY / UNREADABLE
        if (-not (Test-Path -LiteralPath $d)) { continue }
        # ★ 改用 **.NET 枚举**（采纳 `CR-F1` 的实测结论）：`Get-ChildItem -ErrorAction Stop` 依赖 cmdlet 的错误语义，
        #   对"注入的函数/非终止错误"会失效 ⇒ 可能变成"因偶然而判对"。
        #   `[IO.Directory]::GetFileSystemEntries` 抛的是**真异常**（DirectoryNotFound / UnauthorizedAccess）⇒ catch 可靠。
        #   并先断言**绝对路径**（相对路径会被 .NET 按进程 CWD 解析 ⇒ 本片踩过一次）。
        if (-not [System.IO.Path]::IsPathRooted($d)) { Log ("  temp cleanup SKIPPED (NOT-JUDGED: path not rooted): " + $d); continue }
        $enumOk = $false
        $cnt = 0
        try {
            $entries = [System.IO.Directory]::GetFileSystemEntries($d, '*', [System.IO.SearchOption]::AllDirectories)
            $cnt = @($entries).Count
            $enumOk = $true
        } catch { $enumOk = $false }
        if ($enumOk -and $cnt -eq 0) {
            Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath ($d + '.meta') -Force -ErrorAction SilentlyContinue
            Log ("  temp cleanup: removed " + $d + " (state=EMPTY)")
        }
        if (-not $enumOk) { Log ("  temp cleanup SKIPPED (NOT-JUDGED: enumerate failed): " + $d) }
    }
    Log ("temp dirs removed: " + (-not (Test-Path "$proj\Assets\Temp\CR-V2")))
    Beat 'alpha-endpoints' 'ok' 'a0(screen+camera) / a1 / restored captured; dump-originals + restore-assert logged'
}

# ---- 5. 端点 A **补全版** + **线性实验**（team-lead 2026-09-23 批）----
# 目的：① 端点 A 补全（把 `TimerLabel`/`Timer` 也置 0 ⇒ 板矩形内应逐像素等于 camera）
#       ② 线性实验：设定 alpha 0.4 / 0.6 / 1.0 各采一次 ⇒ 看"设定 → 实测"是否线性，
#          直接区分"有效 alpha ≠ 设定值"与"alpha 被乘了两次"。
# ★ 数学上**不需要 B**：同像素上 `C - S(a) = (1-a_eff)*(C-B)` ⇒
#   `(C-S(a1))/(C-S(a2)) = (1-a1_eff)/(1-a2_eff)` ⇒ 只用 a1（α=1⇒C）与各 α 的板区图即可，
#   对"板下背景究竟是什么"完全免疫。camera 图只用于端点 A 的"应当相等"检查。
# ⚠️ 本块自带一份 `$shot`（与 alpha 块同形）：⛔ 刻意不去重构已验证过的 alpha 块以免动到它的指纹/行为。
# ---- 6. `rows` 模式的前半：7 条 HUD 行需要的 battle/pause 两张 + 派生物源图刷新 ----
# 出处（team-lead ⑧ / CR-R1 的溯源）：`CR-T2-hud-mine.png` 在 `策划/` 里**无引用行**、是
#   `tools/probes/cr-t2x-*.py` 的**输入**（⇒ 它是"派生物的源"，不是独立态证据）⇒ 与源图**同一条链**刷新。
# 覆盖前先留 legacy 副本 + 链日志写三样（覆盖声明 / 新副本名 / 旧件 sha16+bytes）。
if ($Chain -eq 'rows') {
    Log "--- ROWS: battle + pause captures + derived-source refresh ---"
    $t2 = "$root\.ai-tmp\screenshots\CR-T2-hud-mine.png"
    if (Test-Path $t2) {
        $ih = (Get-FileHash $t2 -Algorithm SHA256).Hash.Substring(0, 16)
        $ib = (Get-Item $t2).Length
        $lg = "$root\.ai-tmp\screenshots\CR-T2-hud-mine-legacy-$ih.png"
        Copy-Item $t2 $lg -Force
        Log ("OVERWRITE-DECL: about to overwrite CR-T2-hud-mine.png (old: sha16=$ih bytes=$ib) ; legacy copy (COPY ONLY, NOT an independent observation) -> $lg exists=" + (Test-Path $lg))
    } else {
        Log "OVERWRITE-DECL: CR-T2-hud-mine.png did not exist before this chain"
    }
    Copy-Item "$root\.ai-tmp\screenshots\CR-V2-hud-mine.png" $t2 -Force
    $nh = (Get-FileHash $t2 -Algorithm SHA256).Hash.Substring(0, 16)
    Log ("NEW COPY: CR-T2-hud-mine.png refreshed from this chain's battle screen capture (sha16=$nh bytes=" + (Get-Item $t2).Length + ")")
    Beat 'rows-captures' 'ok' 'battle screen capture refreshed CR-T2-hud-mine.png (legacy preserved)'
    # ⚠️ pause 态**放到本链最后**再采 —— 实测发现：共用探针 `CR-U5-probe.cs` **不支持** `closePanel:`
    #    （`Select-String closePanel` = 0 命中）⇒ 一旦在链中间打开暂停面板就**关不掉**，会把后面所有
    #    `--source screen` 采图全遮住（= 采出一堆废图）。⇒ 顺序改为"先采能采的，暂停态留到最后"，
    #    链尾直接 `editor_stop` ⇒ 不需要关它。（这条是我自己的"先验能力再排步骤"挡下来的。）
}

if ($Chain -eq 'alpha2' -or $Chain -eq 'rows') {
    Log "--- ENDPOINT-A-COMPLETE + LINEARITY ---"
    $shot2 = {
        param([string]$rel, [string]$dst, [string]$src)
        $c = 'command capture_game_view --save_path "' + $rel + '" --source ' + $src + ' --width 1080 --height 1920'
        $r = RunUnity $c 180
        Log ("  shot(" + $src + ") " + $rel + " : " + $r)
        $sp = "$proj\Assets\$rel"
        if (Test-Path $sp) {
            Copy-Item $sp $dst -Force
            Log ("  copied -> " + $dst + " exists=" + (Test-Path $dst))
            Remove-Item $sp -Force -ErrorAction SilentlyContinue
            Remove-Item ($sp + '.meta') -Force -ErrorAction SilentlyContinue
        }
    }
    Log ("dump-originals : " + (P 'alphadump:TimerPlateFill,TimerBox,TimerLabel,Timer'))
    Log ("193 -> 0 (keep it out of the way) : " + (P 'alpha:0:TimerBox'))
    foreach ($a in @('1', '0.6', '0.4')) {
        Log ("fill alpha=" + $a + " : " + (P ('alpha:' + $a + ':TimerPlateFill')))
        & $shot2 ("Temp/CR-V2/lin" + $a + ".png") ("$root\.ai-tmp\screenshots\CR-V2-hud-a" + ($a -replace '\.', '') + ".png") 'screen'
    }
    Log ("endpoint-A-COMPLETE (fill+193+texts all 0) : " + (P 'alpha:0:TimerPlateFill,TimerLabel,Timer'))
    & $shot2 'Temp/CR-V2/a0full.png' "$root\.ai-tmp\screenshots\CR-V2-hud-a0full.png" 'screen'
    & $shot2 'Temp/CR-V2/a0fullbg.png' "$root\.ai-tmp\screenshots\CR-V2-hud-a0fullbg.png" 'camera'
    Log ("restore fill=0.800 : " + (P 'alpha:0.800:TimerPlateFill'))
    Log ("restore text/193   : " + (P 'alpha:1:TimerLabel,Timer') + " | " + (P 'alpha:0.350:TimerBox'))
    $d2 = P 'alphadump:TimerPlateFill,TimerBox,TimerLabel,Timer'
    Log ("RESTORE-ASSERT (expect fill 0.800 / 193 0.350 / texts 1.000) : " + $d2)
    & $shot2 'Temp/CR-V2/a2restored.png' "$root\.ai-tmp\screenshots\CR-V2-hud-a2restored.png" 'screen'
    foreach ($d in @("$proj\Assets\Temp\CR-V2", "$proj\Assets\Temp")) {
        # ★ CR-V2 自捕（团队新支⑤「读失败被聚合吞掉」，且这里是【破坏性分支】）：
        #   旧写法 `... -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0` 在【枚举失败】时同样得 0
        #   ⇒ 看起来像"空目录" ⇒ 会去删一个【内容根本没读到】的目录。⇒ 改三态：EMPTY / NOTEMPTY / UNREADABLE
        if (-not (Test-Path -LiteralPath $d)) { continue }
        # ★ 改用 **.NET 枚举**（采纳 `CR-F1` 的实测结论）：`Get-ChildItem -ErrorAction Stop` 依赖 cmdlet 的错误语义，
        #   对"注入的函数/非终止错误"会失效 ⇒ 可能变成"因偶然而判对"。
        #   `[IO.Directory]::GetFileSystemEntries` 抛的是**真异常**（DirectoryNotFound / UnauthorizedAccess）⇒ catch 可靠。
        #   并先断言**绝对路径**（相对路径会被 .NET 按进程 CWD 解析 ⇒ 本片踩过一次）。
        if (-not [System.IO.Path]::IsPathRooted($d)) { Log ("  temp cleanup SKIPPED (NOT-JUDGED: path not rooted): " + $d); continue }
        $enumOk = $false
        $cnt = 0
        try {
            $entries = [System.IO.Directory]::GetFileSystemEntries($d, '*', [System.IO.SearchOption]::AllDirectories)
            $cnt = @($entries).Count
            $enumOk = $true
        } catch { $enumOk = $false }
        if ($enumOk -and $cnt -eq 0) {
            Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath ($d + '.meta') -Force -ErrorAction SilentlyContinue
            Log ("  temp cleanup: removed " + $d + " (state=EMPTY)")
        }
        if (-not $enumOk) { Log ("  temp cleanup SKIPPED (NOT-JUDGED: enumerate failed): " + $d) }
    }
    Log ("temp dirs removed: " + (-not (Test-Path "$proj\Assets\Temp\CR-V2")))
    Beat 'alpha2-endpoints' 'ok' 'a1/a06/a04/a0full(+camera)/a2restored captured; restore-assert logged'
}

# ---- 7. `rows` 模式的**尾部**：pause 态采图（刻意放在最后一—见上面那条注释：探针关不掉暂停面板）----
if ($Chain -eq 'rows') {
    Log "--- ROWS tail: pause-state capture (deliberately last) ---"
    Log ("click PauseButton : " + (P 'click:PauseButton'))
    Start-Sleep -Seconds 3
    Log ("state(pause) : " + (P 'state'))
    RunUnity ('command capture_game_view --save_path "Temp/CR-V2/pause.png" --source screen --width 1080 --height 1920') 180 | Out-Null
    $psrc = "$proj\Assets\Temp\CR-V2\pause.png"
    if (Test-Path $psrc) {
        Copy-Item $psrc "$root\.ai-tmp\screenshots\CR-V2-hud-pause.png" -Force
        Remove-Item $psrc -Force -ErrorAction SilentlyContinue
        Remove-Item ($psrc + '.meta') -Force -ErrorAction SilentlyContinue
        Log ("pause capture -> CR-V2-hud-pause.png exists=" + (Test-Path "$root\.ai-tmp\screenshots\CR-V2-hud-pause.png"))
    } else { Log "pause capture MISSING (Temp/CR-V2/pause.png not produced)" }
    # ★ CR-V2 追加（team-lead 2026-09-23 ③）：**最后一次写 Assets 之后**再清一次临时树。
    #   根因（实测，不是猜）：本链原有的两次清理都在 `alpha2` 块里（L215/L288 那两处），而
    #   **pause 采图发生在它们之后** ⇒ 采图把 `Assets\Temp\CR-V2\`（以及 Unity 生成的目录 `.meta`）
    #   **重新造了出来**；我只删了 `pause.png` 与它的 meta ⇒ 留下
    #   `Assets\Temp\CR-V2\`（空目录）+ `CR-V2.meta`(172 B) —— 与 `CR-F1` 早先踩过的形态完全同形。
    #   ⇒ 清理必须放在"最后一次写 `Assets/` 之后"。形态照 `CR-F1-run.ps1:142-153`。
    foreach ($d in @("$proj\Assets\Temp\CR-V2", "$proj\Assets\Temp")) {
        # ★ CR-V2 自捕（团队新支⑤「读失败被聚合吞掉」，此处是【破坏性分支】）：见上面同形注释。
        if (-not (Test-Path -LiteralPath $d)) { continue }
        # ★ 改用 **.NET 枚举**（采纳 `CR-F1` 的实测结论）：`Get-ChildItem -ErrorAction Stop` 依赖 cmdlet 的错误语义，
        #   对"注入的函数/非终止错误"会失效 ⇒ 可能变成"因偶然而判对"。
        #   `[IO.Directory]::GetFileSystemEntries` 抛的是**真异常**（DirectoryNotFound / UnauthorizedAccess）⇒ catch 可靠。
        #   并先断言**绝对路径**（相对路径会被 .NET 按进程 CWD 解析 ⇒ 本片踩过一次）。
        if (-not [System.IO.Path]::IsPathRooted($d)) { Log ("  temp cleanup SKIPPED (NOT-JUDGED: path not rooted): " + $d); continue }
        $enumOk = $false
        $cnt = 0
        try {
            $entries = [System.IO.Directory]::GetFileSystemEntries($d, '*', [System.IO.SearchOption]::AllDirectories)
            $cnt = @($entries).Count
            $enumOk = $true
        } catch { $enumOk = $false }
        if ($enumOk -and $cnt -eq 0) {
            Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath ($d + '.meta') -Force -ErrorAction SilentlyContinue
            Log ("  temp cleanup AFTER last capture: removed " + $d + " (+ .meta, state=EMPTY)")
        }
        if (-not $enumOk) { Log ("  temp cleanup SKIPPED (NOT-JUDGED: enumerate failed): " + $d) }
    }
    # ⚠️ 刻意**写成单行**：本片已在"跨行 `Log (...)` + 行首 `+`"这个形态上翻了两次（一次 AB2 段、
    #    一次本次），两次都是 `Missing closing ')'`（`parsecheck` 拦下、链没跑）。⇒ 取证日志一律单行拼接。
    $tNow = "CR-V2=" + (Test-Path "$proj\Assets\Temp\CR-V2") + " Temp=" + (Test-Path "$proj\Assets\Temp") + " Temp.meta=" + (Test-Path "$proj\Assets\Temp.meta")
    Log ("  temp tree now: " + $tNow)
    Beat 'rows-pause' 'ok' 'pause-state capture taken last; pause panel intentionally left open (editor_stop ends the chain); temp tree cleaned after the last Assets write'
}

# ---- 3. assembly gate, post ----
$shaPost = DllSha
$same = ($shaPost -eq $shaPre)
if (-not $same) { $warn += "pre/post DIFFER: pre=$shaPre post=$shaPost" }
Log ("ASSEMBLY post-run: CR.dll sha256_16=" + $shaPost + " pre=" + $shaPre + " pre_eq_post=" + $same)
if ($warn.Count -gt 0) {
    Log ("ASSEMBLY WARN: " + ($warn -join ' | '))
    Beat 'assembly-gate' 'fail' ("WARN: " + ($warn -join ' | '))
} else {
    Log ("ASSEMBLY OK: sha16=" + $shaPre + " (kept copy present, in index.tsv, pre==post)")
    Beat 'assembly-gate' 'ok' ("sha16=" + $shaPre)
}
Log ("=== CR-V2 chain=$Chain done " + (Get-Date -Format o) + " (started $runStart) ===")
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))

# ★ CR-V2 追加（采纳 `CR-F1` 的 CleanPost 形态 + team-lead 2026-09-23 ⑦）：**链末 + `editor_stop` 之后**
#   再做一次**存在性断言** ⇒ "清理晚了一步 / 清理位置又错了"会**自己暴露**，⛔ 不靠"位置正确"这句话。
#   判定：`Assets\Temp\CR-V2` 与它的 `.meta` 都不存在 = OK；任一存在 = FAIL（本片遗留物）。
#   ⚠️ 该段的失败模式是【遗留物留下】、**不是**"证据被删"：清理只删**空目录**（负控已实测非空目录一个都不删）。
# ⚠️ 三态（★ CR-V2 自捕，2026-09-23）：**第一版是"因偶然而判对"** —— 原写法 `$cpOk = -not (Test-Path …)`
#   一旦 `Test-Path` 因**读不到**（权限/路径异常/编辑器占用）而返回 `$false`，就等价于"不存在" ⇒ **判 OK**。
#   ⇒ 即"**读失败被当成通过**"（正是团队刚入册的「因偶然而判对」族，也与「读不到 ≠ 通过」同根）。
#   修法：`-ErrorAction Stop` + `try/catch` 分三态 ⇒ **读失败 = NOT-JUDGED**（⛔ 既不计 OK、也不计 FAIL）。
$cpDir = $false
$cpMeta = $false
$cpReadErr = ''
try {
    $cpDir = Test-Path -LiteralPath "$proj\Assets\Temp\CR-V2" -ErrorAction Stop
    $cpMeta = Test-Path -LiteralPath "$proj\Assets\Temp\CR-V2.meta" -ErrorAction Stop
} catch {
    $cpReadErr = $_.Exception.Message
}
$cpOk = $false
if ($cpReadErr -ne '') { $cp = 'NOT-JUDGED(read-error: ' + $cpReadErr + ')' } else {
    $cpOk = -not ($cpDir -or $cpMeta)
    if ($cpOk) { $cp = 'OK' } else { $cp = 'FAIL(dir=' + $cpDir + ' meta=' + $cpMeta + ')' }
}
Log ("  CLEANPOST: " + $cp + " (checked AFTER editor_stop; 3-state)")
if ($cpOk) { Beat 'cleanpost' 'ok' 'Assets/Temp/CR-V2 + .meta absent after editor_stop' } else { Beat 'cleanpost' 'fail' ('NOT-ok after editor_stop: ' + $cp) }
Beat 'chain-end' 'ok' ("chain=" + $Chain + " editor stopped, window FREE; CLEANPOST=" + $cp)
$lfG2 = EnsureLF $play
Log ("  LFGUARD(end-append): play-log trailing-LF = " + $lfG2)
App $play ((Get-Date -Format o) + "`tCR-V2`t" + $Chain + "`tEND - editor stopped, window FREE. Capture = .ai-tmp/screenshots/CR-V2-hud-mine.png + CR-V2-hud-bg.png; readings = .ai-tmp/test/CR-V2-measure-after.txt; sha16 pre=" + $shaPre + " post=" + $shaPost + " pre_eq_post=" + $same + "; CLEANPOST=" + $cp + "; LFGUARD=" + $lfG1 + "/" + $lfG2 + "`n")
