// Opaque Blit Copy - Copies RGB and forces alpha=1
// Used by CompositionLayerVideoRenderer to prevent invisible video when
// NDI sources deliver RGBX with undefined (potentially 0x00) alpha bytes.
// The XR compositor respects alpha, so this shader ensures full opacity.

Shader "Hidden/NDIViewer/OpaqueBlitCopy"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                c.a = 1.0;
                return c;
            }
            ENDCG
        }
    }

    FallBack Off
}
