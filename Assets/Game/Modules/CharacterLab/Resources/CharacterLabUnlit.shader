Shader "AstralOath/CharacterLabUnlit"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _SrcBlend ("Source Blend", Float) = 5 _DstBlend ("Destination Blend", Float) = 10 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            Varyings vert(Attributes input) { Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz);o.uv=input.uv;o.color=input.color;return o; }
            half4 frag(Varyings input):SV_Target { return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv)*input.color; }
            ENDHLSL
        }
    }
}
