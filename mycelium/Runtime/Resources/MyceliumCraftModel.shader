// Draws your Minecraft player's body in Mycelium's world (McPlayerModel). Minecraft-style: the
// skin's pixels, see-through where the skin is (the second layer), and each face shaded by which
// way it points (top brightest, bottom darkest), the way Minecraft shades entities.
//
// Besides the colour pass it has the two passes Unity's renderer (URP) uses to build its depth
// and normals textures before drawing (Mycelium's SSAO asks for them). Without them the body was
// missing from those textures, and Mycelium's Fog effect, which reads the depth, took the body's
// pixels for open sky and covered them in sky-coloured haze.
Shader "Hidden/MyceliumCraft/Model"
{
    Properties
    {
        _MainTex ("Minecraft texture", 2D) = "white" {}
        _Brightness ("Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Cull Off

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float _Brightness;

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 world : TEXCOORD1;
            fixed4 color : COLOR;
        };

        v2f vert(appdata_full v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord.xy;
            o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.color = v.color;
            return o;
        }

        // The face's direction in the world, from the screen-space slopes, turned toward the camera
        // (the mesh has no normals of its own; every Minecraft face is flat).
        float3 FaceNormal(v2f i)
        {
            float3 n = normalize(cross(ddy(i.world), ddx(i.world)));
            if (dot(n, _WorldSpaceCameraPos - i.world) < 0) n = -n;
            return n;
        }

        fixed4 Skin(v2f i)
        {
            fixed4 c = tex2D(_MainTex, i.uv) * i.color;
            clip(c.a - 0.1);
            return c;
        }
        ENDCG

        Pass
        {
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = Skin(i);
                float3 n = FaceNormal(i);
                float3 n2 = n * n;
                float shade = n2.x * 0.6 + n2.z * 0.8 + n2.y * (n.y > 0 ? 1.0 : 0.5);
                return fixed4(c.rgb * shade * _Brightness, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(v2f i) : SV_Target
            {
                Skin(i);
                return 0;
            }
            ENDCG
        }

        // The body's shadow: drawn into the sun's shadow map, with the skin's see-through pixels left
        // out. Pushed slightly away from the sun by URP's depth bias (_ShadowBias.x along
        // _LightDirection), as URP's own shaders do; there are no vertex normals for its normal bias.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment frag

            float4 _ShadowBias;
            float3 _LightDirection;

            v2f vertShadow(appdata_full v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz + _LightDirection * _ShadowBias.x;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                #if UNITY_REVERSED_Z
                o.pos.z = min(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
                #else
                o.pos.z = max(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = v.texcoord.xy;
                o.world = world;
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                Skin(i);
                return 0;
            }
            ENDCG
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP's normals texture holds the world-space normal.
            half4 frag(v2f i) : SV_Target
            {
                Skin(i);
                return half4(FaceNormal(i), 0);
            }
            ENDCG
        }
    }
}
