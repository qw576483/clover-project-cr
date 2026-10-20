using System.Collections.Generic;
using System.Text;
using CloverEngine;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 竞技场的装饰 / 光线层。**所有像素都来自原版 `arena_training` 图集**（`ResPaths.ArenaRoot` 的 23 帧，
    /// 逐帧导出名见 `策划/单位动画分组表.md` 的 `arena_training` 小节），本类只做三件事：
    /// 裁出件的 alpha 包围盒、按格落位、给层序。⛔ 不画任何新像素。
    ///
    /// <para>
    /// <b>一、装饰件（树 / 灌木 / 石块）</b>：每件取 <see cref="DecorFrame"/> 里的一帧，尺寸 = 件自身像素 ÷
    /// <see cref="ArenaView.GroundPxPerTileX"/>（与地面美术同一个"像素→格"口径），底边贴落位格 y。
    /// 落位坐标写死在 <see cref="DecorTileX"/> / <see cref="DecorTileY"/>（**固定表 = 固定种子**，每局一样），
    /// 只落在两条车道之外、六塔占位之外的草地侧带；RED 半场 = 同一张表的 <c>y → 32 − y</c> 镜像。
    /// 层序 = <see cref="SortingLayers.Decoration"/>（单位 / 落点环 / 血条都在它之上）。
    /// </para>
    ///
    /// <para>
    /// <b>二、接地阴影</b>：塔脚一枚软椭圆（<see cref="ShadowAlpha"/>，宽 = 塔体白件宽 ×
    /// <see cref="ShadowWidthScale"/>），桥两端各一条暗部。椭圆是运行时生成的**纯 alpha 遮罩**
    /// （不是美术件）：颜色为黑、透明度按径向衰减。层序 = 塔层 − 2（压在塔体与废墟之下、装饰之上），
    /// 因此阴影永远在所有单位之下。
    /// </para>
    ///
    /// <para>
    /// <b>三、全屏暗角</b>：一张四边渐变压暗（中心透明、边缘 <see cref="VignetteEdgeAlpha"/>），
    /// 挂在主相机下、铺满相机视野，层序 = <see cref="SortingLayers.Overlay"/> − 100 —— 高于单位与场地、
    /// 低于血条；HUD 是独立的 Overlay 画布，不受影响。
    /// </para>
    /// </summary>
    public sealed class ArenaDecor : MonoBehaviour
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "ArenaDecor";

        // ───────────────────────── 装饰件表（像素量自 1090×1677 画布）─────────────────────────

        /// <summary>
        /// 每件的帧号。语义（`策划/单位动画分组表.md` 的 `arena_training` export 表）：
        /// 2 = `bush01`、0 = `training_deco2`（灰石块）、7 = `tree02`、8 = `tree01`、13 = `cliff04`（石板堆）、3 = `bush02`。
        /// </summary>
        private static readonly int[] DecorFrame = { 2, 0, 7, 8, 13, 3 };

        /// <summary>各件 alpha 包围盒（画布 px，y 向下）：左沿。</summary>
        private static readonly float[] DecorPxLeft = { 467f, 491f, 475f, 456f, 460f, 473f };

        /// <summary>各件 alpha 包围盒：上沿。</summary>
        private static readonly float[] DecorPyTop = { 1056f, 1067f, 1032f, 1004f, 1065f, 1056f };

        /// <summary>各件 alpha 包围盒：右沿。</summary>
        private static readonly float[] DecorPxRight = { 558f, 534f, 547f, 564f, 564f, 552f };

        /// <summary>各件 alpha 包围盒：下沿。</summary>
        private static readonly float[] DecorPyBottom = { 1151f, 1140f, 1136f, 1134f, 1176f, 1146f };

        /// <summary>各件落位中心 x（格；BLUE 半场）。</summary>
        private static readonly float[] DecorTileX = { 1.15f, 1.10f, 1.15f, 16.75f, 16.80f, 16.90f };

        /// <summary>各件落位**底边** y（格；BLUE 半场 = 己方半场，RED 侧镜像到 32 − y）。</summary>
        private static readonly float[] DecorTileY = { 1.20f, 5.00f, 9.20f, 2.60f, 8.40f, 12.40f };

        // ───────────────────────── 塔脚阴影（尺寸/位置取自塔体渲染器的世界包围盒）─────────────────────────

        /// <summary>阴影宽相对塔体渲染宽的比例。</summary>
        private const float ShadowWidthScale = 1.1f;

        /// <summary>阴影椭圆的纵横比（俯视下压扁）。</summary>
        private const float ShadowAspect = 0.34f;

        /// <summary>
        /// 阴影中心相对塔体**底边**向前（屏幕下方）偏移量 = 阴影高度的这个比例。
        /// 塔体自己的底座会把阴影的后半盖住 ⇒ 只留后半等于看不见；向前让出 15% 高度，
        /// 约 2/3 的椭圆落在塔体之前的地面上。
        /// </summary>
        private const float ShadowForwardRatio = 0.15f;

        /// <summary>塔脚阴影的黑色透明度。</summary>
        private const float ShadowAlpha = 0.28f;

        /// <summary>桥两端暗部的透明度。</summary>
        private const float BridgeShadowAlpha = 0.22f;

        /// <summary>暗角**边缘**的黑色透明度（中心为 0）。</summary>
        private const float VignetteEdgeAlpha = 0.13f;

        /// <summary>暗角开始变暗的归一化半径（切比雪夫距离，0 = 中心、1 = 屏幕边）。</summary>
        private const float VignetteInnerRadius = 0.55f;

        /// <summary>暗角铺满视野时的额外外扩（格），保证四角也被盖住。</summary>
        private const float VignetteOverdraw = 0.5f;

        private readonly List<Object> _owned = new List<Object>();
        private Transform _root;
        private Transform _vignette;
        private Camera _cam;
        private int _vignettePw;
        private int _vignettePh;
        private string _report = "-";

        /// <summary>本层台账（装饰件数 / 坐标 / 阴影读数 / 暗角参数）；建成后一次性写日志，也供外部读数。</summary>
        public string Report => _report;

        /// <summary>
        /// 建出装饰件 + 塔脚阴影 + 桥端暗部 + 全屏暗角。
        /// </summary>
        /// <param name="frames">竞技场整目录的帧（按帧号取件，⛔ 不按数组下标）。</param>
        /// <param name="towerRoot">已建好的塔根节点（取每座塔 `Body` 层的世界包围盒作阴影尺寸 / 落位）。</param>
        /// <param name="decorOrder">装饰件层序（= <see cref="SortingLayers.Decoration"/>）。</param>
        /// <param name="bridgeOrder">桥面层序（桥端暗部画在它之上）。</param>
        /// <param name="shadowOrder">接地阴影层序（塔层 − 2）。</param>
        public void Build(Sprite[] frames, Transform towerRoot, int decorOrder, int bridgeOrder, int shadowOrder)
        {
            _root = transform;
            var sb = new StringBuilder();

            // ① 装饰件：BLUE 半场按表落位，RED 半场 y → 32 − y（塔位同一镜像口径）。
            var placed = 0;
            var decorSpots = new System.Collections.Generic.List<(float x, float y, float w, string name)>();
            for (var team = 0; team < 2; team++)
            {
                for (var i = 0; i < DecorFrame.Length; i++)
                {
                    var sprite = FindFrame(frames, DecorFrame[i]);
                    if (sprite == null || sprite.texture == null)
                    {
                        Game.Logger?.Warn(LogTag,
                            $"装饰件取不到帧 {DecorFrame[i]}（第 {i} 件 / team={team}）⇒ 该件缺席，其余件照常");
                        continue;
                    }

                    var wTiles = (DecorPxRight[i] - DecorPxLeft[i]) / ArenaView.GroundPxPerTileX;
                    var hTiles = (DecorPyBottom[i] - DecorPyTop[i]) / ArenaView.GroundPxPerTileX;
                    var x = DecorTileX[i];
                    var yLow = team == 0 ? DecorTileY[i] : GameConst.ArenaTilesH - DecorTileY[i];
                    var name = $"Decor{DecorFrame[i]:D3}_T{team}_{i}";
                    Place(sprite, name, DecorPxLeft[i], DecorPyTop[i], DecorPxRight[i], DecorPyBottom[i],
                        x - wTiles * 0.5f, x + wTiles * 0.5f, yLow, yLow + hTiles, decorOrder, Color.white);
                    placed++;
                    decorSpots.Add((x, yLow, wTiles, name));
                    sb.Append(name).Append('(').Append(x.ToString("F2")).Append(',')
                        .Append(yLow.ToString("F2")).Append(") ");
                }
            }
            sb.Append("| decor=").Append(placed);

            // ② 塔脚阴影：逐座塔取它自己 `Body` 层的**世界包围盒**（宽 / 底边），
            //    阴影宽 = 塔体宽 × 1.1、底边贴在塔体底边之下 —— ⛔ 不用画布常量反推（子精灵的 pivot 在包围盒中心）。
            var shadow = MakeEllipseSprite("ArenaShadow", 128, 64);
            if (shadow == null)
            {
                Game.Logger?.Warn(LogTag, "塔脚阴影的 alpha 遮罩生成失败 ⇒ 不打接地阴影");
            }
            else
            {
                var towers = 0;
                if (towerRoot == null)
                {
                    Game.Logger?.Warn(LogTag, "塔根节点为空 ⇒ 塔脚阴影无处取塔体包围盒，本场不打塔脚阴影");
                }
                else
                {
                    for (var i = 0; i < towerRoot.childCount; i++)
                    {
                        var tower = towerRoot.GetChild(i);
                        var body = tower.Find("Body");
                        var sr = body == null ? null : body.GetComponent<SpriteRenderer>();
                        if (sr == null || sr.sprite == null)
                        {
                            Game.Logger?.Warn(LogTag,
                                $"塔体层 Body 取不到（{tower.name} 第 {i} 座）⇒ 该塔没有塔脚阴影，其余塔照常");
                            continue;
                        }

                        var b = sr.bounds;
                        var w = b.size.x * ShadowWidthScale;
                        var h = w * ShadowAspect;
                        PlaceShadowWorld(shadow, $"Shadow_{i}_{tower.name}", b.center.x,
                            b.min.y - h * ShadowForwardRatio, w, h, shadowOrder, ShadowAlpha);
                        towers++;
                        if (towers <= 2)
                            sb.Append(" shadow").Append(i).Append('=')
                                .Append(w.ToString("F2")).Append('x').Append(h.ToString("F2"));
                    }
                }
                // ②b 装饰件脚下的软阴影（otherwise 树/灌木像贴纸浮在草地上）。
                var decorShadows = 0;
                foreach (var d in decorSpots)
                {
                    PlaceShadowWorld(shadow, "Shadow_" + d.name, d.x, d.y + 0.06f,
                        d.w * 0.90f, d.w * 0.26f, shadowOrder, 0.20f);
                    decorShadows++;
                }
                sb.Append(" | decorShadow=").Append(decorShadows).Append(" alpha=0.20");

                sb.Append(" | shadow=").Append(towers).Append(" order=").Append(shadowOrder)
                    .Append(" alpha=").Append(ShadowAlpha.ToString("F2")).Append(" scale=").Append(ShadowWidthScale.ToString("F2"));

                // ③ 桥两端暗部：桥板两端与地面的接触暗面（画在桥面之上）。
                var xs = new[] { GameConst.BridgeCxATile, GameConst.BridgeCxBTile };
                for (var i = 0; i < xs.Length; i++)
                {
                    PlaceShadow(shadow, $"BridgeShadowNear_{i}", xs[i], ArenaView.BridgePlankTileBottom + 0.18f,
                        2.0f, 0.55f, bridgeOrder + 1, BridgeShadowAlpha);
                    PlaceShadow(shadow, $"BridgeShadowFar_{i}", xs[i], ArenaView.BridgePlankTileTop - 0.18f,
                        2.0f, 0.55f, bridgeOrder + 1, BridgeShadowAlpha);
                }
                sb.Append(" | bridgeShadow=4 order=").Append(bridgeOrder + 1)
                    .Append(" alpha=").Append(BridgeShadowAlpha.ToString("F2"));
            }

            // ④ 全屏暗角：挂在主相机下，逐帧按相机视野定尺寸（见 LateUpdate）。
            var vignetteSprite = MakeVignetteSprite();
            if (vignetteSprite == null)
            {
                Game.Logger?.Warn(LogTag, "暗角贴图生成失败 ⇒ 本场不做边缘压暗");
            }
            else
            {
                var go = new GameObject("Vignette");
                go.transform.SetParent(transform, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = vignetteSprite;
                r.color = Color.white;
                r.sortingOrder = ArenaLayers.Instance.Overlay - 100;
                _vignette = go.transform;
                sb.Append(" | vignette order=").Append(r.sortingOrder)
                    .Append(" edgeAlpha=").Append(VignetteEdgeAlpha.ToString("F2"));
            }

            _report = sb.ToString();
            Game.Logger?.Info(LogTag, "竞技场装饰层建成：" + _report);
        }

        private void LateUpdate()
        {
            UpdateVignette();
        }

        /// <summary>
        /// 把暗角铺满当前相机视野：尺寸随相机宽高比 / 窗口变化重算，位置随相机平移。
        /// </summary>
        private void UpdateVignette()
        {
            if (_vignette == null) return;
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            var pw = _cam.pixelWidth;
            var ph = _cam.pixelHeight;
            if (pw <= 0 || ph <= 0) return;
            if (pw == _vignettePw && ph == _vignettePh && _vignette.parent == _cam.transform) return;
            _vignettePw = pw;
            _vignettePh = ph;

            _vignette.SetParent(_cam.transform, false);
            _vignette.localPosition = new Vector3(0f, 0f, 5f);
            _vignette.localRotation = Quaternion.identity;

            // 视野尺寸由相机自己的屏幕↔世界换算给出（与 ArenaView.SetupCamera 的非等比投影一致）。
            var bl = _cam.ScreenToWorldPoint(new Vector3(0f, 0f, 5f));
            var tr = _cam.ScreenToWorldPoint(new Vector3(pw, ph, 5f));
            var viewW = Mathf.Abs(tr.x - bl.x) + VignetteOverdraw;
            var viewH = Mathf.Abs(tr.y - bl.y) + VignetteOverdraw;

            var r = _vignette.GetComponent<SpriteRenderer>();
            var native = r != null && r.sprite != null ? r.sprite.bounds.size : new Vector3(1f, 1f, 1f);
            if (native.x < 0.01f || native.y < 0.01f) return;
            _vignette.localScale = new Vector3(viewW / native.x, viewH / native.y, 1f);
        }

        /// <summary>桥端暗部：一枚软椭圆，中心落在格 (<paramref name="tileX"/>, <paramref name="tileY"/>)。</summary>
        private void PlaceShadow(Sprite sprite, string name, float tileX, float tileY,
            float widthTiles, float heightTiles, int order, float alpha)
        {
            PlaceShadowWorld(sprite, name, GameConst.TileToWorld(tileX, tileY).x,
                GameConst.TileToWorld(tileX, tileY).y, widthTiles, heightTiles, order, alpha);
        }

        /// <summary>按世界坐标摆一枚软椭圆阴影（塔脚阴影走这条路径：尺寸直接来自塔体包围盒）。</summary>
        private void PlaceShadowWorld(Sprite sprite, string name, float worldX, float worldY,
            float widthTiles, float heightTiles, int order, float alpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = new Color(0f, 0f, 0f, alpha);
            r.sortingOrder = order;

            var native = sprite.bounds.size;
            go.transform.localScale = new Vector3(widthTiles / native.x, heightTiles / native.y, 1f);
            go.transform.position = new Vector3(worldX, worldY, 0f);
        }

        /// <summary>
        /// 把 <paramref name="src"/> 的画布矩形 <c>px × py</c>（y 向下）裁成 Sprite 并按格矩形落位。
        /// 缩放口径与 <see cref="ArenaView"/> 的裁条一致：件自身像素 ÷ <see cref="ArenaView.GroundPxPerTileX"/> = 格。
        /// </summary>
        private void Place(Sprite src, string name, float pxLeft, float pyTop, float pxRight, float pyBottom,
            float tileXLeft, float tileXRight, float tileYLow, float tileYHigh, int order, Color tint)
        {
            var texH = src.texture.height;
            var texW = src.texture.width;
            var ppu = src.pixelsPerUnit > 0.01f ? src.pixelsPerUnit : SpriteBank.FallbackPixelsPerUnit;
            var cropL = Mathf.Clamp(pxLeft, 0f, texW);
            var cropR = Mathf.Clamp(pxRight, 0f, texW);
            var cropT = Mathf.Clamp(pyTop, 0f, texH);
            var cropB = Mathf.Clamp(pyBottom, 0f, texH);
            var cropW = cropR - cropL;
            var cropH = cropB - cropT;
            if (cropW <= 1f || cropH <= 1f || tileXRight <= tileXLeft || tileYHigh <= tileYLow)
            {
                Game.Logger?.Warn(LogTag,
                    $"{name} 裁切 / 落位参数非法（px x{pxLeft}..{pxRight} py{pyTop}..{pyBottom}，" +
                    $"贴图 {texW}×{texH}，格 x{tileXLeft}..{tileXRight} y{tileYLow}..{tileYHigh}）⇒ 跳过该件");
                return;
            }

            var sp = Sprite.Create(src.texture, new Rect(cropL, texH - cropB, cropW, cropH),
                new Vector2(0.5f, 0.5f), ppu);
            sp.name = name;
            _owned.Add(sp);

            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.color = tint;
            r.sortingOrder = order;
            go.transform.position = GameConst.TileToWorld((tileXLeft + tileXRight) * 0.5f, (tileYLow + tileYHigh) * 0.5f);
            go.transform.localScale = new Vector3((tileXRight - tileXLeft) / (cropW / ppu),
                (tileYHigh - tileYLow) / (cropH / ppu), 1f);
        }

        /// <summary>运行时生成一枚白色椭圆 alpha 遮罩（边缘衰减到 0），供阴影以黑色顶点色使用。</summary>
        private Sprite MakeEllipseSprite(string name, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = name + "Tex";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var dx = (x + 0.5f) / w * 2f - 1f;
                    var dy = (y + 0.5f) / h * 2f - 1f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _owned.Add(tex);
            var sp = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), SpriteBank.FallbackPixelsPerUnit);
            sp.name = name;
            _owned.Add(sp);
            return sp;
        }

        /// <summary>运行时生成四边暗角遮罩：中心全透明、屏幕边缘 alpha = <see cref="VignetteEdgeAlpha"/>。</summary>
        private Sprite MakeVignetteSprite()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.name = "ArenaVignetteTex";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var dx = Mathf.Abs((x + 0.5f) / n * 2f - 1f);
                    var dy = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                    var d = Mathf.Max(dx, dy);
                    var t = Mathf.Clamp01((d - VignetteInnerRadius) / (1f - VignetteInnerRadius));
                    var a = t * t * (3f - 2f * t) * VignetteEdgeAlpha;
                    px[y * n + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(255f * a));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _owned.Add(tex);
            var sp = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), SpriteBank.FallbackPixelsPerUnit);
            sp.name = "ArenaVignette";
            _owned.Add(sp);
            return sp;
        }

        /// <summary>按帧号取帧（多子图 PNG 会让数组下标整体偏移，⛔ 不按下标取）。</summary>
        private static Sprite FindFrame(Sprite[] frames, int frameNo)
        {
            if (frames == null) return null;
            var want = "frame_" + frameNo.ToString("D3");
            for (var i = 0; i < frames.Length; i++)
            {
                var s = frames[i];
                if (s == null) continue;
                var n = s.name;
                if (n == want || n.StartsWith(want + "_", System.StringComparison.Ordinal)) return s;
            }
            return null;
        }

        private void OnDestroy()
        {
            for (var i = 0; i < _owned.Count; i++)
                if (_owned[i] != null) Destroy(_owned[i]);
            _owned.Clear();
        }
    }
}
