// Port of src/systems/landmarks/materials.js — texture loading + shared landmark
// material factories (surface / metal / emissive / basicEmissive). Materials use
// the LandmarkSurface / LandmarkMetal / LandmarkEmissive shaders; the wet uniform
// (_Wetness) is a global set by PlayerController like the other shaders.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class LmMats
    {
        const string TexDir = "Game/Textures/Landmarks";

        static readonly Dictionary<string, Texture2D> _tex = new();

        /// Load all landmark textures (srgb flags per materials.js).
        public static void LoadTextures()
        {
            void L(string k, string file) => _tex[k] = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/" + TexDir + "/" + file + ".png");
            foreach (var n in new[] { "concrete", "stonewall", "bark" })
            {
                L(n, n);
                L(n + "_n", n + "_n");
                L(n + "_r", n + "_r");
            }
            L("blossom", "blossom_cards_a");
            L("ads", "holo_ads");
            L("ads2", "holo_ads2");
            L("ads3", "holo_ads3");
            L("petals", "petals_a");
            L("wood", "wood_slats");
            L("wood_slats_n", "wood_slats_n");
            L("wood_slats_r", "wood_slats_r");
        }

        public static Texture2D Tex(string name)
            => _tex.TryGetValue(name, out var t) ? t : null;

        public static Material ShaderMat(string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null) { Debug.LogError("[LmMats] missing shader " + shaderName); return null; }
            return new Material(sh);
        }

        /// surface() port. texBase e.g. "concrete" -> concrete.png/_n.png/_r.png.
        public static Material Surface(string texBase, Color? tint = null, float uvScale = 1,
            float normalScale = 1, float macro = 0.5f, float macroFreq = 0.045f,
            float streaks = 0.7f, float roughMul = 1, float rough = 0.8f,
            float wetDark = 0.62f, float wetVert = 0.45f, float wetRough = 0.06f,
            float metal = 0, bool detail = true)
        {
            var m = ShaderMat("NEON KAIDO/LandmarkSurface");
            if (m == null) return null;
            m.SetTexture("_MainTex", Tex(texBase));
            m.SetTexture("_BumpMap", Tex(texBase + "_n"));
            m.SetTexture("_RoughMap", Tex(texBase + "_r"));
            m.SetColor("_Tint", tint ?? Color.white);
            m.SetFloat("_UvScale", uvScale);
            m.SetFloat("_NormalScale", normalScale);
            m.SetFloat("_Macro", macro);
            m.SetFloat("_MacroFreq", macroFreq);
            m.SetFloat("_Streaks", streaks);
            m.SetFloat("_RoughMul", roughMul);
            m.SetFloat("_Rough", rough);
            m.SetFloat("_WetDark", wetDark);
            m.SetFloat("_WetVert", wetVert);
            m.SetFloat("_WetRough", wetRough);
            m.SetFloat("_Metal", metal);
            m.SetFloat("_Detail", detail ? 1 : 0);
            return m;
        }

        /// metal() port — painted/bare metal with procedural grime, no textures.
        public static Material Metal(Color? color = null, float rough = 0.45f, float metal = 0.85f, float freq = 0.8f)
        {
            var m = ShaderMat("NEON KAIDO/LandmarkMetal");
            if (m == null) return null;
            m.SetColor("_Tint", color ?? new Color(0.2f, 0.2f, 0.22f));
            m.SetFloat("_Rough", rough);
            m.SetFloat("_Metal", metal);
            m.SetFloat("_Freq", freq);
            return m;
        }

        /// emissiveMat() port — dark body with emissive rgb*intensity.
        public static Material Emissive(Color rgb, float intensity = 3)
        {
            var m = ShaderMat("NEON KAIDO/LandmarkEmissive");
            if (m == null) return null;
            m.SetColor("_Emissive", rgb);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Unlit", 0);
            return m;
        }

        /// basicEmissive() port — unlit rgb*intensity.
        public static Material BasicEmissive(Color rgb, float intensity = 3)
        {
            var m = ShaderMat("NEON KAIDO/LandmarkEmissive");
            if (m == null) return null;
            m.SetColor("_Emissive", rgb);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Unlit", 1);
            return m;
        }
    }
}
