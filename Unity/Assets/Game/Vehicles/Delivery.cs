// NEON KAIDO — NX-9 vehicle delivery. The phone app (P) orders one of the four player
// cars in a chosen paint; an autonomous car pulls out of the avenue, runs the kerb lane,
// brakes, parks itself near you with its hazards on, and joins the normal parked-car list
// so F enters it and Driving takes over exactly as with any other car.
//
// Controls: P phone, ↑/↓ pick unit, ←/→ paint, ENTER dispatch, X cancel/dismiss, P/ESC close.
// Public surface (also used by the editor debug hooks):
//   TogglePhone/OpenPhone/ClosePhone, Order(typeIndex, paintIndex), Cancel, Reset,
//   Phase/State/Pending/EnRoute, CarObject, CarName, StateText, DebugWarp/DebugArrive.
//
// The drop point is deliberately in front of the player (6 m short of their z in the
// direction they are looking) so the car is seen arriving and never drives through them.
// No pathfinding: the avenue is straight, so the delivery is a speed profile plus a
// 40 m lateral ease from the travel lane to the kerb.
using UnityEngine;

namespace NeonKaido
{
    public static class Delivery
    {
        public enum Phase { Idle, EnRoute, Arrived, Done }

        // ---------------------------------------------------------------- tuning
        const float VMAX = 12.5f;      // m/s cruise (~45 km/h)
        const float ACC = 3.2f;
        const float DEC = 3.0f;
        const float PULL = 42f;        // metres over which the car eases to the kerb
        const float DOCK_X = 2.82f;    // kerb-side dock (road half-width is 6 m)
        const float LANE_X = 1.5f;     // avenue travel-lane centre
        const float Z_S = -60f;        // avenue ends in Unity z (sim 60 .. -420)
        const float Z_N = 420f;

        // ---------------------------------------------------------------- runtime state
        static Phase _phase;
        static string _type;
        static CarCatalog.Entry _entry;
        static GameObject _go;
        static MeshRenderer _mr;
        static MaterialPropertyBlock _mpb;
        static VehicleTraffic.ParkedCar _rec;

        static int _paint, _fee;
        static float _v, _spin, _brake, _blink, _wheelR;
        static float _z, _x, _zStop, _run, _d, _laneX, _dockX;
        static Vector3 _lastPos, _stopPos;
        static string _drop;
        static bool _wasWelcome;

        static bool _welcome = true;          // first dispatch of the session is free
        static bool _paintedInit;
        static readonly int[] _paintSel = new int[8];

        // phone
        static bool _phone;
        static int _sel, _enterFrame = -1;
        static string _phoneMsg;
        static float _phoneMsgT;
        static float _previewRun;
        static string _previewDrop;

        public static bool PhoneOpen => _phone;
        public static Phase State => _phase;
        public static bool Pending => _phase == Phase.EnRoute || _phase == Phase.Arrived;
        public static bool EnRoute => _phase == Phase.EnRoute;
        public static GameObject CarObject => _go;
        public static string CarName => _entry != null ? _entry.Name : null;
        public static float Fee => _fee;

        // ---------------------------------------------------------------- phone lifecycle
        public static void TogglePhone()
        {
            if (_phone) { ClosePhone(); return; }
            OpenPhone();
        }

        public static void OpenPhone()
        {
            if (_phone) return;
            if (CarGallery.Active) return;
            var pc = PlayerController.I;
            if (pc == null) return;
            if (Driving.Active)
            {
                GameplayHud.Note("NX-9 // DRIVING", "PULL OVER BEFORE USING THE PHONE");
                return;
            }
            EnsurePaintSel();
            _phone = true;
            _enterFrame = Time.frameCount;
            _phoneMsg = null;
            _phoneMsgT = 0f;
            ComputeDrop(pc, out _, out _, out _previewRun, out _previewDrop, out _);
        }

        public static void ClosePhone()
        {
            _phone = false;
            _phoneMsg = null;
            _phoneMsgT = 0f;
        }

        static void EnsurePaintSel()
        {
            if (_paintedInit) return;
            _paintedInit = true;
            for (int i = 0; i < _paintSel.Length && i < CarCatalog.Count; i++)
                _paintSel[i] = Mathf.Clamp(CarCatalog.Ground[i].DefaultPaint, 0, Mathf.Max(0, CarCatalog.Ground[i].Palette.Length - 1));
        }

        static void Msg(string s)
        {
            _phoneMsg = s;
            _phoneMsgT = 2.4f;
        }

        // ---------------------------------------------------------------- order
        public static bool Order(int index, int paint)
        {
            if (Pending) { Msg("A DELIVERY IS ALREADY IN PROGRESS"); return false; }
            if (Driving.Active) { Msg("PULL OVER BEFORE ORDERING"); return false; }
            if (CarGallery.Active) return false;
            var pc = PlayerController.I;
            if (pc == null) return false;
            if (CarCatalog.Count == 0) return false;
            index = Mathf.Clamp(index, 0, CarCatalog.Count - 1);
            var e = CarCatalog.Ground[index];
            var pal = e.Palette;
            paint = pal.Length > 0 ? ((paint % pal.Length) + pal.Length) % pal.Length : 0;

            var gigs = Gameplay.Gigs;
            bool free = _welcome;
            int fee = free ? 0 : e.DispatchFee;
            if (fee > 0 && (gigs == null || gigs.Eddies < fee))
            {
                Msg("INSUFFICIENT EDDIES");
                GameplayHud.Note("NX-9 // PAYMENT DECLINED", "INSUFFICIENT EDDIES — " + CarCatalog.Money(fee) + " NEEDED");
                return false;
            }

            var err = VehicleTraffic.LoadModels();
            if (err != null || !VehicleTraffic.ModelsReady)
            {
                Msg("FLEET UPLINK OFFLINE");
                GameplayHud.Note("NX-9 // FLEET ERROR", err ?? "VEHICLE MODELS MISSING");
                return false;
            }

            ComputeDrop(pc, out int d, out float zStop, out float run, out string drop, out float dockX);
            float laneX = d > 0f ? -LANE_X : LANE_X;
            float zStart = Mathf.Clamp(zStop - d * run, Z_S + 2f, Z_N - 2f);
            float actualRun = Mathf.Abs(zStop - zStart);
            if (actualRun < 1f) { Msg("NO KERB SPACE AVAILABLE"); return false; }

            var go = VehicleTraffic.SpawnDeliveryCar(null, e.Type,
                new Vector3(laneX, 0f, zStart), d > 0f ? 0f : 180f, pal[paint], new Vector3(1f, 1f, 0f));
            if (go == null) { Msg("FLEET UPLINK OFFLINE"); return false; }

            if (fee > 0) gigs.Eddies -= fee;
            _wasWelcome = free;
            _welcome = false;

            _phase = Phase.EnRoute;
            _type = e.Type; _entry = e; _paint = paint; _fee = fee;
            _go = go; _mr = go.GetComponent<MeshRenderer>(); _mpb = new MaterialPropertyBlock();
            _rec = null;
            _d = d; _zStop = zStop; _z = zStart; _laneX = laneX; _dockX = dockX;
            _x = laneX; _run = Mathf.Max(actualRun, 1f); _v = 0f; _spin = 0f; _brake = 0f; _blink = 0f;
            _wheelR = VehicleTraffic.GetManifest(e.Type)?.wheelRadius ?? 0.33f;
            _drop = drop;
            _lastPos = new Vector3(_x, 0f, _z);
            _stopPos = new Vector3(_dockX, 0f, _zStop);
            ApplyLights(new Vector3(1f, 1f, 0f));
            VehicleTraffic.SetExtraLitCar(_go, _type, 0f);
            Minimap.ExtraMarker = _go.transform.position;

            _sel = index;
            _paintSel[index] = paint;
            GameplayHud.DispatchToast("DISPATCH CONFIRMED",
                e.Name + " · " + e.PaintName(paint) + " · " + (fee == 0 ? "COMPLIMENTARY" : CarCatalog.Money(fee)));
            return true;
        }

