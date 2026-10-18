// Drivable car — port of src/systems/vehicles/drive.js (Rapier DynamicRayCastVehicleController
// -> Unity Rigidbody + 4 WheelColliders) plus the driving half of vehicles/index.js
// (enter/exit/nearestCar, chase camera, hero spotCar lights) and the HUD/audio hooks.
//
// The car mesh is the same shared part-tagged mesh the traffic uses; wheels are spun in the
// VehicleCar vertex shader via _Spin, so the WheelColliders are invisible collision helpers.
// Tuning is mapped from drive.js TUNE: engine torque = engine * wheelRadius / 2 per rear wheel,
// service brake = brake * 34 Nm, handbrake x1.6 on the rear axle.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Driving
    {
        class Tune
        {
            public float mass, engine, brake, maxSteer, top, grip, rearGrip;
        }

        static readonly Dictionary<string, Tune> Tunes = new Dictionary<string, Tune>
        {
            // drive.js TUNE (mass kg, engine N, brake N, maxSteer rad, top m/s, grip)
            ["hero"] = new Tune { mass = 1550, engine = 7800, brake = 95, maxSteer = 0.55f, top = 68, grip = 2.4f, rearGrip = 2.15f },
            ["kei"] = new Tune { mass = 850, engine = 3200, brake = 55, maxSteer = 0.60f, top = 40, grip = 2.1f, rearGrip = 2.00f },
            ["taxi"] = new Tune { mass = 1350, engine = 5200, brake = 80, maxSteer = 0.55f, top = 50, grip = 2.2f, rearGrip = 2.05f },
            ["van"] = new Tune { mass = 3200, engine = 9500, brake = 160, maxSteer = 0.50f, top = 36, grip = 2.0f, rearGrip = 2.00f },
        };

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["hero"] = "KUROGANE GT-R",
            ["kei"] = "MIZUCHI KEI",
            ["taxi"] = "KAIDO CAB",
            ["van"] = "ISUZU-ELF FREIGHT",
        };

        static VehicleTraffic.ParkedCar _parked;
        static GameObject _go;
        static VehicleTraffic.Manifest _mf;
        static Tune _tune;
        static Rigidbody _rb;
        static WheelCollider[] _wheels;
        static readonly bool[] _front = new bool[4];
        static MaterialPropertyBlock _mpb;
        static MeshRenderer _mr;

        static float _spin, _steer, _speed, _speedKmh, _throttle, _brakeLight;
        static bool _reverse, _handbrakePrev;
        static int _gear = 1;

        static Vector3 _camPos, _lastSafe;
        static float _camYaw, _camYawOff, _camPitchOff, _lookIdle;
        static bool _camInit;

        // HUD + audio read these
        public static bool Active => _go != null;
        public static float SpeedKmh => _speedKmh;
        public static float Speed => _speed;
        public static string CarName { get; private set; }
        public static string Prompt { get; private set; }
        public static string PromptName { get; private set; }
        public static Vector3 CarPosition => _go != null ? _go.transform.position : Vector3.zero;
        public static float BrakeLight => _brakeLight;
        public static int Gear => _gear;
        public static bool Reverse => _reverse;
        public static float Throttle => _throttle;
        public static Rigidbody Body => _rb;

        // test hooks: seconds of synthetic full throttle + steer (GameDebug.DriveTest)
        public static float TestDrive, TestSteer;

        // ---------------------------------------------------------------- enter / exit
        public static bool TryEnter(PlayerController pc)
        {
            if (Active || pc == null) return false;
            var p = VehicleTraffic.NearestParked(pc.transform.position, 4.5f);
            if (p == null) return false;
            Enter(p, pc);
            return Active;
        }

        public static void Enter(VehicleTraffic.ParkedCar p, PlayerController pc)
        {
            if (Active || p == null || p.go == null || p.taken) return;
            _mf = VehicleTraffic.GetManifest(p.type);
            if (_mf == null) return;
            _parked = p;
            _go = p.go;
            _tune = Tunes.TryGetValue(p.type, out var t) ? t : Tunes["hero"];
            CarName = Names.TryGetValue(p.type, out var n) ? n : p.type.ToUpperInvariant();

            var tr = _go.transform;
            p.taken = true;

            // The parked static BoxCollider becomes the chassis collider.
            var bc = _go.GetComponent<BoxCollider>();
            if (bc == null)
            {
                bc = _go.AddComponent<BoxCollider>();
                bc.size = new Vector3(_mf.width * 0.92f, _mf.height * 0.80f, _mf.length * 0.96f);
                bc.center = new Vector3(0f, _mf.height * 0.43f, 0f);
            }

            _rb = _go.AddComponent<Rigidbody>();
            _rb.mass = _tune.mass;
            _rb.linearDamping = 0.04f;
            _rb.angularDamping = 0.6f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.centerOfMass = new Vector3(0f, _mf.height * 0.28f, 0f);
            _rb.maxAngularVelocity = 8f;
            _rb.position = tr.position + Vector3.up * 0.10f;

            BuildWheels();

            _mr = _go.GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _spin = 0f; _steer = 0f; _speed = 0f; _speedKmh = 0f;
            _reverse = false; _gear = 1; _throttle = 0f; _brakeLight = 0f;
            _handbrakePrev = false;
            _camInit = false; _camYawOff = 0f; _camPitchOff = 0f; _lookIdle = 0f;
            Prompt = null; PromptName = null;
            _camYaw = Mathf.Atan2(tr.forward.x, tr.forward.z);
            _lastSafe = tr.position;

            if (pc != null)
            {
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                // the camera is usually the player root in this port; snap it behind the car now
                Chase(1f, pc);
            }

            VehicleTraffic.SetDrivenCar(_go, p.type);
            GameplayHud.Note("VEHICLE LINK ESTABLISHED", CarName);
        }

        public static void Exit(PlayerController pc)
        {
            if (!Active) return;
            var tr = _go.transform;
            Vector3 fwd = tr.forward;
            Vector3 side = -tr.right;
            float halfW = _mf.width * 0.5f + 0.62f;

            Vector3[] cands =
            {
                tr.position + side * halfW,
                tr.position - side * halfW,
                tr.position - fwd * (_mf.length * 0.5f + 1.1f),
                tr.position + fwd * (_mf.length * 0.5f + 1.1f),
            };
            Vector3 spot = cands[0];
            foreach (var c in cands)
            {
                if (!Physics.CheckCapsule(c + Vector3.up * 0.4f, c + Vector3.up * 1.6f, 0.34f,
                        ~0, QueryTriggerInteraction.Ignore)) { spot = c; break; }
            }

            // leave it parked: head/tail markers dim, no more physics
            if (_mr != null)
            {
                _mr.GetPropertyBlock(_mpb);
                _mpb.SetVector("_LightLv", new Vector3(0f, 0.25f, 0f));
                _mr.SetPropertyBlock(_mpb);
            }
            if (_wheels != null)
                foreach (var w in _wheels) if (w != null) Object.Destroy(w.gameObject);
            _wheels = null;
            if (_rb != null) Object.Destroy(_rb);
            _rb = null;
            _go = null;
            if (_parked != null) _parked.taken = false;
            _parked = null;
            CarName = null;
            VehicleTraffic.ClearDrivenCar();

            var cc2 = pc != null ? pc.GetComponent<CharacterController>() : null;
            if (cc2 != null) cc2.enabled = true;
            if (pc != null)
            {
                pc.PlaceAt(spot.x, spot.y + 0.6f, spot.z, Mathf.Atan2(fwd.x, fwd.z));
                pc.Pitch = 0f;
                var cam = Camera.main;
                if (cam != null) cam.fieldOfView = 75f;
            }
            Soundscape.CarEngine(false, 0f, 0f, Vector3.zero, 1f);
        }

        public static void Reset()
        {
            if (!Active || _rb == null) return;
            var tr = _go.transform;
            float yaw = Mathf.Atan2(tr.forward.x, tr.forward.z);
            Vector3 p = tr.position;
            if (p.y < -4f && _lastSafe.y > -2f) p = _lastSafe;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = p + Vector3.up * 0.9f;
            _rb.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            _go.transform.position = _rb.position;
            _go.transform.rotation = _rb.rotation;
            _camInit = false;
        }

        // ---------------------------------------------------------------- wheels
        static void BuildWheels()
        {
            _wheels = new WheelCollider[4];
            float x = _mf.width * 0.44f;
            float frontZ = -_mf.axleF;    // sim -Z forward -> Unity +Z after mirror
            float rearZ = -_mf.axleR;
            float y = _mf.wheelRadius + 0.05f;
            Vector3[] pos =
            {
                new Vector3(-x, y, frontZ), new Vector3(x, y, frontZ),
                new Vector3(-x, y, rearZ), new Vector3(x, y, rearZ),
            };
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                _front[i] = front;
                var wgo = new GameObject(front ? "wheel_f" + i : "wheel_r" + (i - 2));
                wgo.transform.SetParent(_go.transform, false);
                wgo.transform.localPosition = pos[i];
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = _mf.wheelRadius;
                wc.suspensionDistance = 0.12f;
                wc.mass = Mathf.Max(12f, _tune.mass * 0.02f);
                wc.forceAppPointDistance = 0f;
                wc.center = Vector3.zero;
                var spring = wc.suspensionSpring;
                spring.spring = _tune.mass * 55f;
                spring.damper = _tune.mass * 3.2f;
                spring.targetPosition = 0.5f;
                wc.suspensionSpring = spring;

                var side = wc.sidewaysFriction;
                side.extremumSlip = 0.35f; side.extremumValue = 1f;
                side.asymptoteSlip = 1.2f; side.asymptoteValue = 0.6f;
                side.stiffness = front ? _tune.grip : _tune.rearGrip;
                wc.sidewaysFriction = side;

                var fwd = wc.forwardFriction;
                fwd.extremumSlip = 0.42f; fwd.extremumValue = 1f;
                fwd.asymptoteSlip = 1.2f; fwd.asymptoteValue = 0.55f;
                fwd.stiffness = 1.5f;
                wc.forwardFriction = fwd;

                wc.motorTorque = 0f;
                wc.brakeTorque = 0f;
                _wheels[i] = wc;
            }
        }

        // ---------------------------------------------------------------- prompt (on foot)
        public static void UpdateIdle(PlayerController pc)
        {
            Prompt = null; PromptName = null;
            if (pc == null || pc.transform == null) return;
            var p = VehicleTraffic.NearestParked(pc.transform.position, 4.5f);
            if (p == null) return;
            Prompt = "ENTER VEHICLE";
            PromptName = Names.TryGetValue(p.type, out var n) ? n : p.type.ToUpperInvariant();
        }

        // ---------------------------------------------------------------- per-frame
        public static void UpdateDriving(float dt, PlayerController pc)
        {
            if (!Active || _rb == null) return;

            // ---- input (drive.js signature: throttle -1..1, steer -1..1, handbrake, boost)
            float thr = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                      - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            float steerIn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                          - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            bool hb = Input.GetKey(KeyCode.Space);
            bool boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (TestDrive > 0f) { thr = 1f; boost = false; steerIn = TestSteer; TestDrive -= dt; }

            // hard cap on vertical launch (a kinematic traffic proxy spawning into the car
            // can otherwise throw it 15 m into the air)
            var lv0 = _rb.linearVelocity;
            if (lv0.y > 6f) { lv0.y = 6f; _rb.linearVelocity = lv0; }

            var tr = _go.transform;
            float v = Vector3.Dot(_rb.linearVelocity, tr.forward); // signed, m/s
            float absV = Mathf.Abs(v);
            _reverse = v < -0.5f;
            _throttle = Mathf.Abs(thr);
            float speedCap = _tune.top * (boost ? 1.2f : 1f);

            // speed-sensitive steering (JS maxSteer at rest, ~0.38x at top)
            float maxSteer = _tune.maxSteer * Mathf.Lerp(1f, 0.38f, Mathf.Clamp01(absV / _tune.top));
            _steer = Mathf.MoveTowards(_steer, steerIn * maxSteer, 4.5f * dt);
            float steerDeg = _steer * Mathf.Rad2Deg;
            for (int i = 0; i < 4; i++) if (_front[i]) _wheels[i].steerAngle = steerDeg;

            // ---- drive/brake torque
            float motor = 0f;
            if (absV < speedCap)
            {
                if (thr > 0f && v > -0.6f)
                    motor = _tune.engine * _mf.wheelRadius * 0.5f * thr * (boost ? 1.45f : 1f);
                else if (thr < 0f && v < 0.6f)
                    motor = _tune.engine * _mf.wheelRadius * 0.34f * thr; // thr<0 -> reverse
            }
            motor *= Mathf.Clamp01(1f - absV / speedCap);

            float brake = 0f;
            if (thr < 0f && v > 0.5f) brake = _tune.brake * 34f;          // braking from forward
            else if (thr > 0f && v < -0.5f) brake = _tune.brake * 34f;    // braking from reverse
            if (hb) brake = Mathf.Max(brake, _tune.brake * 55f);
            if (Mathf.Abs(thr) < 0.01f && absV < 0.6f) brake = Mathf.Max(brake, 80f); // hold at rest
            _brakeLight = (brake > 120f || hb) ? 1f : 0f;

            for (int i = 0; i < 4; i++)
            {
                var w = _wheels[i];
                w.motorTorque = i >= 2 ? motor : 0f; // rear-drive (drive.js TUNE is rear-drive)
                w.brakeTorque = (i >= 2 && hb) ? brake * 1.6f : brake;
                if (i >= 2 && hb != _handbrakePrev)
                {
                    var sf = w.sidewaysFriction;
                    sf.stiffness = hb ? _tune.rearGrip * 0.55f : _tune.rearGrip;
                    w.sidewaysFriction = sf;
                }
            }
            _handbrakePrev = hb;

            // downforce keeps the light arcade chassis planted at speed
            _rb.AddForce(-tr.up * (absV * absV * 1.2f));

            // ---- state + car shader (wheel spin, brake lamps)
            _speed = v;
            _speedKmh = absV * 3.6f;
            _gear = _reverse ? 0 : Mathf.Clamp(1 + Mathf.FloorToInt(_speedKmh / 34f), 1, 6);
            _spin = Mathf.Repeat(_spin + (v * dt) / Mathf.Max(0.05f, _mf.wheelRadius), Mathf.PI * 2f);
            if (_mr != null)
            {
                _mr.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_Spin", _spin);
                _mpb.SetVector("_LightLv", new Vector3(1f, 1f, _brakeLight));
                _mr.SetPropertyBlock(_mpb);
            }
            if (tr.position.y > -1f && tr.up.y > 0.6f) _lastSafe = tr.position;

            Soundscape.CarEngine(true, absV, _throttle, tr.position, dt);
            Chase(dt, pc);
        }

        // ---------------------------------------------------------------- chase camera
        static void Chase(float dt, PlayerController pc)
        {
            var cam = Camera.main;
            if (cam == null || !Active) return;
            var tr = _go.transform;
            float sp = _rb != null ? _rb.linearVelocity.magnitude : 0f;
            float carYaw = Mathf.Atan2(tr.forward.x, tr.forward.z);
            float wantYaw = carYaw;
            if (sp > 4f && _rb != null)
            {
                var lv = _rb.linearVelocity;
                if (Mathf.Abs(lv.y) < sp * 0.6f) // drift view: follow the velocity direction
                {
                    float vy = Mathf.Atan2(lv.x, lv.z);
                    float d = Mathf.DeltaAngle(carYaw * Mathf.Rad2Deg, vy * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    if (Mathf.Cos(d) > 0f) wantYaw = carYaw + d * 0.45f;
                }
            }
            if (!_camInit) { _camYaw = wantYaw; _camPos = tr.position; _camInit = true; }
            _camYaw = Mathf.LerpAngle(_camYaw * Mathf.Rad2Deg, wantYaw * Mathf.Rad2Deg, 1f - Mathf.Exp(-4.5f * dt)) * Mathf.Deg2Rad;

            float mx = 0f, my = 0f;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                mx = Input.GetAxis("Mouse X"); my = Input.GetAxis("Mouse Y");
            }
            if (Mathf.Abs(mx) + Mathf.Abs(my) > 0.001f)
            {
                _camYawOff += mx * 0.06f; // +X orbits the camera so the view turns right (Unity yaw is CW from above)
                _camPitchOff = Mathf.Clamp(_camPitchOff - my * 0.03f, -0.25f, 0.5f);
                _lookIdle = 0f;
            }
            else
            {
                _lookIdle += dt;
                if (_lookIdle > 1.2f)
                {
                    _camYawOff *= Mathf.Exp(-dt * 2.5f);
                    _camPitchOff *= Mathf.Exp(-dt * 2.5f);
                }
            }

            float cy = _camYaw + _camYawOff;
            float dist = _mf.length * 1.18f + 1.6f + sp * 0.025f;
            float height = _mf.height * 1.05f + 0.55f;
            Vector3 behind = new Vector3(-Mathf.Sin(cy), 0f, -Mathf.Cos(cy));
            Vector3 wantPos = tr.position + behind * dist + Vector3.up * (height + _camPitchOff * dist);
            _camPos = Vector3.Lerp(_camPos, wantPos, 1f - Mathf.Exp(-9f * dt));

            // keep the camera out of walls
            Vector3 origin = tr.position + Vector3.up * (_mf.height * 0.85f);
            var d2 = _camPos - origin;
            if (Physics.Linecast(origin, _camPos, out var hit, ~0, QueryTriggerInteraction.Ignore))
            {
                float len = d2.magnitude;
                if (hit.distance < len)
                    _camPos = origin + d2.normalized * Mathf.Max(0.8f, hit.distance - 0.35f);
            }

            cam.transform.position = _camPos;
            cam.transform.LookAt(tr.position + Vector3.up * (_mf.height * 0.6f) + tr.forward * 2.4f);
            float fov = 62f + Mathf.Min(sp, 60f) * 0.22f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-3f * dt));
        }
    }
}
