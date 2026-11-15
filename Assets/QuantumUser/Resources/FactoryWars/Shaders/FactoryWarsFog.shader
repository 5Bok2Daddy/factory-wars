// 항목 34-A 전장의 안개 판: 텍스처 알파만큼 검게 덮는다(조명 없음, 깊이 쓰기·검사 없음). 텍스처는 96x96이고 쌍선형으로 늘어난다.
Shader "FactoryWars/Fog"
{
    Properties
    {
        _BaseMap ("Fog (alpha)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // 고지대 둔덕·건물처럼 안개 판(0.32m)보다 높은 것도 그 자리 안개만큼 어둡게: 깊이 검사 없이 위에 덮는다.
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                return half4(0.02, 0.03, 0.05, alpha);
            }
            ENDHLSL
        }
    }
}
