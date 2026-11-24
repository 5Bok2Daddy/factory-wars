// 항목 36 수로 물 표면(우리 쪽 셰이더, 구입한 팩에는 물 재질이 없다). 파스텔 에메랄드 얕은 쪽 ↔ 깊은 쪽이 월드 좌표 노이즈로 천천히 흐른다.
// 높은 품질(키워드 _FW_DEPTH_FOAM): URP 카메라 깊이 텍스처로 "물 면과 뒤의 물체(둑·수문 기둥·배·다리 기둥)의 깊이 차"를 구해, 맞닿는 곳에 흰 거품 띠가 그려지고 가까울수록 투명해진다.
// 낮음 품질(키워드 없음): 깊이 텍스처를 읽지 않는다. 정점 색 r이 1인 띠(강둑 가장자리)에 줄무늬 거품만 그리고 불투명 단색이다(메쉬의 알파 가장자리 거품).
// 머티리얼 하나를 모든 물이 공유한다. 색·거품 폭·흐름 속도는 VisualProfile 데이터가 채운다.
Shader "FactoryWars/UrbanWater"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.14, 0.44, 0.56, 1)
        _Deep ("Deep", Color) = (0.08, 0.23, 0.38, 1)
        _Foam ("Foam", Color) = (0.94, 0.98, 1, 1)
        _WaveTex ("Wave Surface (IgniteCoders)", 2D) = "gray" {}
        _Flow ("Flow (xz speed)", Vector) = (0.06, 0.03, 0, 0)
        _Scale ("Pattern scale", Float) = 0.08
        _FoamWidth ("Depth foam width (m)", Float) = 1.1
        _DepthFade ("Depth fade distance (m)", Float) = 3.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _FW_DEPTH_FOAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_WaveTex);
            SAMPLER(sampler_WaveTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow;
                half4 _Deep;
                half4 _Foam;
                float4 _Flow;
                float _Scale;
                float _FoamWidth;
                float _DepthFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half4 color : COLOR; };

            float WHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float WNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WHash(i), WHash(i + float2(1, 0)), f.x), lerp(WHash(i + float2(0, 1)), WHash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 p = input.positionWS.xz * _Scale + _Flow.xy * t;
                float2 uv1 = input.positionWS.xz * 0.045 + float2(t * 0.018, t * 0.011);
                float2 uv2 = input.positionWS.xz * 0.085 - float2(t * 0.013, -t * 0.019);
                half w1 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv1).r;
                half w2 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv2).g;
                float waveTex = saturate((w1 + w2) * 0.5);
                float n = WNoise(p) * 0.45 + waveTex * 0.55;
                half3 color = lerp(_Deep.rgb, _Shallow.rgb, saturate(n * 1.15));
                // IgniteCoders wave crest highlights + gentle directional sun sheen.
                color += smoothstep(0.68, 0.92, waveTex) * half3(0.08, 0.13, 0.16);
                // 잔물결 하이라이트: 좁은 띠가 흐른다.
                #if defined(_FW_DEPTH_FOAM)
                    // Two traveling waves tilt the reflection. Fade them before they become subpixel.
                    const float2 first = float2(1.15, 0.45);
                    const float2 second = float2(-0.36, 1.9);
                    float a = dot(input.positionWS.xz, first) - t * 1.4;
                    float b = dot(input.positionWS.xz, second) + t * 1.9;
                    float2 slope = first * (0.045 * cos(a) * saturate(1 - fwidth(a)))
                                 + second * (0.028 * cos(b) * saturate(1 - fwidth(b)));
                    half3 normal = normalize(half3(-slope.x, 1, -slope.y));
                    half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    Light sun = GetMainLight();
                    half3 halfVector = SafeNormalize(view + sun.direction);
                    half grazing = 1 - saturate(dot(normal, view));
                    color = lerp(color, lerp(_Shallow.rgb, _Foam.rgb, 0.34), 0.06 + 0.22 * grazing * grazing);
                    color += sun.color * (0.075 * pow(saturate(dot(normal, halfVector)), 20.0));
                #else
                    color += smoothstep(0.82, 0.95, WNoise(p * 3.7 - float2(t * 0.12, 0))) * 0.04;
                #endif
                half alpha = 1;
                // 줄무늬 거품(정점 색 r이 거품 띠 안쪽 1, 바깥쪽 0). 낮음 품질의 가장자리 거품이자, 높은 품질에서는 깊이 거품 위에 얹는 보조 줄무늬다.
                float stripes = WNoise(float2(input.positionWS.x * 0.35 + input.positionWS.z * 0.35, t * 0.6) * 1.7);
                half foam = saturate(input.color.r * (0.55 + 0.8 * stripes));
                #if defined(_FW_DEPTH_FOAM)
                    float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                    float rawDepth = SampleSceneDepth(uv);
                    float sceneEye = IsPerspectiveProjection() ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
                    float gap = max(sceneEye + TransformWorldToView(input.positionWS).z, 0);
                    // 물 면 바로 뒤에 둑·기둥·선체가 있으면(깊이 차가 작으면) 흰 거품. 거품 폭 안쪽은 꽉 차고 바깥으로 갈수록 줄무늬로 흩어진다.
                    float edge = 1 - saturate(gap / max(_FoamWidth, 0.01));
                    float ring = edge * (0.55 + 0.6 * stripes);
                    // 깊이가 얕을수록 투명(물 아래 바닥이 비친다), 깊을수록 불투명.
                    alpha = lerp(0.55, 0.96, saturate(gap / max(_DepthFade, 0.01)));
                    foam = saturate(max(foam * 0.6, ring));
                #endif
                color = lerp(color, _Foam.rgb, foam * 0.9);
                alpha = max(alpha, foam);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
