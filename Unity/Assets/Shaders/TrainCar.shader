// Port of the train body material (rail.js makeBodyMat): stainless sides with
// stripe bands + corrugation, window band with per-cell passenger silhouettes,
// doors, dark glass, cab windshield + head/tail lamps + destination sign.
// Local space: x [-9.75, 9.75] along car, y 0.98..3.95 (rail top = 0), z width.
// Per-car uniforms: _Head (1 lead / 0 tail), _Stripe (rgb), _InstId (variety).
Shader "NEON KAIDO/TrainCar"
{
    Properties
    {
        _Head ("Head (1/0)", Float) = 1
        _Stripe ("Stripe", Color) = (0.05, 0.55, 0.22, 1)
        _InstId ("Instance Id", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
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
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Wetness;
            float _Rain;
            float _SimTime;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float _Head, _InstId;
                float4 _Stripe;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float3 localPos : TEXCOORD1; float3 nor : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                // Train cars are moved by Transform every frame — clip on the WORLD
                // position. Using IN.pos put every train car at the world origin
                // (the "train in the road" apparition).
                float3 wp = NkWorld(IN.pos.xyz);
                o.posCS = NkClip(wp);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = wp;
                o.localPos = IN.pos.xyz;
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                return o;
            }

            // three.js TSL hash (truncating)
            float NkTslHash(float seed)
            {
                uint state = (uint)floor(abs(seed) + 0.5) * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (float)((word >> 22u) ^ word) * (1.0 / 4294967296.0);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 p = IN.localPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);
                float x = p.x, y = p.y, pz = p.z;

                float side = smoothstep(0.55, 0.8, abs(N.z));

                // window band + cells
                float inBand = smoothstep(1.5, 1.53, y) * smoothstep(2.68, 2.65, y);
                float cellW = 1.25;
                float cx = x / cellW;
                float cid = floor(cx);
                float fx = frac(cx);
                float winX = smoothstep(0.1, 0.13, fx) * smoothstep(0.9, 0.87, fx);
                // doors at fixed x
                float doorD = min(min(abs(x + 7.0), abs(x + 2.35)), min(abs(x - 2.35), abs(x - 7.0)));
                float inDoor = step(doorD, 0.68);
                float doorGlass = smoothstep(0.62, 0.58, doorD) * smoothstep(0.03, 0.06, doorD) * smoothstep(1.45, 1.48, y) * smoothstep(2.7, 2.67, y);
                float ends = smoothstep(9.1, 9.0, abs(x));
                float glassSide = lerp(inBand * winX, doorGlass, inDoor) * ends * side;

                // passenger silhouettes (per cell + instance)
                float hcell = NkTslHash(cid + _InstId * 31.0 + _Stripe.x * 311.0 + step(0, pz) * 57.0);
                float occ = step(0.38, hcell);
                float standing = step(0.75, hcell);
                float px = frac(hcell * 13.7) * 0.5 + 0.25;
                float headY = lerp(1.98, 2.36, standing);
                float dxp = (fx - px) * cellW;
                float headD = length(float2(dxp, (y - headY) * 0.85));
                float headM = smoothstep(0.13, 0.11, headD);
                float bodyM = smoothstep(0.26, 0.22, abs(dxp)) * step(y, headY - 0.12);
                float sil = max(headM, bodyM) * occ;
                float h2 = frac(hcell * 91.3);
                float px2 = frac(h2 * 7.1) * 0.6 + 0.2;
                float d2 = (fx - px2) * cellW;
                float sil2 = max(smoothstep(0.1, 0.085, length(float2(d2, y - 2.32))),
                                 smoothstep(0.19, 0.16, abs(d2)) * step(y, 2.2)) * step(0.55, h2);
                float ceiling = smoothstep(2.5, 2.6, y);
                float3 interior = float3(1.0, 0.96, 0.88) * lerp(2.6, 5.0, ceiling);
                float silMix = max(sil, sil2 * 0.6);
                float3 winEmit = interior * lerp(1.0, 0.06, silMix);

                // body colour: stainless + stripe + corrugation + roof
                float stripe = smoothstep(1.02, 1.05, y) * smoothstep(1.36, 1.33, y)
                             + smoothstep(2.74, 2.77, y) * smoothstep(2.86, 2.83, y);
                float nz = NkVnoise(float2(x * 0.3 + pz, y * 4.0));
                float3 steel = float3(0.6, 0.62, 0.65) * (nz * 0.08 + 0.95);
                float corr = (sin(y * 60) * 0.5 + 0.5) * step(y, 1.45) * side;
                float3 base = lerp(steel * lerp(1.0, 0.85, corr), _Stripe.rgb, clamp(stripe * side, 0, 1));
                float roof = smoothstep(3.3, 3.6, y);
                base = lerp(base, float3(0.18, 0.18, 0.2), roof);

                float metal = lerp(lerp(0.85, 0.2, roof), 0.0, glassSide);
                float oRough = lerp(0.3, 0.05, glassSide);
                float3 col = lerp(base, float3(0.01, 0.01, 0.01), glassSide);

                float3 emis = winEmit * glassSide;

                // cab extras
                if (_InstId >= 100.0) // cab cars carry id 100+
                {
                    float nose = smoothstep(9.05, 9.3, x);
                    float wind = nose * smoothstep(2.0, 2.05, y) * smoothstep(3.25, 3.2, y) * smoothstep(1.2, 1.1, abs(pz));
                    float lampD = length(float2((abs(pz) - 1.0) * 1.4, y - 1.3));
                    float lamp = nose * smoothstep(0.17, 0.12, lampD);
                    float3 lampCol = lerp(float3(1.0, 0.04, 0.02) * 14, float3(1.0, 0.95, 0.85) * 60, _Head);
                    emis += float3(0.25, 0.32, 0.38) * wind * 0.8 + lampCol * lamp;
                    col = lerp(col, float3(0.01, 0.01, 0.01), max(wind, lamp));
                    float sign = nose * smoothstep(3.32, 3.35, y) * smoothstep(3.6, 3.57, y) * smoothstep(0.75, 0.7, abs(pz));
                    emis += float3(1.0, 0.55, 0.1) * 6 * sign;
                }

                // PBR lighting tail (as LandmarkMetal)
                float wet = clamp(_Wetness, 0, 1);
                col *= lerp(1.0, 0.75, wet * 0.5);
                float3 Ldir = normalize(_SkyLightDir.xyz);
                float3 H = normalize(Ldir + V);
                float ndl = max(dot(N, Ldir), 0);
                float ndh = max(dot(N, H), 0);
                float ndv = max(dot(N, V), 0.001);
                float a2 = oRough * oRough;
                float dnm = ndh * ndh * (a2 * a2 - 1) + 1;
                float D = a2 * a2 / (3.14159265 * dnm * dnm);
                float fres = 0.04 + 0.96 * pow(1 - ndv, 5);
                float spec = D / (4 * ndl * ndv + 0.001) * ndl * fres;
                float3 env = NkHazeColor(reflect(-V, N), _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz,
                    _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float envMix = exp(-oRough * 9.0);
                float3 oCol = col * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * col + spec * (metal > 0.5 ? 2 : 1) * 0.25)
                           + env * envMix * (0.35 + fres * 0.65) * lerp(1, col, metal);
                oCol += emis;

                oCol += NkBPointLights(N, wp, V, col, oRough, metal, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

// touch

// touch
