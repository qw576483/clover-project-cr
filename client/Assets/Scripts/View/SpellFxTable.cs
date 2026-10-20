using CR.Def;   // CardInfo（XML 文档里的 cref 用；本类的 switch 只吃字符串 key）

namespace CR.View
{
    /// <summary>
    /// 法术卡的**命中特效**查表（差异登记见 `策划/对照表.md` §D.3）：法术卡 key → 原版帧目录 + 起始帧 + 帧数。
    ///
    /// <para>
    /// <b>为什么需要这张表</b>：法术卡走 `EvPlayCard` 事件，但事件载荷里只有 `card_id`
    /// （→ <see cref="CR.Def.CardInfo"/> 的 `key` / `type`）与落点，**没有帧号** ——
    /// 帧号必须由客户端按卡 key 查到。表里的每一行都**逐条引了出处**（`策划/单位动画分组表.md`
    /// 的 `effects` 小节，该文件是生成物、不是手写的），⛔ 没有任何一帧是"挑"出来的。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不全用同一个特效</b>：若 10 张法术都放同一个通用爆闪，只是把"看不到"换成"看到的是错的"。
    /// 所以按卡给帧。
    /// </para>
    ///
    /// <para>
    /// <b>两段式（有飞行段的法术）</b>：<see cref="Entry.FlightUse"/> 非空时，调用方先播飞行段
    /// （从施法起点到落点），到点后再播 <see cref="Entry.Use"/> 这一段（落地命中）。火球 = 两段都填，
    /// 其余法术 = 只有落地段（<see cref="Entry.FlightUse"/> 为空 ⇒ 在落点直接播）。
    /// </para>
    ///
    /// <para>
    /// <b>时长</b>：<see cref="Entry.Seconds"/> / <see cref="Entry.FlightSeconds"/> 是**原版该 export
    /// 自己的时间轴时长**（= 帧数 ÷ 该 export 的 fps，fps 逐行取自分组表）——
    /// ⛔ 不吃 `EffectsView.DefaultFps`（14 fps）：42 帧按 14 fps 要播 3 秒，比原版慢数倍。
    /// </para>
    ///
    /// <para>
    /// <b>帧号怎么落地</b>：源图集 `effects_out` 里每张法术只用一小段连续帧，落地只把用得着的那几段
    /// 拷进 `Resources/Sprites/Effects/`（`Spell` / `SpellFlight` / `SpellBarrel`，分段理由见
    /// <see cref="ResPaths.EffectSpellBarrel"/>）。`EffectsView` 按 <c>(firstFrame, count)</c> 从目录里定位并顺序播。
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>改动本表时必须同步改 <c>copy-fx-assets.py</c> / <c>copy_spell_fx.py</c> 与
    /// <see cref="ResPaths"/> 的常量</b> —— 三处不一致的表现是"目录里没有这一帧"，
    /// `EffectsView` 会 Warn 并从目录头开始播（画面错但不报错）。
    /// </para>
    /// </summary>
    public static class SpellFxTable
    {
        /// <summary>一条法术特效的帧位置。</summary>
        public struct Entry
        {
            /// <summary>用途目录名（<c>ResPaths.EffectSpell</c> / <c>EffectSpellBarrel</c>）。</summary>
            public string Use;

            /// <summary>起始帧（原版源帧号，不是下标）。</summary>
            public int First;

            /// <summary>帧数。</summary>
            public int Count;

            /// <summary>落地段整段时长（秒）；<c>&lt;= 0</c> ⇒ 由 <see cref="EffectsView.DefaultFps"/> 播。</summary>
            public float Seconds;

            /// <summary>飞行段用途目录；<c>null</c> / 空 ⇒ 该法术没有飞行段（在落点直接播落地段）。</summary>
            public string FlightUse;

            /// <summary>飞行段起始帧（原版源帧号）。</summary>
            public int FlightFirst;

