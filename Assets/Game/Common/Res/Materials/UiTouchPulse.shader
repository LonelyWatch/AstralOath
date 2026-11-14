Shader "AstralOath/UI/TouchPulse"
{
    Properties { [PerRendererData] _MainTex ("Pulse frames", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            sampler2D _MainTex;

            Varyings Vert(Input input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 pulse = tex2D(_MainTex, input.uv);
                // RGB contains the additive energy as well as premultiplied normal layers.
                // Alpha contains only normal-layer coverage. Zero alpha may still have visible RGB.
                #if !defined(UNITY_COLORSPACE_GAMMA)
                pulse.rgb = GammaToLinearSpace(pulse.rgb);
                #endif
                return pulse * input.color.a;
            }
            ENDCG
        }
    }
}
