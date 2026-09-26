// uGUI 版本的「原版颜色变换」着色器：给 `Image.material` 用。
//
// 为什么另开一份：HUD 的图元走 uGUI `Image`（不是 `SpriteRenderer`），
// `Assets/Shaders/SpriteBlend.shader` 依赖 `UnitySprites.cginc` 的 `SpriteVert` / `_RendererColor`，
// uGUI 的网格不提供那些量，且 UI 还需要遮罩/裁剪（`UnityUI.cginc` 的 `UnityGet2DClipping` 与 stencil）。
//
// 数值口径与 `SpriteBlend.shader` 完全一致：原版 `.sc` 的 `0x09` 是
// `out_rgb = clamp(src * mul/255 + add)`、`out_a = clamp(src_a * alpha/255)`（`255` = 1.0）。
// 材质把 `alpha` 并进 `_ColorMul.a`、`add` 放进 `_ColorAdd.rgb`。
//
// 顶点色语义保持 UI 默认：`顶点色 × _Color`（`Image.color` 是逐实例的 tint，⛔ 不能破坏它）。
Shader "CR/UiColorTransform"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ColorMul ("Color Mul (transform)", Color) = (1,1,1,1)
        _ColorAdd ("Color Add (transform)", Color) = (0,0,0,0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UiColorTransform"

            CGPROGRAM
            #pragma vertex UiColorTransformVert
            #pragma fragment UiColorTransformFrag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _ColorMul;
            fixed4 _ColorAdd;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            v2f UiColorTransformVert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 UiColorTransformFrag(v2f IN) : SV_Target
            {
                half4 s = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                s.rgb = saturate(s.rgb * _ColorMul.rgb + _ColorAdd.rgb);
                s.a = saturate(s.a * _ColorMul.a);

                fixed4 c = s * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif

                return c;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
