// Minecraft's red "hurt" flash on a Mycelium enemy: the enemy's mesh drawn again on top of itself
// (HurtFlash adds this material for a moment, then takes it away), in two steps:
//  1. multiply the colours already there by _Multiply: red stays, green and blue dim, so every detail
//     of the enemy's own look stays visible (a flat red layer, the first version, covered them);
//  2. add a little red light (_Add), so the flash isn't darker than the enemy (your note, 2026-10-19).
// Both passes carry a URP light mode (SRPDefaultUnlit, UniversalForward) so URP draws both.
Shader "Hidden/MyceliumCraft/HurtTint"
{
    Properties
    {
        _Multiply ("Multiply by", Color) = (1, 0.72, 0.72, 1)
        _Add ("Then add", Color) = (0.12, 0.03, 0.03, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Back
        ZWrite Off
        ZTest LEqual
        Offset -1, -1

        CGINCLUDE
        #include "UnityCG.cginc"
        float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 _Multiply;
            fixed4 frag() : SV_Target { return _Multiply; }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 _Add;
            fixed4 frag() : SV_Target { return fixed4(_Add.rgb, 0); }
            ENDCG
        }
    }
}
