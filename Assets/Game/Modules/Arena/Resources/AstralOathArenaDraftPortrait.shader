Shader "AstralOath/UI/ArenaDraftPortrait"
{
    Properties
    {
        [PerRendererData] _MainTex ("Portrait", 2D) = "white" {}
        _WhiteFlash ("White Flash", Range(0, 1)) = 0
        _Desaturation ("Desaturation", Range(0, 1)) = 0
        _SoftEffectEdges ("Soft Effect Edges", Range(0, 1)) = 0
        _RightFade ("Right Fade", Vector) = (1, 2, 0, 0)
        _BlendCardLight ("Blend Card Light", Range(0, 1)) = 0
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct Input
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _ClipRect;
            half _WhiteFlash, _Desaturation, _SoftEffectEdges, _BlendCardLight;
            float4 _RightFade;

            Varyings Vert(Input input)
            {
                Varyings value;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(value);
                value.position = UnityObjectToClipPos(input.vertex);
                value.localPosition = input.vertex.xy;
                value.uv = input.uv;
                value.color = input.color;
                return value;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The skeleton camera already premultiplied the captured color into its render target.
                half4 color = tex2D(_MainTex, input.uv);
                half peak = max(max(color.r, color.g), color.b);
                // Confirmed picks preserve separate coverage for normal and additive layers.
                // Keep the authored RGB over 1 so the slot can briefly peak white, then reveal
                // the portrait as the filled slots fade before the line slots.
                color.rgb *= lerp(rcp(max(1.0h, peak)), 1.12h, _BlendCardLight);
                color.a = saturate(color.a);
                color *= half4(input.color.rgb, 1) * input.color.a;
                // A white silhouette inside the captured alpha, still premultiplied.
                // Multiplying a portrait by white only preserves its original colors.
                half luminance = dot(color.rgb, half3(.2126, .7152, .0722));
                color.rgb = lerp(color.rgb, luminance.xxx, saturate(_Desaturation));
                color.rgb = lerp(color.rgb, color.aaa, saturate(_WhiteFlash));
                // Feather only the padded render target's edges for pick effects, beyond
                // the native outline. The central ban effect uses a distinct radial envelope.
                float2 fromCenter = abs(input.uv * 2.0 - 1.0);
                float pickFeather = (1.0 - smoothstep(.76, .995, fromCenter.x)) *
                    (1.0 - smoothstep(.62, .995, fromCenter.y));
                float banFeather = 1.0 - smoothstep(.50, .98, length(fromCenter));
                color *= lerp(1.0, lerp(banFeather, pickFeather, _BlendCardLight), _SoftEffectEdges) *
                    (1.0 - smoothstep(_RightFade.x, _RightFade.y, input.uv.x));
                #ifdef UNITY_UI_CLIP_RECT
                    color *= UnityGet2DClipping(input.localPosition, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - .001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
