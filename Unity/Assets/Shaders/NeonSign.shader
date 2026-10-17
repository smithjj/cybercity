// Facade sign faces — port of signage/materials.js makeFaceMaterial + flickerNode.
// ONE material for all sign faces; per-vertex attributes:
//   uv    atlas uv (neon or colour atlas)
//   COLOR tint (neon colour or multiplier)
//   UV2   sp = (mode, intensity, seed, flickerType)  [modes 6..11 = open variants, no backing panel]
//   UV3   back = backing panel albedo
//   UV4   luv = (x, y, width, height) in metres of the face corner
// modes: 0 neon tube, 1 colour light box, 2 solid emissive strip, 3 painted print (unlit),
//        4 LED channel letters, 5 screen/pixel grid.
Shader "NEON KAIDO/NeonSign"
{
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float3 _SkyLightDir;
            float3 _SkyLightColor;
            float3 _AmbientSky;
            float3 _AmbientGround;
            float3 _FogColor;
            float3 _MidColor;
            float3 _ZenithColor;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Rain;
            float _SimTime;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_NeonTex); SAMPLER(sampler_NeonTex);
            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            // three.js TSL hash — seed.toUint() truncates toward zero
            float NkTslHash(float seed)
            {
                uint state = (uint)floor(abs(seed) + 0.5) * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (float)((word >> 22u) ^ word) * (1.0 / 4294967296.0);
            }

            // type 0 steady, 1 stutter, 2 broken segments, 3 chase, 4 slow breathe
            float NkFlicker(float seed, float type, float seg)
            {
                float t = _SimTime;
                float burstWin = NkTslHash(floor(t * 0.7) + seed * 7919);
                float fast = NkTslHash(floor(t * 23.0) + seed * 4999 + seg * 1597);
                float stutter = burstWin > 0.72 ? (step(0.38, fast) * 0.92 + 0.08) : 1.0;
                float segH = NkTslHash(seg * 613 + seed * 2851);
                float segDead = step(0.16, segH);
                float segFlick = segH < 0.3 ? step(0.45, NkTslHash(floor(t * 15.0) + seg * 977 + seed * 31)) : 1.0;
                float broken = max(segDead * segFlick, 0.025);
                float ph = frac(t * 0.45 - seg * 0.11 + seed * 0.137);
                float chase = max(smoothstep(0.0, 0.08, ph) * step(ph, 0.72), 0.05);
                float breathe = sin(t * 1.7 + seed * 0.4) * 0.18 + 0.86;
                if (type < 0.5) return 1.0;
                if (type < 1.5) return stutter;
                if (type < 2.5) return broken * (stutter * 0.3 + 0.7);
                if (type < 3.5) return chase;
                return breathe;
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 sp : TEXCOORD1;
                float3 back : TEXCOORD2;
                float4 luv : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 tint : TEXCOORD3;
                float4 sp : TEXCOORD4;
                float3 back : TEXCOORD5;
                float4 luv : TEXCOORD6;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                OUT.uv = IN.uv;
                OUT.tint = IN.color;
                OUT.sp = IN.sp;
                OUT.back = IN.back;
                OUT.luv = IN.luv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 tint = IN.tint.rgb;
                float4 sp = IN.sp;
                float rawMode = sp.x, inten = sp.y, seed = sp.z, ftype = sp.w;
                bool open = rawMode > 5.5;
                float mode = open ? rawMode - 6.0 : rawMode;

                float4 nm = SAMPLE_TEXTURE2D(_NeonTex, sampler_NeonTex, IN.uv);
                float4 cm = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, IN.uv);
                float tube = nm.r, halo = nm.g;
                float seg = floor(nm.b * 255.0 + 0.5);

                float on = NkFlicker(seed, ftype, seg);
                float ftypeBox = (ftype > 1.5 && ftype < 2.5) ? 1.0 : ftype;
                float onBox = NkFlicker(seed, ftypeBox, 0.0);

                // neon tube: crisp edge, colour body, white-hot core
                float edge = smoothstep(0.1, 0.42, tube);
                float core = smoothstep(0.62, 0.95, tube);
                float3 tubeCol = tint * edge + float3(1.0, 0.95, 0.92) * core * 0.55 + tint * core * 0.6;
                float3 unlitGlass = float3(0.05, 0.05, 0.055) * edge;
                float3 neonE = tubeCol * inten * on + tint * halo * inten * on * 0.06;

                // light box: diffuser + tube banding + vignette + grime
                float lx = IN.luv.x, ly = IN.luv.y;
                float lw = max(IN.luv.z, 0.01), lh = max(IN.luv.w, 0.01);
                float edgeD = min(min(lx, lw - lx), min(ly, lh - ly));
                float vign = smoothstep(0.0, 0.14, edgeD) * 0.4 + 0.6;
                float across = lw > lh ? ly : lx;
                float band = cos(across * (3.14159 * 2.0 / 0.24)) * 0.07 + 0.93;
                float cellN = NkTslHash(floor(lx * 7.0) + floor(ly * 7.0) * 131 + seed * 17);
                float dirt = (1.0 - smoothstep(0.35, 0.0, ly) * 0.3) * (cellN * 0.12 + 0.88);
                float3 boxE = cm.rgb * cm.rgb * tint * inten * band * vign * dirt * onBox;

                float3 chanE = tint * smoothstep(0.25, 0.6, tube) * inten * on + tint * halo * inten * on * 0.05;
                float3 solidE = tint * inten * onBox;
                float3 scrE = cm.rgb * cm.rgb * inten * onBox;

                float3 emissiveN = neonE;
                if (mode > 0.5 && mode < 1.5) emissiveN = boxE;
                else if (mode > 1.5 && mode < 2.5) emissiveN = solidE;
                else if (mode > 2.5 && mode < 3.5) emissiveN = cm.rgb * cm.rgb * inten;
                else if (mode > 3.5 && mode < 4.5) emissiveN = chanE;
                else if (mode > 4.5) emissiveN = scrE;

                float3 albedo = (mode < 0.5 || (mode > 3.5 && mode < 4.5)) ? IN.back + unlitGlass
                    : (mode > 2.5 && mode < 3.5) ? cm.rgb * cm.rgb * tint
                    : float3(0.02, 0.02, 0.02);

                // discard open-variant pixels where there is no tube art
                if (open && tube <= 0.07) discard;

                // basic lighting on the dark backing + emissive
                float3 N = IN.normalWS;
                float3 P = IN.worldPos;
                float3 V = normalize(P - _WorldSpaceCameraPos);
                float ndl = saturate(dot(N, normalize(_SkyLightDir)));
                float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                float metal = (mode < 0.5 || (mode > 3.5 && mode < 4.5)) ? 0.35 : 0.0;
                float rough = (mode > 2.5 && mode < 3.5) ? 0.55 : 0.35;
                float3 col = albedo * (hemi + _SkyLightColor * diff) * _SkyBrightness;
                col += NkBPointLights(N, P, V, albedo, rough, metal, 1);
                col += emissiveN;

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(col, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
