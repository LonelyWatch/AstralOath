Shader "AstralOath/CharacterCutinBlack"
{
    Properties { _MainTex ("Cut-in", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 frame = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                // The original WebP colors are composited on opaque black in sRGB.
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return half4(frame.rgb * frame.a, 1);
                #else
                    return half4(SRGBToLinear(LinearToSRGB(frame.rgb) * frame.a), 1);
                #endif
            }
            ENDHLSL
        }
    }
}
