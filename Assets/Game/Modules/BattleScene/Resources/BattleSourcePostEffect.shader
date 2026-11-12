Shader "AstralOath/BattleSourcePostEffect"
{
    Properties
    {
        _MainTex ("World image", 2D) = "black" {}
        u_resolution ("Resolution", Vector) = (1280,720,0,0)
        u_direction ("Direction", Vector) = (0,1,0,0)
        u_range ("Range", Float) = 0
        u_sample ("Sample", Float) = 3
        u_ratio ("Original image ratio", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float2 u_resolution, u_direction;
            float u_range, u_sample, u_ratio;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 original = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                if (u_range <= 0 || dot(u_direction, u_direction) == 0 || u_sample <= .5)
                    return original;

                // The source sprite_blur.frag uses a directional triangular kernel.
                float2 direction = normalize(u_direction) / u_resolution;
                float step = u_range / min(5.0, u_sample);
                float4 color = 0;
                float total = 0;
                [loop] for (int index = 0; index < 11; index++)
                {
                    float offset = -u_range + step * index;
                    if (offset >= u_range) break;
                    float weight = u_range - abs(offset);
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + direction * offset) * weight;
                    total += weight;
                }
                return total > 0 ? lerp(color / total, original, saturate(u_ratio)) : original;
            }
            ENDHLSL
        }
    }
}
