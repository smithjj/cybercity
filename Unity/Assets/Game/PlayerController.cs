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
        bool _eyeIsRoot;   // NKCamera hosts the camera on the player root itself
        public bool WalkMode = true;
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

        public void Teleport(float x, float y, float z) { PlaceAt(x, y, z, null); }

        void Awake()
        {
            I = this;
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
                cam.nearClipPlane = 0.05f;
                cam.fieldOfView = 75f;
            }
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
            // street-view start (sim 1.5, 30 -> unity z=-30), facing north (down -z)
            transform.position = new Vector3(1.5f, 1.7f, -30f);
            Yaw = 0f; Pitch = 0.04f;
            _camY = transform.position.y;
            _lastSafe = transform.position;
            PlaceAt(1.5f, 1.7f, -30f, null);
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
            double td = Time.timeAsDouble % 86400.0;
            Shader.SetGlobalFloat("_SimTime", t);
            NeonKaido.Atmo.PushGlobals();
            NeonKaido.VehicleTraffic.Update(Time.deltaTime, t, transform.position);
            NeonKaido.WeatherDriver.Update(Time.deltaTime, t, transform.position);
            NeonKaido.Crowd.Update(Time.deltaTime, t, transform.position);
            NeonKaido.Gameplay.Update(Time.deltaTime, t, transform.position);
            if (Input.GetKeyDown(KeyCode.F)) NeonKaido.Gameplay.TakeLift();
            NeonKaido.Soundscape.Update(Time.deltaTime, td);
            if (Input.GetKeyDown(KeyCode.M)) NeonKaido.Soundscape.ToggleMusic();
            for (int i = 0; i < NeonKaido.FacadeSigns.FlickerL.Count; i++)
            {
                var l = NeonKaido.FacadeSigns.FlickerL[i];
                if (l == null) continue;
                l.intensity = NeonKaido.FacadeSigns.FlickerBase[i] * NeonKaido.FacadeSigns.Flicker(NeonKaido.FacadeSigns.FlickerSeed[i], NeonKaido.FacadeSigns.FlickerType[i], 0, t);
            }
            NeonKaido.TrafficSignals.Update(t);
            NeonKaido.Koi.Update(td);
            NeonKaido.Trains.Update(td);

            if (Input.GetKeyDown(KeyCode.V)) { WalkMode = !WalkMode; _locked = false; }

            // cursor lock shared by both modes
            if (Input.GetKeyDown(KeyCode.Escape) && _locked)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true; _locked = false;
            }
            if (!_locked)
            {
                if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; _locked = true; }
            }
            else
            {
                Yaw += Input.GetAxis("Mouse X") * 0.15f;   // +yaw turns right in Unity (Euler Y CW from above)
                Pitch = Mathf.Clamp(Pitch + Input.GetAxis("Mouse Y") * 0.15f, -1.45f, 1.45f);
                _lookLag += Input.GetAxis("Mouse X") * 0.0021f;
            }

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (WalkMode) StepWalk(dt, t);
            else StepFly(dt);
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
            _sprintK = Damp(_sprintK, (sprinting && spd > 4.5f) ? 1 : 0, 5, dt);
            float maxSpeed = _crouch ? CrouchSpd : sprinting ? Sprint : Walk;

            var sy = Mathf.Sin(Yaw); var cy = Mathf.Cos(Yaw);
            float wx = -sy * fwd + cy * right, wz = -cy * fwd - sy * right;
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
            NeonKaido.GameplayHud.Draw(NeonKaido.Gameplay.Gigs, transform.position);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && _locked) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; _locked = false; }
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
    }
}

// recompile nudge
