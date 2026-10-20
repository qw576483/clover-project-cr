using System.Collections.Generic;
using CloverEngine;
using CR.Def;
using CR.UI;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 一个**单位或建筑**的视图：逐帧精灵（D5）+ `anim` 档位 + 头顶血条（D6）。
    ///
    /// <para>
    /// <b>它怎么拿到坐标</b>：坐标由 <see cref="BattleViewRoot"/> 插值后传进来（见那边的双帧缓冲）。
    /// 本类**不做插值**、也不认识 `Module`（契约 §5.5：`View/**` ⛔ 不许 `using CR.Module`）。
    /// </para>
    /// <para>
    /// <b>为什么朝向用 `flipX` 而不是负 `localScale.x`</b>：血条 <see cref="WorldHpBar"/> 是
    /// 本节点的**子节点**，父节点负缩放会把血条一起镜像；`SpriteRenderer.flipX` 只影响精灵本身，
    /// 不影响子 Transform，血条与将来挂上来的特效都不会被翻。
    /// </para>
    /// <para>
    /// <b>动画分段怎么来的</b>：解包素材目录（如 `chr_knight_out` 486 帧）本身没有分段元数据，但原版
    /// `.sc`（`原版资源/sc/*_v215.sc`，2.1.5 权威源）的 `0c` 动画块带 `export 名 → clip id → shapeID 列表`，
    /// 而 `shapeID` **就是** `frame_NNN` 的 `NNN`（`ShapeCount` == PNG 张数，逐目录核过）⇒ 每档的**帧号集合**
    /// 可**显式**推出。数据（每目录 4 档的帧号段表 + 来源 + 可用性）**全部落在 <see cref="UnitAnimTable"/>**
    /// （独立数据文件；出处 = `策划/单位帧段表.md` §3「可直接填 `UnitView.AnimRanges` 的四元组」）。本类只做三件事：
    /// ① 把该档的**帧号段表**（`UnitAnimTable.Clip.Runs`）经 <see cref="SpriteBank.FrameNumberMap"/>
    /// 换算成**帧数组下标序列**（见 <see cref="ClipIndices"/>）；② 把该档的帧序列 + 帧率 + 是否循环交给
    /// 引擎逐帧动画器 <see cref="SpriteFrameAnimator"/>（帧计时 / 帧下标推进 / 播完停末帧 / 写 `sprite` 都在那里）；
    /// ③ 该档 `Known == false`（素材无此动画 / `.sc` 引用越界 shapeID）⇒ **回落整目录循环**
    /// （`die` 例外：原版单位没有死亡动画 ⇒ 保持当前画面不动，见 <see cref="PlayTier"/>）。
    /// </para>
    /// <para>
    /// <b>已收录 vs 未收录</b>：<see cref="UnitAnimTable"/> 覆盖 **45 个目录**（89 个里 44 个因
    /// 「素材无 idle/walk/attack/run 语义的 export」或「`.sc` 引用越界 shapeID」而**全部档位不可用**）。
    /// 未收录目录（或某档位不可用）**保持"整目录循环"**并在首次绑定时留一条 `Warn`
    /// （见 <see cref="WarnIfUnmapped"/>）—— 这是**已登记差异**，不是静默降级。
    /// </para>
    /// <para>
    /// <b>已登记的近似</b>（出处 = `策划/单位帧段表.md` §3 + §2 的「蓝/红」列取舍）：
    /// 每档帧号集合取「9 个视角的并集」（原版按单位朝向只播其中 1 个视角，本工程没有视角选择）
    /// ⇒ 长段会慢慢"转视角"；攻击档帧率按「攻击段帧数 ÷ `hit_speed_ms`」标定（见 <see cref="AssertAttackTiming"/>）
    /// ⇒ 攻击段播完一遍的时长 == `hit_speed_ms`。**die 所有目录都未找到**（素材无死亡动画，该表逐目录核过）
    /// ⇒ `Known=false` ⇒ **保持当前画面不动**（⛔ 不回落整目录，见 <see cref="PlayTier"/>）。
    /// </para>
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "UnitView";

        // ───── anim 档位取值 —— 逐字对应契约 `EntitySnapshot.anim`（`Def/ProtoDef.cs:137`）─────
        /// <summary>`anim == 0`：待机。</summary>
        public const int AnimIdle = 0;
        /// <summary>`anim == 1`：移动。</summary>
        public const int AnimWalk = 1;
        /// <summary>`anim == 2`：攻击。</summary>
        public const int AnimAttack = 2;
        /// <summary>`anim == 3`：死亡（播完停在末帧）。</summary>
        public const int AnimDie = 3;

        /// <summary>
        /// 各档**默认**播放帧率（帧/秒）—— 只给「不在 <see cref="UnitAnimTable"/> 里的目录 / 未覆盖的档位」用
        /// （它们整目录循环、没有 `.sc` 逐档 FPS 可查）。出处：**本项目自定**，
        /// 取值依据只有三条相对关系：移动比待机快、攻击比移动快、死亡播完即停。
        /// <para>⛔ 收录目录**不**用这里：帧率一律按「帧数 ÷ 该档原版时长」算（见 <see cref="FpsFor"/> 与
        /// <see cref="UnitAnimDurations"/>）；本数组只给「表外目录 / 表内档位不可用」的整目录循环用。</para>
        /// </summary>
        private static readonly float[] AnimFps = { 8f, 12f, 14f, 10f };

        /// <summary>
        /// 「档位帧下标序列」缓存（键 = `mode|path|anim`）。帧段表（<see cref="UnitAnimTable"/>）给的是**帧号**，
        /// 播放要用的是**帧数组下标** ⇒ 每个目录每档只换算一次（换算规则见 <see cref="ResolveFrame"/>）。
        /// 值语义：`null` = 该档没有可用帧段（回落整目录）；非 null 且长度 &gt; 0 = 按它逐帧播。
        /// </summary>
        private static readonly Dictionary<string, int[]> ClipIndexCache = new Dictionary<string, int[]>();

        /// <summary>
        /// 帧段数据表在 **<see cref="UnitAnimTable"/>**（独立文件，⛔ 本类不再内联几千行表）：
        /// 每目录 4 档的**帧号段表**（`Clip.Runs` = [起始帧号, 长度, ...]）+ 来源 + 可用性。
        /// <para>
        /// ⚠️ 表里的 `Runs` 是**帧号**（= `frame_NNN` 的 `NNN` = `.sc` 的 shape 序号），**不是数组下标**
        /// —— 由 <see cref="SpriteBank.FrameNumberMap"/> 换算（单位目录 1 PNG = 1 Sprite ⇒ 通常**恒等**，
        /// 但**必须断言**且**不假设**：`chr_giant_out` 实测非恒等，见那里的日志与 <see cref="ResolveFrame"/>）。
        /// </para>
        /// <para>⛔ 为什么用「段表」而不是单个 `(start, count)` 区间：`策划/单位帧段表.md` §3 明写
        /// 「连续?=否时 `start/count` 不能当连续区间用」—— 例如 `chr_archer` attack = `182-230 ∪ 247-251`
        /// （2 段）。把它当连续区间会把 `231-235`（walk 帧）夹进攻击动作里。</para>
        /// </summary>
        public static int MappedDirCount => UnitAnimTable.DirCount;

        /// <summary>按 spriteDir 分桶的空闲实例（复用，⛔ 不每次 Instantiate）。</summary>
        private static readonly Dictionary<string, Stack<UnitView>> Pools = new Dictionary<string, Stack<UnitView>>();

        /// <summary>取不到帧时用的 1×1 白色占位精灵（懒建、全局共享）。</summary>
        private static Sprite _fallbackSprite;

        /// <summary>血条离脚底的高度（格）—— 出处见 <see cref="Bind"/> 里创建血条那一处。</summary>
        private const float HpBarYOffset = 1.6f;

        /// <summary>
        /// `spriteDir` → **逐单位缩放**（写在单位根的 `transform.localScale` 上）。
        /// <para>
        /// <b>出处（原版数据反解）</b>：官方 `.sc`（`原版资源/sc/chr_*_v215.sc`）的 shape 记录（tag `0x12`）
        /// 同时给出两样东西 —— 形状多边形的外接框（**`.sc` 单位**）与同一多边形在图集上占的矩形（**px**）；
        /// 两者之比 = 该单位「1 图集 px = 多少 `.sc` 单位」（记 `upp`，本表取**逐帧中位**）。
        /// 换算常数 = **1000 `.sc` 单位/格**（证据：`building_tower_v215.sc` 的 1 格方块 rec1 = 1021 单位 /
        /// 图集 52 px；PEKKA 多边形半宽 747 ≈ 官方 `collision_radius = 750` = 0.75 格）。
        /// 本工程导入一律 `spritePixelsToUnits = 100`（= 100 图集 px/格），正确值是 `1000 / upp`
        /// ⇒ 本表的值 = `upp / 10`。
        /// </para>
        /// <para>
        /// <b>哪些 shape 参与取值（规则）</b>：`0x12` 记录里只有「多边形与图集矩形是 1:1 贴图」的那些
        /// 才携带一个「单位/像素」数 —— 判据 = `0.9 ≤ upp_x / upp_y ≤ 1.1`（`upp_x` = 多边形外接框宽
        /// ÷ 图集矩形宽，`upp_y` 同理）；其余形状的多边形与纹理不成 1:1，会把中位带偏
        /// （`building_tesla_v215.sc` 全量中位 6.79、可用子集中位 9.82）。取值 = 可用子集 `upp` 的**中位** ÷ 10。
        /// </para>
        /// <para>
        /// ⚠️ 只列 `chr_*_out`（部队）。**建筑目录一律不列、按 1.0**，两条理由：
        /// ① `.sc` 侧 —— 建筑的 shape 集只能筛出约四成 1:1 贴图（`building_tesla_v215.sc` 全量中位 6.79 /
        /// 可用子集 9.82），仍不足以定位「主体件」；
        /// ② **素材侧** —— `building_*_out` 目录是**多子图**目录（如 `building_goblin_hut_out` 的帧宽中位 42 px、
        /// 最宽帧 169 px），「帧宽中位」≠ 建筑自身的宽度 ⇒ 没有可配对的两个量。
        /// `chr_balloon_out` 同理不列（`.sc` 只有 10 条可用 shape）。
        /// 塔目录 `building_tower_out` **从不**走本类（`BattleViewRoot` 只把快照的 `Entities`（部队 / 已部署建筑）
        /// 交给它，冠状塔由 `ArenaView` 用自己的层配方画）。
        /// </para>
        /// <para>生成路径：`tools/probes/sc-placement.py`（解析 `.sc` 后出表）。</para>
        /// </summary>
        private static readonly Dictionary<string, float> UnitSpriteScale =
            new Dictionary<string, float>
        {
            { "chr_archer_out", 0.9904f },
            { "chr_axe_man_out", 1.9833f },
            { "chr_baby_dragon_out", 1.3115f },
            { "chr_bandit_out", 1.9684f },
            { "chr_barbarian_out", 1.2891f },
            { "chr_bats_out", 0.9903f },
            { "chr_battle_ram_out", 1.9782f },
            { "chr_bomber_out", 0.9873f },
            { "chr_bowler_out", 1.3083f },
            { "chr_electro_wizard_out", 1.9726f },
            { "chr_fire_firespirit_out", 1.0849f },
            { "chr_giant_out", 1.7672f },
            { "chr_giant_skeleton_out", 1.9626f },
            { "chr_goblin_archer_out", 0.9859f },
            { "chr_goblin_blowdart_out", 1.9763f },
            { "chr_goblin_out", 1.1385f },
            { "chr_hog_rider_out", 1.3094f },
            { "chr_ice_spirits_out", 1.2836f },
            { "chr_ice_wizard_out", 0.9911f },
            { "chr_knight_out", 1.4417f },
            { "chr_lava_hound_out", 1.3096f },
            { "chr_lava_pups_out", 1.3029f },
            { "chr_mega_knight_out", 1.3134f },
            { "chr_mega_minion_out", 0.9916f },
            { "chr_miner_out", 1.3072f },
            { "chr_mini_pekka_out", 1.1883f },
            { "chr_minion_out", 0.9897f },
            { "chr_movingcannon_out", 2.1822f },
            { "chr_musketeer_out", 1.3031f },
            { "chr_pekka_out", 1.3103f },
            { "chr_prince_out", 1.6341f },
            { "chr_princess_out", 0.9903f },
            { "chr_royal_giant_out", 1.7029f },
            { "chr_skeleton_out", 0.9792f },
            { "chr_valkyrie_out", 0.9862f },
            { "chr_witch_out", 0.9918f },
            { "chr_wizard_out", 1.3045f },
        };

        /// <summary>取该 `spriteDir` 的逐单位缩放（未列出 / 非正值 ⇒ 1.0）。见 <see cref="UnitSpriteScale"/>。</summary>
        private static float SpriteScaleFor(string spriteDir)
        {
            float s;
            return spriteDir != null && UnitSpriteScale.TryGetValue(spriteDir, out s) && s > 0f ? s : 1f;
        }

        /// <summary>
        /// 该精灵目录的**逐单位缩放**（= 对本目录建出的单位实际生效的那个值）。
        /// 供预览类界面按"与战场上同一尺寸"摆一张 idle 首帧。
        /// </summary>
        public static float PreviewScaleFor(string dir)
        {
            return SpriteScaleFor(dir);
        }

        /// <summary>
        /// 把逐单位缩放写到单位根，并**抵消它对血条的影响**。
        /// <para>
        /// 血条挂在单位根下（`WorldHpBar.Create` 用 `localPosition = (0, yOffset, 0)`、两个 Quad 的尺寸
        /// 也是局部单位）⇒ 父节点一缩放，血条的**世界尺寸与离地高度会一起跟着变**（大单位的血条又大又高）。
        /// 这里按 `1 / scale` 反算回去，使血条的世界尺寸恒为 1.0×0.12 格、离脚底恒为
        /// <see cref="HpBarYOffset"/> 格 —— 与单位缩放无关。
        /// </para>
        /// </summary>
        private void ApplySpriteScale(string spriteDir)
        {
            var s = SpriteScaleFor(spriteDir);
            transform.localScale = new Vector3(s, s, 1f);
            if (_hpBar == null) return;
            var bar = _hpBar.transform;
            bar.localScale = new Vector3(1f / s, 1f / s, 1f);
            var p = bar.localPosition;
            bar.localPosition = new Vector3(p.x, HpBarYOffset / s, p.z);
        }

        /// <summary>铺 / 收本单位的贴地阴影（判据与出处见文件上方「贴地阴影」一节）。</summary>
        private void ApplyShadow(bool isBuilding)
        {
            var show = !isBuilding && !FlyingDirs.Contains(_spriteDir ?? string.Empty)
                && _frames != null && _frames.Length > 0;
            if (!show)
            {
                if (_shadow != null) _shadow.enabled = false;
                return;
            }
            if (_shadow == null) CreateShadow();
            Vector2 m;
            if (!ShadowMetricsFor(_spriteDir, _frames, out m) || m.x <= 0f || m.y <= 0f)
            {
                _shadow.enabled = false;
                return;
            }
            _shadow.enabled = true;
            // 单位根 pivot = 全目录 rect 并集的中心（见 `FrameBank.PivotMode.UnifiedCanvasAnchor`）
            // ⇒ 脚底（并集下沿）在局部 y = -高/2。子节点继承单位根的逐单位缩放，故这里写"缩放前"的格数。
            var w = m.x * ShadowWidthRatio;
            var h = w * ShadowAspect;
            _shadow.transform.localPosition = new Vector3(
                w * ShadowOffsetRatioX,
                -m.y * 0.5f - h * ShadowOffsetRatioY,
                0f);
            _shadow.transform.localScale = new Vector3(w, h, 1f);
        }

        private void CreateShadow()
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(transform, false);
            _shadow = go.AddComponent<SpriteRenderer>();
            _shadow.sprite = SharedShadowSprite();
            _shadow.color = new Color(0f, 0f, 0f, ShadowAlpha);
            // 材质必须用项目自己的 `CR/SpriteBlend`（无光照）：URP 的 `Sprite-Lit-Default` 在没有 2D 光源时
            // 输出透明（实测阴影整枚不显示）。恒等变换 + 顶点色 (黑, 0.22) ⇒ 预乘后只剩 α 遮罩。
            _shadow.sharedMaterial = SpriteColorTransformMaterial.ForSprite(255, 255, 255, 255, 0, 0, 0);
            _shadow.enabled = false;
        }

        /// <summary>
        /// 建效果件层渲染器（单位根的子节点 ⇒ 与本体同一个单位根、同一世界缩放、同一画布坐标）。
        /// </summary>
        private void EnsureFxLayer()
        {
            if (_fxRenderer != null) return;
            var go = new GameObject("FxLayer");
            go.transform.SetParent(transform, false);
            _fxRenderer = go.AddComponent<SpriteRenderer>();
            _fxRenderer.sortingOrder = ArenaLayers.Instance.Actor;
            _fxRenderer.enabled = false;
            _fxFrameNo = 0;
        }

        /// <summary>
        /// 效果件层：本体播到第 <c>i</c> 帧位时，若该位有效果件，就在**同一画布坐标**上再画一层。
        /// <para>
        /// 映射来源 = <see cref="UnitAnimTable.Clip.FxByPos"/> / <see cref="UnitAnimTable.Clip.FxByPosRed"/>
        /// （生成物：逐视角的「每个本体帧位对应的效果帧号」，`0` = 该位没有效果件）。
        /// ⛔ 效果件**不进本体帧段**（进则本体消失一帧）—— 它只在这一层出现。
        /// </para>
        /// </summary>
        private void SyncFxLayer()
        {
            var frameNo = FxFrameNoForCurrentPos();
            if (frameNo == _fxFrameNo) return;      // 只在真的变了才写（⛔ 不每帧写渲染器）
            _fxFrameNo = frameNo;
            if (frameNo <= 0 || !HasFrames)
            {
                if (_fxRenderer != null)
                {
                    _fxRenderer.sprite = null;
                    _fxRenderer.enabled = false;
                }
                return;
            }
            EnsureFxLayer();                     // 只有真的用到效果件时才建这一层（⛔ 不给每个单位都加节点）
            if (_fxRenderer == null) return;
            var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);
            var idx = ResolveFrame(map, frameNo);
            if (idx < 0 || idx >= _frames.Length)
            {
                // 非预期分支必须留痕：效果件帧号在本目录解析不到 ⇒ 这一层不画。
                LogThrottle.WarnOnce(LogTag, "fx:" + _spriteDir + ":" + frameNo,
                    $"效果件帧号在本目录解析不到 ⇒ 效果件层不画：dir={_spriteDir} frame={frameNo}");
                _fxRenderer.sprite = null;
                _fxRenderer.enabled = false;
                return;
            }
            _fxRenderer.sprite = _frames[idx];
            _fxRenderer.enabled = true;
            ApplyFxMaterial();
            // 效果件层生效的留痕（每个目录只报一次；数值类证据 = 运行时日志行）。
            if (FxLogged.Add(_spriteDir + ":" + frameNo))
                Game.Logger?.Info(LogTag,
                    $"效果件层叠画：dir={_spriteDir} frame={frameNo} 视角={_viewNo} 帧位={_animator.FrameIndex}（不进本体 Runs）");
        }

        /// <summary>当前本体帧位对应的效果帧号（`0` = 这一帧位没有效果件）。</summary>
        private int FxFrameNoForCurrentPos()
        {
            if (_animator == null || !HasFrames) return 0;
            UnitAnimTable.Entry entry;
            if (!UnitAnimTable.TryGet(_spriteDir, out entry) || entry.Tiers == null) return 0;
            var anim = Mathf.Clamp(_anim, AnimIdle, AnimDie);
            if (anim >= entry.Tiers.Length) return 0;
            var clip = entry.Tiers[anim];
            if (!clip.Known) return 0;
            var slots = _team == UnitAnimTable.TeamRed ? clip.FxByPosRed : clip.FxByPos;
            if (slots == null || slots.Length == 0) return 0;
            var clipLength = _animator.ClipLength;
            var arr = (_viewNo >= 1 && _viewNo <= slots.Length) ? slots[_viewNo - 1] : null;
            if (arr == null || arr.Length != clipLength)
            {
                // 该视角没有素材 ⇒ 运行时回落到默认视角的帧段（见 <see cref="ClipIndices"/>）；
                // 效果件序列也跟着回落（长度必须与当前片段一致）。
                arr = null;
                for (var i = 0; i < slots.Length; i++)
                {
                    var a = slots[i];
                    if (a != null && a.Length == clipLength) { arr = a; break; }
                }
            }
            if (arr == null || arr.Length == 0) return 0;
            var pos = _animator.FrameIndex;
            if (pos < 0 || pos >= arr.Length) return 0;
            return arr[pos];
        }

        /// <summary>效果件层的材质：受击闪白期间与本体同材质，否则按该帧的原版混合模式。</summary>
        private void ApplyFxMaterial()
        {
            if (_fxRenderer == null || _fxFrameNo <= 0) return;
            Material mat = null;
            if (_hitFlashOn)
            {
                mat = LogMissingFlashMaterial();
            }
            else
            {
                mat = SpriteBlendMaterial.For(_spritePath, _fxFrameNo, _rendererDefault);
            }
            if (mat == null) return;         // ⛔ 绝不写 null（那会让渲染器落到错误材质上）
            SpriteBlendMaterial.Set(_fxRenderer, mat);
        }

        /// <summary>柔边圆盘（径向 `a = (1 - r²)²`）：一次生成、全类共用，⛔ 不新增图片资源。</summary>
        private static Sprite SharedShadowSprite()
        {
            if (_shadowSprite != null) return _shadowSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "UnitShadow";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                var dy = (y + 0.5f) / size * 2f - 1f;
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) / size * 2f - 1f;
                    var r2 = dx * dx + dy * dy;
                    var a = r2 >= 1f ? 0f : (1f - r2) * (1f - r2);
                    px[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _shadowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _shadowSprite.name = "gen_unit_shadow";
            return _shadowSprite;
        }

        /// <summary>逐帧 `rect` 并集的尺寸（格）；同一 `sprite_dir` 只算一次。</summary>
        private static bool ShadowMetricsFor(string dir, Sprite[] frames, out Vector2 size)
        {
            size = Vector2.zero;
            if (frames == null || frames.Length == 0) return false;
            if (ShadowMetrics.TryGetValue(dir ?? string.Empty, out size)) return true;
            var ppu = frames[0] != null && frames[0].pixelsPerUnit > 0.01f ? frames[0].pixelsPerUnit : 100f;
            var minX = float.MaxValue; var maxX = float.MinValue;
            var minY = float.MaxValue; var maxY = float.MinValue;
            for (var i = 0; i < frames.Length; i++)
            {
                var s = frames[i];
                if (s == null) continue;
                var r = s.rect;
                if (r.width <= 0.01f || r.height <= 0.01f) continue;
                if (r.xMin < minX) minX = r.xMin;
                if (r.xMax > maxX) maxX = r.xMax;
                if (r.yMin < minY) minY = r.yMin;
                if (r.yMax > maxY) maxY = r.yMax;
            }
            if (minX > maxX || minY > maxY) return false;
            size = new Vector2((maxX - minX) / ppu, (maxY - minY) / ppu);
            ShadowMetrics[dir ?? string.Empty] = size;
            return true;
        }

        private SpriteRenderer _renderer;

        /// <summary>
        /// 渲染器**自带**的默认材质（`AddComponent` 之后立刻记下）。没有混合的帧要写回它 ——
        /// ⛔ 不许往 `sharedMaterial` 写 null（那会让渲染器落到 Unity 的错误材质上，整块画成洋红）。
        /// </summary>
        private Material _rendererDefault;

        // ───────────────────────────── 贴地阴影（程序化） ─────────────────────────────
        //
        // 原版单位帧**自带不带烘焙阴影**：chr_knight_out 全 486 帧的 alpha 里，
        // 低 alpha（4..60）像素只是实体轮廓外 1 px 的抗锯齿晕（帧 000：238 px，软过渡包围盒 = 实体包围盒 ±1 px），
        // 没有"脚下低 alpha 椭圆"。⇒ 地面单位需要一枚程序化软阴影，否则整队人像浮在草地上。
        // 飞行单位不做（判据 = server/game/table/tsv/unit.tsv 的 `flying=1` 行的 `sprite_dir`）。

        /// <summary>阴影的不透明度（黑，alpha 0.30 —— 与基线图 `策划/参考图/20_对局_1080x1920.jpg` 脚下一片明显暗影同量级）。</summary>
        private const float ShadowAlpha = 0.30f;

        /// <summary>阴影宽度 ÷ 单位内容宽度（基线图里影子比脚宽，取 1.05）。</summary>
        private const float ShadowWidthRatio = 1.05f;

        /// <summary>阴影高宽比（椭圆，压扁 ⇒ 俯视角下的落地投影）。</summary>
        private const float ShadowAspect = 0.42f;

        /// <summary>
        /// 阴影相对脚底的偏移（占阴影宽/高的比例）：光从左上方来 ⇒ 影子落向右下。
        /// 基线图 `20_对局_1080x1920.jpg` 的单位影子明显偏在身体下方一侧，不是正下方一个圆。
        /// </summary>
        private const float ShadowOffsetRatioX = 0.10f;
        private const float ShadowOffsetRatioY = 0.10f;

        /// <summary>
        /// 飞行单位的 `sprite_dir`（出处 = `server/game/table/tsv/unit.tsv` 里 `flying=1` 的行；
        /// `Bat` 行没有 sprite_dir ⇒ 不在表里，其素材与 `bats` 共用 `chr_bats_out`）。
        /// 这些目录不铺阴影 —— 它们是离地的。
        /// </summary>
        private static readonly HashSet<string> FlyingDirs = new HashSet<string>
        {
            "chr_baby_dragon_out", "chr_balloon_out", "chr_bats_out",
            "chr_lava_hound_out", "chr_lava_pups_out", "chr_mega_minion_out", "chr_minion_out",
        };

        /// <summary>本视图的贴地阴影（单位根的子节点；⛔ 不参与受击闪白 —— 闪白只写 `_renderer` / `_layers`）。</summary>
        private SpriteRenderer _shadow;

        /// <summary>
        /// 效果件层渲染器（单位根的子节点）：原版把**本体层**与**效果层**叠画在同一条时间轴上，
        /// 本工程一帧一图元 ⇒ 效果件单独一层画。
        /// <para>
        /// 与本体同目录、同锚点口径、同世界缩放 ⇒ 效果件与本体**严丝合缝**（同一画布坐标）。
        /// </para>
        /// </summary>
        private SpriteRenderer _fxRenderer;

        /// <summary>效果件层当前显示的帧号（`0` = 这一帧位没有效果件）。</summary>
        private int _fxFrameNo;

        /// <summary>「效果件层生效」只报一次的集合（键 = 目录 + 效果帧号）。</summary>
        private static readonly HashSet<string> FxLogged = new HashSet<string>();

        /// <summary>全类共享的 1×1 世界单位柔边圆盘（运行时生成一次；⛔ 不是美术资源）。</summary>
        private static Sprite _shadowSprite;

        /// <summary>`sprite_dir → (内容宽格, 内容高格)`：逐帧 `rect` 并集的尺寸，阴影尺寸/落点由它换算。</summary>
        private static readonly Dictionary<string, Vector2> ShadowMetrics = new Dictionary<string, Vector2>();

        private WorldHpBar _hpBar;
        private string _spriteDir = string.Empty;
        private Sprite[] _frames;
        /// <summary>加载时用的完整资源路径（`ResPaths.UnitDir/BuildingDir`）—— 帧号→下标映射的缓存键要用。</summary>
        private string _spritePath = string.Empty;
        /// <summary>加载时用的锚点模式（与 <see cref="_spritePath"/> 一起构成 <see cref="SpriteBank.FrameNumberMap"/> 的键）。</summary>
        private SpriteBank.SpritePivotMode _pivotMode = SpriteBank.SpritePivotMode.UnifiedCanvasAnchor;

        /// <summary>本视图是不是建筑（取 `ResPaths.BuildingDir` 那一支；层配方只对建筑生效）。</summary>
        private bool _isBuilding;

        /// <summary>
        /// 建筑的各层渲染器（出处 = <see cref="BuildingLayerTable"/>；非建筑 / 目录未收录时为空）。
        /// <para>
        /// 一个建筑的 `building_*_out` 目录装的是**建物的多个子件 + 两队配色 + 小道具**，单个渲染器一次只能画一帧
        /// ⇒ 会把部件帧与敌队配色帧也播出来。有配方时改成「按配方给的帧与偏移叠 N 层」，层序 = 配方顺序（后建的在上）。
        /// </para>
        /// </summary>
        private SpriteRenderer[] _layers;

        /// <summary>`_layers` 是按哪一队的配方建的（`-1` = 还没建；池复用可能换队，必须重建）。</summary>
        private int _layersTeam = -1;

        /// <summary>「该建筑目录没有层配方」只报一次的集合。</summary>
        private static readonly HashSet<string> BuildingRecipeWarned = new HashSet<string>();

        /// <summary>该目录是否有建筑层配方（两队任一即可）。</summary>
        private bool HasBuildingRecipe()
        {
            if (!_isBuilding) return false;
            BuildingLayerTable.Layer[] tmp;
            return BuildingLayerTable.TryGet(_spriteDir, BuildingLayerTable.TeamBlue, out tmp)
                || BuildingLayerTable.TryGet(_spriteDir, BuildingLayerTable.TeamRed, out tmp);
        }

        /// <summary>
        /// 已建的建筑层里是否**至少有一块**（`BuildLayers` 对"配方帧一个都解析不出来"也返回 true，
        /// 那种情形下列表里全是 null —— 调用方据此决定要不要保住单帧渲染器）。
        /// </summary>
        private bool HasAnyLayer()
        {
            if (_layers == null) return false;
            for (var i = 0; i < _layers.Length; i++)
                if (_layers[i] != null) return true;
            return false;
        }

        /// <summary>销毁已有的层节点（池复用 / 换目录 / 换队时调）。</summary>
        private void ClearLayers()
        {
            _layerBaseMaterials = null;
            if (_layers == null) return;
            for (var i = 0; i < _layers.Length; i++)
                if (_layers[i] != null) Destroy(_layers[i].gameObject);
            _layers = null;
            _layersTeam = -1;
        }

        /// <summary>
        /// 按 <see cref="BuildingLayerTable"/> 的配方建/重建这个建筑的各层。返回 true = 该队有配方、已按层渲染。
        /// <para>偏移换算与 `ArenaView` 的塔层同式：`localPosition = (DxPx, −DyPx) ÷ 该帧的 pixelsPerUnit`
        /// （`.sc` 矩阵的 y 向下为正，而 Unity 的 +y 向上）。</para>
        /// </summary>
        private bool BuildLayers(int team)
        {
            BuildingLayerTable.Layer[] recipe;
            if (_frames == null || _frames.Length == 0
                || !BuildingLayerTable.TryGet(_spriteDir, team, out recipe)) return false;
            if (_layers != null && _layersTeam == team) return true;
            ClearLayers();
            // 配方的 `Frame` 是**帧号**（`frame_NNN` 的 NNN），不是帧数组下标 —— 老规矩必须过
            // `SpriteBank.FrameNumberMap`（一张 PNG 被切成多个子 Sprite 时下标 ≠ 帧号），见 `ResolveFrame`。
            var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);
            _layers = new SpriteRenderer[recipe.Length];
            _layerBaseMaterials = new Material[recipe.Length];
            for (var i = 0; i < recipe.Length; i++)
            {
                var spec = recipe[i];
                var idx = ResolveFrame(map, spec.Frame);
                if (idx < 0 || idx >= _frames.Length)
                {
                    // 非预期分支必须留痕：配方引用的帧在本目录取不到（缺帧 / 映射缺项）⇒ 这一层不画。
                    LogThrottle.WarnOnce(LogTag, "layer:" + _spriteDir + ":" + spec.Frame,
                        $"建筑层配方的帧在本目录取不到 ⇒ 跳过该层：dir={_spriteDir} frame={spec.Frame} 下标={idx} frames.Length={_frames.Length}");
                    continue;
                }
                var sprite = _frames[idx];
                var ppu = sprite != null && sprite.pixelsPerUnit > 0.01f
                    ? sprite.pixelsPerUnit : SpriteBank.FallbackPixelsPerUnit;
                var go = new GameObject("L" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(spec.DxPx / ppu, -spec.DyPx / ppu, 0f);
                go.transform.localScale = new Vector3(spec.Sx, spec.Sy, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                // 层配方里的帧可能有原版 Add / Multiply / Screen 混合（见 `SpriteBlendTable`）⇒ 逐层选材质；
                // 没有混合的帧写回该渲染器自带的默认材质（⛔ 不许写 null = 整块洋红，见 `SpriteBlendMaterial.For`）。
                // 这一份**逐层记下来** —— 受击闪白期间层材质被换成提亮材质，闪白结束要按它逐层还原。
                _layerBaseMaterials[i] = SpriteBlendMaterial.For(
                    _spritePath, spec.Frame, SpriteBlendMaterial.RememberDefault(sr));
                SpriteBlendMaterial.Set(sr, _layerBaseMaterials[i]);
                _layers[i] = sr;
            }
            _layersTeam = team;
            // 建层期间可能正处在闪白窗口里（换队重建）⇒ 新层也要立刻换上提亮材质，否则会出现"白了一半"。
            if (_hitFlashOn) ApplyHitFlashMaterial(LogMissingFlashMaterial());
            return true;
        }

        /// <summary>把层序与透明度推到各层（层序从建筑根的世界 y 深度起算，配方顺序即绘制顺序）。</summary>
        private void ApplyLayers(Vector2 worldPos, float alpha)
        {
            if (_layers == null) return;
            var baseOrder = ArenaLayers.Instance.DepthOrder(worldPos.y);
            for (var i = 0; i < _layers.Length; i++)
            {
                var sr = _layers[i];
                if (sr == null) continue;
                sr.sortingOrder = baseOrder + i;
                var c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
        }

        /// <summary>
        /// 引擎逐帧动画器（出处 = `clover-client-unity-engine/Runtime/Presentation/SpriteFrameAnimator.cs`）：
        /// 帧表 + 档位帧下标切片 + 帧率 ⇒ 逐帧推进并写 `SpriteRenderer.sprite`。
        /// <para>
        /// 本类只给它**业务口径**（哪一档 / 哪个视角的帧序列、多少 fps、是否循环、何时换档），
        /// 帧计时、帧下标推进、播完停末帧都是引擎件的机械动作。
        /// </para>
        /// <para>`Play` / `SwitchTo` / `SetFrame` 内部都已把该帧写进渲染器，**当帧生效**（不用等一个帧周期）。</para>
        /// </summary>
        private SpriteFrameAnimator _animator;

        /// <summary>`_spriteIndex` 的哨兵值：还没有任何一帧被本类补过附随状态（换宿主后必须重贴）。</summary>
        private const int NotShown = int.MinValue;

        /// <summary>
        /// 上一次由本类补过附随状态（混合材质 / 占位图）的帧数组下标；`-1` = 无帧。
        /// 用途同引擎动画器的"只在帧真的变了时写 `sprite`"：⛔ 不每帧写。
        /// </summary>
        private int _spriteIndex = NotShown;

        private int _anim = AnimIdle;

        /// <summary>
        /// 本视图当前使用的配色侧（<see cref="UnitAnimTable.TeamBlue"/> / <see cref="UnitAnimTable.TeamRed"/>）。
        /// 由 <see cref="Apply"/> 按快照 `team` 跟进：敌方取红侧帧段（<see cref="UnitAnimTable.Clip.RunsRed"/>）。
        /// </summary>
        private int _team = UnitAnimTable.TeamBlue;

        /// <summary>
        /// 本次攻击档播放期间，服务端是否已经请求过非攻击档（= 攻击窗口的**释放**）。
        /// <para>
        /// 服务端把一次挥砍编码成一段 `AnimAttack` 窗口，窗口之间必然夹着 walk/idle
        /// （见 `server/game/core/combat.go` 的 `attackAnimHoldTicks`）⇒「出现过释放之后又来
        /// AnimAttack」= **新的一刀**，必须从首帧重播。只靠 `anim != _anim` 判不出来：
        /// 同一档的两次挥砍之间 `_anim` 一直是 `AnimAttack`。
        /// </para>
        /// </summary>
        private bool _attackReleased;
        private int _lastHp = -1;
        private int _lastMaxHp = -1;
        private bool _barVisible = true;

        // ───────────────────────────── 受击闪白 ─────────────────────────────
        //
        // 判据 = 快照 `hp` 比上一帧小（协议没有"命中"事件，与 `BattleViewRoot` 从快照反推命中同一口径）。
        //
        // 为什么落在渲染层：原版**没有**"单位受击动画帧段" —— `策划/单位帧段表.md` 逐目录核过，
        // 89 个目录的 `hit` 档全部"未找到" ⇒ 单位身上唯一能做的受击反馈就是**提亮精灵**。
        //
        // ⛔ 也**没有**可直接搬的受击色值：`原版资源/sc/chr_*_v215.sc` 的 `09` 颜色变换逐目录扫过
        // （`tools/probes/sc-color-transform.py --sc <目录> --bank-stats`），单位侧的非恒等件各自是
        // 该单位的固有表现（`chr_ice_wizard` 的冰霜偏色 / `chr_electro_wizard` 的自我染白 /
        // `chr_golem` 的岩体配色 / `chr_witch` 的召唤烟），没有通用"受击白闪"件。
        //
        // 取值：
        //   · 提亮 `_ColorAdd.rgb` = **255 三通道** —— **本项目自定**（原版无此值可查，见上）。
        //     着色器 `Assets/Shaders/SpriteBlend.shader:78` 是 `saturate(src * _ColorMul.rgb + _ColorAdd.rgb)`，
        //     `add = 1.0` 即饱和成纯白；`_ColorMul.a` 保持 1 ⇒ α 由精灵自身决定
        //     ⇒ 单位变成**保留自身剪影的白影**（不是白方块）。
        //   · 持续时长 = 5 帧 ÷ 60 fps = 0.0833 s —— 出处 `策划/单位动画分组表.md:5179`（原版受击件
        //     `effect_Hit1` 自己的 timeline：5 条 ÷ 60 fps），与 `ResPaths.EffectHitSeconds` 同值：
        //     同一次"被打中"的两个表现（落点闪光 + 单位白闪）共用该原版时长。

        /// <summary>受击闪白的提亮值（`_ColorAdd.rgb` 原始字节，255 = 1.0）。</summary>
        private const int HitFlashAdd = 255;

        /// <summary>受击闪白的持续时长（秒）= 原版受击件 `effect_Hit1` 的 timeline（见上）。</summary>
        private const float HitFlashSeconds = 5f / 60f;

        /// <summary>`_hitPrevHp` 的初值：本视图还没收到过第一帧快照（那时不判受击）。</summary>
        private const int NoHitHp = int.MinValue;

        /// <summary>
        /// 上一次快照里本实体的 `hp` —— 只用来判"这一刻掉血了"，⛔ 与血条的 <see cref="_lastHp"/>
        /// 无关（那是血条语义：它还会因治疗 / 上限变化而更新，且更新时机必须留在 <see cref="UpdateHpBar"/> 里）。
        /// </summary>
        private int _hitPrevHp = NoHitHp;

        /// <summary>是否有正在进行的受击闪白。</summary>
        private bool _hitFlashOn;

        /// <summary>本次受击闪白的结束时刻（`Time.time`）；<see cref="_hitFlashOn"/> 为 false 时无意义。</summary>
        private float _hitFlashUntil;

        /// <summary>本视图累计触发过几次受击闪白（读数用）。</summary>
        private int _hitFlashCount;

        /// <summary>本次攻击档起播时刻（`Time.time`，秒）—— 与 <see cref="_attackInterval"/> 一起决定重播时刻。</summary>
        private float _attackClipStart;

        /// <summary>
        /// 攻击档的重播间隔（秒）= 该目录的 `hit_speed_ms / 1000`；`0` = 表里没有攻击节拍 ⇒ 不重播。
        /// <para>服务端把连续挥砍编码成一段**持续为 2** 的 `anim`，两次挥砍之间没有 idle/walk 让位 ⇒
        /// 只靠 `_attackReleased` 判不出后续每一刀（见 <see cref="Apply"/> 的滞回规则），
        /// 需要按该卡的攻击节拍在**攻击档内**重播，否则单位会停在收招帧上直到服务端释放。</para>
        /// </summary>
        private float _attackInterval;

        /// <summary>本视图累计在攻击档内按节拍重播过几次（读数用）。</summary>
        private int _attackClipReplays;

        /// <summary>
        /// 建筑各层**各自的**基础材质（<see cref="BuildLayers"/> 建层时记下：本帧有原版混合就是那份
        /// 混合材质，否则是该渲染器自带的默认材质）—— 闪白结束按它逐层还原（⛔ 不写 null = 洋红）。
        /// </summary>
        private Material[] _layerBaseMaterials;

        // ───────────────── 朝向 → 视角（9 视角 / 16 档朝向）─────────────────
        /// <summary>朝向档（0..15，见 <see cref="UnitAnimTable.StepToView"/>）。初值 4 = 侧身朝右（φ=0°）。</summary>
        private int _viewStep = 4;
        /// <summary>是否已由**实际移动方向**定过朝向（false ⇒ 首帧用契约 `facing` 的左右兜底）。</summary>
        private bool _viewStepSet;
        /// <summary>当前视角号（1..9）= `UnitAnimTable.StepToView[_viewStep]`；变号时必须重算该视角的帧序列（见 <see cref="PlayTier"/> 与 <see cref="Apply"/>）。</summary>
        private int _viewNo = 5;
        /// <summary>最近一次**有效**移动方向（未归一化；站桩时保持上一次朝向）。</summary>
        private Vector2 _moveDir = new Vector2(1f, 0f);
        private bool _hasMoveDir;

        /// <summary>当前绑定的精灵目录（`ResPaths` 里那一列，如 `chr_knight_out`）。空串 = 用占位色。</summary>
        public string SpriteDir => _spriteDir;

        /// <summary>当前帧数（0 = 没取到帧，正在用占位色）。</summary>
        public int FrameCount => _frames == null ? 0 : _frames.Length;

        /// <summary>当前 `anim` 档位。</summary>
        public int Anim => _anim;

        /// <summary>当前**视角号**（1..9）—— 朝向选择的当前结果，供实机采样/回归脚本读取。</summary>
        public int ViewNo => _viewNo;

        /// <summary>当前**朝向档**（0..15）—— `UnitAnimTable.StepToView` 的下标。</summary>
        public int ViewStep => _viewStep;

        /// <summary>最近一次有效的移动方向（未归一化；`_hasMoveDir == false` 时是 `facing` 的左右兜底）。</summary>
        public Vector2 MoveDir => _moveDir;

        /// <summary>
        /// 视角切换计数器：`dir → 长度 9 的数组`（下标 = 视角号 − 1，值 = 该视角被**切入**的次数）。
        /// 判据：同向移动的单位切一次就稳定，切换次数应远少于每秒 1 次
        /// （取并集播放时就是"苍蝇海 1 秒 9 次视角"）。
        /// </summary>
        private static readonly Dictionary<string, int[]> ViewSwitchCount = new Dictionary<string, int[]>();

        /// <summary>视角切换计数的汇总（每个目录一行 `dir total=N [_1=a _2=b …]`）。</summary>
        public static string ViewSwitchReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in ViewSwitchCount)
            {
                var total = 0;
                for (var i = 0; i < kv.Value.Length; i++) total += kv.Value[i];
                sb.Append(kv.Key).Append(" total=").Append(total).Append(" [");
                for (var i = 0; i < kv.Value.Length; i++)
                    sb.Append(i == 0 ? "" : " ").Append('_').Append(i + 1).Append('=').Append(kv.Value[i]);
                sb.Append("] ; ");
            }
            return sb.Length == 0 ? "(无视角切换)" : sb.ToString();
        }

        /// <summary>清空视角切换计数（驱动脚本在每个采样段开始时调，便于分段统计）。</summary>
        public static void ResetViewSwitchCount() { ViewSwitchCount.Clear(); }

        // ───────────────────────────── 取用 / 归还 ─────────────────────────────

        /// <summary>
        /// 取一个 <see cref="UnitView"/>（优先复用同 spriteDir 的空闲实例）。
        /// </summary>
        /// <param name="parent">挂到哪个节点下（通常是 `BattleViewRoot` 的单位根）。</param>
        /// <param name="spriteDir">精灵目录名（`chr_*_out` / `building_*_out`）。空串 ⇒ 占位色（保留给"没有素材索引"的卡）。</param>
        /// <param name="isBuilding">true ⇒ 从 `ResPaths.BuildingDir` 取（建筑卡），false ⇒ `ResPaths.UnitDir`（部队卡）。</param>
        public static UnitView Acquire(Transform parent, string spriteDir, bool isBuilding)
        {
            var key = (isBuilding ? "B:" : "U:") + (spriteDir ?? string.Empty);
            Stack<UnitView> bucket;
            if (Pools.TryGetValue(key, out bucket))
            {
                while (bucket.Count > 0)
                {
                    var reused = bucket.Pop();
                    // Unity 的"假 null"：GameObject 被外部 Destroy 掉时这里会拿到 null，跳过并继续找。
                    if (reused == null) continue;
                    reused.gameObject.SetActive(true);
                    reused.Bind(parent, spriteDir, isBuilding);
                    return reused;
                }
            }

            var go = new GameObject("Unit");
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<UnitView>();
            view.Bind(parent, spriteDir, isBuilding);
            return view;
        }

        /// <summary>归还到池（不 Destroy —— 帧精灵的下一次绑定还会用到）。</summary>
        public void Release()
        {
            if (_renderer != null && _renderer.sprite != null && _renderer.sprite != _fallbackSprite)
                _renderer.sprite = null; // 松开对帧精灵的引用（帧精灵本身在 SpriteBank 缓存里，不在这里释放）
            gameObject.SetActive(false);
            var key = _key;
            Stack<UnitView> bucket;
            if (!Pools.TryGetValue(key, out bucket))
            {
                bucket = new Stack<UnitView>();
                Pools[key] = bucket;
            }
            bucket.Push(this);
        }

        /// <summary>清空所有池（出图时由 `BattleViewRoot` 调，避免跨场景留着一堆隐藏 GameObject）。</summary>
        public static void ClearPools()
        {
            foreach (var bucket in Pools.Values)
            {
                while (bucket.Count > 0)
                {
                    var view = bucket.Pop();
                    if (view != null) Destroy(view.gameObject);
                }
            }
            Pools.Clear();
        }

        private string _key = string.Empty;

        private void Bind(Transform parent, string spriteDir, bool isBuilding)
        {
            _key = (isBuilding ? "B:" : "U:") + (spriteDir ?? string.Empty);
            if (parent != null && transform.parent != parent) transform.SetParent(parent, false);
            _isBuilding = isBuilding;
            ClearLayers();          // 池复用：上一个宿主可能不是同一个建筑（甚至不是建筑）
            // 池复用：上一个宿主的受击闪白状态与计时必须复位，否则新单位会一落地就白着；
            // 渲染器材质由本方法末尾的 `SyncSprite` 重新贴回。
            ResetHitFlash();

            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sortingOrder = ArenaLayers.Instance.Actor;
                // 默认材质必须在**第一次写** `sharedMaterial` 之前记下来（见 `_rendererDefault`）。
                _rendererDefault = SpriteBlendMaterial.RememberDefault(_renderer);
                _animator = new SpriteFrameAnimator(_renderer);
            }
            // 血条：幂等创建（`WorldHpBar.Create` 内部已有则复用并更新参数）。
            if (_hpBar == null)
            {
                // 尺寸口径：单位约 1 格宽，血条取 1.0×0.12（引擎默认值），离脚底 1.6 格
                //（比默认 2.15 低 —— 我们的相机是"一格约一两百像素"的竖屏构图，2.15 会让血条飘太高）。
                // `sortingOrder` 口径：血条必须**高于单位层上界、低于特效层**
                //（推导见 <see cref="SortingLayers.Overlay"/>）。不传的话 = 引擎默认 0
                //（`UIWidgets.CreateQuad` 不写 `sortingOrder`）⇒ 血条被**自己单位的精灵**
                //（<see cref="SortingLayers.Actor"/> = 1000+）盖住 —— 这是"血量看不见"的第二个原因。
                // ⚠️ 这里给的 1.0 / 0.12 / `HpBarYOffset` 都是**世界格**口径：单位根上的逐单位缩放
                //（见 `UnitSpriteScale`）会乘进来，所以 `ApplySpriteScale` 会把这三项按 `1 / scale` 反算回去
                // —— 血条的世界尺寸与离地高度因此与本单位的缩放无关。
                _hpBar = WorldHpBar.Create(transform, 1.0f, 0.12f, HpBarYOffset, LogTag + ".Hp", ArenaLayers.Instance.Overlay);
                _lastHp = -1;
                _lastMaxHp = -1;
            }
            // 池复用必须**显式拉一次可见**：上一个宿主可能把这条血条 `SetVisible(false)` 过
            //（那时它满血/阵亡），而 `_barVisible` 只是本类的镜像、`WorldHpBar` 内部还记着 false。
            // 不拉这一下 ⇒ 同一个池对象第二次被取用时血条再也不出现（无报错的静默失败）。
            if (_hpBar != null) _hpBar.SetVisible(true);
            _barVisible = true;

            if (_spriteDir != (spriteDir ?? string.Empty) || _frames == null)
            {
                _spriteDir = spriteDir ?? string.Empty;
                // ★ 逐帧单位必须用 **统一画布锚点**：导入态下每帧 pivot 是各自裁剪框中心 ⇒ 换帧就位移
                //   （算式见 `SpriteBank.SpritePivotMode.UnifiedCanvasAnchor` 的注释）。
                _pivotMode = SpriteBank.SpritePivotMode.UnifiedCanvasAnchor;
                _spritePath = string.IsNullOrEmpty(_spriteDir)
                    ? string.Empty
                    : (isBuilding ? ResPaths.BuildingDir(_spriteDir) : ResPaths.UnitDir(_spriteDir));
                // 用**预载口径**：帧数组与「帧号 → 下标」映射表一次建好 —— 下面 `AssertFrameMapping`、
                // `ClipIndices`、`BuildLayers` 读同一张表时是纯读（⛔ 不在这条装配路径上现建）。
                _frames = string.IsNullOrEmpty(_spritePath)
                    ? System.Array.Empty<Sprite>()
                    : SpriteBank.Preload(_spritePath, _pivotMode).Frames;
                if (_frames.Length == 0)
                    // 非预期分支必须留痕：素材索引缺失时静默画白块是最难查的现象之一。
                    Game.Logger?.Warn(LogTag, $"精灵取不到，回落占位色：dir={_spriteDir} isBuilding={isBuilding}");
                else
                    AssertFrameMapping(isBuilding);
            }

            // ★ 逐单位缩放（出处见 `UnitSpriteScale`）：必须在血条创建**之后** —— 它同时按 `1 / scale`
            //   抵消父缩放对血条世界尺寸 / 离地高度的影响（见 `ApplySpriteScale`）。
            ApplySpriteScale(_spriteDir);
            // 外观层要在血条世界尺寸定型**之后**建：圆角半径按"这块版面在屏幕上有多少像素"换算，
            // 早于缩放补偿建会把半径算偏（条体越小偏得越多）。
            ApplyHpBarSkin();
            // 贴地阴影要在帧表与缩放都就位之后铺（尺寸/落点都从 `_frames` 与单位根缩放换算）。
            ApplyShadow(isBuilding);
            // 效果件层：与本体同一画布坐标的第二层（只在本体帧位有效果件时才画）。
            // 池复用：效果件层的「当前帧号」必须复位，否则换宿主后会以为「没变」而不重贴。
            _fxFrameNo = 0;
            if (_fxRenderer != null)
            {
                _fxRenderer.sprite = null;
                _fxRenderer.enabled = false;
            }

            // ★ 建筑层配方（见 `BuildingLayerTable`）：有配方的目录**不用**单帧渲染器 —— 它一次只画一帧，
            //   而目录里混着部件帧与敌队配色帧（真正的层序由 `Apply` 按队叠出）。
            if (_renderer != null) _renderer.enabled = !HasBuildingRecipe();

            // 无任何可用帧段的目录（89 个里 44 个）保持"整目录循环"，只报一次（已登记差异，非静默降级）；
            // 部分档位不可用的目录在 ClipIndices 里按档位留痕。
            WarnIfUnmapped(isBuilding, _spriteDir);
            AssertHpBarOrder();

            // 归一档位：档位帧序列由 ClipIndices 现算（帧号语义），播放一律从该档首帧起（动画器 Play 内归零）。
            _anim = AnimIdle;
            _attackReleased = false;
            // 换宿主必须把"已定朝向"复位 —— 新单位的第一帧还没有移动方向，
            // 否则会**继承上一个宿主的朝向**（池复用的静默串味）。
            _viewStep = 4;                     // 4 = 侧身朝右（φ=0°），此时不翻
            _viewStepSet = false;
            _hasMoveDir = false;
            _viewNo = UnitAnimTable.StepToView[_viewStep];
            _team = UnitAnimTable.TeamBlue;
            // 上一宿主的档位推进不许再写这个渲染器（帧表可能已经被换掉）。
            _animator?.Stop();
            // 复用时必须重新贴帧：`Release()` 会松开 sprite 引用（避免池长期持有），
            // 若这里不补回，第二次取用同一个 spriteDir 就会得到"看不见的单位"（无报错的静默失败）。
            _spriteIndex = NotShown;
            PlayTier();
            SyncSprite();
        }

        // ───────────────────────────── 每帧驱动 ─────────────────────────────

        /// <summary>「血条排序层」断言只报一次的集合（每目录一次）。</summary>
        private static readonly HashSet<string> HpBarOrderChecked = new HashSet<string>();

        /// <summary>
        /// 不变量断言：血条的 `sortingOrder` 必须**严格高于单位层上界**
        /// （<see cref="SortingLayers.ActorOrderMax"/>），否则血条会被**自己单位的精灵**盖住
        /// —— 且不报任何错（静默失败）。
        /// 出处：单位层上界 = <see cref="SortingLayers.ActorOrderMax"/> = 1000 + 32 格 × 16 级 ÷ 2 = 1512
        /// （见 <see cref="Apply"/>）。
        /// </summary>
        private void AssertHpBarOrder()
        {
            if (_hpBar == null) return;
            var upper = ArenaLayers.Instance.ActorOrderMax;
            if (_hpBar.SortingOrder <= upper && HpBarOrderChecked.Add(_spriteDir))
                Game.Logger?.Warn(LogTag,
                    $"血条 sortingOrder={_hpBar.SortingOrder} <= 单位层上界 {upper} ⇒ 血条会被单位精灵盖住：dir={_spriteDir}");
        }

        /// <summary>
        /// 用一条（已插值的）实体状态刷新表现。
        /// </summary>
        /// <param name="e">服务端实体快照（`EntitySnapshot`）。</param>
        /// <param name="worldPos">**已插值**的世界坐标（格，竞技场中心为原点）—— 由 `BattleViewRoot` 算好。</param>
        /// <param name="alpha">整条实体的透明度（部署期为半透明，见 `BattleViewRoot` 的说明）。</param>
        /// <param name="moveDir">
        /// **本插值窗口**两端快照的世界位移（格）= 服务端这一步的真实行进向量；
        /// 无上一帧（刚落地）时为 `Vector2.zero`。⛔ 不要传逐帧插值位移（见 `FacingMinMoveTiles`）。
        /// </param>
        public void Apply(EntitySnapshot e, Vector2 worldPos, float alpha, Vector2 moveDir)
        {
            if (e == null) return;

            // z = 同 sortingOrder 下的**确定性次级键**（只由 id 决定的微小偏移；取模基数 / 步长见
            // 引擎 `Runtime/Presentation/SortingLayers.cs:70-74`）。
            transform.position = new Vector3(worldPos.x, worldPos.y, ArenaLayers.Instance.TiebreakOffset(e.id));

            // 朝向 → 视角：用**本窗口两端快照之差**求朝向极角 φ，再查映射表选视角
            //（`UnitAnimTable.StepToView`，权威 = 引擎 `UnitFacingMap.Standard16StepToView`，由
            // 逐视角剪影反解的断言 A1–A6 + 负控保证）。
            // 契约 `facing` ∈ {-1, 1} 只作为"一次都还没动过"时的左右兜底（不翻）。
            // 攻击档播放期间**锁视角**：一档帧序列来自某个视角，换视角 = 换成另一套帧号区间。
            // 挥砍中途换视角会把这一刀换成另一个朝向的动作（画面上是"挥到一半整个人转过去／换了个姿势"），
            // 本刀播完再换（`_viewStep` 回退到本帧之前的值，`_moveDir` 仍照常更新）。
            // 配色侧：`team` 0 = 蓝队（我方）、1 = 红队（敌方）。换侧要重算该档帧序列
            // （红侧是同目录另一套帧），与换视角走同一段"保持档位内位置"的换序列逻辑。
            var teamNow = e.team == UnitAnimTable.TeamRed ? UnitAnimTable.TeamRed : UnitAnimTable.TeamBlue;
            var teamChanged = teamNow != _team;
            if (teamChanged)
            {
                _team = teamNow;
                ApplyHpBarSkin();
            }

            var holdView = _anim == AnimAttack && !AnimDone;
            var prevStep = _viewStep;
            var viewChanged = UpdateFacing(moveDir, e.facing);
            if (viewChanged && holdView)
            {
                _viewStep = prevStep;
                _viewNo = UnitAnimTable.StepToView[prevStep];
                viewChanged = false;
            }
            if (viewChanged || teamChanged)
            {
                // 无帧时动画器里可能还留着上一个宿主的帧表（池复用）⇒ 整段跳过，只让 SyncSprite 贴占位块。
                if (HasFrames)
                {
                    // 转身只是**换角度**、不是换动作 ⇒ 换到新视角的帧序列后**保持档位内位置**
                    //（只夹到新序列长度内），⛔ 不许把走路/攻击从头播（那会让转身看起来像"卡了一下"）。
                    // "已播完"的状态也要一起带过去，否则攻击档会因这次换序列重新变成"在播"而再次扣住让位。
                    var wasDone = AnimDone;
                    var keep = _animator.FrameIndex;
                    PlayTier();
                    var length = _animator.ClipLength;
                    if (length > 0)
                    {
                        _animator.SetFrame(Mathf.Clamp(keep, 0, length - 1));
                        if (wasDone) _animator.Stop();   // 保留当前帧画面，只是不再推进
                    }
                }
                SyncSprite();
            }
            if (_renderer != null) _renderer.flipX = UnitAnimTable.StepFlip(_viewStep);

            // 档位切换：换档要**重置帧**，否则会从上一档的残留帧继续播（现象是"走路的第 30 帧直接接攻击"）。
            // 但**不能每次都照单接受**：服务端的 `anim` 是"瞬时"的 —— 一次挥砍只在挥砍那一 tick 置
            // `AnimAttack`（`server/game/core/combat.go:100` / `:112`），下一 tick 就回 `AnimWalk`
            //（`battle.go:525/533/536-539`）。若每 tick 都切，就会出现"攻击 1 帧 → 走路从第 0 帧重来"，
            // 单位看起来在原地**抽搐**（苍蝇海攻击间隔 1s、攻击段 7 帧时最明显）。
            // 滞回规则（同档不重播 + 攻击播完才让位）：
            //   ① 请求档 == 当前档 ⇒ 什么都不做（⛔ 绝不重新从 0 帧起播）；
            //   ② 当前档是 **attack 且还没播完** ⇒ 扣住不放（除 `AnimDie` —— 死亡无条件立即生效）；
            //   ③ 其余情况正常换档（换档当帧立刻贴新档首帧）。
            var anim = Mathf.Clamp(e.anim, AnimIdle, AnimDie);
            var holdAttack = _anim == AnimAttack && !AnimDone && anim != AnimDie;
            if (anim == _anim)
            {
                // 同档请求：攻击档只有在**这一档期间出现过释放**（`_attackReleased`）时才重播。
                // 服务端把一次挥砍编码成一段 AnimAttack 窗口、窗口之间必然夹着 walk/idle
                // （见 `server/game/core/combat.go` 的 `attackAnimHoldTicks`）⇒ 释放之后又来
                // AnimAttack = **新的一刀**。⛔ 不能只看 `anim != _anim`：同一档的两次挥砍之间
                // `_anim` 一直是 `AnimAttack`，攻击段一旦比攻击间隔长（如弓箭手 1000ms 段 /
                // 900ms 间隔），只看值变化会把每一刀都吞掉，单位看起来"不打"。
                // 只在**这一刀已经播完**时重播：`_attackReleased` 只证明服务端中途请求过别的档位，
                // 当前这一刀可能还在挥砍中（实测同一 clip 内 fi 12/15 → 0）。此时重播会把收招帧截掉，
                // 画面是"挥到一半突然重新起手"。没播完就继续播，播完后本分支下一次会接住新的一刀。
                if (anim == AnimAttack && _attackReleased && AnimDone)
                {
                    _attackReleased = false;
                    PlayTier();     // 从该档首帧重播
                    SyncSprite();
                }
            }
            else if (!holdAttack)
            {
                _anim = anim;
                _attackReleased = false;
                // 换档**重算档位帧序列**并从首帧起播：否则会从上一档的残留帧继续播
                //（现象 = "走路第 30 帧直接接攻击"），或按上一档的帧号序列取帧（档位帧号区间互不相同）。
                PlayTier();
                SyncSprite();
            }
            else
            {
                // 攻击档还没播完，服务端已经在请求 walk/idle ⇒ 扣住这一刀不打断（见上面的滞回规则），
                // 但记下这次释放：之后到来的 AnimAttack 请求要判成新的一刀。
                _attackReleased = true;
            }

            // 半透明（部署期）：原版落点后到 `deploy_time` 结束前是"未激活"表现。
            // 这里用 alpha 表达（不改缩放/位置），并在日志上不刷屏。
            if (_renderer != null)
            {
                var c = _renderer.color;
                c.a = alpha;
                _renderer.color = c;
            }
            if (_fxRenderer != null)
            {
                var fc = _fxRenderer.color;
                fc.a = alpha;
                _fxRenderer.color = fc;
            }

            // 深度排序：俯视视角下"越靠近屏幕下方（y 越小）越靠前"。
            // 粒度 = **16 级/格**（= `SortingLayers.DefaultDepthLevelsPerTile`）。上限推导（⛔ 不许超出层级预算）：
            //   场地 32 格（`GameConst.ArenaTilesH`）× 16 = 512 级 ⇒ order ∈ [Actor−512, Actor+512]
            //   = [488, 1512]（= `ArenaLayers.Instance` 的 `ActorOrderMin..ActorOrderMax`）；
            //   必须 > `SortingLayers.Structure`(50)、< `SortingLayers.Overlay`(2000) —— 两个约束都满足。
            //   1 级 ≈ 1/16 格 ≈ 3.75 px（1080p 竖向构图一格约 60 px），已细于任何两个实体的最小可见纵深差。
            // ⛔ 但 int 粒度到 1/16 格为止：**位置完全重合**的单位（一次 6 只落在同一格）必然同序，
            //   谁盖谁又回到"渲染器枚举顺序"。第二键由 `ArenaLayers.Instance.TiebreakOffset`（微小的 z）给出。
            if (_renderer != null)
                _renderer.sortingOrder = ArenaLayers.Instance.DepthOrder(worldPos.y);
            // 效果件层比本体高 1 级（同一格深度内，效果件画在本体之上；仍低于血条层）。
            if (_fxRenderer != null)
                _fxRenderer.sortingOrder = _renderer != null
                    ? _renderer.sortingOrder + 1
                    : ArenaLayers.Instance.Actor + 1;
            // 阴影恒比本单位低 1 级（单位层内），⛔ 不跨到地面层 / 血条层。
            // 同时按整条实体的透明度（部署期 0.55 / 其它 1.0）换算阴影的 alpha：
            // 阴影是不受单位渲染器材质影响的独立 SpriteRenderer，不在这里同步就会在落位期
            // 出现"半透明的单位 + 实心的黑椭圆"（影子比人还实）。
            if (_shadow != null && _shadow.enabled)
            {
                _shadow.sortingOrder = _renderer != null ? _renderer.sortingOrder - 1 : ArenaLayers.Instance.Actor - 1;
                var sc = _shadow.color;
                sc.a = ShadowAlpha * Mathf.Clamp01(alpha);
                _shadow.color = sc;
            }

            // ★ 建筑：按层配方叠帧（出处见 `BuildingLayerTable`）。有配方 ⇒ 关掉单帧渲染器，
            //   否则它会把目录里的部件帧 / 敌队配色帧也逐帧播出来。
            //   ⛔ 层**一块都没建出来**（帧取不到 / 配方帧号在本目录解析不到 / 该队无配方）时
            //   必须把单帧渲染器保住：`Bind` 是按"该目录有配方"关掉它的，而那时并不知道层能不能
            //   建 —— 不在这里补回，整座建筑会一块都画不出来（连占位块都没有）。
            var drawLayers = _isBuilding && BuildLayers(e.team) && HasAnyLayer();
            if (_renderer != null && _renderer.enabled == drawLayers) _renderer.enabled = !drawLayers;
            if (drawLayers) ApplyLayers(worldPos, alpha);

            UpdateHitFlash(e);

            UpdateHpBar(e.hp, e.max_hp);
        }

        // ───────────────────────────── 受击闪白的驱动 ─────────────────────────────

        /// <summary>
        /// 按快照掉血触发受击闪白（判据与取值出处见文件内「受击闪白」一节）。
        /// <para>⛔ 不复用血条的 <see cref="_lastHp"/>，也不改 <see cref="UpdateHpBar"/> 的调用顺序：
        /// 那边是血条语义（治疗 / 上限变化也更新），这里只判"这一刻是否掉血"。</para>
        /// </summary>
        private void UpdateHitFlash(EntitySnapshot e)
        {
            if (_hitPrevHp == NoHitHp)
            {
                // 第一帧快照只记基线，⛔ 不判受击：出场那帧没有前一帧可比（同 `BattleViewRoot` 的防误报口径）。
                _hitPrevHp = e.hp;
                return;
            }
            var prev = _hitPrevHp;
            _hitPrevHp = e.hp;      // 治疗（hp 变大）也要跟上，否则之后那次掉血判不出来
            if (e.hp < prev) StartHitFlash(e, prev);
        }

        /// <summary>
        /// 触发一次受击闪白：该实体**当前所有在绘的渲染器**换成提亮材质，
        /// <see cref="HitFlashSeconds"/> 后各自还原。连续受击 = **刷新计时**，⛔ 不叠加。
        /// </summary>
        private void StartHitFlash(EntitySnapshot e, int prevHp)
        {
            _hitFlashOn = true;
            // 至少亮到下一帧画完：低帧率（编辑器 10~15 fps）下 83 ms 会落在同一帧内 ⇒ 玩家看不到闪白。
            _hitFlashUntil = Time.time + Mathf.Max(HitFlashSeconds, 2f * Time.deltaTime);
            _hitFlashCount++;
            ApplyHitFlashMaterial(LogMissingFlashMaterial());
            // 数值类证据 = 运行时日志行（闪白只有几十~一百毫秒，截图抓不到 ⇒ 读数靠这条）。
            // `材质=` 是从**渲染器上回读**的名字（不是刚要写的那个局部变量）⇒ 这条行同时证明"真的换上了"。
            Game.Logger?.Info(LogTag,
                $"受击闪白：ent={e.id} team={e.team} hp {prevHp}→{e.hp} add={HitFlashAdd} " +
                $"持续={HitFlashSeconds:F3}s 本局累计={_hitFlashCount} 材质={CurrentRendererMaterialName()}");
        }

        /// <summary>当前写在"在绘渲染器"上的材质名（闪白读数用；取不到材质时回 `—`）。</summary>
        private string CurrentRendererMaterialName()
        {
            Material m = null;
            if (_layers != null)
            {
                for (var i = 0; i < _layers.Length; i++)
                    if (_layers[i] != null) { m = _layers[i].sharedMaterial; break; }
            }
            else if (_renderer != null) m = _renderer.sharedMaterial;
            return m == null ? "—" : m.name;
        }

        /// <summary>本视图累计触发过几次受击闪白（实机读数用）。</summary>
        public int HitFlashCount { get { return _hitFlashCount; } }

        /// <summary>此刻是否正在闪白（实机读数用）。</summary>
        public bool HitFlashOn { get { return _hitFlashOn; } }

        /// <summary>本视图累计在攻击档内按节拍重播过几次（实机读数用）。</summary>
        public int AttackClipReplays { get { return _attackClipReplays; } }

        /// <summary>
        /// 提亮材质的共享实例；取不到（着色器缺失）时留一条 Warn 并返回 <c>null</c> —— 调用方拿到 null
        /// 就什么都不写（渲染器保持当前材质），⛔ 不静默、⛔ 绝不写 <c>null</c> 到渲染器上（洋红）。
        /// </summary>
        private Material LogMissingFlashMaterial()
        {
            var mat = SpriteColorTransformMaterial.ForSprite(255, 255, 255, 255,
                HitFlashAdd, HitFlashAdd, HitFlashAdd);
            if (mat != null) return mat;
            LogThrottle.WarnOnce(LogTag, "hitflash:material",
                $"受击闪白材质取不到（着色器 {SpriteColorTransformMaterial.SpriteShaderName} 缺失）⇒ 单位不闪白（表现缺失）");
            return null;
        }

        /// <summary>把提亮材质写到该实体**当前所有在绘的渲染器**（普通单位 = 单帧渲染器；建筑 = 每一层）。</summary>
        private void ApplyHitFlashMaterial(Material mat)
        {
            if (mat == null) return;
            if (_layers != null)
            {
                // 建筑有层配方时单帧渲染器已被关掉（见 `Apply` / `Bind`），真正的画面是这些层。
                for (var i = 0; i < _layers.Length; i++)
                    if (_layers[i] != null) SpriteBlendMaterial.Set(_layers[i], mat);
                return;
            }
            if (_renderer != null) SpriteBlendMaterial.Set(_renderer, mat);
            // 效果件层是本体的一部分 ⇒ 受击闪白同样作用在它上面。
            if (_fxRenderer != null && _fxFrameNo > 0) SpriteBlendMaterial.Set(_fxRenderer, mat);
        }

        /// <summary>
        /// 闪白计时收尾：到点就把各渲染器还原成"没有闪白时该有的材质" —— 本帧有原版混合就是那份
        /// 混合材质，否则是该渲染器自带的默认材质（建筑层按 <see cref="_layerBaseMaterials"/> 逐层还原）。
        /// </summary>
        private void TickHitFlash()
        {
            if (!_hitFlashOn || Time.time < _hitFlashUntil) return;
            _hitFlashOn = false;
            _hitFlashUntil = 0f;
            RestoreHitFlashMaterials();
        }

        private void RestoreHitFlashMaterials()
        {
            if (_layers != null)
            {
                for (var i = 0; i < _layers.Length; i++)
                {
                    var sr = _layers[i];
                    if (sr == null) continue;
                    var baseMat = _layerBaseMaterials != null && i < _layerBaseMaterials.Length
                        ? _layerBaseMaterials[i] : null;
                    if (baseMat == null)
                    {
                        // 非预期分支必须留痕：还原材质取不到 ⇒ 该层会一直停在提亮材质上（表现错误）。
                        LogThrottle.WarnOnce(LogTag, "hitflash:restore:" + _spriteDir,
                            $"建筑层 {i} 的基础材质取不到 ⇒ 闪白结束后该层无法还原：dir={_spriteDir}");
                        continue;
                    }
                    SpriteBlendMaterial.Set(sr, baseMat);
                }
                return;
            }
            // 效果件层同样还原成它自己那一帧的混合材质（⛔ 不写 null）。
            ApplyFxMaterial();
            if (_renderer == null) return;
            var idx = DisplayedFrameIndex();
            if (idx >= 0) { ApplyBlendMaterial(idx); return; }   // 表内帧会写回该帧的混合材质，表外写回默认
            SpriteBlendMaterial.Set(_renderer, _rendererDefault);
        }

        /// <summary>
        /// 复位受击闪白状态（<see cref="Bind"/> 里池复用必须调）：⛔ 不复位则新单位会继承上一个宿主的
        /// "正在闪白"状态与计时（一落地就白着），且 <see cref="_hitPrevHp"/> 可能让第一帧被误判成受击。
        /// 材质由 <see cref="Bind"/> 末尾的 <see cref="SyncSprite"/> 重新贴回。
        /// </summary>
        private void ResetHitFlash()
        {
            _hitFlashOn = false;
            _hitFlashUntil = 0f;
            _hitFlashCount = 0;
            _hitPrevHp = NoHitHp;
        }

        /// <summary>
        /// 按当前队伍色给血条套外观层（圆角胶囊 + 描边 + 高光），并关掉引擎那条直角填充。
        /// <para>外观层建不出来时退化为"引擎填充 = 队伍色"（⛔ 不留一条看不见的条）。</para>
        /// </summary>
        private void ApplyHpBarSkin()
        {
            if (_hpBar == null) return;
            var fill = _team == UnitAnimTable.TeamRed ? CrUiStyle.HpFillRed : CrUiStyle.HpFillBlue;
            var skin = CrUiStyle.DressHpBar(_hpBar, fill);
            _hpBar.SetFillColor(skin != null ? new Color(0f, 0f, 0f, 0f) : fill);
        }

        /// <summary>只更新血条（塔这类没有 `EntitySnapshot` 的实体由 `ArenaView` 直接调它）。</summary>
        public void UpdateHpBar(int hp, int maxHp)
        {
            if (_hpBar == null) return;
            if (hp == _lastHp && maxHp == _lastMaxHp) return;
            _lastHp = hp;
            _lastMaxHp = maxHp;
            _hpBar.SetHp(hp, maxHp);

            // 血条常显：目标**活着**就显示（含满血）。
            // 出处 = 原版对局图 `策划/基线图/03_对局_1320x2868.jpg`：满血蓝方国王塔的血条读数 `1740`
            //（= 满血）与两座满血公主塔**都画着满格血条**；`策划/基线图/20_对局_1080x1920.jpg` 同。
            // ⛔ 若按 `hp < maxHp` 才显示（满血不显）⇒ 开局全部单位都没有血条 —— 正是"血量看不见"的主因。
            // 空血（`hp <= 0`，服务端已判死）不显示；`maxHp <= 0`（明细缺失）也不显示。
            var visible = maxHp > 0 && hp > 0;
            if (visible != _barVisible)
            {
                _barVisible = visible;
                _hpBar.SetVisible(visible);
            }
        }

        private void Update()
        {
            // 受击闪白的计时（每帧一次）。⛔ 必须放在下面两处早退**之前**：按层渲染的建筑会走第一条早退。
            TickHitFlash();
            // 按层配方渲染的建筑不逐帧换图（见 `BuildLayers`）：换图会把部件帧也播出来。
            if (_layers != null) return;
            if (_animator == null || !HasFrames) return;   // 无帧：渲染器停在占位白块（见 `SyncSprite`）
            // 攻击档播完后服务端仍在请求攻击档 ⇒ 按节拍重播下一刀（见 TickAttackReplay）。
            TickAttackReplay();
            // 帧计时 / 帧下标推进 / 播完停末帧全在引擎动画器里；`sprite` 也由它写
            //（⛔ 只在帧真的变了时写，不是每帧无条件写）。
            _animator.Advance(Time.deltaTime);
            SyncSprite();
            // 效果件层与本体同一条时间轴（本体播到第 i 帧位 ⇒ 该位若有效果件就叠画一层）。
            SyncFxLayer();
        }

        /// <summary>
        /// 按该帧的原版 <c>blend_mode</c> 选材质（出处 = <see cref="SpriteBlendTable"/>，生成物、⛔ 不许手改）。
        /// 表里没有的帧 ⇒ 写回渲染器自带的默认材质（即未登记就是 Normal）；⛔ 绝不写 <c>null</c>（洋红）。
        /// <para>受击闪白期间一直保持提亮材质：帧照播（本方法每次换帧都被 <see cref="SyncSprite"/> 调到），
        /// 若在这里按帧写材料，闪白会被**下一次换帧洗掉**。</para>
        /// </summary>
        private void ApplyBlendMaterial(int frameIndex)
        {
            if (_renderer == null || _frames == null || frameIndex < 0 || frameIndex >= _frames.Length) return;
            if (_hitFlashOn)
            {
                ApplyHitFlashMaterial(LogMissingFlashMaterial());
                return;
            }
            var mat = SpriteBlendMaterial.For(_spritePath,
                SpriteBank.ParseFrameIndex(_frames[frameIndex].name), _rendererDefault);
            SpriteBlendMaterial.Set(_renderer, mat);
        }

        /// <summary>本视图是否有帧可放（`false` = 素材索引缺失，渲染器显示共享占位白块）。</summary>
        private bool HasFrames { get { return _frames != null && _frames.Length > 0; } }

        /// <summary>
        /// 当前档位是否**已播完**（只对 `attack` / `die` 这类"播一次"的档位有意义）。
        /// <para>
        /// 判据 = 引擎动画器播一次到末帧后 <see cref="SpriteFrameAnimator.IsPlaying"/> 变 false
        ///（见其 `Step()`）；循环档（`idle` / `walk`）恒为 `false`。
        /// </para>
        /// </summary>
        private bool AnimDone
        {
            get { return _animator != null && !IsLoopTier(_anim) && !_animator.IsPlaying; }
        }

        /// <summary>`idle` / `walk` 循环播；`attack` / `die` 播一次停在末帧（让位规则见 <see cref="Apply"/>）。</summary>
        private static bool IsLoopTier(int anim)
        {
            return anim == AnimIdle || anim == AnimWalk;
        }

        /// <summary>
        /// 把**当前档位**（`_anim` + `_viewNo`）交给引擎逐帧动画器：帧表 = 整目录，帧下标切片 =
        /// <see cref="ClipIndices"/>（帧号 → 下标换算见 <see cref="ResolveFrame"/>），帧率 = <see cref="FpsFor"/>。
        /// <para>⛔ 每次调用都从该档首帧起播（引擎 `Play` 的语义）⇒ 只在换档 / 换视角 / "新的一刀"时调。</para>
        /// <para>
        /// ⛔ 不用引擎 `SpriteFrameAnimator.SwitchTo` 的两个理由（判据留在业务侧，动画器只负责播）：
        /// ① 它的"同档不重播"会挡下**同一档位的第二次挥砍** —— 那是本项目必须从首帧重播的一刀，
        /// 由 `_attackReleased` 判定（见 <see cref="Apply"/>）；② 它的"播一次未播完时扣住"这条本类
        /// 还要额外放行 `AnimDie`（死亡无条件立即生效），口径比切片的"帧表 + 切片 + 帧率 + 循环"四元组更宽。
        /// </para>
        /// </summary>
        private void PlayTier()
        {
            if (_animator == null || !HasFrames) return;
            var clip = ClipIndices(_spriteDir, _anim, _viewNo, _team);
            // die 档没有帧段 ⇒ **保持当前画面不动**，⛔ 不回落整目录。整目录不是死亡动画：`_frames` 是那个
            // 目录的**全部**帧（如 `chr_knight_out` 486 张，含 9 个视角与 idle/walk/attack 各档），
            // 从它的第 0 帧起播会把单位当场换成一张无关姿势。原版单位没有死亡动画
            // （`策划/单位帧段表.md` 逐目录核过，89 个目录的 die 档全部"未找到"），死亡表现由通用死亡特效
            // 承担（`ResPaths` 的 `Death_blue` / `Death_purple` 族，见其注释）；服务端只在实体消失前的
            // 那一帧下发 `anim=die`（`server/game/core/battle.go` 的 `compact` 口径）⇒ 这一帧只需维持原样。
            // `ClipLength > 0` 一档都没播过时无画面可保（此时仍走下面的整目录兜底，免得贴出占位白块）。
            if (clip == null && _anim == AnimDie && _animator.ClipLength > 0)
            {
                _animator.Stop();
                return;
            }
            _animator.Play(_frames, clip, FpsFor(_spriteDir, _anim, _viewNo, _team), IsLoopTier(_anim));
            if (_anim == AnimAttack)
            {
                // 攻击档的节拍从**本拍起播**算起：下一次重播落在 `hit_speed_ms` 之后（见 TickAttackReplay）。
                _attackClipStart = Time.time;
                _attackInterval = AttackIntervalSeconds(_spriteDir);
            }
            else
            {
                _attackInterval = 0f;
            }
        }

        /// <summary>该目录两次挥砍的间隔（秒）= `hit_speed_ms / 1000`；表里没有该目录 / 节拍为 0 ⇒ `0`。</summary>
        private static float AttackIntervalSeconds(string dir)
        {
            UnitAnimTable.Entry entry;
            return UnitAnimTable.TryGet(dir ?? string.Empty, out entry) && entry.HitSpeedMs > 0
                ? entry.HitSpeedMs / 1000f
                : 0f;
        }

        /// <summary>
        /// 攻击档已播完、但服务端仍在请求攻击档（`anim == 2` 持续）⇒ 按该卡的攻击节拍从头重播。
        /// <para>服务端的 `anim` 是**状态**：连续挥砍期间一直是 2，两次挥砍之间不夹 idle/walk ⇒
        /// `Apply` 的 `_attackReleased` 分支等不到让位信号，攻击段会停在末帧、连续挥砍只看得见第一刀。
        /// 节拍来源 = 同一份 `UnitAnimTable` 的 `HitSpeedMs`（与服务端两次挥砍间隔同值），⛔ 不自定节奏。</para>
        /// </summary>
        private void TickAttackReplay()
        {
            if (_anim != AnimAttack || _animator == null || !HasFrames) return;
            if (_attackInterval <= 0f || !AnimDone) return;
            if (Time.time - _attackClipStart < _attackInterval) return;
            PlayTier();       // 从该档首帧重播，并刷新 _attackClipStart
            SyncSprite();
            _attackClipReplays++;
        }

        /// <summary>
        /// 补上"引擎动画器不管、但渲染需要"的附随状态：该帧的原版混合材质（<see cref="SpriteBlendTable"/>）／
        /// 无帧时的共享占位白块。<see cref="SpriteFrameAnimator"/> 只在帧真的变了时写
        /// `SpriteRenderer.sprite`，本方法同理**只在显示的帧变了时**动手（⛔ 不每帧写）。
        /// </summary>
        private void SyncSprite()
        {
            if (_renderer == null) return;
            var idx = DisplayedFrameIndex();
            if (idx == _spriteIndex) return;
            _spriteIndex = idx;
            if (idx < 0)
            {
                // 无帧：引擎动画器对空帧表不写渲染器（见其 `Begin` 的空表分支）。
                // 本视图**已经显示过有效帧**时保持那一帧（不换画面 = 不丢模型）；一帧都没有才贴占位白块。
                var shown = _renderer.sprite;
                if (shown != null && shown != _fallbackSprite) return;
                LogThrottle.WarnOnce(LogTag, "sprite:none:" + _spriteDir,
                    $"无可用帧 ⇒ 贴占位色块：dir={_spriteDir} anim={TierName(_anim)} view={_viewNo}");
                _renderer.sprite = FallbackSprite();
                SpriteBlendMaterial.Set(_renderer, _rendererDefault);
                return;
            }
            ApplyBlendMaterial(idx);
        }

        /// <summary>
        /// 引擎动画器当前**显示的帧数组下标**（`-1` = 无帧可放）。
        /// <para>
        /// 用了帧切片时 <see cref="SpriteFrameAnimator.FrameIndex"/> 是**档位内位置**，要过切片
        /// （<see cref="SpriteFrameAnimator.ClipIndices"/>）才是帧数组下标；没用切片时位置即下标。
        /// </para>
        /// </summary>
        private int DisplayedFrameIndex()
        {
            if (_animator == null || !HasFrames) return -1;
            var length = _animator.ClipLength;
            if (length == 0) return -1;
            var pos = Mathf.Clamp(_animator.FrameIndex, 0, length - 1);
            var clip = _animator.ClipIndices;
            var idx = clip != null ? clip[pos] : pos;
            return idx >= 0 && idx < _frames.Length ? idx : -1;
        }

        /// <summary>朝向档切换的**滞回半宽**（单位 = 档；0.25 档 = 5.625°）—— 避免在档边界上左右跳。</summary>
        private const float ViewHysteresisSteps = 0.25f;

        /// <summary>
        /// 判定"确实在走"所需的**最小窗口位移**（格）。
        /// <para>
        /// ⛔ 不能取 `1e-4` 这一档：服务端位置是**毫格**（<c>1e-3</c> 格）量化的，站着不动的单位
        /// 也会因为插值窗口切换算出 ±2 毫格的位移；那个门槛会把这种**舍入噪声**当成真实移动方向，
        /// 于是朝向在 180° 两侧反复翻（逐帧实测：<c>id=320</c>
        /// frame 6062..6066 的 vstep 片段 <c>[4, 12, 12, 12, 4]</c>，而同期逐帧位移只有
        /// <c>-0.002 格</c> —— 现象就是"人物又飘又抖"）。
        /// </para>
        /// <para>
        /// 取值 0.01 格 = **10 倍量化步长**，同时远低于最小真实移速对应的窗口位移
        /// （0.1 格/秒 × 100ms 快照间隔 = 0.01 格）⇒ 既滤掉噪声，也不会让慢速单位定不了朝向。
        /// </para>
        /// </summary>
        private const float FacingMinMoveTiles = 0.01f;

        /// <summary>
        /// 由**窗口位移**更新朝向档；返回 `_viewNo` 是否变化（变化时调用方必须把新视角的帧序列重新交给动画器）。
        /// <para>
        /// 算式（同引擎 `clover-client-unity-engine/Runtime/Presentation/UnitFacingMap.cs` 的 `StepForHeading`）：
        /// `φ = atan2(dy, dx)`（度，+x 起算、+y = 远离镜头）
        /// ⇒ `u = (90° − φ) / 22.5°`（连续档位，0 = 背身）⇒ `step = round(u) mod 16`。
        /// </para>
        /// <para>
        /// <b>为什么入参是"窗口位移"而不是"逐帧插值位移"</b>：一个插值窗口内 `t` 从 0 推到 1，
        /// 逐帧位移 = 窗口位移 × Δt，方向本应恒定；但服务端的毫格量化会让站立单位的逐帧位移
        /// 在 ±0.002 格之间抖，方向随之翻 180°。窗口位移 = 两端快照之差 = **服务端这一步的
        /// 真实行进向量**，在一个窗口内恒定 ⇒ 朝向天然稳定（见 <see cref="FacingMinMoveTiles"/>）。
        /// </para>
        /// <para>站桩（窗口位移低于门槛）保持上一次朝向；一次都没动过 ⇒ 用 `facing` 的左右兜底。</para>
        /// </summary>
        private bool UpdateFacing(Vector2 moveDir, int facing)
        {
            if (moveDir.sqrMagnitude >= FacingMinMoveTiles * FacingMinMoveTiles)
            {
                _moveDir = moveDir;
                _hasMoveDir = true;
            }

            var dirv = _hasMoveDir ? _moveDir : new Vector2(facing >= 0 ? 1f : -1f, 0f);
            var u = (90f - Mathf.Atan2(dirv.y, dirv.x) * Mathf.Rad2Deg) / 22.5f;

            var step = _viewStep;
            if (!_viewStepSet)
            {
                _viewStepSet = true;
                step = Wrap16(Mathf.RoundToInt(u));      // 首次直接吸附（不做滞回）
            }
            else
            {
                var diff = u - _viewStep;
                diff -= Mathf.Round(diff / 16f) * 16f;   // 折到 [-8, 8]（环绕）
                if (Mathf.Abs(diff) > 0.5f + ViewHysteresisSteps) step = Wrap16(Mathf.RoundToInt(u));
            }

            if (step == _viewStep) return false;
            _viewStep = step;
            _viewNo = UnitAnimTable.StepToView[step];

            int[] cnt;
            if (!ViewSwitchCount.TryGetValue(_spriteDir ?? string.Empty, out cnt))
            {
                cnt = new int[9];
                ViewSwitchCount[_spriteDir ?? string.Empty] = cnt;
            }
            cnt[_viewNo - 1]++;
            return true;
        }

        private static int Wrap16(int v)
        {
            v %= 16;
            return v < 0 ? v + 16 : v;
        }

        /// <summary>
        /// 当前档位的**帧下标序列**（把帧段表的**帧号**经 <see cref="SpriteBank.FrameNumberMap"/> 换算成下标）。
        /// 返回 `null` = 该档没有可用帧段（目录未收录 / 该档 `Known==false` / 帧号一帧都解析不出）
        /// ⇒ 调用方回落「整目录」。
        /// </summary>
        private int[] ClipIndices(string dir, int anim, int view, int team)
        {
            if (_frames == null || _frames.Length == 0 || _spritePath.Length == 0) return null;
            if (anim < 0 || anim > AnimDie) return null;

            // 键含配色侧：同一目录的蓝侧 / 红侧是同目录里两套帧号区间，不能共用一条缓存。
            var key = (int)_pivotMode + "|" + _spritePath + "|" + anim + "|" + team + "|" + view;
            int[] cached;
            if (ClipIndexCache.TryGetValue(key, out cached)) return cached.Length == 0 ? null : cached;

            int[] result = null;
            UnitAnimTable.Entry entry;
            if (UnitAnimTable.TryGet(dir, out entry) && entry.Tiers != null
                && anim < entry.Tiers.Length && entry.Tiers[anim].Known)
            {
                var clip = entry.Tiers[anim];
                // 优先取**当前朝向对应视角**的帧段（`ViewRuns[view-1]`，出处 = `UnitAnimTable` 的逐视角帧段表）；
                // 敌方（`team=1`）取红侧 `ViewRunsRed`；该视角无素材 ⇒ 回落该配色的默认视角。
                var runs = UnitAnimTable.RunsForView(clip, view, team) ?? UnitAnimTable.RunsForTeam(clip, team);
                var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);
                var list = ExpandRuns(runs, map);
                if (list.Count == 0)
                {
                    // 该视角的帧段一帧都解析不出 ⇒ 依次试同档其它视角（同一动作的不同角度），
                    // 命中即用；全部落空才回落整目录。只留一条 Warn（每目录 × 档位）。
                    for (var v = 1; v <= 9 && list.Count == 0; v++)
                    {
                        if (v == view) continue;
                        list = ExpandRuns(UnitAnimTable.RunsForView(clip, v, team), map);
                    }
                    if (list.Count > 0)
                        LogThrottle.WarnOnce(LogTag, "tierview:" + dir + ":" + anim,
                            $"该视角帧段 0 帧 ⇒ 回落同档其它视角：dir={dir} anim={TierName(anim)} view={view}");
                }
                if (list.Count >= 1) result = list.ToArray();
                else
                    // 非预期分支必须留痕：段表有帧、但帧号→下标**一帧都解析不出**（非恒等目录的缺项）⇒
                    // 静默变"木头人"是最难查的。去重走引擎的**进程级**闸门（键 = 目录）。
                    LogThrottle.WarnOnce(LogTag, "tier:" + dir,
                        $"帧段解析出的可用帧 0 帧（该目录帧号→下标全部缺项）⇒ 该档回落整目录：dir={dir} anim={TierName(anim)} view={view} 段表帧数={UnitAnimTable.CountOfRuns(runs)} frames.Length={_frames.Length}");
            }
            else if (!HasBuildingRecipe())
                // 目录在表里但该档不可用（素材无此动画 / `.sc` 引用越界 shapeID）⇒ 整目录（已登记差异，每目录一次）。
                // 有建筑层配方的目录**不发这句**：它的单帧渲染器在 `Apply` 里已关（`enabled = !HasBuildingRecipe()`），
                // 真正的层序由 `BuildingLayerTable` 叠出（逐层读数见 `BuildLayers`）⇒ 「整目录循环播」与事实相反。
                LogThrottle.WarnOnce(LogTag, "tier:" + dir,
                    $"该档位不可用（素材无此动画 / `.sc` 越界 shapeID）：dir={dir} anim={TierName(anim)}"
                    + (anim == AnimDie
                        ? " ⇒ die 保持当前画面不动（原版单位没有死亡动画，见 `PlayTier`）"
                        : " ⇒ 整目录循环播（已登记差异）"));

            ClipIndexCache[key] = result ?? System.Array.Empty<int>();
            return result;
        }

        /// <summary>
        /// **帧号 → 帧数组下标**（⛔ 不假设恒等）。`map` 来自 <see cref="SpriteBank.FrameNumberMap"/>：
        /// `map[n]` = 帧号 `n` 对应的下标，取不到 = `-1`。返回 `-1` = 该帧号在此目录**不存在**
        /// （非恒等目录，如 `chr_giant_out`：`.sc` 的 shapeID 张数 > 本地 PNG 张数）⇒ **跳过该帧**
        /// 而不是用邻帧顶替（顶替会把别的动作的帧混进来 —— 静默错帧比缺帧难查得多）。
        /// </summary>
        private static int ResolveFrame(int[] map, int frameNo)
        {
            if (map != null && map.Length > 0)
                return (frameNo >= 0 && frameNo < map.Length) ? map[frameNo] : -1;
            return frameNo;   // 没有映射表（理论上不会发生：调用前必已取过）⇒ 退化为恒等
        }

        /// <summary>把帧段表（帧号）展开成帧数组下标；跳过本目录不存在的帧号。</summary>
        private List<int> ExpandRuns(int[] runs, int[] map)
        {
            if (runs == null) return new List<int>(0);
            var list = new List<int>(UnitAnimTable.CountOfRuns(runs));
            for (var r = 0; r + 1 < runs.Length; r += 2)
                for (var i = 0; i < runs[r + 1]; i++)
                {
                    var idx = ResolveFrame(map, runs[r] + i);
                    if (idx >= 0 && idx < _frames.Length) list.Add(idx);
                }
            return list;
        }

        /// <summary>档位名（日志/断言用）。</summary>
        private static string TierName(int anim)
        {
            switch (anim)
            {
                case AnimIdle: return "idle";
                case AnimWalk: return "walk";
                case AnimAttack: return "attack";
                case AnimDie: return "die";
                default: return "anim" + anim;
            }
        }

        /// <summary>
        /// 当前目录+档位的播放帧率（帧/秒）= **帧数 ÷ 该档的原版时长**。时长与帧数的出处：
        /// <list type="bullet">
        /// <item>**时长**：<see cref="UnitAnimDurations"/>（= `策划/单位动画分组表.md` 里该档那条 export 的
        /// `timeline 帧数 ÷ FPS`）。这就是原版播这一档用的时间。</item>
        /// <item>**帧数**：<see cref="UnitAnimTable"/> 的帧段表按**当前视角**数（各视角帧数可能不同）。</item>
        /// </list>
        /// ⛔ **不许直接拿 `.sc` 的 FPS 播像素帧**：`Runs` 里存的是**唯一像素帧**，timeline 里的重复帧
        /// 已被折叠（`axe_man_run1_5` = timeline 38 步 / 8 张像素帧 / 60 fps ⇒ 原版周期 0.633 s；
        /// 按 60 fps 播那 8 张只有 0.133 s，**快 4.75 倍** —— 现象就是"走起来在发抖"）。
        /// <para><b>attack 的时长上限</b>：一次挥砍的动画必须**在下一刀到来前播完**，否则每刀都会被
        /// 下一次挥砍切掉尾巴再从头播（现象 = 攻击段永远播不到收招帧）⇒ 时长 = `min(原版攻击段时长,
        /// hit_speed_ms)`。两者出处：原版时长 = `策划/单位动画分组表.md` 的 `timeline ÷ FPS`，
        /// `hit_speed_ms` = `server/game/table/tsv/unit.tsv`（同一个值就是服务端两次挥砍的间隔）。
        /// 例：archer 原版段 1.000 s &gt; hit 0.900 s ⇒ 按 0.900 s 播。</para>
        /// <para>兜底（分组表里查不到该 export / 目录未收录 / 帧数为 0）：idle = 不播（`0f`）、
        /// attack = 帧数 ÷ `hit_speed_ms`、walk/die = `.sc` 自带 FPS；再兜底 = <see cref="AnimFps"/>。
        /// 单帧档无论时长多大都不会动（帧序列只有一帧，换帧无处可去）。</para>
        /// </summary>
        private static float FpsFor(string dir, int anim, int view, int team)
        {
            UnitAnimTable.Entry entry;
            if (UnitAnimTable.TryGet(dir ?? string.Empty, out entry) && entry.Tiers != null
                && anim >= 0 && anim < entry.Tiers.Length && entry.Tiers[anim].Known)
            {
                var clip = entry.Tiers[anim];
                var runs = UnitAnimTable.RunsForView(clip, view, team) ?? UnitAnimTable.RunsForTeam(clip, team);
                var frames = UnitAnimTable.CountOfRuns(runs);
                int[] durations;
                var hasDur = UnitAnimDurations.TryGet(dir ?? string.Empty, out durations)
                             && anim < durations.Length && durations[anim] > 0;
                var segMs = hasDur ? (float)durations[anim] : 0f;

                if (anim == AnimAttack && frames > 0 && entry.HitSpeedMs > 0)
                {
                    var capMs = segMs > 0f && segMs < entry.HitSpeedMs ? segMs : entry.HitSpeedMs;
                    return frames * 1000f / capMs;
                }
                if (frames > 0 && segMs > 0f) return frames * 1000f / segMs;
                if (anim == AnimIdle) return 0f;                                  // 静止帧：不播
                if (entry.ScFps > 0) return entry.ScFps;                          // walk / die：.sc 自带帧率
            }
            return AnimFps[Mathf.Clamp(anim, 0, AnimFps.Length - 1)];
        }

        /// <summary>清「档位帧下标序列」缓存（由 <see cref="SpriteBank.ClearCache"/> 同步调 —— 帧缓存没了，下标也就失效了）。</summary>
        internal static void ClearClipCache()
        {
            ClipIndexCache.Clear();
        }

        // ───────────────────────────── 断言 / 留痕（各只报一次）─────────────────────────────

        /// <summary>「该目录帧段接入情况」只报一次的集合。</summary>
        private static readonly HashSet<string> ClipSummaryLogged = new HashSet<string>();

        /// <summary>
        /// 单位目录：算一次「帧号 → 下标」映射（内部打印恒等断言，见 <see cref="SpriteBank.FrameNumberMap"/>），
        /// 打印该目录的**帧段接入情况**（frames.Length / 是否恒等 / 4 档可用性与帧数 / ScFps / hit_speed_ms），
        /// 并做「攻击段时长 ↔ `hit_speed_ms`」断言。⛔ 建筑（塔/竞技场是多子图目录，下标 ≠ 帧号）不在本类职责内。
        /// </summary>
        private void AssertFrameMapping(bool isBuilding)
        {
            if (isBuilding || string.IsNullOrEmpty(_spritePath)) return;
            var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);
            var identity = map.Length == _frames.Length;
            for (var n = 0; identity && n < map.Length; n++) if (map[n] != n) identity = false;

            if (ClipSummaryLogged.Add(_spriteDir))
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("帧段接入：dir=").Append(_spriteDir)
                  .Append(" frames.Length=").Append(_frames.Length)
                  .Append("（== 该目录 PNG 张数 == AS1 指纹里的 ShapeCount）")
                  .Append(" map.Length=").Append(map.Length)
                  .Append(identity ? " 恒等（帧号 == 下标）" : " **非恒等**（下标 != 帧号）");
                UnitAnimTable.Entry entry;
                if (UnitAnimTable.TryGet(_spriteDir, out entry) && entry.Tiers != null)
                {
                    for (var a = 0; a < UnitAnimTable.TierCount; a++)
                        sb.Append(" | ").Append(TierName(a)).Append('=')
                          .Append(a < entry.Tiers.Length && entry.Tiers[a].Known
                              ? entry.Tiers[a].Count + "帧" : "Unknown");
                    sb.Append(" | ScFps=").Append(entry.ScFps)
                      .Append(" hit_speed_ms=").Append(entry.HitSpeedMs);
                }
                else sb.Append(" | 未收录帧段表 ⇒ 整目录循环");
                Game.Logger?.Info(LogTag, sb.ToString());
            }
            AssertAttackTiming(_spriteDir);
        }

        // ★ 「档位不可用 ⇒ 整目录」与「帧段解析不出」这两条告警的生命周期 = **进程级**，
        //   统一走引擎的 `LogThrottle.WarnOnce`
        //   （`Runtime/Core/LogThrottle.cs:164`，键 = `"tier:" + dir`），⛔ 不自持第二套去重集合。
        //   引擎台账口径：「不再写第二套去重（已有同类能力不准再起第二套）」。

        /// <summary>目录**一个可用档位都没有**（未收录 / 4 档全 Unknown）的**单位**目录只报一次
        /// （整目录循环 = 已登记差异，非静默降级）。</summary>
        private static readonly HashSet<string> UnmappedWarned = new HashSet<string>();

        private static void WarnIfUnmapped(bool isBuilding, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (isBuilding)
            {
                // 建筑：有层配方 ⇒ 按配方叠帧（无告警）；没有 ⇒ 仍是「整目录逐帧循环」——
                // 那种目录里混着部件帧与敌队配色帧，**必须留痕**，不静默降级。
                BuildingLayerTable.Layer[] tmp;
                if (BuildingLayerTable.TryGet(dir, BuildingLayerTable.TeamBlue, out tmp)
                    || BuildingLayerTable.TryGet(dir, BuildingLayerTable.TeamRed, out tmp)) return;
                if (BuildingRecipeWarned.Add(dir))
                    Game.Logger?.Warn(LogTag,
                        $"建筑目录无层配方，仍按「整目录逐帧循环」播（会把部件帧 / 敌队配色帧也播出来）：dir={dir}");
                return;
            }
            UnitAnimTable.Entry entry;
            var any = false;
            if (UnitAnimTable.TryGet(dir, out entry) && entry.Tiers != null)
                for (var a = 0; a < entry.Tiers.Length; a++)
                    if (entry.Tiers[a].Known) { any = true; break; }
            if (any) return;
            if (UnmappedWarned.Add(dir))
                Game.Logger?.Warn(LogTag,
                    $"目录无任何可用帧段，按整目录循环播（已登记差异）：dir={dir}（帧段表共收录 {UnitAnimTable.DirCount} 个目录）");
        }

        /// <summary>「攻击段帧数 ↔ `hit_speed_ms`」自洽断言的去重集合。</summary>
        private static readonly HashSet<string> AttackTimingLogged = new HashSet<string>();

        /// <summary>
        /// 打印**攻击段的播放口径**（每个已收录目录一次），一行能读出每张卡的三个量：
        /// 帧数 / **原版段时长**（<see cref="UnitAnimDurations"/> = 分组表 `timeline ÷ FPS`）/
        /// **`hit_speed_ms`**（两次挥砍的间隔），以及本档**是否切尾**
        /// （原版段比间隔长 ⇒ 必须按间隔播，尾部帧播不到）。
        /// 出处：`策划/单位动画分组表.md`（时长）+ `server/game/table/tsv/unit.tsv`（`hit_speed_ms`）。
        /// </summary>
        private static void AssertAttackTiming(string dir)
        {
            UnitAnimTable.Entry entry;
            if (!UnitAnimTable.TryGet(dir, out entry) || entry.Tiers == null
                || AnimAttack >= entry.Tiers.Length || !entry.Tiers[AnimAttack].Known) return;
            if (!AttackTimingLogged.Add(dir)) return;

            var count = entry.Tiers[AnimAttack].Count;
            var hitMs = entry.HitSpeedMs;
            if (hitMs <= 0)
            {
                Game.Logger?.Warn(LogTag,
                    $"目录已收录帧段但 unit.tsv 里查不到 hit_speed_ms ⇒ attack 用默认帧率（已登记差异）：dir={dir}");
                return;
            }
            int[] durations;
            var hasDur = UnitAnimDurations.TryGet(dir, out durations) && durations[AnimAttack] > 0;
            var segSec = hasDur ? durations[AnimAttack] / 1000f : 0f;
            var hitSec = hitMs / 1000f;
            // 切尾 = 原版攻击段比挥砍间隔长 ⇒ 只能按 hit_speed 播，尾部帧播不到（FpsFor 的口径）。
            var cut = hasDur && segSec > hitSec;
            var basis = !hasDur ? "hit_speed（无原版时长）"
                : cut ? $"hit_speed（原版段长 {(segSec - hitSec) / hitSec * 100f:+0.0;-0.0}%）"
                : "原版段";
            var playSec = hasDur ? (cut ? hitSec : segSec) : hitSec;
            Game.Logger?.Info(LogTag,
                $"攻击段口径：dir={dir} 帧数={count} 原版段={(hasDur ? segSec.ToString("F3") + "s" : "无")} " +
                $"hit_speed={hitSec:F3}s 切尾={(cut ? "是" : "否")} ⇒ 按 {basis} 播（{count / playSec:F2} fps）");
        }

        private static Sprite FallbackSprite()
        {
            // 复用 SpriteBank 的共享白块（pixelsPerUnit = 1 ⇒ 该块正好占 1 个世界单位 = 1 格，
            // 与单位自身的占地大小一致，不会出现"缺素材的单位大得离谱"）。
            if (_fallbackSprite == null) _fallbackSprite = SpriteBank.WhiteSprite();
            return _fallbackSprite;
        }
    }

    /// <summary>
    /// 本项目唯一的 2D 层级预算（引擎 <see cref="SortingLayers"/> 的默认表），唯一定义处 —— 避免各 View
    /// 各写一个数字导致"底图盖住单位"。
    /// <para>
    /// 约定（后画 = 数值大）：底图 <see cref="SortingLayers.Ground"/> 0 / 装饰
    /// <see cref="SortingLayers.Decoration"/> 10 / **塔** <see cref="SortingLayers.Structure"/> 50 /
    /// **落点指示** <see cref="SortingLayers.Indicator"/> 200 / **单位** <see cref="SortingLayers.Actor"/> 1000
    /// +（世界 y 深度，16 级/格、32 格 ⇒ ±512）/ **血条** <see cref="SortingLayers.Overlay"/> 2000 /
    /// **特效** <see cref="SortingLayers.Effect"/> 3000。
    /// </para>
    /// <para>
    /// 单位给到 1000 是因为它要按世界 y 做深度排序（±512），与塔的 50 之间留足空隙，
    /// 避免"站在塔前面的兵被塔盖住"；血条的 2000 必须**高于单位层上界** 1512、低于特效层 3000
    /// （上下界 = <see cref="SortingLayers.ActorOrderMin"/> / <see cref="SortingLayers.ActorOrderMax"/>）。
    /// </para>
    /// <para>场地纵向格数出处：`client/Assets/Scripts/Core/GameConst.cs:43`（<c>ArenaTilesH = 32f</c>）。</para>
    /// </summary>
    public static class ArenaLayers
    {
        /// <summary>
        /// 层级预算实例。<see cref="SortingLayers.DepthOrder"/>（单位深度序）与
        /// <see cref="SortingLayers.TiebreakOffset"/>（同序次级键）都从这里取。
        /// </summary>
        public static readonly SortingLayers Instance = new SortingLayers(GameConst.ArenaTilesH);
    }

    /// <summary>
    /// 精灵目录仓（项目侧入口）：类型名与成员名保持本项目既有调用面，实现全部委托引擎
    /// <see cref="CloverEngine.FrameBank"/>（整目录抓帧 / 帧号排序 / 帧号→下标映射 / 统一画布锚点 / 生命周期）。
    /// <para>
    /// <b>为什么必须整目录取帧</b>：逐帧名 `LoadAsset&lt;Sprite&gt;(path, cb)` 取不到
    /// （引擎契约 `clover-client-unity-engine/Runtime/Core/Contracts.cs:1107-1124`），
    /// 只有整条取才拿得到 ⇒ 统一走 <see cref="CloverEngine.IResourceManager.LoadAll{T}(string)"/>
    /// （同步、阻塞主线程，请在进图前 / 读条阶段调用，⛔ 不要在战斗热路径里第一次调用）。
    /// </para>
    /// <para>
    /// 锚点模式 <see cref="SpritePivotMode"/> 与引擎 <see cref="CloverEngine.FrameBank.PivotMode"/>
    /// 的取值一一对应：<c>AsImported = 0</c> / <c>UnifiedCanvasAnchor = 1</c>。
    /// </para>
    /// </summary>
    public static class SpriteBank
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "SpriteBank";

        /// <summary>
        /// 由 `Texture2D` 现场造 `Sprite` 时使用的 PPU（= 引擎默认 100 px/单位，见
        /// <see cref="CloverEngine.FrameBank.DefaultPixelsPerUnit"/>）。
        /// </summary>
        public const float FallbackPixelsPerUnit = FrameBank.DefaultPixelsPerUnit;

        /// <summary>现场造出来的 Sprite 的名字前缀（引擎按它区分"本类造的"与"导入/引擎给的"）。</summary>
        public const string GeneratedSpritePrefix = FrameBank.GeneratedSpritePrefix;

        /// <summary>精灵的**锚点模式**（<see cref="LoadDir(string, SpritePivotMode)"/> 的第二参）。</summary>
        public enum SpritePivotMode
        {
            /// <summary>
            /// 按导入设置原样用。多 Sprite 导入时**每帧的 pivot 是自己那张裁剪框的中心**
            /// ⇒ 逐帧裁剪框不同 ⇒ 换帧时整帧被重新居中。只在"每档只用一帧"的目录上安全。
            /// </summary>
            AsImported = (int)FrameBank.PivotMode.AsImported,

            /// <summary>
            /// 全目录共用一个**纹理坐标锚点**（= 该目录所有帧 `rect` 并集的中心）重建每一帧。
            /// 逐帧动画目录（单位 / 特效）必须走这个：素材是"一个固定画布 + 每帧一张紧裁剪 PNG"，
            /// 导入态 pivot 会让每换一帧人物在自己的画布上**重新居中**一次（"贴图抖动"）。
            /// </summary>
            UnifiedCanvasAnchor = (int)FrameBank.PivotMode.UnifiedCanvasAnchor,
        }

        /// <summary>全项目共用的目录仓实例（引擎件在主线程使用，非线程安全）。</summary>
        private static readonly FrameBank Bank = new FrameBank(null, FallbackPixelsPerUnit);

        /// <summary>上一次**取成功**的帧（键 = 锚点模式 + 目录）；见 <see cref="LoadDir(string, SpritePivotMode)"/>。</summary>
        private static readonly Dictionary<string, Sprite[]> LastGood =
            new Dictionary<string, Sprite[]>(System.StringComparer.Ordinal);

        /// <summary>取某目录下**全部**帧（同步），按帧号升序排；锚点按导入设置原样。</summary>
        /// <returns>帧数组（长度 0 = 该目录没有可用资源；⛔ 不返回 null）。</returns>
        public static Sprite[] LoadDir(string path)
        {
            return LoadDir(path, SpritePivotMode.AsImported);
        }

        /// <summary>取某目录下**全部**帧（同步），按帧号升序排，并按 <paramref name="mode"/> 决定锚点。</summary>
        /// <param name="path">`Assets/Resources/` 下的相对目录（`ResPaths` 组装）。</param>
        /// <param name="mode">锚点模式；逐帧单位 / 特效用 <see cref="SpritePivotMode.UnifiedCanvasAnchor"/>。</param>
        /// <returns>帧数组（长度 0 = 该目录没有可用资源；⛔ 不返回 null）。</returns>
        public static Sprite[] LoadDir(string path, SpritePivotMode mode)
        {
            var frames = Bank.LoadDir(path, (FrameBank.PivotMode)(int)mode);
            var key = CacheKey(mode, path);
            if (frames != null && frames.Length > 0)
            {
                LastGood[key] = frames;
                return frames;
            }

            // 引擎把「本帧取不到」的**空数组**也写进了它自己的缓存（`FrameBank.LoadDir` 的缓存有效性检查
            // 把长度 0 当成有效条目）⇒ 同一目录此后**不再重试**。于是一次短时取不到（引擎重启之间的
            // `Game.Res` 为空、编辑器正在重导入这批资产）会让该目录在本次进程内**永久**取不到 ——
            // 表现 = 落地 / 命中 / 弹道特效整段静默消失，零报错。
            // 处置：本目录此前取成功过就沿用那一份（⛔ 不把空数组交给调用方）。
            Sprite[] good;
            if (LastGood.TryGetValue(key, out good) && good != null && good.Length > 0 && good[0] != null)
            {
                LogThrottle.WarnOnce(LogTag, "LoadDir.LastGood/" + key,
                    $"整目录取帧本次为空（引擎已把这一空结果缓存下来 ⇒ 不会自己重试）：dir={path} 模式={mode}" +
                    $" ⇒ 沿用本目录**上一次取成功**的 {good.Length} 帧（特效不会静默不播）");
                return good;
            }
            return frames;
        }

        /// <summary>
        /// 预载某目录：**帧数组 +「帧号 → 帧数组下标」映射表一次建好**（同步）。
        /// <para>
        /// 与 <see cref="LoadDir(string, SpritePivotMode)"/> 的差别只在**多建一张派生表**（帧数组是同一份
        /// 缓存条目，⛔ 不重复加载）：映射表按**目录首次**现建、要逐帧解析 `frame_NNN` 的名字，开销随帧数
        /// 线性增长（658 帧的目录上一次约 6.6 ms）⇒ 建在"该目录第一个单位出场那一帧"就是一处可归因的卡顿。
        /// 进图前 / 读条阶段用本方法把两样一次建掉，战斗期 <see cref="LoadDir"/> 与
        /// <see cref="FrameNumberMap(string, SpritePivotMode)"/> 都是**纯读**。
        /// </para>
        /// <para>
        /// ⚠️ 同步阻塞主线程（与 <see cref="LoadDir(string, SpritePivotMode)"/> 同一条硬需求）
        /// —— ⛔ 不要在战斗热路径里第一次调用。
        /// </para>
        /// </summary>
        /// <returns>
        /// 帧数组与映射表；帧数组取不到时按 <see cref="LoadDir(string, SpritePivotMode)"/> 的口径沿用本目录
        /// **上一次取成功**的那份（并留一次 Warn）—— ⛔ 两者都不为 null。
        /// </returns>
        public static FrameBank.Preloaded Preload(string path, SpritePivotMode mode)
        {
            var key = CacheKey(mode, path);
            var pre = Bank.Preload(path, (FrameBank.PivotMode)(int)mode);
            if (pre.Frames != null && pre.Frames.Length > 0)
            {
                LastGood[key] = pre.Frames;
                return pre;
            }
            return new FrameBank.Preloaded(LoadDir(path, mode), pre.FrameMap);
        }

        /// <summary><see cref="LastGood"/> 的键（锚点模式 + 目录，两个模式各存一份）。</summary>
        private static string CacheKey(SpritePivotMode mode, string path)
        {
            return ((int)mode).ToString() + "|" + (path ?? string.Empty);
        }

        /// <summary>精灵目录已缓存的帧（`AsImported` 模式；调试 / 自检用）。没缓存过返回空数组（⛔ 不触发加载）。</summary>
        public static Sprite[] Cached(string path)
        {
            return Bank.Cached(path);
        }

        /// <summary>
        /// 「帧号（`frame_NNN` 的 `NNN` = `.sc` 里的 shape 序号）→ 帧数组下标」映射表：
        /// `map[n]` = 帧号 `n` 的下标，取不到 = `-1`；数组长度 = 该目录**最大帧号 + 1**。
        /// <para>
        /// 建表时引擎会断言并留痕「是否恒等」：单位目录（1 张 PNG = 1 个 Sprite）应当恒等
        /// （`map[n] == n`）；「帧号 ≠ 下标」发生在"一张 PNG 被切成多个子 Sprite"的目录里（塔 / 竞技场），
        /// 那类目录**必须走本表取帧**，⛔ 不许按下标取。
        /// </para>
        /// </summary>
        public static int[] FrameNumberMap(string path, SpritePivotMode mode)
        {
            return Bank.FrameNumberMap(path, (FrameBank.PivotMode)(int)mode);
        }

        /// <summary>
        /// 把一目录的帧统一到**同一个纹理坐标锚点**上重建（见 <see cref="SpritePivotMode.UnifiedCanvasAnchor"/>）。
        /// 前提：所有帧的画布尺寸一致（不满足 ⇒ 引擎保原样 + Warn，⛔ 不静默）。
        /// <para>
        /// `public` 而不是 `private`：判据探针（`tools/probes/FlowProbe.cs`）要在**编辑模式**下直接断言它
        /// —— `LoadDir` 走 `Game.Res`，编辑模式下为空。
        /// </para>
        /// </summary>
        public static Sprite[] UnifyCanvasAnchor(Sprite[] frames, string cacheKey)
        {
            return Bank.UnifyCanvasAnchor(frames, cacheKey);
        }

        /// <summary>
        /// 清缓存 + 销毁现场造出来的 Sprite（出图时调）；并让「档位帧下标序列」缓存一并失效
        /// （帧缓存没了，由它派生的下标序列也就失效了）。
        /// </summary>
        public static void ClearCache()
        {
            Bank.Clear();
            LastGood.Clear();
            UnitView.ClearClipCache();
        }

        /// <summary>
        /// 共享的 1×1 白色精灵（`pixelsPerUnit = 1` ⇒ 恰好 1 个世界单位 = 1 格）。
        /// 世界空间纯色底块 / 占位块用它，避免每个视图各造一张贴图（贴图多了会各自打断合批）。
        /// </summary>
        public static Sprite WhiteSprite()
        {
            return Bank.WhiteSprite();
        }

        /// <summary>
        /// 取文件名里的帧序号；解析不出时返回 `-1`（⛔ 不抛）。
        /// <para>
        /// 规则 = 名字里**倒数第二段起往回找的第一个纯数字段**；只有一段数字时
        /// （`frame_003` / `gen_frame_012`）才认最后一段。
        /// </para>
        /// <para>
        /// ⛔ 不取"尾部数字串"：Unity 对"一张 PNG 多个子 Sprite"的导入会把 Sprite 命名成
        /// <c>&lt;文件名&gt;_&lt;子序号&gt;</c>（本工程单位帧名 = <c>frame_000_0</c>），
        /// 尾部那段数字是**子序号**、对整目录恒同值 ⇒ 所有帧拿到同一个排序键 ⇒ 排序空转
        /// ⇒ 播放顺序变成批量加载的偶然顺序（**不报任何错**）。
        /// </para>
        /// <para>
        /// ⚠️ 同一 PNG 内的多个子 Sprite 会拿到相同的键 ⇒ 它们之间的先后由**稳定排序**保持为
        /// 导入器给的顺序。这对"1 PNG = 1 Sprite"的逐帧目录无影响；对"一张 PNG 多子 Sprite"的目录
        /// 则是**刻意保持现状**（这类目录由调用方按映射表取帧）。
        /// </para>
        /// </summary>
        public static int ParseFrameIndex(string name)
        {
            return FrameBank.ParseFrameIndex(name);
        }
    }
}
