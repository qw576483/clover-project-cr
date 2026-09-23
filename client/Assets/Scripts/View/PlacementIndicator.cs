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
    /// <b>为什么用程序生成的圆盘而不是素材</b>：解包素材里没有"落点指示"图（`ui_out` 914 帧未做逐帧识别，
    /// 且卡面/UI 序号→语义的映射表在本项目里不存在）。用 `Texture2D` 生成一个圆盘是**自制品**，
    /// 不冒充原版素材、也不会因路径猜错而静默不显示（见 `ResPaths` 注释里"别编映射表"的同款理由）。
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

        private SpriteRenderer _renderer;
        private Sprite _discSprite;
        private bool _visible;

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
            // 落点指示必须压在**所有单位与塔之下**（它是一块地板高亮），所以给一个很低的 sortingOrder。
            // 与 UnitView/ArenaView 的层级约定：底图 0 / 塔 50 / 单位 100 / 指示 200（见各自注释）。
            _renderer.sortingOrder = SortingOrder.PlacementIndicator;
            SetVisible(false);
        }

        /// <summary>
        /// 显示指示盘。
        /// </summary>
        /// <param name="tileXY">落点（**格**坐标，不是世界坐标 —— 与 `GameConst.TileToWorld` 同一套口径）。</param>
        /// <param name="radiusTiles">半径（格）。法术用其 AoE 半径；部队用 1 格。</param>
        /// <param name="legal">是否合法（由 <see cref="IsLegalDeploy(DeployInput,float,float,bool)"/> 判定）。</param>
        public void Show(Vector2 tileXY, float radiusTiles, bool legal)
        {
            transform.position = GameConst.TileToWorld(tileXY.x, tileXY.y);
            // 圆盘贴图直径 = 1 世界单位（Sprite 默认 1 unit/pixel 比例下由 Sprite.Create 的 pixelsPerUnit 决定），
            // 所以直接按"直径 = 2 * 半径"缩放即可 —— 不引入额外魔法数。
            var diameter = Mathf.Max(0.5f, radiusTiles * 2f);
            transform.localScale = new Vector3(diameter, diameter, 1f);
            _renderer.color = legal ? LegalColor : IllegalColor;
            SetVisible(true);
        }

        /// <summary>隐藏指示盘（拖放结束 / 抬手）。</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_renderer != null) _renderer.enabled = visible;
            gameObject.SetActive(visible);
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
            // ⚠️ 2026-09-23 修正（CR-F1，审计差异 A）：旧实现只按 y∈[15,17) 判"水"，把两座桥也判成水 ⇒
            //    与 `server/game/core/arena.go:88-90` 的 `IsWater = inRiverBand(y) && !onBridge(x)`
            //    **不同源**。症状：敌方该路公主塔被摧毁、口袋区展开到桥面格时，客户端画红盘而服务端会收。
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
            //   ⚠️ 2026-09-23 修正（CR-F1，审计差异 B）：**两队的边界口径不对称**，与服务端
            //   `arena.go:148-153` 的 `OwnHalf` 逐字一致 ——
            //     BLUE（绝对 y∈[0,15)）  ⇒ 本场 yOwn∈[0,15)   上沿**不含**（河带第一行 y=15.0 是水）
            //     RED （绝对 y∈[17,32)） ⇒ 本场 yOwn∈(0,15]  下沿**含**（河带下沿 y=17.0 是实地）
            //   旧实现两队一刀切用 `<` ⇒ RED 在 y=17.0（yOwn=15.0）被判非法、服务端却收。
            //   ⛔ **不能一刀切改成 `<=`**：桥豁免（③）之后，BLUE 的 y=15.0 **落在桥面上**时不再被水拦掉，
            //      一刀切 `<=` 会把它判成"可放"，而服务端 `OwnHalf(BLUE)` 上沿不含 ⇒ 反而**新增**反向分歧。
            //   （两种形状的逐点比：`.ai-tmp/test/CR-F1-geom-after.log` 的 §4c）
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
