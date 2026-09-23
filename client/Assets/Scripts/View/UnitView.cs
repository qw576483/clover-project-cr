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
    /// （独立数据文件；出处 = `策划/单位帧段表.md` §3「可直接填 `UnitView.AnimRanges` 的四元组」与
    /// `.ai-tmp/test/AS1-*`，见那边的文件头注释）。本类只做三件事：
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
        /// <para>⛔ 收录目录**不**用这里：idle=0（静止帧不播）、walk/die = 该目录 `.sc` 自带 FPS、
        /// attack = 攻击段帧数 ÷ (`hit_speed_ms`÷1000)（见 <see cref="FpsFor"/>）。</para>
        /// </summary>
        private static readonly float[] AnimFps = { 8f, 12f, 14f, 10f };

        /// <summary>
        /// 「档位帧下标序列」缓存（键 = `mode|path|anim`）。帧段表（<see cref="UnitAnimTable"/>）给的是**帧号**，
        /// 播放要用的是**帧数组下标** ⇒ 每个目录每档只换算一次（换算规则见 <see cref="ResolveFrame"/>）。
        /// 值语义：`null` = 该档没有可用帧段（回落整目录）；非 null 且长度 &gt; 0 = 按它逐帧播。
        /// </summary>
        private static readonly Dictionary<string, int[]> ClipIndexCache = new Dictionary<string, int[]>();

        /// <summary>`hit_speed_ms` 与攻击段帧率的关系（⛔ 不再在本类里写死数值，一律查 <see cref="UnitAnimTable"/>）：
        /// `AnimFps[attack] = 攻击段帧数 ÷ (hit_speed_ms ÷ 1000)` ⇒ 攻击段**播完一遍的时长 == `hit_speed_ms`**。
        /// 演算原文打印在 <see cref="AssertAttackTiming"/>（每目录一次）。</summary>
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

        private SpriteRenderer _renderer;
        private WorldHpBar _hpBar;
        private string _spriteDir = string.Empty;
        private Sprite[] _frames;
        /// <summary>加载时用的完整资源路径（`ResPaths.UnitDir/BuildingDir`）—— 帧号→下标映射的缓存键要用。</summary>
        private string _spritePath = string.Empty;
        /// <summary>加载时用的锚点模式（与 <see cref="_spritePath"/> 一起构成 <see cref="SpriteBank.FrameNumberMap"/> 的键）。</summary>
        private SpriteBank.SpritePivotMode _pivotMode = SpriteBank.SpritePivotMode.UnifiedCanvasAnchor;

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

        /// <summary>当前绑定的精灵目录（`ResPaths` 里那一列，如 `chr_knight_out`）。空串 = 用占位色。</summary>
        public string SpriteDir => _spriteDir;

        /// <summary>当前帧数（0 = 没取到帧，正在用占位色）。</summary>
        public int FrameCount => _frames == null ? 0 : _frames.Length;

        /// <summary>当前 `anim` 档位。</summary>
        public int Anim => _anim;

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

            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sortingOrder = SortingOrder.Unit;
            }
            // 血条：幂等创建（`WorldHpBar.Create` 内部已有则复用并更新参数）。
            if (_hpBar == null)
            {
                // 尺寸口径：单位约 1 格宽，血条取 1.0×0.12（引擎默认值），离脚底 1.6 格
                //（比默认 2.15 低 —— 我们的相机是"一格约一两百像素"的竖屏构图，2.15 会让血条飘太高）。
                _hpBar = WorldHpBar.Create(transform, 1.0f, 0.12f, 1.6f, LogTag + ".Hp");
                _lastHp = -1;
                _lastMaxHp = -1;
                _barVisible = true;
            }

            if (_spriteDir != (spriteDir ?? string.Empty) || _frames == null)
            {
                _spriteDir = spriteDir ?? string.Empty;
                // ★ 逐帧单位必须用 **统一画布锚点**：导入态下每帧 pivot 是各自裁剪框中心 ⇒ 换帧就位移
                //   （根因与算式见 `SpriteBank.SpritePivotMode.UnifiedCanvasAnchor` 的注释）。
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

            // 无任何可用帧段的目录（89 个里 44 个）保持"整目录循环"，只报一次（已登记差异，非静默降级）；
            // 部分档位不可用的目录在 ClipIndices 里按档位留痕。
            WarnIfUnmapped(isBuilding, _spriteDir);

            // 归一档位/位置：档位帧序列由 ClipIndices 现算（帧号语义），位置一律从 0 起（= 该档首帧）。
            _anim = AnimIdle;
            _animDone = false;
            _frameTimer = 0f;
            _clip = ClipIndices(_spriteDir, _anim);
            _pos = 0;
            // 复用时必须重新贴帧：`Release()` 会松开 sprite 引用（避免池长期持有），
            // 若这里不补回，第二次取用同一个 spriteDir 就会得到"看不见的单位"（无报错的静默失败）。
            RefreshSprite();
        }

        // ───────────────────────────── 每帧驱动 ─────────────────────────────

        /// <summary>
        /// 用一条（已插值的）实体状态刷新表现。
        /// </summary>
        /// <param name="e">服务端实体快照（`EntitySnapshot`）。</param>
        /// <param name="worldPos">**已插值**的世界坐标（格，竞技场中心为原点）—— 由 `BattleViewRoot` 算好。</param>
        /// <param name="alpha">整条实体的透明度（部署期为半透明，见 `BattleViewRoot` 的说明）。</param>
        public void Apply(EntitySnapshot e, Vector2 worldPos, float alpha)
        {
            if (e == null) return;

            transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            // 朝向：契约 `facing` ∈ {-1, 1}；只有"向左"才翻。
            if (_renderer != null) _renderer.flipX = e.facing < 0;

            // 档位切换：换档要**重置帧**，否则会从上一档的残留帧继续播（现象是"走路的第 30 帧直接接攻击"）。
            var anim = Mathf.Clamp(e.anim, AnimIdle, AnimDie);
            if (anim != _anim)
            {
                _anim = anim;
                // 换档必须**重算档位帧序列**并把位置归 0：否则会从上一档的残留位置继续播
                //（现象 = "走路第 30 帧直接接攻击"），或按上一档的帧号序列取帧（档位帧号区间互不相同）。
                _clip = ClipIndices(_spriteDir, _anim);
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
            // 用世界 y 换算成排序值（每 0.5 格一级），避免同一行单位互相穿插。
            if (_renderer != null)
                _renderer.sortingOrder = SortingOrder.Unit + Mathf.RoundToInt((GameConst.ArenaTilesH * 0.5f - worldPos.y) * 2f);

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

            // 满血不显示血条（原版表现）；空血/死亡也不显示。
            var visible = maxHp > 0 && hp > 0 && hp < maxHp;
            if (visible != _barVisible)
            {
                _barVisible = visible;
                _hpBar.SetVisible(visible);
            }
        }

        private void Update()
        {
            if (_frames == null || _frames.Length <= 1) return;
            if (_animDone) return;

            var count = ClipCount();
            if (count <= 1) return;

            var fps = FpsFor(_spriteDir, _anim);
            if (fps <= 0f) return;   // 0 = 该档"静止帧（不播）"（收录目录的 idle，见 FpsFor 出处）
            _frameTimer += Time.deltaTime * fps;
            while (_frameTimer >= 1f)
            {
                _frameTimer -= 1f;
                _pos++;
                if (_pos >= count) AdvanceEnd();
            }

            // ⛔ **只在帧真的变了时**写 `_renderer.sprite`（修复前是每帧无条件写）。
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
            }
        }

        /// <summary>
        /// 当前档位的**帧下标序列**（把帧段表的**帧号**经 <see cref="SpriteBank.FrameNumberMap"/> 换算成下标）。
        /// 返回 `null` = 该档没有可用帧段（目录未收录 / 该档 `Known==false` / 帧号一帧都解析不出）
        /// ⇒ 调用方回落「整目录」。
        /// </summary>
        private int[] ClipIndices(string dir, int anim)
        {
            if (_frames == null || _frames.Length == 0 || _spritePath.Length == 0) return null;
            if (anim < 0 || anim > AnimDie) return null;

            var key = (int)_pivotMode + "|" + _spritePath + "|" + anim;
            int[] cached;
            if (ClipIndexCache.TryGetValue(key, out cached)) return cached.Length == 0 ? null : cached;

            int[] result = null;
            UnitAnimTable.Entry entry;
            if (UnitAnimTable.TryGet(dir, out entry) && entry.Tiers != null
                && anim < entry.Tiers.Length && entry.Tiers[anim].Known)
            {
                var clip = entry.Tiers[anim];
                var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);
                var list = new List<int>(clip.Count);
                var runs = clip.Runs;
                if (runs != null)
                {
                    for (var r = 0; r + 1 < runs.Length; r += 2)
                        for (var i = 0; i < runs[r + 1]; i++)
                        {
                            var idx = ResolveFrame(map, runs[r] + i);
                            if (idx >= 0 && idx < _frames.Length) list.Add(idx);
                        }
                }
                if (list.Count > 1) result = list.ToArray();
                else if (UnknownTierWarned.Add(dir))
                    // 非预期分支必须留痕：段表有帧、但帧号→下标解析不出（非恒等目录的缺项）⇒ 静默变"木头人"是最难查的。
                    Game.Logger?.Warn(LogTag,
                        $"帧段解析出的可用帧 {list.Count} 帧（该目录帧号→下标有缺项）⇒ 该档回落整目录：dir={dir} anim={TierName(anim)} 段表帧数={clip.Count} frames.Length={_frames.Length}");
            }
            else if (UnknownTierWarned.Add(dir))
                // 目录在表里但该档不可用（素材无此动画 / `.sc` 引用越界 shapeID）⇒ 整目录（已登记差异，每目录一次）。
                Game.Logger?.Warn(LogTag,
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
            if (_anim == AnimDie)
            {
                // 死亡：停在末帧（⛔ 不循环 —— 循环会让尸体反复站起来）
                _animDone = true;
                _pos = Mathf.Max(0, ClipCount() - 1);
            }
            else
            {
                _pos = 0;
            }
        }

        /// <summary>
        /// 当前目录+档位的播放帧率（帧/秒）。出处 = `策划/单位帧段表.md` §3 + `server/game/table/tsv/unit.tsv`：
        /// <list type="bullet">
        /// <item>**idle**：`0f` —— 表里 idle 是「静止姿态帧」（9 视角各 1 帧）⇒ **不播**，停在首帧。</item>
        /// <item>**walk**（原版 `run1`）：该目录 `.sc` 自带 FPS（`UnitAnimTable.Entry.ScFps`）。</item>
        /// <item>**attack**：`攻击段帧数 ÷ (hit_speed_ms ÷ 1000)` ⇒ **攻击段播完一遍 == `hit_speed_ms`**。</item>
        /// <item>**die**：同 walk（素材无死亡动画 ⇒ 该档 `Known==false` ⇒ 实际走不到这里）。</item>
        /// </list>
        /// 未收录目录 / 未覆盖档位 ⇒ 回落 <see cref="AnimFps"/> 默认值。
        /// </summary>
        private static float FpsFor(string dir, int anim)
        {
            UnitAnimTable.Entry entry;
            if (UnitAnimTable.TryGet(dir ?? string.Empty, out entry) && entry.Tiers != null
                && anim >= 0 && anim < entry.Tiers.Length && entry.Tiers[anim].Known)
            {
                var clip = entry.Tiers[anim];
                if (anim == AnimIdle) return 0f;                                  // 静止帧：不播
                if (anim == AnimAttack && entry.HitSpeedMs > 0)
                    return clip.Count / (entry.HitSpeedMs / 1000f);               // = 攻击段帧数 ÷ 秒
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
            }
            else
            {
                _spriteIndex = -1;
                _renderer.sprite = FallbackSprite();
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

        /// <summary>「档位不可用 ⇒ 整目录」与「帧段解析不出」的去重集合（每目录一次）。</summary>
        private static readonly HashSet<string> UnknownTierWarned = new HashSet<string>();

        /// <summary>目录**一个可用档位都没有**（未收录 / 4 档全 Unknown）的**单位**目录只报一次
        /// （整目录循环 = 已登记差异，非静默降级）。</summary>
        private static readonly HashSet<string> UnmappedWarned = new HashSet<string>();

        private static void WarnIfUnmapped(bool isBuilding, string dir)
        {
            if (isBuilding || string.IsNullOrEmpty(dir)) return;
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
        /// 打印**攻击段与 `hit_speed_ms` 的自洽断言**（每个已收录目录一次）—— 算式原文：
        /// `attackFps = attackCount / (hitSpeedMs / 1000)` ⇒ `attackCount / attackFps == hitSpeedMs`。
        /// 出处：`策划/单位帧段表.md` §3（attack 帧段帧数）+ `server/game/table/tsv/unit.tsv`（`hit_speed_ms`）。
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
            var sec = hitMs / 1000f;
            var fps = count / sec;
            var segSec = count / fps;
            Game.Logger?.Info(LogTag,
                $"攻击段自洽断言：dir={dir} {AttackFpsFormula} = {count} / ({hitMs}/1000) = {fps:F2}fps | " +
                $"攻击段时长 = {count}帧 / {fps:F2}fps = {segSec:F3}s vs hit_speed_ms={sec:F3}s（差 {(segSec - sec) / sec * 100f:+0.0;-0.0}%）");
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
    /// 渲染层级（sortingOrder）常量 —— 唯一定义处，避免各 View 各写一个数字导致"底图盖住单位"。
    /// <para>
    /// 约定（后画 = 数值大）：底图 0 / 装饰 10 / **塔** 50 / **落点指示** 200 / **单位** 1000+（单位按世界 y 再加 0..64 的深度）
    /// / 塔的血条由 <see cref="WorldHpBar"/> 的 3D 位置决定，不在这里。
    /// 单位给到 1000 是因为它要按世界 y 做深度排序（每 0.5 格一级，最多 32 格 ⇒ 至多 +64），
    /// 与塔的 50 之间留足空隙，避免"站在塔前面的兵被塔盖住"。
    /// </para>
    /// </summary>
    public static class SortingOrder
    {
        /// <summary>竞技场底图（地面 + 河道 + 通路）。</summary>
        public const int ArenaBase = 0;

        /// <summary>竞技场装饰层（浮动小岛/草丛/木栅栏；本片只标定层级，未启用，见 `ArenaView` 的登记）。</summary>
        public const int ArenaDeco = 10;

        /// <summary>6 座塔。</summary>
        public const int Tower = 50;

        /// <summary>拖放落点指示（在地板之上、单位之下）。</summary>
        public const int PlacementIndicator = 200;

        /// <summary>单位基准层级（+ 世界 y 深度，见 <see cref="UnitView.Apply"/>）。</summary>
        public const int Unit = 1000;
    }

    /// <summary>
    /// 精灵批量加载 + 缓存（按目录）。
    /// <para>
    /// <b>为什么不用 `Game.Res.LoadAsset&lt;Sprite&gt;(path, cb)` 逐帧取</b>：那是一次异步回调一个帧，
    /// 486 帧的目录要 486 次回调；而引擎的同步入口 <c>LoadAll&lt;T&gt;</c> 正是为"条带/整目录"场景提供的
    /// （`Contracts.cs:1100-1117`：逐个帧名 `LoadAsset` 会取不到，只有整条取才拿得到，⇒ 本项目必须用 LoadAll）。
    /// </para>
    /// <para>
    /// <b>为什么还要尝试 `LoadAll&lt;Texture2D&gt;`</b>：`LoadAll` 走的是 Unity 的
    /// `Resources` 的 `LoadAll&lt;T&gt;`，**T 必须与导入类型匹配** —— 若这批 PNG 被导成 `Texture` 而不是 `Sprite`，
    /// `LoadAll&lt;Sprite&gt;` 会返回空数组（引擎契约：空数组 = 没取到，且 Warn），
    /// 于是整个表现层会"什么都不显示但也不报错"。这里兜一层：拿不到 Sprite 就取 Texture 现场 `Sprite.Create`，
    /// 让资源导入类型不至于把画面变成空白。
    /// </para>
    /// </summary>
    public static class SpriteBank
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "SpriteBank";

        /// <summary>
        /// 由 `Texture2D` 现场造 `Sprite` 时使用的 PPU。
        /// = Unity 默认值（100 px/单位），与本项目其它 Sprite 资源的默认口径一致 ⇒ 不会一大一小。
        /// </summary>
        public const float FallbackPixelsPerUnit = 100f;

        private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>
        /// 精灵的**锚点模式**（<see cref="LoadDir(string, SpritePivotMode)"/> 的第二参）。
        /// </summary>
        public enum SpritePivotMode
        {
            /// <summary>
            /// 按导入设置原样用。多 Sprite 导入时，**每帧的 pivot 是自己那张裁剪框的中心**
            /// （`.meta` 的 `alignment: 0` + `spriteMode: 2`）⇒ 逐帧裁剪框不同 ⇒ 换帧时整帧被重新居中。
            /// 竞技场底图与塔是"每档只用一帧"，没有换帧位移问题，走这个（保持既有画面不变）。
            /// </summary>
            AsImported = 0,

            /// <summary>
            /// 全目录共用一个**纹理坐标锚点**（= 该目录所有帧 `rect` 并集的中心）重建每一帧。
            /// <para>
            /// <b>为什么逐帧单位精灵必须走这个</b>：素材是"一个固定画布 + 每帧一张紧裁剪 PNG"，
            /// 原版靠逐帧锚点表（`anchors.json`）把每帧的落脚点对齐到同一个画布位置；本项目没有该文件，
            /// 而多 Sprite 导入给出的 pivot 是**每帧各自裁剪框的中心**（实测 `chr_knight_out` 486 帧里
            /// 有 **245 个互不相同的锚点**、跨度 0.71×0.61 格）⇒ 每换一帧人物就在画布上"重新居中"一次，
            /// 这就是"贴图抖动"。共用一个纹理锚点后，每帧都按**美术自己的画布位置**落位，换帧不再位移。
            /// </para>
            /// <para>
            /// 锚点取"并集中心"而不是"并集底边中点"的理由：① 本工程既有约定是**内容中心对齐逻辑位置**
            /// （见 `ArenaView.TowerScale` 注释：塔的内容 3.0×3.9 格正好对上 3×3 占地，用的就是导入的中心 pivot），
            /// 取中心 ⇒ **不引入任何新的整体位移**（回归面最小）；取底边中点会把整目录内容**整体上移 0.905 格**
            /// （= 画布高 181 px 的一半，实测）。② 只要锚点是"全目录共用的同一个纹理点"，
            /// 抖动就已经消除，中心/底边对抖动**等效**。③ 实测 `chr_knight_out` 的并集正好是整幅画布
            /// (0,0,187,181)，其中心 = (93.5, 90.5) 恰好等于 `.meta` 给的全图 pivot (0.5,0.5) ⇒ 最保守。
            /// </para>
            /// <para>
            /// ⚠️ **已知差异（登记）**：没有逐帧锚点表 ⇒ 只能"整目录共用一个锚点"，
            /// 因此**不同动画族之间**的落脚线差异（实测该目录各帧 `rect.y` ∈ 0..55 px）仍按美术原样保留，
            /// 无法像原版那样逐帧对齐。消除条件 = 拿到 `anchors.json` 级别的逐帧锚点表。
            /// </para>
            /// </summary>
            UnifiedCanvasAnchor = 1,
        }

        /// <summary>
        /// 取某目录下**全部**帧（同步），按帧序号（文件名里的数字）排序；锚点按导入设置原样。
        /// 等价于 <see cref="LoadDir(string, SpritePivotMode)"/> 传 <see cref="SpritePivotMode.AsImported"/>。
        /// </summary>
        /// <returns>帧数组（长度 0 = 该目录没有可用资源；⛔ 不返回 null）。</returns>
        public static Sprite[] LoadDir(string path)
        {
            return LoadDir(path, SpritePivotMode.AsImported);
        }

        /// <summary>
        /// 取某目录下**全部**帧（同步），按帧序号排序，并按 <paramref name="mode"/> 决定锚点。
        /// <para>
        /// ⚠️ **必须自己排序**：`Resources` 批量加载的返回顺序**没有保证**，
        /// 直接拿来播会出现"帧序乱跳"；帧名规则是 `frame_NNN`（`ResPaths` 内部 `FrameName`），
        /// 所以按数字升序排即可。实测导入出来的 Sprite 名是 `frame_000_0`（`.meta` 的
        /// `name: frame_000_0`，`_` 后缀是**子 Sprite 序号**：一张 PNG 可能被切成多个子 Sprite，
        /// 竞技场 23 张 PNG → 25 个 Sprite、塔 214 张 → 236 个）⇒ 帧序号取**倒数第二段**数字，
        /// 见 <see cref="ParseFrameIndex(string)"/>。
        /// </para>
        /// </summary>
        /// <param name="path">`Assets/Resources/` 下的相对目录（`ResPaths` 组装）。</param>
        /// <param name="mode">锚点模式；逐帧单位用 <see cref="SpritePivotMode.UnifiedCanvasAnchor"/>。</param>
        /// <returns>帧数组（长度 0 = 该目录没有可用资源；⛔ 不返回 null）。</returns>
        public static Sprite[] LoadDir(string path, SpritePivotMode mode)
        {
            if (string.IsNullOrEmpty(path)) return System.Array.Empty<Sprite>();
            // ⚠️ 缓存键必须带上 mode：两参重载并存时，只用 path 做键会让第二次不同 mode 的调用
            //    拿到第一次的结果（静默用错锚点 —— 比崩溃难查得多）。
            var key = (int)mode + "|" + path;
            Sprite[] cached;
            // 缓存有效性检查：`Game.Res.UnloadAll()` 之后拿到的会是 Unity 的"假 null"；
            // 直接返回缓存会让所有单位变成看不见（最难查的一类静默失败）⇒ 失效即重新取。
            if (Cache.TryGetValue(key, out cached) && cached != null
                && (cached.Length == 0 || cached[0] != null))
                return cached;

            var frames = Game.Res != null ? Game.Res.LoadAll<Sprite>(path) : null;
            if (frames == null || frames.Length == 0)
                frames = LoadViaTexture(path);

            if (frames == null || frames.Length == 0)
            {
                frames = System.Array.Empty<Sprite>();
                if (Warned.Add(key))
                    Game.Logger?.Warn(LogTag, $"取不到任何帧（Sprite/Texture 两条路都空）：{path}");
            }
            else
            {
                SortByFrameIndex(frames);
                if (mode == SpritePivotMode.UnifiedCanvasAnchor) frames = UnifyCanvasAnchor(frames, key);
            }

            Cache[key] = frames;
            return frames;
        }

        /// <summary>精灵目录已缓存的帧（`AsImported` 模式；调试/自检用）。没缓存过返回空数组。</summary>
        public static Sprite[] Cached(string path)
        {
            Sprite[] frames;
            return Cache.TryGetValue((int)SpritePivotMode.AsImported + "|" + path, out frames)
                ? frames : System.Array.Empty<Sprite>();
        }

        /// <summary>「帧号 → 扁平化下标」映射缓存（键与 <see cref="LoadDir(string, SpritePivotMode)"/> 同）。</summary>
        private static readonly Dictionary<string, int[]> FrameNoMap = new Dictionary<string, int[]>();

        /// <summary>「帧号 → 下标」恒等断言的去重集合（每个目录只打印一次）。</summary>
        private static readonly HashSet<string> FrameNoMapLogged = new HashSet<string>();

        /// <summary>
        /// 「帧号（`frame_NNN` 的 `NNN` = `.sc` 里的 shape 序号）→ 帧数组下标」映射表。
        /// <para>
        /// 返回 `int[]`：`map[n]` = 帧号 `n` 对应的下标，取不到 = `-1`；数组长度 = 该目录**最大帧号 + 1**。
        /// 单位目录（1 张 PNG = 1 个 Sprite）应当**恒等**（`map[n] == n`）—— 这里**断言并打印**
        /// （目录名 / `frames.Length` / `map.Length` / 是否恒等；非恒等则给出前若干个 `fN→idx` 作为映射规则），
        /// 因为「帧号 ≠ 下标」一旦发生而无人知晓，表现就是「攻击动画取错帧」这类最难查的静默错误。
        /// </para>
        /// <para>
        /// ⚠️ **只用于单位/建筑目录的逐帧取帧**。<see cref="ArenaView"/> 的塔/竞技场目录
        /// （一张 PNG 被切成多个子 Sprite ⇒ 下标 ≠ 帧号）**不走这里**（那片是 `ArenaView.cs` 的地盘）。
        /// </para>
        /// </summary>
        /// <param name="path">`Assets/Resources/` 下的相对目录（与 <see cref="LoadDir(string, SpritePivotMode)"/> 同参）。</param>
        /// <param name="mode">锚点模式（同一目录不同 mode 各有各的键，避免拿到别的 mode 的映射）。</param>
        public static int[] FrameNumberMap(string path, SpritePivotMode mode)
        {
            if (string.IsNullOrEmpty(path)) return System.Array.Empty<int>();
            var key = (int)mode + "|" + path;
            int[] map;
            if (FrameNoMap.TryGetValue(key, out map) && map != null) return map;

            var frames = LoadDir(path, mode);
            map = BuildFrameNumberMap(frames);
            FrameNoMap[key] = map;

            if (FrameNoMapLogged.Add(key))
            {
                var identity = map.Length == frames.Length;
                for (var n = 0; identity && n < map.Length; n++)
                    if (map[n] != n) identity = false;
                Game.Logger?.Info(LogTag,
                    $"帧号→下标映射：dir={path} frames.Length={frames.Length} map.Length={map.Length} " +
                    (identity
                        ? "恒等（1 PNG = 1 Sprite ⇒ 帧号 == 下标）"
                        : "**非恒等**（下标 ≠ 帧号）：" + DescribeFrameMap(map)));
            }
            return map;
        }

        /// <summary>由帧数组建「帧号 → 下标」表；同帧号取**首次**出现的下标（稳定）。</summary>
        private static int[] BuildFrameNumberMap(Sprite[] frames)
        {
            var maxNo = -1;
            for (var i = 0; i < frames.Length; i++)
            {
                var n = ParseFrameIndex(frames[i]);
                if (n > maxNo) maxNo = n;
            }
            var map = new int[maxNo + 1];
            for (var n = 0; n < map.Length; n++) map[n] = -1;          // -1 = 该帧号在此目录不存在
            for (var i = 0; i < frames.Length; i++)
            {
                var n = ParseFrameIndex(frames[i]);
                if (n >= 0 && n < map.Length && map[n] < 0) map[n] = i;
            }
            return map;
        }

        /// <summary>把「下标 ≠ 帧号」的前若干项写成一行（断言不恒等时作为映射规则证据打印）。</summary>
        private static string DescribeFrameMap(int[] map)
        {
            var sb = new System.Text.StringBuilder();
            var shown = 0;
            for (var n = 0; n < map.Length && shown < 8; n++)
            {
                if (map[n] != n)
                {
                    sb.Append('f').Append(n).Append("→idx").Append(map[n]).Append(' ');
                    shown++;
                }
            }
            if (shown == 0) sb.Append("(前 8 项无差异)");
            return sb.ToString();
        }

        /// <summary>
        /// 把一目录的帧统一到**同一个纹理坐标锚点**上重建（见 <see cref="SpritePivotMode.UnifiedCanvasAnchor"/>）。
        /// <para>
        /// 前提（不满足就**降级保原样 + Warn**，⛔ 不静默）：**所有帧的画布尺寸一致**。
        /// <para>
        /// ⚠️ 这里一开始写成"所有帧必须来自**同一张贴图**"，跑起来立刻发现是错的（实测日志：
        /// `无法统一锚点（同目录的帧不属于同一张贴图）` 对**每一个**单位目录都打了）：
        /// 单位素材是**一帧一张 PNG**（`chr_knight_out` 486 张 187×181 的 PNG ⇒ 486 张贴图，
        /// 每张只被切成 1 个子 Sprite），而"一张 PNG 多个子 Sprite"只出现在
        /// 竞技场（23 张 → 25 个）与塔（214 张 → 236 个）。两者**坐标系是同一个**：
        /// 每张 PNG 都是**同一尺寸的完整画布**，`rect` 就是"内容在这块画布上的位置"。
        /// 所以正确的判据是 **画布尺寸一致**（不是贴图对象同一），重建时用**每帧自己的**贴图。
        /// </para>
        /// <para>
        /// 锚点 = 全目录 `rect` 并集的中心（画布像素坐标；两种情形下 `rect` 都在同一套画布坐标里）。
        /// </para>
        /// </summary>
        /// <para>
        /// <b>换算式</b>（`Sprite.Create` 的 `pivot` 参数是"**相对 rect 归一化**"，不是相对整张贴图 ——
        /// 这一条是实测出来的，见 `.ai-tmp/test/T3-pivot-semantics.txt` / 探针
        /// `tools/probes/FlowProbe.PivotSemantics`，⛔ 不靠回忆）：
        /// <c>pivot = ((anchorX - rect.x) / rect.width, (anchorY - rect.y) / rect.height)</c>。
        /// 重建出来的 Sprite 名字带 <see cref="GeneratedSpritePrefix"/> 前缀 ⇒ <see cref="ClearCache"/> 能销毁它们
        /// （导入出来的那份归 Unity 资源管，本类 ⛔ 不 Destroy）。
        /// </para>
        /// </summary>
        /// <remarks>
        /// `public` 而不是 `private`：判据探针（`tools/probes/FlowProbe.cs`）要能在**编辑模式**下
        /// 直接对它断言（`LoadDir` 走 `Game.Res`，编辑模式下为空；若只能经 `LoadDir` 验证，
        /// 每改一次锚点算法都要进一次 Play 才能看一眼结果）。
        /// </remarks>
        public static Sprite[] UnifyCanvasAnchor(Sprite[] frames, string cacheKey)
        {
            var tex0 = frames[0] != null ? frames[0].texture : null;
            if (tex0 == null)
            {
                WarnAnchorDegrade(cacheKey, "首帧没有贴图（Texture2D 为空）");
                return frames;
            }
            // 画布尺寸必须一致（各帧可以是各自的贴图，但都必须是同一尺寸的完整画布）。
            var canvasW = tex0.width;
            var canvasH = tex0.height;
            for (var i = 1; i < frames.Length; i++)
            {
                var s = frames[i];
                if (s == null || s.texture == null) continue;
                if (s.texture.width != canvasW || s.texture.height != canvasH)
                {
                    WarnAnchorDegrade(cacheKey,
                        $"同目录的帧画布尺寸不一致（首帧 {canvasW}x{canvasH}，第 {i} 帧 " +
                        $"{s.texture.width}x{s.texture.height}）—— 无法定义共用锚点");
                    return frames;
                }
            }

            var minX = float.MaxValue; var minY = float.MaxValue;
            var maxX = float.MinValue; var maxY = float.MinValue;
            for (var i = 0; i < frames.Length; i++)
            {
                var s = frames[i];
                if (s == null) continue;
                var r = s.rect;
                if (r.width <= 0.01f || r.height <= 0.01f) continue;
                if (r.xMin < minX) minX = r.xMin;
                if (r.yMin < minY) minY = r.yMin;
                if (r.xMax > maxX) maxX = r.xMax;
                if (r.yMax > maxY) maxY = r.yMax;
            }
            if (minX > maxX || minY > maxY)
            {
                WarnAnchorDegrade(cacheKey, "所有帧的 rect 都无效（宽高 <= 0）");
                return frames;
            }

            var anchorX = (minX + maxX) * 0.5f;
            var anchorY = (minY + maxY) * 0.5f;
            var ppu = frames[0].pixelsPerUnit > 0.01f ? frames[0].pixelsPerUnit : FallbackPixelsPerUnit;

            var rebuilt = new Sprite[frames.Length];
            var failed = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                var s = frames[i];
                if (s == null) { failed++; continue; }
                var r = s.rect;
                if (r.width <= 0.01f || r.height <= 0.01f) { rebuilt[i] = s; failed++; continue; }
                var pivot = new Vector2((anchorX - r.x) / r.width, (anchorY - r.y) / r.height);
                // ⚠️ 用**每帧自己的**贴图（单位素材是一帧一张 PNG；`tex0` 只在"一张 PNG 多个
                //    子 Sprite"的竞技场/塔上才等于它）。用 `tex0` 会把别的帧画成第一帧的图。
                var fppu = s.pixelsPerUnit > 0.01f ? s.pixelsPerUnit : ppu;
                var made = Sprite.Create(s.texture, r, pivot, fppu);
                made.name = GeneratedSpritePrefix + s.name;
                rebuilt[i] = made;
            }
            if (failed > 0)
                Game.Logger?.Warn(LogTag,
                    $"{failed} 帧无法重建（rect 无效 / 元素为 null），这些帧保留导入态 —— 它们与其余帧的锚点不一致（会位移）");

            Game.Logger?.Info(LogTag,
                $"统一锚点：{cacheKey} 帧={frames.Length} 并集=({minX:F0},{minY:F0})-({maxX:F0},{maxY:F0}) " +
                $"锚点(纹理像素)=({anchorX:F1},{anchorY:F1}) ppu={ppu:F0}（逐帧 pivot 已换算成相对各自 rect 的归一化值）");
            return rebuilt;
        }

        /// <summary>降级留痕（每个目录只报一次）：拿不到贴图 / 帧不同源 ⇒ 保留导入态 Sprite。</summary>
        private static void WarnAnchorDegrade(string cacheKey, string why)
        {
            if (Warned.Add("pivot:" + cacheKey))
                Game.Logger?.Warn(LogTag,
                    $"无法统一锚点（{why}）⇒ 保留导入态 Sprite（逐帧 pivot = 各自裁剪框中心 ⇒ 换帧会位移）：{cacheKey}");
        }

        /// <summary>清缓存（出图时调；`Sprite.Create` 造出来的 Sprite 由这里 Destroy，避免泄漏）。</summary>
        public static void ClearCache()
        {
            foreach (var pair in Cache)
            {
                var frames = pair.Value;
                if (frames == null || frames.Length == 0) continue;
                // 只销毁"现场造出来"的（名字以 GeneratedSpritePrefix 开头），引擎加载的不归我们释放。
                for (var i = 0; i < frames.Length; i++)
                {
                    var s = frames[i];
                    // ⚠️ 必须写全 `UnityEngine.Object.Destroy`：本类是 **static class**，没有
                    // `MonoBehaviour.Destroy` 这个实例成员 —— 只写 `Destroy(s)` 会 CS0103
                    //（真机编译才报；离线 Roslyn 自检当时没抓到，见 registry.md 的已知盲区）。
                    if (s != null && s.name.StartsWith(GeneratedSpritePrefix)) UnityEngine.Object.Destroy(s);
                }
            }
            Cache.Clear();
            Warned.Clear();
            FrameNoMap.Clear();
            FrameNoMapLogged.Clear();
            UnitView.ClearClipCache();   // 帧缓存清了 ⇒ 帧号→下标映射与由此派生的档位帧序列一并失效
        }

        /// <summary>现场造出来的 Sprite 的名字前缀（用于在 ClearCache 里区分归属）。</summary>
        public const string GeneratedSpritePrefix = "gen_";

        private static Sprite _white;

        /// <summary>
        /// 共享的 1×1 白色精灵（`pixelsPerUnit = 1` ⇒ 恰好 1 个世界单位 = 1 格）。
        /// 纯色底图 / 占位块都用它，避免每个 View 各造一张贴图（贴图多了会各自打断合批）。
        /// </summary>
        public static Sprite WhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "White1x1" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false, false);
            _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            return _white;
        }

        private static Sprite[] LoadViaTexture(string path)
        {
            var textures = Game.Res != null ? Game.Res.LoadAll<Texture2D>(path) : null;
            if (textures == null || textures.Length == 0) return System.Array.Empty<Sprite>();

            var sprites = new Sprite[textures.Length];
            for (var i = 0; i < textures.Length; i++)
            {
                var t = textures[i];
                if (t == null) continue;
                var s = Sprite.Create(t, new Rect(0f, 0f, t.width, t.height), new Vector2(0.5f, 0.5f), FallbackPixelsPerUnit);
                s.name = GeneratedSpritePrefix + t.name;
                sprites[i] = s;
            }
            if (Warned.Add(path))
                Game.Logger?.Warn(LogTag,
                    $"目录被导成 Texture（不是 Sprite），已现场造 {sprites.Length} 个 Sprite（PPU={FallbackPixelsPerUnit}）：{path}");
            return sprites;
        }

        private static void SortByFrameIndex(Sprite[] frames)
        {
            // 插入排序：帧数最多 ~1000，且**几乎已经有序**（Resources 批量加载通常按名序），
            // 这里不值得引入 Array.Sort + 比较器闭包（每次加载都要分配）。
            for (var i = 1; i < frames.Length; i++)
            {
                var current = frames[i];
                var currentIndex = ParseFrameIndex(current);
                var j = i - 1;
                while (j >= 0 && ParseFrameIndex(frames[j]) > currentIndex)
                {
                    frames[j + 1] = frames[j];
                    j--;
                }
                frames[j + 1] = current;
            }
        }

        /// <summary>取文件名里的帧序号（见 <see cref="ParseFrameIndex(string)"/>）。解析不出时返回 -1（不抛）。</summary>
        private static int ParseFrameIndex(Sprite s)
        {
            if (s == null) return -1;
            return ParseFrameIndex(s.name);
        }

        /// <summary>
        /// 帧序号 = 名字里**倒数第二段**的纯数字段。
        /// <para>
        /// <b>为什么不是"尾部数字串"（修复前的写法，真机上就是它让帧序失效）</b>：Unity 对
        /// "一张 PNG 多个子 Sprite"的导入会把 Sprite 命名成 <c>&lt;文件名&gt;_&lt;子序号&gt;</c>，
        /// 实测本工程的单位帧名是 <c>frame_000_0</c>（`.meta` 的 `name: frame_000_0`）——
        /// 尾部那段数字是**子序号**，对本目录的 486 帧恒为 `0` ⇒ 486 帧拿到同一个排序键
        /// ⇒ <see cref="SortByFrameIndex"/> 变成空转 ⇒ 播放顺序变成 `Resources` 批量加载的**偶然顺序**。
        /// 倒过来取倒数第二段（`000`）才是真正的帧序号。
        /// </para>
        /// <para>
        /// 实测取值（探针 `tools/probes/FlowProbe.FrameIndexRule` 与离线脚本
        /// `tools/probes/check_frame_order.py` 都打印同一张表）：
        /// <c>frame_000_0→0</c>、<c>frame_485_0→485</c>、<c>frame_7_1→7</c>、<c>frame_003→3</c>、
        /// <c>gen_frame_012→12</c>；没有数字段的名字（`White1x1` / `ArenaSeg`）→ <c>-1</c>。
        /// </para>
        /// <para>
        /// ⚠️ **同一 PNG 内的多个子 Sprite 会拿到相同的键** ⇒ 它们之间的先后由**稳定排序**保持为
        /// 导入器给的顺序。这对单位目录无影响（实测 `chr_knight_out` 486 张 PNG = 486 个 Sprite，无多重），
        /// 对竞技场/塔目录则是**刻意保持现状**（这两处按硬编码下标取帧，实测"排序后"与"LoadAll 原始顺序"
        /// 逐项相同 ⇒ 修复对它们零影响，见 `tools/probes/FlowProbe.IndexMap`）。
        /// </para>
        /// </summary>
        public static int ParseFrameIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            var segs = name.Split('_');
            for (var i = segs.Length - 2; i >= 0; i--)
            {
                if (segs[i].Length == 0 || !IsAllDigits(segs[i])) continue;
                int value;
                if (int.TryParse(segs[i], out value)) return value;
            }
            // 只有一段数字（如 `frame_003` / `gen_frame_012`）时上面找不到 ⇒ 认最后一段
            if (segs.Length > 0 && IsAllDigits(segs[segs.Length - 1]))
            {
                int value;
                if (int.TryParse(segs[segs.Length - 1], out value)) return value;
            }
            return -1;
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (var i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }
    }
}
