// 항목 19B 공용 셰이더: Kenney CC0 모델(자연물·도시 구조물)을 목표 룩으로 맞춘다.
// 원래 색(텍스처 × 색) × 팔레트 틴트 × 월드 높이 그라데이션 × 월드 좌표 저주파 색 노이즈 × 바닥 접촉 가짜 AO.
// 램버트 + 주광 그림자 + 구면 조화 환경광 + 안개. 외곽선 없음. SRP Batcher·GPU 인스턴싱 호환(머티리얼 값은 한 CBUFFER).
Shader "FactoryWars/KenneyLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Tint ("Palette Tint", Color) = (1, 1, 1, 1)
        _Desaturate ("Desaturate", Range(0, 1)) = 0.35
        _GradientBottom ("Gradient Bottom (world Y)", Float) = 0
        _GradientTop ("Gradient Top (world Y)", Float) = 12
        _GradientStrength ("Gradient Strength", Range(0, 1)) = 0.22
        _NoiseScale ("Noise Scale", Float) = 0.035
        _NoiseStrength ("Noise Strength", Range(0, 0.5)) = 0.08
        _AoHeight ("Contact AO Height", Float) = 1.0
        _AoStrength ("Contact AO Strength", Range(0, 1)) = 0.35
        _GroundY ("Ground Y", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _Tint;
            half _Desaturate;
            float _GradientBottom;
            float _GradientTop;
            half _GradientStrength;
            float _NoiseScale;
            half _NoiseStrength;
            float _AoHeight;
            half _AoStrength;
            float _GroundY;
            half _Cutoff;
        CBUFFER_END

        // 값 노이즈 두 겹. 월드 좌표만 쓰므로 같은 자리는 항상 같은 색이다.
        float FwHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float FwNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = FwHash(i);
            float b = FwHash(i + float2(1, 0));
            float c = FwHash(i + float2(0, 1));
            float d = FwHash(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        half3 FwShade(half3 albedo, float3 positionWS)
        {
            half grey = dot(albedo, half3(0.299, 0.587, 0.114));
            albedo = lerp(albedo, grey.xxx, _Desaturate) * _Tint.rgb;
            float t = saturate((positionWS.y - _GradientBottom) / max(0.01, _GradientTop - _GradientBottom));
            half shade = 1.0 + _GradientStrength * (t - 0.5);
            float n = FwNoise(positionWS.xz * _NoiseScale) * 0.7 + FwNoise(positionWS.xz * _NoiseScale * 3.1 + 17.0) * 0.3;
            shade += _NoiseStrength * (n * 2.0 - 1.0);
            float lift = positionWS.y - _GroundY;
            shade -= _AoStrength * (1.0 - saturate(lift / max(0.01, _AoHeight)));
            return albedo * max(0.2, shade);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                albedo = FwShade(albedo, input.positionWS);
                half3 normal = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);
                half ambientOcclusion = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUv);
                    ambientOcclusion = ao.indirectAmbientOcclusion;
                    light.color *= ao.directAmbientOcclusion;
                #endif
                half lambert = saturate(dot(normal, light.direction));
                half3 lit = albedo * light.color * (lambert * light.shadowAttenuation * light.distanceAttenuation);
                lit += albedo * SampleSH(normal) * ambientOcclusion;
                lit = MixFog(lit, input.fog);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