        public static bool Cancel()
        {
            if (!Pending) return false;
            if (Driving.Active && VehicleTraffic.DrivenCar == _go) return false;
            bool arrived = _phase == Phase.Arrived;
            int refund = arrived ? 0 : _fee;
            if (_rec != null) { VehicleTraffic.UnregisterDelivered(_rec); _rec = null; }
            if (_go != null) Object.Destroy(_go);
            _go = null; _mr = null;
            VehicleTraffic.ClearExtraLitCar();
            Minimap.ExtraMarker = null;
            if (refund > 0 && Gameplay.Gigs != null) Gameplay.Gigs.Eddies += refund;
            if (_wasWelcome && !arrived) _welcome = true;
            GameplayHud.DispatchToast(arrived ? "DISPATCH CLOSED" : "DISPATCH CANCELLED",
                arrived ? "VEHICLE RETURNED TO THE FLEET"
                        : (refund > 0 ? CarCatalog.Money(refund) + " REFUNDED" : "COMPLIMENTARY ORDER RELEASED"));
            ResetOrder();
            return true;
        }

        public static void Reset()
        {
            bool driven = _go != null && Driving.Active && VehicleTraffic.DrivenCar == _go;
            if (_go != null && !driven) Object.Destroy(_go);
            if (_rec != null && !driven) VehicleTraffic.UnregisterDelivered(_rec);
            VehicleTraffic.ClearExtraLitCar();
            Minimap.ExtraMarker = null;
            ResetOrder();
            if (!driven) { _go = null; _mr = null; _rec = null; }
            _welcome = true; _wasWelcome = false;
            ClosePhone();
        }

        static void ResetOrder()
        {
            _phase = Phase.Idle;
            _type = null; _entry = null; _fee = 0; _paint = 0;
            _v = 0f; _z = _zStop = _run = 0f;
            _entry = null; _drop = null;
        }

        // ---------------------------------------------------------------- drop point
        static void ComputeDrop(PlayerController pc, out int d, out float zStop, out float run, out string area, out float dockX)
        {
            Vector3 p = pc.transform.position;
            float pz = Mathf.Clamp(p.z, Z_S + 14f, Z_N - 14f);

            // arrive from where the player is looking; fall back to their side of the avenue
            bool facingZ = Mathf.Abs(Mathf.Cos(pc.Yaw)) > 0.25f;
            d = facingZ ? (Mathf.Cos(pc.Yaw) > 0f ? -1 : 1) : (p.x >= 0f ? -1 : 1);

            // stop 6 m short of the player so the car is seen arriving instead of driving through them
            float zs0 = Mathf.Clamp(pz - d * 6f, Z_S + 12f, Z_N - 12f);
            float avail = d < 0 ? Z_N - zs0 : zs0 - Z_S;

            // near an avenue end, take the other approach if it has a much longer run-up
            if (avail < 40f)
            {
                int alt = -d;
                float zs2 = Mathf.Clamp(pz - alt * 6f, Z_S + 12f, Z_N - 12f);
                float avail2 = alt < 0 ? Z_N - zs2 : zs2 - Z_S;
                if (avail2 > avail + 10f) { d = alt; zs0 = zs2; avail = avail2; }
            }

            dockX = d > 0 ? -DOCK_X : DOCK_X;
            // keep the car short of the player and step the stop further along the avenue
            // until it lands in a gap in the parked row (3 to 7 m car spacing)
            for (int k = 0; k < 3; k++)
            {
                float cand = Mathf.Clamp(zs0 - d * (7f * k), Z_S + 12f, Z_N - 12f);
                if (VehicleTraffic.ParkedZClearance(dockX, cand) >= 6.2f) { zs0 = cand; break; }
            }
            avail = d < 0 ? Z_N - zs0 : zs0 - Z_S;

            float zStart = Mathf.Clamp(zs0 - d * Mathf.Clamp(avail - 3f, 22f, 115f), Z_S + 2f, Z_N - 2f);
            run = Mathf.Abs(zs0 - zStart);
            zStop = zs0;
            var zone = Zones.At(dockX, 0, -zStop);
            area = zone.Id != null ? zone.Area : "NEON AVENUE";
        }

        static float EtaFor(float run)
        {
            float t = 0f;
            // accelerate, cruise, brake: a cheap but honest estimate
            float ta = VMAX / ACC;
            float da = 0.5f * ACC * ta * ta;
            if (run <= da) t = Mathf.Sqrt(2f * run / ACC);
            else
            {
                float tr = VMAX / DEC;
                float dr = 0.5f * VMAX * tr;
                if (run < da + dr) t = ta + (run - da) / VMAX + tr * 0.5f;
                else t = ta + (run - da - dr) / VMAX + tr;
            }
            return t + 0.8f;
        }

