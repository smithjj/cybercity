// NEON KAIDO sky — verbatim port of src/systems/atmosphere/atmo.js skyColor/hazeColor.
// Overcast low deck lit from below by the city, cloud deck from the 256x256 fBm noise,
// lightning lighting the deck around the strike direction. All colors LINEAR.
Shader "NEON KAIDO/Sky"
{
    Properties
    {
        _FogColor ("Fog Color (linear)", Vector) = (0.085, 0.058, 0.16, 1)
        _MidColor ("Mid Color (linear)", Vector) = (0.045, 0.034, 0.10, 1)
        _ZenithColor ("Zenith Color (linear)", Vector) = (0.010, 0.011, 0.030, 1)
        _GlowMagenta ("Glow Magenta (linear)", Vector) = (0.12, 0.025, 0.10, 1)
        _GlowOrange ("Glow Orange (linear)", Vector) = (0.13, 0.05, 0.018, 1)
        _GlowBlue ("Glow Blue (linear)", Vector) = (0.015, 0.04, 0.12, 1)
        _GroundHaze ("Ground Haze (linear)", Vector) = (0.07, 0.045, 0.12, 1)
        _SkyBrightness ("Sky Brightness", Float) = 1.0
        _CloudCover ("Cloud Cover", Float) = 0.5
        _Lightning ("Lightning", Float) = 0.0
        _LightningDir ("Lightning Dir", Vector) = (0.3, 0.35, -0.9, 0)
        _CloudOffset ("Cloud Offset", Vector) = (0, 0, 0, 0)
        _NoiseTex ("Noise Tex", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float4 _MidColor;
                float4 _ZenithColor;
                float4 _GlowMagenta;
                float4 _GlowOrange;
                float4 _GlowBlue;
                float4 _GroundHaze;
                float _SkyBrightness;
                float _CloudCover;
                float _Lightning;
                float4 _LightningDir;
                float4 _CloudOffset;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; };
            struct Varyings { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.pos = NkClip(IN.pos.xyz);
                o.worldPos = NkWorld(IN.pos.xyz);
                return o;
            }

            float3 HazeColor(float3 dir)
            {
                float e = dir.y;
                float h = max(e, 0.0);
                // JS: az = atan2(dir.x, -dir.z) in SIM space; here dir is UNITY space (z negated) => atan2(dir.x, dir.z).
                // Sim check: looking sim-north (unity +Z) gives 0; sim-east (+X) gives +PI/2.
                float az = atan2(dir.x, dir.z);
                float3 c = lerp(_FogColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.2, h));
                c = lerp(c, _ZenithColor.rgb, smoothstep(0.1, 0.7, h));
                float band = exp(-7.0 * h);
                float mag = pow(cos(az - 0.3) * 0.5 + 0.5, 2.0);
                float or1 = pow(cos(az + 1.9) * 0.5 + 0.5, 3.0);
                float or2 = pow(cos(az + 0.6) * 0.5 + 0.5, 10.0);
                float blu = pow(cos(az - 1.05) * 0.5 + 0.5, 8.0) + pow(cos(az + 2.4) * 0.5 + 0.5, 4.0);
                c += _GlowMagenta.rgb * band * (mag * 0.75 + 0.25);
                c += _GlowOrange.rgb * exp(-13.0 * h) * (or1 + or2 * 0.9);
                c += _GlowBlue.rgb * exp(-5.0 * h) * blu;
                // below the horizon: glowing ground haze of the city
                c = lerp(c, _GroundHaze.rgb + c * 0.5, 1.0 - smoothstep(-0.3, 0.0, e));
                return c * _SkyBrightness;
            }

            half4 frag(Varyings IN) : SV_TARGET
            {
                // JS background mesh: a unit sphere around the camera, normalWorldGeometry = view dir.
                float3 dir = normalize(IN.worldPos - _WorldSpaceCameraPos);
                float e = dir.y;
                float h = max(e, 0.0);
                float2 p = dir.xz / (h + 0.07) * 0.22 + _CloudOffset.xy;
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p * 2.3 + _CloudOffset.xy * 0.6 + float2(0.31, 0.77)).g;
                float n3 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p * 6.1 + float2(0.53, 0.19)).b;
                float n = n1 * 0.6 + n2 * 0.3 + n3 * 0.1;
                float dens = smoothstep(_CloudCover - 0.22, _CloudCover + 0.25, n);
                float3 haze = HazeColor(dir);
                float billow = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p * 3.7 + float2(0.11, 0.42)).a;
                float3 lit = haze * lerp(0.35, 1.5, dens) * (billow * 0.35 + 0.82);
                float fade = smoothstep(0.0, 0.2, h);
                float3 c = lerp(haze, lit, fade);
                // lightning: lights the cloud deck around the strike direction
                float3 lightningColor = float3(0.55, 0.62, 1.0);
                float ld = pow(max(dot(dir, normalize(_LightningDir.xyz)), 0.0), 5.0);
                c += lightningColor * _Lightning * (ld * 2.5 + 0.25) * (dens * 0.8 + 0.3);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
