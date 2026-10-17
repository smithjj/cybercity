// First-person spectator controller (review mode). Runs in Play mode — real Update loop,
// so legacy Input works reliably. Click the Game view to capture the mouse.
using UnityEngine;

namespace NeonKaido
{
    public class PlayerController : MonoBehaviour
    {
        float _yaw, _pitch;
        bool _locked;

        public void Teleport(float x, float y, float z)
        {
            transform.position = new Vector3(x, y, z);
        }

        void Start()
        {
            // street-view start (sim 1.5, 30 -> unity z=-30), eye 1.7, facing north (down -z)
            transform.position = new Vector3(1.5f, 1.7f, -30f);
            _yaw = 0f; _pitch = 4f;
            EnsureLightArrays();
            // lighting spike conclusion (risk R1): all city lighting is shader-array based
            // (NkBPointLights); the real Light components are data holders only and cost HDRP
            // ~8% fps for zero visual effect -> keep them DISABLED in play mode.
            foreach (var l in Object.FindObjectsOfType<Light>()) l.enabled = false;
        }

        // SetVectorArray data does not survive domain reloads — re-arm from the scene lights.
        static void EnsureLightArrays()
        {
            var lights = new System.Collections.Generic.List<Light>();
            foreach (var rn in new[] { "/Buildings", "/StreetLamps", "/Signage", "/FacadeSigns", "/LandmarksRiver", "/LandmarksRail", "/LandmarksExpress", "/LandmarksBridge" })
            {
                var root = GameObject.Find(rn);
                if (root != null) lights.AddRange(root.GetComponentsInChildren<Light>());
            }
            if (lights.Count == 0) return;
            // shader arrays are declared _BLPos[384] — pushing more silently breaks lighting
            if (lights.Count > 384)
            {
                lights.Sort((a, b) =>
                {
                    float ka = (a.color.r + a.color.g + a.color.b) * a.intensity * a.range;
                    float kb = (b.color.r + b.color.g + b.color.b) * b.intensity * b.range;
                    return kb.CompareTo(ka);
                });
                lights.RemoveRange(384, lights.Count - 384);
            }
            var pos = new Vector4[lights.Count];
            var col = new Vector4[lights.Count];
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                pos[i] = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
                col[i] = new Vector4(l.color.r, l.color.g, l.color.b, l.intensity);
            }
            NeonKaido.BuildingsRuntime.PushLightArrays(pos, col, new System.Collections.Generic.List<Material>(), null);
        }

        void Update()
        {
            Shader.SetGlobalFloat("_SimTime", (float)(Time.timeSinceLevelLoadAsDouble % 3600.0));
            NeonKaido.Atmo.PushGlobals();
            NeonKaido.VehicleTraffic.Update(Time.deltaTime, (float)(Time.timeSinceLevelLoadAsDouble % 3600.0), transform.position);
            NeonKaido.WeatherDriver.Update(Time.deltaTime, (float)(Time.timeSinceLevelLoadAsDouble % 3600.0), transform.position);
            NeonKaido.Crowd.Update(Time.deltaTime, (float)(Time.timeSinceLevelLoadAsDouble % 3600.0), transform.position);
            NeonKaido.Gameplay.Update(Time.deltaTime, (float)(Time.timeSinceLevelLoadAsDouble % 3600.0), transform.position);
            if (Input.GetKeyDown(KeyCode.F)) NeonKaido.Gameplay.TakeLift();
            NeonKaido.Soundscape.Update(Time.deltaTime, Time.timeAsDouble % 86400.0);
            if (Input.GetKeyDown(KeyCode.M)) { NeonKaido.Soundscape.ToggleMusic(); }
            NeonKaido.TrafficSignals.Update((float)(Time.timeSinceLevelLoadAsDouble % 3600.0));
            NeonKaido.Koi.Update(Time.timeSinceLevelLoadAsDouble % 3600.0);
            NeonKaido.Trains.Update(Time.timeSinceLevelLoadAsDouble % 3600.0);
            float st = (float)(Time.timeSinceLevelLoadAsDouble % 3600.0);
            for (int i = 0; i < NeonKaido.FacadeSigns.FlickerL.Count; i++)
            {
                var l = NeonKaido.FacadeSigns.FlickerL[i];
                if (l == null) continue;
                l.intensity = NeonKaido.FacadeSigns.FlickerBase[i] * NeonKaido.FacadeSigns.Flicker(NeonKaido.FacadeSigns.FlickerSeed[i], NeonKaido.FacadeSigns.FlickerType[i], 0, st);
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    _locked = false;
                }
                return; // second Esc (unlocked) exits play via editor hook
            }
            if (!_locked)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    _locked = true;
                }
                return;
            }

            _yaw += Input.GetAxis("Mouse X") * 2.2f;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * 2.2f, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float sp = (run ? 9f : 3.2f) * Time.deltaTime;
            var pos = transform.position;
            if (Input.GetKey(KeyCode.W)) pos += transform.forward * sp;
            if (Input.GetKey(KeyCode.S)) pos -= transform.forward * sp;
            if (Input.GetKey(KeyCode.D)) pos += transform.right * sp;
            if (Input.GetKey(KeyCode.A)) pos -= transform.right * sp;
            if (Input.GetKey(KeyCode.Space)) pos += Vector3.up * sp;
            if (Input.GetKey(KeyCode.C)) pos -= Vector3.up * sp;
            if (pos.y < 0.4f) pos.y = 0.4f;
            transform.position = pos;
        }

        void OnGUI()
        {
            NeonKaido.GameplayHud.Draw(NeonKaido.Gameplay.Gigs, transform.position);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && _locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _locked = false;
            }
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

// recompile nudge
