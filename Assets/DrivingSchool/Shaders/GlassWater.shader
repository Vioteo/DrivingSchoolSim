// Water drops, film and snow on car glass (WindshieldRainView). URP, unlit, transparent.
// _WetMap  — wetness grid of the pane (R: 0 dry … 1 soaked), written by the CPU model every other frame.
// _DropTex — tiling drop atlas: RG = drop surface normal (0.5 = flat), B = wetness at which the drop appears, A = drop coverage.
// Drops refract the scene behind them (_CameraOpaqueTexture, requested on the view camera by WindshieldRainView)
// and get a rim and a highlight; with _Snow = 1 the same drops read as stuck flakes and the film as frost.
Shader "DrivingSchool/GlassWater"
{
    Properties
    {
        _WetMap ("Wetness grid", 2D) = "black" {}
        _DropTex ("Drop atlas", 2D) = "black" {}
        _DropTiling ("Drop atlas repeats (u, v)", Vector) = (4, 2, 0, 0)
        _Snow ("Snow", Range(0, 1)) = 0
        _Refraction ("Refraction strength", Float) = 0.035
        _Light ("Ambient brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+100" "IgnoreProjector" = "True" }
        Pass
        {
            Name "GlassWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_WetMap); SAMPLER(sampler_WetMap); float4 _WetMap_TexelSize;
            TEXTURE2D(_DropTex); SAMPLER(sampler_DropTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _WetMap_ST; float4 _DropTex_ST; float4 _DropTiling; float _Snow; float _Refraction; float _Light;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1.0), f.x), f.y);
            }

            // The CPU grid is coarse (≈1 cm cells): blur it and break the edge with noise so no square cells show.
            float Wetness(float2 uv)
            {
                float2 o = _WetMap_TexelSize.xy * 1.3;
                float w = SAMPLE_TEXTURE2D(_WetMap, sampler_WetMap, uv).r * 0.36;
                w += SAMPLE_TEXTURE2D(_WetMap, sampler_WetMap, uv + float2(o.x, o.y)).r * 0.16;
                w += SAMPLE_TEXTURE2D(_WetMap, sampler_WetMap, uv + float2(-o.x, o.y)).r * 0.16;
                w += SAMPLE_TEXTURE2D(_WetMap, sampler_WetMap, uv + float2(o.x, -o.y)).r * 0.16;
                w += SAMPLE_TEXTURE2D(_WetMap, sampler_WetMap, uv + float2(-o.x, -o.y)).r * 0.16;
                return w;
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float wet = Wetness(i.uv);
                float grain = ValueNoise(i.uv * _DropTiling.xy * 9.0) * 0.6 + ValueNoise(i.uv * _DropTiling.xy * 23.0) * 0.4;
                float4 d = SAMPLE_TEXTURE2D(_DropTex, sampler_DropTex, i.uv * _DropTiling.xy);
                float appear = saturate((wet - d.b) * 10.0);
                float drop = d.a * appear;
                float film = smoothstep(0.6, 1.0, wet + (grain - 0.5) * 0.35);
                float2 n = (d.rg * 2.0 - 1.0) * appear;
                float nl = saturate(length(n));
                float2 suv = i.screenPos.xy / max(i.screenPos.w, 1e-5);

                // Rain: a drop is a small lens — the scene behind it is displaced, the rim darkens, a highlight sits top-left.
                float3 bg = SampleSceneColor(suv + n * _Refraction * drop + (film * 0.004) * float2(sin(i.uv.y * 90.0), cos(i.uv.x * 70.0)));
                float3 lensed = SampleSceneColor(suv - n * _Refraction * 1.6);
                float spec = pow(saturate(1.0 - length(n - float2(-0.35, 0.45)) * 2.2), 6.0);
                float3 rainCol = lerp(bg, lensed * (1.0 - 0.6 * nl * nl * nl), drop) + spec * drop * 0.8 * _Light;
                float rainA = saturate(drop * 0.95 + film * 0.55);

                // Snow: flakes and frost scatter light instead of refracting it.
                float3 snowCol = float3(0.86, 0.9, 0.95) * _Light * (0.8 + 0.2 * (1.0 - nl));
                float snowA = saturate(d.a * saturate((wet - d.b * 0.8) * 6.0) * 0.92 + film * (0.25 + 0.35 * grain));

                float3 col = lerp(rainCol, snowCol, _Snow);
                float a = lerp(rainA, snowA, _Snow);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
