namespace CR
{
    /// <summary>
    /// 音频路径常量 —— **唯一定义处**（架构契约 D8/D11；风格照 <see cref="ResPaths"/>）。
    ///
    /// <para>
    /// <b>路径语义</b>：全部是 <c>Assets/Resources/</c> 下的**相对路径**，不带扩展名。
    /// 业务**不直接用**它喂 <c>Game.Res</c>；音效播放只有 <c>Game.Sound.PlaySFX(clipName)</c>
    /// 一个入口（clover-engine 门面）；本类只提供两样东西：
    /// ① 每个用途的 <c>clipName</c>（= <c>PlaySFX</c> 的入参）；
    /// ② <see cref="SfxPath"/> 把 <c>clipName</c> 拼成完整资源路径，供"问一句在不在"
    /// （<c>Game.Res.Exists</c>）与日志使用。
    /// </para>
    /// <para>
    /// <b>为什么根前缀是 <c>Sound/SFX</c> 而不是别的</b>：引擎的音效播放 API 内部**硬编码**了这个前缀 ——
    /// <c>clover-client-unity-engine/Runtime/Presentation/Sound.cs</c> 的
    /// <c>PlaySFX</c>（第 329 行）/ <c>PlaySFXAt</c>（第 355 行）都是
    /// <c>Game.Res.LoadAsset&lt;AudioClip&gt;($"Sound/SFX/{clipName}", …)</c>；
    /// <c>PlayVoice</c>（第 381 行）用 <c>Sound/Voice/{clipName}</c>。该前缀写在 clover-engine 内部，本工程改不了，
    /// 所以落地的音频**必须**放在 <c>Resources/Sound/SFX/</c> 下才播得出声。
    /// </para>
    /// <para>
    /// <b>素材从哪来、为什么路径里保留源文件名</b>：6 个音效全部复制自
    /// <c>&lt;项目根&gt;/原版资源/cr-sfx/</c>（《皇室战争》原版音频整包，来源见该目录 <c>清单.md</c>）。
    /// 与 <see cref="ResPaths"/> 同一约定：**源文件名原样保留在路径里**（<c>&lt;用途&gt;/&lt;源文件名&gt;</c>），
    /// 于是任意一条路径都能反查到整包里的那一个文件与 <c>清单.md</c> 的选型理由。
    /// </para>
    /// </summary>
    public static class AudioPaths
    {
        /// <summary>
        /// 音效根前缀：<c>Sound/SFX</c>。
        /// ⚠️ 必须与 clover-engine 的 <c>Sound.cs:329</c> 的 <c>$"Sound/SFX/{clipName}"</c> <b>逐字一致</b>
        ///（引擎在内部拼前缀；这里再拼一次只是为了让 <see cref="SfxPath"/> 能给出完整路径）。
        /// </summary>
        public const string SfxRoot = "Sound/SFX";

        // ───────────────────────── 用途 → clipName ─────────────────────────
        //
        // 每个常量 = 引擎 PlaySFX 的入参（相对 SfxRoot 的路径，不带扩展名）。
        // 用途 ↔ 事件映射见 View/BattleAudioView.cs。

        /// <summary>出牌：己方出牌落地。</summary>
        public const string PlayCard = "PlayCard/summon_own_07";

        /// <summary>
        /// **非致死命中**（受击闪光的那一次）。
        /// <para>
        /// 触发 = **快照里某个实体的 `hp` 比上一帧低**（`View/BattleAudioView.OnSnapshot` 逐 id 比对）——
        /// 因为协议里**没有**独立的「命中」事件（`Def/ProtoDef.cs:194` 只有 0..5 六种 kind），
        /// 而 `kind==2` 只在**死亡**时发 ⇒ 只挂 `kind==2` 会让非致死命中**一声不响**。
        /// 死亡改用 <see cref="Death"/>。
        /// </para>
        /// </summary>
        public const string Hit = "Hit/boulder_impact_01";

        /// <summary>塔毁。</summary>
        public const string TowerDown = "TowerDown/building_explode_01";

        /// <summary>圣水满。</summary>
        public const string ElixirFull = "ElixirFull/get_elixir_02";

        /// <summary>对局胜利。</summary>
        public const string Win = "Win/scroll_win_02";

        /// <summary>对局失败。</summary>
        public const string Lose = "Lose/scroll_lose_01";

        // ─────────────── 其余对局音效槽 ───────────────
        //
        // <b>素材来源一律 = <c>&lt;项目根&gt;/原版资源/cr-sfx/</c>（原版整包，2953 个 ogg）</b>，
        // 按**文件名语义**选型；每个常量都写明源文件，可反查。⛔ 不拿无关音冒充、⛔ 不静音糊过去。

        /// <summary>
        /// 单位**死亡**（`kind==2` `EvDeath`）。
        /// <para>
        /// 源：<c>原版资源/cr-sfx/Cards/0_Special/npc_die_04.ogg</c> —— `Cards/0_Special/` 是原版**通用**音槽
        /// （与 <see cref="Hit"/> 同目录同语义层），`npc_die_*` 是通用 NPC 死亡音（被 `Archers` / `Bandit` /
        /// `Barbarians` / `Bats` … 多张卡复用 ⇒ 不是某张卡专属）。
        /// </para>
        /// <para>
        /// <b>为什么把命中与死亡拆成两个槽</b>：`kind==2` 在服务端语义里**只是死亡**
        /// （`combat.go:410-432`），非致死命中根本不会产生 `kind==2`（见 <see cref="Hit"/>）。
        /// </para>
        /// </summary>
        public const string Death = "Death/npc_die_04";

