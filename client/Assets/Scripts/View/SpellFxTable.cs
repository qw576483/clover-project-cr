using CR.Def;   // CardInfo（XML 文档里的 cref 用；本类的 switch 只吃字符串 key）

namespace CR.View
{
    /// <summary>
    /// 法术卡的**命中特效**查表（差异登记 D148）：法术卡 key → 原版帧目录 + 起始帧 + 帧数。
    ///
    /// <para>
    /// <b>为什么需要这张表</b>：法术卡走 `EvPlayCard` 事件，但事件载荷里只有 `card_id`
    /// （→ <see cref="CR.Def.CardInfo"/> 的 `key` / `type`）与落点，**没有帧号** ——
    /// 帧号必须由客户端按卡 key 查到。表里的每一行都**逐条引了出处**（`策划/单位动画分组表.md`
    /// 的 `effects` 小节，该文件是生成物、不是手写的），⛔ 没有任何一帧是"挑"出来的。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不全用同一个特效</b>：用户第 9 条报的是"看不到法术"；若 10 张法术都放同一个通用爆闪，
    /// 只是把"看不到"换成"看到的是错的"。所以按卡给帧。
    /// </para>
    ///
    /// <para>
    /// <b>帧号怎么落地</b>：源图集 `effects_out` 里每张法术只用一小段连续帧，落地脚本
    /// `.ai-tmp/hosts/copy_spell_fx.py` 只把这 10 段拷进 `Resources/Sprites/Effects/Spell/`
    /// （哥布林飞桶是另一本图集 ⇒ 另一个目录 `SpellBarrel/`，理由见 <see cref="ResPaths.EffectSpellBarrel"/>）。
    /// `EffectsView` 按 <c>(firstFrame, count)</c> 从目录里定位并顺序播。
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>改动本表时必须同步改 <c>copy_spell_fx.py</c> 与 <see cref="ResPaths"/> 的常量</b> ——
    /// 三处不一致的表现是"目录里没有这一帧"，`EffectsView` 会 Warn 并从目录头开始播（画面错但不报错）。
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
        }

        /// <summary>
        /// 按卡 key（`spell.tsv` 的 `key` 列，服务端原样下发到 <c>CardInfo.key</c>）取特效。
        /// 返回 <c>false</c> = 这张法术还没有接特效（调用方须**留痕**，⛔ 不静默丢弃）。
        /// </summary>
        public static bool TryGet(string cardKey, out Entry entry)
        {
            switch (cardKey)
            {
                // 火球 —— 分组表 :4710 `fireball` 帧列 `22 396-436` 的连续段。
                case "fireball":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectFireballFirst, Count = ResPaths.EffectFireballCount };
                    return true;

                // 万箭齐发 —— 分组表 :4864 `Arrow_enemy_ground_anim` 帧列 `498-504`。
                case "arrows":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectArrowsFirst, Count = ResPaths.EffectArrowsCount };
                    return true;

                // 狂暴法术 —— 分组表 :4752 `rage_effect` 帧列 `97 206-211` 的连续段。
                case "rage":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectRageFirst, Count = ResPaths.EffectRageCount };
                    return true;

                // 火箭 —— 分组表 :5249 `projectile_rocket` 帧列 `536-595`。
                case "rocket":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectRocketFirst, Count = ResPaths.EffectRocketCount };
                    return true;

                // 冰冻法术 —— 分组表 :4712 `freeze_effect` 帧列 `73-79`。
                case "freeze":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectFreezeFirst, Count = ResPaths.EffectFreezeCount };
                    return true;

                // 雷电法术 —— 分组表 :4740 `lightning` 帧列 `172-179`。
                case "lightning":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectLightningFirst, Count = ResPaths.EffectLightningCount };
                    return true;

                // 电击法术 —— 分组表 :4741 `zap` 帧列 `172-179`（**与 lightning 同帧，原版如此**）。
                case "zap":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectZapFirst, Count = ResPaths.EffectZapCount };
                    return true;

                // 伤害药水法术 —— 分组表 :4816 `poison` 帧列 `263-293`。
                case "poison":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectPoisonFirst, Count = ResPaths.EffectPoisonCount };
                    return true;

                // 滚木 —— 分组表 :4750 `log` 帧列 `180-187`。
                case "the-log":
                    entry = new Entry { Use = ResPaths.EffectSpell, First = ResPaths.EffectTheLogFirst, Count = ResPaths.EffectTheLogCount };
                    return true;

                // 哥布林飞桶 —— 分组表 :6205 `spell_goblin_barrel` 帧列 `0 2-12`（另一本图集）。
                case "goblin-barrel":
                    entry = new Entry { Use = ResPaths.EffectSpellBarrel, First = ResPaths.EffectGoblinBarrelFirst, Count = ResPaths.EffectGoblinBarrelCount };
                    return true;

                default:
                    entry = default(Entry);
                    return false;
            }
        }
    }
}
