Shader "AstralOath/BattleCinematicHatch"
{
    Properties
    {
        _MainTex ("Source layer 0", 2D) = "black" {}
        _Texture1 ("Source layer 1", 2D) = "black" {}
        _Texture2 ("Source layer 2", 2D) = "black" {}
        _FragColor0 ("Source tint 0", Color) = (1,1,1,1)
        _FragColor1 ("Source tint 1", Color) = (1,1,1,1)
        _FragColor2 ("Source tint 2", Color) = (1,1,1,1)
        _Option ("Source UV offsets", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Texture1); SAMPLER(sampler_Texture1);
            TEXTURE2D(_Texture2); SAMPLER(sampler_Texture2);
            float4 _Option;
            half4 _FragColor0, _FragColor1, _FragColor2;
            Varyings vert(Attributes input) { Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv; o.color=input.color; return o; }
            half4 frag(Varyings input):SV_Target
            {
                // Original Cocos PNG uploads are premultiplied. These source PNGs remain
                // straight RGBA in Unity: their alpha carries the soft streak intensity.
                // Preserve that intensity before the original three-layer tint sum.
                half4 t0=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv+float2(_Option.x,0));
                half4 t1=SAMPLE_TEXTURE2D(_Texture1,sampler_Texture1,input.uv+float2(_Option.y,0));
                half4 t2=SAMPLE_TEXTURE2D(_Texture2,sampler_Texture2,input.uv+float2(_Option.z,0));
                half3 rgb=t0.rgb*t0.a*_FragColor0.rgb*_FragColor0.a+
                    t1.rgb*t1.a*_FragColor1.rgb*_FragColor1.a+
                    t2.rgb*t2.a*_FragColor2.rgb*_FragColor2.a;
                half alpha=(_FragColor0.a+_FragColor1.a+_FragColor2.a)*input.color.a;
                // The sprite opacity affects premultiplied RGB once; the blend stage
                // must not multiply by the accumulated alpha for a second time.
                return half4(rgb*input.color.rgb*input.color.a,saturate(alpha));
            }
            ENDHLSL
        }
    }
}
