Shader "AnimationInstancing/UnlitShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vertn
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "AnimationInstancingBase.cginc"

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vertn (appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                vert(v);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        Pass
        {
            Tags{ "LightMode" = "ShadowCaster" }

            CGPROGRAM

            #pragma vertex vertn
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"
            #include "AnimationInstancingBase.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vertn(appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                vert(v);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }

            ENDCG
        }
        Pass
        {
            Tags{ "LightMode" = "DepthOnly" }

            CGPROGRAM
            #if UNITY_VERSION >= 0
            #pragma vertex vertn
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"
            #include "AnimationInstancingBase.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };
            
            v2f vertn(appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                vert(v);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }
            #endif
            ENDCG
        }
    }
}