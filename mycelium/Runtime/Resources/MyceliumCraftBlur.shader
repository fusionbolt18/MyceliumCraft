// A 9-tap Gaussian blur in one direction (_Direction: (1,0) across, (0,1) down), for the picture of
// Mycelium's UI during Minecraft's pause (McPauseBlur). Run across then down, twice. Works on
// premultiplied colours, so see-through edges blend correctly.
Shader "Hidden/MyceliumCraft/Blur"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "black" {}
        _Direction ("Direction", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Direction;

            fixed4 frag(v2f_img i) : SV_Target
            {
                // Gaussian weights, sigma about 2 texels.
                const float w[5] = { 0.2042, 0.1802, 0.1238, 0.0663, 0.0276 };
                float2 step = _Direction.xy * _MainTex_TexelSize.xy;
                float4 c = tex2D(_MainTex, i.uv) * w[0];
                for (int k = 1; k < 5; k++)
                {
                    c += tex2D(_MainTex, i.uv + step * k) * w[k];
                    c += tex2D(_MainTex, i.uv - step * k) * w[k];
                }
                return c;
            }
            ENDCG
        }
    }
}
