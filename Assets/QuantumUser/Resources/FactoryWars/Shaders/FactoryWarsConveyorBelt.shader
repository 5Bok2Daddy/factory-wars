Shader "FactoryWars/ExtractorConveyorBelt"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float seam = abs(frac(input.uv.y) - 0.5);
                float aa = max(fwidth(input.uv.y), 0.002);
                half groove = smoothstep(0.085 - aa, 0.085 + aa, seam);
                half grain = smoothstep(0.0, 0.14, min(input.uv.x, 1.0 - input.uv.x));
                half3 rubber = lerp(half3(0.04, 0.05, 0.055), half3(0.22, 0.24, 0.25), groove * grain);
                return half4(rubber, 1);
            }
            ENDHLSL
        }
    }
}
