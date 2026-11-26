Shader "AstralOath/Mail/SourceAdditive"
{
 Properties { [PerRendererData] _MainTex ("Source texture",2D)="black" {} }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend One One
  ColorMask RGB
  Pass
  {
   CGPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   struct Input { float4 position:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
   struct Varying { float4 position:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
   sampler2D _MainTex;
   Varying Vert(Input v) { Varying o;o.position=UnityObjectToClipPos(v.position);o.uv=v.uv;o.color=v.color;return o; }
   float4 Frag(Varying v):SV_Target
   {
    float4 source=tex2D(_MainTex,v.uv);
    source.rgb*=v.color.rgb*v.color.a;
    #if !defined(UNITY_COLORSPACE_GAMMA)
    source.rgb=GammaToLinearSpace(source.rgb);
    #endif
    return source;
   }
   ENDCG
  }
 }
}
