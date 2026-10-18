// Draws Minecraft's overlay picture on top of Mycelium. Minecraft's picture has premultiplied
// alpha (its colors are already multiplied by their coverage), so it's blended with
// "One, OneMinusSrcAlpha" instead of Unity UI's usual "SrcAlpha, OneMinusSrcAlpha".
Shader "Hidden/MyceliumCraft/Overlay"
{
    Properties
    {
        _MainTex ("Minecraft picture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color.a;
            }
            ENDCG
        }
    }
}
