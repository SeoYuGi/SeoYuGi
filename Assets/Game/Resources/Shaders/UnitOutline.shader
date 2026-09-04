// 유닛 외곽선 — 뒤집힌 껍질(Cull Front) + 월드 단위 노멀 확장. URP 전용.
// 렌더러의 머티리얼 배열 끝에 추가 패스로 얹는다 (UnitOutlineApplier) — 스킨드 메시도 그대로 먹는다.
// Resources 아래에 두는 이유: 머티리얼 에셋이 참조하지 않아도 빌드에 포함되고 Shader.Find가 된다.
Shader "SeoYuGi/UnitOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.05, 0.04, 0.06, 1)
        _OutlineWidth ("Outline Width (world units)", Float) = 0.055
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // 월드 공간에서 밀어야 모델마다 스케일이 달라도 선 두께가 같다
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                posWS += nWS * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
