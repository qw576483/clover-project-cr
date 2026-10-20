using System.Collections.Generic;
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
    /// <b>落点图形取自原版素材</b>（差异登记见 `策划/对照表.md` §D.3）：`effects_out` 的 f221（黑环，分组表 `:4777`
    /// `spell_radius`）—— 原版 `spell_*_radius` 一族的帧列**全都是 f221**，可放 / 不可放两个环
    /// 也是**同一张 f221**、只差颜色变换（见 <see cref="DropRingMaterial"/>）⇒ 本类合法 / 非法共用这张环、
    /// 靠**缩放**适配半径（也就是 <see cref="Show"/> 的缩放口径）。
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

        /// <summary>场景里已建的全部落位指示器（同一次出牌的去重需要跨实例判断，见 <see cref="ShowDeployRing"/>）。</summary>
        private static readonly List<PlacementIndicator> Instances = new List<PlacementIndicator>();

        /// <summary>
        /// 拖放落点要显示的模型（HUD 写入，见 <see cref="SetDragPreviewModel"/>）；<c>null</c> = 不显示。
        /// 群体卡出几张就带几只（数量 = 长度，见 <see cref="_dragPreviewOffsets"/>）。
        /// </summary>
        private static Sprite _dragPreviewSprite;

        /// <summary>拖放落点模型的逐单位缩放（与 <see cref="_dragPreviewSprite"/> 同源）。</summary>
        private static float _dragPreviewScale = 1f;

        /// <summary>
        /// 拖放落点各只模型相对落点的偏移（**格**，与服务端 `spawnGroup` 的 `SummonLayout` 同一套布局；
        /// `null`/空 = 一只、摆落点）。
        /// </summary>
        private static Vector2[] _dragPreviewOffsets;

        /// <summary>
        /// 同一次出牌判定的落点距离（格）。群体卡逐只落点都在 `summon_radius`（≤ 0.7 格）内，
        /// 1.5 格既能覆盖同一批，又不会吞掉相邻的另一次出牌。
        /// </summary>
        private const float SameDeployTileRadius = 1.5f;

        /// <summary>合法落点颜色（半透明绿）。</summary>
        public static readonly Color LegalColor = new Color(0.25f, 0.95f, 0.35f, 0.35f);

        /// <summary>非法落点颜色（半透明红）。</summary>
        public static readonly Color IllegalColor = new Color(0.95f, 0.25f, 0.20f, 0.35f);

        /// <summary>指示圆盘贴图边长（像素）。64 足够 —— 它永远是半透明的，放大后靠线性过滤就够平滑。</summary>
        private const int DiscTexSize = 64;

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
        /// 落点处的**半透明单位 / 建筑模型**池（每只单位一个渲染器，idle 首帧）。
        /// 拖动时落点上站的是"将要落下的那一批单位"（群体卡逐只各一，布局见 <see cref="_previewOffsets"/>）。
        /// <para>
        /// ⛔ 它们**不是本指示器的子节点**：指示器自身带 `localScale`（按半径缩放），模型挂在它下面
        /// 会被按半径再缩放一次。两者位置同源（落点 + 偏移）、层级相邻，各管各的变换。
        /// </para>
        /// </summary>
        private readonly List<SpriteRenderer> _previewModels = new List<SpriteRenderer>();

        /// <summary>当前预览模型的精灵（`null` = 不显示；法术卡恒为 `null`）。</summary>
        private Sprite _previewSprite;

        /// <summary>预览模型的逐单位缩放（与场上同类单位同一口径，见 <see cref="SetPreviewModel"/>）。</summary>
        private float _previewScale = 1f;

        /// <summary>当前预览各只模型相对落点的偏移（**格**；空 = 一只、摆落点）。</summary>
        private Vector2[] _previewOffsets;

        /// <summary>预览模型的透明度 —— 它是"还没落下"的预览。</summary>
        private const float PreviewAlpha = 0.6f;

        /// <summary>就位计时的**剩余秒数**（&gt; 0 = 正在显示；由 <see cref="ShowDeployRing"/> 起算）。</summary>
        private float _deployLeft;

        /// <summary><see cref="ShowDeployRing"/> 传入的总时长（秒）—— 扇形扫过角度的分母。</summary>
        private float _deployTotal = 1f;

        /// <summary>
        /// 当前是否处于「出牌落位计时」形态（显示原版 `troopDeployTimer` 而不是落点范围环）。
        /// 由 <see cref="ShowDeployRing"/> 置位、由 <see cref="Show"/>/<see cref="Hide"/>（拖动接手）复位。
        /// </summary>
        private bool _deployTimerMode;

        /// <summary>被"同一次出牌的后续单位"去重掉的表盘次数（自检用）。</summary>
        private int _deployRingDeduped;

        /// <summary>当前这块表盘属于哪张卡（同一次出牌的去重钥匙之一；0 = 未指定）。</summary>
        private int _deployCardId;

        /// <summary>
        /// 本实例的落位计时件**是否正在走**（形态是计时件且还剩时间）—— 供**实例池**分配空闲块用
        /// （`BattleViewRoot.AcquireDeployRing` 优先取"没在走"的那块）。
        /// </summary>
        public bool DeployTimerActive { get { return _deployTimerMode && _deployLeft > 0f; } }

        /// <summary>
        /// 落位计时件的**剩余秒数**（= 0 表示没在走）。池在"全忙"时按它取**最先到期**的那块复用
        /// （先到期的先让位 ⇒ 尽量保住"刚开始转的那块"完整走完一圈）。
        /// </summary>
        public float DeployLeft { get { return _deployLeft; } }

        /// <summary>
        /// 表盘扫过角（度）：**自正上方、顺时针** 0 → 360 恰好一圈；不在走时 = 0。
        /// 供实机采样核对"一次出牌只转一圈"（与指针轴的 |z| 同一读数）。
        /// </summary>
        public float DeploySweepDegrees
        {
            get { return DeployTimerActive ? 360f * Mathf.Clamp01(1f - _deployLeft / _deployTotal) : 0f; }
        }

        /// <summary>当前这块表盘所属的卡 id（0 = 未指定）。</summary>
        public int DeployCardId { get { return _deployCardId; } }

        /// <summary>场景里正在走的落位表盘数量（同屏同一次出牌应恒为 1）。</summary>
        public static int ActiveDeployTimerCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Instances.Count; i++)
                {
                    var it = Instances[i];
                    if (it != null && it.DeployTimerActive) n++;
                }
                return n;
            }
        }

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

        /// <summary>扫过片的旋转轴（= 表盘盘心，自身不缩放，只被 <see cref="ApplyTimerSweep"/> 转）。</summary>
        private Transform _timerSweepPivot;

        /// <summary>扫过片（原版 f253/f256，按阵营取色）；以**自身左上角**对齐盘心，绕盘心转 = 扫过。</summary>
        private SpriteRenderer _timerSweep;

        /// <summary>指针的旋转轴（= 表盘盘心；指针按 clip 平移量摆在轴上，随进度绕盘心转一圈）。</summary>
        private Transform _timerStemPivot;

        /// <summary>指针 / 表冠（原版 f255，按 clip 的平移量摆在盘心上方）。</summary>
        private SpriteRenderer _timerStem;

        /// <summary>
        /// 表盘面的**绿白扇形**（未走 = 绿、已走 = 白；起点正上方、顺时针、恰一圈）。
        /// 画布与 f252 同尺寸同枢轴（见 <see cref="EnsureTimerPie"/>）⇒ 与表盘逐像素对齐。
        /// </summary>
        private SpriteRenderer _timerPie;

        /// <summary>扇形贴图画布（与 f252 画布同尺寸）。</summary>
        private Texture2D _timerPieTex;

        /// <summary>扇形贴图对应的精灵（枢轴 / PPU 与 f252 一致）。</summary>
        private Sprite _timerPieSprite;

        /// <summary>扇形贴图最近一次画到的进度（0..1，量化到 <see cref="PieSteps"/>）—— 没变不重画。</summary>
        private float _timerPieDrawnP = -1f;

        /// <summary>扇形进度的量化档数（一圈 360° ⇒ 每档 1.4°，肉眼看不出台阶）。</summary>
        private const int PieSteps = 256;

        /// <summary>
        /// 表盘面圆在 f252 画布上的位置（**画布像素，原点左上**）与半径：
        /// `frame_252.png` 中心行灰面 (216,208,204) 跨 x≈13..41、中心列跨 y≈22..52
        /// ⇒ 面圆心 (27,37)、半径 15 px。
        /// </summary>
        private static readonly Vector2 TimerFaceCenterPx = new Vector2(27f, 37f);

        /// <summary>表盘面圆半径（画布像素，量法见 <see cref="TimerFaceCenterPx"/>）。</summary>
        private const float TimerFaceRadiusPx = 15f;

        /// <summary>
        /// 表盘扇形「未走」颜色（绿）：与 <see cref="LegalColor"/> 同色相、不透明
        /// （绿白两段是表盘读数，不随阵营变）。
        /// </summary>
        public static readonly Color TimerUnwalkedColor = new Color(0.25f, 0.95f, 0.35f, 1f);

        /// <summary>表盘扇形「已走」颜色（白）。</summary>
        public static readonly Color TimerWalkedColor = Color.white;

        /// <summary>
        /// 指针（f255）相对表盘中心的位置（**表盘像素**）：取 clip 1245 里 child3(f255) `tx=1.00 ty=5.55`
        /// 与 child0(f252) `tx=1.10 ty=6.00` 的位移差 ÷ 表盘缩放 0.089。
        /// `.sc` 舞台的 y 向下为正 ⇒ 折到 Unity（y 向上）后取正号。进度 0 时指针落在这个位置 = 指向正上方。
        /// </summary>
        private static readonly Vector2 TimerClipStemOffsetPx = new Vector2(-1.124f, 5.056f);

        /// <summary>计时件取不到时只留痕一次。</summary>
        private bool _timerWarned;

        /// <summary>落位的是不是敌方单位（决定用红扫过片 f256 还是蓝扫过片 f253）。</summary>
        private bool _timerHostile;

        /// <summary>
        /// 原版环形素材 f221（合法 / 非法**共用**同一张黑环，只靠 <see cref="DropRingMaterial"/> 的颜色变换区分）；
        /// 未到货时为 null ⇒ 用 <see cref="_discSprite"/> 兜底。
        /// </summary>
        private Sprite _ringFriendly;

        /// <summary>环形素材到货（或确认取不到）后只留痕一次。</summary>
        private static bool _ringLoadLogged;

        /// <summary>
        /// f221 的**可见圆直径（像素）**：`Effects/RangeRing/frame_221.png` 画布 474×537，
        /// 非透明像素包围盒 124×129（横向 124 = 圆直径，纵向 129 多出的 5 px 是上下的柔光）。
        /// <para>
        /// 缩放必须按这个像素直径换算：图元自带一大圈透明留白，按 `sprite.bounds.size`（= 整张画布）
        /// 缩到目标直径，画出来的圆只有目标的 124/474 ≈ 26%（实测 2 格环只剩半个格子宽）。
        /// </para>
        /// </summary>
        private const float RingVisibleDiameterPx = 126.5f;

        /// <summary>环的像素换算只报一次（供实机核对可见直径）。</summary>
        private static bool _ringScaleLogged;

        /// <summary>当前是否可见。</summary>
        public bool Visible => _visible;

        /// <summary>
        /// 当前是否正在显示**拖放落点环**，⛔ 不含落位计时件形态。
        /// 供实机 driver 判定"这一帧画面上有没有范围环"（自检读数，与 `BattleViewRoot` 的计数器同一用途）。
        /// </summary>
        public bool ShowingRangeRing => _visible && !_deployTimerMode;

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
            Instances.Add(this);
            _discSprite = CreateDiscSprite();
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = _discSprite;
            _renderer.color = LegalColor;
            // 默认材质必须在**第一次写** `sharedMaterial` 之前记下（见 `_rendererDefault`）。
            _rendererDefault = SpriteBlendMaterial.RememberDefault(_renderer);
            // 落点指示必须压在**所有单位与塔之下**（它是一块地板高亮），所以给一个很低的 sortingOrder。
            // 与 UnitView/ArenaView 的层级约定：底图 0 / 塔 50 / 指示 200 / 单位 1000（见各自注释）。
            _renderer.sortingOrder = ArenaLayers.Instance.Indicator;

            // 落点预览模型：**兄弟节点**（理由见 `_previewModels` 的注释），按需建（见 EnsurePreviewModelSlot）。
            // 层级与场上单位同一层（`ArenaLayers.Instance.DepthOrder`），看起来就是"站在落点上的那一批单位"。

            // 落位计时件的子节点是**兄弟节点**（同 `_previewModels`）：计时件形态由本 GameObject 的渲染器当表盘，
            // 而 `Reapply` 会给本 GameObject 写 `localScale`（表盘像素 → 格）—— 挂成子节点会把这个缩放
            // **再乘一次**（扫过片/指针会被放大到表盘的 localScale 倍）。位置因此一律写世界坐标，见 ApplyTimer。
            var pivotGo = new GameObject("DeployTimerSweepPivot");
            pivotGo.transform.SetParent(transform.parent, false);
            _timerSweepPivot = pivotGo.transform;
            var sweepGo = new GameObject("DeployTimerSweep");
            sweepGo.transform.SetParent(_timerSweepPivot, false);
            _timerSweep = sweepGo.AddComponent<SpriteRenderer>();
            _timerSweep.color = Color.white;            // ⛔ 不 tint：原版扫过片自身就是蓝/红
            _timerSweep.sortingOrder = ArenaLayers.Instance.Indicator + 2;
            // 指针：另一根盘心轴上（指针与扫过片的角度不同 —— 指针自正上方起转，见 ApplyTimerSweep）。
            var stemPivotGo = new GameObject("DeployTimerStemPivot");
            stemPivotGo.transform.SetParent(transform.parent, false);
            _timerStemPivot = stemPivotGo.transform;
            var stemGo = new GameObject("DeployTimerStem");
            stemGo.transform.SetParent(_timerStemPivot, false);
            _timerStem = stemGo.AddComponent<SpriteRenderer>();
            _timerStem.color = Color.white;
            _timerStem.sortingOrder = ArenaLayers.Instance.Indicator + 4;
            // 表盘面的绿白扇形：挂在本 GameObject 下（与表盘同变换 ⇒ 逐像素对齐，见 EnsureTimerPie）。
            var pieGo = new GameObject("DeployTimerPie");
            pieGo.transform.SetParent(transform, false);
            _timerPie = pieGo.AddComponent<SpriteRenderer>();
            _timerPie.color = Color.white;
            _timerPie.sortingOrder = ArenaLayers.Instance.Indicator + 3;
            // 三个计时件节点只在计时件形态下露面（见 SetTimerPiecesActive / SetVisible）。
            pivotGo.SetActive(false);
            stemPivotGo.SetActive(false);
            pieGo.SetActive(false);

            LoadRingSprites();
            LoadTimerSprites();
            SetVisible(false);
        }

        /// <summary>
        /// 落点处显示**半透明单位 / 建筑模型**（每只单位一个渲染器，该单位 idle 首帧）。
        /// </summary>
        /// <param name="sprite">模型精灵；<c>null</c> = 不显示（法术卡 / 抬手 / 取消 / 就位读条开始）。</param>
        /// <param name="scale">该单位的逐单位缩放（与场上同类单位同一份，见
        /// <c>UnitView.PreviewScaleFor</c>）⇒ 预览与真实单位同大。</param>
        /// <param name="offsetsTiles">逐只模型相对落点的偏移（**格**，服务端 `spawnGroup` 同一布局）；
        /// <c>null</c>/空 = 一只、摆落点。法术卡的数量口径 = 0（不显示）。</param>
        public void SetPreviewModel(Sprite sprite, float scale, Vector2[] offsetsTiles = null)
        {
            _previewSprite = sprite;
            _previewScale = scale > 0f ? scale : 1f;
            _previewOffsets = offsetsTiles;
            RebuildPreviewModels();
        }

        /// <summary>
        /// 按 <see cref="_previewSprite"/> / <see cref="_previewOffsets"/> 把模型池调到所需只数并逐只显隐
        /// （显 = 逐只同精灵、alpha <see cref="PreviewAlpha"/>；隐 = 全部关掉）。
        /// </summary>
        private void RebuildPreviewModels()
        {
            var want = _previewSprite == null ? 0
                : (_previewOffsets == null || _previewOffsets.Length == 0 ? 1 : _previewOffsets.Length);
            for (var i = 0; i < want; i++) EnsurePreviewModelSlot(i);
            for (var i = 0; i < _previewModels.Count; i++)
            {
                var r = _previewModels[i];
                if (r == null) continue;
                var on = i < want;
                r.sprite = on ? _previewSprite : null;
                r.gameObject.SetActive(on);
            }
            if (want > 0) ReapplyPreviewModel();
        }

        /// <summary>取第 <paramref name="index"/> 只预览模型渲染器（不够就建，兄弟节点，理由见 <see cref="_previewModels"/>）。</summary>
        private SpriteRenderer EnsurePreviewModelSlot(int index)
        {
            while (_previewModels.Count <= index) _previewModels.Add(null);
            var r = _previewModels[index];
            if (r != null) return r;
            var go = new GameObject("DeployPreviewModel" + _previewModels.Count);
            go.transform.SetParent(transform.parent, false);
            r = go.AddComponent<SpriteRenderer>();
            r.color = new Color(1f, 1f, 1f, PreviewAlpha);
            go.SetActive(false);
            _previewModels[index] = r;
            return r;
        }

        /// <summary>
        /// 落点处的卡面图不再显示：落点画的是"将要落下的单位"（见 <see cref="SetPreviewModel"/>）。
        /// 保留这个入口给仍按旧参数调用的一方，传入的卡面被忽略。
        /// </summary>
        public void SetDropCard(Sprite art) { }

        /// <summary>
        /// 拖放中的落点预览模型（HUD 每次指针移动时给出）：精灵 = 该单位 idle 首帧，
        /// 缩放 = 该单位在战场上的逐单位缩放，逐只偏移 = 服务端 `spawnGroup` 同一布局
        /// （群体卡几张几只、单体一只、法术 0 只传 <c>null</c>）。
        /// 下一次 <see cref="Show"/> 起效；<see cref="Hide"/> 清掉。
        /// </summary>
        public static void SetDragPreviewModel(Sprite sprite, float scale, Vector2[] offsetsTiles = null)
        {
            _dragPreviewSprite = sprite;
            _dragPreviewScale = scale > 0f ? scale : 1f;
            _dragPreviewOffsets = offsetsTiles;
        }

        /// <summary>
        /// 附近是否已有一块**正在走**的同一次出牌表盘 —— 钥匙 = (cardId, tile, 阵营)，
        /// tile 用 <see cref="SameDeployTileRadius"/> 的半径判"同一个落点"。
        /// </summary>
        /// <param name="cardId">要开的表盘所属卡 id；0 = 未指定（此时只按 tile + 阵营判）。</param>
        private bool HasRunningTimerFor(int cardId, Vector2 tileXY, bool hostile)
        {
            var tileWorld = GameConst.TileToWorld(tileXY.x, tileXY.y);
            var world = new Vector3(tileWorld.x, tileWorld.y, 0f);
            for (var i = 0; i < Instances.Count; i++)
            {
                var other = Instances[i];
                if (other == null || ReferenceEquals(this, other)) continue;
                if (!other.DeployTimerActive) continue;
                if (other._timerHostile != hostile) continue;
                if (cardId != 0 && other._deployCardId != cardId) continue;
                var d = other.transform.position - world;
                if (d.sqrMagnitude <= SameDeployTileRadius * SameDeployTileRadius) return true;
            }
            return false;
        }

        /// <summary>
        /// 预览模型各只的位置 / 缩放 / 深度序：位置 = 落点 + 逐只布局偏移（格 → 世界，1 格 = 1 世界单位），
        /// 缩放取逐单位缩放。
        /// </summary>
        private void ReapplyPreviewModel()
        {
            if (_previewSprite == null) return;
            var p = transform.position;
            for (var i = 0; i < _previewModels.Count; i++)
            {
                var r = _previewModels[i];
                if (r == null || !r.gameObject.activeSelf) continue;
                var off = (_previewOffsets != null && i < _previewOffsets.Length)
                    ? _previewOffsets[i]
                    : Vector2.zero;
                r.transform.position = new Vector3(p.x + off.x, p.y + off.y, 0f);
                r.transform.localScale = new Vector3(_previewScale, _previewScale, 1f);
                r.sortingOrder = ArenaLayers.Instance.DepthOrder(p.y + off.y);
            }
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
        /// <para>
        /// <paramref name="cardId"/> = 这块表盘属于哪张卡（0 = 未指定）。它与 tile / 阵营一起构成
        /// "同一次出牌"的去重钥匙（见 <see cref="HasRunningTimerFor"/>）：同一张卡在同一落点附近
        /// 已经有一块在走时，⛔ 不再开第二块（重开 = 扇形从 0 再转一圈）。
        /// </para>
        /// </summary>
        public void ShowDeployRing(Vector2 tileXY, float radiusTiles, float seconds, bool hostile = false, int cardId = 0)
        {
            SetPreviewModel(null, 1f);
            // 表盘一旦开始走，⛔ 不许重开：重开会让扇形从 0 再转一圈，看起来就是"钟转了好几圈"。
            if (DeployTimerActive) return;
            if (HasRunningTimerFor(cardId, tileXY, hostile))
            {
                _deployRingDeduped++;
                Game.Logger?.Info(LogTag,
                    $"落位表盘去重：(card={cardId}, tile=({tileXY.x:F2},{tileXY.y:F2}), hostile={hostile}) 的表盘仍在走 " +
                    $"本局累计去重={_deployRingDeduped}");
                return;
            }
            // ⚠️ 顺序：`Show` 会把 `_deployLeft` 归零并把形态复位成范围环，所以形态与倒计时都要在它**之后**置。
            Show(tileXY, radiusTiles, true);
            _deployTimerMode = true;
            // `Show` 会把拖放模型带上（它是拖放形态的件）—— 计时件形态不要模型，这里清掉。
            SetPreviewModel(null, 1f);
            _timerHostile = hostile;
            _deployCardId = cardId;
            _deployTotal = seconds > 0f ? seconds : 1f;
            _deployLeft = _deployTotal;
            Reapply();
            Game.Logger?.Info(LogTag,
                $"落位表盘开转：card={cardId} tile=({tileXY.x:F2},{tileXY.y:F2}) hostile={hostile} " +
                $"时长={_deployTotal:F2}s（0→360 一圈）");
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
        /// 异步取原版范围图元 f221。到货前 <see cref="Show"/> 用自制圆盘兜底。
        /// <para>
        /// 为什么用 `Game.Res.LoadAsset&lt;Sprite&gt;`（异步）而不是 `SpriteBank.LoadDir`：这张图
        /// **就是一个用途目录里的单帧**，没有"整条取"的需求；`HudPanel` 取卡面走的也是这条入口。
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
            LoadOneRing(ResPaths.EffectRangeRingFriendly);
        }

        private void LoadOneRing(int frame)
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
                _ringFriendly = sprite;
                if (!_ringLoadLogged)
                {
                    _ringLoadLogged = true;
                    Game.Logger?.Info(LogTag,
                        $"落点范围图元已就绪：f{ResPaths.EffectRangeRingFriendly}" +
                        $"（目录 {ResPaths.EffectRangeRing}；出处 策划/单位动画分组表.md :4777 spell_radius）");
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
            // 落点模型：拖放形态显示"将要落下的那一批单位"（由 HUD 随指针每次移动写入，见 SetDragPreviewModel）。
            SetPreviewModel(_dragPreviewSprite, _dragPreviewScale, _dragPreviewOffsets);
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
            // 拖放落点环是一块地板高亮，仍在所有单位之下。
            _renderer.sortingOrder = ArenaLayers.Instance.Indicator;

            // 合法 / 非法共用原版 f221（黑环），差别只在下面的颜色变换（见 DropRingMaterial）。
            var sprite = _ringFriendly;
            if (sprite == null) sprite = _discSprite;      // 兜底：原版图元还没到货 / 取不到
            var usingFallback = ReferenceEquals(sprite, _discSprite);
            _renderer.sprite = sprite;

            var native = sprite != null ? sprite.bounds.size.x : 1f;
            if (native <= 0f) native = 1f;
            // 自制圆盘的 pixelsPerUnit = 贴图边长 ⇒ 它的 bounds 正好 1 个世界单位、圆占满画布，直接除即可。
            // 原版 f221 的圆只占画布的 124/474 ⇒ 必须先把"可见圆直径"折成世界尺寸再除（见 RingVisibleDiameterPx）。
            var diameter = Mathf.Max(0.5f, _lastRadius * 2f);
            var scale = usingFallback
                ? diameter / native
                : diameter * sprite.rect.width / (RingVisibleDiameterPx * native);
            transform.localScale = new Vector3(scale, scale, 1f);

            if (!usingFallback && !_ringScaleLogged)
            {
                _ringScaleLogged = true;
                Game.Logger?.Info(LogTag,
                    $"落点环尺寸口径：f{ResPaths.EffectRangeRingFriendly} 画布 {sprite.rect.width:0}×{sprite.rect.height:0}px、" +
                    $"可见圆 {RingVisibleDiameterPx:0.#}px ⇒ 半径 {_lastRadius:0.##} 格时缩放 {scale:0.###}，" +
                    $"可见直径 = {diameter:0.##} 格（= 2 × 半径）");
            }

            // 着色：原版图元（黑环）一律用**白色顶点色** —— 颜色全交给材质那一层（见 DropRingMaterial
            // 的注释：顶点色只能乘不能加）；只有自制兜底圆盘才用半透明红/绿顶点色（那张图是白的）。
            if (usingFallback) _renderer.color = _lastLegal ? LegalColor : IllegalColor;
            else _renderer.color = Color.white;

            // 材质两条路：① 用上原版图元 = 颜色变换材质（拖放环见 DropRingMaterial）；
            // ② 兜底圆盘 = 渲染器自带默认材质。
            // ⛔ 一律不许写 null = 整块洋红（见 `SpriteBlendMaterial.For`）。
            SpriteBlendMaterial.Set(_renderer, (usingFallback
                ? _rendererDefault
                : DropRingMaterial()) ?? _rendererDefault);

            ReapplyPreviewModel();
        }

        /// <summary>
        /// 按最近一次参数摆出原版落位计时件：灰色表盘（f252，居中于落点）+ 表盘面的**绿白扇形**
        /// （未走 = 绿、已走 = 白）+ 指针（f255，按 clip 的平移量、绕盘心转）+ 扫过片
        /// （f253 我方 / f256 敌方，以**自身左上角**对齐盘心、按阵营取色）。
        /// <para>
        /// <b>尺寸</b>：件自身像素 ÷ <see cref="TimerArtPxPerTile"/>（⛔ 不写死"计时件 = N 格"这类常量）。
        /// <b>扫过</b>：两根盘心轴的角度由 <see cref="ApplyTimerSweep"/> 按 <see cref="ShowDeployRing"/> 的进度推。
        /// </para>
        /// </summary>
        private void ApplyTimer()
        {
            if (_renderer == null || _timerSweepPivot == null) return;
            if (_timerBodySprite == null) { SetTimerPiecesActive(false); return; }
            SetTimerPiecesActive(true);
            EnsureTimerPie();

            _renderer.sprite = _timerBodySprite;
            _renderer.color = Color.white;                  // ⛔ 不 tint：原版表盘自身就是灰的
            _renderer.sortingOrder = ArenaLayers.Instance.Indicator + 1;
            var bodyNative = _timerBodySprite.bounds.size;
            _renderer.transform.localScale = new Vector3(
                _timerBodySprite.rect.width / TimerArtPxPerTile / bodyNative.x,
                _timerBodySprite.rect.height / TimerArtPxPerTile / bodyNative.y, 1f);
            transform.localRotation = Quaternion.identity;  // 表盘不转，转的是扫过片与指针
            SpriteBlendMaterial.Set(_renderer, _rendererDefault);

            // 盘心 = 表盘中心（原版盘面圆心与表盘包围盒中心差 1.5 px < 1/10 格，取同一个点）。
            var center = transform.position;

            // 指针：平移量取 clip 1245 里 child3(f255) 与 child0(f252) 的位移差，
            // 再按表盘自身的缩放（0.089 / 1024）折成表盘像素 ⇒ 单位与表盘同一套（格）。
            // 指针挂在自己的盘心轴下（进度 0 时轴的角度为 0 = 指向正上方，随进度转一圈）。
            if (_timerStemPivot != null) _timerStemPivot.position = center;
            if (_timerStem != null && _timerStemSprite != null)
            {
                _timerStem.sprite = _timerStemSprite;
                _timerStem.enabled = true;
                var stemNative = _timerStemSprite.bounds.size;
                _timerStem.transform.localScale = new Vector3(
                    _timerStemSprite.rect.width / TimerArtPxPerTile / stemNative.x,
                    _timerStemSprite.rect.height / TimerArtPxPerTile / stemNative.y, 1f);
                _timerStem.transform.localPosition = new Vector3(
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

        /// <summary>
        /// 表盘面绿白扇形的贴图（画布与 f252 同尺寸、同枢轴 / PPU ⇒ 挂在本 GameObject 下与表盘逐像素对齐）。
        /// 首次画表盘时建一次；<see cref="ApplyTimerSweep"/> 按进度重画扇形。
        /// </summary>
        private void EnsureTimerPie()
        {
            if (_timerPie == null || _timerBodySprite == null || _timerPieSprite != null) return;
            var w = Mathf.Max(1, Mathf.RoundToInt(_timerBodySprite.rect.width));
            var h = Mathf.Max(1, Mathf.RoundToInt(_timerBodySprite.rect.height));
            _timerPieTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "DeployTimerPie",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            // 枢轴 / PPU 抄 f252 自己的（`Sprite.Create` 的 pivot 是归一化值）⇒ 叠上去不会错位。
            var pivotNorm = new Vector2(
                _timerBodySprite.pivot.x / Mathf.Max(1f, _timerBodySprite.rect.width),
                _timerBodySprite.pivot.y / Mathf.Max(1f, _timerBodySprite.rect.height));
            _timerPieSprite = Sprite.Create(_timerPieTex, new Rect(0, 0, w, h), pivotNorm,
                _timerBodySprite.pixelsPerUnit);
            _timerPie.sprite = _timerPieSprite;
            _timerPieDrawnP = -1f;
            Game.Logger?.Info(LogTag,
                $"表盘绿白扇形贴图就绪：画布 {w}x{h}px 面圆心 ({TimerFaceCenterPx.x},{TimerFaceCenterPx.y})px " +
                $"半径 {TimerFaceRadiusPx}px（未走=绿 已走=白 起点正上方 顺时针 恰一圈）");
        }

        /// <summary>
        /// 按进度（0..1）重画表盘面的绿白扇形：**自正上方起、顺时针**，已走 = 白、未走 = 绿。
        /// 进度量化到 <see cref="PieSteps"/> 档，档内不重画。
        /// </summary>
        private void DrawTimerPie(float p)
        {
            if (_timerPieTex == null) return;
            var q = Mathf.RoundToInt(Mathf.Clamp01(p) * PieSteps);
            if (q == Mathf.RoundToInt(_timerPieDrawnP * PieSteps) && _timerPieDrawnP >= 0f) return;
            _timerPieDrawnP = q / (float)PieSteps;

            var tex = _timerPieTex;
            var w = tex.width;
            var h = tex.height;
            var pixels = new Color32[w * h];
            // 贴图 y 向上 ⇒ 面圆心 y = 画布高 − 顶左量得的 y。
            var cx = TimerFaceCenterPx.x;
            var cy = h - TimerFaceCenterPx.y;
            var r2 = TimerFaceRadiusPx * TimerFaceRadiusPx;
            var walked = _timerPieDrawnP * 360f;
            var white = (Color32)TimerWalkedColor;
            var green = (Color32)TimerUnwalkedColor;
            for (var y = 0; y < h; y++)
            {
                var dy = y + 0.5f - cy;
                for (var x = 0; x < w; x++)
                {
                    var dx = x + 0.5f - cx;
                    if (dx * dx + dy * dy > r2) continue;
                    // 角度：自正上方（+y）起、顺时针为正 ⇒ atan2(dx, dy)。
                    var deg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    if (deg < 0f) deg += 360f;
                    pixels[y * w + x] = deg <= walked ? white : green;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
        }

        /// <summary>
        /// 按倒计时进度摆角度（一圈 = 整个落位期，**自正上方、顺时针**）：
        /// 指针轴 = −360°·p（正上方起、顺时针，p=0 时落在 clip 的指针位 = 指向正上方）；
        /// 扫过片轴 = 135° − 360°·p（其扇形的角平分线贴着白绿交界，hence 135° 的相位）。
        /// 表盘面的绿白扇形同一进度重画（见 <see cref="DrawTimerPie"/>）。
        /// </summary>
        private void ApplyTimerSweep()
        {
            var p = _deployTotal > 0f ? Mathf.Clamp01(1f - _deployLeft / _deployTotal) : 1f;
            if (_timerStemPivot != null) _timerStemPivot.localRotation = Quaternion.Euler(0f, 0f, -360f * p);
            if (_timerSweepPivot != null) _timerSweepPivot.localRotation = Quaternion.Euler(0f, 0f, 135f - 360f * p);
            DrawTimerPie(p);
        }

        /// <summary>
        /// 开 / 关计时件的三个**兄弟**节点（形态切换与显隐时用；⛔ 不动本 GameObject 自身的渲染器）。
        /// 扫过片轴 / 指针轴不在本 GameObject 下，`SetVisible` 的 `SetActive` 管不到；
        /// 绿白扇形是本 GameObject 的子节点、但只在计时件形态露面。
        /// </summary>
        private void SetTimerPiecesActive(bool active)
        {
            if (_timerSweepPivot != null) _timerSweepPivot.gameObject.SetActive(active);
            if (_timerStemPivot != null) _timerStemPivot.gameObject.SetActive(active);
            if (_timerPie != null) _timerPie.gameObject.SetActive(active);
        }

        /// <summary>拖放落点环颜色变换的 `mul`（0 ⇒ 丢掉黑环的图元本色，只留图元的 alpha 当遮罩）。</summary>
        private const int DropRingMul = 0;

        /// <summary>「可放」颜色变换的 `add` 原值：原版通用蓝环 clip 644 的 `add = 33beff`。</summary>
        private const int DropRingLegalAddR = 0x33;
        private const int DropRingLegalAddG = 0xbe;
        private const int DropRingLegalAddB = 0xff;

        /// <summary>「可放」颜色变换的 `alpha` 原值：原版通用蓝环 clip 644 的 `alpha = d2`。</summary>
        private const int DropRingLegalAlpha = 0xd2;

        /// <summary>「不可放」颜色变换的 `add` 原值：原版通用红环 clip 645 的 `add = ff3300`。</summary>
        private const int DropRingIllegalAddR = 0xff;
        private const int DropRingIllegalAddG = 0x33;
        private const int DropRingIllegalAddB = 0x00;

        /// <summary>「不可放」颜色变换的 `alpha` 原值：原版通用红环 clip 645 的 `alpha = 8f`。</summary>
        private const int DropRingIllegalAlpha = 0x8f;

        /// <summary>
        /// 拖放落点环的颜色变换材质：`out = src × 0 + add`、`out_a = src_a × alpha`
        /// （机制与数值口径见 <see cref="SpriteColorTransformMaterial"/>）。可放 = 青蓝 (51,190,255) / alpha 210，
        /// 不可放 = 橙红 (255,51,0) / alpha 143。
        /// <para>
        /// <b>为什么必须走颜色变换而不是顶点色 tint</b>：原版范围图元是**黑环**
        /// （<see cref="ResPaths.EffectRangeRingFriendly"/> f221 实测非透明像素 RGB 全为 0、alpha 均值 145）⇒
        /// 顶点色只能乘不能加（黑 × 任何色 = 黑），只有加项能把它提成亮色。
        /// </para>
        /// <para>
        /// <b>数值出处</b>：`原版资源/sc/effects_v215.sc` 里**同帧 f221** 的两个通用环 clip ——
        /// 644（青蓝 `33beff d2`）/ 645（橙红 `ff3300 8f`）的 `09` 颜色变换；
        /// 复跑 `python tools/probes/sc-color-transform.py --sc effects --find-shape 221`。
        /// </para>
        /// </summary>
        private Material DropRingMaterial()
        {
            return _lastLegal
                ? SpriteColorTransformMaterial.ForSprite(DropRingMul, DropRingMul, DropRingMul, DropRingLegalAlpha,
                    DropRingLegalAddR, DropRingLegalAddG, DropRingLegalAddB)
                : SpriteColorTransformMaterial.ForSprite(DropRingMul, DropRingMul, DropRingMul, DropRingIllegalAlpha,
                    DropRingIllegalAddR, DropRingIllegalAddG, DropRingIllegalAddB);
        }

        /// <summary>隐藏指示盘（拖放结束 / 抬手）—— 连同落点模型与就位计时件一起收掉。</summary>
        public void Hide()
        {
            _deployLeft = 0f;
            _deployTimerMode = false;
            _dragPreviewSprite = null;
            _dragPreviewOffsets = null;
            SetPreviewModel(null, 1f);
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
        /// 推进当前形态的动画：**落位计时件**的扫过片走一圈（形态由 <see cref="_deployTimerMode"/>
        /// 区分，见 <see cref="ShowDeployRing"/>）。
        /// <para>
        /// 落点范围环是**静止**的（原版 f221 只有 1 帧，没有自转这回事）；
        /// 会转的只有落位计时件的扫过片，而且一圈 = 该卡真实的落位期。
        /// </para>
        /// <para>
        /// 只在可见时推进（`SetVisible(false)` 会 `SetActive(false)`，本方法自然不会被调到）；
        /// 用 `unscaledDeltaTime`：对局暂停 / 时间倍率为 0 时，拖放手势仍然要有反馈。
        /// </para>
        /// </summary>
        private void Update()
        {
            // 倒计时到点自己收掉：落位计时件用 `ShowDeployRing` 给的总时长。
            if (_deployLeft > 0f)
            {
                _deployLeft -= Time.unscaledDeltaTime;
                if (_deployLeft <= 0f)
                {
                    _deployLeft = 0f;
                    // 收掉之前停在**整一圈**的位置（进度=1：白绿扇形全白、指针回正上方）再收。
                    ApplyTimerSweep();
                    SetVisible(false);
                    return;
                }
            }
            if (_deployTimerMode) ApplyTimerSweep();
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
            if (_discSprite != null) Destroy(_discSprite);
            if (_timerPieSprite != null) Destroy(_timerPieSprite);
            if (_timerPieTex != null) Destroy(_timerPieTex);
            // 预览模型 / 扫过片轴 / 指针轴都是**兄弟节点**（见 Build：本 GameObject 带按半径缩放，
            // 挂成子节点会被乘两次）⇒ 它们不随本节点自动销毁，必须在这里一起拆，否则会作为孤儿留在场景里。
            //（绿白扇形是本 GameObject 的子节点，随节点走；只有它的贴图/精灵在上面单独拆。）
            for (var i = 0; i < _previewModels.Count; i++)
            {
                if (_previewModels[i] != null) Destroy(_previewModels[i].gameObject);
            }
            _previewModels.Clear();
            if (_timerSweepPivot != null) Destroy(_timerSweepPivot.gameObject);
            if (_timerStemPivot != null) Destroy(_timerStemPivot.gameObject);
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
