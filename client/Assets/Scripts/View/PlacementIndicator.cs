using CloverEngine;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 拖放出牌的**落点指示**渲染 + 部署合法性预校验（D9）。
    ///
    /// <para>
    /// <b>职责边界</b>：本类只做两件事 ——
    /// ① 在世界空间画一个圆盘（合法=绿 / 非法=红）；
    /// ② 提供一个**纯函数** <see cref="IsLegalDeploy(DeployInput,float,float,bool)"/> 做客户端预校验。
    /// 「按下手牌 → 跟随指针 → 抬起发 C2S」的**手势与 HUD 部分不在这里**（属于 `UI/Panels/HudPanel.cs`），
    /// 它只管调 <see cref="Show"/> / <see cref="Hide"/>，并在抬起时发 <c>Events.Battle.PlayCardRequest</c>。
    /// </para>
    /// <para>
    /// <b>为什么判定规则放在 View 而不是面板里</b>：契约 §5.5 要求「⛔ 不许在面板里另写一套判定」。
    /// 几何全部来自 <see cref="GameConst"/>（河/桥/半场/国王塔占地），规则出自
    /// `策划/策划案/皇室战争参考规格.md` §2.1。**最终裁决永远在服务端**，这里只是"别让玩家看着能放、结果被拒"。
    /// </para>
    /// <para>
    /// <b>落点图形取自原版素材</b>（差异登记见 `策划/差异登记.tsv`）：`effects_out` 的 f221（我方：细白环，分组表 `:4777`
    /// `spell_radius`）/ f294（敌方：深色圆盘 + 红边 + 外圈刻度，分组表 `:4815` `Poison`）。
    /// 同族的 `spell_*_radius` 帧列**全都是 f221** ⇒ 原版所有法术共用同一张环、靠**缩放**适配半径，
    /// 这也是本类 <see cref="Show"/> 的缩放口径。
    /// </para>
    /// <para>
    /// ⛔ 旧注释里那句「解包素材里没有'落点指示'图」**是错的**（已订正），当时才用 `Texture2D`
    /// 现场生成了一张圆盘。自制圆盘<b>保留为兜底</b>（素材加载失败时画面不能什么都没有），
    /// 但正常路径不再用它。
    /// </para>
    /// </summary>
    public sealed class PlacementIndicator : MonoBehaviour
    {
        /// <summary>日志标签（`Game.Logger` 用；⛔ 不写裸 `Debug` 日志）。</summary>
        public const string LogTag = "PlacementIndicator";

        /// <summary>合法落点颜色（半透明绿）。</summary>
        public static readonly Color LegalColor = new Color(0.25f, 0.95f, 0.35f, 0.35f);

        /// <summary>非法落点颜色（半透明红）。</summary>
        public static readonly Color IllegalColor = new Color(0.95f, 0.25f, 0.20f, 0.35f);

        /// <summary>指示圆盘贴图边长（像素）。64 足够 —— 它永远是半透明的，放大后靠线性过滤就够平滑。</summary>
        private const int DiscTexSize = 64;

        /// <summary>
        /// 落点指示的**转速**（度/秒）。⚠️ **本项目自定**：原版这张环是单帧静态图（f221/f294
        /// 各自只有 1 帧），"它是否旋转、转多快"在解包素材里**没有出处**。
        /// 用户原话是「原版是转圈的」⇒ 这里把原版环**旋转**起来作为进度感的来源，转速取 90°/s
        /// （4 秒一圈，不晃眼）。**该常量是推断，已登记在 `策划/差异登记.tsv`**。
        /// 环上有可见的不对称特征（f221 四个基本方向的小标记 / f294 外圈的刻度），所以旋转看得见。
        /// </summary>
        private const float SpinDegreesPerSecond = 90f;

        private SpriteRenderer _renderer;

        /// <summary>
        /// 渲染器**自带**的默认材质（`AddComponent` 之后立刻记下）。没有混合的帧 / 兜底圆盘要写回它 ——
        /// ⛔ 不许往 `sharedMaterial` 写 null（那会让渲染器落到 Unity 的错误材质上，整块画成洋红）。
        /// </summary>
        private Material _rendererDefault;

        private Sprite _discSprite;
        private bool _visible;

        /// <summary>最近一次 <see cref="Show"/> 的参数（原版环形素材是**异步**加载的，到货后要按它重画）。</summary>
        private Vector2 _lastTile;
        private float _lastRadius;
        private bool _lastLegal;

        /// <summary>
        /// 落点处的**卡面虚影**（拖动时显示的那张卡本身）。原版拖动时落点上就是这张卡，不是一个小图标。
        /// <para>
        /// ⛔ 它**不是本指示器的子节点**：指示器自身带 `localScale`（按半径缩放）且每帧旋转，
        /// 卡面若挂在它下面会被一起缩放 + 跟着转。两者位置相同、层级相邻，各管各的变换。
        /// </para>
        /// </summary>
        private SpriteRenderer _dropCard;

        /// <summary>当前要显示的卡面（由 HUD 在起拖时给出；<c>null</c> = 不显示虚影）。</summary>
        private Sprite _dropArt;

        /// <summary>落点卡面虚影的着色（半透明 —— 它是"还没落下"的预览，见 <see cref="_dropCard"/>）。</summary>
        private static readonly Color DropCardColor = new Color(1f, 1f, 1f, 0.62f);

        /// <summary>就位计时的**剩余秒数**（&gt; 0 = 正在显示；由 <see cref="ShowDeployRing"/> 起算）。</summary>
        private float _deployLeft;

        /// <summary><see cref="ShowDeployRing"/> 传入的总时长（秒）—— 扇形扫过角度的分母。</summary>
        private float _deployTotal = 1f;

        /// <summary>
        /// 当前是否处于「出牌落位计时」形态（显示原版 `troopDeployTimer` 而不是落点范围环）。
        /// 由 <see cref="ShowDeployRing"/> 置位、由 <see cref="Show"/>/<see cref="Hide"/>（拖动接手）复位。
        /// </summary>
        private bool _deployTimerMode;

        /// <summary>
        /// 落位计时件的尺寸口径。原版件自身像素 ÷ <see cref="ArenaView.GroundPxPerTileX"/>（45.545 px/格）——
        /// 与 <see cref="DeployAreaView"/> 的 `deployArea_*` 一族**同一条**换算（同一张 `ui` 图集、同一批尺寸）。
        /// </summary>
        private const float TimerArtPxPerTile = ArenaView.GroundPxPerTileX;

        /// <summary>表盘（f252）已到的原版图元。</summary>
        private Sprite _timerBodySprite;

        /// <summary>扫过片（我方 f253 / 敌方 f256）已到的原版图元。</summary>
        private Sprite _timerSweepFriendly, _timerSweepHostile;

        /// <summary>指针 / 表冠（f255）已到的原版图元。</summary>
        private Sprite _timerStemSprite;

        /// <summary>计时件图元的到货计数（4 帧齐了才画；与 <see cref="Frames"/> 的数量无关，故单独计）。</summary>
        private int _timerLoaded;

        /// <summary>扇形的旋转轴（= 表盘盘心，自身不缩放，只被 <see cref="Update"/> 转）。</summary>
        private Transform _timerSweepPivot;

        /// <summary>扇形（原版扫过片）；以**自身左上角**对齐盘心，绕盘心转 = 扫过。</summary>
        private SpriteRenderer _timerSweep;

        /// <summary>指针 / 表冠（原版 f255，按 clip 的平移量摆在盘心）。</summary>
        private SpriteRenderer _timerStem;

        /// <summary>
        /// 指针（f255）相对表盘中心的位置（**表盘像素**）：取 clip 1245 里 child3(f255) `tx=1.00 ty=5.55`
        /// 与 child0(f252) `tx=1.10 ty=6.00` 的位移差 ÷ 表盘缩放 0.089。
        /// `.sc` 舞台的 y 向下为正 ⇒ 折到 Unity（y 向上）后取正号。
        /// </summary>
        private static readonly Vector2 TimerClipStemOffsetPx = new Vector2(-1.124f, 5.056f);

        /// <summary>计时件取不到时只留痕一次。</summary>
        private bool _timerWarned;

        /// <summary>落位的是不是敌方单位（决定用红扫过片 f256 还是蓝扫过片 f253）。</summary>
        private bool _timerHostile;

        /// <summary>
        /// 落点卡面虚影的世界高度（格）—— <b>本项目自定</b>：卡面虚影取**部队落点圈的外径**
        /// （`HudPanel.PlacementRadiusTilesDrop` = 1 格半径 ⇒ 直径 2 格），宽按卡面素材自身比例。
        /// ⛔ 不跟法术半径缩放：法术半径最大 3.5 格 ⇒ 卡面会被放大到 7 格，不是"落下前的预览"了。
        /// </summary>
        private const float DropCardHeightTiles = 2f;

        /// <summary>原版环形素材（我方 f221 / 敌方 f294）；未到货时为 null ⇒ 用 <see cref="_discSprite"/> 兜底。</summary>
        private Sprite _ringFriendly;
        private Sprite _ringHostile;

        /// <summary>环形素材到货（或确认取不到）后只留痕一次。</summary>
        private static bool _ringLoadLogged;

        /// <summary>当前是否可见。</summary>
        public bool Visible => _visible;

        /// <summary>
        /// 在 <paramref name="parent"/> 下建一个落点指示器（世界空间）。返回的实例由调用方持有；
        /// ⛔ 不要每帧 Create —— 它内部只有 1 个 `SpriteRenderer`，用 <see cref="Show"/>/<see cref="Hide"/> 复用。
        /// </summary>
        public static PlacementIndicator Create(Transform parent)
        {
            var go = new GameObject(LogTag);
            if (parent != null) go.transform.SetParent(parent, false);
            var indicator = go.AddComponent<PlacementIndicator>();
            indicator.Build();
            return indicator;
        }

        private void Build()
        {
            _discSprite = CreateDiscSprite();
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = _discSprite;
            _renderer.color = LegalColor;
            // 默认材质必须在**第一次写** `sharedMaterial` 之前记下（见 `_rendererDefault`）。
            _rendererDefault = SpriteBlendMaterial.RememberDefault(_renderer);
            // 落点指示必须压在**所有单位与塔之下**（它是一块地板高亮），所以给一个很低的 sortingOrder。
            // 与 UnitView/ArenaView 的层级约定：底图 0 / 塔 50 / 指示 200 / 单位 1000（见各自注释）。
            _renderer.sortingOrder = ArenaLayers.Instance.Indicator;

            // 落点卡面虚影：**兄弟节点**（理由见 `_dropCard` 的注释），压在落点环之下、单位之上仍在其下
            //（单位层从 `ArenaLayers.Instance.Actor` 起，见 UnitView）。
            var dropGo = new GameObject("DropCard");
            dropGo.transform.SetParent(transform.parent, false);
            _dropCard = dropGo.AddComponent<SpriteRenderer>();
            _dropCard.sortingOrder = ArenaLayers.Instance.Indicator - 1;
            _dropCard.color = DropCardColor;
            dropGo.SetActive(false);

            // 落位计时件的子节点是**兄弟节点**（同 `_dropCard`）：计时件形态由本 GameObject 的渲染器当表盘，
            // 而 `Reapply` 会给本 GameObject 写 `localScale`（表盘像素 → 格）—— 挂成子节点会把这个缩放
            // **再乘一次**（扇形/指针会被放大到表盘的 localScale 倍）。位置因此一律写世界坐标，见 ApplyTimer。
            var pivotGo = new GameObject("DeployTimerSweepPivot");
            pivotGo.transform.SetParent(transform.parent, false);
            _timerSweepPivot = pivotGo.transform;
            var sweepGo = new GameObject("DeployTimerSweep");
            sweepGo.transform.SetParent(_timerSweepPivot, false);
            _timerSweep = sweepGo.AddComponent<SpriteRenderer>();
            _timerSweep.color = Color.white;            // ⛔ 不 tint：原版扫过片自身就是蓝/红
            _timerSweep.sortingOrder = ArenaLayers.Instance.Indicator + 2;
            var stemGo = new GameObject("DeployTimerStem");
            stemGo.transform.SetParent(transform.parent, false);
            _timerStem = stemGo.AddComponent<SpriteRenderer>();
            _timerStem.color = Color.white;
            _timerStem.sortingOrder = ArenaLayers.Instance.Indicator + 3;
            // 两个兄弟节点只在计时件形态下露面（见 SetTimerPiecesActive / SetVisible）。
            pivotGo.SetActive(false);
            stemGo.SetActive(false);

            LoadRingSprites();
            LoadTimerSprites();
            SetVisible(false);
        }

        /// <summary>
        /// 设置落点处显示的**卡面**（原版拖放时落点上显示的就是这张卡本身）。
        /// 传 <c>null</c> 关掉虚影（抬手 / 取消 / 就位读条开始时）。
        /// </summary>
        public void SetDropCard(Sprite art)
        {
            _dropArt = art;
            if (_dropCard == null) return;
            _dropCard.sprite = art;
            _dropCard.gameObject.SetActive(art != null);
            if (art != null) ReapplyDropCard();
        }

        /// <summary>落点卡面虚影的位置与缩放（位置跟落点走，缩放取"卡面高 = 落点直径"）。</summary>
        private void ReapplyDropCard()
        {
            if (_dropCard == null || _dropArt == null) return;
            _dropCard.transform.position = transform.position;
            var native = _dropArt.bounds.size.y;
            if (native <= 0f) native = 1f;
            var scale = DropCardHeightTiles / native;
            _dropCard.transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// 落点处的**就位计时件**：在 <paramref name="tileXY"/> 摆出原版 `troopDeployTimer`（灰色表盘
        /// `f252` + 我方蓝 / 敌方红扫过片 `f253`/`f256` + 指针 `f255`），扫过片按进度绕盘心转一圈，
        /// 持续 <paramref name="seconds"/> 秒（= 出牌落位期，服务端 `deploy_time` 的长度）。
        /// <para>
        /// <b>图元出处</b>：`原版资源/sc/ui_v215.sc` 的 clip 1245 `troopDeployTimer_player` /
        /// 1247 `troopDeployTimer_enemy`（分组 `spawn`，帧列 252-257；表 `策划/原版UI素材名称索引.md:433-434`）。
        /// 原版这一族**不是**范围环 —— 表盘像素均值 (216,208,204) 灰、扫过片 (116,200,255) 蓝。
        /// </para>
        /// <para>
        /// <paramref name="radiusTiles"/> 只保留为调用方接口的一部分（落点半径），计时件自身尺寸按
        /// 原版件像素换算（见 <see cref="TimerArtPxPerTile"/>），⛔ 不跟法术半径缩放。
        /// </para>
        /// <para>
        /// <paramref name="hostile"/> = 落位的是不是**敌方**单位。原版两个 clip 只有扫过片颜色不同
        /// （`player` = 蓝 f253 / `enemy` = 红 f256），表盘与指针两路共用。
        /// </para>
        /// </summary>
        public void ShowDeployRing(Vector2 tileXY, float radiusTiles, float seconds, bool hostile = false)
        {
            SetDropCard(null);
            // ⚠️ 顺序：`Show` 会把 `_deployLeft` 归零并把形态复位成范围环，所以形态与倒计时都要在它**之后**置。
            Show(tileXY, radiusTiles, true);
            _deployTimerMode = true;
            _timerHostile = hostile;
            _deployTotal = seconds > 0f ? seconds : 1f;
            _deployLeft = _deployTotal;
            Reapply();
        }

        /// <summary>
        /// 异步取 4 帧原版计时件图元（表盘 252 / 我方 253 / 敌方 256 / 指针 255）。
        /// 到货前 <see cref="ApplyTimer"/> 不画（画面留空总好过画一个自造件）。
        /// </summary>
        private void LoadTimerSprites()
        {
            if (Game.Res == null)
            {
                WarnOnceTimer("Game.Res 为空（漏了 CloverRes.Init？）⇒ 出牌落位计时件画不出来");
                return;
            }
            LoadTimerOne(ResPaths.EffectDeployTimerBody);
            LoadTimerOne(ResPaths.EffectDeployTimerSweepFriendly);
            LoadTimerOne(ResPaths.EffectDeployTimerSweepHostile);
            LoadTimerOne(ResPaths.EffectDeployTimerStem);
        }

        /// <summary>取一帧计时件图元；4 帧齐了且此刻正显示计时件时按最近一次参数补画。</summary>
        private void LoadTimerOne(int frame)
        {
            var path = ResPaths.EffectFrame(ResPaths.EffectDeployTimer, frame);
            Game.Res.LoadAsset<Sprite>(path, sprite =>
            {
                if (sprite == null)
                {
                    WarnOnceTimer($"原版落位计时件加载不到：{path}");
                    return;
                }
                if (frame == ResPaths.EffectDeployTimerBody) _timerBodySprite = sprite;
                else if (frame == ResPaths.EffectDeployTimerSweepFriendly) _timerSweepFriendly = sprite;
                else if (frame == ResPaths.EffectDeployTimerSweepHostile) _timerSweepHostile = sprite;
                else if (frame == ResPaths.EffectDeployTimerStem) _timerStemSprite = sprite;
                _timerLoaded++;
                if (_timerLoaded >= 4)
                    Game.Logger?.Info(LogTag,
                        $"落位计时件已就绪：表盘 f{ResPaths.EffectDeployTimerBody} / 我方扫过片 f{ResPaths.EffectDeployTimerSweepFriendly} / " +
                        $"敌方 f{ResPaths.EffectDeployTimerSweepHostile} / 指针 f{ResPaths.EffectDeployTimerStem}" +
                        "（出处 策划/原版UI素材名称索引.md:433-434 troopDeployTimer_player/enemy）");
                if (_deployTimerMode && _visible) ApplyTimer();
            });
        }

        /// <summary>计时件取不到时只留痕一次（⛔ 不静默）。</summary>
        private void WarnOnceTimer(string message)
        {
            if (_timerWarned) return;
            _timerWarned = true;
            Game.Logger?.Warn(LogTag, message + "（只报一次；落位本身照常，只是没有计时件显示）");
        }

        /// <summary>
        /// 异步取两张原版范围图元（我方 f221 / 敌方 f294）。到货前 <see cref="Show"/> 用自制圆盘兜底。
        /// <para>
        /// 为什么用 `Game.Res.LoadAsset&lt;Sprite&gt;`（异步）而不是 `SpriteBank.LoadDir`：这两张图**各自
        /// 就是一个用途目录里的单帧**，没有"整条取"的需求；`HudPanel` 取卡面走的也是这条入口。
        /// </para>
        /// </summary>
        private void LoadRingSprites()
        {
            if (Game.Res == null)
            {
                if (!_ringLoadLogged)
                {
                    _ringLoadLogged = true;
                    Game.Logger?.Warn(LogTag,
                        "Game.Res 为空（漏了 CloverRes.Init？）⇒ 落点指示只能用**自制圆盘**兜底" +
                        $"，取不到原版范围图元（{ResPaths.EffectRangeRing}/f{ResPaths.EffectRangeRingFriendly}）");
                }
                return;
            }
            LoadOneRing(ResPaths.EffectRangeRingFriendly, 0);
            LoadOneRing(ResPaths.EffectRangeRingHostile, 1);
        }

        private void LoadOneRing(int frame, int which)
        {
            var path = ResPaths.EffectFrame(ResPaths.EffectRangeRing, frame);
            Game.Res.LoadAsset<Sprite>(path, sprite =>
            {
                if (sprite == null)
                {
                    if (!_ringLoadLogged)
                    {
                        _ringLoadLogged = true;
                        Game.Logger?.Warn(LogTag,
                            $"原版范围图元加载不到（仍用自制圆盘兜底）：{path}；" +
                            "检查 .ai-tmp/hosts/copy_spell_fx.py 是否跑过、以及新 PNG 是否已被 Unity 导入");
                    }
                    return;
                }
                if (which == 0) _ringFriendly = sprite; else _ringHostile = sprite;
                if (!_ringLoadLogged)
                {
                    _ringLoadLogged = true;
                    Game.Logger?.Info(LogTag,
                        $"落点范围图元已就绪：我方 f{ResPaths.EffectRangeRingFriendly} / " +
                        $"敌方 f{ResPaths.EffectRangeRingHostile}（目录 {ResPaths.EffectRangeRing}；" +
                        "出处 策划/单位动画分组表.md :4777 spell_radius / :4815 Poison）");
                }
                // 素材是异步到的：如果此刻正显示着，按最近一次参数**重画**，否则会一直停在自制圆盘上。
                if (_visible) Reapply();
            });
        }

        /// <summary>
        /// 显示指示盘。
        /// </summary>
        /// <param name="tileXY">落点（**格**坐标，不是世界坐标 —— 与 `GameConst.TileToWorld` 同一套口径）。</param>
        /// <param name="radiusTiles">半径（格）。法术用其 AoE 半径；部队用 1 格。</param>
        /// <param name="legal">是否合法（由 <see cref="IsLegalDeploy(DeployInput,float,float,bool)"/> 判定）。</param>
        public void Show(Vector2 tileXY, float radiusTiles, bool legal)
        {
            // 拖动接手：把上一条就位计时的倒计时清掉（否则它会中途把这一轮拖动的环收走），
            // 并把形态复位成落点范围环（同一个实例不许两种形态叠着画）。
            _deployLeft = 0f;
            _deployTimerMode = false;
            _lastTile = tileXY;
            _lastRadius = radiusTiles;
            _lastLegal = legal;
            Reapply();
            SetVisible(true);
        }

        /// <summary>
        /// 按**最近一次** <see cref="Show"/> 的参数重画（位置 / 缩放 / 贴图 / 颜色）。
        /// <para>
        /// 缩放口径：原版所有法术共用同一张环，靠**缩放**适配半径（见 <see cref="ResPaths.EffectRangeRing"/>）
        /// ⇒ `缩放 = 目标直径 / 该贴图自身的世界直径`。贴图直径从 `sprite.bounds.size.x` 现读，
        /// ⛔ 不写死"环直径 1.24 格"这类会随素材变更而失效的常量。
        /// </para>
        /// </summary>
        private void Reapply()
        {
            if (_renderer == null) return;

            transform.position = GameConst.TileToWorld(_lastTile.x, _lastTile.y);

            if (_deployTimerMode)
            {
                ApplyTimer();
                return;
            }
            SetTimerPiecesActive(false);
            _renderer.sortingOrder = ArenaLayers.Instance.Indicator;

            var sprite = _lastLegal ? _ringFriendly : _ringHostile;
            if (sprite == null) sprite = _discSprite;      // 兜底：原版图元还没到货 / 取不到
            var usingFallback = ReferenceEquals(sprite, _discSprite);
            _renderer.sprite = sprite;

            var native = sprite != null ? sprite.bounds.size.x : 1f;
            if (native <= 0f) native = 1f;
            // 自制圆盘的 pixelsPerUnit = 贴图边长 ⇒ 它的 bounds 正好 1 个世界单位，走同一条算式也成立。
            var diameter = Mathf.Max(0.5f, _lastRadius * 2f);
            var scale = diameter / native;
            transform.localScale = new Vector3(scale, scale, 1f);

            // 两种贴图用两套着色（理由见 ApplyTint 的注释）。
            if (usingFallback) _renderer.color = _lastLegal ? LegalColor : IllegalColor;
            else _renderer.color = _lastLegal ? LegalTint : IllegalTint;

            // 原版该帧的 `blend_mode` 若为非 Normal（表 `SpriteBlendTable`：`Sprites/Effects/RangeRing`
            // 的敌方帧 = Add），走 `SpriteBlendMaterial` 的共享材质；兜底圆盘与表外帧写回渲染器自带的默认材质
            //（⛔ 不许写 null = 整块洋红，见 `SpriteBlendMaterial.For`）。
            SpriteBlendMaterial.Set(_renderer, usingFallback
                ? _rendererDefault
                : SpriteBlendMaterial.For(
                    ResPaths.EffectRangeRing,
                    _lastLegal ? ResPaths.EffectRangeRingFriendly : ResPaths.EffectRangeRingHostile,
                    _rendererDefault));

            ReapplyDropCard();
        }

        /// <summary>
        /// 按最近一次参数摆出原版落位计时件：灰色表盘（f252，居中于落点）+ 指针（f255，按 clip 的平移量）
        /// + 扫过片（f253 我方 / f256 敌方，以**自身左上角**对齐盘心）。
        /// <para>
        /// <b>尺寸</b>：件自身像素 ÷ <see cref="TimerArtPxPerTile"/>（⛔ 不写死"计时件 = N 格"这类常量）。
        /// <b>扫过</b>：盘心轴的角度由 <see cref="Update"/> 按 <see cref="ShowDeployRing"/> 的进度推。
        /// </para>
        /// </summary>
        private void ApplyTimer()
        {
            if (_renderer == null || _timerSweepPivot == null) return;
            if (_timerBodySprite == null) { SetTimerPiecesActive(false); return; }
            SetTimerPiecesActive(true);

            _renderer.sprite = _timerBodySprite;
            _renderer.color = Color.white;                  // ⛔ 不 tint：原版表盘自身就是灰的
            _renderer.sortingOrder = ArenaLayers.Instance.Indicator + 1;
            var bodyNative = _timerBodySprite.bounds.size;
            _renderer.transform.localScale = new Vector3(
                _timerBodySprite.rect.width / TimerArtPxPerTile / bodyNative.x,
                _timerBodySprite.rect.height / TimerArtPxPerTile / bodyNative.y, 1f);
            transform.localRotation = Quaternion.identity;  // 计时件不转（落点范围环才转）
            SpriteBlendMaterial.Set(_renderer, _rendererDefault);

            // 盘心 = 表盘中心（原版盘面圆心与表盘包围盒中心差 1.5 px < 1/10 格，取同一个点）。
            var center = transform.position;

            // 指针 / 表冠：平移量取 clip 1245 里 child3(f255) 与 child0(f252) 的位移差，
            // 再按表盘自身的缩放（0.089 / 1024）折成表盘像素 ⇒ 单位与表盘同一套（格）。
            if (_timerStem != null && _timerStemSprite != null)
            {
                _timerStem.sprite = _timerStemSprite;
                _timerStem.enabled = true;
                var stemNative = _timerStemSprite.bounds.size;
                _timerStem.transform.localScale = new Vector3(
                    _timerStemSprite.rect.width / TimerArtPxPerTile / stemNative.x,
                    _timerStemSprite.rect.height / TimerArtPxPerTile / stemNative.y, 1f);
                _timerStem.transform.position = center + new Vector3(
                    TimerClipStemOffsetPx.x / TimerArtPxPerTile,
                    TimerClipStemOffsetPx.y / TimerArtPxPerTile, 0f);
            }

            var sweepSprite = _timerHostile ? _timerSweepHostile : _timerSweepFriendly;
            if (_timerSweepPivot != null) _timerSweepPivot.position = center;
            if (_timerSweep != null && sweepSprite != null)
            {
                _timerSweep.sprite = sweepSprite;
                _timerSweep.enabled = true;
                var sweepNative = sweepSprite.bounds.size;
                var w = sweepSprite.rect.width / TimerArtPxPerTile;
                var h = sweepSprite.rect.height / TimerArtPxPerTile;
                _timerSweep.transform.localScale = new Vector3(w / sweepNative.x, h / sweepNative.y, 1f);
                // 扫过片的**左上角**就是盘心（原版这一片是绕盘心的扇形）⇒ 把它的左上角对到轴的原点。
                // 轴自身不缩放 ⇒ 这里的偏移量就是格。
                _timerSweep.transform.localPosition = new Vector3(w * 0.5f, -h * 0.5f, 0f);
            }
            ApplyTimerSweep();
        }

        /// <summary>按倒计时进度把扫过片转过对应角度（一圈 = 整个落位期）。</summary>
        private void ApplyTimerSweep()
        {
            if (_timerSweepPivot == null) return;
            var p = _deployTotal > 0f ? 1f - Mathf.Clamp01(_deployLeft / _deployTotal) : 1f;
            _timerSweepPivot.localRotation = Quaternion.Euler(0f, 0f, -360f * p);
        }

        /// <summary>
        /// 开 / 关计时件的两个**兄弟**节点（形态切换与显隐时用；⛔ 不动本 GameObject 自身的渲染器）。
        /// 它们不在本 GameObject 下，所以 `SetVisible` 的 `SetActive` 管不到。
        /// </summary>
        private void SetTimerPiecesActive(bool active)
        {
            if (_timerSweepPivot != null) _timerSweepPivot.gameObject.SetActive(active);
            if (_timerStem != null) _timerStem.gameObject.SetActive(active);
        }

        /// <summary>
        /// 合法落点着色（白 → 略带绿，alpha = 1 保留原图元不透明度）。
        /// <para>
        /// 只用于**原版范围环**：环自身已含红/白区分，整块染绿会把它染成一张绿环、丢掉那个区分
        /// ⇒ 原版素材上只做"合法性提示"的轻量着色（合法 = 略带绿，非法 = 压红 <see cref="IllegalTint"/>），
        /// 两者都保留原图元自己的明暗。**自制圆盘兜底时**用另两套半透明色
        /// （<see cref="LegalColor"/> / <see cref="IllegalColor"/>）—— 那张图是白的，不染色就没有合法性提示。
        /// </para>
        /// </summary>
        private static readonly Color LegalTint = new Color(0.80f, 1f, 0.85f, 1f);

        /// <summary>非法落点着色（压红 + 降一点不透明度，让"不能放"一眼可辨）。</summary>
        private static readonly Color IllegalTint = new Color(1f, 0.55f, 0.50f, 0.9f);

        /// <summary>隐藏指示盘（拖放结束 / 抬手）—— 连同落点卡面虚影与就位计时件一起收掉。</summary>
        public void Hide()
        {
            _deployLeft = 0f;
            _deployTimerMode = false;
            SetDropCard(null);
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_renderer != null) _renderer.enabled = visible;
            SetTimerPiecesActive(visible && _deployTimerMode);
            gameObject.SetActive(visible);
        }

        /// <summary>
        /// 推进当前形态的动画：**落点范围环**转圈 / **落位计时件**的扫过片走一圈
        /// （两种形态由 <see cref="_deployTimerMode"/> 区分，见 <see cref="ShowDeployRing"/>）。
        /// <para>
        /// 只在可见时推进（`SetVisible(false)` 会 `SetActive(false)`，本方法自然不会被调到）；
        /// 用 `unscaledDeltaTime`：对局暂停 / 时间倍率为 0 时，拖放手势仍然要有反馈。
        /// </para>
        /// </summary>
        private void Update()
        {
            // 落位计时：到点自己收掉（落位期 = 服务端 `deploy_time`，见 ShowDeployRing）。
            if (_deployLeft > 0f)
            {
                _deployLeft -= Time.unscaledDeltaTime;
                if (_deployLeft <= 0f)
                {
                    _deployLeft = 0f;
                    SetVisible(false);
                    return;
                }
            }
            if (_deployTimerMode)
            {
                ApplyTimerSweep();
                return;
            }
            transform.Rotate(0f, 0f, SpinDegreesPerSecond * Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (_discSprite != null) Destroy(_discSprite);
        }

        // ───────────────────────── 合法性预校验（纯函数，⛔ 不碰任何 Unity 状态） ─────────────────────────

        /// <summary>
        /// 放置判定的输入（由 `BattleViewRoot` 从塔的快照状态里填好）。
        /// <para>
        /// <b>为什么"敌/己"分开命名而不是传一个 6 塔数组</b>：判定只用到「敌方公主塔是否已被摧毁」（决定口袋区）
        /// 和「己方塔是否存活」（决定哪些格子被塔身占住）。数组会迫使调用方记住下标含义，
        /// 而索引错位在这里的后果是"客户端以为能放、服务端拒绝"——正是本类要避免的现象。
        /// </para>
        /// </summary>
        public struct DeployInput
        {
            /// <summary>我方队伍：0=BLUE 1=RED（契约 `BattleStartNotify.my_team`）。</summary>
            public int MyTeam;

            /// <summary>敌方左侧公主塔（x = <see cref="GameConst.BridgeCxATile"/>）是否**存活**。</summary>
            public bool EnemyLeftPrincessAlive;

            /// <summary>敌方右侧公主塔（x = <see cref="GameConst.BridgeCxBTile"/>）是否**存活**。</summary>
            public bool EnemyRightPrincessAlive;

            /// <summary>己方左侧公主塔是否存活（存活 ⇒ 其周围 1 格半径不可放置）。</summary>
            public bool OwnLeftPrincessAlive;

            /// <summary>己方右侧公主塔是否存活。</summary>
            public bool OwnRightPrincessAlive;
        }

        /// <summary>
        /// 客户端**预校验**：该落点看起来是否合法。
        /// <para>
        /// 规则逐条出自 `策划/策划案/皇室战争参考规格.md` §2.1（与 `server/game/core/arena.go` 同源）：
        /// <list type="number">
        /// <item>场地内：x ∈ [0,18)、y ∈ [0,32)。</item>
        /// <item>半场：<b>己方**本场 frame**</b> 的 y 上界 = <see cref="GameConst.RiverTopTile"/>(15) —— 止于**河岸**、不是中线。
        ///   「本场 frame」= <see cref="GameConst.MirrorTileYForTeam"/> 换算后的 y（RED 走 `y → 32 - y`）。
        ///   ⚠️ **两队的边界口径不对称**（与服务端 <c>arena.go:148-153</c> 的 <c>OwnHalf</c> 逐字一致）：
        ///   BLUE 的 `[0,15)` 上沿**不含**（河带第一行是水），RED 的 `[17,32)` 下沿**含**（河带下沿是实地）——
        ///   见 ⑤ 处的推导。⛔ 不是"特例"，是"下沿含、上沿不含"两组端点的必然结果。</item>
        /// <item>河面（y ∈ [15,17) 且**不在桥上**）：地面单位不可站；<b>桥面是实地**可放**</b>；<b>法术可落河面</b>。</item>
        /// <item>国王塔占地 **3×3 格**不可放置（`anchors.json` → `king_tower_footprint_tiles`；
        ///   本模型按 `tilemap.csv` 的 BLOCKED 区：x ∈ [7.5,10.5)、y ∈ [1.5,4.5)（BLUE）与镜像的 RED）。</item>
        /// <item>公主塔**存活**时其周围 **1 格半径**不可放置（§4.1「碰撞半径 1000 milli-tile = 1 格」）。
        ///   ⚠️ 这一条是**从严**取的：服务端若允许"把兵放在公主塔格上"，结果是玩家少了一块可选区域（无害）；
        ///   反过来（客户端说能放、服务端拒绝）才是要避免的现象。故取严。</item>
        /// <item>口袋区：**敌方某路公主塔已被摧毁** ⇒ 该路（以最近桥中心 x 判车道）可向敌方半场扩展，
        ///   上限是**该塔所在行（不含该行）**。</item>
        /// </list>
        /// </para>
        /// <para>⛔ 最终裁决在服务端；本函数的 `true` 不代表服务端一定接受。</para>
        /// </summary>
        /// <param name="input">三塔状态 + 我方队伍。</param>
        /// <param name="xTile">落点 x（格）。</param>
        /// <param name="yTile">落点 y（格，竞技场绝对坐标：小 = BLUE 后方）。</param>
        /// <param name="isSpell">该卡是否是法术（法术可落河面/敌方半场，且不受塔身占格限制？——
        /// 法术同样**不能**落在国王塔占地内，因为那里是建筑实体）。</param>
        public static bool IsLegalDeploy(DeployInput input, float xTile, float yTile, bool isSpell)
        {
            // ① 场地内
            if (xTile < 0f || xTile >= GameConst.ArenaTilesW) return false;
            if (yTile < 0f || yTile >= GameConst.ArenaTilesH) return false;

            // ② 国王塔占地（3×3）：BLUE 在 (9,3)，RED = y → 32 - y
            if (InsideKingFootprint(xTile, yTile, 0)) return false;
            if (InsideKingFootprint(xTile, yTile, 1)) return false;

            // ③ 河面（= 河带内**且不在桥上**；桥面是实地，地面单位可走、也因此可放）
            // ⚠️ 判"水"须与服务端 `server/game/core/arena.go:88-90` 的
            //    `IsWater = inRiverBand(y) && !onBridge(x)` **同源**：只按 y∈[15,17) 判会把两座桥也判成水。
            //    症状：敌方该路公主塔被摧毁、口袋区展开到桥面格时，客户端画红盘而服务端会收。
            var onWater = yTile >= GameConst.RiverTopTile && yTile < GameConst.RiverBottomTile
                          && !IsOnBridge(xTile);
            if (isSpell) return true; // 法术：可落河面 + 可落敌方半场（塔身占格已在 ② 排除）
            if (onWater) return false;

            // ④ 己方公主塔存活时的占位（半径 1 格 = 两边塔的 collision_radius 1000 milli-tile）
            var princessY = GameConst.PrincessTowerTileY; // ⛔ 用常量，不写 6.5f
            if (input.OwnLeftPrincessAlive && InsideCircle(xTile, yTile, PrincessXLeft(input.MyTeam), OwnFrameY(input.MyTeam, princessY), 1f))
                return false;
            if (input.OwnRightPrincessAlive && InsideCircle(xTile, yTile, PrincessXRight(input.MyTeam), OwnFrameY(input.MyTeam, princessY), 1f))
                return false;

            // ⑤ 半场（换算到"我方 frame"的 y）
            //   ⚠️ **两队的边界口径不对称**，须与服务端 `arena.go:148-153` 的 `OwnHalf` 逐字一致 ——
            //     BLUE（绝对 y∈[0,15)）  ⇒ 本场 yOwn∈[0,15)   上沿**不含**（河带第一行 y=15.0 是水）
            //     RED （绝对 y∈[17,32)） ⇒ 本场 yOwn∈(0,15]  下沿**含**（河带下沿 y=17.0 是实地）
            //   ⛔ **不能两队一刀切用 `<`**：RED 在 y=17.0（yOwn=15.0）会被判非法、服务端却收。
            //   ⛔ **也不能一刀切改成 `<=`**：桥豁免（③）之后，BLUE 的 y=15.0 **落在桥面上**时不再被水拦掉，
            //      一刀切 `<=` 会把它判成"可放"，而服务端 `OwnHalf(BLUE)` 上沿不含 ⇒ 反而**新增**反向分歧。
            //   （两种形状按上述两条边界逐点比过）
            var yOwn = OwnFrameY(input.MyTeam, yTile);
            var inOwnHalf = GameConst.IsMirroredForTeam(input.MyTeam)
                ? yOwn <= GameConst.RiverTopTile   // RED：下沿含
                : yOwn < GameConst.RiverTopTile;   // BLUE：上沿不含
            if (inOwnHalf) return true;

            // ⑥ 口袋区：敌方该路公主塔已毁 ⇒ 允许到"该塔所在行（不含该行）"
            //    我方 frame 里敌方公主塔恒在 y = 32 - 6.5 = 25.5（走到哪一侧都成立）。
            var enemyPrincessYInOwnFrame = GameConst.ArenaTilesH - princessY;
            if (yOwn >= enemyPrincessYInOwnFrame) return false;

            // 车道 = 最近桥中心 x（§2.1「以最近桥中心 x 判定」）
            var leftLane = Mathf.Abs(xTile - GameConst.BridgeCxATile) <= Mathf.Abs(xTile - GameConst.BridgeCxBTile);
            return leftLane ? !input.EnemyLeftPrincessAlive : !input.EnemyRightPrincessAlive;
        }

        /// <summary>把竞技场绝对 y 换算成「该队伍的 frame」y（BLUE 原样、RED `y → 32 - y`）。</summary>
        public static float OwnFrameY(int team, float yTile)
        {
            return GameConst.MirrorTileYForTeam(yTile, team);
        }

        /// <summary>该队伍的左侧公主塔 x（就是左桥中心 x —— §2「塔与桥对齐」；x 不做镜像）。</summary>
        public static float PrincessXLeft(int team)
        {
            return GameConst.BridgeCxATile;
        }

        /// <summary>该队伍的右侧公主塔 x。</summary>
        public static float PrincessXRight(int team)
        {
            return GameConst.BridgeCxBTile;
        }

        /// <summary>国王塔占地（3 格宽的重心在 <see cref="GameConst.KingTowerTileX"/>，3×3 ⇒ 半宽 1.5）。</summary>
        private static bool InsideKingFootprint(float xTile, float yTile, int team)
        {
            const float half = 1.5f; // 3×3 占地的一半；3 来自参考规格 §2「国王塔占地 3×3 格」
            var kx = GameConst.KingTowerTileX;
            var ky = GameConst.MirrorTileYForTeam(GameConst.KingTowerTileY, team);
            return xTile >= kx - half && xTile < kx + half && yTile >= ky - half && yTile < ky + half;
        }

        private static bool InsideCircle(float xTile, float yTile, float cx, float cy, float radius)
        {
            var dx = xTile - cx;
            var dy = yTile - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>
        /// 该 x 是否落在某座桥的跨度内。与服务端 `arena.go:75-85` 的 `onBridge` **同口径**：
        /// 半开区间 `cx - half ≤ x &lt; cx + half`（左闭右开 ⇒ 桥右边界 x=4.5 / 15.5 不属于桥），
        /// 且负 x 提前否掉（与服务端开头的 `if xMilli &lt; 0 { return false }` 一致）。
        /// <para>
        /// 桥 = 河带内**不是水**的那两段（数据只标了河，没标桥；见 `arena.go:70-74`）。这两座桥
        /// 让地面单位能过河，所以它们既是可行走面、也是可放置面。
        /// </para>
        /// </summary>
        private static bool IsOnBridge(float xTile)
        {
            if (xTile < 0f) return false;
            return InBridgeSpan(xTile, GameConst.BridgeCxATile) || InBridgeSpan(xTile, GameConst.BridgeCxBTile);
        }

        /// <summary>单座桥的半开区间判定（`cx` = 桥中心 x）。</summary>
        private static bool InBridgeSpan(float xTile, float cx)
        {
            return xTile >= cx - GameConst.BridgeHalfTile && xTile < cx + GameConst.BridgeHalfTile;
        }

        /// <summary>
        /// 生成落点圆盘贴图（自制，非原版素材）。软边（最后 12% 半径上做 alpha 渐变）避免锯齿。
        /// </summary>
        private static Sprite CreateDiscSprite()
        {
            var tex = new Texture2D(DiscTexSize, DiscTexSize, TextureFormat.RGBA32, false)
            {
                name = "PlacementDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[DiscTexSize * DiscTexSize];
            var center = (DiscTexSize - 1) * 0.5f;
            const float outer = DiscTexSize * 0.5f;
            const float softFrom = 0.88f; // 88% 半径开始渐隐
            for (var y = 0; y < DiscTexSize; y++)
            {
                for (var x = 0; x < DiscTexSize; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var d = Mathf.Sqrt(dx * dx + dy * dy) / outer; // 0 = 中心, 1 = 圆边
                    float a;
                    if (d >= 1f) a = 0f;
                    else if (d <= softFrom) a = 1f;
                    else a = 1f - (d - softFrom) / (1f - softFrom);
                    pixels[y * DiscTexSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            // pixelsPerUnit = 贴图边长 ⇒ 生成的 Sprite 正好占 1 个世界单位（= 1 格），
            // 于是 Show() 里 localScale = 直径(格) 就是对的，不需要再除以任何常量。
            return Sprite.Create(tex, new Rect(0, 0, DiscTexSize, DiscTexSize), new Vector2(0.5f, 0.5f), DiscTexSize);
        }
    }
}
