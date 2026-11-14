// 항목 36 수로 물 표면(우리 쪽 셰이더, 구입한 팩에는 물 재질이 없다). 얕은 청록 ↔ 깊은 파랑이 월드 좌표 노이즈로 천천히 흐르고,
// 정점 색 r이 1인 띠(강둑 가장자리)에는 흰 거품이 줄무늬로 흐른다. 조명 없이 단색으로 그린다(밝고 깨끗한 물). 머티리얼 하나를 모든 물이 공유한다.
Shader "FactoryWars/UrbanWater"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.36, 0.70, 0.80, 1)
        _Deep ("Deep", Color) = (0.18, 0.50, 0.78, 1)
        _Foam ("Foam", Color) = (0.96, 0.99, 1, 1)
        _Flow ("Flow (xz speed)", Vector) = (0.06, 0.03, 0, 0)
        _Scale ("Pattern scale", Float) = 0.11
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow;
                half4 _Deep;
                half4 _Foam;
                float4 _Flow;
                float _Scale;
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
                float n = WNoise(p) * 0.6 + WNoise(p * 2.3 + float2(t * 0.05, 7.0)) * 0.4;
                half3 color = lerp(_Deep.rgb, _Shallow.rgb, saturate(n * 1.25));
                // 잔물결 하이라이트: 좁은 띠가 흐른다.
                float ripple = smoothstep(0.82, 0.95, WNoise(p * 3.7 - float2(t * 0.12, 0)));
                color += ripple * 0.10;
                // 가장자리 거품: 정점 색 r이 거품 띠 안쪽 1, 바깥쪽 0. 줄무늬가 둑을 따라 흐른다.
                float stripes = WNoise(float2(input.positionWS.x * 0.35 + input.positionWS.z * 0.35, t * 0.6) * 1.7);
                half foam = saturate(input.color.r * (0.55 + 0.8 * stripes));
                color = lerp(color, _Foam.rgb, foam * 0.85);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
