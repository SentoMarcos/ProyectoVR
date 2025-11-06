Shader "Custom/RoadHeightTintURP"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0,2)) = 1
        _RampStrength ("Ramp Strength", Range(0,1)) = 0.35
        _HeightMin ("Height Min", Float) = 0
        _HeightMax ("Height Max", Float) = 1
        _Axis ("Axis", Vector) = (0,1,0,0)
        _BasePoint ("Base Point", Vector) = (0,0,0,0)
        _SlopeDarken ("Slope Darken", Range(0,1)) = 0.2
        _Smoothness ("Smoothness", Range(0,1)) = 0.4
        _Metallic ("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags{ "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Opaque" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 viewDirWS  : TEXCOORD3;
                float3 tangentWS  : TEXCOORD4;
                float3 bitangentWS: TEXCOORD5;
                UNITY_FOG_COORDS(6)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _NormalScale;
                float _RampStrength;
                float _HeightMin;
                float _HeightMax;
                float4 _Axis; // xyz axis
                float4 _BasePoint; // xyz base
                float _SlopeDarken;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 tWS = TransformObjectToWorldDir(IN.tangentOS.xyz);
                float tangentSign = IN.tangentOS.w * unity_WorldTransformParams.w;
                float3 bWS = cross(OUT.normalWS, tWS) * tangentSign;
                OUT.tangentWS = tWS;
                OUT.bitangentWS = bWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.viewDirWS = GetWorldSpaceViewDir(positionWS);
                UNITY_TRANSFER_FOG(OUT, OUT.positionCS);
                return OUT;
            }

            float3 SampleNormal(float2 uv, float3 nWS, float3 tWS, float3 bWS)
            {
                float4 nTex = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv);
                float3 nTS = UnpackNormalScale(nTex, _NormalScale);
                float3x3 TBN = float3x3(tWS, bWS, nWS);
                return normalize(mul(nTS, TBN));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                if (_NormalScale > 0.001)
                    nWS = SampleNormal(IN.uv, nWS, normalize(IN.tangentWS), normalize(IN.bitangentWS));

                float3 axis = normalize(_Axis.xyz);
                float h = dot(IN.positionWS - _BasePoint.xyz, axis);
                float hNorm = saturate((h - _HeightMin) / max(1e-5, (_HeightMax - _HeightMin)));
                float ramp = lerp(1 - _RampStrength, 1 + _RampStrength, hNorm);

                // Optional slight darkening by slope angle
                float slope = 1 - saturate(dot(nWS, axis));
                float slopeFactor = 1 - slope * _SlopeDarken;

                float4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                baseCol.rgb *= ramp * slopeFactor;

                // Lighting (URP simplified physically based)
                SurfaceData surface;
                surface.albedo = baseCol.rgb;
                surface.alpha = 1;
                surface.metallic = _Metallic;
                surface.specular = 0; // unused in metallic workflow
                surface.smoothness = _Smoothness;
                surface.normalTS = float3(0,0,1); // not used
                surface.normalWS = nWS;
                surface.occlusion = 1;
                surface.emission = 0;
                surface.clearCoatMask = 0;
                surface.clearCoatSmoothness = 0;

                InputData inputData;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = normalize(IN.viewDirWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.vertexLighting = 0;
                inputData.bakedGI = SAMPLE_GI(IN.lightmapUV, 0, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = 0;
                inputData.shadowMask = 1;

                half4 col = UniversalFragmentPBR(inputData, surface);
                col.a = 1;
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
