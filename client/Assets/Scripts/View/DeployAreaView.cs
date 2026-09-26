using System.Collections.Generic;
using CloverEngine;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 拖放出牌时的**不可放置区域**显示（原版 `deployArea_*` 那一族图元）。
    ///
    /// <para>
    /// <b>职责</b>：把「这一格的这副牌放不下去」的区域在地面上画出来 ——
    /// 区域内铺一层原版的暗色底（<see cref="FrameFillBase"/>），沿区域边界描原版的红色边条
    /// （<see cref="FrameSideLeft"/> 等）。合法性本身**不在这里判**：区域由
    /// <see cref="PlacementIndicator.IsLegalDeploy"/> 逐格取反得到（唯一判定处，见该类注释）。
    /// </para>
    ///
    /// <para>
    /// <b>图元出处</b>（CR 2.1.5 APK `assets/sc/ui`，导出表见 `策划/原版UI素材名称索引.md:304-316`）：
    /// <list type="bullet">
    /// <item>`deployArea_base`（clip 1241）＝ 1×1 像素 RGBA(23,0,0,23)，原版的区域底色；</item>
    /// <item>`deployArea_side_{top,right,left,bottom}`（clip 1227-1230）＝ 61×16 / 13×49 边条；</item>
    /// <item>`deployArea_corner_{leftTop,leftBottom,rightTop,rightBottom}`（clip 1237-1240）＝ 61×55 / 62×50 角件；</item>
    /// <item>`deployArea_innerCorner_{top,bottom}{Left,Right}`（clip 1232/1233/1235/1236）＝ 14×16 **凹角塞子**
    ///   （原版这 4 个名字只对应 2 帧：`topLeft`/`topRight` → 245、`bottomLeft`/`bottomRight` → 246 ——
    ///   件左右对称，故左右不分开），落位见 <see cref="EmitInnerCorners"/>。</item>
    /// </list>
    /// 落地文件 = `Resources/Sprites/Effects/DeployArea/frame_NNN.png`（按可见包围盒裁切）。
    /// </para>
    ///
    /// <para>
    /// <b>「像素 → 格」的换算出处</b>：图元自身像素尺寸 ÷ 原版竞技场底图自身的横向像素密度
    /// <see cref="ArenaView.GroundPxPerTileX"/>（45.545 px/格，由原版 `training_area_bg` 画布上的
    /// 车道间距 501 px ÷ 11 格标定，见 `ArenaView` 的 `GroundPxPerTileX` 注释）。
    /// 角件 62 px ÷ 45.545 = 1.36 格、边条厚 16 px ÷ 45.545 = 0.35 格 —— 与「边条长 61 px ≈ 1.34 格」
    /// 自洽（同一批图元同一比例），所以两轴共用这一个密度。
    /// </para>
    ///
    /// <para>
    /// <b>为什么画「不可放」而不是「可放」</b>：原版参考图 `策划/参考图/22_对局_720x1600.jpg`
    /// （唯一一张拖卡中的截图）里被描边 + 着色的正是**敌方半场**（部队放不下去的那半边）；
    /// 且该族的底色像素本身是 alpha 23 的暗色 —— 它是「压暗」而不是「高亮」。
    /// 落点的合法性提示仍由 <see cref="PlacementIndicator"/> 的环承担（可放/不可放随指针变色）。
    /// </para>
    /// </summary>
    public sealed class DeployAreaView : MonoBehaviour
    {
        /// <summary>日志标签（`Game.Logger` 用；⛔ 不写裸 `Debug` 日志）。</summary>
        public const string LogTag = "DeployArea";

        // ───────────────────────── 原版帧号（`ui` 的 `12` 记录序号，1:1 对应 frame_NNN.png） ─────────────────────────

        /// <summary>`deployArea_base`：区域底色（1×1，RGBA(23,0,0,23)）。</summary>
        private const int FrameFillBase = 251;

        /// <summary>`deployArea_side_top`：上边条（61×16）。</summary>
        private const int FrameSideTop = 241;

        /// <summary>`deployArea_side_right`：右边条（13×49）。</summary>
        private const int FrameSideRight = 242;

        /// <summary>`deployArea_side_left`：左边条（13×49）。</summary>
        private const int FrameSideLeft = 243;

        /// <summary>`deployArea_side_bottom`：下边条（61×16）。</summary>
        private const int FrameSideBottom = 244;

        /// <summary>`deployArea_corner_rightTop`：右外上角（62×55）。</summary>
        private const int FrameCornerRightTop = 247;

        /// <summary>`deployArea_corner_rightBottom`：右外下角（62×50）。</summary>
        private const int FrameCornerRightBottom = 248;

        /// <summary>`deployArea_corner_leftTop`：左外上角（61×55）。</summary>
        private const int FrameCornerLeftTop = 249;

        /// <summary>`deployArea_corner_leftBottom`：左外下角（61×50）。</summary>
        private const int FrameCornerLeftBottom = 250;

        /// <summary>
        /// `deployArea_innerCorner_top{Left,Right}`：**凹角塞子**（14×16，原版这两个名字是同一帧 —— 件左右对称，
        /// 所以只要「上/下」两种）。
        /// </summary>
        private const int FrameInnerCornerTop = 245;

        /// <summary>`deployArea_innerCorner_bottom{Left,Right}`：凹角塞子（14×16，见 <see cref="FrameInnerCornerTop"/>）。</summary>
        private const int FrameInnerCornerBottom = 246;

        /// <summary>原版竞技场自身的横向像素密度（px/格）—— 见类注释「像素 → 格」。</summary>
        private const float ArtPxPerTile = ArenaView.GroundPxPerTileX;

        /// <summary>左右边条厚度（格）= 图元像素厚 13 ÷ <see cref="ArtPxPerTile"/>。</summary>
        private static readonly float EdgeThinTiles = 13f / ArtPxPerTile;

        /// <summary>上下边条厚度（格）= 图元像素厚 16 ÷ <see cref="ArtPxPerTile"/>。</summary>
        private static readonly float EdgeThickTiles = 16f / ArtPxPerTile;

        /// <summary>底色层相对指示环的排序；环在 <see cref="ArenaLayers"/>.Indicator（单位在它之上）。</summary>
        private const int FillOrderOffset = -4;

        /// <summary>边条 / 角件层相对指示环的排序（压住底色）。</summary>
        private const int EdgeOrderOffset = -3;

        private Transform _quadRoot;
        private readonly List<SpriteRenderer> _quads = new List<SpriteRenderer>();
        private int _used;

        private readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();
        private bool _artWarned;
        private bool _visible;

        /// <summary>当前已画出的区域键（队伍 + 是否法术 + 4 座塔的存活位）；相同则只切显隐。</summary>
        private long _builtKey = long.MinValue;

        /// <summary>最近一次请求显示的参数（图元异步到货后据此补画）。</summary>
        private int _team;
        private bool _isSpell;
        private bool _enemyLeftAlive, _enemyRightAlive, _ownLeftAlive, _ownRightAlive;

        /// <summary>这一族要用到的帧号（底色 + 4 边 + 4 外角 + 2 凹角塞子）。</summary>
        private static readonly int[] Frames =
        {
            FrameFillBase, FrameSideTop, FrameSideRight, FrameSideLeft, FrameSideBottom,
            FrameCornerRightTop, FrameCornerRightBottom, FrameCornerLeftTop, FrameCornerLeftBottom,
            FrameInnerCornerTop, FrameInnerCornerBottom
        };

        /// <summary>在 <paramref name="parent"/> 下建一个区域显示层（世界空间）。</summary>
        public static DeployAreaView Create(Transform parent)
        {
            var go = new GameObject(LogTag);
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<DeployAreaView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            var root = new GameObject("DeployAreaQuads");
            root.transform.SetParent(transform, false);
            _quadRoot = root.transform;
            LoadSprites();
            SetVisible(false);
        }

        /// <summary>异步取这一族原版图元（到货前不画；到货后若正处于拖动中则补画）。</summary>
        private void LoadSprites()
        {
            if (Game.Res == null)
            {
                WarnOnce("Game.Res 为空（漏了 CloverRes.Init？）⇒ 部署区显示画不出来");
                return;
            }
            for (var i = 0; i < Frames.Length; i++)
            {
                var frame = Frames[i];
                var path = ResPaths.EffectFrame(ResPaths.EffectDeployArea, frame);
                Game.Res.LoadAsset<Sprite>(path, sprite =>
                {
                    if (sprite == null)
                    {
                        WarnOnce($"原版部署区图元加载不到：{path}");
                        return;
                    }
                    _sprites[frame] = sprite;
                    if (_sprites.Count == Frames.Length && _visible) Rebuild(forced: true);
                });
            }
        }

        private void WarnOnce(string message)
        {
            if (_artWarned) return;
            _artWarned = true;
            Game.Logger?.Warn(LogTag, message + "（只报一次；拖放仍然按合法性发请求，只是没有区域显示）");
        }

        /// <summary>
        /// 显示「这副牌放不下去」的区域。参数就是 <see cref="PlacementIndicator.IsLegalDeploy"/> 的那套输入。
        /// <para>区域在整次拖动里是**常量** ⇒ 只有输入变了才重建；指针每动一次只切显隐。</para>
        /// </summary>
        public void Show(int myTeam, bool isSpell, bool enemyLeftAlive, bool enemyRightAlive,
                         bool ownLeftAlive, bool ownRightAlive)
        {
            _team = myTeam;
            _isSpell = isSpell;
            _enemyLeftAlive = enemyLeftAlive;
            _enemyRightAlive = enemyRightAlive;
            _ownLeftAlive = ownLeftAlive;
            _ownRightAlive = ownRightAlive;
            Rebuild(forced: false);
            SetVisible(true);
        }

        /// <summary>收掉区域显示（抬手 / 取消拖放）。</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_quadRoot != null) _quadRoot.gameObject.SetActive(visible);
        }

        private long Key()
        {
            long k = _team & 1;
            if (_isSpell) k |= 1L << 1;
            if (_enemyLeftAlive) k |= 1L << 2;
            if (_enemyRightAlive) k |= 1L << 3;
            if (_ownLeftAlive) k |= 1L << 4;
            if (_ownRightAlive) k |= 1L << 5;
            return k;
        }

        private void Rebuild(bool forced)
        {
            if (_sprites.Count < Frames.Length) return;   // 图元还没到齐：等回调里补画
            var key = Key();
            if (!forced && key == _builtKey && _used > 0) return;
            _builtKey = key;

            var input = new PlacementIndicator.DeployInput
            {
                MyTeam = _team,
                EnemyLeftPrincessAlive = _enemyLeftAlive,
                EnemyRightPrincessAlive = _enemyRightAlive,
                OwnLeftPrincessAlive = _ownLeftAlive,
                OwnRightPrincessAlive = _ownRightAlive
            };

            var w = Mathf.RoundToInt(GameConst.ArenaTilesW);
            var h = Mathf.RoundToInt(GameConst.ArenaTilesH);
            var blocked = new bool[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    // 格中心取格坐标；⛔ 判定只走 IsLegalDeploy，不在这里重写任何规则
                    blocked[y * w + x] = !PlacementIndicator.IsLegalDeploy(input, x + 0.5f, y + 0.5f, _isSpell);
                }
            }

            _used = 0;
            for (var y = 0; y < h; y++)
            {
                var x = 0;
                while (x < w)
                {
                    if (!blocked[y * w + x]) { x++; continue; }
                    var x0 = x;
                    while (x < w && blocked[y * w + x]) x++;
                    EmitRun(blocked, w, h, x0, x, y);
                }
            }
            EmitInnerCorners(blocked, w, h);
            for (var i = _used; i < _quads.Count; i++) _quads[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// 凹角塞子：区域把**一个格点周围的 3 格**占住时，那个格点是凹角（缺的那格 = 凹口方向）——
        /// 用原版 `deployArea_innerCorner_*` 把拐角补成连续描边。
        /// <para>
        /// <b>尺寸与落位的口径</b>：与 4 个外角件**同一条**（件自身像素 ÷ <see cref="ArtPxPerTile"/>，
        /// 叠在格点上）；差别只是凹角件**以格点为中心**（件左右对称 ⇒ 原版 topLeft/topRight 是同一帧，
        /// 左右不需要分开），外角件则按"指向区域内"的符号偏移半件。
        /// </para>
        /// <para>⛔ 不改判定：几何/合法性仍只由 <see cref="PlacementIndicator.IsLegalDeploy"/> 决定，这里只读它。</para>
        /// </summary>
        private void EmitInnerCorners(bool[] blocked, int w, int h)
        {
            // ⛔ 只扫**四格都在场地内**的格点：贴边的格点会因"场地外"被算成可放而造出假凹角
            //   （场地自身的边界已由行差分的左右边条负责；实测 18×32 全扫时右沿每行都误报一格）。
            for (var y = 1; y <= h - 1; y++)
            {
                for (var x = 1; x <= w - 1; x++)
                {
                    // 格点 (x,y) 周围 4 格：NW(-1,0) NE(0,0) SW(-1,-1) SE(0,-1)。
                    var nw = BlockedAt(blocked, w, h, x - 1, y);
                    var ne = BlockedAt(blocked, w, h, x, y);
                    var sw = BlockedAt(blocked, w, h, x - 1, y - 1);
                    var se = BlockedAt(blocked, w, h, x, y - 1);
                    // ⛔ 必须是**恰好 3 格**：4 格全占 = 区域内部（不是拐角）——写成"≥3"会让整片区域内部每格都冒一个塞子。
                    var taken = (nw ? 1 : 0) + (ne ? 1 : 0) + (sw ? 1 : 0) + (se ? 1 : 0);
                    if (taken != 3) continue;
                    // 唯一空的那格在北侧 ⇒ 用 top 帧；在南侧 ⇒ 用 bottom 帧（原版只有这两种）。
                    var notchNorth = !nw || !ne;
                    PutInnerCorner(notchNorth ? FrameInnerCornerTop : FrameInnerCornerBottom, x, y);
                }
            }
        }

        /// <summary>格是否**不可放**；越界（场地外）返回 false（= 可放，与行差分同口径）。</summary>
        private static bool BlockedAt(bool[] blocked, int w, int h, int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return false;
            return blocked[y * w + x];
        }

        /// <summary>把凹角塞子以格点为中心铺上去（尺寸取件自身像素 ÷ <see cref="ArtPxPerTile"/>）。</summary>
        private void PutInnerCorner(int frame, float cornerTileX, float cornerTileY)
        {
            var sprite = _sprites[frame];
            if (sprite == null) return;
            var r = TakeQuad(frame, EdgeOrderOffset);
            if (r == null) return;
            var native = r.sprite.bounds.size;
            var tilesW = sprite.rect.width / ArtPxPerTile;
            var tilesH = sprite.rect.height / ArtPxPerTile;
            r.transform.position = GameConst.TileToWorld(cornerTileX, cornerTileY);
            r.transform.localScale = new Vector3(tilesW / native.x, tilesH / native.y, 1f);
        }

        /// <summary>
        /// 画一行里的一段连续不可放置格（[x0,x1) at y）：底色 + **只在本格真暴露的那一侧**画边条 + 暴露角。
        /// <para>
        /// <b>为什么逐格判暴露、而不是"邻行只要有一段空就画整条"</b>：一次拖放的区域常出现**1 格宽**的凸出
        /// （我方公主塔周围 1 格半径的菱形、国王塔 3×3 的四角）—— 整段式判法在这些窄处会同时画上两侧的角件，
        /// 而角件的臂长（1.34 格）远大于 1 格 ⇒ 两组臂互相穿过，画面上读成一个「X」而不是一块区域。
        /// 逐格判 + 角件臂长按区域自身范围夹取（见 <see cref="PutCorner"/>）后，边条与角件只在真正的边界上闭合。
        /// </para>
        /// </summary>
        private void EmitRun(bool[] blocked, int w, int h, int x0, int x1, int y)
        {
            // 底色：整段铺一层（原版的底色是 1×1 纯色像素，拉伸 = 平铺）
            PutEdge(FrameFillBase, (x0 + x1) * 0.5f, y + 0.5f, (float)(x1 - x0), 1f, FillOrderOffset);

            // 上下边条：只铺在「本格的上方/下方为空」的格上，连续暴露段合成一条（逐格拼会有接缝）
            EmitSideRun(blocked, w, h, x0, x1, y, true);
            EmitSideRun(blocked, w, h, x0, x1, y, false);

            for (var x = x0; x < x1; x++)
            {
                var cy = y + 0.5f;
                var leftFree = !BlockedAt(blocked, w, h, x - 1, y);
                var rightFree = !BlockedAt(blocked, w, h, x + 1, y);
                if (leftFree)
                    PutEdge(FrameSideLeft, x + EdgeThinTiles * 0.5f, cy, EdgeThinTiles, 1f, EdgeOrderOffset);
                if (rightFree)
                    PutEdge(FrameSideRight, x + 1f - EdgeThinTiles * 0.5f, cy, EdgeThinTiles, 1f, EdgeOrderOffset);

                // 凸角：一格同时暴露「上+左」这类两条边 ⇒ 用原版角件盖住缺口。
                //   入参 (sx,sy) = 从角点指向**区域内**的方向：左边缘 ⇒ +x，右边缘 ⇒ -x，上边缘 ⇒ -y，下边缘 ⇒ +y。
                var upFree = !BlockedAt(blocked, w, h, x, y + 1);
                var downFree = !BlockedAt(blocked, w, h, x, y - 1);
                if (upFree && leftFree) PutCorner(blocked, w, h, FrameCornerLeftTop, x, y + 1f, 1f, -1f);
                if (upFree && rightFree) PutCorner(blocked, w, h, FrameCornerRightTop, x + 1f, y + 1f, -1f, -1f);
                if (downFree && leftFree) PutCorner(blocked, w, h, FrameCornerLeftBottom, x, y, 1f, 1f);
                if (downFree && rightFree) PutCorner(blocked, w, h, FrameCornerRightBottom, x + 1f, y, -1f, 1f);
            }
        }

        /// <summary>
        /// 把 [x0,x1) 行内**上方（<paramref name="up"/>）或下方为空**的连续格合成一条边条铺出去
        /// （段长 = 真实暴露长度；越界算空，与 <see cref="BlockedAt"/> 同口径）。
        /// </summary>
        private void EmitSideRun(bool[] blocked, int w, int h, int x0, int x1, int y, bool up)
        {
            var dy = up ? 1 : -1;
            var frame = up ? FrameSideTop : FrameSideBottom;
            var cy = up ? y + 1f - EdgeThickTiles * 0.5f : y + EdgeThickTiles * 0.5f;
            var start = -1;
            for (var x = x0; x <= x1; x++)
            {
                var free = x < x1 && !BlockedAt(blocked, w, h, x, y + dy);
                if (free)
                {
                    if (start < 0) start = x;
                }
                else if (start >= 0)
                {
                    PutEdge(frame, (start + x) * 0.5f, cy, (float)(x - start), EdgeThickTiles, EdgeOrderOffset);
                    start = -1;
                }
            }
        }

        /// <summary>格 (cellX, cellY) 起、沿 (stepX, stepY) 方向**连续不可放**的格数（用于夹角件臂长）。</summary>
        private static int RunLength(bool[] blocked, int w, int h, int cellX, int cellY, int stepX, int stepY)
        {
            var n = 0;
            while (BlockedAt(blocked, w, h, cellX + stepX * n, cellY + stepY * n)) n++;
            return n;
        }

        /// <summary>把边条按「格」尺寸铺到某个格坐标上（拉伸到目标尺寸，⛔ 不写死像素）。</summary>
        private void PutEdge(int frame, float tileX, float tileY, float tilesW, float tilesH, int orderOffset)
        {
            var r = TakeQuad(frame, orderOffset);
            if (r == null) return;
            var native = r.sprite.bounds.size;
            r.transform.position = GameConst.TileToWorld(tileX, tileY);
            r.transform.localScale = new Vector3(tilesW / native.x, tilesH / native.y, 1f);
        }

        /// <summary>
        /// 角件：贴到区域的外角上（<paramref name="sx"/>/<paramref name="sy"/> = 指向区域内的符号）。
        /// <para>
        /// <b>臂长 = min(件自身像素尺寸 ÷ <see cref="ArtPxPerTile"/>, 区域在该方向上的连续长度)</b>：
        /// 角件的两条臂是伸进区域内部的，区域只有 1 格宽的凸出处（我方公主塔的菱形角）如果按件自身像素
        /// （1.34×1.21 格）画，臂会越出区域、和对面的角件穿在一起。
        /// </para>
        /// </summary>
        private void PutCorner(bool[] blocked, int w, int h, int frame, float cornerTileX, float cornerTileY,
                              float sx, float sy)
        {
            var sprite = _sprites[frame];
            if (sprite == null) return;
            var r = TakeQuad(frame, EdgeOrderOffset);
            if (r == null) return;
            var native = r.sprite.bounds.size;
            // 角件**内侧**那一格（臂所覆盖的第一格）：角点 (cornerTileX,cornerTileY) 是格点，
            // 内侧格 = 格点往区域内的那一格。⛔ 用整数运算定索引，不走四舍五入（x+0.5 会被舍到偶数侧）。
            var innerX = sx > 0f ? (int)cornerTileX : (int)cornerTileX - 1;
            var innerY = sy > 0f ? (int)cornerTileY : (int)cornerTileY - 1;
            var stepX = sx > 0f ? 1 : -1;
            var stepY = sy > 0f ? 1 : -1;
            // 尺寸取件自己的**像素**尺寸 ÷ 竞技场像素密度（`sprite.rect` 的单位是像素，
            // ⛔ 不用 `bounds`：它的单位是「世界单位」= 像素 ÷ 导入的 PPU）。
            var tilesW = Mathf.Min(sprite.rect.width / ArtPxPerTile, RunLength(blocked, w, h, innerX, innerY, stepX, 0));
            var tilesH = Mathf.Min(sprite.rect.height / ArtPxPerTile, RunLength(blocked, w, h, innerX, innerY, 0, stepY));
            r.transform.position = GameConst.TileToWorld(cornerTileX + sx * tilesW * 0.5f, cornerTileY + sy * tilesH * 0.5f);
            // ⛔ 不按 sx/sy 做负缩放：每一侧的角件都是**独立帧**（247-250 各一张），镜像会把它画反。
            r.transform.localScale = new Vector3(tilesW / native.x, tilesH / native.y, 1f);
        }

        /// <summary>取一个空闲的 quad，把它设成 <paramref name="frame"/> 那张图元并指定排序。</summary>
        private SpriteRenderer TakeQuad(int frame, int orderOffset)
        {
            Sprite sprite;
            if (!_sprites.TryGetValue(frame, out sprite) || sprite == null) return null;

            SpriteRenderer r;
            if (_used < _quads.Count)
            {
                r = _quads[_used];
            }
            else
            {
                var go = new GameObject("DeployAreaQuad");
                go.transform.SetParent(_quadRoot, false);
                r = go.AddComponent<SpriteRenderer>();
                _quads.Add(r);
            }
            _used++;

            r.sprite = sprite;
            r.sortingOrder = ArenaLayers.Instance.Indicator + orderOffset;
            r.enabled = true;
            r.gameObject.SetActive(true);
            return r;
        }

        private void OnDestroy()
        {
            _sprites.Clear();
            _quads.Clear();
        }
    }
}
