Shader "AstralOath/UI/InfoPanelSkill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 panel = tex2D(_MainTex, input.uv);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                // Reduce linear-light bleed through the painted surface while retaining the source shadow and corners.
                float paintedSurface = smoothstep(0.75, 0.96, panel.a);
                panel.a = lerp(panel.a, 1.0 - pow(1.0 - panel.a, 1.36), paintedSurface);
                #endif
                panel *= input.color;
                return float4(panel.rgb * panel.a, panel.a);
            }
            ENDCG
        }
    }
}
