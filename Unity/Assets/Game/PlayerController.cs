// Walking player controller — port of src/systems/player/controller.js (Rapier KCC → Unity CharacterController).
// First-person rig with head-bob, landing dip, lean, sprint FOV kick, crouch, jump (coyote + buffer),
// footstep events into Soundscape. V toggles spectator fly mode (legacy review camera).
using UnityEngine;

namespace NeonKaido
{
    public class PlayerController : MonoBehaviour
    {
        // --- JS constants (controller.js) ---
        const float Radius = 0.32f;
        const float HStand = 1.80f;   // capsule total height
        const float HCrouch = 1.12f;
        const float EyeStand = 1.66f;
        const float EyeCrouch = 1.02f;
        const float Gravity = 21f;
        const float JumpV = 6.6f;
        const float Walk = 4.3f, Sprint = 7.8f, CrouchSpd = 2.1f;
        const float StepOffset = 0.45f, SlopeLimit = 52f;

        CharacterController _cc;
        Transform _eye;
        // Fixed street spawn. Start() applies it, and the first Update re-applies it if the
        // editor's play-mode scene snapshot overwrote the camera transform after Start
        // (a race seen when Time.timeScale was left at 0 by shot tooling).
        static readonly Vector3 SpawnPos = new Vector3(3.3f, 1.7f, -30f); // between the traffic lane and the parked row
        bool _spawnChecked;
        bool _eyeIsRoot;   // NKCamera hosts the camera on the player root itself
        public bool WalkMode = true;
        // HUD inputs (GameplayHud reads these per frame)
        public bool Crouched => _crouch;
        public bool SprintGait;   // set each frame in StepWalk
        public bool FlyMode => !WalkMode;
        bool _locked;

        // movement state
        Vector3 _vel;
        float _vy;
        bool _grounded, _crouch;
        float _crouchK, _sprintK, _coyote, _jumpBuf, _airTime;
        Vector3 _lastSafe;

        // camera rig state
        public float Yaw, Pitch;
        float _camY, _bobPhase, _bobAmp, _dip, _dipVel, _roll, _lookLag, _fov = 75f;
        float _sens = 0.0021f;

        public static PlayerController I;

        // F8: dump every renderer within 8-90m whose screen projection crosses the central
        // band — call this while the "train in the road" is visible; the console names the culprit
        void DumpEyeBand()
        {
            var cam = _eye.GetComponent<Camera>();
            var eye = _eye.position;
            var rows = new System.Collections.Generic.List<(float d, string line)>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (b.size.sqrMagnitude < 1f) continue;
                float d = Vector3.Distance(b.center, eye);
                if (d < 0.5f || d > 240f) continue;
                float dy = b.center.y - eye.y;
                if (dy > 24f || dy < -6f) continue;
                var vp = cam.WorldToViewportPoint(b.center);
                if (vp.z < 0.5f) continue;
                if (vp.x < 0.1f || vp.x > 0.9f || vp.y < 0.15f || vp.y > 0.85f) continue;
                rows.Add((d, r.gameObject.name + " d=" + ((int)d) + "m y=" + ((int)b.center.y) + " sz=" + b.size.ToString("F1") + (b.size.x > 6f || b.size.z > 6f ? " *** BIG ***" : "") + (r.isVisible ? "" : " INVISIBLE")));
            }
            rows.Sort((a, b) => a.d.CompareTo(b.d));
            Debug.Log("[NK F8] header pos=" + transform.position.ToString("F1") + " yaw=" + Mathf.RoundToInt(Yaw * Mathf.Rad2Deg));
            int big = 0;
            foreach (var rr in rows) if (rr.d >= 9f) big++;
            foreach (var rr in rows)
            {
                if (big <= 60 && rr.d < 10f) continue;   // nearest clutter ring only shown if few hits
                Debug.Log("[NK F8] " + rr.line);
            }
            Debug.Log("[NK F8] " + rows.Count + " candidates");
        }

