// 항목 36 시각 교정: 점령 거점 표시 링. 조명 없는 반투명 발광(시안·흰색), 깊이 쓰기 없음, 바닥과 겹쳐 깨지지 않게 깊이 오프셋.
Shader "FactoryWars/CaptureRing"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.95, 1, 0.78)
        _Glow ("Glow", Float) = 1.8
        _Pulse ("Pulse", Float) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+1" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Glow;
                float _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float pulse = 1.0 + _Pulse * sin(_Time.y * 2.2);
                return half4(_Color.rgb * _Glow * pulse, _Color.a);
            }
            ENDHLSL
        }
    }
}
