// 精灵混合着色器：把 `SpriteRenderer` 的默认混合（`Sprites/Default` = `Blend One OneMinusSrcAlpha`）
// 换成「原版精灵的 blend_mode」对应的一档。
//
// 顶点/片元阶段与 Unity 内置精灵着色器同源 —— 直接 include Unity 自带的 `UnitySprites.cginc`
// （`<Unity 安装>/Editor/Data/Resources/CGIncludes/UnitySprites.cginc`）：
//   · `SpriteVert`：`OUT.color = IN.color * _Color * _RendererColor`（顶点色 × 材质色 × `SpriteRenderer.color`），
//     `_Flip` / `_RendererColor` 由 `SpriteRenderer` 逐 draw 写入，缺了它们单位会失去染色与翻转。
//   · `SampleSpriteTexture`：采样 `_MainTex`（`SpriteRenderer` 逐 sprite 写入，故可用**共享材质**）。
// 本文件只做两件事：① `Blend` 由材质属性驱动；② 片元按模式决定是否**预乘**（见 `_Premultiply`）。
//
// Blend 值的出处：Unity Manual `SL-Blend.html`（ShaderLab `Blend` 命令）——
//   加法 Add      = `Blend One One`            （`src + dst`）
//   正片叠底 Multiply = `Blend DstColor Zero`  （`src * dst`）
//   滤色 Screen   = `Blend OneMinusDstColor One`（`1-(1-src)(1-dst)`）
//   属性驱动写法 `Blend [_SrcBlend] [_DstBlend]` 与该页给出的 `UnityEngine.Rendering.BlendMode` 数值
//   （Zero=0 / One=1 / DstColor=2 / OneMinusDstColor=4 / OneMinusSrcAlpha=10）一致。
//
// `_Premultiply` 的取舍：`Sprites/Default` 的片元输出是**预乘**的（`c.rgb *= c.a`）——
// 加法与滤色的 src factor 都作用于源色，必须保持预乘，否则全透明像素也会被加进去；
// 正片叠底用的是**未预乘**的原色（乘数语义：透明像素为白才对目标无影响）。
Shader "CR/SpriteBlend"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [MaterialToggle] _Premultiply ("Premultiply RGB by Alpha", Float) = 1
        // 原版 `.sc` 的 `0x09` 颜色变换：`out = clamp(src * mul/255 + add)`（`255` = 1.0）。
        // 默认值 = 恒等（mul = 1、add = 0）⇒ 不设这两项的材质与改动前逐像素一致。
        _ColorMul ("Color Mul (transform)", Color) = (1,1,1,1)
        _ColorAdd ("Color Add (transform)", Color) = (0,0,0,0)
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Name "SpriteBlend"

            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment SpriteBlendFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnityCG.cginc"
            #include "UnitySprites.cginc"

            // 1 = 预乘（加法 / 滤色 / 普通 alpha 混合）；0 = 不预乘（正片叠底，用原色当乘数）。
            fixed _Premultiply;
            fixed4 _ColorMul;
            fixed4 _ColorAdd;

            fixed4 SpriteBlendFrag(v2f IN) : SV_Target
            {
                fixed4 s = SampleSpriteTexture(IN.texcoord);
                // 原版颜色变换先作用在**图元自身**上（`.sc` 的 `09` 就是原版的染色机制），
                // 之后再叠我们自己的顶点色（合法性提示一类）。默认 mul=1/add=0 ⇒ 恒等。
                s.rgb = saturate(s.rgb * _ColorMul.rgb + _ColorAdd.rgb);
                s.a = saturate(s.a * _ColorMul.a);
                fixed4 c = s * IN.color;
                c.rgb *= lerp(fixed(1.0), c.a, _Premultiply);
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
