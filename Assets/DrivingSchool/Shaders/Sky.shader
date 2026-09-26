// Procedural sky for WeatherController: day gradient to the fog colour at the horizon, sun and moon discs, and at night
// a star field (many faint stars, few bright ones, slight colour spread and twinkle near the horizon), a faint
// Milky Way band and warm light-pollution glow over the horizon. All parameters are set from C# every weather change.
Shader "DrivingSchool/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.25, 0.45, 0.8, 1)
        _HorizonColor ("Horizon (fog colour)", Color) = (0.6, 0.7, 0.8, 1)
        _GroundColor ("Below horizon", Color) = (0.3, 0.3, 0.3, 1)
        _GlowColor ("Light pollution glow", Color) = (0.2, 0.12, 0.06, 1)
        _Stars ("Star visibility", Range(0, 1)) = 0
        _StarBrightness ("Star brightness", Float) = 1.6
        _SunDir ("Sun direction", Vector) = (0, 0.7, 0.7, 0)
        _SunColor ("Sun colour (alpha = disc visibility)", Color) = (1, 0.95, 0.85, 1)
        _MoonDir ("Moon direction", Vector) = (0.3, 0.45, -0.8, 0)
        _Moon ("Moon visibility", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _HorizonColor, _GroundColor, _GlowColor, _SunDir, _SunColor, _MoonDir;
                float _Stars, _StarBrightness, _Moon;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash1(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float4 Hash4(float3 p)
            {
                return float4(Hash1(p), Hash1(p + 17.31), Hash1(p + 41.7), Hash1(p + 73.13));
            }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash1(i), Hash1(i + float3(1, 0, 0)), f.x), lerp(Hash1(i + float3(0, 1, 0)), Hash1(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash1(i + float3(0, 0, 1)), Hash1(i + float3(1, 0, 1)), f.x), lerp(Hash1(i + float3(0, 1, 1)), Hash1(i + 1.0), f.x), f.y), f.z);
            }
            float Fbm(float3 p) { float a = 0.5, s = 0; for (int k = 0; k < 4; k++) { s += a * Noise(p); p *= 2.03; a *= 0.5; } return s; }

            // One star layer on a cube-face grid. Stars are placed away from cell borders and are a few pixels wide,
            // so a single cell lookup is enough; width is taken from screen derivatives, so stars stay pixel-sharp.
            float3 StarLayer(float3 d, float cells, float density, float sizePx, float twinkle)
            {
                float3 a = abs(d);
                float2 uv; float face;
                if (a.x >= a.y && a.x >= a.z) { uv = d.yz / a.x; face = d.x > 0 ? 0 : 1; }
                else if (a.y >= a.z) { uv = d.xz / a.y; face = d.y > 0 ? 2 : 3; }
                else { uv = d.xy / a.z; face = d.z > 0 ? 4 : 5; }
                float2 g = uv * cells;
                float2 cell = floor(g);
                float4 h = Hash4(float3(cell, face * 131.0 + cells));
                if (h.x > density) return 0;
                float2 centre = cell + 0.25 + 0.5 * h.yz;
                float px = max(fwidth(g.x), fwidth(g.y));
                float dist = length(g - centre) / max(px, 1e-5);
                float mag = pow(h.w, 7.0);                               // most stars faint, few bright
                float b = (0.08 + 2.6 * mag) * exp(-dist * dist / (sizePx * sizePx * (0.6 + mag)));
                b *= 1.0 + twinkle * sin(_Time.y * (2.0 + 6.0 * h.y) + h.z * 40.0);
                float3 tint = lerp(float3(1.0, 0.82, 0.66), float3(0.78, 0.86, 1.0), h.z);
                tint = lerp(float3(1, 1, 1), tint, 0.55);
                return tint * b;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;

                // Base gradient: horizon = fog colour so distant geometry melts into the sky.
                float t = pow(saturate(up), 0.45);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                col = up < 0 ? lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-up * 6.0)) : col;

                // Sun: disc + glow (day), moon: disc + halo (night).
                float3 sd = normalize(_SunDir.xyz);
                float cs = dot(d, sd);
                col += _SunColor.rgb * _SunColor.a * (smoothstep(0.99985, 0.9999, cs) * 12.0 + pow(saturate(cs), 350.0) * 0.6 + pow(saturate(cs), 12.0) * 0.12);

                float3 md = normalize(_MoonDir.xyz);
                float cm = dot(d, md);
                float disc = smoothstep(0.99993, 0.99996, cm);
                float mottled = 0.8 + 0.2 * Noise(d * 2400.0);
                col += _Moon * (float3(0.95, 0.96, 1.0) * disc * 2.2 * mottled + float3(0.35, 0.4, 0.5) * pow(saturate(cm), 900.0) * 0.25 + float3(0.1, 0.12, 0.16) * pow(saturate(cm), 40.0) * 0.08);

                // Night sky.
                if (_Stars > 0.001 && up > -0.02)
                {
                    float extinction = saturate(up * 5.0);                // stars fade into the haze near the horizon
                    float moonWash = 1.0 - 0.6 * _Moon * pow(saturate(cm), 4.0);
                    float3 n = normalize(float3(0.35, 0.62, 0.7));         // Milky Way plane
                    float band = exp(-pow(dot(d, n), 2.0) / 0.018);
                    float clouds = Fbm(d * 5.0), lanes = Fbm(d * 11.0 + 3.1);
                    float3 milky = float3(0.55, 0.6, 0.75) * band * saturate(clouds * 1.4 - 0.35) * (0.55 + 0.45 * smoothstep(0.35, 0.65, lanes));
                    // Densities per cube-face cell: ≈2–3 thousand visible stars over the sky, most of them at the limit of sight.
                    float3 stars = StarLayer(d, 220.0, 0.035 + 0.06 * band, 0.6, 0.0) * 0.45
                                 + StarLayer(d, 70.0, 0.07, 0.75, 0.25 * (1.0 - extinction))
                                 + StarLayer(d, 22.0, 0.12, 0.9, 0.3 * (1.0 - extinction)) * 1.4;
                    col += _Stars * extinction * moonWash * (stars * _StarBrightness * 0.5 + milky * 0.035);
                }

                // Light pollution: warm glow sitting on the horizon (towns around the range).
                col += _GlowColor.rgb * exp(-max(up, 0.0) * 9.0) * (up > -0.05 ? 1.0 : 0.0);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