            /// <summary>飞行段帧数。</summary>
            public int FlightCount;

            /// <summary>飞行段时长（秒）；<c>&lt;= 0</c> ⇒ 由调用方按原版 timeline 推算。</summary>
            public float FlightSeconds;

            /// <summary>
            /// 飞行段弹体的**额外**尺寸系数（相对该段换算后的原版世界尺寸，见
            /// <see cref="EffectsView.SegmentScale"/>）；<c>&lt;= 0</c> ⇒ 按 <c>1</c>（= 原版尺寸）。
            /// <para>
            /// 该段换算（画布 PPU → 原版世界尺寸）已由 <see cref="EffectsView.SegmentScale"/> 做完
            /// （系数 = `upp`/10）⇒ 本系数只用于「同一段原版像素按卡缩到不同大小」；
            /// 不需要额外缩放时 = <c>1</c>。
            /// </para>
            /// <para>只对 <see cref="FlightUse"/> 非空的法术有意义，其余法术没有飞行段。</para>
            /// </summary>
            public float FlightScale;

            /// <summary>
            /// 飞行段的**弧高**（格，见 <see cref="EffectsView.ProjectileFx.ArcHeight"/>）：
            /// `0` = 直线；火球 = 3.5（本项目口径：火球是抛物线飞过去的熔岩巨球，其余法术无飞行段）。
            /// 落地时刻与落点不受它影响。
            /// </summary>
            public float FlightArcHeight;
        }

        /// <summary>
        /// 按卡 key（`spell.tsv` 的 `key` 列，服务端原样下发到 <c>CardInfo.key</c>）取特效。
        /// 返回 <c>false</c> = 这张法术还没有接特效（调用方须**留痕**，⛔ 不静默丢弃）。
        /// </summary>
        public static bool TryGet(string cardKey, out Entry entry)
        {
            switch (cardKey)
            {
                // 火球 —— 两段式。出处：分组表 `:4710` `fireball`（clip 229–1353｜fps 30,60｜帧列 `22 396-436`）
                // + `:5276` `fireball_projectile1`（clip 1353｜60 fps｜91 步｜帧列 `396-398 … 411 … 411-425`）。
                // 逐帧像素：f396..f411 = 带火焰拖尾的弹体（飞行），f412..f436 = 落地爆 + 余烬
                //（联络图 `.ai-tmp/test/fx-semantics/fireball-{a-396-416,b-417-436}.png`）。
                case "fireball":
                    entry = new Entry
                    {
                        // 落地 = f412-436（实心火球闪 f412-425 + 余烬火星 f426-436，25 帧 ÷ 60 fps = 0.417 s）。
                        Use = ResPaths.EffectSpell,
                        First = ResPaths.EffectFireballBlastFirst,
                        Count = ResPaths.EffectFireballBlastCount,
                        Seconds = ResPaths.EffectFireballBlastSeconds,
                        // 飞行 = f396-425 整段 `fireball_projectile1`（91 步 ÷ 60 fps = 1.517 s，
                        // 与 `spell.tsv` 的 `flight_ms=1517` 一致）；前半 f396-411 是带火舌的小弹体、
                        // 后半 f412-425 长成熔岩巨球 ⇒ 火球法术与法师的 f397-411 小火焰**不是同一个形状**。
                        FlightUse = ResPaths.EffectSpell,
                        FlightFirst = 396,
                        FlightCount = 30,
                        FlightSeconds = ResPaths.EffectFireballFlightSeconds,
                        // 弹体尺寸系数 = **1.0**（原版尺寸）。
                        // 推导（`upp` = 「1 图集 px = 多少 `.sc` 单位」，见 `EffectsView.SegmentScale`
                        // 的注释与 `tools/probes/fx-scale-table.py`）：
                        //   ① 原版该 export 的画布 = 474 px，而 1 格 = 1000 `upp` px
                        //      （`upp` = 19.74 ⇒ 50.66 px/格；出处 = `tools/probes/fx-scale-table.py`
                        //      打印的逐段表 `.ai-tmp/test/fx-scale-table.tsv` 的
                        //      `Spell 396 30 fireball_projectile1 19.74 strict` 行）；
                        //   ② 本工程特效素材导入 PPU = 100（`.meta` 的 `spritePixelsToUnits`）
                        //      ⇒ 素材像素与原版像素 1:1；
                        //   ③ ⇒ 原版该帧的可见宽度（格）= 内容像素 × `upp`/1000；
                        //      本工程渲染 = 内容像素 × `SegmentScale`（= `upp`/10 = 1.9737）× 本系数
                        //      ⇒ 两边相等 ⇒ 本系数 = 1.0（再乘就多算一遍 `upp`/10 这一层）。
                        FlightScale = 1.0f,
                        // 抛物线弧高 3.5 格（本项目口径）：弹道 = lerp + up·4h·t(1−t)，落点/落地时刻不变。
                        FlightArcHeight = 3.5f,
                    };
                    return true;

                // 万箭齐发落地 —— 分组表 `:4864` `Arrow_enemy_ground_anim` 帧列 `498-504`（60 fps）。
                case "arrows":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectArrowsFirst, Count = ResPaths.EffectArrowsCount, Seconds = ResPaths.EffectArrowsCount / 60f };
                    return true;

                // 狂暴法术生效 —— 分组表 `:4752` `rage_effect` 帧列 `97 206-211`（30 fps）。
                case "rage":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectRageFirst, Count = ResPaths.EffectRageCount, Seconds = ResPaths.EffectRageCount / 30f };
                    return true;

