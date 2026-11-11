Shader "AstralOath/CharacterSpriteBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Preview", 2D) = "white" {}
        u_resolution ("Resolution", Vector) = (1920,1080,0,0)
        u_direction ("Direction", Vector) = (1,0,0,0)
        u_range ("Range", Float) = 0
        u_sample ("Sample", Float) = 3
        u_ratio ("Original image ratio", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            Blend One Zero
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            sampler2D _MainTex;
            float2 u_resolution;
            float2 u_direction;
            float u_range;
            float u_sample;
            float u_ratio;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex=UnityObjectToClipPos(input.vertex);
                output.uv=input.uv;
                return output;
            }
            // Faithful weighted directional kernel from the read-only source's sprite_blur.frag.
            // The lab applies it to the camera image, not to a proven original sprite grouping.
            float4 blur(float2 uv)
            {
                float4 result=tex2D(_MainTex,uv);
                if(u_range>0 && length(u_direction)>0 && u_sample>.5)
                {
                    float4 color=float4(0,0,0,0);
                    float2 direction=normalize(u_direction)/u_resolution;
                    float step=u_range/min(5.0,u_sample);
                    float count=0;
                    if(step>0)
                    {
                        [loop] for(float offset=-u_range;offset<u_range;offset+=step)
                        {
                            float weight=u_range-abs(offset);
                            color+=tex2D(_MainTex,uv+offset*direction)*weight;
                            count+=weight;
                        }
                    }
                    // Degenerate/underflowed kernels retain the initialized source sample.
                    // Every positive-weight kernel uses its original exact denominator.
                    float denominator=count>0 ? count : 1;
                    result=count>0 ? color/denominator : result;
                }
                return result;
            }
            float4 frag(Varyings input):SV_Target
            {
                float ratio=saturate(u_ratio);
                return blur(input.uv)*(1-ratio)+ratio*tex2D(_MainTex,input.uv);
            }
            ENDHLSL
        }
    }
}
