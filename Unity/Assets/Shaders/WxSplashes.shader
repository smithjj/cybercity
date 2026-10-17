// Impact splashes — port of src/systems/weather/splashes.js (analytic variant).
// 32768 slots around the camera; each slot cycles: a thin crown sheet (cylindrical billboard)
// + 5 ballistic bounce droplets drawn as tiny motion streaks. Surface height comes from the
// baked heightfield so splashes land on roofs/awnings/street, not just y=0.
Shader "NEON KAIDO/WxSplashes"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

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
            float _Wetness;
            float _Rain;
            float _SimTime;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"
            #include "Assets/Shaders/WxCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 corner : TEXCOORD0;  // (±0.5, ±0.5)
                float2 slot : TEXCOORD1;    // x = slot index, y = part (0 crown, 1..5 droplets)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float2 corner : TEXCOORD1;
                float3 col : TEXCOORD2;
                float alpha : TEXCOORD3;
                float part : TEXCOORD4;
                float seed : TEXCOORD5;
                float age : TEXCOORD6;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float fi = IN.slot.x;
                float part = IN.slot.y;
                const float life = 0.32;
                // slot position: hashed point within 40 m of the camera, on the baked surface
                float px = _WxCam.x + (WxHash(fi * 3.7 + 1.3) - 0.5) * 80.0;
                float pz = _WxCam.z + (WxHash(fi * 5.1 + 7.7) - 0.5) * 80.0;
                float h = WxHeight(float2(px, pz));
                float3 origin = float3(px, h, pz);
                float radius = lerp(0.0009, 0.0024, WxHash(fi * 5.11 + 3.7) * WxHash(fi * 5.11 + 3.7));
                float rs = clamp(radius / 0.002, 0.4, 3.0);
                float seed = WxHash(fi * 0.977 + 2.1);
                float cyc = WxFmod(_WxTime + WxHash(fi * 1.9 + 3.3) * 37.0, 0.9);
                float gate = WxHash(fi * 7.7 + floor((_WxTime + WxHash(fi * 1.9 + 3.3) * 37.0) / 0.9));
                bool alive = cyc < life && gate < 0.55 * saturate(_WxRain * 1.4);
                float a = saturate(cyc / life);
                float dCam = length(_WxCam - origin);
                float3 outP = float3(0, -10000, 0);
                float alpha = 0;
                if (part < 0.5)
                {
                    // crown: cylindrical billboard standing on the surface
                    float3 toCam = _WxCam - origin;
                    float3 side = normalize(float3(toCam.z, 0, -toCam.x) + float3(1e-6, 0, 0));
                    float w = sqrt(rs) * 0.11 * (sqrt(a) * 0.9 + 0.3);
                    float hgt = sqrt(rs) * 0.07 * smoothstep(0.0, 0.15, a) * (1.0 - a * 0.6);
                    float pixMin = dCam * _WxPixK * 2.5;
                    float ww = max(w, pixMin);
                    float hh = max(hgt, pixMin * 0.6);
                    outP = origin + side * (IN.corner.x * ww) + float3(0, (IN.corner.y + 0.5) * hh, 0);
                    alpha = pow(1.0 - a, 1.6) * (min(w / ww, 1.0) * 0.6 + 0.4);
                }
                else
                {
                    // bounce droplet k: ballistic arc, tiny motion streak
                    float k = part;
                    float ang = seed * 6.2831 + k * (6.2831 / 5.0) + WxHash(seed + k) * 0.9;
                    float hs = WxHash(seed * 3.1 + k * 1.7);
                    float spd = sqrt(rs) * (hs * 0.9 + 0.5);
                    float up = sqrt(rs) * (WxHash(seed + k * 5.3) * 1.3 + 0.9);
                    float3 v0 = float3(cos(ang) * spd, up, sin(ang) * spd);
                    float3 p = origin + v0 * cyc + float3(0, -4.9, 0) * (cyc * cyc);
                    float3 vel = v0 + float3(0, -9.8, 0) * cyc;
                    float dr = max(radius * 0.35, 0.0004);
                    float3 world; float coverage; float dist;
                    WxStreak(p, vel * 0.03, dr, 0.7, IN.corner, world, coverage, dist);
                    outP = world;
                    alpha = coverage * (1.0 - a) * (p.y > origin.y - 0.01 ? 1.0 : 0.0);
                }
                float distFade = 1.0 - smoothstep(18.0, 32.0, dCam);
                float lit = WxSoftClamp(WxLightAt(origin, normalize(_WxCam - origin), 0.75, 0.18) * 0.10, 0.25);
                float3 c = _WxAmbient * 2.5 + float3(0.05, 0.05, 0.07) + lit
                         + float3(0.55, 0.6, 0.75) * _WxLightning * 2.5;
                OUT.worldPos = outP;
                OUT.corner = IN.corner;
                OUT.col = c;
                OUT.alpha = alive ? alpha * distFade * (_WxRain * 0.7 + 0.3) : 0.0;
                OUT.part = part;
                OUT.seed = seed;
                OUT.age = a;
                OUT.positionCS = NkClip(outP);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float x = IN.corner.x * 2.0;         // -1..1
                float uy = IN.corner.y + 0.5;        // 0..1
                float shapeA;
                if (IN.part < 0.5)
                {
                    // crown silhouette: elliptical rim ring + thin walls + impact bead
                    float rimY = 0.82 - IN.age * 0.25;
                    float ry = 0.16;
                    float e = x * x + (uy - rimY) * (uy - rimY) / (ry * ry);
                    float spikes = sin(x * 19.0 + IN.seed * 40.0) * 0.5 + 0.5;
                    float rimA = smoothstep(0.32, 0.0, abs(e - 1.0)) * (spikes * 0.5 + 0.5);
                    float wallEdge = abs(x) - pow(max(uy / rimY, 0.0), 0.7);
                    float wallA = smoothstep(0.18, 0.0, abs(wallEdge)) * step(uy, rimY) * 0.55;
                    float bead = smoothstep(0.35, 0.0, length(float2(x * 1.4, uy * 3.0))) * smoothstep(0.35, 0.0, IN.age) * 1.5;
                    shapeA = max(max(rimA, wallA), bead);
                }
                else
                {
                    float across = 1.0 - abs(x);
                    float along = smoothstep(0.0, 0.3, uy) * smoothstep(1.0, 0.7, uy);
                    shapeA = across * across * along;
                }
                float a = clamp(IN.alpha * shapeA, 0.0, 1.0);
                if (a < 0.002) discard;
                float3 col = IN.col * 4.0 + _WxAmbient * 2.0;
                col = NkFogApply(col, IN.worldPos, _WorldSpaceCameraPos.xyz, 1.0, _Rain * 0.35 + 0.75,
                    _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz,
                    _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(col, IN.screenUV, _SimTime), a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
