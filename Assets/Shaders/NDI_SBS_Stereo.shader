// NDI Side-by-Side Stereo Shader
// Renders full SBS 3840x1080 frames with optional stereo eye splitting.
// When SBS mode is OFF: displays full frame on a single plane.
// When SBS mode is ON: left half -> left eye, right half -> right eye.

Shader "NDIViewer/SBS_Stereo"
{
    Properties
    {
        _MainTex ("NDI Video Texture", 2D) = "black" {}
        _SBSEnabled ("SBS 3D Mode (0=Off, 1=On)", Float) = 0
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.0
        _Contrast ("Contrast", Range(0.5, 2.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 100

        Pass
        {
            Name "NDI_SBS_Pass"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _SBSEnabled;
                float _Brightness;
                float _Contrast;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;

                // SBS Mode: split the texture horizontally for each eye
                if (_SBSEnabled > 0.5)
                {
                    // Determine which eye we're rendering for
                    // unity_StereoEyeIndex: 0 = left eye, 1 = right eye
                    #if defined(UNITY_SINGLE_PASS_STEREO) || defined(STEREO_INSTANCING_ON)
                        int eyeIndex = unity_StereoEyeIndex;
                    #else
                        // Fallback: use screen position to determine eye
                        int eyeIndex = (input.positionCS.x > _ScreenParams.x * 0.5) ? 1 : 0;
                    #endif

                    // Left eye gets left half (0.0 - 0.5), right eye gets right half (0.5 - 1.0)
                    uv.x = uv.x * 0.5 + eyeIndex * 0.5;
                }
                // When SBS is OFF, uv passes through unmodified (full frame displayed)

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // Apply brightness and contrast adjustments
                color.rgb = (color.rgb - 0.5) * _Contrast + 0.5;
                color.rgb *= _Brightness;
                color.rgb = saturate(color.rgb);

                return color;
            }
            ENDHLSL
        }
    }

    // Fallback for non-URP rendering
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _SBSEnabled;
            float _Brightness;
            float _Contrast;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.uv;

                if (_SBSEnabled > 0.5)
                {
                    #if defined(UNITY_SINGLE_PASS_STEREO)
                        int eyeIndex = unity_StereoEyeIndex;
                    #else
                        int eyeIndex = (i.pos.x > _ScreenParams.x * 0.5) ? 1 : 0;
                    #endif

                    uv.x = uv.x * 0.5 + eyeIndex * 0.5;
                }

                fixed4 color = tex2D(_MainTex, uv);
                color.rgb = (color.rgb - 0.5) * _Contrast + 0.5;
                color.rgb *= _Brightness;
                color.rgb = saturate(color.rgb);

                return color;
            }
            ENDCG
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