                // 火箭命中 —— 分组表 `:5249` `projectile_rocket` 帧列 `536-595`（60 fps）。
                case "rocket":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectRocketFirst, Count = ResPaths.EffectRocketCount, Seconds = ResPaths.EffectRocketCount / 60f };
                    return true;

                // 冰冻法术生效 —— 分组表 `:4712` `freeze_effect` 帧列 `73-79`（30 fps）。
                case "freeze":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectFreezeFirst, Count = ResPaths.EffectFreezeCount, Seconds = ResPaths.EffectFreezeCount / 30f };
                    return true;

                // 雷电法术 —— 分组表 `:4740` `lightning` 帧列 `172-179`（30 fps）。
                case "lightning":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectLightningFirst, Count = ResPaths.EffectLightningCount, Seconds = ResPaths.EffectLightningCount / 30f };
                    return true;

                // 电击法术 —— 分组表 `:4741` `zap` 帧列 `172-179`（30 fps；**与 lightning 同帧，原版如此**）。
                case "zap":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectZapFirst, Count = ResPaths.EffectZapCount, Seconds = ResPaths.EffectZapCount / 30f };
                    return true;

                // 伤害药水法术 —— 分组表 `:4816` `poison` 帧列 `263-293`（60 fps）。
                case "poison":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectPoisonFirst, Count = ResPaths.EffectPoisonCount, Seconds = ResPaths.EffectPoisonCount / 60f };
                    return true;

                // 滚木 —— 分组表 `:4750` `log` 帧列 `180-187`（60 fps）。
                case "the-log":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectTheLogFirst, Count = ResPaths.EffectTheLogCount, Seconds = ResPaths.EffectTheLogCount / 60f };
                    return true;

                // 哥布林飞桶 —— 分组表 `:6203` `spell_goblin_barrel` 帧列 `0 2-12`（30 fps；另一本图集）。
                case "goblin-barrel":
                    entry = new Entry { Use = ResPaths.EffectSpellBarrel, First = ResPaths.EffectGoblinBarrelFirst, Count = ResPaths.EffectGoblinBarrelCount, Seconds = ResPaths.EffectGoblinBarrelCount / 30f };
                    return true;

                default:
                    entry = default(Entry);
                    return false;
            }
        }
    }
}
