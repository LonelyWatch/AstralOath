Shader "AstralOath/Skeleton"
{
    Properties
    {
        [MainTexture] _MainTex("Atlas", 2D) = "white" {}
        _PremultipliedAlpha("Premultiplied atlas", Float) = 0
        [HideInInspector] _PmaSrgb("PMA authored in sRGB", Float) = 0
        [HideInInspector] _PresentationBrightness("Presentation brightness", Float) = 1
        [HideInInspector] _PresentationOpacity("Presentation opacity", Float) = 1
        [HideInInspector] _SrcBlend("Source", Float) = 1
        [HideInInspector] _DstBlend("Destination", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "SkeletonUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest LEqual
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _PremultipliedAlpha;
                float _PmaSrgb;
                float _PresentationBrightness;
                float _PresentationOpacity;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 light:COLOR; half4 dark:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 light:COLOR; half3 dark:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.light = input.light; output.dark = input.dark.rgb;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                                // sRGB sampling linearizes the already-premultiplied channels. Undo that
                // conversion before unpremultiplication, then premultiply in linear space.
                if (_PmaSrgb > 0.5)
                    tex.rgb = SRGBToLinear(saturate(LinearToSRGB(tex.rgb) / max(tex.a, 0.00001h))) * tex.a;
                else
                    tex.rgb *= lerp(tex.a, 1.0h, (half)_PremultipliedAlpha);
                // Two-color tint in premultiplied space works for either atlas encoding.
                half3 rgb = (tex.rgb * input.light.rgb + (tex.a - tex.rgb) * input.dark) * input.light.a;
                return half4(rgb * saturate(_PresentationBrightness) * saturate(_PresentationOpacity),
                    tex.a * input.light.a * saturate(_PresentationOpacity));
            }
            ENDHLSL
        }
    }
}
