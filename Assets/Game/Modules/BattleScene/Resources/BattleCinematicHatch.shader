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
            Blend SrcAlpha OneMinusSrcAlpha
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
                // Original sprite_hatch.frag equation; provenance is in Cinematic/source.json.
                half3 t0=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv+float2(_Option.x,0)).rgb;
                half3 t1=SAMPLE_TEXTURE2D(_Texture1,sampler_Texture1,input.uv+float2(_Option.y,0)).rgb;
                half3 t2=SAMPLE_TEXTURE2D(_Texture2,sampler_Texture2,input.uv+float2(_Option.z,0)).rgb;
                return input.color*(half4(t0*_FragColor0.rgb*_FragColor0.a,_FragColor0.a)+
                    half4(t1*_FragColor1.rgb*_FragColor1.a,_FragColor1.a)+half4(t2*_FragColor2.rgb*_FragColor2.a,_FragColor2.a));
            }
            ENDHLSL
        }
    }
}
