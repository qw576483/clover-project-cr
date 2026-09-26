using System.Collections.Generic;
using CloverEngine;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 把原版 <c>blend_mode</c>（<see cref="SpriteBlendTable"/> 给出的 3 / 4 / 8）换成**共享材质**，
    /// 供逐帧精灵的 <c>SpriteRenderer.sharedMaterial</c> 使用。
    ///
    /// <para>
    /// <b>为什么是运行时造材质</b>：材质只能在运行时取（资源目录走 <c>Game.Res</c>，本工程禁用
    /// <c>Resources.Load</c>；材质资产又无法在编辑期引用）。同一做法见引擎件
    /// <c>clover-client-unity-engine/Runtime/Presentation/UIWidgets.cs:1507-1548</c>
    /// （静态缓存 + <c>Shader.Find</c> + <c>new Material</c>）。
    /// </para>
    ///
    /// <para>
    /// <b>共享材质的正确性</b>：逐 renderer 的染色 / 翻转走
    /// <c>_RendererColor</c> / <c>_Flip</c>（`UnitySprites.cginc` 的 `SpriteVert` 把它们乘进顶点色，
    /// 由 <c>SpriteRenderer</c> 逐 draw 写入），`_MainTex` 由 <c>SpriteRenderer</c> 逐 sprite 写入
    /// ⇒ 三种混合各一份材质即可，⛔ 不要 `renderer.material`（那会逐实例克隆材质）。
    /// </para>
    ///
    /// <para>
    /// <b>Blend 参数出处</b>：Unity Manual `SL-Blend.html`（`Blend` 命令 + 属性驱动写法
    /// <c>Blend [_SrcBlend] [_DstBlend]</c>）给出的 <c>UnityEngine.Rendering.BlendMode</c> 数值：
    /// Zero=0 / One=1 / DstColor=2 / OneMinusDstColor=4。
    /// </para>
    /// </summary>
    internal static class SpriteBlendMaterial
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "SpriteBlend";

        /// <summary>项目侧着色器名（见 `Assets/Shaders/SpriteBlend.shader` 的 <c>Shader "CR/SpriteBlend"</c>）。</summary>
        public const string ShaderName = "CR/SpriteBlend";

        // `UnityEngine.Rendering.BlendMode` 的数值（出处见类注释）。
        private const float ModeZero = 0f;
        private const float ModeOne = 1f;
        private const float ModeDstColor = 2f;
        private const float ModeOneMinusDstColor = 4f;

        private static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();
        private static bool _warned;
        private static Material _fallbackDefault;

        /// <summary>
        /// 取该帧该用的共享材质。表里没有 / 表说 Normal / 未知 blend / 取不到着色器
        /// ⇒ 返回 <paramref name="rendererDefault"/>（**保证非 null**）。
        /// <para>
        /// ⛔ **不许让调用方退到 `sharedMaterial = null`**：那会让渲染器落到 Unity 的错误材质上，
        /// 单元整块画成**洋红**（实测：`sharedMaterial = null` 渲染后中心像素 = `(255,0,255)`，
        /// 重新赋一份真材质即恢复）。没有混合时必须写回渲染器**自带**的那份默认材质。
        /// </para>
        /// </summary>
        /// <param name="resPath">资源目录路径（与 <see cref="SpriteBank.LoadDir"/> 入参同形）。</param>
        /// <param name="frameNo">帧号（<c>frame_NNN</c> 的 <c>NNN</c>）。</param>
        /// <param name="rendererDefault">该渲染器自带的默认材质（见 <see cref="RememberDefault"/>）。</param>
        public static Material For(string resPath, int frameNo, Material rendererDefault)
        {
            var blend = SpriteBlendTable.BlendFor(resPath, frameNo);
            return blend == SpriteBlendTable.Normal ? rendererDefault : (ForBlend(blend) ?? rendererDefault);
        }

        /// <summary>
        /// 记下渲染器**自带**的默认材质；必须在第一次写该渲染器的 <c>sharedMaterial</c> **之前**
        /// （<c>AddComponent&lt;SpriteRenderer&gt;</c> 之后立刻）调用，否则读到的会是我们写过的值。
        /// 取不到时退回 <see cref="FallbackDefault"/> 并留痕。
        /// </summary>
        public static Material RememberDefault(SpriteRenderer renderer)
        {
            var def = renderer == null ? null : renderer.sharedMaterial;
            if (def != null) return def;
            // 非预期分支必须留痕：默认材质若沿用 null，渲染器会整块画成洋红且零报错。
            LogThrottle.WarnOnce(LogTag, "rendererDefault",
                "SpriteRenderer 自带默认材质取不到 ⇒ 退回内置 Sprites/Default");
            return FallbackDefault();
        }

        /// <summary>写渲染器材质；<paramref name="material"/> 为 null ⇒ 什么都不做（⛔ 绝不写 null，见 <see cref="For"/>）。</summary>
        public static void Set(SpriteRenderer renderer, Material material)
        {
            if (renderer == null || material == null || renderer.sharedMaterial == material) return;
            renderer.sharedMaterial = material;
        }

        /// <summary>兜底的默认精灵材质（内置 <c>Sprites/Default</c> 的共享实例），懒建。</summary>
        public static Material FallbackDefault()
        {
            if (_fallbackDefault != null) return _fallbackDefault;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Game.Logger?.Error(LogTag,
                        "取不到着色器 Sprites/Default ⇒ 默认材质无法还原，渲染器会整块画成洋红");
                }
                return null;
            }
            _fallbackDefault = new Material(shader) { name = "CR_SpriteBlend_Default" };
            return _fallbackDefault;
        }

        /// <summary>取某个原版 <c>blend_mode</c> 的共享材质（未知值 ⇒ <c>null</c>）。</summary>
        public static Material ForBlend(int blend)
        {
            Material cached;
            // 缓存有效性检查：`Resources.UnloadUnusedAssets` 会把没被引用的运行时材质销毁，
            // 此时缓存里拿到的是 Unity 的**"假 null"**，直接返回会让混合表现整段消失且零报错
            // （同 `FrameBank.LoadDir` 对"假 null"的处理口径）⇒ 失效即重造。
            if (Cache.TryGetValue(blend, out cached) && cached != null && cached.shader != null) return cached;
            var mat = Create(blend);
            Cache[blend] = mat;
            return mat;
        }

        /// <summary>已造的材质份数（自检用）。</summary>
        public static int CachedCount { get { return Cache.Count; } }

        private static Material Create(int blend)
        {
            float src, dst, premultiply;
            string suffix;
            switch (blend)
            {
                case SpriteBlendTable.Add:
                    src = ModeOne; dst = ModeOne; premultiply = 1f; suffix = "Add";
                    break;
                case SpriteBlendTable.Multiply:
                    src = ModeDstColor; dst = ModeZero; premultiply = 0f; suffix = "Multiply";
                    break;
                case SpriteBlendTable.Screen:
                    src = ModeOneMinusDstColor; dst = ModeOne; premultiply = 1f; suffix = "Screen";
                    break;
                default:
                    // 非预期分支必须留痕：表里出现了没实现的 blend_mode ⇒ 保持默认渲染（不静默开混合）。
                    LogThrottle.WarnOnce(LogTag, "unknown:" + blend,
                        $"SpriteBlendTable 出现了未实现的 blend_mode {blend} ⇒ 该帧保持默认材质");
                    return null;
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                // 非预期分支必须留痕：着色器取不到（被裁剪 / 名字改了）⇒ 表现会退回 Normal。
                if (!_warned)
                {
                    _warned = true;
                    Game.Logger?.Error(LogTag,
                        $"取不到着色器 {ShaderName} ⇒ 全部帧退回默认材质（混合表现缺失）");
                }
                return null;
            }

            var mat = new Material(shader) { name = "CR_SpriteBlend_" + suffix };
            mat.SetFloat("_SrcBlend", src);
            mat.SetFloat("_DstBlend", dst);
            mat.SetFloat("_Premultiply", premultiply);
            return mat;
        }
    }
}
