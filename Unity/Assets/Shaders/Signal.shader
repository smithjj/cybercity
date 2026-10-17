// Traffic + pedestrian signal lenses — port of signage/street.js signal mesh.
// Per-vertex sig = (phase, lamp): lamp 0/1/2 = car G/Y/R, 3/4 = ped R/G (icon from color atlas).
// State driven by globals: _SigA (avenue car), _SigC (cross car), _SigPA, _SigPC (ped): 0 green, 1 yellow, 2 red.
Shader "NEON KAIDO/Signal"
{
    Properties { _MainTex ("tex", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }
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
            float4 _SigA; // x = car state 0/1/2
            float4 _SigC;
            float4 _SigPA; // x = ped state 0 walk, 1 blink, 2 stop
            float4 _SigPC;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 sig : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 sig : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                OUT.uv = IN.uv;
                OUT.sig = IN.sig;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float phase = IN.sig.x, lamp = IN.sig.y;
                bool isPed = lamp > 2.5;
                float carState = phase < 0.5 ? _SigA.x : _SigC.x;
                float pedSt = phase < 0.5 ? _SigPA.x : _SigPC.x;
                bool on;
                if (isPed)
                {
                    // lamp 3 = red man (stop), lamp 4 = green walker (walk / blink)
                    on = lamp < 3.5 ? (pedSt > 1.5) : (pedSt < 0.5);
                    // blink phase handled on CPU: state oscillates 0/1; green shows only when 0
                }
                else
                {
                    on = lamp < 0.5 ? (carState < 0.5) : (lamp < 1.5 ? (carState > 0.5 && carState < 1.5) : carState > 1.5);
                }
                float3 col = lamp < 0.5 ? float3(0.05, 1.0, 0.55) : (lamp < 1.5 ? float3(1.0, 0.55, 0.05) : (lamp < 2.5 ? float3(1.0, 0.06, 0.03) : float3(1, 1, 1)));
                float3 cm = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb;
                float3 icon = cm * cm;
                float3 base = isPed ? icon * 0.05 : col * 0.04;
                float3 e = isPed ? icon * 4.0 : col * 7.0;
                float3 P = IN.worldPos;
                float3 N = IN.normalWS;
                float3 V = normalize(P - _WorldSpaceCameraPos);
                float ndl = dot(N, normalize(_SkyLightDir));
                float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                float3 colOut = base * (hemi + _SkyLightColor * diff) * _SkyBrightness;
                colOut += NkBPointLights(N, P, V, base, 0.2, 0, 1);
                colOut += e * (on ? 1 : 0.02);
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                colOut = NkFogApply(colOut, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(colOut, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
