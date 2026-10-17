// Persistent user settings. Port of src/systems/player/settings.js — localStorage becomes PlayerPrefs.
namespace NeonKaido
{
    public static class Settings
    {
        public static readonly string[] QualityPresets = { "low", "medium", "high", "ultra" };
        const string Key = "nk.settings.v1";

        public static float Fov = 70f;
        public static float Sensitivity = 1.0f;
        public static bool InvertY, HeadBob = true, CameraSway = true, MinimapRotate = true, Hud = true;
        public static string Quality = "ultra";

        public static void Load()
        {
            Fov = UnityEngine.PlayerPrefs.GetFloat(Key + ".fov", 70f);
            Sensitivity = UnityEngine.PlayerPrefs.GetFloat(Key + ".sensitivity", 1f);
            InvertY = UnityEngine.PlayerPrefs.GetInt(Key + ".invertY", 0) != 0;
            HeadBob = UnityEngine.PlayerPrefs.GetInt(Key + ".headBob", 1) != 0;
            CameraSway = UnityEngine.PlayerPrefs.GetInt(Key + ".cameraSway", 1) != 0;
            MinimapRotate = UnityEngine.PlayerPrefs.GetInt(Key + ".minimapRotate", 1) != 0;
            Hud = UnityEngine.PlayerPrefs.GetInt(Key + ".hud", 1) != 0;
            Quality = UnityEngine.PlayerPrefs.GetString(Key + ".quality", "ultra");
        }

        public static void Set(string k, object v)
        {
            switch (k)
            {
                case "fov": Fov = (float)System.Convert.ToDouble(v); UnityEngine.PlayerPrefs.SetFloat(Key + ".fov", Fov); break;
                case "sensitivity": Sensitivity = (float)System.Convert.ToDouble(v); UnityEngine.PlayerPrefs.SetFloat(Key + ".sensitivity", Sensitivity); break;
                case "invertY": InvertY = (bool)v; UnityEngine.PlayerPrefs.SetInt(Key + ".invertY", InvertY ? 1 : 0); break;
                case "headBob": HeadBob = (bool)v; UnityEngine.PlayerPrefs.SetInt(Key + ".headBob", HeadBob ? 1 : 0); break;
                case "cameraSway": CameraSway = (bool)v; UnityEngine.PlayerPrefs.SetInt(Key + ".cameraSway", CameraSway ? 1 : 0); break;
                case "minimapRotate": MinimapRotate = (bool)v; UnityEngine.PlayerPrefs.SetInt(Key + ".minimapRotate", MinimapRotate ? 1 : 0); break;
                case "hud": Hud = (bool)v; UnityEngine.PlayerPrefs.SetInt(Key + ".hud", Hud ? 1 : 0); break;
                case "quality": Quality = (string)v; UnityEngine.PlayerPrefs.SetString(Key + ".quality", Quality); break;
            }
            UnityEngine.PlayerPrefs.Save();
        }

        public static object Get(string k)
        {
            switch (k)
            {
                case "fov": return Fov;
                case "sensitivity": return Sensitivity;
                case "invertY": return InvertY;
                case "headBob": return HeadBob;
                case "cameraSway": return CameraSway;
                case "minimapRotate": return MinimapRotate;
                case "hud": return Hud;
                case "quality": return Quality;
                default: return null;
            }
        }
    }
}