        public void Teleport(float x, float y, float z) { PlaceAt(x, y, z, null); }

        // --- car gallery: the showroom drives the camera through these (no player movement while open)
        public void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _locked = false;
        }

        public void SetGalleryPose(Vector3 pos, Quaternion rot)
        {
            transform.SetPositionAndRotation(pos, rot);
            if (!_eyeIsRoot) { _eye.localPosition = Vector3.zero; _eye.localRotation = Quaternion.identity; }
        }

        public void RestorePose(Vector3 pos, float yaw, float pitch)
        {
            Yaw = yaw; Pitch = pitch;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, 0f));
            _camY = pos.y; _vel = Vector3.zero; _vy = 0f;
        }

        int _aaIdx;
        // F10 cycles AA mode — diagnostic for the train shimmer. Modes are discovered from the
        // actual enum (SMAA was removed from some HDRP versions), so missing names are skipped.
        static readonly string[] _aaWanted = { "TemporalAntialiasing", "None", "FastApproximateAntialiasing", "SubpixelMorphologicalAntialiasing" };
        static string[] _aaModes;
        void CycleAntiAliasing()
        {
            var data = _eye.GetComponent("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData") as Component;
            if (data == null) { Debug.Log("[NK] no HDAdditionalCameraData on camera"); return; }
            if (_aaModes == null)
            {
                var fi0 = data.GetType().GetField("antialiasing");
                var names = System.Enum.GetNames(fi0.FieldType);
                var list = new System.Collections.Generic.List<string>();
                foreach (var w in _aaWanted) if (System.Array.IndexOf(names, w) >= 0) list.Add(w);
                _aaModes = list.ToArray();
                Debug.Log("[NK] AA cycles: " + string.Join(", ", _aaModes));
                _aaIdx = 0;
            }
            _aaIdx = (_aaIdx + 1) % _aaModes.Length;
            var t = data.GetType();
            var fi = t.GetField("antialiasing");
            fi.SetValue(data, System.Enum.Parse(fi.FieldType, _aaModes[_aaIdx]));
            // while TAA is on, bias the anti-flicker/jitter knobs against bright-emissive shimmer
            if (_aaModes[_aaIdx] == "TemporalAntialiasing")
            {
                t.GetField("taaAntiFlicker")?.SetValue(data, 0.85f);
                t.GetField("taaRingingReduction")?.SetValue(data, 0.5f);
                t.GetField("taaMotionVectorRejection")?.SetValue(data, 0.5f);
                Debug.Log("[NK] TAA anti-flicker preset applied");
            }
            Debug.Log("[NK] antialiasing = " + fi.GetValue(data));
        }

        void Awake()
        {
            I = this;
            // shot tooling can leave the edit-mode timeScale at 0; play must start running
            Time.timeScale = 1f;
            _cc = gameObject.GetComponent<CharacterController>();
            if (_cc == null) _cc = gameObject.AddComponent<CharacterController>();
            _cc.radius = Radius; _cc.height = HStand; _cc.center = new Vector3(0, HStand * 0.5f, 0);
            _cc.stepOffset = StepOffset; _cc.slopeLimit = SlopeLimit; _cc.skinWidth = 0.03f;
            var cam = GetComponentInChildren<Camera>();
            if (cam == null)
            {
                var go = new GameObject("Eye");
                go.transform.SetParent(transform, false);
                cam = go.AddComponent<Camera>();
            }
            // tight depth range: near 0.1/far 4000 starved the 24-bit depth buffer and z-fighted
            // distant geometry (train underframe/body seam flicker). sky dome is 1000-radius.
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 75f;
            // exactly one enabled AudioListener: on the eye camera (warning spam otherwise)
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) l.enabled = (l.transform == cam.transform);
            var rootGO = GameObject.Find("/AudioRoot");
            if (rootGO != null) foreach (var l in rootGO.GetComponentsInChildren<AudioListener>()) l.enabled = false;
            _eye = cam.transform;
            _eyeIsRoot = _eye == transform;
            if (_eyeIsRoot) SetCapsule(HStand); // capsule hangs below the eye (feet at eye − 1.66)
            // other cameras off
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c != cam) c.enabled = false;
        }

        void Start()
        {
            // street-view start (sim 3.3, 30 -> unity z=-30), facing north (down +z)
            transform.position = SpawnPos;
            Yaw = 0f; Pitch = 0.04f;         // face +Z, north down the avenue
            _camY = transform.position.y;
            _lastSafe = transform.position;
            PlaceAt(SpawnPos.x, SpawnPos.y, SpawnPos.z, null);
            EnsureLightArrays();
            foreach (var l in Object.FindObjectsOfType<Light>()) l.enabled = false;
        }

        static void EnsureLightArrays()
        {
            var lights = new System.Collections.Generic.List<Light>();
            foreach (var rn in new[] { "/Buildings", "/StreetLamps", "/Signage", "/FacadeSigns", "/LandmarksRiver", "/LandmarksRail", "/LandmarksExpress", "/LandmarksBridge" })
            {
                var root = GameObject.Find(rn);
                if (root != null) lights.AddRange(root.GetComponentsInChildren<Light>());
            }
            if (lights.Count == 0) return;
            const int budget = NeonKaido.BuildingsRuntime.LightArraySize;
            if (lights.Count > budget)
            {
                // Rank by reach*intensity so lamps and signs both survive; the tail is dropped.
                lights.Sort((a, b) =>
                {
                    float ka = (a.color.r + a.color.g + a.color.b) * a.intensity * a.range;
                    float kb = (b.color.r + b.color.g + b.color.b) * b.intensity * b.range;
                    return kb.CompareTo(ka);
                });
                lights.RemoveRange(budget, lights.Count - budget);
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

        // Drop the player onto the highest surface under (x,z) (spawn / elevators / respawn).
        public void PlaceAt(float x, float y, float z, float? yaw)
        {
            float gy = y;
            if (Physics.Raycast(new Vector3(x, y + 2f, z), Vector3.down, out var hit, 60f))
                gy = y + 2f - hit.distance;
            transform.position = new Vector3(x, Mathf.Max(0.05f, gy + 0.05f) + (_eyeIsRoot ? EyeStand : 0f), z);
            _camY = transform.position.y;
            _vel = Vector3.zero; _vy = 0;
            if (yaw.HasValue) Yaw = yaw.Value;
        }

        void Update()
        {
            float t = (float)(Time.timeSinceLevelLoadAsDouble % 3600.0);
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            double td = Time.timeAsDouble % 86400.0;
            if (!_spawnChecked)
            {
                _spawnChecked = true;
                Time.timeScale = 1f;
                // If Start ran before the play-mode snapshot restored the edit camera pose,
                // the player would see the saved camera (often perched on a car) and freeze.
                if (Time.frameCount < 30 && !NeonKaido.Driving.Active)
                {
                    if (Vector3.Distance(transform.position, SpawnPos) > 5f)
                        PlaceAt(SpawnPos.x, SpawnPos.y, SpawnPos.z, null);
                    Yaw = 0f; Pitch = 0.04f;   // face north (+Z); a saved-camera snapshot may have restored another facing
                }
            }
            Shader.SetGlobalFloat("_SimTime", t);
            NeonKaido.Atmo.PushGlobals();

            if (Input.GetKeyDown(KeyCode.G) && !NeonKaido.CarGallery.Active && !NeonKaido.Delivery.PhoneOpen) NeonKaido.CarGallery.Toggle();
            if (NeonKaido.CarGallery.Active)
            {
                _cc.enabled = false;
                NeonKaido.CarGallery.Update(dt);
                // keep the city breathing without traffic/crowd/gameplay/chase logic
                var gf = transform.position;
                NeonKaido.WeatherDriver.Update(dt, t, gf);
                NeonKaido.Soundscape.Update(dt, td);
                if (Input.GetKeyDown(KeyCode.M)) NeonKaido.Soundscape.ToggleMusic();
                CityAmbient(t, td);
                return;
            }

            // NX-9 phone: freeze the player but keep the street alive; P/Esc close it in UpdatePhone
            if (Input.GetKeyDown(KeyCode.P) && !NeonKaido.Delivery.PhoneOpen) NeonKaido.Delivery.TogglePhone();
            if (NeonKaido.Delivery.PhoneOpen)
            {
                _cc.enabled = false;
                var pf = transform.position;
                NeonKaido.Delivery.UpdatePhone(dt);
                NeonKaido.Delivery.Update(dt);
                NeonKaido.VehicleTraffic.Update(dt, t, pf);
                NeonKaido.WeatherDriver.Update(dt, t, pf);
                NeonKaido.Crowd.Update(dt, t, pf);
                NeonKaido.Soundscape.Update(dt, td);
                if (Input.GetKeyDown(KeyCode.M)) NeonKaido.Soundscape.ToggleMusic();
                CityAmbient(t, td);
                NeonKaido.Minimap.Prepare(dt);
                return;
            }

            // while driving the camera is the player root, so the world systems need the car as focus
            var focus = NeonKaido.Driving.Active ? NeonKaido.Driving.CarPosition : transform.position;
            NeonKaido.Delivery.Update(dt);
            NeonKaido.VehicleTraffic.Update(dt, t, focus);
            NeonKaido.WeatherDriver.Update(dt, t, focus);
            NeonKaido.Crowd.Update(dt, t, focus);
            NeonKaido.Gameplay.Update(dt, t, focus);
            if (Input.GetKeyDown(KeyCode.F))
            {
                // prompt priority matches JS: vehicle > gig lift? No — the gig prompt wins when present,
                // but exiting a car always takes F while driving.
                if (NeonKaido.Driving.Active) NeonKaido.Driving.Exit(this);
                else if (NeonKaido.Gameplay.Prompt.HasValue && NeonKaido.Gameplay.Prompt.Value.Key == "F") NeonKaido.Gameplay.TakeLift();
                else if (!NeonKaido.Driving.TryEnter(this)) NeonKaido.Gameplay.TakeLift();
            }
            NeonKaido.Soundscape.Update(dt, td);
            if (Input.GetKeyDown(KeyCode.M)) NeonKaido.Soundscape.ToggleMusic();
            CityAmbient(t, td);

            if (!NeonKaido.Driving.Active && Input.GetKeyDown(KeyCode.V)) { WalkMode = !WalkMode; _locked = false; }
            if (Input.GetKeyDown(KeyCode.H)) NeonKaido.GameplayHud.Toggle();
            if (Input.GetKeyDown(KeyCode.N)) NeonKaido.Minimap.ToggleRotate();
            if (Input.GetKeyDown(KeyCode.F8)) DumpEyeBand();
            if (Input.GetKeyDown(KeyCode.R))
            {
                if (NeonKaido.Driving.Active)
                {
                    NeonKaido.Driving.Reset();
                    NeonKaido.GameplayHud.Note("VEHICLE RECOVERED", "CHASSIS RE-SEATED // DRIVETRAIN READY");
                }
                else
                {
                    NeonKaido.WeatherDriver.RainOverride = NeonKaido.WeatherDriver.RainOverride >= 0f ? -1f : 0f;
                    bool clear = NeonKaido.WeatherDriver.RainOverride >= 0f;
                    NeonKaido.GameplayHud.Note(clear ? "RAIN: OFF" : "RAIN: STORM",
                        clear ? "CLOUD SEEDERS HOLDING // SKIES CLEARING" : "STORM RESUMING // WET GROUND");
                }
            }
            if (Input.GetKeyDown(KeyCode.F10)) CycleAntiAliasing();

            // cursor lock shared by both modes
            if (Input.GetKeyDown(KeyCode.Escape) && _locked)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true; _locked = false;
            }
            if (!_locked)
            {
                if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; _locked = true; }
            }
            else if (!NeonKaido.Driving.Active)
            {
                Yaw += Input.GetAxis("Mouse X") * 0.15f;   // +yaw turns right in Unity (Euler Y CW from above)
                Pitch = Mathf.Clamp(Pitch - Input.GetAxis("Mouse Y") * 0.15f, -1.45f, 1.45f); // +Euler X pitches down, so subtract for look-up
                _lookLag += Input.GetAxis("Mouse X") * 0.0021f;
            }

            if (NeonKaido.Driving.Active)
            {
                _cc.enabled = false;
                NeonKaido.Driving.UpdateDriving(dt, this);
            }
            else
            {
                if (WalkMode) StepWalk(dt, t);
                else StepFly(dt);
                NeonKaido.Driving.UpdateIdle(this);
            }
            // HUD map: rotate/scale the baked city map into its RT off the GUI thread
            NeonKaido.Minimap.Prepare(dt);
        }

        void CityAmbient(float t, double td)
        {
            for (int i = 0; i < NeonKaido.FacadeSigns.FlickerL.Count; i++)
            {
                var l = NeonKaido.FacadeSigns.FlickerL[i];
                if (l == null) continue;
                l.intensity = NeonKaido.FacadeSigns.FlickerBase[i] * NeonKaido.FacadeSigns.Flicker(NeonKaido.FacadeSigns.FlickerSeed[i], NeonKaido.FacadeSigns.FlickerType[i], 0, t);
            }
            NeonKaido.TrafficSignals.Update(t);
            NeonKaido.Koi.Update(td);
            NeonKaido.Trains.Update(td);
        }

        void StepFly(float dt)
        {
            _cc.enabled = false;
            bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float sp = (run ? 9f : 3.2f) * dt;
            var pos = transform.position;
            var fwd = Quaternion.Euler(Pitch * Mathf.Rad2Deg, Yaw * Mathf.Rad2Deg, 0f) * Vector3.forward;
            if (Input.GetKey(KeyCode.W)) pos += fwd * sp;
            if (Input.GetKey(KeyCode.S)) pos -= fwd * sp;
            if (Input.GetKey(KeyCode.D)) pos += transform.right * sp;
            if (Input.GetKey(KeyCode.A)) pos -= transform.right * sp;
            if (Input.GetKey(KeyCode.Space)) pos += Vector3.up * sp;
            if (Input.GetKey(KeyCode.C)) pos -= Vector3.up * sp;
            if (pos.y < 0.4f) pos.y = 0.4f;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(Pitch * Mathf.Rad2Deg, Yaw * Mathf.Rad2Deg, 0f);
            if (!_eyeIsRoot) { _eye.localPosition = Vector3.zero; _eye.localRotation = Quaternion.identity; }
        }

        void StepWalk(float dt, float t)
        {
            if (!_cc.enabled) { _cc.enabled = true; }
            // crouch (hold C / Ctrl)
            bool wantCrouch = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl);
            if (wantCrouch && !_crouch) { _crouch = true; SetCapsule(HCrouch); }
            else if (!wantCrouch && _crouch && HasHeadroom()) { _crouch = false; SetCapsule(HStand); }
            _crouchK = Damp(_crouchK, _crouch ? 1 : 0, 12, dt);

            float fwd = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            float right = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            bool moving = Mathf.Abs(fwd) + Mathf.Abs(right) > 0.01f;
            bool sprinting = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && fwd > 0 && !_crouch && moving;
            float spd = new Vector2(_vel.x, _vel.z).magnitude;
            SprintGait = sprinting && spd > 3.5f;
            _sprintK = Damp(_sprintK, (sprinting && spd > 4.5f) ? 1 : 0, 5, dt);
            float maxSpeed = _crouch ? CrouchSpd : sprinting ? Sprint : Walk;

            var sy = Mathf.Sin(Yaw); var cy = Mathf.Cos(Yaw);
            // Unity basis at yaw: forward = (sinYaw, 0, cosYaw), right = (cosYaw, 0, -sinYaw)
            float wx = sy * fwd + cy * right, wz = cy * fwd - sy * right;
            float wl = Mathf.Sqrt(wx * wx + wz * wz);
            if (wl > 1e-4f) { wx /= wl; wz /= wl; }
            float tx = moving ? wx * maxSpeed : 0f, tz = moving ? wz * maxSpeed : 0f;
            float accel = _grounded ? (moving ? 11f : 13f) : 1.8f;
            _vel.x = Damp(_vel.x, tx, accel, dt);
            _vel.z = Damp(_vel.z, tz, accel, dt);

            // jump with coyote time + input buffering
            _coyote = _grounded ? 0.12f : Mathf.Max(0, _coyote - dt);
            _jumpBuf = Input.GetKeyDown(KeyCode.Space) ? 0.14f : Mathf.Max(0, _jumpBuf - dt);
            if (_jumpBuf > 0 && _coyote > 0 && !_crouch)
            {
                _vy = JumpV; _coyote = 0; _jumpBuf = 0; _grounded = false;
                _dipVel += 0.6f;
            }
            _vy -= Gravity * dt;
            if (_vy < -40) _vy = -40;

            bool wasGrounded = _grounded;
            float prevVy = _vy;
            var desired = new Vector3(_vel.x * dt, _vy * dt, _vel.z * dt);
            if (_grounded && _vy < 0) desired.y = Mathf.Min(desired.y, -0.02f);
            _cc.Move(desired);
            _grounded = _cc.isGrounded;
            if (_vy > 0 && _cc.velocity.y < desired.y / Mathf.Max(dt, 1e-4f) * 0.5f) _vy = 0; // bonk head
            if (_grounded && _vy < 0) _vy = 0;

            if (!_grounded) _airTime += dt;
            if (_grounded && !wasGrounded)
            {
                float impact = Mathf.Max(0, -prevVy);
                if (_airTime > 0.12f || impact > 3f)
                {
                    _dipVel -= Mathf.Min(3.2f, impact * 0.22f);
                    NeonKaido.Soundscape.Footstep(0.9f, false, _crouch);
                }
                _airTime = 0;
            }
            if (_grounded) _lastSafe = transform.position;
            if (transform.position.y < -40f) { PlaceAt(_lastSafe.x, _lastSafe.y + 1, _lastSafe.z, null); }

            // smoothed camera height: fast on drops, eased on step-ups
            float dyFeet = transform.position.y - _camY;
            if (!_grounded || Mathf.Abs(dyFeet) > 1.2f) _camY = transform.position.y;
            else _camY = Damp(_camY, transform.position.y, dyFeet > 0 ? 14f : 22f, dt);

            // head-bob + footsteps
            spd = _grounded ? new Vector2(_vel.x, _vel.z).magnitude : 0f;
            float targetAmp = Mathf.Min(1, spd / Walk) * (sprinting ? 1.6f : 1f) * (_crouch ? 0.6f : 1f);
            _bobAmp = Damp(_bobAmp, targetAmp, 8, dt);
            float prevPhase = _bobPhase;
            _bobPhase += dt * (sprinting ? 7.4f : 5.6f) * Mathf.Min(1.2f, 0.35f + spd / Walk * 0.65f);
            if (spd > 0.8f && Mathf.FloorToInt(prevPhase / Mathf.PI) != Mathf.FloorToInt(_bobPhase / Mathf.PI))
                NeonKaido.Soundscape.Footstep(Mathf.Min(1f, spd / Walk), sprinting, _crouch);

            // landing-dip spring
            _dipVel += (-140f * _dip - 15f * _dipVel) * dt;
            _dip += _dipVel * dt;

            // lean / roll from strafe velocity and mouse yaw rate
            float localRight = _vel.x * cy - _vel.z * sy;
            _lookLag = Damp(_lookLag, 0, 10, dt);
            float targetRoll = (-localRight * 0.0042f - _lookLag * 0.35f);
            _roll = Damp(_roll, targetRoll, 7, dt);

            // camera rig
            float eye = Mathf.Lerp(EyeStand, EyeCrouch, _crouchK);
            float bobY = Mathf.Sin(_bobPhase * 2) * 0.034f * _bobAmp;
            float bobX = Mathf.Cos(_bobPhase) * 0.026f * _bobAmp;
            float breathe = Mathf.Sin(t * 1.3f) * 0.004f;
            float idlePitch = (Mathf.Sin(t * 0.9f) * 0.0035f + Mathf.Sin(t * 0.37f) * 0.002f);
            float idleYaw = Mathf.Sin(t * 0.53f) * 0.0025f;
            if (_eyeIsRoot)
            {
                // camera lives on the root: one combined rotation write (CC owns position)
                transform.rotation = Quaternion.Euler(
                    (Pitch + idlePitch + _dip * 0.035f - Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.004f * _bobAmp) * Mathf.Rad2Deg,
                    (Yaw + idleYaw) * Mathf.Rad2Deg,
                    (_roll + Mathf.Cos(_bobPhase) * 0.004f * _bobAmp) * Mathf.Rad2Deg);
            }
            else
            {
                _eye.localPosition = new Vector3(cy * bobX, eye + bobY + breathe + _dip * 0.09f, -sy * bobX);
                _eye.localRotation = Quaternion.Euler(
                    (Pitch + idlePitch + _dip * 0.035f - Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.004f * _bobAmp) * Mathf.Rad2Deg,
                    idleYaw * Mathf.Rad2Deg, // root already carries Yaw — eye must not double it
                    (_roll + Mathf.Cos(_bobPhase) * 0.004f * _bobAmp) * Mathf.Rad2Deg);
                // root yaw only (CC moves in world space; pitch/roll live on the eye)
                transform.rotation = Quaternion.Euler(0, Yaw * Mathf.Rad2Deg, 0);
            }

            float fovT = 75f + _sprintK * 8f + (_grounded ? 0f : 2f);
            _fov = Damp(_fov, fovT, 6, dt);
            var cam = _eye.GetComponent<Camera>();
            if (cam != null && Mathf.Abs(cam.fieldOfView - _fov) > 0.01f) cam.fieldOfView = _fov;
        }

        void SetCapsule(float h)
        {
            _cc.height = h;
            // eye==root: capsule bottom stays glued to the feet (root − eyeH) as it squashes
            _cc.center = new Vector3(0, h * 0.5f - (_eyeIsRoot ? Mathf.Lerp(EyeStand, EyeCrouch, _crouchK) : 0f), 0);
        }

        bool HasHeadroom()
        {
            var top = transform.position + new Vector3(0, HCrouch, 0);
            return !Physics.Raycast(top, Vector3.up, (HStand - HCrouch) + 0.1f);
        }

        static float Damp(float a, float b, float k, float dt) => a + (b - a) * (1f - Mathf.Exp(-k * dt));

        void OnGUI()
        {
            if (NeonKaido.CarGallery.Active) { NeonKaido.CarGallery.DrawHud(); return; }
            var feet = NeonKaido.Driving.Active ? NeonKaido.Driving.CarPosition : transform.position;
            NeonKaido.GameplayHud.Draw(NeonKaido.Gameplay.Gigs, feet);
            NeonKaido.Delivery.DrawHud();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && _locked) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; _locked = false; }
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            NeonKaido.Delivery.ClosePhone();
        }
    }
}

// recompile nudge
