// ============================================================================
// T3 判据资产 —— 《皇室战争》复刻项目「对局模型抖动」的前后对比量法/探针
// ----------------------------------------------------------------------------
// 这个文件是**判据资产**（删了就无法重新判定同一件事），因此它放 tools/probes/ 并进版本库，
// ⛔ 不放 .ai-tmp/test/。它只是"量法"，不含任何游戏逻辑；产物（TSV / 对比图）落在
// <项目根>/.ai-tmp/ 下，不进 Assets/。
//
// 怎么跑（编辑器已开着；每条命令都要带 --project-path，闸门 2b）：
//   cd <项目根>/client
//   unity command run_script --project-path <项目根>/client --no-banner \
//     --file "<项目根>/tools/probes/FlowProbe.cs" --entry FlowProbe.<方法> --timeout_ms 120000
//
// 三条实测教训（沿用 tools/probes/CrProbe.cs 的结论，别踩第二遍）：
//  1) 不要用 `unity command eval --code`：代码要经 PowerShell → CLI → HTTP 三层引号，双引号/方括号必被吃掉。
//  2) 不要用 `run_script --args`：数组参数同样过不了引号层 ⇒ **每个动作写成一个无参入口**。
//  3) 逐帧采样必须 `await Task.Yield()`（已实测：30 次 await 共耗 3.1 s，确实在"跨帧"恢复），
//     并且 Play 前先 `set_autotick` + 运行时置 `Application.runInBackground=true`，
//     否则编辑器失焦时玩家循环冻结（frameCount 不动，采样全落在一帧上）。
//
// ⚠️ 只读/不改工程：本文件只读 Unity API 与项目公开成员；唯一"写"是往
//    <项目根>/.ai-tmp/test/ 写测量产物（导出 TSV）。
// ============================================================================
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class FlowProbe
{
    // ───────────────────────────── 公共小工具 ─────────────────────────────

    /// <summary>项目根（= <c>&lt;client&gt;/..</c>）：由 `Application.dataPath` 反推，⛔ 不写死绝对路径。</summary>
    public static string ProjectRoot
    {
        get
        {
            var client = System.IO.Path.GetDirectoryName(Application.dataPath);   // .../client
            return System.IO.Path.GetDirectoryName(client);                        // .../<项目根>
        }
    }

    /// <summary>测量产物目录（`.ai-tmp/test/`，全局 §1.8 规定的唯一临时区）。</summary>
    public static string TestDir
    {
        get
        {
            var d = System.IO.Path.Combine(ProjectRoot, ".ai-tmp", "test");
            System.IO.Directory.CreateDirectory(d);
            return d;
        }
    }

    private static void Log(string msg) { CloverEngine.Game.Logger?.Info("FlowProbe", msg); }

    // ───────────────────────────── A. 环境基线（skill §2.7） ─────────────────────────────

    /// <summary>
    /// 环境基线：**渲染设备名 + 帧时间 + 分辨率**。⛔ 没确认渲染设备之前，一切"卡/掉帧/抖"的结论无效。
    /// 设备名是 `Microsoft Basic Render Driver`（软件光栅）时，帧时间数字无意义，必须先修环境。
    /// </summary>
    public static string EnvBaseline()
    {
        var sb = new StringBuilder();
        sb.Append("isPlaying=").Append(Application.isPlaying).Append('\n');
        sb.Append("graphicsDeviceName=").Append(SystemInfo.graphicsDeviceName).Append('\n');
        sb.Append("graphicsDeviceType=").Append(SystemInfo.graphicsDeviceType)
          .Append(" vendor=").Append(SystemInfo.graphicsDeviceVendor)
          .Append(" version=").Append(SystemInfo.graphicsDeviceVersion).Append('\n');
        sb.Append("graphicsMemoryMB=").Append(SystemInfo.graphicsMemorySize)
          .Append(" shaderLevel=").Append(SystemInfo.graphicsShaderLevel)
          .Append(" maxTextureSize=").Append(SystemInfo.maxTextureSize).Append('\n');
        sb.Append("screen=").Append(Screen.width).Append('x').Append(Screen.height)
          .Append(" fullscreen=").Append(Screen.fullScreen)
          .Append(" dpi=").Append(Screen.dpi).Append('\n');
        sb.Append("runInBackground=").Append(Application.runInBackground)
          .Append(" targetFrameRate=").Append(Application.targetFrameRate)
          .Append(" vSync=").Append(QualitySettings.vSyncCount)
          .Append(" timeScale=").Append(Time.timeScale).Append('\n');
        var cam = Camera.main;
        sb.Append("mainCamera=").Append(cam == null ? "null" : cam.name)
          .Append(" orthoSize=").Append(cam == null ? 0f : cam.orthographicSize)
          .Append(" aspect=").Append(cam == null ? 0f : cam.aspect)
          .Append(" pixelRect=").Append(cam == null ? new Rect() : cam.pixelRect).Append('\n');
        sb.Append("frameCount=").Append(Time.frameCount)
          .Append(" realtime=").Append(Time.realtimeSinceStartup.ToString("F3")).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// 采样用的**帧率实测**：连续 <c>FpsProbeFrames</c> 帧记录 `Time.unscaledDeltaTime`，报 min/avg/max 与 FPS。
    /// 判断"抖"之前先知道这台机器一帧多长（软件渲染下 60 FPS 的假设不成立）。
    /// </summary>
    public const int FpsProbeFrames = 120;

    /// <summary>实测帧率（逐帧，用于解释"尖峰"到底是渲染卡还是插值错）。</summary>
    public static async Task<string> MeasureFps()
    {
        var min = float.MaxValue; var max = 0f; var sum = 0f;
        var frames = 0;
        for (var i = 0; i < FpsProbeFrames; i++)
        {
            var dt = Time.unscaledDeltaTime;
            if (dt < min) min = dt;
            if (dt > max) max = dt;
            sum += dt;
            frames++;
            await Task.Yield();
        }
        var avg = sum / Mathf.Max(1, frames);
        return $"frames={frames} minMs={min * 1000f:F2} avgMs={avg * 1000f:F2} maxMs={max * 1000f:F2} " +
               $"fps={1f / Mathf.Max(0.0001f, avg):F1} device={SystemInfo.graphicsDeviceName}";
    }

    /// <summary>
    /// **只允许在 Play 模式下**解冻玩家循环（编辑器失焦时 `runInBackground=false` 会让循环完全冻结）。
    /// ⛔ 刻意加 `Application.isPlaying` 守卫：在编辑模式改它可能落到 `ProjectSettings/**`，那是本片禁区。
    /// </summary>
    public static string Unpause()
    {
        if (!Application.isPlaying)
        {
            Log("拒绝在编辑模式改 runInBackground（可能写进 ProjectSettings，属本片禁区）");
            return "EDITMODE-REFUSED isPlaying=False runInBackground=" + Application.runInBackground;
        }
        Application.runInBackground = true;
        return "runInBackground=True";
    }

    // ───────────────────────── B. 帧序断言（根因 2） ─────────────────────────

    /// <summary>断言用的目录：`chr_knight_out`（486 帧，任务书点名的取证对象）。</summary>
    public const string KnightDir = "chr_knight_out";

    /// <summary>
    /// 帧序断言：打印**排序后**前 5 / 后 5 个 Sprite 的真实名字，以及
    /// ①排序前（`Resources.LoadAll` 的原始返回顺序）②排序后 的帧序是否单调。
    /// <para>
    /// 判据：修复前 `ParseFrameIndex` 取名字**尾部**数字串，而导入名是 `frame_000_0`（`.meta` 的
    /// `name: frame_000_0`）⇒ 尾部数字恒为 `0` ⇒ 所有帧同一个 key ⇒ 排序是空转；修复后应取
    /// **倒数第二段**数字（`000`）⇒ 486 个 key、排序后严格 0..485
    ///（实测：`frame_000_0`..`frame_485_0`，无缺口）。
    /// </para>
    /// </summary>
    public static string FrameOrderKnight() { return FrameOrder("Units", KnightDir); }

    /// <summary>同一断言作用于任一 `Units` 目录（改常量即可换目录，⛔ 不用 `--args` 传参）。</summary>
    public static string FrameOrder(string area, string dir)
    {
        var sb = new StringBuilder();
        var path = "Sprites/" + area + "/" + dir;
        sb.Append("path=Assets/Resources/").Append(path).Append('\n');

        // ① 原始顺序（绕过 SpriteBank，直接看 Unity 给了什么）——这是"排序前"的基线。
        var raw = Resources.LoadAll<Sprite>(path);
        sb.Append("rawCount=").Append(raw == null ? -1 : raw.Length)
          .Append("  rawFirst5=").Append(Names(raw, 0, 5))
          .Append("  rawLast5=").Append(Names(raw, Mathf.Max(0, (raw == null ? 0 : raw.Length) - 5), 5)).Append('\n');
        sb.Append("rawDistinctTailIndex=").Append(DistinctIndex(raw, true))
          .Append("  rawDistinctSecondLastIndex=").Append(DistinctIndex(raw, false)).Append('\n');

        // ② 工程真实路径（SpriteBank，含排序 + 本片的锚点重建）
        var frames = LoadViaSpriteBank(path, out var how);
        sb.Append("loadVia=").Append(how)
          .Append("  count=").Append(frames == null ? -1 : frames.Length).Append('\n');
        if (frames == null || frames.Length == 0)
        {
            sb.Append("!!! SpriteBank 取不到帧（Game.Res 为空 / 资源缺失）—— 断言无法进行\n");
            return sb.ToString();
        }

        sb.Append("sortedFirst5=").Append(Names(frames, 0, 5)).Append('\n');
        sb.Append("sortedLast5=").Append(Names(frames, frames.Length - 5, 5)).Append('\n');

        // ③ 排序是否真的生效：用**修复后**的规则（倒数第二段）算 key，检查是否严格递增
        var keys = new int[frames.Length];
        for (var i = 0; i < frames.Length; i++) keys[i] = IndexSecondLast(frames[i].name);
        var monotonic = true;
        for (var i = 1; i < frames.Length; i++) if (keys[i] <= keys[i - 1]) { monotonic = false; break; }
        sb.Append("indexRule=secondLastNumeric  keyFirst5=");
        for (var i = 0; i < Mathf.Min(5, keys.Length); i++) sb.Append(keys[i]).Append(' ');
        sb.Append(" keyLast5=");
        for (var i = Mathf.Max(0, keys.Length - 5); i < keys.Length; i++) sb.Append(keys[i]).Append(' ');
        sb.Append('\n');
        sb.Append("strictlyIncreasing=").Append(monotonic)
          .Append("  distinctKeys=").Append(DistinctInts(keys))
          .Append("  tailRuleDistinct=").Append(DistinctIndex(frames, true)).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// 帧序修复的**回归检查**：竞技场（`GroundFrameIndex = 6`）与塔（`203/201/213/211`）是按
    /// **硬编码下标**取帧的，而帧序修复把"LoadAll 的偶然顺序"换成了"确定性的数字序"。
    /// 若原始顺序本来就是数字升序 ⇒ 修复后下标含义不变（安全）；不是升序 ⇒ 修复会**改变**这些下标
    /// 指向的图，必须同时改 `ArenaView` 的常量（本条就是那个判据）。
    /// </summary>
    public static string FrameOrderRegression()
    {
        var sb = new StringBuilder();
        sb.Append("检查：raw(LoadAll) 顺序是否 == 数字升序（决定硬编码下标是否安全）\n");
        Row(sb, "Arenas", "arena_training_out", "GroundFrameIndex=6");
        Row(sb, "Towers", "building_tower_out", "tower frames 203/201/213/211");
        Row(sb, "Units", "chr_giant_out", "(对照：另一个单位目录)");
        return sb.ToString();
    }

    /// <summary>
    /// **下标映射**：`ArenaView` 用硬编码下标取帧（竞技场 6 / 塔 203·201·213·211）。
    /// 帧序修复把"LoadAll 偶然顺序"换成"稳定按（png 序号）排序"，若两者不同，这些下标就会指向别的图。
    /// 本入口打印这些下标在 **raw 顺序**（= 修复前）与 **排序后**（= 修复后）分别指向谁。
    /// 判据：两列**逐字相同** ⇒ 修复对硬编码下标零影响。
    /// </summary>
    public static string IndexMap()
    {
        var sb = new StringBuilder();
        Map(sb, "Arenas", "arena_training_out", new[] { 6 });
        Map(sb, "Towers", "building_tower_out", new[] { 203, 201, 213, 211 });
        Map(sb, "Units", KnightDir, new[] { 0, 1, 2, 481, 485 });
        return sb.ToString();
    }

    private static void Map(StringBuilder sb, string area, string dir, int[] idx)
    {
        var raw = Resources.LoadAll<Sprite>("Sprites/" + area + "/" + dir);
        if (raw == null || raw.Length == 0) { sb.Append(dir).Append(" : 取不到\n"); return; }
        var sorted = StableSortByKey(raw);
        // 每张 PNG 里的子 Sprite 数（暴露"一个 PNG 多个 Sprite"这一事实）
        var perPng = new Dictionary<int, int>();
        foreach (var s in raw)
        {
            var k = IndexSecondLast(s.name);
            perPng[k] = (perPng.ContainsKey(k) ? perPng[k] : 0) + 1;
        }
        var multi = 0;
        foreach (var kv in perPng) if (kv.Value > 1) multi++;
        sb.Append(dir).Append("  n=").Append(raw.Length)
          .Append("  pngCount=").Append(perPng.Count)
          .Append("  pngWithMultipleSprites=").Append(multi).Append('\n');
        foreach (var i in idx)
        {
            sb.Append("  idx=").Append(i.ToString().PadLeft(4))
              .Append("  raw=").Append(i < raw.Length ? raw[i].name.PadRight(14) : "(oob)")
              .Append("  sorted=").Append(i < sorted.Length ? sorted[i].name.PadRight(14) : "(oob)")
              .Append("  same=").Append(i < raw.Length && i < sorted.Length && raw[i].name == sorted[i].name)
              .Append('\n');
        }
    }

    /// <summary>镜像 `SpriteBank.SortByFrameIndex`（稳定插入排序 + 修复后的 key 规则）。</summary>
    private static Sprite[] StableSortByKey(Sprite[] src)
    {
        var a = (Sprite[])src.Clone();
        for (var i = 1; i < a.Length; i++)
        {
            var cur = a[i];
            var ck = IndexSecondLast(cur.name);
            var j = i - 1;
            while (j >= 0 && IndexSecondLast(a[j].name) > ck) { a[j + 1] = a[j]; j--; }
            a[j + 1] = cur;
        }
        return a;
    }

    private static void Row(StringBuilder sb, string area, string dir, string note)
    {
        var raw = Resources.LoadAll<Sprite>("Sprites/" + area + "/" + dir);
        if (raw == null || raw.Length == 0) { sb.Append("  ").Append(dir).Append(" : 取不到\n"); return; }
        var inName = true;
        for (var i = 1; i < raw.Length; i++)
            if (IndexSecondLast(raw[i].name) < IndexSecondLast(raw[i - 1].name)) { inName = false; break; }
        sb.Append("  ").Append(dir.PadRight(22)).Append(" n=").Append(raw.Length)
          .Append(" rawOrderIsNumericAscending=").Append(inName)
          .Append("  [0]=").Append(raw[0].name)
          .Append("  [n-1]=").Append(raw[raw.Length - 1].name)
          .Append("   <- ").Append(note).Append('\n');
    }

    /// <summary>
    /// 帧序断言（**字符串形态的规则自检**，不依赖资源）：
    /// `frame_000_0→0`、`frame_7_1→7`、`frame_003→3`、`frame_000→0`、`gen_frame_012→12`、`White1x1→-1`、`ArenaSeg→-1`。
    /// 同时打印**旧规则**（尾段）对同样输入的取值，用来把"根因 2"钉死。
    /// </summary>
    public static string FrameIndexRule()
    {
        var cases = new[] { "frame_000_0", "frame_7_1", "frame_003", "frame_000", "gen_frame_012", "frame_485_0", "White1x1", "ArenaSeg", "ArenaSeg.1" };
        var sb = new StringBuilder();
        sb.Append("new! = 倒数第二段数字（本片修复后）   old = 尾部数字串（修复前）\n");
        foreach (var c in cases)
            sb.Append(c.PadRight(16)).Append(" new=").Append(IndexSecondLast(c).ToString().PadLeft(5))
              .Append("  old=").Append(IndexTail(c).ToString().PadLeft(5)).Append('\n');
        return sb.ToString();
    }

    // ───────────────────────── C. 逐帧 pivot 断言（根因 4） ─────────────────────────

    /// <summary>
    /// pivot 语义自检（**不许编 API**）：`Sprite.Create(tex, rect, pivot, ppu)` 的 `pivot` 参数到底是
    /// "相对 rect 归一化"还是"相对整张贴图归一化"？用一个**非零偏移 + 非方形**的 rect 实测：
    /// 若 `sprite.pivot`（单位 = 像素）等于 `pivot * rect.size` ⇒ 相对 rect；若等于 `pivot * texSize` ⇒ 相对贴图。
    /// 这决定了"统一纹理锚点"该怎么换算，⛔ 不许靠回忆写。
    /// </summary>
    public static string PivotSemantics()
    {
        var sb = new StringBuilder();
        var path = "Sprites/Units/" + KnightDir;
        var raw = Resources.LoadAll<Sprite>(path);
        if (raw == null || raw.Length == 0) return "取不到 " + path;
        var src = raw[0];
        var tex = src.texture;
        var rect = new Rect(27f, 33f, 101f, 86f);          // 刻意非原点、非方形（= frame_000 的真实 rect）
        var ppu = src.pixelsPerUnit > 0.01f ? src.pixelsPerUnit : 100f;
        sb.Append("tex=").Append(tex.width).Append('x').Append(tex.height).Append(" ppu=").Append(ppu).Append('\n');
        sb.Append("importedRect=").Append(Format(src.rect)).Append(" importedPivot=").Append(Format2(src.pivot))
          .Append(" importedBoundsC=").Append(Format2((Vector2)src.bounds.center)).Append('\n');

        var made = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), ppu);
        sb.Append("Create(pivot=0.5,0.5) -> sprite.pivot=").Append(Format2(made.pivot))
          .Append("  ifRectRel=").Append(Format2(new Vector2(rect.width * 0.5f, rect.height * 0.5f)))
          .Append("  ifTexRel=").Append(Format2(new Vector2(tex.width * 0.5f, tex.height * 0.5f)))
          .Append("  boundsCenter=").Append(Format2((Vector2)made.bounds.center)).Append('\n');

        var made2 = Sprite.Create(tex, rect, new Vector2((93.5f - rect.x) / rect.width, (90.5f - rect.y) / rect.height), ppu);
        sb.Append("Create(rect-relative anchor of tex point (93.5,90.5)) -> pivot=").Append(Format2(made2.pivot))
          .Append(" boundsCenter=").Append(Format2((Vector2)made2.bounds.center)).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// 逐帧 pivot 断言：打印 `chr_knight_out` **重建前/重建后**各 5 帧的锚点（纹理坐标）与 `bounds.center`。
    /// <para>
    /// 判据（两条，都能复算）：
    /// ① **锚点共享**：同一目录所有帧的 `sprite.pivot` 换算回纹理像素坐标后必须**完全相同**
    ///    （= 并集中心，实测 chr_knight_out 的并集 = 整幅画布 187×181 ⇒ 纹理点 (93.5, 90.5)）。
    ///    修复前每帧的纹理锚点 = 各自裁剪框中心（实测 frame_000 → (77.5, 76)、frame_001 → (74.5, 79.5)…）⇒ 换帧就位移。
    /// ② **内容随锚点共位**：修复后 `bounds.center` 在各帧之间的差 = 该帧内容相对画布锚点的**美术偏移**
    ///    （"人物画在画布上的哪里"），而**不再是 0**（修复前每帧内容都被重新居中 ⇒ bounds.center 恒等于
    ///    transform.position，看着"没动"，实际每次换帧内容都在跳 —— 这才是"贴图抖动"的真正观测）。
    /// </para>
    /// </summary>
    public static string PivotKnight() { return Pivot("Units", KnightDir); }

    /// <summary>同一断言作用于任一目录。</summary>
    public static string Pivot(string area, string dir)
    {
        var sb = new StringBuilder();
        var path = "Sprites/" + area + "/" + dir;

        sb.Append("=== ① 导入态（= 修复前的行为：逐帧 pivot = 各自裁剪框中心）===\n");
        var raw = Resources.LoadAll<Sprite>(path);
        DumpAnchors(sb, raw, "asImported");

        // ② **直接对重建函数断言**（编辑模式也能跑，不必进 Play）：把 Resources 原始帧交给
        //    `SpriteBank.UnifyCanvasAnchor`，看它是否把每帧的纹理锚点统一到并集中心。
        sb.Append("=== ② SpriteBank.UnifyCanvasAnchor（= 工程里逐帧单位走的那条路）===\n");
        if (raw == null || raw.Length == 0)
        {
            sb.Append("  Resources 取不到帧，跳过\n");
        }
        else
        {
            var rebuilt = CR.View.SpriteBank.UnifyCanvasAnchor((Sprite[])raw.Clone(), "probe|" + path);
            DumpAnchors(sb, rebuilt, "unified");
            // 名字前缀 `gen_` 是"确实重建过"的判据（`ClearCache` 也只销毁这个前缀的）
            var gen = 0;
            for (var i = 0; i < rebuilt.Length; i++)
                if (rebuilt[i] != null && rebuilt[i].name.StartsWith(CR.View.SpriteBank.GeneratedSpritePrefix)) gen++;
            sb.Append("  >> 重建出来的 Sprite 数（名字带 `")
              .Append(CR.View.SpriteBank.GeneratedSpritePrefix).Append("` 前缀）= ").Append(gen)
              .Append(" / ").Append(rebuilt.Length).Append('\n');
        }

        // ③ 工程真实路径（`Game.Res` 取帧 + 模式选择）——只在 Play 模式下才是非空
        sb.Append("=== ③ 工程真实路径（`Game.Res`；编辑模式下为空是正常的）===\n");
        var frames = LoadViaSpriteBank(path, out var how);
        sb.Append("loadVia=").Append(how).Append(" count=").Append(frames == null ? -1 : frames.Length).Append('\n');
        if (frames != null && frames.Length > 0) DumpAnchors(sb, frames, "viaSpriteBank");
        return sb.ToString();
    }

    /// <summary>打印若干帧的"纹理锚点 + bounds.center"，并给出锚点是否共享的结论。</summary>
    private static void DumpAnchors(StringBuilder sb, Sprite[] frames, string tag)
    {
        if (frames == null || frames.Length == 0) { sb.Append("  (空)\n"); return; }
        var pick = new List<int>();
        for (var i = 0; i < 5 && i < frames.Length; i++) pick.Add(i);
        for (var i = Mathf.Max(5, frames.Length - 5); i < frames.Length; i++) pick.Add(i);

        var shared = true; float ax0 = 0f, ay0 = 0f; var first = true;
        foreach (var i in pick)
        {
            var s = frames[i];
            if (s == null) { sb.Append("  [").Append(i).Append("] null\n"); continue; }
            // sprite.pivot 是"相对 rect 的像素"⇒ 纹理坐标 = rect.xy + pivot
            var ax = s.rect.x + s.pivot.x;
            var ay = s.rect.y + s.pivot.y;
            if (first) { ax0 = ax; ay0 = ay; first = false; }
            else if (Mathf.Abs(ax - ax0) > 0.001f || Mathf.Abs(ay - ay0) > 0.001f) shared = false;
            sb.Append("  ").Append(tag).Append(" idx=").Append(i.ToString().PadLeft(3))
              .Append(" name=").Append(s.name.PadRight(14))
              .Append(" rect=").Append(Format(s.rect))
              .Append(" pivotPx=").Append(Format2(s.pivot))
              .Append(" texAnchor=").Append(Format2(new Vector2(ax, ay)))
              .Append(" boundsCenter=").Append(Format2((Vector2)s.bounds.center))
              .Append('\n');
        }
        sb.Append("  >> 纹理锚点是否共享(10 帧)=").Append(shared)
          .Append("  anchor=(").Append(ax0.ToString("F1")).Append(',').Append(ay0.ToString("F1")).Append(")\n");
    }

    // ───────────── B2. 插值时钟的**离线预演**（不依赖 Play，改一次算法先在这里过一遍） ─────────────
    //
    // ⛔⛔ 历史资产，已不代表运行时（2026-09-22 CR-T4 标注）⛔⛔
    // ① 本方法复刻的是**旧算法**：渲染时钟被 `SteerRate` 连续调速，且**插值窗口绑在"包到达"上**
    //    （`OnSnapshot` 每收一帧就把 `_prevIndex/_curIndex` 换成最新一对）—— 于是"换窗那一帧"要交付
    //    窗口末端剩下的任意份额（实测 `t` 最高只到 0.707，最快帧 2.33× 均值）= 10 Hz 速度脉冲，
    //    也就是用户第二次报的"怪物还是抖动"的根因。
    // ② 自 2026-09-22（CR-T4 修复）起运行时已改为：**稳态速率恒 1** + `SelectWindow()` **按渲染时钟**
    //    从 4 格快照历史里选窗口 + 渲染落后 2 个快照间隔（`RenderLagIntervals`）。所以本方法的读数
    //    **不再等于线上行为**，别拿它当"当前算法"的判据（本方法里的 `hasPrev/tMin/tMax` 只描述旧口径）。
    // ③ 当前的权威离线 A/B 入口 = **离线仿真**的 `lookup` 分支（对拍 `current` /
    //    `rate1`）：它用**实机记录的真实帧节拍**驱动（`--dt-file <FlowProbe 的 TSV>`），
    //    裁决数是"同兵种干净直线行进窗口的速度 cv 与 max/min"。实测量级：`current` cv 0.2382、
    //    `rate1`（只固定速率）cv 0.3713（**更差**，据此证伪"速率微调是主因"）、`lookup` cv 0.0000。
    // ⛔ 不删本方法：它保留的是"**旧算法为什么错**"的可复算证据（B2 这一层的两次踩坑复盘）。
    // ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 离线预演插值时钟：用**纯算术**复刻 `BattleViewRoot` 的时钟推进（只调用它的纯函数
    /// `SteerRate`），模拟"快照每 100ms 到、渲染每 16.7ms 一帧、单位匀动"，然后把
    /// t 与位置逐帧打出来。
    /// <para>
    /// ⛔ <b>它复刻的是旧算法，已不代表运行时</b>（见本节顶部横幅）：权威离线 A/B 请用
    /// 离线仿真的 `lookup` 分支。
    /// </para>
    /// <para>
    /// <b>为什么值得有这一步</b>：本片在这条链上连着踩了两次，**两次都是靠"进 Play 采一次数据"
    /// 才发现的**（① 只夹取不追赶 ⇒ 落后 2000ms、t 恒 0，单位冻住；② 落后超过一个间隔 ⇒ t 只走到 0.4
    /// 就被推走，退化成 10 Hz 台阶）。这一层算术完全可以在**编辑模式、零成本**先跑一遍：
    /// 判据 = ① 每帧位移严格 &gt; 0（不是台阶）② 每个间隔内 t 从 ~0 走到 ~1（有插值）
    /// ③ 位置单调不回跳。
    /// </para>
    /// </summary>
    public static string SimulateController()
    {
        var sb = new StringBuilder();
        const float FrameMs = 16.6667f;   // 60 FPS
        const float SnapMs = 100f;        // 服务端 10 Hz
        var tLag = CR.View.BattleViewRoot.TargetLagMs;

        // 时钟状态（与运行时代码同一套算术：clock = base + (real - baseReal) * rate）
        float baseMs = float.NaN, baseReal = 0f, rate = 1f, clock = 0f;
        float prev = 0f, curr = 0f, arrivalReal = 0f;
        var hasPrev = false;
        var nextSnap = SnapMs;            // server_ms 与到达时刻同值（服务端按固定 10Hz 打时间戳）
        var pos = 0f;                     // 渲染位置（单位匀动：1 秒走 1 格）
        float lastPos = float.NaN;

        sb.Append("frame  realMs  currMs  clockMs   err    rate     t      pos     dPos\n");
        var zeroSteps = 0; var jumps = 0; var tMin = 9f; var tMax = -9f;
        for (var i = 0; i < 60; i++)
        {
            var real = i * FrameMs;

            if (real + 1e-4f >= nextSnap)
            {
                prev = curr;
                hasPrev = true;
                curr = nextSnap;
                arrivalReal = real;
                nextSnap += SnapMs;
                if (float.IsNaN(baseMs)) { baseMs = curr - tLag; baseReal = real; rate = 1f; }
            }
            if (float.IsNaN(baseMs)) { sb.Append("  (还未收到快照)\n"); continue; }

            clock = baseMs + (real - baseReal) * rate;
            // 连续的服务端时间推定（与运行时同一条算式）
            var serverNow = curr + (real - arrivalReal);
            var target = serverNow - tLag;
            var err = target - clock;
            var r = CR.View.BattleViewRoot.SteerRate(err);
            if (!Mathf.Approximately(r, rate)) { baseMs = clock; baseReal = real; rate = r; clock = baseMs; }
            if (clock > curr) clock = curr;

            var span = Mathf.Max(1f, curr - prev);
            var t = hasPrev ? Mathf.Clamp01((clock - prev) / span) : 1f;
            pos = hasPrev ? Mathf.Lerp(unitPos(prev), unitPos(curr), t) : unitPos(curr);

            var dPos = float.IsNaN(lastPos) ? 0f : pos - lastPos;
            // i > 8：跳过"还没有快照"的预热帧（那时 pos 恒为 0，不构成台阶）
            if (i > 8)
            {
                if (dPos <= 1e-6f) zeroSteps++;
                if (dPos < -1e-6f) jumps++;
                tMin = Mathf.Min(tMin, t); tMax = Mathf.Max(tMax, t);
            }
            lastPos = pos;
            sb.Append(i.ToString().PadLeft(5)).Append(' ')
              .Append(real.ToString("F1").PadLeft(7)).Append(' ')
              .Append(curr.ToString("F0").PadLeft(7)).Append(' ')
              .Append(clock.ToString("F1").PadLeft(8)).Append(' ')
              .Append(err.ToString("F1").PadLeft(6)).Append(' ')
              .Append(rate.ToString("F3").PadLeft(6)).Append(' ')
              .Append(t.ToString("F3").PadLeft(7)).Append(' ')
              .Append(pos.ToString("F4").PadLeft(8)).Append(' ')
              .Append(dPos.ToString("F5").PadLeft(9)).Append('\n');
        }

        sb.Append("\n判据（预热后 51 帧）：零位移帧=").Append(zeroSteps)
          .Append("（必须 0 —— 否则就是 10 Hz 台阶）  回跳帧=").Append(jumps)
          .Append("（必须 0）  t 范围=").Append(tMin.ToString("F3")).Append("..").Append(tMax.ToString("F3"))
          .Append("（必须覆盖到 ~1，否则没有真正插值）\n");
        return sb.ToString();
    }

    /// <summary>模拟里的单位轨迹：1 秒 1 格（与 server_ms 同单位）。</summary>
    private static float unitPos(float serverMs) { return serverMs / 1000f; }

    // ───────────────────────── D. 进对局（驱动链） ─────────────────────────

    /// <summary>
    /// 从主菜单进人机对战：登录（若需）→ 创角（若需）→ 主菜单 → 人机对战 → 等竞技场画面就绪。
    /// 每个等待都有上限和明确文案（⛔ 不静默）。
    /// </summary>
    public static async Task<string> GoAiBattle()
    {
        var sb = new StringBuilder();
        sb.Append("Unpause=").Append(Unpause()).Append('\n');

        sb.Append(await LoginStep());
        sb.Append(await MenuStep());
        sb.Append(await BattleStep());
        return sb.ToString();
    }

    /// <summary>
    /// 驱动链**第 1 步**：登录（必要时创角），等离开 `Login` 站点。
    /// ⚠️ 为什么拆成三步：Pipeline 对**单条命令**有 30 s 硬上限（实测
    /// `Pipeline command 'run_script' timed out after 30000ms`，`--timeout_ms` 抬不动它），
    /// 而"整条登录→主菜单→人机→首帧快照"的等待预算加起来会超 ⇒ 每步各自一次调用。
    /// </summary>
    public static async Task<string> LoginStep()
    {
        var sb = new StringBuilder();
        sb.Append("Unpause=").Append(Unpause()).Append('\n');
        var ok = await WaitUntil(() => CloverEngine.Game.Fsm != null, 200, "Game.Fsm");
        sb.Append("wait Fsm: ").Append(ok).Append('\n');
        if (!ok) return sb.ToString();

        if (CloverEngine.Game.Fsm.Current == "Login")
            sb.Append("Click(LoginButton)=").Append(Click("LoginButton")).Append('\n');

        var waitNick = await WaitUntil(() => CloverEngine.Game.Fsm.Current != "Login", 300, "leave Login");
        sb.Append("wait leaveLogin: ").Append(waitNick).Append(" fsm=").Append(CloverEngine.Game.Fsm.Current).Append('\n');
        if (CloverEngine.Game.Fsm.Current == "Nickname" || FindPanel("NicknamePanel") != null)
        {
            sb.Append("Fill(NicknameInput)=").Append(Fill("NicknameInput", "克洛弗")).Append('\n');
            sb.Append("Click(SubmitButton)=").Append(Click("SubmitButton")).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>驱动链**第 2 步**：等主菜单。</summary>
    public static async Task<string> MenuStep()
    {
        var sb = new StringBuilder();
        var atMenu = await WaitUntil(() => CloverEngine.Game.Fsm.Current == "MainMenu", 400, "MainMenu");
        sb.Append("wait MainMenu: ").Append(atMenu).Append(" fsm=").Append(CloverEngine.Game.Fsm.Current).Append('\n');
        return sb.ToString();
    }

    /// <summary>驱动链**第 3 步**：点人机对战，等竞技场就绪且已收到首帧快照。</summary>
    public static async Task<string> BattleStep()
    {
        var sb = new StringBuilder();
        sb.Append("Click(AiBattleButton)=").Append(Click("AiBattleButton")).Append('\n');
        var inBattle = await WaitUntil(
            () => CloverEngine.Game.Fsm.Current == "Battle"
                  && CR.View.BattleViewRoot.Instance != null
                  && CR.View.BattleViewRoot.Instance.Ready
                  && CR.View.BattleViewRoot.Instance.SnapshotCount > 0,
            500, "Battle ready + 首帧快照");
        var bvr = CR.View.BattleViewRoot.Instance;
        sb.Append("wait BattleReady: ").Append(inBattle)
          .Append(" fsm=").Append(CloverEngine.Game.Fsm.Current)
          .Append(" Ready=").Append(bvr != null && bvr.Ready)
          .Append(" SnapshotCount=").Append(bvr != null ? bvr.SnapshotCount : -1)
          .Append(" LiveUnits=").Append(bvr != null ? bvr.LiveUnitCount : -1)
          .Append('\n');
        return sb.ToString();
    }

    /// <summary>当前对局态速览（采样前后各取一次，用来证明"确实在打同一局"）。</summary>
    public static string BattleState()
    {
        var bvr = CR.View.BattleViewRoot.Instance;
        var bm = CR.Module.Battle.BattleManager.Instance;
        var cam = Camera.main;
        var fsm = CloverEngine.Game.Fsm;
        return "fsm=" + (fsm == null ? "null" : fsm.Current)
             + " ready=" + (bvr != null && bvr.Ready)
             + " snaps=" + (bvr != null ? bvr.SnapshotCount : -1)
             + " units=" + (bvr != null ? bvr.LiveUnitCount : -1)
             + " myTeam=" + (bm != null ? bm.MyTeam : -1)
             + " room=" + (bm != null ? (bm.CurrentRoomId ?? "null") : "null")
             + " cam=" + (cam == null ? "null" : cam.name);
    }

    // ───────────────────────── E. 位置序列采样（判据） ─────────────────────────

    /// <summary>采样帧数上限（10 Hz 快照 ⇒ 600 帧足够覆盖约 60 个快照间隔）。</summary>
    public const int SampleFrames = 900;

    /// <summary>采样墙钟上限（秒）：防止低帧率机器上跑太久（帧数不够时按实际取，回报里写明）。</summary>
    public const float SampleWallSeconds = 20f;

    /// <summary>开局后第几帧尝试出一张牌（保证场上有"会走的单位"，不必等服务端 AI 出兵）。</summary>
    public const int PlayCardAtFrame = 5;

    /// <summary>
    /// **逐帧**采样：对场上的每个 `UnitView` 记录
    /// 帧号 / 时间 / 快照数 / 单位 id / 精灵目录 / 当前帧精灵名 / transform 世界坐标 /
    /// `SpriteRenderer.bounds.center`（世界） / 它的屏幕像素坐标。
    /// <para>
    /// 为什么三个都要：`transform.position` 只能反映**插值**（根因 1），
    /// `bounds.center` 才能反映**换帧时的贴图位移**（根因 4），屏幕坐标是"人眼看到的那一步"。
    /// </para>
    /// <para>产物：`TestDir` 下的 `T3-flow-&lt;tag&gt;.tsv`。</para>
    /// </summary>
    public static async Task<string> SampleFlow(string tag)
    {
        var sb = new StringBuilder();
        var path = System.IO.Path.Combine(TestDir, "T3-flow-" + tag + ".tsv");
        sb.Append("# FlowProbe.SampleFlow tag=").Append(tag).Append('\n');
        sb.Append("# ").Append(EnvBaseline().Replace('\n', '|')).Append('\n');
        sb.Append("# BattleState ").Append(BattleState()).Append('\n');
        // `lagMs` = BattleViewRoot.RenderLagMs（渲染落后服务端多少毫秒，判据：>=0 且不超过一个快照间隔），
        // `t` = 当前插值比例。这两列是把"插值健康度"直接量出来（根因 1 的运行时判据）。
        //
        // ★ `refScreenX/Y` = **美术固定参考点**（原始画布中心）的屏幕像素坐标 —— 这是"人眼看到的那一跳"。
        //   为什么不能用 `screenX/Y`（= bounds.center）：多 Sprite 导入下 bounds 由 rect 决定，
        //   而 pivot 恰好是**各自 rect 的中心** ⇒ `bounds.center` **恒等于** `transform.position`
        //   （实测 before 数据里 `bcY == worldY` 每一位都相同）⇒ 它**看不见**"换帧时整帧被重新居中"
        //   （根因 4）这一跳，只能看见插值（根因 1）。
        //   换算：worldOfTexturePoint(p) = pos + (p - (rect.xy + sprite.pivot)) / pixelsPerUnit。
        // CR-T4 追加三列（`bufLagMs` / `winStartMs` / `winEndMs`）：
        //  `bufLagMs`   = BattleViewRoot.BufferLagMs（渲染落后**最新快照**多少毫秒；修后应落在 100~200ms
        //                 = 1~2 个间隔，那多出来的一个间隔就是抖动缓冲）
        //  `winStart/End` = 正在渲染的那一对快照的时间戳（两者之差 = 真实服务端间隔，用来核对"窗口走满")
        // ⛔ 原有列语义一个字都没改（`lagMs` 仍是 RenderLagMs = 窗口末端 - 渲染时钟）。
        sb.Append("frame\ttimeMs\tdtMs\tsnaps\tinstId\tdir\tsprite\tworldX\tworldY\tbcX\tbcY\tscreenX\tscreenY\trefWorldX\trefWorldY\trefScreenX\trefScreenY\tlagMs\tt\trate\tbufLagMs\twinStartMs\twinEndMs\n");

        var units = new List<CR.View.UnitView>();
        var t0 = Time.realtimeSinceStartup;
        var played = false;
        var frames = 0;
        var lastUnits = 0;
        for (var i = 0; i < SampleFrames; i++)
        {
            if (Time.realtimeSinceStartup - t0 > SampleWallSeconds) break;
            if (!played && i == PlayCardAtFrame) { played = true; sb.Append("# playCard ").Append(TryPlayCard()).Append('\n'); }

            var cam = Camera.main;
            units.Clear();
            units.AddRange(Object.FindObjectsByType<CR.View.UnitView>(FindObjectsInactive.Exclude));
            // ⚠️ 稳定 id 用 `transform.GetSiblingIndex()`：`Object.GetInstanceID()` 在 Unity 6.6 已是
            //    **编译错误**（`Use GetEntityId instead`），而 siblingIndex 在整局里对同一个 UnitView 稳定
            //    （`Release()` 只 SetActive(false) 不重挂父节点，池复用也不改层级）。
            units.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            lastUnits = units.Count;

            var dt = Time.deltaTime * 1000f;
            var bvr = CR.View.BattleViewRoot.Instance;
            var snaps = bvr != null ? bvr.SnapshotCount : -1;
            var lag = bvr != null ? bvr.RenderLagMs : float.NaN;
            var ratio = bvr != null ? bvr.InterpRatio : float.NaN;
            var rate = bvr != null ? bvr.ClockRate : float.NaN;
            var bufLag = bvr != null ? bvr.BufferLagMs : float.NaN;
            var winA = bvr != null ? bvr.WindowStartMs : float.NaN;
            var winB = bvr != null ? bvr.WindowEndMs : float.NaN;
            foreach (var u in units)
            {
                if (u == null) continue;
                var r = u.GetComponent<SpriteRenderer>();
                var p = u.transform.position;
                var bc = r != null ? r.bounds.center : p;
                var sp = cam != null ? cam.WorldToScreenPoint(bc) : Vector3.zero;
                var spr = r != null && r.sprite != null ? r.sprite.name : "(null)";

                // 美术固定参考点 = **原始画布中心**（贴图中心），换算见表头注释。
                var refWorld = p;
                var refScreen = sp;
                if (r != null && r.sprite != null && r.sprite.texture != null)
                {
                    var s = r.sprite;
                    var ppu = s.pixelsPerUnit > 0.01f ? s.pixelsPerUnit : 100f;
                    var anchorTex = new Vector2(s.rect.x + s.pivot.x, s.rect.y + s.pivot.y); // 该帧的纹理锚点
                    var canvasCentre = new Vector2(s.texture.width * 0.5f, s.texture.height * 0.5f);
                    refWorld = new Vector2(p.x + (canvasCentre.x - anchorTex.x) / ppu,
                                           p.y + (canvasCentre.y - anchorTex.y) / ppu);
                    refScreen = cam != null ? cam.WorldToScreenPoint(refWorld) : Vector3.zero;
                }

                sb.Append(Time.frameCount).Append('\t')
                  .Append((Time.realtimeSinceStartup * 1000f).ToString("F1")).Append('\t')
                  .Append(dt.ToString("F2")).Append('\t').Append(snaps).Append('\t')
                  .Append(u.transform.GetSiblingIndex()).Append('\t').Append(u.SpriteDir).Append('\t').Append(spr).Append('\t')
                  .Append(p.x.ToString("F4")).Append('\t').Append(p.y.ToString("F4")).Append('\t')
                  .Append(bc.x.ToString("F4")).Append('\t').Append(bc.y.ToString("F4")).Append('\t')
                  .Append(sp.x.ToString("F1")).Append('\t').Append(sp.y.ToString("F1")).Append('\t')
                  .Append(refWorld.x.ToString("F4")).Append('\t').Append(refWorld.y.ToString("F4")).Append('\t')
                  .Append(refScreen.x.ToString("F1")).Append('\t').Append(refScreen.y.ToString("F1")).Append('\t')
                  .Append(lag.ToString("F1")).Append('\t').Append(ratio.ToString("F3")).Append('\t')
                  .Append(rate.ToString("F2")).Append('\t')
                  .Append(bufLag.ToString("F1")).Append('\t')
                  .Append(winA.ToString("F0")).Append('\t').Append(winB.ToString("F0")).Append('\n');
            }
            frames = i + 1;
            await Task.Yield();
        }
        // ⛔ 刻意用 `UTF8Encoding(false)`（**不写 BOM**）：带 BOM 时下游脚本要按 utf-8-sig 读，
        //    否则第一行 `#` 注释会被当成表头、真数据全被丢（实测踩过，0 行）。
        System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        var wall = Time.realtimeSinceStartup - t0;
        return "written=" + path + " sampledFrames=" + frames + " wallSec=" + wall.ToString("F1") + " unitsLast=" + lastUnits;
    }

    /// <summary>采样变体（无参入口，见文件头教训 2）：tag=before。</summary>
    public static Task<string> SampleFlowBefore() { return SampleFlow("before"); }

    /// <summary>采样变体：tag=after。</summary>
    public static Task<string> SampleFlowAfter() { return SampleFlow("after"); }

    /// <summary>
    /// 采样变体：tag=cr4-after（本轮"对局单位抖动"修复后的取证）。
    /// ⛔ 刻意**不复用** `after` 这个 tag：`T3-flow-after.tsv` 是**上一轮的产物**，
    /// 它在这轮里扮演"修改前基线"，绝不能被覆盖。
    /// </summary>
    public static Task<string> SampleFlowCr4() { return SampleFlow("cr4-after"); }

    /// <summary>
    /// 尽力出一张牌（**不经 HUD/UI**，直接走 C2S，与 `BattleManager.PlayCardAsync` 同一条消息）：
    /// 没有会走的单位时快照里就没有实体 ⇒ 采样为空（那就说不清了）。被服务端拒绝也照样留痕（`err` 原样打出来）。
    /// </summary>
    public static string TryPlayCard()
    {
        var bm = CR.Module.Battle.BattleManager.Instance;
        if (bm == null) return "BattleManager=null";
        var room = bm.CurrentRoomId;
        if (string.IsNullOrEmpty(room)) return "room=null";
        var start = bm.StartConfig;
        if (start == null) return "StartConfig=null";
        var hand = bm.MyTeam == 0 ? start.hand_a : start.hand_b;
        if (hand == null || hand.Length == 0) return "hand empty";
        var net = CloverEngine.Game.Net;
        if (net == null) return "Net=null";
        var card = hand[0];
        var t = net.Call<CR.Def.BattlePlayCardReply>(CR.Def.MsgDef.BattlePlayCard,
            new CR.Def.BattlePlayCardReq { room_id = room, card_id = card, x_milli = 9000, y_milli = 9000 });
        t.ContinueWith(r => Log(r.IsFaulted
            ? "出牌异常 " + (r.Exception == null ? "?" : r.Exception.GetBaseException().Message)
            : "出牌回包 ok=" + (r.Result != null ? r.Result.ok.ToString() : "null") + " err=" + (r.Result != null ? r.Result.err : "null")));
        return "sent card=" + card + " tile(9,9) team=" + bm.MyTeam;
    }

    // ───────────────────────────── 内部 ─────────────────────────────

    /// <summary>
    /// 走**工程真实路径**取帧：优先 2 参重载（本片新增的"统一锚点"模式，用反射找，避免探针在修复前编译不过），
    /// 找不到就退 1 参重载（= 修复前的行为）。`how` 回传走了哪条，写进证据。
    /// </summary>
    private static Sprite[] LoadViaSpriteBank(string path, out string how)
    {
        var t = typeof(CR.View.SpriteBank);
        // ⚠️ `SpritePivotMode` 是 `SpriteBank` 的**嵌套**类型：反射名是 `CR.View.SpriteBank+SpritePivotMode`
        //    （`+` 而不是 `.`）。第一版写的是 `CR.View.SpritePivotMode` ⇒ 恒为 null ⇒ 探针一直走 1 参重载，
        //    于是"改动后"的取证打印出来的其实还是导入态（自欺欺人）。用 GetNestedType 才稳。
        var modeType = t.GetNestedType("SpritePivotMode", System.Reflection.BindingFlags.Public)
                       ?? t.Assembly.GetType("CR.View.SpriteBank+SpritePivotMode")
                       ?? t.Assembly.GetType("CR.View.SpritePivotMode");
        if (modeType != null)
        {
            var m2 = t.GetMethod("LoadDir", new[] { typeof(string), modeType });
            if (m2 != null)
            {
                object mode;
                try { mode = System.Enum.Parse(modeType, "UnifiedCanvasAnchor"); }
                catch (System.Exception e) { how = "mode enum parse failed: " + e.Message; return System.Array.Empty<Sprite>(); }
                how = "LoadDir(path, UnifiedCanvasAnchor)";
                return (Sprite[])m2.Invoke(null, new[] { (object)path, mode });
            }
        }
        how = "LoadDir(path) [1 参 = 导入态，修复前的行为]";
        return CR.View.SpriteBank.LoadDir(path);
    }

    private static string Names(Sprite[] arr, int from, int count)
    {
        if (arr == null) return "(null)";
        var sb = new StringBuilder();
        for (var i = from; i < Mathf.Min(arr.Length, from + count); i++)
            sb.Append(arr[i] == null ? "(null)" : arr[i].name).Append(' ');
        return sb.ToString();
    }

    private static int DistinctIndex(Sprite[] arr, bool tailRule)
    {
        if (arr == null) return -1;
        var seen = new HashSet<int>();
        foreach (var s in arr)
        {
            if (s == null) continue;
            seen.Add(tailRule ? IndexTail(s.name) : IndexSecondLast(s.name));
        }
        return seen.Count;
    }

    private static int DistinctInts(int[] keys)
    {
        var seen = new HashSet<int>();
        foreach (var k in keys) seen.Add(k);
        return seen.Count;
    }

    /// <summary>
    /// 本片修复后的规则：取**倒数第二段数字**。
    /// `frame_000_0`→0（`000`）、`frame_7_1`→7、`frame_003`→3。
    /// </summary>
    public static int IndexSecondLast(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        var segs = name.Split('_');
        for (var i = segs.Length - 2; i >= 0; i--)          // 从倒数第二段往前找第一个"纯数字段"
        {
            int v;
            if (segs[i].Length > 0 && IsAllDigits(segs[i]) && int.TryParse(segs[i], out v)) return v;
        }
        // 只有一段数字（如 `frame_003`）时上面的循环找不到 ⇒ 最后再认**最后一段**
        if (segs.Length > 0 && IsAllDigits(segs[segs.Length - 1]))
        {
            int v;
            if (int.TryParse(segs[segs.Length - 1], out v)) return v;
        }
        return -1;
    }

    /// <summary>修复前的规则（取证用）：取名字**尾部**的数字串。</summary>
    public static int IndexTail(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        var digits = 0;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] >= '0' && name[i] <= '9') digits++;
            else break;
        }
        if (digits == 0) return -1;
        int v;
        return int.TryParse(name.Substring(name.Length - digits, digits), out v) ? v : -1;
    }

    private static bool IsAllDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (var i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
        return true;
    }

    private static string Format(Rect r)
    {
        return "(" + r.x.ToString("F0") + "," + r.y.ToString("F0") + "," + r.width.ToString("F0") + "," + r.height.ToString("F0") + ")";
    }

    private static string Format2(Vector2 v) { return "(" + v.x.ToString("F1") + "," + v.y.ToString("F1") + ")"; }

    private static async Task<bool> WaitUntil(System.Func<bool> cond, int maxFrames, string what)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (cond()) return true;
            await Task.Yield();
        }
        Log("等待超时：" + what);
        return false;
    }

    private static GameObject FindPanel(string typeName)
    {
        var root = GameObject.Find("[UI]");
        if (root == null) return null;
        foreach (Transform layer in root.transform)
            foreach (Transform panel in layer)
                if (panel.name == typeName || panel.name == typeName + "(Clone)") return panel.gameObject;
        return null;
    }

    private static string Click(string buttonName)
    {
        var root = GameObject.Find("[UI]");
        if (root == null) return "NO [UI] ROOT";
        foreach (var b in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            if (b.name != buttonName) continue;
            if (!b.interactable) return "FOUND but NOT interactable: " + buttonName;
            b.onClick.Invoke();
            return "CLICKED " + buttonName;
        }
        return "BUTTON NOT FOUND: " + buttonName;
    }

    private static string Fill(string fieldName, string text)
    {
        var root = GameObject.Find("[UI]");
        if (root == null) return "NO [UI] ROOT";
        foreach (var f in root.GetComponentsInChildren<UnityEngine.UI.InputField>(true))
        {
            if (f.name != fieldName) continue;
            f.text = text;
            return "FILLED " + fieldName + " = " + text;
        }
        return "FIELD NOT FOUND: " + fieldName;
    }
}
