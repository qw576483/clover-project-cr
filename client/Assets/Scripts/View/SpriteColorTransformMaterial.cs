using System.Collections.Generic;
using CloverEngine;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 把原版 `.sc` 的 `0x09` **颜色变换**（`add.rgb / alpha / mul.rgb`）变成**共享材质**，
    /// 供 `SpriteRenderer.sharedMaterial` 与 uGUI `Image.material` 使用。
    ///
    /// <para>
    /// <b>数值口径（权威源）</b>：`github.com/scwmake/SupercellFlash`（commit `41e894d5…`）的
    /// `ColorTransform.h/.cpp` —— 字段序 `add.r, add.g, add.b, alpha, mul.r, mul.g, mul.b`，
    /// 应用式 `out_rgb = clamp(src * mul/255 + add)`、`out_a = clamp(src_a * alpha/255)`；
    /// **`255` = 1.0**（`ColorTransform` 的默认值 `mul = {255,255,255}` / `add = {0,0,0}` 即恒等）。
    /// </para>
    ///
    /// <para>
    /// <b>为什么需要它</b>：`add ≠ 0` 的件（例：`ghost` 的 `add=255`、计时板 `193` 的 `mul=0` ⇒ 结果 = `add`）
    /// 用「顶点色乘算」表达不出来 —— 乘算得到的最亮值就是图元原色，无法提亮、无法换色相。
    /// 顶点色仍按各自调用点的语义使用（逐实例 tint），本类只提供**图元侧**的那一层变换。
    /// </para>
    ///
    /// <para>
    /// <b>两条渲染路径各一份着色器</b>：精灵层 `Assets/Shaders/SpriteBlend.shader`（`CR/SpriteBlend`，
    /// `UnitySprites.cginc` 口径）；uGUI 层 `Assets/Shaders/UiColorTransform.shader`（`CR/UiColorTransform`，
    /// `UnityUI.cginc` 口径，保留 `Image.color` 的顶点色语义与裁剪/stencil）。两者都只多两个属性：
    /// `_ColorMul` / `_ColorAdd`，**默认值 = 恒等** ⇒ 不设这两项的材质与改动前逐像素一致。
    /// </para>
    ///
    /// <para>
    /// <b>共享材质的正确性</b>：逐 renderer 的染色/翻转走 `_RendererColor` / `_Flip`（`SpriteRenderer`
    /// 逐 draw 写入）、`_MainTex` 逐 sprite 写入；uGUI 侧 `Image.color` 走顶点色
    /// ⇒ 一种颜色变换一份材质即可，⛔ 不要 `renderer.material`（那会逐实例克隆材质）。
    /// </para>
    /// </summary>
    internal static class SpriteColorTransformMaterial
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "SpriteColorXform";

        /// <summary>精灵层着色器名（见 `Assets/Shaders/SpriteBlend.shader`）。</summary>
        public const string SpriteShaderName = "CR/SpriteBlend";

        /// <summary>uGUI 层着色器名（见 `Assets/Shaders/UiColorTransform.shader`）。</summary>
        public const string UiShaderName = "CR/UiColorTransform";

        // `UnityEngine.Rendering.BlendMode` 数值（出处：Unity Manual `SL-Blend.html`）。
        // 精灵层保持 `Sprites/Default` 的行为：预乘 + `One / OneMinusSrcAlpha`。
        private const float ModeOne = 1f;
        private const float ModeOneMinusSrcAlpha = 10f;

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static bool _warnedSprite;
        private static bool _warnedUi;

        /// <summary>
        /// 取一份「精灵层」共享材质（`SpriteRenderer.sharedMaterial` 用）。
        /// </summary>
        /// <param name="mulR">`mul.r` 原始字节（0..255，255 = 1.0）。</param>
        /// <param name="mulG">`mul.g` 原始字节。</param>
        /// <param name="mulB">`mul.b` 原始字节。</param>
        /// <param name="alpha">`alpha` 原始字节（255 = 1.0）。</param>
        /// <param name="addR">`add.r` 原始字节。</param>
        /// <param name="addG">`add.g` 原始字节。</param>
        /// <param name="addB">`add.b` 原始字节。</param>
        public static Material ForSprite(int mulR, int mulG, int mulB, int alpha, int addR, int addG, int addB)
        {
            return For(false, mulR, mulG, mulB, alpha, addR, addG, addB);
        }

        /// <summary>取一份「uGUI 层」共享材质（`Image.material` 用）。</summary>
        public static Material ForUi(int mulR, int mulG, int mulB, int alpha, int addR, int addG, int addB)
        {
            return For(true, mulR, mulG, mulB, alpha, addR, addG, addB);
        }

        /// <summary>已造的材质份数（自检用）。</summary>
        public static int CachedCount { get { return Cache.Count; } }

        private static Material For(bool ui, int mulR, int mulG, int mulB, int alpha, int addR, int addG, int addB)
        {
            var key = (ui ? "ui:" : "sp:") + mulR + "," + mulG + "," + mulB + "," + alpha + "," + addR + "," + addG + "," + addB;

            Material cached;
            // 缓存有效性检查：`Resources.UnloadUnusedAssets` 会把没被引用的运行时材质销毁，
            // 此时缓存里拿到的是 Unity 的"假 null"，直接返回会让颜色变换整段消失且零报错
            // （同 `SpriteBlendMaterial.ForBlend` 与 `FrameBank.LoadDir` 的"假 null"处理口径）⇒ 失效即重造。
            if (Cache.TryGetValue(key, out cached) && cached != null && cached.shader != null) return cached;

            var mat = Create(ui, mulR, mulG, mulB, alpha, addR, addG, addB, key);
            if (mat == null) return null;
            Cache[key] = mat;
            return mat;
        }

        private static Material Create(bool ui, int mulR, int mulG, int mulB, int alpha, int addR, int addG, int addB, string key)
        {
            var shaderName = ui ? UiShaderName : SpriteShaderName;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                // 非预期分支必须留痕：着色器取不到（被裁剪 / 名字改了）⇒ 表现退回我们自己的顶点色近似。
                if (ui ? !_warnedUi : !_warnedSprite)
                {
                    if (ui) _warnedUi = true; else _warnedSprite = true;
                    Game.Logger?.Error(LogTag,
                        $"取不到着色器 {shaderName} ⇒ 该路径的 `.sc` 颜色变换退化为顶点色近似（add 项缺失）");
                }
                return null;
            }

            var mat = new Material(shader) { name = "CR_SpriteXform_" + (ui ? "ui_" : "sp_") + key.Replace(":", "").Replace(",", "_") };
            if (!ui)
            {
                // 精灵层保持 `Sprites/Default` 的混合行为（预乘 + One / OneMinusSrcAlpha）。
                mat.SetFloat("_SrcBlend", ModeOne);
                mat.SetFloat("_DstBlend", ModeOneMinusSrcAlpha);
                mat.SetFloat("_Premultiply", 1f);
            }
            mat.SetColor("_ColorMul", new Color(mulR / 255f, mulG / 255f, mulB / 255f, alpha / 255f));
            mat.SetColor("_ColorAdd", new Color(addR / 255f, addG / 255f, addB / 255f, 0f));
            return mat;
        }
    }
}
