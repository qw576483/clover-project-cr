using System.Collections.Generic;
using CloverEngine;
using CR.Def;
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
    /// 换算成**帧数组下标序列**（见 <see cref="ClipIndices"/>）；② 按档位帧率推进；
    /// ③ 该档 `Known == false`（素材无此动画 / `.sc` 引用越界 shapeID）⇒ **回落整目录循环**。
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
    /// ⇒ `Known=false` ⇒ 整目录播完停末帧（与原行为一致）。
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

        /// <summary>攻击档的**兜底**帧率算式（只在 <see cref="UnitAnimDurations"/> 查不到该档时长时用）：
        /// `attackFps = 攻击段帧数 ÷ (hit_speed_ms ÷ 1000)`。演算原文打印在 <see cref="AssertAttackTiming"/>。</summary>
        private const string AttackFpsFormula = "attackFps = attackCount / (hitSpeedMs / 1000)";

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
        /// <para>生成路径：`tools/probes/sc-placement.py`（解析 `.sc`）+ `.ai-tmp/test/cr-scale-table.py`（出表）。</para>
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

        private SpriteRenderer _renderer;

        /// <summary>
        /// 渲染器**自带**的默认材质（`AddComponent` 之后立刻记下）。没有混合的帧要写回它 ——
        /// ⛔ 不许往 `sharedMaterial` 写 null（那会让渲染器落到 Unity 的错误材质上，整块画成洋红）。
        /// </summary>
        private Material _rendererDefault;

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

        /// <summary>销毁已有的层节点（池复用 / 换目录 / 换队时调）。</summary>
        private void ClearLayers()
        {
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
                SpriteBlendMaterial.Set(sr, SpriteBlendMaterial.For(
                    _spritePath, spec.Frame, SpriteBlendMaterial.RememberDefault(sr)));
                _layers[i] = sr;
            }
            _layersTeam = team;
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

        /// <summary>当前档位的**帧下标序列**（null = 该档无可用帧段 ⇒ 整目录；见 <see cref="ClipIndices"/>）。</summary>
        private int[] _clip;
        /// <summary>在**当前档位内**的位置（0 起；`_clip == null` 时即整目录的下标）。</summary>
        private int _pos;

        /// <summary>上一次**真正写进** `SpriteRenderer.sprite` 的帧下标（`-1` = 还没贴过）。见 <see cref="Update"/>。</summary>
        private int _spriteIndex = -1;

        private float _frameTimer;
        private int _anim = AnimIdle;
        private bool _animDone;
        private int _lastHp = -1;
        private int _lastMaxHp = -1;
        private bool _barVisible = true;

        // ───────────────── 朝向 → 视角（9 视角 / 16 档朝向）─────────────────
        /// <summary>朝向档（0..15，见 <see cref="UnitAnimTable.StepToView"/>）。初值 4 = 侧身朝右（φ=0°）。</summary>
        private int _viewStep = 4;
        /// <summary>是否已由**实际移动方向**定过朝向（false ⇒ 首帧用契约 `facing` 的左右兜底）。</summary>
        private bool _viewStepSet;
        /// <summary>当前视角号（1..9）= `UnitAnimTable.StepToView[_viewStep]`；变号时必须重算 `_clip`。</summary>
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

            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sortingOrder = ArenaLayers.Instance.Actor;
                // 默认材质必须在**第一次写** `sharedMaterial` 之前记下来（见 `_rendererDefault`）。
                _rendererDefault = SpriteBlendMaterial.RememberDefault(_renderer);
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
                _frames = string.IsNullOrEmpty(_spritePath)
                    ? System.Array.Empty<Sprite>()
                    : SpriteBank.LoadDir(_spritePath, _pivotMode);
                if (_frames.Length == 0)
                    // 非预期分支必须留痕：素材索引缺失时静默画白块是最难查的现象之一。
                    Game.Logger?.Warn(LogTag, $"精灵取不到，回落占位色：dir={_spriteDir} isBuilding={isBuilding}");
                else
                    AssertFrameMapping(isBuilding);
            }

            // ★ 逐单位缩放（出处见 `UnitSpriteScale`）：必须在血条创建**之后** —— 它同时按 `1 / scale`
            //   抵消父缩放对血条世界尺寸 / 离地高度的影响（见 `ApplySpriteScale`）。
            ApplySpriteScale(_spriteDir);

            // ★ 建筑层配方（见 `BuildingLayerTable`）：有配方的目录**不用**单帧渲染器 —— 它一次只画一帧，
            //   而目录里混着部件帧与敌队配色帧（真正的层序由 `Apply` 按队叠出）。
            if (_renderer != null) _renderer.enabled = !HasBuildingRecipe();

            // 无任何可用帧段的目录（89 个里 44 个）保持"整目录循环"，只报一次（已登记差异，非静默降级）；
            // 部分档位不可用的目录在 ClipIndices 里按档位留痕。
            WarnIfUnmapped(isBuilding, _spriteDir);
            AssertHpBarOrder();

            // 归一档位/位置：档位帧序列由 ClipIndices 现算（帧号语义），位置一律从 0 起（= 该档首帧）。
            _anim = AnimIdle;
            _animDone = false;
            _frameTimer = 0f;
            // 换宿主必须把"已定朝向"复位 —— 新单位的第一帧还没有移动方向，
            // 否则会**继承上一个宿主的朝向**（池复用的静默串味）。
            _viewStep = 4;                     // 4 = 侧身朝右（φ=0°），此时不翻
            _viewStepSet = false;
            _hasMoveDir = false;
            _viewNo = UnitAnimTable.StepToView[_viewStep];
            _clip = ClipIndices(_spriteDir, _anim, _viewNo);
            _pos = 0;
            // 复用时必须重新贴帧：`Release()` 会松开 sprite 引用（避免池长期持有），
            // 若这里不补回，第二次取用同一个 spriteDir 就会得到"看不见的单位"（无报错的静默失败）。
            RefreshSprite();
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
            if (UpdateFacing(moveDir, e.facing))
            {
                // 转身只是**换角度**、不是换动作 ⇒ 保持 `_pos`（只夹到新序列长度内），
                // ⛔ 不许把走路/攻击从头播（那会让转身看起来像"卡了一下"）。
                _clip = ClipIndices(_spriteDir, _anim, _viewNo);
                if (_clip != null && _clip.Length > 0) _pos = Mathf.Clamp(_pos, 0, _clip.Length - 1);
                RefreshSprite();
            }
            if (_renderer != null) _renderer.flipX = UnitAnimTable.StepFlip(_viewStep);

            // 档位切换：换档要**重置帧**，否则会从上一档的残留帧继续播（现象是"走路的第 30 帧直接接攻击"）。
            // 但**不能每次都照单接受**：服务端的 `anim` 是"瞬时"的 —— 一次挥砍只在挥砍那一 tick 置
            // `AnimAttack`（`server/game/core/combat.go:68-73`），下一 tick 就回 `AnimWalk`
            //（`battle.go:525/533/536-539`）。若每 tick 都切，就会出现"攻击 1 帧 → 走路从第 0 帧重来"，
            // 单位看起来在原地**抽搐**（苍蝇海攻击间隔 1s、攻击段 7 帧时最明显）。
            // 滞回规则（同档不重播 + 攻击播完才让位）：
            //   ① 请求档 == 当前档 ⇒ 什么都不做（⛔ 绝不重新从 0 帧起播）；
            //   ② 当前档是 **attack 且还没播完** ⇒ 扣住不放（除 `AnimDie` —— 死亡无条件立即生效）；
            //   ③ 其余情况正常换档（换档当帧立刻贴新档首帧）。
            var anim = Mathf.Clamp(e.anim, AnimIdle, AnimDie);
            var holdAttack = _anim == AnimAttack && !_animDone && anim != AnimDie;
            if (anim != _anim && !holdAttack)
            {
                _anim = anim;
                // 换档必须**重算档位帧序列**并把位置归 0：否则会从上一档的残留位置继续播
                //（现象 = "走路第 30 帧直接接攻击"），或按上一档的帧号序列取帧（档位帧号区间互不相同）。
                _clip = ClipIndices(_spriteDir, _anim, _viewNo);
                _pos = 0;
                _frameTimer = 0f;
                _animDone = false;
                RefreshSprite();   // 换档当帧立刻贴新档首帧，不等计时器攒满一帧
            }

            // 半透明（部署期）：原版落点后到 `deploy_time` 结束前是"未激活"表现。
            // 这里用 alpha 表达（不改缩放/位置），并在日志上不刷屏。
            if (_renderer != null)
            {
                var c = _renderer.color;
                c.a = alpha;
                _renderer.color = c;
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

            // ★ 建筑：按层配方叠帧（出处见 `BuildingLayerTable`）。有配方 ⇒ 关掉单帧渲染器，
            //   否则它会把目录里的部件帧 / 敌队配色帧也逐帧播出来。
            if (_isBuilding && BuildLayers(e.team))
            {
                if (_renderer != null) _renderer.enabled = false;
                ApplyLayers(worldPos, alpha);
            }

            UpdateHpBar(e.hp, e.max_hp);
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
            // 按层配方渲染的建筑不逐帧换图（见 `BuildLayers`）：换图会把部件帧也播出来。
            if (_layers != null) return;
            if (_frames == null || _frames.Length <= 1) return;
            if (_animDone) return;

            var count = ClipCount();
            if (count <= 1) return;

            var fps = FpsFor(_spriteDir, _anim, _viewNo);
            if (fps <= 0f) return;   // 0 = 该档"静止帧（不播）"（收录目录的 idle，见 FpsFor 出处）
            _frameTimer += Time.deltaTime * fps;
            while (_frameTimer >= 1f)
            {
                _frameTimer -= 1f;
                _pos++;
                if (_pos >= count) AdvanceEnd();
            }

            // ⛔ **只在帧真的变了时**写 `_renderer.sprite`（不是每帧无条件写）。
            //   两个理由：① 每帧赋同一个 Sprite 也会让 SpriteRenderer 每帧重建渲染数据，
            //   486 帧的目录上纯属浪费；② 无条件写会**掩盖"帧其实没动"**，自检时看不出来。
            //   判据用"贴上去的实际下标"（`_spriteIndex`）而不是"本帧是否推进过" ——
            //   换档时 `Apply()` 会把 `_pos` 归 0，那一下也必须立刻贴回首帧，
            //   否则会继续显示上一档的残留帧直到计时器攒满一帧（现象 = "走路第 30 帧直接接攻击"）。
            var idx = SpriteIndexAt(_pos);
            if (_renderer != null && _spriteIndex != idx && idx >= 0 && idx < _frames.Length)
            {
                _spriteIndex = idx;
                _renderer.sprite = _frames[idx];
                ApplyBlendMaterial(idx);
            }
        }

        /// <summary>
        /// 按该帧的原版 <c>blend_mode</c> 选材质（出处 = <see cref="SpriteBlendTable"/>，生成物、⛔ 不许手改）。
        /// 表里没有的帧 ⇒ 写回渲染器自带的默认材质（即未登记就是 Normal）；⛔ 绝不写 <c>null</c>（洋红）。
        /// </summary>
        private void ApplyBlendMaterial(int frameIndex)
        {
            if (_renderer == null || _frames == null || frameIndex < 0 || frameIndex >= _frames.Length) return;
            var mat = SpriteBlendMaterial.For(_spritePath,
                SpriteBank.ParseFrameIndex(_frames[frameIndex].name), _rendererDefault);
            SpriteBlendMaterial.Set(_renderer, mat);
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
        /// 由**窗口位移**更新朝向档；返回 `_viewNo` 是否变化（变化时调用方必须重算 `_clip`）。
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
        private int[] ClipIndices(string dir, int anim, int view)
        {
            if (_frames == null || _frames.Length == 0 || _spritePath.Length == 0) return null;
            if (anim < 0 || anim > AnimDie) return null;

            var key = (int)_pivotMode + "|" + _spritePath + "|" + anim + "|" + view;
            int[] cached;
            if (ClipIndexCache.TryGetValue(key, out cached)) return cached.Length == 0 ? null : cached;

            int[] result = null;
            UnitAnimTable.Entry entry;
            if (UnitAnimTable.TryGet(dir, out entry) && entry.Tiers != null
                && anim < entry.Tiers.Length && entry.Tiers[anim].Known)
            {
                var clip = entry.Tiers[anim];
                // 优先取**当前朝向对应视角**的帧段（`ViewRuns[view-1]`，出处 = `UnitAnimTable` 的逐视角帧段表）；
                // 该视角无素材 ⇒ 回落 `clip.Runs`（默认视角）。
                var runs = UnitAnimTable.RunsForView(clip, view) ?? clip.Runs;
                var want = UnitAnimTable.CountOfRuns(runs);
                var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);
                var list = new List<int>(want);
                if (runs != null)
                {
                    for (var r = 0; r + 1 < runs.Length; r += 2)
                        for (var i = 0; i < runs[r + 1]; i++)
                        {
                            var idx = ResolveFrame(map, runs[r] + i);
                            if (idx >= 0 && idx < _frames.Length) list.Add(idx);
                        }
                }
                if (list.Count >= 1) result = list.ToArray();
                else
                    // 非预期分支必须留痕：段表有帧、但帧号→下标**一帧都解析不出**（非恒等目录的缺项）⇒
                    // 静默变"木头人"是最难查的。去重走引擎的**进程级**闸门（键 = 目录）。
                    LogThrottle.WarnOnce(LogTag, "tier:" + dir,
                        $"帧段解析出的可用帧 0 帧（该目录帧号→下标全部缺项）⇒ 该档回落整目录：dir={dir} anim={TierName(anim)} view={view} 段表帧数={want} frames.Length={_frames.Length}");
            }
            else if (!HasBuildingRecipe())
                // 目录在表里但该档不可用（素材无此动画 / `.sc` 引用越界 shapeID）⇒ 整目录（已登记差异，每目录一次）。
                // 有建筑层配方的目录**不发这句**：它的单帧渲染器在 `Apply` 里已关（`enabled = !HasBuildingRecipe()`），
                // 真正的层序由 `BuildingLayerTable` 叠出（逐层读数见 `BuildLayers`）⇒ 「整目录循环播」与事实相反。
                LogThrottle.WarnOnce(LogTag, "tier:" + dir,
                    $"该档位不可用（素材无此动画 / `.sc` 越界 shapeID）⇒ 整目录循环播（已登记差异）：dir={dir} anim={TierName(anim)}");

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

        /// <summary>当前档位的帧数（`_clip == null` ⇒ 整目录帧数）。</summary>
        private int ClipCount()
        {
            if (_clip != null && _clip.Length > 0) return _clip.Length;
            return _frames == null ? 0 : _frames.Length;
        }

        /// <summary>档位内位置 → **帧数组下标**（`_clip == null` ⇒ 位置即下标）。</summary>
        private int SpriteIndexAt(int pos)
        {
            if (_frames == null || _frames.Length == 0) return -1;
            if (_clip != null && _clip.Length > 0)
                return _clip[Mathf.Clamp(pos, 0, _clip.Length - 1)];
            return Mathf.Clamp(pos, 0, _frames.Length - 1);
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

        private void AdvanceEnd()
        {
            if (_anim == AnimDie || _anim == AnimAttack)
            {
                // 死亡：停在末帧（⛔ 不循环 —— 循环会让尸体反复站起来）。
                // 攻击：**播完一遍就停在末帧**（配合 `Apply` 的滞回：攻击段没播完时不让位给 walk/idle，
                //   否则"挥砍只播 1 帧就被走路打断"= 抽搐）。下一档请求到来时 `Apply` 会重置 `_animDone`。
                _animDone = true;
                _pos = Mathf.Max(0, ClipCount() - 1);
            }
            else
            {
                _pos = 0;
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
        /// <para>兜底（分组表里查不到该 export / 目录未收录 / 帧数为 0）：idle = 不播（`0f`）、
        /// attack = 帧数 ÷ `hit_speed_ms`、walk/die = `.sc` 自带 FPS；再兜底 = <see cref="AnimFps"/>。
        /// 单帧档无论时长多大都不会动（`Update` 里 `count &lt;= 1` 直接 return）。</para>
        /// </summary>
        private static float FpsFor(string dir, int anim, int view)
        {
            UnitAnimTable.Entry entry;
            if (UnitAnimTable.TryGet(dir ?? string.Empty, out entry) && entry.Tiers != null
                && anim >= 0 && anim < entry.Tiers.Length && entry.Tiers[anim].Known)
            {
                var clip = entry.Tiers[anim];
                var runs = UnitAnimTable.RunsForView(clip, view) ?? clip.Runs;
                var frames = UnitAnimTable.CountOfRuns(runs);
                int[] durations;
                if (frames > 0 && UnitAnimDurations.TryGet(dir ?? string.Empty, out durations)
                    && anim < durations.Length && durations[anim] > 0)
                    return frames * 1000f / durations[anim];
                if (anim == AnimIdle) return 0f;                                  // 静止帧：不播
                if (anim == AnimAttack && entry.HitSpeedMs > 0)
                    return frames / (entry.HitSpeedMs / 1000f);                    // = 攻击段帧数 ÷ 秒
                if (entry.ScFps > 0) return entry.ScFps;                          // walk / die：.sc 自带帧率
            }
            return AnimFps[Mathf.Clamp(anim, 0, AnimFps.Length - 1)];
        }

        /// <summary>清「档位帧下标序列」缓存（由 <see cref="SpriteBank.ClearCache"/> 同步调 —— 帧缓存没了，下标也就失效了）。</summary>
        internal static void ClearClipCache()
        {
            ClipIndexCache.Clear();
        }

        /// <summary>把当前 <see cref="_pos"/> 对应的帧立刻写进 `SpriteRenderer`（换档 / 绑定归位时必须调）。</summary>
        private void RefreshSprite()
        {
            if (_renderer == null) return;
            var idx = SpriteIndexAt(_pos);
            if (_frames != null && _frames.Length > 0 && idx >= 0)
            {
                _spriteIndex = idx;
                _renderer.sprite = _frames[idx];
                ApplyBlendMaterial(idx);
            }
            else
            {
                _spriteIndex = -1;
                _renderer.sprite = FallbackSprite();
                // 没有帧时不该留着上一档的混合材质；写回渲染器自带的默认材质（⛔ 不许写 null = 洋红）。
                SpriteBlendMaterial.Set(_renderer, _rendererDefault);
            }
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
        /// 打印**攻击段时长与 `hit_speed_ms` 的对照**（每个已收录目录一次）—— 读数原文：
        /// `攻击段时长 = attackCount / attackFps`（= <see cref="UnitAnimDurations"/> 的原版时长）
        /// vs `hit_speed_ms`（= 两次挥砍的间隔）。两者**不必相等**：原版里一次挥砍的动画时长
        /// 可以短于攻击间隔（播完停在末帧等下一次）。
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
            var fps = segSec > 0f ? count / segSec : count / hitSec;
            Game.Logger?.Info(LogTag,
                $"攻击段时长对照：dir={dir} 帧数={count} 原版时长={segSec:F3}s（= 分组表的 timeline÷FPS）" +
                $" ⇒ fps={fps:F2}" + (hasDur ? "" : $"（无时长数据 ⇒ 兜底 {AttackFpsFormula}）") +
                $" | hit_speed_ms={hitSec:F3}s" +
                (hasDur ? $"（差 {(segSec - hitSec) / hitSec * 100f:+0.0;-0.0}%）" : ""));
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

        /// <summary>取某目录下**全部**帧（同步），按帧号升序排；锚点按导入设置原样。</summary>
        /// <returns>帧数组（长度 0 = 该目录没有可用资源；⛔ 不返回 null）。</returns>
        public static Sprite[] LoadDir(string path)
        {
            return Bank.LoadDir(path);
        }

        /// <summary>取某目录下**全部**帧（同步），按帧号升序排，并按 <paramref name="mode"/> 决定锚点。</summary>
        /// <param name="path">`Assets/Resources/` 下的相对目录（`ResPaths` 组装）。</param>
        /// <param name="mode">锚点模式；逐帧单位 / 特效用 <see cref="SpritePivotMode.UnifiedCanvasAnchor"/>。</param>
        /// <returns>帧数组（长度 0 = 该目录没有可用资源；⛔ 不返回 null）。</returns>
        public static Sprite[] LoadDir(string path, SpritePivotMode mode)
        {
            return Bank.LoadDir(path, (FrameBank.PivotMode)(int)mode);
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
