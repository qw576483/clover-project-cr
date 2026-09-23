namespace CR
{
    /// <summary>
    /// 资源路径常量 —— **唯一定义处**（架构契约 D8/D11）。
    ///
    /// <para>
    /// <b>路径语义</b>：全部是 `Assets/Resources/` 下的**相对路径**，不带扩展名，直接喂给
    /// `Game.Res.LoadAsset&lt;Sprite&gt;(path, cb)` / `Game.Res.LoadAll&lt;Sprite&gt;(dir)` /
    /// `Game.Res.Preload(...)`。⛔ 业务脚本里出现裸资源路径字符串一律算错。
    /// </para>
    /// <para>
    /// <b>素材从哪来、为什么是这个布局</b>：全部复制自 `<项目根>/原版资源/cr-assets-png/assets/sc/`
    /// （A 本体 APK 解包；见 `策划/素材调研.md`）。源目录名（`chr_knight_out` 等）**原样保留**在路径里，
    /// 便于任何一条路径反查到源目录与 `策划/数值文档/unit_cs.txt` 的 `sprite_dir` 列。
    /// 目录内文件由 `<项目根>/.ai-tmp/hosts/copy_assets.py` **规范化重命名**为 `frame_NNN.png`
    /// （源文件名里的序号补零位数不统一：大目录 3 位 `_sprite_000`、小目录 2 位 `_sprite_00`；
    /// 统一成固定位数后这里才能用一条规则拼路径）。
    /// </para>
    /// <para>
    /// <b>帧数未知时用目录入口</b>：`Game.Res.LoadAll&lt;Sprite&gt;(UnitDir("chr_knight_out"))` 一次取全帧
    /// （返回长度 0 表示该路径下没有资源 / 后端不支持）。⛔ 不要把帧数硬编码进 View。
    /// </para>
    /// </summary>
    public static class ResPaths
    {
        /// <summary>素材根目录（`Assets/Resources/Sprites/`）。</summary>
        public const string SpritesRoot = "Sprites";

        // ───────────────────────── 单位（部队）逐帧精灵 ─────────────────────────

        /// <summary>部队精灵根：`Sprites/Units/`。子目录名 = `unit_cs.sprite_dir` / 参考规格 §6「素材目录」列。</summary>
        public const string UnitsRoot = SpritesRoot + "/Units";

        /// <summary>某单位精灵的目录（给 `LoadAll&lt;Sprite&gt;` 用）：`Sprites/Units/chr_knight_out`。</summary>
        public static string UnitDir(string spriteDir)
        {
            return UnitsRoot + "/" + spriteDir;
        }

        /// <summary>某单位精灵的第 <paramref name="frameIndex"/> 帧：`Sprites/Units/chr_knight_out/frame_000`。</summary>
        public static string UnitFrame(string spriteDir, int frameIndex)
        {
            return UnitDir(spriteDir) + "/" + FrameName(frameIndex);
        }

        // ───────────────────────── 建筑逐帧精灵 ─────────────────────────

        /// <summary>建筑精灵根：`Sprites/Buildings/`。</summary>
        public const string BuildingsRoot = SpritesRoot + "/Buildings";

        /// <summary>某建筑精灵的目录（给 `LoadAll&lt;Sprite&gt;` 用）：`Sprites/Buildings/building_mortar_out`。</summary>
        public static string BuildingDir(string spriteDir)
        {
            return BuildingsRoot + "/" + spriteDir;
        }

        /// <summary>某建筑精灵的第 <paramref name="frameIndex"/> 帧。</summary>
        public static string BuildingFrame(string spriteDir, int frameIndex)
        {
            return BuildingDir(spriteDir) + "/" + FrameName(frameIndex);
        }

        // ───────────────────────── 塔 / 竞技场 ─────────────────────────

        /// <summary>塔精灵源目录名（`building_tower_out`：国王塔 + 公主塔 + 摧毁态，214 帧）。</summary>
        public const string TowerSpriteDir = "building_tower_out";

        /// <summary>塔精灵根：`Sprites/Towers/building_tower_out`。</summary>
        public const string TowersRoot = SpritesRoot + "/Towers/" + TowerSpriteDir;

        /// <summary>塔的第 <paramref name="frameIndex"/> 帧。</summary>
        public static string TowerFrame(int frameIndex)
        {
            return TowersRoot + "/" + FrameName(frameIndex);
        }

        /// <summary>竞技场底图源目录名（`arena_training_out`，23 层/帧，画布 1090×1677）。</summary>
        public const string ArenaSpriteDir = "arena_training_out";

        /// <summary>竞技场素材根：`Sprites/Arenas/arena_training_out`。</summary>
        public const string ArenaRoot = SpritesRoot + "/Arenas/" + ArenaSpriteDir;

        /// <summary>竞技场第 <paramref name="frameIndex"/> 帧。</summary>
        public static string ArenaFrame(int frameIndex)
        {
            return ArenaRoot + "/" + FrameName(frameIndex);
        }

        // ───────────────────────── 卡面 ─────────────────────────

        /// <summary>
        /// 法术卡面源目录名（`ui_spells_out`：403×377 卡面画布，92 张）。
        /// ⚠️ 解包素材里**只有序号**、没有「序号 → 卡 key」的索引文件 ⇒ 本类只提供按序号取图，
        /// ⛔ **没有** `CardArtByKey`（那需要一份本项目自造的映射表 = 编造，见 agent-05 回报）。
        /// </summary>
        public const string SpellArtSpriteDir = "ui_spells_out";

        /// <summary>卡面图根：`Sprites/Cards/`（子目录 = 卡面素材目录名）。</summary>
        public const string CardsRoot = SpritesRoot + "/Cards";

        /// <summary>法术卡面根：`Sprites/Cards/ui_spells_out`。</summary>
        public const string SpellArtRoot = CardsRoot + "/" + SpellArtSpriteDir;

        /// <summary>哥布林飞桶法术自己的卡面目录（参考规格 §6.3：`ui_spells_out` + 本目录）。</summary>
        public const string GoblinBarrelArtSpriteDir = "spell_goblin_barrel_out";

        /// <summary>任意卡面目录（给 `LoadAll&lt;Sprite&gt;` 用）：`Sprites/Cards/ui_spells_out`。</summary>
        public static string CardArtDir(string spriteDir)
        {
            return CardsRoot + "/" + spriteDir;
        }

        /// <summary>任意卡面目录的第 <paramref name="artIndex"/> 张（序号 = 解包素材原始序号）。</summary>
        public static string CardArtFrame(string spriteDir, int artIndex)
        {
            return CardArtDir(spriteDir) + "/" + FrameName(artIndex);
        }

        /// <summary>法术卡面第 <paramref name="artIndex"/> 张（`ui_spells_out` 的便捷入口）。</summary>
        public static string SpellArtFrame(int artIndex)
        {
            return CardArtFrame(SpellArtSpriteDir, artIndex);
        }

        // ───────────────────────── UI 图 ─────────────────────────

        /// <summary>UI 图根：`Sprites/Ui/`。</summary>
        public const string UiRoot = SpritesRoot + "/Ui";

        /// <summary>
        /// 启动 / 登录 / 主菜单的整幅背景图（源：`loading_out/loading_sprite_24.png`，1152×1880）。
        /// <para>
        /// <b>为什么用它</b>：这是原版启动画面（loading）用的**整幅官方主视觉**，
        /// 是解包素材里少数几张"铺满整幅画布"的图（非透明像素占比 99.7%，其余 UI 目录里
        /// 绝大多数帧都是同一画布上的小图元）。启动 / 登录 / 主菜单共用它，是因为原版本身
        /// 这三个界面就共用同一张主视觉；本项目不额外造背景图。
        /// </para>
        /// </summary>
        public const string BootBackground = UiRoot + "/loading_bg";

        // ───────────────── 原版 UI 图元（面板 / 按钮 / 条 / 卡槽 / 图标）─────────────────
        //
        // <b>为什么有这一层</b>：A 的 UI 图集是一张张「小图元躺在超大透明画布上」的帧
        // （`ui_out` 画布 1663x2810，绝大多数帧的非透明包围盒只有几十像素）。哪一帧是什么、
        // 该当什么用，来自**逐帧看图**的全量识别（1342 帧，见 `策划/原版UI素材索引.md`）；
        // 本类只登记**确认要用**的那些帧，落地脚本 = `tools/probes/copy-ui-assets.py`
        // （⛔ 只复制被引用的那几个，不许整目录搬）。
        //
        // <b>路径形如</b> `Sprites/Ui/&lt;用途&gt;/&lt;源图集目录&gt;/frame_NNN`：
        //   · `&lt;用途&gt;` = <see cref="UiPanelsDir"/> / <see cref="UiButtonsDir"/> /
        //     <see cref="UiBarsDir"/> / <see cref="UiSlotsDir"/> / <see cref="UiIconsDir"/>
        //   · `&lt;源图集目录&gt;` = <see cref="UiSrcUi"/> / <see cref="UiSrcBattleEnd"/>，**必须保留** ——
        //     帧号只在**同一源图集内**唯一（`ui_out` 帧 226 与 `ui_battle_end_out` 帧 226 是两张不同的图）
        //   · `frame_NNN` 里的 NNN = **原版源帧号** ⇒ 每张图都能反查到索引文档里那一行

        /// <summary>用途目录：面板底框 / 纸张面板 / 面板切角。</summary>
        public const string UiPanelsDir = "Panels";

        /// <summary>用途目录：按钮底框与九宫格切角。</summary>
        public const string UiButtonsDir = "Buttons";

        /// <summary>用途目录：标题条 / 底条 / 圣水条。</summary>
        public const string UiBarsDir = "Bars";

        /// <summary>用途目录：卡槽底。</summary>
        public const string UiSlotsDir = "Slots";

        /// <summary>用途目录：图标（皇冠 / 宝箱 / 奖杯 / 货币 / 徽章 …）。</summary>
        public const string UiIconsDir = "Icons";

        /// <summary>源图集目录名：通用 UI（914 帧）。</summary>
        public const string UiSrcUi = "ui_out";

        /// <summary>源图集目录名：战斗结算 UI（240 帧）。</summary>
        public const string UiSrcBattleEnd = "ui_battle_end_out";

        /// <summary>
        /// 任意一张已落地的原版 UI 图元：`Sprites/Ui/Panels/ui_out/frame_806`。
        /// <paramref name="purposeDir"/> 取 <see cref="UiPanelsDir"/> 等；
        /// <paramref name="sourceDir"/> 取 <see cref="UiSrcUi"/> 等；
        /// <paramref name="sourceFrame"/> = 原版源帧号。
        /// </summary>
        public static string UiFrame(string purposeDir, string sourceDir, int sourceFrame)
        {
            return UiRoot + "/" + purposeDir + "/" + sourceDir + "/" + FrameName(sourceFrame);
        }

        // ── 面板（Panels）──────────────────────────────────────────────────────────

        // ⚠️ 下面 5 个 `PanelPaper*` 的判定 = **无法判定**（`策划/原版UI图元更正表.md` 第 1~5 行）：
        //    它们的 shape **不被任何 `0c` 动画引用**（= 原版 `.sc` 对这些帧无任何命名引用），
        //    所以「哪块是底 / 哪块是哪个方位的角」在本项目**没有权威依据**（9-slice 几何与 `08` 矩阵-`0c` 三元组
        //    的对应关系未解出，见 `策划/原版UI素材名称索引.md` §1.1）。
        //    ⇒ 本片**保留现状**（不改帧号、不改方位），逐条登记在 `.ai-tmp/test/Y2-允许差异.md` B1~B5。

        /// <summary>羊皮纸面板底 —— 米黄色纸方块（`ui_out` 806）。<b>判定 = 无法判定</b>（无命名引用）⇒ 保留现状，见 `.ai-tmp/test/Y2-允许差异.md` B1。</summary>
        public static string PanelPaper { get { return UiFrame(UiPanelsDir, UiSrcUi, 806); } }

        /// <summary>羊皮纸面板右上斜切角（`ui_out` 807）。<b>判定 = 无法判定</b>（无命名引用；方位不可判）⇒ 保留现状，见 `.ai-tmp/test/Y2-允许差异.md` B2。</summary>
        public static string PanelPaperCornerTr { get { return UiFrame(UiPanelsDir, UiSrcUi, 807); } }

        /// <summary>羊皮纸面板左上斜切角（`ui_out` 808）。<b>判定 = 无法判定</b>（无命名引用；方位不可判）⇒ 保留现状，见 `.ai-tmp/test/Y2-允许差异.md` B3。</summary>
        public static string PanelPaperCornerTl { get { return UiFrame(UiPanelsDir, UiSrcUi, 808); } }

        /// <summary>羊皮纸面板左下斜切角（`ui_out` 811）。<b>判定 = 无法判定</b>（无命名引用；方位不可判）⇒ 保留现状，见 `.ai-tmp/test/Y2-允许差异.md` B4。</summary>
        public static string PanelPaperCornerBl { get { return UiFrame(UiPanelsDir, UiSrcUi, 811); } }

        /// <summary>羊皮纸面板大切角（`ui_out` 812）。<b>判定 = 无法判定</b>（无命名引用；方位不可判）⇒ 保留现状，见 `.ai-tmp/test/Y2-允许差异.md` B5。</summary>
        public static string PanelPaperCornerBig { get { return UiFrame(UiPanelsDir, UiSrcUi, 812); } }

        /// <summary>白色描边圆角方框（空心 ⇒ 面板底框，`ui_out` 802）。</summary>
        public static string PanelFrameOutline { get { return UiFrame(UiPanelsDir, UiSrcUi, 802); } }

        /// <summary>
        /// 传说卡牌框光效 —— 原版**权威名** `card_frame_glow_legendary`（`ui_out` frame 532，145×188）。
        /// <para>
        /// <b>Y2 改名（不信原名）</b>：旧键名 <c>PanelFrameWhiteInner</c>（「通用面板内框」）是错的 ——
        /// 该 shape 在 `ui.sc` 的**显式导出表**里被 `card_frame_glow_legendary` 引用
        /// （帧 532，见 `策划/原版UI素材名称索引.md` 第 713 行；判定见 `策划/原版UI图元更正表.md` 第 7 行 = **不符**）。
        /// ⛔ 通用面板内框在原版源内**没有**对应帧 ⇒ 保留现状并登记（`.ai-tmp/test/Y2-允许差异.md` D1），
        /// 不得拿本帧冒充「通用面板内框」。
        /// </para>
        /// <para>路径里的 <see cref="UiPanelsDir"/> 是本项目的**归类标签**、不是原版权威信息（`.sc` 不含目录概念）
        /// ⇒ 本片只改键名与注释、不动已落地的帧文件（理由登记在允许差异 D1）。</para>
        /// </summary>
        public static string CardFrameGlowLegendary { get { return UiFrame(UiPanelsDir, UiSrcUi, 532); } }

        /// <summary>灰白描边圆角矩形框（空心；`ui_out` 592）。</summary>
        public static string PanelFrameGrey { get { return UiFrame(UiPanelsDir, UiSrcUi, 592); } }

        /// <summary>蓝底金边面板角（`ui_out` 505）。</summary>
        public static string PanelCornerBlueGold { get { return UiFrame(UiPanelsDir, UiSrcUi, 505); } }

        /// <summary>蓝底金边圆弧边（`ui_out` 506）。</summary>
        public static string PanelEdgeBlueGold { get { return UiFrame(UiPanelsDir, UiSrcUi, 506); } }

        /// <summary>深色圆角空框（横向；`ui_battle_end_out` 108）。</summary>
        public static string PanelFrameDark { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 108); } }

        // ── 按钮（Buttons）─────────────────────────────────────────────────────────

        /// <summary>
        /// 绿色圆角按钮件（`ui_out` frame 002；原版 `.sc` **无命名引用**）。
        /// <para>
        /// ⚠️ <b>键名与素材不符（判定 = 不符；本片**保留现状**并登记）</b>：素材实测是**绿色**圆角件，
        /// 而键名声明的是「白色圆角胶囊描边」—— 白色胶囊按钮底在原版源内**无对应**
        /// （`ui.sc` 的 `button_*` 只有 `button_timeline` / `button_small_orange` / `button_small_square_orange` /
        /// `button_share_deck` / `full_page_button_tab*`）。白色按钮底请用 <see cref="ButtonWhite"/>（`ui_out` 476）；
        /// 绿色主按钮底用 `ui_out` 002 / 004 / 005。
        /// </para>
        /// <para>
        /// <b>为什么不改</b>：更正表该条的「建议改用」只给了**别的用途**（绿色主按钮）的帧，对本键自己的用途
        /// 写的是「源内无对应」⇒ 按任务规矩（更正表未给替代帧 = 不自行挑帧）保留现状。
        /// 出处 `策划/原版UI图元更正表.md` 第 12 行 + §3；登记 `.ai-tmp/test/Y2-允许差异.md` D2。
        /// </para>
        /// </summary>
        public static string ButtonCapsule { get { return UiFrame(UiButtonsDir, UiSrcUi, 2); } }

        /// <summary>绿色按钮左上切角（`ui_out` 4）。</summary>
        public static string ButtonGreenCornerTl { get { return UiFrame(UiButtonsDir, UiSrcUi, 4); } }

        /// <summary>绿色按钮大圆角切角（`ui_out` 5）。</summary>
        public static string ButtonGreenCorner { get { return UiFrame(UiButtonsDir, UiSrcUi, 5); } }

        /// <summary>蓝色按钮切角（`ui_out` 165）。</summary>
        public static string ButtonBlueCorner { get { return UiFrame(UiButtonsDir, UiSrcUi, 165); } }

        /// <summary>蓝色按钮切角（变体；`ui_out` 166）。</summary>
        public static string ButtonBlueCornerAlt { get { return UiFrame(UiButtonsDir, UiSrcUi, 166); } }

        /// <summary>金黄色按钮底（渐变+立体边；`ui_out` 300）。</summary>
        public static string ButtonGold { get { return UiFrame(UiButtonsDir, UiSrcUi, 300); } }

        /// <summary>橙黄色按钮底（`ui_out` 357）。</summary>
        public static string ButtonOrange { get { return UiFrame(UiButtonsDir, UiSrcUi, 357); } }

        /// <summary>橙黄色按钮底（描边+底；`ui_out` 359）。</summary>
        public static string ButtonOrangeAlt { get { return UiFrame(UiButtonsDir, UiSrcUi, 359); } }

        /// <summary>白色按钮底（灰边；`ui_out` 476）。</summary>
        public static string ButtonWhite { get { return UiFrame(UiButtonsDir, UiSrcUi, 476); } }

        /// <summary>深蓝灰按钮底（`ui_out` 14）。</summary>
        public static string ButtonDarkGrey { get { return UiFrame(UiButtonsDir, UiSrcUi, 14); } }

        /// <summary>深蓝灰按钮底（变体；`ui_out` 15）。</summary>
        public static string ButtonDarkGreyAlt { get { return UiFrame(UiButtonsDir, UiSrcUi, 15); } }

        /// <summary>橙色宽扁按钮描边框（`ui_out` 551）。</summary>
        public static string ButtonOrangeWide { get { return UiFrame(UiButtonsDir, UiSrcUi, 551); } }

        // ── 条（Bars）───────────────────────────────────────────────────────────

        /// <summary>金色面板标题条底（`ui_out` 69）。</summary>
        public static string TitleBarGold { get { return UiFrame(UiBarsDir, UiSrcUi, 69); } }

        /// <summary>木纹面板标题条（`ui_out` 70）。</summary>
        public static string TitleBarWood { get { return UiFrame(UiBarsDir, UiSrcUi, 70); } }

        // ✅ 圣水条三件：**来源已定**（本节替换的是 Y2 那版「⛔ 不许动 + 来源未定」的注释，Y2 的登记 C1 已作废）。
        //    HUD 层**就在 `ui.sc` 里**，只是以 `HUD_*` export 内部的**具名子元件**出现（不是顶层 export）——
        //    判据 = `ui.sc` → `HUD_player`(clip 1091) → `elixir_bar`(clip 1080) 的 `0c` 记录 cnt2 子元件表自带原版命名。
        //    出处 `策划/战斗HUD素材索引.md` §1 结论表第 1 行 + §3.1（该文档 §1 明确推翻上一片「HUD 层不在这批 `.sc`」的结论）。
        //    ⇒ 原版对应：槽 = `bar_bg`(clip 902) / 填充 = `bar_body`+`ghost`(clip 904/905) / 描边端 = `bar_end`(clip 906)。
        //    ⛔ 原键指向的 `ui_out` 516/517/518 **不在** `elixir_bar` 的引用闭包内：516 不被任何 `0c` 动画引用，
        //       517/518 被 `spell_card_full` / `win_reward_bar` / `Menu_topLayer` 引用（= 卡牌条 / 奖励条，见索引 §1.1 表）
        //       ⇒ 那三帧不能当圣水条用，本片按权威出处换掉（换帧登记 `策划/对照表.md`）。

        /// <summary>
        /// 圣水条 槽底 —— 原版 `HUD_player` → `elixir_bar`(clip 1080) → 子元件 **`bar_bg`**（clip 902），
        /// `ui_out` **frame_155**（1×74；1 像素宽，原版靠放置矩阵横向拉伸）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.1（帧号 ↔ `12` 记录序号的可复跑链见该文档 §2）。</para>
        /// </summary>
        public static string ElixirBarTrack { get { return UiFrame(UiBarsDir, UiSrcUi, 155); } }

        /// <summary>
        /// 圣水条 条端 / 框 —— 原版 `elixir_bar`(clip 1080) → 子元件 **`bar_end`**（clip 906），
        /// `ui_out` **frame_158**（10×59）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.1。</para>
        /// </summary>
        public static string ElixirBarFrame { get { return UiFrame(UiBarsDir, UiSrcUi, 158); } }

        /// <summary>
        /// 圣水条 填充 —— 原版 `elixir_bar`(clip 1080) → 子元件 **`bar_body`** / **`ghost`**（clip 904 / 905，同帧），
        /// `ui_out` **frame_157**（59×1；品红细线，原版纵向拉伸）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.1。</para>
        /// </summary>
        public static string ElixirBarFill { get { return UiFrame(UiBarsDir, UiSrcUi, 157); } }

        /// <summary>
        /// 圣水条 槽底**同族件**（原版 `elixir_bar`(clip 1080) 子元件表里**未命名的裸子件**），
        /// `ui_out` **frame_156**（15×75）。
        /// <para>
        /// 与 <see cref="ElixirBarTrack"/>（`bar_bg` = frame_155，1×74 靠矩阵横向拉伸）**同族同用途**（圣水条槽身），
        /// 是 `.sc` 里唯一一条**没有名字**的 `elixir_bar` 子件（子元件表见 `策划/战斗HUD素材索引.md` §2
        /// 的 `⇒ elixir_bar → clip 1080 → 子元件表`）⇒ 本键名是本项目的**归类标签**、不是原版权威命名。
        /// </para>
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.1 表第 2 行 + §4 落地表。</para>
        /// </summary>
        public static string ElixirBarTrackAlt { get { return UiFrame(UiBarsDir, UiSrcUi, 156); } }

        /// <summary>
        /// 圣水条 10 格**刻度分隔**—— 原版 `elixir_bar`(clip 1080) 子元件 **`d1`…`d9`**（同属 clip 909，
        /// 原版在 clip 内把这一张复用了 **9 次**，10 格刻度 = 9 条分隔线），`ui_out` **frame_160**（1×1）。
        /// <para>
        /// 1×1 是**原版画法**（一条 1 像素线，靠放置矩阵拉成刻度线），不是裁错；落地的是原版像素。
        /// 出处 `策划/战斗HUD素材索引.md` §3.1 表 `d1`…`d9` 行 + §4 落地表。
        /// </para>
        /// </summary>
        public static string ElixirBarTick { get { return UiFrame(UiBarsDir, UiSrcUi, 160); } }

        /// <summary>
        /// 圣水**需求条** 条身 —— 原版 `elixir_bar`(clip 1080) 子元件 **`elixirRequirementBar`**（clip 912，11 帧），
        /// `ui_out` **frame_161**（1 像素高，落地 69×1，靠矩阵横向拉伸）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.1 表 `elixirRequirementBar` 行 + §4 落地表。</para>
        /// </summary>
        public static string ElixirRequirementTrack { get { return UiFrame(UiBarsDir, UiSrcUi, 161); } }

        /// <summary>
        /// 圣水**需求条** 条端 —— 同属原版 `elixirRequirementBar`（clip 912），`ui_out` **frame_162**（14×69）。
        /// <para>
        /// 与 <see cref="ElixirRequirementTrack"/>（frame_161）**同一原版元件的前后两件**（条身 / 条端），
        /// 帧号本就不同、语义不同 ⇒ 两个键。出处 `策划/战斗HUD素材索引.md` §3.1 + §4 落地表。
        /// </para>
        /// </summary>
        public static string ElixirRequirementEnd { get { return UiFrame(UiBarsDir, UiSrcUi, 162); } }

        /// <summary>
        /// 金色圆角**方块**板（24×24；`ui_battle_end_out` frame 201）。
        /// <para>
        /// <b>Y2 改名</b>：旧键名 <c>BarGold</c>（「金色圆角**底条**（宽）」）是错的 —— 素材是 24×24 的方块，
        /// 不是宽底条（判定 = **不符**，`策划/原版UI图元更正表.md` 第 29 行；更正表的建议 = 改成本名）。
        /// </para>
        /// <para>
        /// ⚠️ 结算界面真正需要的「金色宽底条」**本片给不出帧**：更正表指向的 `win_reward_bar`
        /// 在 `ui_battle_end.sc` 里是**空 clip**（索引第 676 行：clip 4492，帧列表 = `—`）⇒ 无帧可复制。
        /// 需要金色宽底条时登记为本项目的**表现缺口**（`.ai-tmp/test/Y2-允许差异.md` D3），⛔ 不许拿本方块拉成宽条冒充。
        /// </para>
        /// </summary>
        public static string GoldSquarePlate { get { return UiFrame(UiBarsDir, UiSrcBattleEnd, 201); } }

        /// <summary>白色圆角底条（`ui_battle_end_out` 222）。</summary>
        public static string BarWhite { get { return UiFrame(UiBarsDir, UiSrcBattleEnd, 222); } }

        // ── 卡槽（Slots）─────────────────────────────────────────────────────────

        /// <summary>白色卡片底（卡槽底框；`ui_out` 43）。</summary>
        public static string SlotCard { get { return UiFrame(UiSlotsDir, UiSrcUi, 43); } }

        /// <summary>
        /// 聊天气泡（左下尖角）—— 原版**权威名** `battle_end_chat_selection_bubble`（`ui_out` frame 54，73×113）。
        /// <para>
        /// <b>Y2 改名（不信原名）</b>：旧键名 <c>SlotCardAlt</c>（「白色卡片底（变体）」）是错的 ——
        /// 帧 54 在 `ui.sc` 导出表里被 `battle_end_chat_selection_bubble` 引用（索引第 1005 行；判定 = **不符**，
        /// 更正表第 32 行）。按权威命名改为本名。
        /// ⛔「卡槽底变体」在原版源内**没有**对应帧 ⇒ 保留现状并登记（`.ai-tmp/test/Y2-允许差异.md` D4），
        /// 不得拿聊天气泡冒充卡槽底。
        /// </para>
        /// <para>路径里的 <see cref="UiSlotsDir"/> 是本项目的**归类标签**、不是原版权威信息 ⇒ 本片只改键名、不动帧文件（允许差异 D4）。</para>
        /// </summary>
        public static string ChatBubble { get { return UiFrame(UiSlotsDir, UiSrcUi, 54); } }

        /// <summary>白色卡片（无描边；`ui_out` 531）。</summary>
        public static string SlotCardPlain { get { return UiFrame(UiSlotsDir, UiSrcUi, 531); } }

        /// <summary>浅灰圆角小方块（卡槽角 / 小按钮底；`ui_out` 11）。</summary>
        public static string SlotCorner { get { return UiFrame(UiSlotsDir, UiSrcUi, 11); } }

        // ── 图标（Icons）─────────────────────────────────────────────────────────

        /// <summary>紫色圣水水滴（`ui_out` 99）。</summary>
        public static string IconElixirDrop { get { return UiFrame(UiIconsDir, UiSrcUi, 99); } }

        /// <summary>
        /// 圣水图标（**大**：圣水条左侧 / 回复提示）—— 原版两处引用同一帧：
        /// `elixir_bar`(clip 1080) → 子元件 **`elixirBarLeft`**（clip 1077）与 **`elixirRegen`**（clip 1014，40 帧，
        /// 兼作**圣水回复提示图标**），`ui_out` **frame_159**（94×115）。
        /// <para>
        /// 与 <see cref="IconElixirDrop"/>（frame_99，圣水**水滴**小图标）**不是同一张**：本键是圣水条左侧那颗大图标。
        /// 出处 `策划/战斗HUD素材索引.md` §3.1 表 `elixirBarLeft` 行 + §3.4 表 `elixirRegen` 行 + §4 落地表。
        /// </para>
        /// </summary>
        public static string IconElixirBarLeft { get { return UiFrame(UiIconsDir, UiSrcUi, 159); } }

        /// <summary>金色皇冠（`ui_out` 50）。</summary>
        public static string IconCrownGold { get { return UiFrame(UiIconsDir, UiSrcUi, 50); } }

        /// <summary>黑色皇冠剪影（大；`ui_out` 75）。</summary>
        public static string IconCrownBlack { get { return UiFrame(UiIconsDir, UiSrcUi, 75); } }

        /// <summary>
        /// 蓝底金皇冠 / 蓝色方 star —— 原版权威语义名 **`star1` / `star2` / `star3`**（`printScore_player`，clip 1024）
        /// 与 **`starPlayer`**（`HUD_rightMiddle`，clip 989），`ui_out` **frame_187**（120×98）。
        /// <para>
        /// 帧号**本来就对（差值 0，本片未换帧）**，本片补的是权威命名；出处 `策划/战斗HUD素材索引.md` §3.3。
        /// 战斗 HUD 内取冠数请优先用 <see cref="HudStarPlayer"/>（同帧的 HUD 语义键）；
        /// ⛔ 本键保留不动键名，以免破坏其它文件的既有引用。
        /// </para>
        /// </summary>
        public static string IconCrownBlueGem { get { return UiFrame(UiIconsDir, UiSrcUi, 187); } }

        /// <summary>
        /// 红底金皇冠 / 红色方 star —— 原版权威语义名 **`star1` / `star2` / `star3`**（`printScore_enemy`，clip 1029）
        /// 与 **`starEnemy`**（`HUD_rightMiddle`，clip 992），`ui_out` **frame_188**（120×98）。
        /// <para>帧号本来就对（差值 0，本片未换帧）；出处 `策划/战斗HUD素材索引.md` §3.3。</para>
        /// </summary>
        public static string IconCrownRedGem { get { return UiFrame(UiIconsDir, UiSrcUi, 188); } }

        /// <summary>金色五尖皇冠（宽；`ui_out` 878）。</summary>
        public static string IconCrownFivePoint { get { return UiFrame(UiIconsDir, UiSrcUi, 878); } }

        /// <summary>金色五尖皇冠（大、实心；`ui_out` 883）。</summary>
        public static string IconCrownBig { get { return UiFrame(UiIconsDir, UiSrcUi, 883); } }

        /// <summary>金色宝箱（开启，内有金币/宝石；`ui_out` 526）。</summary>
        public static string IconChestGoldOpen { get { return UiFrame(UiIconsDir, UiSrcUi, 526); } }

        /// <summary>木纹宝箱（闭合，金边金锁；`ui_out` 527）。</summary>
        public static string IconChestWoodClosed { get { return UiFrame(UiIconsDir, UiSrcUi, 527); } }

        /// <summary>金棕色宝箱（半开；`ui_out` 555）。</summary>
        public static string IconChestHalfOpen { get { return UiFrame(UiIconsDir, UiSrcUi, 555); } }

        /// <summary>放大镜（搜索图标；`ui_out` 279）。</summary>
        public static string IconSearch { get { return UiFrame(UiIconsDir, UiSrcUi, 279); } }

        /// <summary>绿色剑+盾牌徽章（攻击/装备；`ui_out` 280）。</summary>
        public static string IconAttackGear { get { return UiFrame(UiIconsDir, UiSrcUi, 280); } }

        /// <summary>
        /// 「新建锦标赛」图标（蓝底白「+」）—— 原版**权威名** `icon_tournament_create`（`ui_out` frame 281）。
        /// <para>
        /// <b>Y2 改名（不信原名）</b>：旧键名 <c>IconHeal</c>（「治疗 / 加血」）是错的 ——
        /// 帧 281 在 `ui.sc` 导出表里被 `icon_tournament_create` 引用（索引第 831 行；判定 = **不符**，
        /// 更正表第 47 行；该 export 共 4 帧 274/281/380/388，本键取 281 与更正表一致）。
        /// </para>
        /// <para>
        /// ⚠️ 「治疗图标」在原版源内**无对应**（`ui.sc` 里没有任何 heal 命名）⇒ 本项目**不存在**治疗图标图元，
        /// 登记 `.ai-tmp/test/Y2-允许差异.md` D5。确有展示需求时用 <see cref="IconPlus"/>（`ui_out` 521），
        /// ⛔ 不许再拿本帧当治疗图标。
        /// </para>
        /// </summary>
        public static string IconTournamentCreate { get { return UiFrame(UiIconsDir, UiSrcUi, 281); } }

        /// <summary>金黄色问号（未知/帮助；`ui_out` 292）。</summary>
        public static string IconQuestion { get { return UiFrame(UiIconsDir, UiSrcUi, 292); } }

        /// <summary>交叉双剑（交战/对战；`ui_out` 226）。</summary>
        public static string IconBattle { get { return UiFrame(UiIconsDir, UiSrcUi, 226); } }

        /// <summary>绿色向上箭头（升级；`ui_out` 519）。</summary>
        public static string IconArrowUp { get { return UiFrame(UiIconsDir, UiSrcUi, 519); } }

        /// <summary>绿色十字（加号；`ui_out` 521）。</summary>
        public static string IconPlus { get { return UiFrame(UiIconsDir, UiSrcUi, 521); } }

        /// <summary>灰色齿轮（设置；`ui_out` 563）。</summary>
        public static string IconGear { get { return UiFrame(UiIconsDir, UiSrcUi, 563); } }

        /// <summary>白色游戏手柄（操作方式；`ui_out` 570）。</summary>
        public static string IconGamepad { get { return UiFrame(UiIconsDir, UiSrcUi, 570); } }

        /// <summary>白色气泡对话（聊天；`ui_out` 572）。</summary>
        public static string IconChat { get { return UiFrame(UiIconsDir, UiSrcUi, 572); } }

        /// <summary>蓝灰六边形宝石（`ui_out` 597）。</summary>
        public static string IconGemBlue { get { return UiFrame(UiIconsDir, UiSrcUi, 597); } }

        /// <summary>金色奖杯（`ui_battle_end_out` 109）。</summary>
        public static string IconTrophy { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 109); } }

        /// <summary>金色奖杯+橄榄枝（结算；`ui_battle_end_out` 195）。</summary>
        public static string IconTrophyLaurel { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 195); } }

        /// <summary>金色圆奖章（蓝丝带；`ui_battle_end_out` 213）。</summary>
        public static string IconMedal { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 213); } }

        /// <summary>绿色六边形宝石（`ui_battle_end_out` 214）。</summary>
        public static string IconGemGreen { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 214); } }

        /// <summary>金色圆币（`ui_battle_end_out` 215）。</summary>
        public static string IconCoin { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 215); } }

        /// <summary>金币堆（`ui_battle_end_out` 216）。</summary>
        public static string IconCoinPile { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 216); } }

        /// <summary>灰色城堡（防御；`ui_battle_end_out` 223）。</summary>
        public static string IconCastle { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 223); } }

        /// <summary>蓝色盾牌（金色星徽；`ui_battle_end_out` 225）。</summary>
        public static string IconShieldBlue { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 225); } }

        /// <summary>蓝色旗帜（金城堡纹；`ui_battle_end_out` 226）。</summary>
        public static string IconBanner { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 226); } }

        /// <summary>蓝色法术书（`ui_battle_end_out` 205）。</summary>
        public static string IconSpellBook { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 205); } }

        // ── 战斗 HUD 图元（手牌槽 / 冠数 / 倒计时 / 暂停按钮）──────────────────────────
        //
        // <b>为什么单列一组</b>：这些图元的**用途**来自 `ui.sc` 里 `HUD_*` / `printScore_*` / `replay_HUD_*`
        // 容器的**子元件名**（`0c` 记录的 cnt2 子元件表自带原版命名），不是本项目看图猜的 ——
        // 判据链（`.sc` → export → clip → 子元件名 → `12` 记录序号 → `frame_NNN`）见
        // `策划/战斗HUD素材索引.md` §2，逐项明细见该文档 §3.2（手牌槽）/ §3.3（冠数）/ §3.4（倒计时）/ §3.5（暂停按钮）。
        // ⛔ 上一片「HUD 层不在这批 `.sc`」的结论已被该文档 §1 推翻（HUD 在 `ui.sc` 的 clip 子元件里）。
        // 每帧都已落地在 `Assets/Resources/Sprites/Ui/<用途>/ui_out/frame_NNN.png`（索引 §4 落地表）。

        /// <summary>
        /// 手牌槽底（战斗 HUD 4 个手牌位）—— 原版 `HUD_player`(clip 1091) → 子元件 **`slots`**（clip 1070），
        /// `ui_out` **frame_200**（96×137）。原版自己把同一 shape 在 clip 内放了 **4 次** ⇒ 一张图 × 4 个槽位。
        /// <para>出处 `策划/战斗HUD素材索引.md` §1 结论表第 2 行 + §3.2。</para>
        /// </summary>
        public static string HudHandSlot { get { return UiFrame(UiSlotsDir, UiSrcUi, 200); } }

        /// <summary>
        /// ⛔⛔ <b>本键不属于 2.1.5 的手牌框，当前**没有任何代码引用**（留档待用，⛔ 别接回 HUD）。</b>
        /// 它来自"另一版本/模式"的对局图观感（金框 + 卡顶双菱形紫帽）；主 agent 2026-09-23 裁定
        /// `20_对局_1080x1920.jpg` 不是同一版本、不作几何基线 ⇒ CR-T2c 的接线已撤销
        /// （2.1.5 的手牌框 = `HudHandSlot` / `ui_out` **200**）。素材文件保留：它是原版页面的 1:1 提取。
        ///
        /// <para>
        /// **手牌卡的黄金卡框**（原 CR-T2c 落地件）：`ui_out` **frame_547**，原生 <b>158×162</b>。
        ///
        /// <para>
        /// 出处：原版 `ui` 图集（`.sc` = `ui_v215.sc`）导出的 <c>ui_sprite_547.png</c>
        /// （页面级 1663×2810，该 sprite 的 alpha 包围盒 = <b>(798,2224)-(956,2386)</b> = 158×162）。
        /// 落地口径与既有 74 张 `Ui/**/ui_out/frame_NNN.png` 相同：**按 alpha 包围盒 1:1 裁切**
        /// （可复跑：`tools/probes/cr-t2c-land-frame.py`，自带"与源区域逐像素相同"自检）。
        /// </para>
        /// <para>
        /// <b>为什么是它</b>：`tools/probes/cr-t2c-frame-search.py` 扫了 `原版资源` 下**全部** 91 个
        /// `*_out` 目录共 <b>20931</b> 张 sprite（不是只扫 `ui_out`），按"卡尺寸比例 + 金色像素占比"过筛出
        /// 191 张候选，**唯一**金色卡板就是这张（金占比 0.61，边框环均色 (228,193,111)，
        /// 与原版 `20_对局` 卡框实测金 (244,244,130)/(198,174,42) 同族）。
        /// </para>
        /// <para>
        /// ⚠️ 本件是**卡框底板**（金板），原版卡面（`ui_spells_out` 帧）按量取的内缩压在其上 ⇒
        /// 金板露出的边就是"卡框"。原版卡顶那根**双菱形紫帽**未找到对应 sprite（同一次 20931 张扫描，
        /// 见回报的"仍未找到"一节），⛔ 未自绘。
        /// </para>
        /// </summary>
        public static string HudCardFrameGold { get { return UiFrame(UiSlotsDir, UiSrcUi, 547); } }

        /// <summary>
        /// Draft（选牌）卡槽底 —— 原版子元件 **`card_slots`**（clip 1099 → 1098），
        /// `ui_out` **frame_201**（124×151；原版放了 8 次）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.2。</para>
        /// </summary>
        public static string HudHandSlotDraft { get { return UiFrame(UiSlotsDir, UiSrcUi, 201); } }

        /// <summary>
        /// 冠数（我方）—— 原版 **`star1` / `star2` / `star3`**（`printScore_player`，clip 1024）与
        /// **`starPlayer`**（`HUD_rightMiddle`，clip 989），`ui_out` **frame_187**（120×98）。
        /// <para>
        /// 与 <see cref="IconCrownBlueGem"/> **同一帧**（只补 HUD 语义键、不改帧）；
        /// 同族**第二态** = `frame_197`，**已落地**（`策划/战斗HUD素材索引.md` §4 落地表 + 本片登记）
        /// ⇒ 用 <see cref="HudStarPlayerAlt"/>。
        /// </para>
        /// </summary>
        public static string HudStarPlayer { get { return UiFrame(UiIconsDir, UiSrcUi, 187); } }

        /// <summary>
        /// 冠数（我方）**第二态** —— 与 <see cref="HudStarPlayer"/>（frame_187）属**同一原版元件**
        /// `printScore_player`(clip 1024) 子元件 **`star1` / `star2` / `star3`**，同族两种状态中的**另一种**，
        /// `ui_out` **frame_197**（120×98）。
        /// <para>
        /// 出处 `策划/战斗HUD素材索引.md` §3.3 表第 1 行（`star1`/`star2`/`star3` 列 **187 / 197**，注明「两种状态」）+ §4 落地表。
        /// ⛔ 本键与 <see cref="HudStarPlayer"/> **帧不同（197 ≠ 187）**，是并列的两态、不是别名。
        /// </para>
        /// </summary>
        public static string HudStarPlayerAlt { get { return UiFrame(UiIconsDir, UiSrcUi, 197); } }

        /// <summary>
        /// 冠数（敌方）—— 原版 **`star1` / `star2` / `star3`**（`printScore_enemy`，clip 1029）与
        /// **`starEnemy`**（`HUD_rightMiddle`，clip 992），`ui_out` **frame_188**（120×98）。
        /// <para>
        /// 与 <see cref="IconCrownRedGem"/> 同一帧；同族**第二态** = `frame_198`，**已落地**
        /// ⇒ 用 <see cref="HudStarEnemyAlt"/>。
        /// </para>
        /// </summary>
        public static string HudStarEnemy { get { return UiFrame(UiIconsDir, UiSrcUi, 188); } }

        /// <summary>
        /// 冠数（敌方）**第二态** —— 与 <see cref="HudStarEnemy"/>（frame_188）属**同一原版元件**
        /// `printScore_enemy`(clip 1029) 子元件 **`star1` / `star2` / `star3`**，同族两种状态中的**另一种**，
        /// `ui_out` **frame_198**（120×98）。
        /// <para>
        /// 出处 `策划/战斗HUD素材索引.md` §3.3 表第 2 行（`star1`/`star2`/`star3` 列 **188 / 198**，注明「两种状态」）+ §4 落地表。
        /// ⛔ 与 <see cref="HudStarEnemy"/> **帧不同（198 ≠ 188）**，并列两态、不是别名。
        /// </para>
        /// </summary>
        public static string HudStarEnemyAlt { get { return UiFrame(UiIconsDir, UiSrcUi, 198); } }

        /// <summary>
        /// 冠数 / 比分 名条 —— 原版 = `printScore_player`(1026) / `printScore_enemy`(1031) 的**裸子件**
        /// （原版 `.sc` **未给它命名**），`ui_out` **frame_196**（247×56，白色名条）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.3 第 4 行。</para>
        /// </summary>
        public static string HudScoreNamePlate { get { return UiFrame(UiPanelsDir, UiSrcUi, 196); } }

        /// <summary>
        /// 倒计时 表盘 / 时钟图标 —— 原版 **`Clock_middle`**（clip 184，fps 60），
        /// `ui_out` **frame_042**（15×15）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.4。</para>
        /// </summary>
        public static string HudClockIcon { get { return UiFrame(UiIconsDir, UiSrcUi, 42); } }

        /// <summary>
        /// 倒计时 底板 —— 原版 `HUD_topRight`(clip 1017) 的**无名子件**（clip 1005），
        /// `ui_out` **frame_193**（212×124）。同族 `frame_177`（1×1 纯黑）未落地。
        /// <para>
        /// 出处 `策划/战斗HUD素材索引.md` §3.4。⚠️ 计时**数字**在原版里全是文本域（`timeLeft` clip 1007 /
        /// `print_endTimer` clip 1068），**无图元** ⇒ 本项目只能用文本 + 原版字体（索引 §3.4 / §7.2）。
        /// </para>
        /// </summary>
        public static string HudTopRightPlate { get { return UiFrame(UiPanelsDir, UiSrcUi, 193); } }

        /// <summary>
        /// 倒计时 底板**实心填充** —— 同族 `ui_out` **frame_177**（1×1 纯黑），`HudTopRightPlate`(193) 是**空心**圆角框，
        /// 原版 `HUD_topRight` 另有无名子件（clip 1005）供实心底。
        /// <para>
        /// ⚠️ AF1 补登记（键登记缺口）：`tools/probes/copy-ui-assets.py` 的台账行 `("ui_out", 177, "HudTimerPlateFill")`
        /// （AV2 加）**一直没在 ResPaths 登记**，而 `HudPanel.cs` 用**本地常量** `TimerPlateFill`
        /// 直接拼同一个路径 ⇒ `check-ui-keys.ps1` 的 C 项（台账 ↔ 注册表）报 `<= HudTimerPlateFill`。
        /// 本键与 `HudPanel.TimerPlateFill` 指向同一路径（逐字相同），登记后 C 项一致；面板侧改用本键属后续片。
        /// 盘上文件 = `client/Assets/Resources/Sprites/Ui/Panels/ui_out/frame_177.png`（AF1 `Test-Path` True）。
        /// </para>
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.4（AV2 引 索引：`177(1×1) / 193(212×124)`）。</para>
        /// </summary>
        public static string HudTimerPlateFill { get { return UiFrame(UiPanelsDir, UiSrcUi, 177); } }

        /// <summary>
        /// 暂停按钮 底板 —— 原版 **`play_pause_button`**（clip 948，由 `replay_HUD_left`(962) /
        /// `replay_HUD_left_landscape`(1134) 引用），`ui_out` **frame_163**（219×219）。
        /// <para>
        /// ⚠️ 原版唯一带 `pause` 命名的元件属**回放 HUD**；**战斗内**暂停在原版**没有独立命名元件**
        /// ⇒ 本键是最接近候选（**推断，非原版命名**，缺口见 `策划/战斗HUD素材索引.md` §1 第 2 条）。出处同文档 §3.5。
        /// </para>
        /// </summary>
        public static string HudPauseButtonPlate { get { return UiFrame(UiButtonsDir, UiSrcUi, 163); } }

        /// <summary>
        /// 暂停按钮 播放态图标（▶）—— 原版 `play_pause_button`(clip 948) 子件，`ui_out` **frame_170**（79×87）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.5。</para>
        /// </summary>
        public static string HudPauseIconPlay { get { return UiFrame(UiIconsDir, UiSrcUi, 170); } }

        /// <summary>
        /// 暂停按钮 暂停态图标（‖）—— 原版 `play_pause_button`(clip 948) 子件，`ui_out` **frame_171**（84×90）。
        /// <para>出处 `策划/战斗HUD素材索引.md` §3.5。</para>
        /// </summary>
        public static string HudPauseIconPause { get { return UiFrame(UiIconsDir, UiSrcUi, 171); } }

        /// <summary>
        /// 退出按钮 图标（**✕**）—— 原版 **`quit_button`**（clip 924，与 `play_pause_button` 同族：
        /// 两者共用同一块底板 <see cref="HudPauseButtonPlate"/> frame_163，只换图标），`ui_out` **frame_164**（80×81）。
        /// <para>
        /// ⚠️ 与暂停按钮同理：原版带 `quit` 命名的元件属**回放 HUD**（`replay_HUD_left*` 一系）；
        /// **战斗内**退出/投降在原版**没有独立命名元件**（缺口见 `策划/战斗HUD素材索引.md` §1 第 2 条），
        /// 本键是最接近候选（**推断，非原版命名**）。
        /// 出处 `策划/战斗HUD素材索引.md` §3.5 表 `quit_button` 行 + §4 落地表。
        /// </para>
        /// </summary>
        public static string IconQuitCross { get { return UiFrame(UiIconsDir, UiSrcUi, 164); } }

        // ── 原版战斗结算图元（`ui_battle_end_out`；AR2 全量逐帧辨认后由 AS2 登记并接线）──────────────
        //
        // <b>为什么单列一组</b>：这批帧原先只躺在盘上（AR2 落地、无人登记、也没有键）⇒ 本片按 AR2 的
        // 辨认结论登记（键名 + 路径 + 用途：`.ai-tmp/test/AR2-ResPaths建议.md`；
        // 逐帧辨认与 `.sc` 归属：`.ai-tmp/test/AR2-结算帧辨认.md` §2/§3）。
        // 用途的**权威出处 = 原版 `.sc` 的 export 名**（`battleEnd_loot_item_*` / `gold_reward` /
        // `touchdown_txt_blue|red`），⛔ 不是本项目看图猜的；键名本身是本项目的归类标签。
        // ⚠️ 本图集里**没有**头像外框 / 等级徽记（AR2 §3.3：240 帧逐页看完，无任何圆形/方形头像框，
        //    export 名里也没有 avatar / portrait / frame 语义）⇒ 不登记、不自绘顶替，
        //    如实登记在 `.ai-tmp/test/AS2-允许差异.md` A1。

        /// <summary>结算面板九宫格 左上圆角（`ui_battle_end_out` 002，38×40；几何推定见 AR2 §3.4）。</summary>
        public static string BattleEndBorderCornerLT { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 2); } }

        /// <summary>结算面板九宫格 右上圆角（`ui_battle_end_out` 003，38×39）。</summary>
        public static string BattleEndBorderCornerRT { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 3); } }

        /// <summary>结算面板九宫格 横边（`ui_battle_end_out` 004，115×40）。</summary>
        public static string BattleEndBorderEdgeH { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 4); } }

        /// <summary>结算面板九宫格 横边（较矮；`ui_battle_end_out` 005，115×38）。</summary>
        public static string BattleEndBorderEdgeHThin { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 5); } }

        /// <summary>结算面板九宫格 竖边（`ui_battle_end_out` 006，38×51）。</summary>
        public static string BattleEndBorderEdgeV { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 6); } }

        /// <summary>近黑圆角实心方块（`ui_battle_end_out` 009，132×132；浅蓝细描边）。</summary>
        public static string BattleEndPlateDarkSquare { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 9); } }

        /// <summary>
        /// **胜负字牌底板**（`ui_battle_end_out` 236，65×44 深色渐变圆角横条）。
        /// <para>
        /// 出处：`.sc` 里**唯一**同时被 `touchdown_txt_blue` 与 `touchdown_txt_red` 引用的帧
        /// ⇒ 它就是原版里托住那行"胜 / 负"字的底板（AR2-结算帧辨认.md §3.1）。
        /// </para>
        /// <para>
        /// ⚠️ **字不是贴图**：原版那两行字是**文本字段**（`.sc` 头 `TextFieldCount = 40`，
        /// 样例全部含字体名 `Supercell-Magic`）⇒ ⛔ 不许去找"胜利"贴图、⛔ 不许拿贴图拼字（AR2 §3.1）。
        /// </para>
        /// </summary>
        public static string BattleEndTextPlate { get { return UiFrame(UiPanelsDir, UiSrcBattleEnd, 236); } }

        /// <summary>
        /// 奖励格公共底块（`ui_battle_end_out` 212，149×181）—— 原版 7 个 `battleEnd_loot_item_*`
        /// export **同时**引用这一帧 ⇒ 它就是每个奖励格的格底（AR2-结算帧辨认.md §3.2）。
        /// </summary>
        public static string BattleEndRewardSlotBg { get { return UiFrame(UiSlotsDir, UiSrcBattleEnd, 212); } }

        /// <summary>宝箱白色勾形高光（`ui_battle_end_out` 218，26×29；export `battleEnd_loot_item_chest`）。</summary>
        public static string BattleEndChestGlowArc { get { return UiFrame(UiSlotsDir, UiSrcBattleEnd, 218); } }

        /// <summary>宝箱白色竖条（`ui_battle_end_out` 219，13×37；同上 export）。</summary>
        public static string BattleEndChestGlowBar { get { return UiFrame(UiSlotsDir, UiSrcBattleEnd, 219); } }

        /// <summary>宝箱白色细弧（`ui_battle_end_out` 220，26×29；同上 export）。</summary>
        public static string BattleEndChestGlowArcThin { get { return UiFrame(UiSlotsDir, UiSrcBattleEnd, 220); } }

        /// <summary>宝箱白色弧变体（`ui_battle_end_out` 221，26×29；同上 export）。</summary>
        public static string BattleEndChestGlowArcAlt { get { return UiFrame(UiSlotsDir, UiSrcBattleEnd, 221); } }

        /// <summary>挑战奖励白色卷轴（`ui_battle_end_out` 224，41×45；export `battleEnd_loot_item_challenge`）。</summary>
        public static string BattleEndRewardQuestScroll { get { return UiFrame(UiSlotsDir, UiSrcBattleEnd, 224); } }

        /// <summary>
        /// 王冠 蓝方起始帧（`ui_battle_end_out` 027，176×126；下缘**蓝色**饰带）。
        /// <para>
        /// 原版王冠是**两段 80 帧动画**：蓝方 027–106 / 红方 115–194（AR2-结算帧辨认.md §3.4）；
        /// 本键 = 蓝方那段的**起始帧**（本片只接静态帧，逐帧动画未接）。
        /// ⚠️ 240 帧里**没有** 2 冠 / 3 冠的堆叠帧 ⇒ N 冠 = 同一张**复制 N 次**，
        /// 如实登记在 `.ai-tmp/test/AS2-允许差异.md` A2。
        /// </para>
        /// </summary>
        public static string CrownBlue { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 27); } }

        /// <summary>王冠 红方起始帧（`ui_battle_end_out` 115，176×126；下缘**红色**饰带）。见 <see cref="CrownBlue"/>。</summary>
        public static string CrownRed { get { return UiFrame(UiIconsDir, UiSrcBattleEnd, 115); } }

        /// <summary>特效精灵根：`Sprites/Effects/`（原版解包 `effects_out`）。</summary>
        public const string EffectsRoot = SpritesRoot + "/Effects";

        /// <summary>用途目录：命中 / 受击闪光。</summary>
        public const string EffectHit = "Hit";

        /// <summary>用途目录：爆炸 / 塔毁。</summary>
        public const string EffectBlast = "Blast";

        /// <summary>用途目录：弹道 / 飞行物。</summary>
        public const string EffectArrow = "Arrow";

        /// <summary>命中闪光起始帧（原版 f050..f056，7 帧）。</summary>
        public const int EffectHitFirst = 50;

        /// <summary>命中闪光帧数。</summary>
        public const int EffectHitCount = 7;

        /// <summary>爆炸 / 塔毁起始帧（原版 f418..f427，10 帧）。</summary>
        public const int EffectBlastFirst = 418;

        /// <summary>爆炸帧数。</summary>
        public const int EffectBlastCount = 10;

        /// <summary>弹道 / 飞行物起始帧（原版 f440..f459，20 帧）。</summary>
        public const int EffectArrowFirst = 440;

        /// <summary>弹道帧数。</summary>
        public const int EffectArrowCount = 20;

        // ───────────────────────── 内部 ─────────────────────────

        /// <summary>
        /// 帧文件名规则：`frame_` + 至少 3 位十进制。
        /// ⛔ 与 `.ai-tmp/hosts/copy_assets.py` 的重命名规则**必须一致**（改一处必须同时改另一处），
        /// 否则路径全部落空、表现为"图一个都出不来"。
        /// </summary>
        public static string EffectDir(string use)
        {
            return EffectsRoot + "/" + use;
        }

        public static string EffectFrame(string use, int sourceFrame)
        {
            return EffectDir(use) + "/" + FrameName(sourceFrame);
        }

        private static string FrameName(int frameIndex)
        {
            return "frame_" + frameIndex.ToString("D3");
        }
    }
}
