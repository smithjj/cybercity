// Minimap window blit: samples the baked city map as a rotated, scaled window centred on the
// player. The rotation is done on the GPU because IMGUI's BeginClip stops clipping once
// GUI.matrix is anything but identity; the resulting RenderTexture is drawn unrotated.
Shader "Hidden/NEON KAIDO/MinimapRotate"
{
    Properties
    {
        _MainTex ("Map", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _TexSize;   // baked map size in px
            float4 _Player;    // player position in map px
            float4 _Rot;       // cos(rot), sin(rot)
            float4 _Size;      // canvas size in px
            float _Scale;      // map px -> screen px

            fixed4 frag (v2f_img i) : SV_Target
            {
                // GUI space: y down, (0,0) top-left of the canvas
                float2 d = float2(i.uv.x * _Size.x - _Size.x * 0.5,
                                  (1.0 - i.uv.y) * _Size.y - _Size.y * 0.5);
                // inverse of T(centre) * R(rot) * S(k) * T(-player)
                float2 m = _Player.xy + float2(d.x * _Rot.x + d.y * _Rot.y,
                                              -d.x * _Rot.y + d.y * _Rot.x) / _Scale;
                float2 uv = float2(m.x / _TexSize.x, 1.0 - m.y / _TexSize.y);
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}
