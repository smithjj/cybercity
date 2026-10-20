// Editor-side driver for the two things the port's lighting needs that Unity cannot serialize:
// the per-material point-light arrays (_BLPos/_BLColor/_BLCount, uploaded with SetVectorArray,
// which .mat assets never persist) and the atmosphere/fog shader globals (published by
// Atmo.PushGlobals, which only ran from PlayerController.Update).
//
// Without this, opening Assets/Main.unity shows the world as the shaders see it with everything at
// its default: _BLCount = 0, so NkBPointLights() breaks out of its loop on the first iteration and
// the city has no point lights at all — street lamps are visibly lit (the lens is emissive in the
// material) but cast nothing, and every sign's backing panel goes black. The fog globals are 0 too,
// which makes distance fall to black and the sky blow out. It only looked right in Play mode
// because PlayerController.Start pushed both.
//
// The same arrays are also dropped mid-Play by any asset import: writing a file under Assets/
// (which is how the Editor's capture helpers save their PNGs) reimports the dirty .mat assets and
// wipes every runtime-set property, so the city silently goes dark a second into a capture session.
// AssetPostprocessor notices the import; the push itself is deferred to the update tick.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonKaido.EditorTools
{
    [InitializeOnLoad]
    public static class NkEditorLighting
    {
        const double IntervalSeconds = 0.75;

        static double _next;
        static bool _dirty = true;
        static bool _camValid;
        static Vector3 _lastCam;
        static bool _lightCamValid;
        static Vector3 _lastLightCam;

        static NkEditorLighting()
        {
            EditorApplication.update += Tick;
            EditorApplication.hierarchyChanged += MarkDirty;
            EditorSceneManager.sceneOpened += (s, m) => MarkDirty();
            EditorSceneManager.sceneSaved += s => MarkDirty();
            EditorSceneManager.activeSceneChangedInEditMode += (a, b) => MarkDirty();
        }

        static void MarkDirty() => _dirty = true;

        /// Called from an AssetPostprocessor whenever anything under Assets/ is imported.
        public static void RequestRefresh() => MarkDirty();

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + IntervalSeconds;
            // The weather rig follows the camera in Play via WeatherDriver.Update; in edit mode
            // the rain volume and the steam billboard basis must be moved by hand or they stay
            // behind at whatever pose the last push happened to see.
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Camera.main != null)
            {
                var p = Camera.main.transform.position;
                if (!_camValid || (p - _lastCam).sqrMagnitude > 1e-4f)
                {
                    _camValid = true; _lastCam = p;
                    WeatherDriver.PushEditorCamera(p);
                }
                // The 384-light pool is distance-ranked: after moving between views (overlook ->
                // alley) the old neighbourhood must hand its slots to the new one, or the view is
                // lit by lights that are no longer in frame. Re-rank after ~25 m of camera travel.
                if (!_lightCamValid || (p - _lastLightCam).sqrMagnitude > 625f)
                {
                    _lightCamValid = true; _lastLightCam = p;
                    BuildingsRuntime.PushSceneLights(p);
                }
            }
            if (!_dirty) return;
            _dirty = false;
            Push();
        }

        [MenuItem("NEON KAIDO/Refresh editor lighting")]
        public static void Push()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            // In Play mode PlayerController owns the atmosphere globals (they track weather and the
            // clock); only the light arrays need restoring.
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // The shaders animate off _SimTime; a fixed value keeps edits from flickering.
                Shader.SetGlobalFloat("_SimTime", 0f);
                Atmo.PushGlobals();
                // Runtime weather textures (puff atlas / heightfield) and the per-frame weather
                // uniforms are also non-serialized; without this the steam quads lose their alpha
                // mask after a scene load and cover the alley as hard-edged lavender rectangles.
                WeatherDriver.PushEditorGlobals();
            }
            BuildingsRuntime.PushSceneLights();
        }
    }

    class NkLightArrayGuard : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length == 0 && deleted.Length == 0 && moved.Length == 0) return;
            NkEditorLighting.RequestRefresh();
        }
    }
}