        /// <summary>
        /// **敌方**出牌（`kind==0` `EvPlayCard` 且 `team != my_team`）。
        /// <para>源：<c>Game/enemy_summon_01.ogg</c>（原版「敌方召唤」通用音，`Game/` 是通用对局音目录）。</para>
        /// <para>己方出牌仍走 <see cref="PlayCard"/>(`PlayCardRequest`)，两者互斥 ⇒ 不会双响。</para>
        /// </summary>
        public const string EnemySummon = "EnemySummon/enemy_summon_01";

        /// <summary>
        /// 塔激活（`kind==5` `EvTowerActivated`）。
        /// <para>源：<c>Towers/King Tower/king_activate_01.ogg</c>（原版**国王塔激活**音，语义逐字对上）。</para>
        /// </summary>
        public const string TowerActivate = "TowerActivate/king_activate_01";

        /// <summary>
        /// 平局（`BattleEndNotify.draw == true`）。
        /// <para>
        /// 源：<c>Music/Jingles/scroll_draw_01.ogg</c> —— 与 <see cref="Win"/>/<see cref="Lose"/> **同一目录同一组**
        /// （`scroll_win_02` / `scroll_lose_01` / `scroll_draw_01`，原版结算页三态 jingle）
        /// ⇒ 平局沿用同一组原版结算音。
        /// </para>
        /// </summary>
        public const string Draw = "Draw/scroll_draw_01";

        /// <summary>
        /// UI 按钮点击（**所有面板按钮的统一点** = <c>UI/CrUiStyle.ActionButton</c> / <c>CrUiStyle.Button</c>）。
        /// <para>源：<c>Menu/button_click_02.ogg</c>（原版通用按钮点击音；整包唯一的 `button_click_*`）。</para>
        /// </summary>
        public const string UiClick = "Ui/button_click_02";

        /// <summary>
        /// 卡牌**拖起**（HUD 手牌按下）。
        /// <para>源：<c>Game/grabcard_01.ogg</c>（原版「抓牌」音，`Game/` 通用对局音目录，语义逐字对上）。</para>
        /// </summary>
        public const string GrabCard = "GrabCard/grabcard_01";

        /// <summary>
        /// **放置被拒**：落点非法（抬手时本地预检不合法）或圣水不足（拖起时费用 &gt; 当前圣水）。
        /// <para>源：<c>Game/bad_drop_03.ogg</c>（原版「无效投放」音）。</para>
        /// </summary>
        public const string BadDrop = "BadDrop/bad_drop_03";

        /// <summary>
        /// 倒计时**最后 10 秒**的每秒滴答提示音。
        /// <para>源：<c>Game/deploy_timer_tick_01v4.ogg</c>（原版对局计时滴答音；`enemy_deploy_timer_tick_01` 是它的敌方版本，语义同一套）。</para>
        /// </summary>
        public const string CountdownTick = "CountdownTick/deploy_timer_tick_01v4";

        /// <summary>
        /// 把 clipName 拼成完整资源路径：<c>Sound/SFX/Hit/boulder_impact_01</c>。
        /// 用途：<c>Game.Res.Exists(AudioPaths.SfxPath(clip))</c> 探测与日志。
        /// ⛔ 不要拿它去 <c>Game.Sound.PlaySFX</c>（那个只收 <c>clipName</c>，会重复拼前缀）。
        /// </summary>
        public static string SfxPath(string clipName)
        {
            return SfxRoot + "/" + clipName;
        }

        // ───────────────────────── BGM（背景音乐）─────────────────────────

        /// <summary>
        /// BGM 根前缀：<c>Sound/BGM</c>。
        /// ⚠️ 必须与 clover-engine 的 <c>Runtime/Presentation/Sound.cs:263</c> 的
        /// <c>$"Sound/BGM/{clipName}"</c> <b>逐字一致</b>（引擎在内部拼前缀；这里再拼一次只是为了让
        /// <see cref="BgmPath"/> 能给出完整路径）。落地目录 = <c>Resources/Sound/BGM/</c>。
        /// </summary>
        public const string BgmRoot = "Sound/BGM";

        /// <summary>
        /// 菜单站点群（启动 / 登录 / 创角 / 主菜单 / 房间）的背景音乐。
        /// 源：<c>原版资源/cr-sfx/Music/Lobby/music_lobby_202606.ogg</c>（原版大厅 BGM）。
        /// </summary>
        public const string BgmMenu = "Menu/music_lobby_202606";

        /// <summary>
        /// 对局中的背景音乐（2 分钟对局循环）。
        /// 源：<c>原版资源/cr-sfx/Arenas/General/2min_loop_battle_01.ogg</c>。
        /// </summary>
        public const string BgmBattle = "Battle/2min_loop_battle_01";

        /// <summary>
        /// 结算页背景音乐（原版结算前循环音）。
        /// 源：<c>原版资源/cr-sfx/Music/Jingles/scroll_preresult_loop_01.ogg</c>。
        /// </summary>
        public const string BgmResult = "Result/scroll_preresult_loop_01";

        /// <summary>
        /// 把 BGM clipName 拼成完整资源路径：<c>Sound/BGM/Menu/music_lobby_202606</c>。
        /// 用途：<c>Game.Res.Exists(AudioPaths.BgmPath(clip))</c> 探测与日志。
        /// ⛔ 不要拿它去 <c>Game.Sound.PlayBGM</c>（那个只收 <c>clipName</c>，会重复拼前缀）。
        /// </summary>
        public static string BgmPath(string clipName)
        {
            return BgmRoot + "/" + clipName;
        }
    }
}
