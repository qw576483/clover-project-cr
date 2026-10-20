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

        /// <summary>
        /// 圣水进入 **2 倍段**（`elixir_phase_ms[0]` 走完那一刻）。
        /// <para>源：<c>Game/roayle_2x_elixir_vo_01.ogg</c> —— 原版对局播报「双倍圣水」的 VO，与
        /// <see cref="ElixirRate3x"/> 同目录同组；原版 `battle_timelines.json` 的 `elixir_notify_change`
        /// 三段均为 `true`（= 每次倍率变化都要提示）。</para>
        /// </summary>
        public const string ElixirRate2x = "ElixirRate2x/roayle_2x_elixir_vo_01";

        /// <summary>
        /// 圣水进入 **3 倍段**（`elixir_phase_ms[0] + elixir_phase_ms[1]` 走完那一刻）。
        /// <para>源：<c>Game/roayle_3x_elixir_vo_01.ogg</c>（原版「三倍圣水」播报 VO）。</para>
        /// </summary>
        public const string ElixirRate3x = "ElixirRate3x/roayle_3x_elixir_vo_01";

        /// <summary>
        /// 进入**加时赛**（快照 `phase` 由 `0` 变 `1`）。
        /// <para>源：<c>Game/extratime_jingle_02.ogg</c>（原版加时开场 jingle；`extratime_*` 是整包唯一的加时音）。</para>
        /// </summary>
        public const string OvertimeJingle = "OvertimeJingle/extratime_jingle_02";

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

        // ───────────────────────── 塔的开火音 ─────────────────────────
        //
        // 源 = <c>原版资源/cr-sfx/Towers/&lt;塔&gt;/&lt;源文件名&gt;.ogg</c>，落地 = <c>Resources/Sound/SFX/Towers/</c>。
        // 塔只有开火音（原版整包的 `Towers/` 下没有独立的「塔被命中」音）⇒ 塔受击沿用 <see cref="Hit"/>。

        /// <summary>公主塔开火。源：<c>Towers/Archer Tower/archer_tower_fire_05.ogg</c>（整包唯一的公主塔开火音）。</summary>
        public const string TowerPrincessFire = "Towers/archer_tower_fire_05";

        /// <summary>国王塔开火。源：<c>Towers/King Tower/king_fire_06.ogg</c>（`king_activate_01` 是激活音，各有其用）。</summary>
        public const string TowerKingFire = "Towers/king_fire_06";

        /// <summary>
        /// 逐卡攻击 / 命中音：卡 key（= 服务端 <c>CardInfo.key</c>，逐字等于 <c>策划/数值文档/card_cs.txt</c> 的 key 列）
        /// → <c>PlaySFX</c> 的 clipName。
        ///
        /// <para>
        /// 落地路径 = <c>Resources/Sound/SFX/Cards/&lt;卡 key&gt;/&lt;源文件名&gt;.ogg</c>，
        /// 每个常量都能按 <c>原版资源/cr-sfx/Cards/&lt;卡目录&gt;/&lt;源文件名&gt;.ogg</c> 反查。
        /// 选型口径：远程卡取开火 / 施法音，近战卡取挥砍音；命中音取同卡目录的 hit / impact / explo，
        /// 目录里没有命中音的卡（minions / cannon / zap）返回 <c>false</c> ⇒ 调用方回落 <see cref="Hit"/>。
        /// </para>
        /// </summary>
        public static class Cards
        {
            /// <summary>骑士（Key `knight`）。近战挥砍 + 同组命中。</summary>
            public const string KnightAttack = "Cards/knight/card_hero_knight_atk_cast_a";
            public const string KnightHit = "Cards/knight/card_hero_knight_atk_hit_a";

            /// <summary>弓箭手（Key `archers`）。开火 + 箭矢命中。</summary>
            public const string ArchersAttack = "Cards/archers/archer_attack_07";
            public const string ArchersHit = "Cards/archers/arrow_hit_13";

            /// <summary>哥布林（Key `goblins`）。近战挥砍 + 通用兵命中（同目录）。</summary>
            public const string GoblinsAttack = "Cards/goblins/gob_attack_01v2";
            public const string GoblinsHit = "Cards/goblins/footman_attack_hit_01";

            /// <summary>哥布林投矛手（Key `spear-goblins`）。投矛 + 同目录命中。</summary>
            public const string SpearGoblinsAttack = "Cards/spear-goblins/spear_gob_atk_01";
            public const string SpearGoblinsHit = "Cards/spear-goblins/spear_gob_atk_hit_01";

            /// <summary>巨人（Key `giant`）。巨拳挥击 + 同组命中。</summary>
            public const string GiantAttack = "Cards/giant/giant_attack_swing_01";
            public const string GiantHit = "Cards/giant/card_hero_giant_atk_hit_a";

            /// <summary>亡灵（Key `minions`）。俯冲攻击音；目录里没有命中音 ⇒ <see cref="TryGetHit"/> 返回 false。</summary>
            public const string MinionsAttack = "Cards/minions/tinymite_attack_04";

            /// <summary>女巫（Key `witch`）。施法 + 同组命中。</summary>
            public const string WitchAttack = "Cards/witch/card_evolution_witch_attack";
            public const string WitchHit = "Cards/witch/card_evolution_witch_attack_hit";

            /// <summary>野蛮人（Key `barbarians`）。挥砍 + 同目录的砍击命中。</summary>
            public const string BarbariansAttack = "Cards/barbarians/barbarian_attack_02";
            public const string BarbariansHit = "Cards/barbarians/knight_attack_hack_01";

            /// <summary>骷髅兵（Key `skeletons`）。挥砍 + 通用兵命中（同目录）。</summary>
            public const string SkeletonsAttack = "Cards/skeletons/skeleton_atk_03";
            public const string SkeletonsHit = "Cards/skeletons/footman_attack_hit_01";

            /// <summary>瓦基丽武神（Key `valkyrie`）。旋斧 + 同目录命中。</summary>
            public const string ValkyrieAttack = "Cards/valkyrie/valkyrie_atk_04";
            public const string ValkyrieHit = "Cards/valkyrie/valkyrie_hit_01";

            /// <summary>炸弹兵（Key `bomber`）。投弹 + 同目录爆炸。</summary>
            public const string BomberAttack = "Cards/bomber/bomb_skeleton_atk_04";
            public const string BomberHit = "Cards/bomber/bomb_skeleton_01v3";

            /// <summary>火枪手（Key `musketeer`）。开枪 + 同目录弹着。</summary>
            public const string MusketeerAttack = "Cards/musketeer/musket_fire_02";
            public const string MusketeerHit = "Cards/musketeer/musket_impact_01";

            /// <summary>飞龙宝宝（Key `baby-dragon`）。火球 + 同组命中。</summary>
            public const string BabyDragonAttack = "Cards/baby-dragon/baby_dragon_fireball_08";
            public const string BabyDragonHit = "Cards/baby-dragon/card_evolution_baby_dragon_atk_hit";

            /// <summary>法师（Key `wizard`）。施法 + 同组命中。</summary>
            public const string WizardAttack = "Cards/wizard/card_hero_wizard_atk_cast_01";
            public const string WizardHit = "Cards/wizard/card_hero_wizard_atk_hit_01";

            /// <summary>迷你皮卡（Key `mini-pekka`）。挥砍 + 同目录命中。</summary>
            public const string MiniPekkaAttack = "Cards/mini-pekka/mini_pekka_atk_12";
            public const string MiniPekkaHit = "Cards/mini-pekka/mini_pekka_hit_03";

            /// <summary>野猪骑士（Key `hog-rider`）。挥锤 + 同目录冲击。</summary>
            public const string HogRiderAttack = "Cards/hog-rider/hogrider_atk_01";
            public const string HogRiderHit = "Cards/hog-rider/boulder_impact_01";

            /// <summary>加农炮（Key `cannon`）。开炮；目录里没有命中音 ⇒ <see cref="TryGetHit"/> 返回 false。</summary>
            public const string CannonAttack = "Cards/cannon/cannon_fire_03";

            /// <summary>火球（Key `fireball`）。发射 + 爆炸。</summary>
            public const string FireballAttack = "Cards/fireball/fire_ball_02";
            public const string FireballHit = "Cards/fireball/fire_ball_explo_02";

            /// <summary>万箭齐发（Key `arrows`）。弓弦 + 同目录箭矢落地。</summary>
            public const string ArrowsAttack = "Cards/arrows/archer_queen_attack_02";
            public const string ArrowsHit = "Cards/arrows/arrow_norm_impact_06";

            /// <summary>电击法术（Key `zap`）。放电；目录里没有命中音 ⇒ <see cref="TryGetHit"/> 返回 false。</summary>
            public const string ZapAttack = "Cards/zap/zap_02";

            /// <summary>取该卡的**攻击 / 施法**音。返回 false = key 不在本表（调用方须留痕，⛔ 不静默）。</summary>
            public static bool TryGetAttack(string cardKey, out string clipName)
            {
                switch (cardKey)
                {
                    case "knight": clipName = KnightAttack; return true;
                    case "archers": clipName = ArchersAttack; return true;
                    case "goblins": clipName = GoblinsAttack; return true;
                    case "spear-goblins": clipName = SpearGoblinsAttack; return true;
                    case "giant": clipName = GiantAttack; return true;
                    case "minions": clipName = MinionsAttack; return true;
                    case "witch": clipName = WitchAttack; return true;
                    case "barbarians": clipName = BarbariansAttack; return true;
                    case "skeletons": clipName = SkeletonsAttack; return true;
                    case "valkyrie": clipName = ValkyrieAttack; return true;
                    case "bomber": clipName = BomberAttack; return true;
                    case "musketeer": clipName = MusketeerAttack; return true;
                    case "baby-dragon": clipName = BabyDragonAttack; return true;
                    case "wizard": clipName = WizardAttack; return true;
                    case "mini-pekka": clipName = MiniPekkaAttack; return true;
                    case "hog-rider": clipName = HogRiderAttack; return true;
                    case "cannon": clipName = CannonAttack; return true;
                    case "fireball": clipName = FireballAttack; return true;
                    case "arrows": clipName = ArrowsAttack; return true;
                    case "zap": clipName = ZapAttack; return true;
                    default: clipName = null; return false;
                }
            }

            /// <summary>
            /// 取该卡的**命中**音（被打中时播）。返回 false = 该卡目录没有命中音
            /// （minions / cannon / zap）⇒ 调用方回落通用 <see cref="Hit"/>。
            /// </summary>
            public static bool TryGetHit(string cardKey, out string clipName)
            {
                switch (cardKey)
                {
                    case "knight": clipName = KnightHit; return true;
                    case "archers": clipName = ArchersHit; return true;
                    case "goblins": clipName = GoblinsHit; return true;
                    case "spear-goblins": clipName = SpearGoblinsHit; return true;
                    case "giant": clipName = GiantHit; return true;
                    case "witch": clipName = WitchHit; return true;
                    case "barbarians": clipName = BarbariansHit; return true;
                    case "skeletons": clipName = SkeletonsHit; return true;
                    case "valkyrie": clipName = ValkyrieHit; return true;
                    case "bomber": clipName = BomberHit; return true;
                    case "musketeer": clipName = MusketeerHit; return true;
                    case "baby-dragon": clipName = BabyDragonHit; return true;
                    case "wizard": clipName = WizardHit; return true;
                    case "mini-pekka": clipName = MiniPekkaHit; return true;
                    case "hog-rider": clipName = HogRiderHit; return true;
                    case "fireball": clipName = FireballHit; return true;
                    case "arrows": clipName = ArrowsHit; return true;
                    default: clipName = null; return false;
                }
            }
        }

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
