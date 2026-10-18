// Test aid (-mcModelCheck): reads the scene depth texture (what Mycelium's Fog effect reads) at one
// screen point and writes its distance from the camera, in units, into a 1x1 float texture.
Shader "Hidden/MyceliumCraft/DepthProbe"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _ProbeUV;

            float4 frag(v2f_img i) : SV_Target
            {
                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, _ProbeUV.xy);
                return float4(LinearEyeDepth(raw), raw, 0, 1);
            }
            ENDCG
        }
    }
}
