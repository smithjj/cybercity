// Play-mode bootstrap: attaches the spectator player to the camera on entering play,
// rebuilds buildings if the root is missing, and exits play on a second Esc.
using UnityEditor;
using UnityEngine;

namespace NeonKaido.EditorTools
{
    [InitializeOnLoad]
    public static class PlayMode
    {
        static bool _hooked;

        static PlayMode()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnUpdate;
        }

        static void OnPlayModeChanged(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                // rebuild buildings fresh (idempotent; edit-mode root serialized into play scene)
                EditorApplication.delayCall += () =>
                {
                    var ac = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Materials/AC.mat");
                    var mats = new System.Collections.Generic.Dictionary<string, Material>();
                    foreach (var n in new[] { "Facade", "Detail", "Glass" })
                    {
                        var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Settings/Materials/{n}.mat");
                        if (m != null) mats.Add(n.ToLower(), m);
                    }
                    if (ac != null && mats.Count == 3)
                        NeonKaido.BuildingsRuntime.BuildAll(mats, ac);
                };
            }
        }

        static void OnUpdate()
        {
            if (!Application.isPlaying) { _tried = 0; return; }
            // robust player attach: retry until the camera exists (delayCall can race scene load)
            if (_tried < 300)
            {
                _tried++;
                var cam = Camera.main;
                if (cam != null && cam.GetComponent<PlayerController>() == null)
                    cam.gameObject.AddComponent<PlayerController>();
            }
            // second Esc (cursor already released) leaves play mode
            if (Cursor.lockState == CursorLockMode.None && Input.GetKeyDown(KeyCode.Escape))
            {
                EditorApplication.isPlaying = false;
            }
        }

        static int _tried;
    }
}