        static string FmtEta(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ---------------------------------------------------------------- per-frame
        public static void Update(float dt)
        {
            if (_phoneMsgT > 0f) _phoneMsgT -= dt;
            if (_go == null && Pending)
            {
                Minimap.ExtraMarker = null;
                VehicleTraffic.ClearExtraLitCar();
                ResetOrder();
            }
            if (_phase == Phase.EnRoute) Step(dt);
            else if (_phase == Phase.Arrived) StepArrived(dt);
            else if (_phase == Phase.Done && _go == null) ResetOrder();

            if (Pending && _go != null)
            {
                Minimap.ExtraMarker = _go.transform.position;
                if (_phase == Phase.EnRoute) VehicleTraffic.SetExtraLitCar(_go, _type, _brake);
            }
        }

        static void Step(float dt)
        {
            if (_go == null) { ResetOrder(); return; }
            float remaining = Mathf.Abs(_zStop - _z);
            // the deceleration curve can run out of speed just short of the stop; snap the last
            // half metre instead of crawling (or stalling) at the kerb
            if (remaining <= 0.45f)
            {
                _z = _zStop;
                _x = _dockX;
                _v = 0f;
                Arrive();
                return;
            }
            float target = Mathf.Min(VMAX, Mathf.Sqrt(2f * DEC * Mathf.Max(0.05f, remaining - 0.30f)));
            if (_v < target) _v = Mathf.Min(target, _v + ACC * dt);
            else _v = Mathf.MoveTowards(_v, target, DEC * 1.4f * dt);
            float step = Mathf.Min(_v * dt, remaining);

            if (step > 1e-5f)
            {
                _z += _d * step;
                float pull = Mathf.Clamp01((PULL - remaining) / PULL);
                float sw = pull * pull * (3f - 2f * pull);
                _x = Mathf.Lerp(_laneX, _dockX, sw);
                var pos = new Vector3(_x, 0f, _z);
                var delta = pos - _lastPos;
                float yaw = delta.sqrMagnitude > 1e-7f
                    ? Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg
                    : (_d > 0f ? 0f : 180f);
                _go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                _lastPos = pos;
                _spin = Mathf.Repeat(_spin + step / Mathf.Max(0.05f, _wheelR), Mathf.PI * 2f);
            }

            _brake = (target < _v - 0.2f || _v < 0.35f) ? 1f : 0f;
            ApplyLights(new Vector3(1f, 1f, _brake));
            VehicleTraffic.SetExtraLitCar(_go, _type, _brake);
            if (remaining <= 0.04f || Mathf.Abs(_zStop - _z) < 0.02f) Arrive();
        }

        static void StepArrived(float dt)
        {
            if (_go == null) { ResetOrder(); return; }
            _blink += dt;
            float hz = Mathf.Repeat(_blink, 1.1f) < 0.55f ? 1f : 0.05f;
            ApplyLights(new Vector3(1f, 1f, hz));
            VehicleTraffic.SetExtraLitCar(_go, _type, hz);

            if (Driving.Active && VehicleTraffic.DrivenCar == _go)
            {
                _phase = Phase.Done;
                Minimap.ExtraMarker = null;
                VehicleTraffic.ClearExtraLitCar();
                GameplayHud.DispatchToast("DELIVERY COMPLETE", "VEHICLE LINKED — ENJOY THE RIDE");
            }
        }

        static void Arrive()
        {
            if (_go == null) { ResetOrder(); return; }
            _z = _zStop; _x = _dockX; _v = 0f;
            _go.transform.SetPositionAndRotation(new Vector3(_x, 0f, _z), Quaternion.Euler(0f, _d > 0f ? 0f : 180f, 0f));
            _spin = 0f;
            // if the player is standing where the car parked, step them clear before the
            // static chassis collider appears (otherwise the CharacterController is trapped)
            var pc = PlayerController.I;
            var mf = VehicleTraffic.GetManifest(_type);
            if (pc != null && mf != null && pc.WalkMode && !Driving.Active)
            {
                var local = _go.transform.InverseTransformPoint(pc.transform.position);
                if (Mathf.Abs(local.x) < mf.width * 0.5f + 0.55f && Mathf.Abs(local.z) < mf.length * 0.5f + 0.7f)
                    pc.Teleport(_go.transform.position.x + mf.width * 0.5f + 1.4f, 2.0f, _go.transform.position.z + 1.0f);
            }
            _rec = VehicleTraffic.RegisterDelivered(_type, _go);
            _phase = Phase.Arrived;
            _blink = 0f;
            ApplyLights(new Vector3(1f, 1f, 1f));
            Minimap.ExtraMarker = _go.transform.position;
            float dist = CarDistance();
            GameplayHud.DispatchToast("YOUR RIDE IS HERE",
                _entry.Name + " · " + Mathf.RoundToInt(dist) + " M · WALK OVER");
        }

        static void ApplyLights(Vector3 lv)
        {
            if (_mr == null || _entry == null) return;
            _mpb.SetColor("_PaintColor", _entry.Palette[Mathf.Clamp(_paint, 0, _entry.Palette.Length - 1)]);
            _mpb.SetFloat("_Spin", _spin);
            _mpb.SetVector("_LightLv", lv);
            _mr.SetPropertyBlock(_mpb);
        }

        static float CarDistance()
        {
            var pc = PlayerController.I;
            if (pc == null || _go == null) return 0f;
            var a = pc.transform.position; var b = _go.transform.position;
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static float EtaNow()
        {
            if (_phase != Phase.EnRoute || _go == null) return 0f;
            return EtaFor(Mathf.Abs(_zStop - _z));
        }

        static float Progress()
        {
            if (_phase == Phase.Arrived || _phase == Phase.Done) return 1f;
            if (_phase != Phase.EnRoute) return 0f;
            return Mathf.Clamp01(1f - Mathf.Abs(_zStop - _z) / Mathf.Max(1f, _run));
        }

        // ---------------------------------------------------------------- phone input
        public static void UpdatePhone(float dt)
        {
            if (!_phone) return;
            if (Time.frameCount == _enterFrame) return;   // P opened it: don't also close it

            int n = CarCatalog.Count;
            EnsurePaintSel();
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape)) { ClosePhone(); return; }
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) _sel = (_sel - 1 + n) % n;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) _sel = (_sel + 1) % n;
            var pal = CarCatalog.Ground[_sel].Palette;
            if (pal.Length > 1 && (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)))
                _paintSel[_sel] = (_paintSel[_sel] - 1 + pal.Length) % pal.Length;
            if (pal.Length > 1 && (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)))
                _paintSel[_sel] = (_paintSel[_sel] + 1) % pal.Length;

            if (Pending && Input.GetKeyDown(KeyCode.X)) Cancel();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (Pending) Msg("A DELIVERY IS ALREADY IN PROGRESS");
                else if (Order(_sel, _paintSel[_sel])) ClosePhone();
            }
        }

        // ---------------------------------------------------------------- debug / tests
        public static string StateText()
        {
            var p = _go != null ? _go.transform.position : Vector3.zero;
            int eddies = Gameplay.Gigs != null ? Gameplay.Gigs.Eddies : -1;
            return "phase=" + _phase + " phone=" + _phone + " type=" + (_type ?? "-") +
                   " paint=" + _paint + " name=" + (CarName ?? "-") +
                   " pos=" + p.ToString("F1") + " stop=" + _stopPos.ToString("F1") +
                   " remaining=" + (Pending ? Mathf.Abs(_zStop - _z).ToString("F1") : "-") +
                   " eta=" + (EnRoute ? FmtEta(EtaNow()) : "-") +
                   " run=" + _run.ToString("F1") + " dock=" + _dockX.ToString("F2") +
                   " fee=" + _fee + " welcome=" + _welcome + " eddies=" + eddies +
                   " registered=" + (_rec != null) + " drop=" + (_drop ?? "-") +
                   " driving=" + (Driving.Active ? Driving.CarName : "-");
        }

        public static string DebugWarp()
        {
            if (_phase != Phase.EnRoute || _go == null) return "warp: not en route";
            _z = _zStop - _d * 2.5f;
            _x = Mathf.Lerp(_laneX, _dockX, 0.8f);
            _v = 2.0f;
            _lastPos = new Vector3(_x, 0f, _z);
            _go.transform.position = _lastPos;
            return "warped " + _lastPos.ToString("F1") + " / remaining " + Mathf.Abs(_zStop - _z).ToString("F1");
        }

        public static string DebugArrive()
        {
            if (_phase != Phase.EnRoute || _go == null) return "arrive: not en route";
            _z = _zStop;
            Arrive();
            return StateText();
        }

        // ================================================================ HUD
        static float Z => Mathf.Clamp(Screen.height / 1080f, 0.55f, 2.2f);
        static readonly Color KY = new Color(0.988f, 0.929f, 0.039f);
        static readonly Color KC = new Color(0.369f, 0.965f, 1f);
        static readonly Color KW = new Color(0.914f, 0.965f, 0.969f);
        static readonly Color KR = new Color(1f, 0.382f, 0.345f);

        static Texture2D _white, _discTex, _bodyTex;
        static int _bodyW, _bodyH;
        static Font _font;
        static GUIStyle _tag, _title, _sub, _name, _nameSel, _klass, _feeStyle, _small, _blurb,
                        _btn, _btnDark, _btnWarn, _hint, _warn, _wallet, _chipLabel;

        public static void DrawHud()
        {
            if (!Pending && !_phone) return;
            if (!_phone && !GameplayHud.Enabled) return;
            EnsureStyles();
            if (_phone) { DrawPhone(); return; }
            DrawStatusStrip();
            if (!Driving.Active) DrawWorldMarker();
        }

        static void DrawStatusStrip()
        {
            float z = Z, H = Screen.height;
            float w = 344f * z, h = 88f * z, x = 34f * z, y = H - 250f * z;
            bool here = _phase == Phase.Arrived;
            Fill(new Rect(x, y, w, h), new Color(0.02f, 0.008f, 0.012f, 0.72f));
            Fill(new Rect(x, y, 3f * z, h), here ? KY : KC);
            Border(new Rect(x, y, w, h), new Color(KC.r, KC.g, KC.b, 0.22f), 1);

            Label(new Rect(x + 12f * z, y + 7f * z, w - 24f * z, 12f * z),
                "NX-9 DISPATCH · " + (here ? "ARRIVED" : "EN ROUTE"), _tag);
            Label(new Rect(x + 12f * z, y + 22f * z, w - 70f * z, 22f * z), _entry != null ? _entry.Name : "VEHICLE", _name);
            float d = CarDistance();
            string line = here
                ? (d > 4.5f ? "WALK OVER · " + Mathf.RoundToInt(d) + " M · PRESS F" : "PRESS F TO DRIVE")
                : "ETA " + FmtEta(EtaNow()) + " · " + Mathf.RoundToInt(d) + " M AWAY";
            Label(new Rect(x + 12f * z, y + 46f * z, w - 90f * z, 16f * z), line, _small);
            _tag.alignment = TextAnchor.MiddleRight;
            Label(new Rect(x + w - 76f * z, y + 10f * z, 64f * z, 14f * z), "P PHONE", _tag);
            _tag.alignment = TextAnchor.UpperLeft;

            // progress bar
            float px = x + 12f * z, pw = w - 24f * z, py = y + h - 14f * z;
            Fill(new Rect(px, py, pw, 2f * z), new Color(KC.r, KC.g, KC.b, 0.16f));
            Fill(new Rect(px, py, pw * Progress(), 2f * z), here ? KY : KC);
        }

        static void DrawWorldMarker()
        {
            var cam = Camera.main;
            if (cam == null || _go == null) return;
            float z = Z, W = Screen.width, H = Screen.height;
            var mf = VehicleTraffic.GetManifest(_type);
            float hgt = mf != null ? mf.height : 1.4f;
            Vector3 wp = _go.transform.position + Vector3.up * (hgt + 1.1f);
            var vp = cam.WorldToViewportPoint(wp);
            bool vis = vp.z > 0.5f;
            float sx = vp.x * W, sy = (1f - vp.y) * H;
            if (!vis) { sx = W - sx; sy = H - sy; }
            float m = 64f * z;
            bool edge = false;
            if (sx < m || sx > W - m || sy < m || sy > H - m)
            {
                edge = true;
                float cx = W * 0.5f, cy = H * 0.5f;
                var dir = new Vector2(sx - cx, sy - cy);
                if (dir.sqrMagnitude < 1f) dir = Vector2.right;
                float hm = W * 0.5f - 80f * z, vm = H * 0.5f - 80f * z;
                float k = Mathf.Min(Mathf.Abs(hm / dir.x), Mathf.Abs(vm / dir.y));
                sx = cx + dir.x * k; sy = cy + dir.y * k;
            }
            if (!vis && !edge) return;

            bool here = _phase == Phase.Arrived;
            var col = here ? KY : KC;
            var c = new Vector2(sx, sy);
            DrawDisc(c, 12f * z, new Color(col.r, col.g, col.b, 0.12f));
            var mv = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            Fill(new Rect(sx - 5f * z, sy - 5f * z, 10f * z, 10f * z), new Color(col.r, col.g, col.b, vis ? 0.95f : 0.7f));
            GUI.matrix = mv;
            DrawDisc(c, 2.4f * z, new Color(0.02f, 0.06f, 0.07f));
            _tag.alignment = TextAnchor.UpperCenter;
            _tag.normal.textColor = new Color(col.r, col.g, col.b, 0.9f);
            Label(new Rect(sx - 40f * z, sy + 10f * z, 80f * z, 12f * z), "NX-9 · " + Mathf.RoundToInt(CarDistance()) + "M", _tag);
            _tag.alignment = TextAnchor.UpperLeft;
            _tag.normal.textColor = new Color(KC.r, KC.g, KC.b, 0.72f);
        }

        // ---------------------------------------------------------------- phone draw
        static void DrawPhone()
        {
            float z = Z, W = Screen.width, H = Screen.height;
            Fill(new Rect(0, 0, W, H), new Color(0.005f, 0.012f, 0.016f, 0.58f));

            float pw = 430f * z, ph = Mathf.Min(830f * z, H - 36f * z);
            float px = W * 0.5f - pw * 0.5f, py = (H - ph) * 0.5f - 6f * z;
            GUI.DrawTexture(new Rect(px, py, pw, ph), PhoneBody(pw, ph));
            // side key + a thin power LED on the body
            Fill(new Rect(px + pw - 1f * z, py + 152f * z, 3f * z, 56f * z), new Color(0.06f, 0.10f, 0.12f, 0.95f));
            Fill(new Rect(px + pw * 0.5f - 20f * z, py + 5f * z, 40f * z, 1f * z), new Color(KC.r, KC.g, KC.b, 0.35f));

            var sr = new Rect(px + 14f * z, py + 14f * z, pw - 28f * z, ph - 28f * z);
            Fill(sr, new Color(0.008f, 0.022f, 0.028f, 0.99f));
            Border(sr, new Color(KC.r, KC.g, KC.b, 0.16f), 1);
            // notch
            var notch = new Rect(px + pw * 0.5f - 62f * z, py + 14f * z, 124f * z, 17f * z);
            Fill(notch, new Color(0.005f, 0.012f, 0.016f));
            Fill(new Rect(notch.center.x - 19f * z, notch.y + 7f * z, 38f * z, 2.4f * z), new Color(0.10f, 0.16f, 0.18f));
            Fill(new Rect(notch.x + 23f * z, notch.y + 6f * z, 5f * z, 5f * z), new Color(0.12f, 0.35f, 0.4f));

            // scanlines over the screen (cheap, 1px every 4)
            for (float sy = sr.y; sy < sr.yMax; sy += 4f * z)
                Fill(new Rect(sr.x, sy, sr.width, Mathf.Min(1f, sr.yMax - sy)), new Color(0f, 0f, 0f, 0.12f));

            float cx = sr.x + 20f * z, cw = sr.width - 40f * z;
            float y = sr.y + 30f * z;

            // status bar
            Label(new Rect(cx, y, cw * 0.5f, 12f * z), "NX-9 // MOBILE", _tag);
            _tag.alignment = TextAnchor.MiddleRight;
            Label(new Rect(cx + cw - 112f * z, y, 70f * z, 12f * z), GameplayHud.ClockString, _chipLabel);
            _tag.alignment = TextAnchor.UpperLeft;
            // signal + battery at the far right
            float bx = cx + cw - 36f * z;
            for (int i = 0; i < 3; i++)
                Fill(new Rect(bx + i * 4f * z, y + 9f * z - (i + 1) * 2.4f * z, 2.4f * z, (i + 1) * 2.4f * z),
                     new Color(KC.r, KC.g, KC.b, 0.8f));
            var batt = new Rect(cx + cw - 22f * z, y + 2f * z, 20f * z, 9f * z);
            Border(batt, new Color(KW.r, KW.g, KW.b, 0.5f), 1);
            Fill(new Rect(batt.x + 2f * z, batt.y + 2f * z, 13f * z, 5f * z), new Color(KY.r, KY.g, KY.b, 0.85f));
            Fill(new Rect(batt.xMax, batt.y + 3f * z, 2f * z, 3f * z), new Color(KW.r, KW.g, KW.b, 0.5f));
            y += 20f * z;

            // header
            Label(new Rect(cx, y, cw, 12f * z), "NX-9 // CONCIERGE", _tag);
            y += 12f * z;
            Label(new Rect(cx, y, cw, 32f * z), "VEHICLE DISPATCH", _title);
            y += 32f * z;
            Label(new Rect(cx, y, cw, 12f * z), "KABUKICHO-9 · 24H · AUTONOMOUS FLEET", _sub);
            y += 16f * z;
            Fill(new Rect(cx, y, cw, 1f * z), new Color(KY.r, KY.g, KY.b, 0.55f));
            y += 8f * z;

            // wallet
            int eddies = Gameplay.Gigs != null ? Gameplay.Gigs.Eddies : 0;
            Label(new Rect(cx, y, cw * 0.6f, 18f * z), "€$ " + eddies.ToString("N0"), _wallet);
            _tag.alignment = TextAnchor.MiddleRight;
            Label(new Rect(cx + cw * 0.4f, y + 2f * z, cw * 0.6f, 14f * z),
                _welcome ? "FIRST DISPATCH FREE" : "DISPATCH FEES APPLY", _tag);
            _tag.alignment = TextAnchor.UpperLeft;
            y += 22f * z;

            // active dispatch banner
            if (Pending && _entry != null)
            {
                float bh = 48f * z;
                var br = new Rect(cx, y, cw, bh);
                Fill(br, new Color(0.03f, 0.05f, 0.06f, 0.9f));
                Fill(new Rect(br.x, br.y, 3f * z, bh), _phase == Phase.Arrived ? KY : KC);
                Border(br, new Color(KC.r, KC.g, KC.b, 0.25f), 1);
                Label(new Rect(br.x + 10f * z, br.y + 5f * z, br.width - 20f * z, 12f * z),
                    (_phase == Phase.Arrived ? "ARRIVED · KERB" : "EN ROUTE · ETA " + FmtEta(EtaNow())) +
                    "  ·  " + Mathf.RoundToInt(CarDistance()) + " M", _tag);
                Label(new Rect(br.x + 10f * z, br.y + 19f * z, br.width - 20f * z, 18f * z),
                    _entry.Name + " · " + _entry.PaintName(_paint), _name);
                float p = Progress();
                Fill(new Rect(br.x + 10f * z, br.yMax - 7f * z, br.width - 20f * z, 2f * z), new Color(KC.r, KC.g, KC.b, 0.16f));
                Fill(new Rect(br.x + 10f * z, br.yMax - 7f * z, (br.width - 20f * z) * p, 2f * z), _phase == Phase.Arrived ? KY : KC);
                _tag.alignment = TextAnchor.MiddleRight;
                Label(new Rect(br.xMax - 100f * z, br.y + 5f * z, 90f * z, 12f * z),
                    _phase == Phase.EnRoute ? "X CANCEL" : "X DISMISS", _tag);
                _tag.alignment = TextAnchor.UpperLeft;
                y += bh + 10f * z;
            }

            // list
            Label(new Rect(cx, y, cw * 0.6f, 12f * z), "SELECT UNIT", _tag);
            _tag.alignment = TextAnchor.MiddleRight;
            Label(new Rect(cx + cw * 0.4f, y, cw * 0.6f, 12f * z), "AUTONOMOUS DELIVERY", _tag);
            _tag.alignment = TextAnchor.UpperLeft;
            y += 16f * z;

            for (int i = 0; i < CarCatalog.Count; i++)
            {
                var e = CarCatalog.Ground[i];
                bool sel = i == _sel;
                var row = new Rect(cx, y, cw, 48f * z);
                Fill(row, sel ? new Color(KY.r, KY.g, KY.b, 0.10f) : new Color(0.02f, 0.04f, 0.05f, 0.55f));
                if (sel)
                {
                    Fill(new Rect(row.x, row.y, 3f * z, row.height), KY);
                    Border(row, new Color(KY.r, KY.g, KY.b, 0.75f), 1);
                }
                Label(new Rect(row.x + 9f * z, row.y + 8f * z, 24f * z, 12f * z), (i + 1).ToString("00"), _tag);
                CarIcon(new Rect(row.x + 30f * z, row.y + 9f * z, 58f * z, 30f * z), e, e.Palette[Mathf.Clamp(_paintSel[i], 0, e.Palette.Length - 1)], z);
                Label(new Rect(row.x + 96f * z, row.y + 6f * z, row.width - 190f * z, 20f * z), e.Name, sel ? _nameSel : _name);
                Label(new Rect(row.x + 96f * z, row.y + 26f * z, row.width - 190f * z, 12f * z), e.Klass, _klass);
                _feeStyle.alignment = TextAnchor.MiddleRight;
                Label(new Rect(row.x + row.width - 92f * z, row.y + 15f * z, 82f * z, 18f * z),
                    _welcome ? "FREE" : CarCatalog.Money(e.DispatchFee), _feeStyle);
                y += 52f * z;
            }

            // detail card
            var ce = CarCatalog.Ground[_sel];
            int cp = Mathf.Clamp(_paintSel[_sel], 0, ce.Palette.Length - 1);
            var card = new Rect(cx, y, cw, 158f * z);
            Fill(card, new Color(0.015f, 0.03f, 0.037f, 0.78f));
            Fill(new Rect(card.x, card.y, card.width, 1.5f * z), new Color(KY.r, KY.g, KY.b, 0.5f));
            CarIcon(new Rect(card.x + 12f * z, card.y + 16f * z, 112f * z, 54f * z), ce, ce.Palette[cp], z);
            Label(new Rect(card.x + 134f * z, card.y + 10f * z, card.width - 146f * z, 20f * z), ce.Name, _name);
            Label(new Rect(card.x + 134f * z, card.y + 29f * z, card.width - 146f * z, 12f * z),
                ce.Klass + " · REAR-WHEEL DRIVE", _klass);
            Label(new Rect(card.x + 134f * z, card.y + 43f * z, card.width - 146f * z, 24f * z), ce.Blurb, _blurb);
            Label(new Rect(card.x + 12f * z, card.y + 76f * z, card.width - 24f * z, 12f * z),
                ce.Mass.ToString("N0") + " KG  ·  " + Mathf.RoundToInt(ce.Top * 3.6f) + " KM/H  ·  " +
                (VehicleTraffic.GetManifest(ce.Type)?.length ?? 4.5f).ToString("0.00") + " M  ·  RWD", _small);

            Label(new Rect(card.x + 12f * z, card.y + 96f * z, card.width - 24f * z, 12f * z),
                "PAINT · " + ce.PaintName(cp) + "  " + (cp + 1) + "/" + ce.Palette.Length, _tag);
            float chx = card.x + 12f * z, chy = card.y + 112f * z, ch = 15f * z;
            for (int i = 0; i < ce.Palette.Length && i < 10; i++)
            {
                var chip = new Rect(chx + i * 22f * z, chy, ch, ch);
                Fill(chip, ce.Palette[i]);
                Border(chip, i == cp ? KY : new Color(0f, 0f, 0f, 0.7f), i == cp ? 2 : 1);
            }
            _tag.alignment = TextAnchor.MiddleRight;
            Label(new Rect(card.x + card.width - 170f * z, card.y + 108f * z, 158f * z, 14f * z),
                "DROP · " + (Pending ? _drop : _previewDrop), _tag);
            _tag.alignment = TextAnchor.UpperLeft;
            Label(new Rect(card.x + 12f * z, card.y + 134f * z, card.width - 24f * z, 12f * z),
                Pending ? "DRIVER ASSIGNED · HAZARDS ON AT THE KERB" : "EST. ARRIVAL " + FmtEta(Pending ? EtaNow() : EtaFor(_previewRun)), _small);
            y += card.height + 8f * z;

            // button
            bool canPay = _welcome || Gameplay.Gigs == null || Gameplay.Gigs.Eddies >= ce.DispatchFee;
            var btn = new Rect(cx, y, cw, 50f * z);
            if (Pending)
            {
                Fill(btn, new Color(0.03f, 0.05f, 0.06f, 0.9f));
                Border(btn, _phase == Phase.EnRoute ? new Color(KR.r, KR.g, KR.b, 0.8f) : new Color(KC.r, KC.g, KC.b, 0.6f), 1);
                _btnDark.normal.textColor = _phase == Phase.EnRoute ? KR : KC;
                Label(btn, _phase == Phase.EnRoute ? "CANCEL DISPATCH   [ X ]" : "YOUR RIDE IS WAITING AT THE KERB", _btnDark);
            }
            else if (canPay)
            {
                Fill(btn, new Color(KY.r, KY.g, KY.b, 0.94f));
                Fill(new Rect(btn.x, btn.y, 5f * z, btn.height), new Color(1f, 1f, 1f, 0.85f));
                Label(btn, "DISPATCH NOW   ·   " + (_welcome ? "FREE" : CarCatalog.Money(ce.DispatchFee)), _btn);
            }
            else
            {
                Fill(btn, new Color(0.05f, 0.02f, 0.025f, 0.9f));
                Border(btn, new Color(KR.r, KR.g, KR.b, 0.8f), 1);
                Label(btn, "INSUFFICIENT EDDIES", _btnWarn);
            }
            y += 50f * z;

            // route graphic
            y += 16f * z;
            float lineY = y + 8f * z;
            Label(new Rect(cx, y - 10f * z, cw, 10f * z), Pending ? "DRIVER ROUTE" : "DISPATCH ROUTE", _tag);
            Fill(new Rect(cx + 4f * z, lineY, cw - 8f * z, 1.5f * z), new Color(KC.r, KC.g, KC.b, 0.25f));
            for (int i = 0; i <= 5; i++)
            {
                float dx = cx + 4f * z + (cw - 8f * z) * i / 5f;
                DrawDisc(new Vector2(dx, lineY + 0.75f * z), 2.4f * z, new Color(KC.r, KC.g, KC.b, 0.55f));
            }
            float pr = Progress();
            float mdx = cx + 4f * z + (cw - 8f * z) * pr;
            DrawDisc(new Vector2(mdx, lineY + 0.75f * z), 4.2f * z, _phase == Phase.Arrived ? KY : KC);
            _tag.alignment = TextAnchor.MiddleLeft;
            Label(new Rect(cx, lineY + 6f * z, cw * 0.5f, 12f * z), "DEPOT", _tag);
            _tag.alignment = TextAnchor.MiddleRight;
            Label(new Rect(cx + cw * 0.5f, lineY + 6f * z, cw * 0.5f, 12f * z), "YOUR KERB", _tag);
            _tag.alignment = TextAnchor.UpperLeft;

            // concierge footer block + a decorative barcode
            y = lineY + 24f * z;
            Fill(new Rect(cx, y, cw, 1f * z), new Color(KC.r, KC.g, KC.b, 0.14f));
            y += 6f * z;
            Label(new Rect(cx, y, cw, 10f * z), "NX-9 CONCIERGE · FLEET PROTOCOL", _tag);
            Label(new Rect(cx, y + 12f * z, cw * 0.7f, 10f * z), "AUTONOMOUS DRIVER · LIVE TRAFFIC ROUTING", _sub);
            Label(new Rect(cx, y + 24f * z, cw * 0.7f, 10f * z), "DISPATCH FEES FROM €$120 · 24H CONCIERGE", _tag);
            for (int i = 0; i < 22; i++)
            {
                float bw = (i % 3 == 0) ? 2.2f * z : (i % 5 == 0 ? 1.4f * z : 0.9f * z);
                Fill(new Rect(cx + cw - 74f * z + i * 3.1f * z, y + 2f * z, bw, 26f * z),
                     new Color(KW.r, KW.g, KW.b, 0.24f));
            }

            // footer (an error message temporarily replaces the key hints) + home bar
            if (_phoneMsgT > 0f)
            {
                _warn.alignment = TextAnchor.MiddleCenter;
                Label(new Rect(sr.x, sr.yMax - 22f * z, sr.width, 14f * z), _phoneMsg, _warn);
                _warn.alignment = TextAnchor.UpperLeft;
            }
            else
            {
                _hint.alignment = TextAnchor.MiddleCenter;
                Label(new Rect(sr.x, sr.yMax - 22f * z, sr.width, 14f * z),
                    "↑↓ UNIT    ←→ PAINT    ENTER DISPATCH    " + (EnRoute ? "X CANCEL    " : Pending ? "X DISMISS    " : "") + "P CLOSE", _hint);
                _hint.alignment = TextAnchor.UpperLeft;
            }
            Fill(new Rect(sr.center.x - 55f * z, sr.yMax - 7f * z, 110f * z, 3f * z), new Color(KW.r, KW.g, KW.b, 0.28f));
        }

        // A cheap side-view silhouette: wheels, body, cabin/box, glass strip, lamps.
        static void CarIcon(Rect r, CarCatalog.Entry e, Color paint, float z)
        {
            EnsureStyles();
            float w = r.width, h = r.height;
            float wheelY = r.yMax - 4f * z;
            var wheel = new Color(0.02f, 0.025f, 0.03f, 0.95f);
            float wr = Mathf.Max(2.5f, 5.2f * z);
            DrawDisc(new Vector2(r.x + w * 0.22f, wheelY), wr, wheel);
            DrawDisc(new Vector2(r.x + w * 0.78f, wheelY), wr, wheel);
            DrawDisc(new Vector2(r.x + w * 0.22f, wheelY), wr * 0.45f, new Color(0.22f, 0.26f, 0.28f));
            DrawDisc(new Vector2(r.x + w * 0.78f, wheelY), wr * 0.45f, new Color(0.22f, 0.26f, 0.28f));

            float bodyTop, bodyH;
            bool box = e.Type == "van";
            if (box) { bodyTop = r.y + h * 0.26f; bodyH = h * 0.46f; }
            else if (e.Type == "kei") { bodyTop = r.y + h * 0.34f; bodyH = h * 0.36f; }
            else if (e.Type == "hero") { bodyTop = r.y + h * 0.46f; bodyH = h * 0.26f; }
            else { bodyTop = r.y + h * 0.40f; bodyH = h * 0.30f; }

            var body = new Rect(r.x + w * 0.06f, bodyTop, w * 0.88f, bodyH);
            Fill(body, paint);
            Border(body, new Color(Mathf.Min(1f, paint.r + 0.18f), Mathf.Min(1f, paint.g + 0.18f), Mathf.Min(1f, paint.b + 0.18f), 0.8f), 1);

            float cabW = box ? w * 0.30f : e.Type == "hero" ? w * 0.44f : w * 0.50f;
            float cabH = box ? h * 0.30f : e.Type == "kei" ? h * 0.28f : h * 0.22f;
            float cabX = box ? r.x + w * 0.10f : r.x + (w - cabW) * 0.5f;
            float cabY = bodyTop - cabH + 1f * z;
            var cab = new Rect(cabX, cabY, cabW, cabH);
            Fill(cab, paint);
            Border(cab, new Color(0f, 0f, 0f, 0.25f), 1);
            var glass = new Color(0.09f, 0.20f, 0.24f, 0.95f);
            Fill(new Rect(cab.x + 2f * z, cab.y + 2f * z, cab.width - 4f * z, Mathf.Max(2f, cab.height * 0.55f)), glass);
            if (box)
                Fill(new Rect(r.x + w * 0.10f, bodyTop - h * 0.02f, w * 0.26f, h * 0.20f), glass);

            Fill(new Rect(r.x + w * 0.08f, bodyTop + bodyH * 0.22f, 2.5f * z, Mathf.Max(1.5f, bodyH * 0.22f)),
                 new Color(1f, 0.42f, 0.32f, 0.95f));
            Fill(new Rect(r.x + w * 0.91f, bodyTop + bodyH * 0.22f, 2.5f * z, Mathf.Max(1.5f, bodyH * 0.22f)),
                 new Color(1f, 0.92f, 0.72f, 0.95f));
        }

        // rounded body texture, cached by the drawn size
        static Texture2D PhoneBody(float w, float h)
        {
            int kw = Mathf.RoundToInt(w), kh = Mathf.RoundToInt(h);
            if (_bodyTex != null && _bodyW == kw && _bodyH == kh) return _bodyTex;
            if (_bodyTex != null) Object.Destroy(_bodyTex);
            _bodyW = kw; _bodyH = kh;

            int tw = Mathf.Clamp(Mathf.RoundToInt(w * 0.5f), 80, 560);
            int th = Mathf.Clamp(Mathf.RoundToInt(h * 0.5f), 80, 1100);
            float rad = Mathf.Max(5f, Mathf.Min(tw, th) * 0.030f);
            var px = new Color32[tw * th];
            var bodyA = new Color(0.055f, 0.075f, 0.090f);
            var bodyB = new Color(0.010f, 0.018f, 0.024f);
            var edge = new Color(0.24f, 0.62f, 0.68f);
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    float qx = Mathf.Max(rad - (x + 0.5f), (x + 0.5f) - (tw - rad), 0f);
                    float qy = Mathf.Max(rad - (y + 0.5f), (y + 0.5f) - (th - rad), 0f);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - rad;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (a <= 0f) { px[y * tw + x] = new Color32(0, 0, 0, 0); continue; }
                    Color c = d > -1.7f ? edge : Color.Lerp(bodyB, bodyA, y / (float)th);
                    if (d > -3.2f && d <= -1.7f) c = Color.Lerp(c, new Color(0.02f, 0.03f, 0.035f), 0.55f);
                    px[y * tw + x] = new Color32(
                        (byte)Mathf.RoundToInt(c.r * 255f), (byte)Mathf.RoundToInt(c.g * 255f),
                        (byte)Mathf.RoundToInt(c.b * 255f), (byte)Mathf.RoundToInt(a * 255f));
                }
            _bodyTex = new Texture2D(tw, th, TextureFormat.RGBA32, false)
            { name = "nk-phone-body", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            _bodyTex.SetPixels32(px);
            _bodyTex.Apply(false, false);
            return _bodyTex;
        }

        static void EnsureStyles()
        {
            if (_font == null)
            {
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "nk-dl-white", hideFlags = HideFlags.DontSave };
                var cs = new Color[16];
                for (int i = 0; i < 16; i++) cs[i] = Color.white;
                _white.SetPixels(cs); _white.Apply(false, false);

                const int N = 64;
                _discTex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                { name = "nk-dl-disc", filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                var dpx = new Color32[N * N];
                float half = N / 2f, rr = half - 1.5f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = x + 0.5f - half, dy = y + 0.5f - half;
                        float a = Mathf.Clamp01(rr + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
                        dpx[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                _discTex.SetPixels32(dpx); _discTex.Apply(false, false);
            }
            if (_tag != null) return;

            float z = Z;
            GUIStyle Make(int size, Color c, FontStyle fs, TextAnchor an, bool wrap = false)
            {
                var s = new GUIStyle { font = _font, fontSize = Mathf.Max(7, Mathf.RoundToInt(size * z)), alignment = an, fontStyle = fs, richText = true };
                s.normal.textColor = c;
                s.stretchWidth = false;
                s.wordWrap = wrap;
                return s;
            }
            _tag = Make(9, new Color(KC.r, KC.g, KC.b, 0.72f), FontStyle.Normal, TextAnchor.UpperLeft);
            _title = Make(27, KY, FontStyle.Bold, TextAnchor.UpperLeft);
            _sub = Make(9, new Color(KC.r, KC.g, KC.b, 0.62f), FontStyle.Normal, TextAnchor.UpperLeft);
            _name = Make(16, KW, FontStyle.Bold, TextAnchor.UpperLeft);
            _nameSel = Make(16, KY, FontStyle.Bold, TextAnchor.UpperLeft);
            _klass = Make(8, new Color(KC.r, KC.g, KC.b, 0.7f), FontStyle.Normal, TextAnchor.UpperLeft);
            _feeStyle = Make(12, KY, FontStyle.Bold, TextAnchor.MiddleRight);
            _small = Make(9, new Color(KW.r, KW.g, KW.b, 0.78f), FontStyle.Normal, TextAnchor.UpperLeft);
            _blurb = Make(8, new Color(KW.r, KW.g, KW.b, 0.62f), FontStyle.Normal, TextAnchor.UpperLeft, true);
            _btn = Make(14, new Color(0.05f, 0.045f, 0.01f), FontStyle.Bold, TextAnchor.MiddleCenter);
            _btnDark = Make(12, KC, FontStyle.Bold, TextAnchor.MiddleCenter);
            _btnWarn = Make(13, KR, FontStyle.Bold, TextAnchor.MiddleCenter);
            _hint = Make(8, new Color(KW.r, KW.g, KW.b, 0.58f), FontStyle.Normal, TextAnchor.MiddleCenter);
            _warn = Make(9, KR, FontStyle.Bold, TextAnchor.MiddleCenter);
            _wallet = Make(16, KW, FontStyle.Bold, TextAnchor.UpperLeft);
            _chipLabel = Make(10, KW, FontStyle.Normal, TextAnchor.MiddleRight);
        }

        // ---------------------------------------------------------------- primitives
        static void Fill(Rect r, Color c)
        {
            if (_white == null) EnsureStyles();
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        static void Border(Rect r, Color c, int th)
        {
            Fill(new Rect(r.x, r.y, r.width, th), c);
            Fill(new Rect(r.x, r.yMax - th, r.width, th), c);
            Fill(new Rect(r.x, r.y, th, r.height), c);
            Fill(new Rect(r.xMax - th, r.y, th, r.height), c);
        }

        static void Label(Rect r, string s, GUIStyle st)
        {
            if (!string.IsNullOrEmpty(s)) GUI.Label(r, s, st);
        }

        static void DrawDisc(Vector2 c, float rad, Color col)
        {
            if (_discTex == null) EnsureStyles();
            var old = GUI.color; GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), _discTex);
            GUI.color = old;
        }
    }
}
