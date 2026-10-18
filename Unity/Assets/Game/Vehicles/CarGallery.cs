// NEON KAIDO — car gallery / showroom. A display-only lineup of every ground vehicle
// (glass, paint and lit lamps intact) staged on the avenue, with a catalog-ish HUD.
// Deliberately independent of traffic/parked cars: it loads the meshes, spawns its own
// cars under "CarGallery", hides the street traffic + crowd while open and parks a small
// camera rig on the PlayerController. G toggles from normal play.
//
// Public surface for the future NX-9 phone-app delivery flow:
//   Enter/Exit/Toggle, Focus(i), Overview(), Rear(bool), NextPaint(), ToggleLights(),
//   Selected/SelectedName, LightsOn, Active.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class CarGallery
    {
        public static bool Active { get; private set; }

        // ---------------------------------------------------------------- catalog data
        class Car
        {
            public string type, name, klass, blurb;
            public GameObject go;
            public MeshRenderer mr;
            public Color[] palette;
            public int paint;
            public float mass, top, length, width, height;
        }

        static readonly string[] Types = { "kei", "taxi", "hero", "van" };   // left -> right
        static readonly float[] Xs = { -6.2f, -2.1f, 2.0f, 6.1f };
        const float GalleryZ = 160f;                                          // Unity z on the avenue
        const float FaceYaw = 180f;                                           // nose toward the camera (south)

        static readonly List<Car> _cars = new();
        static Transform _root;
        static PlayerController _pc;
        static int _sel, _enterFrame = -1;
        static bool _built, _overview = true, _rear, _lights = true;

        static Vector3 _camPos, _camLook, _savedPos;
        static float _savedYaw, _savedPitch;
        static bool _snap = true;

        static GameObject _vehRoot, _crowdRoot;

        // showroom rig: painted cars get warm key / cyan rim / top glass light through the same
        // fake per-car point-light globals the traffic uses (NkBPointLights reads them).
        static readonly Vector4[] _rigPos = new Vector4[42];
        static readonly Vector4[] _rigCol = new Vector4[42];

        static void PushRig()
        {
            for (int i = 0; i < _rigPos.Length; i++) { _rigPos[i] = Vector4.zero; _rigCol[i] = Vector4.zero; }
            int n = 0;
            foreach (var c in _cars)
            {
                var p = c.go.transform.position;
                n = Lamp(n, new Vector3(p.x - 2.6f, 4.2f, p.z - 2.2f), 15f, new Color(1f, 0.86f, 0.70f), 26f);
                n = Lamp(n, new Vector3(p.x + 2.6f, 2.7f, p.z + 3.2f), 12f, new Color(0.45f, 0.78f, 1f), 18f);
                n = Lamp(n, new Vector3(p.x, 5.6f, p.z), 12f, new Color(0.92f, 0.95f, 1f), 16f);
                n = Lamp(n, new Vector3(p.x, 1.0f, p.z - 4.4f), 7f, new Color(1f, 0.66f, 0.46f), 7f);
            }
            Shader.SetGlobalFloat("_CarLCount", n);
            Shader.SetGlobalVectorArray("_CarLPos", _rigPos);
            Shader.SetGlobalVectorArray("_CarLColor", _rigCol);
        }

        static int Lamp(int n, Vector3 p, float range, Color col, float intensity)
        {
            if (n >= 42) return n;
            _rigPos[n] = new Vector4(p.x, p.y, p.z, range);
            _rigCol[n] = new Vector4(col.r, col.g, col.b, intensity);
            return n + 1;
        }

        // gui
        static Texture2D _white;
        static Font _font;
        static GUIStyle _tiny, _tag, _title, _name, _nameSel, _klass, _big, _hint, _label, _specK, _specV, _blurb;
        static float Z => Mathf.Clamp(Screen.height / 1080f, 0.55f, 2.2f);

        static readonly Color KY = new Color(0.988f, 0.929f, 0.039f);
        static readonly Color KC = new Color(0.369f, 0.965f, 1f);
        static readonly Color KW = new Color(0.914f, 0.965f, 0.969f);
        static readonly Color KR = new Color(1f, 0.382f, 0.345f);
        static readonly Color KDark = new Color(0.03f, 0.01f, 0.015f, 0.8f);

        // ================================================================ lifecycle
        public static void Toggle()
        {
            if (Active) Exit();
            else Enter();
        }

        public static void Enter()
        {
            if (Active) return;
            if (NeonKaido.Driving.Active)
            {
                GameplayHud.Note("GALLERY UNAVAILABLE", "PARK THE VEHICLE BEFORE ENTERING THE SHOWROOM");
                return;
            }
            if (NeonKaido.Delivery.Pending)
            {
                GameplayHud.Note("SHOWROOM LOCKED", "NX-9 DELIVERY IN PROGRESS — PRESS P TO CHECK IT");
                return;
            }
            NeonKaido.Delivery.ClosePhone();
            _pc = PlayerController.I;
            if (_pc == null) return;
            var err = VehicleTraffic.LoadModels();
            if (err != null || !VehicleTraffic.ModelsReady)
            {
                GameplayHud.Note("GALLERY ERROR", err ?? "VEHICLE MODELS MISSING");
                return;
            }
            if (!_built || _root == null || _cars.Count == 0 || _cars[0].go == null) Build();

            _savedPos = _pc.transform.position;
            _savedYaw = _pc.Yaw;
            _savedPitch = _pc.Pitch;

            HideWorld(true);
            VehicleTraffic.ClearCarLightGlobals();
            PushRig();

            _overview = true; _rear = false; _sel = 0; _lights = true; _snap = true;
            foreach (var c in _cars) Apply(c);
            if (_root != null) _root.gameObject.SetActive(true);

            Active = true;
            _enterFrame = Time.frameCount;   // G opened the gallery: don't let the same press close it
            _pc.ReleaseCursor();
            Update(0f);   // snap the first camera pose + apply the pose before the first frame
        }

        public static void Exit()
        {
            if (!Active) return;
            Active = false;
            HideWorld(false);
            if (_root != null) _root.gameObject.SetActive(false);
            if (_pc != null)
            {
                _pc.RestorePose(_savedPos, _savedYaw, _savedPitch);
                _pc.ReleaseCursor();
            }
            var cam = Camera.main;
            if (cam != null) cam.fieldOfView = 75f;
        }

        static void Build()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            var root = new GameObject("CarGallery");
            _root = root.transform;
            _cars.Clear();
            for (int i = 0; i < Types.Length; i++)
            {
                string type = Types[i];
                var e = CarCatalog.Get(type);
                if (e == null) continue;
                var pal = e.Palette;
                int paint = Mathf.Clamp(e.DefaultPaint, 0, Mathf.Max(0, pal.Length - 1));
                var go = VehicleTraffic.SpawnShowcase(_root, type, new Vector3(Xs[i], 0f, GalleryZ), FaceYaw,
                                                       pal[paint], new Vector3(1f, 1f, 0f));
                if (go == null) continue;
                var mf = VehicleTraffic.GetManifest(type);
                _cars.Add(new Car
                {
                    type = type,
                    name = e.Name,
                    klass = e.Klass,
                    blurb = e.Blurb,
                    mass = e.Mass,
                    top = e.Top,
                    length = mf != null ? mf.length : 4.5f,
                    width = mf != null ? mf.width : 1.9f,
                    height = mf != null ? mf.height : 1.5f,
                    go = go,
                    mr = go.GetComponent<MeshRenderer>(),
                    palette = pal,
                    paint = paint,
                });
            }
            _root.gameObject.SetActive(false);
            _built = true;
        }

        static void Apply(Car c)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_PaintColor", c.palette[c.paint]);
            mpb.SetFloat("_Spin", 0f);
            mpb.SetVector("_LightLv", _lights ? new Vector3(1f, 1f, 0f) : Vector3.zero);
            c.mr.SetPropertyBlock(mpb);
        }

        static void HideWorld(bool hide)
        {
            if (hide)
            {
                if (_vehRoot == null) _vehRoot = GameObject.Find("Vehicles");
                if (_crowdRoot == null) _crowdRoot = GameObject.Find("Crowd");
            }
            if (_vehRoot != null) _vehRoot.SetActive(!hide);
            if (_crowdRoot != null) _crowdRoot.SetActive(!hide);
            // an already-delivered car lives outside the Vehicles root; tuck it away too so it
            // never photobombs the line-up (pending/being-driven deliveries are gated out above)
            var delivered = NeonKaido.Delivery.CarObject;
            if (delivered != null) delivered.SetActive(!hide);
        }

        // ================================================================ per-frame
        public static void Update(float dt)
        {
            if (!Active) return;
            bool entryFrame = Time.frameCount == _enterFrame;

            if (!entryFrame)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Select(-1);
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Select(1);
                if (Input.GetKeyDown(KeyCode.V)) { _overview = !_overview; _snap = false; }
                if (Input.GetKeyDown(KeyCode.B)) { _rear = !_rear; _snap = false; }
                if (Input.GetKeyDown(KeyCode.C)) NextPaint();
                if (Input.GetKeyDown(KeyCode.L)) ToggleLights();
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.G)) { Exit(); return; }
            }

            PoseFor(_sel, _overview, _rear, out var tp, out var tl);
            float k = 1f - Mathf.Exp(-6f * dt);
            if (_snap) { _camPos = tp; _camLook = tl; _snap = false; }
            else
            {
                _camPos = Vector3.Lerp(_camPos, tp, k);
                _camLook = Vector3.Lerp(_camLook, tl, k);
            }

            var rot = Quaternion.LookRotation(_camLook - _camPos, Vector3.up);
            _pc.SetGalleryPose(_camPos, rot);
            var cam = Camera.main;
            if (cam != null) cam.fieldOfView = _overview ? 46f : 42f;
        }

        static void Select(int d)
        {
            if (_cars.Count == 0) return;
            Focus((_sel + d + _cars.Count) % _cars.Count);
        }

        // public hooks for the phone-app / tests: pick a unit, go back to the line-up, flip view
        public static void Focus(int index)
        {
            if (_cars.Count == 0 || index < 0 || index >= _cars.Count) return;
            _sel = index;
            _overview = false;
            _snap = false;
        }

        public static void Overview() { _overview = true; _snap = false; }
        public static void Rear(bool on) { _rear = on; _snap = false; }
        public static int Selected => _sel;
        public static string SelectedName => _cars.Count > 0 ? _cars[Mathf.Clamp(_sel, 0, _cars.Count - 1)].name : null;

        // public API for the future delivery app / tests
        public static void NextPaint()
        {
            if (_cars.Count == 0) return;
            var c = _cars[_sel];
            if (c.palette == null || c.palette.Length == 0) return;
            c.paint = (c.paint + 1) % c.palette.Length;
            Apply(c);
        }

        public static void ToggleLights()
        {
            _lights = !_lights;
            foreach (var c in _cars) Apply(c);
        }

        public static bool LightsOn => _lights;

        static void PoseFor(int i, bool overview, bool rear, out Vector3 pos, out Vector3 look)
        {
            if (overview || _cars.Count == 0)
            {
                pos = new Vector3(0.4f, 2.55f, 139.5f);
                look = new Vector3(1.3f, 1.0f, GalleryZ);
                return;
            }
            var c = _cars[Mathf.Clamp(i, 0, _cars.Count - 1)];
            float x = c.go.transform.position.x;
            float dz = c.length * 0.5f + 4.2f;
            float ox = x > 0f ? -3.1f : 3.1f;       // stay on the road side of the line-up
            pos = new Vector3(x + ox, 1.7f, GalleryZ + (rear ? dz : -dz));
            look = new Vector3(x, Mathf.Min(1.05f, c.height * 0.38f), GalleryZ + (rear ? -0.4f : 0.4f));
        }

        // ================================================================ HUD
        public static void DrawHud()
        {
            if (!Active) return;
            EnsureStyles();
            float z = Z, W = Screen.width, H = Screen.height;
            var cam = Camera.main;

            // --- floating placards under the line-up (drawn first so the HUD panels occlude them)
            if (_overview && cam != null)
            {
                for (int i = 0; i < _cars.Count; i++)
                {
                    var c = _cars[i];
                    if (c.go == null) continue;
                    var wp = c.go.transform.position + Vector3.up * 0.05f;
                    var vp = cam.WorldToScreenPoint(wp);
                    if (vp.z < 0.1f) continue;
                    float sx2 = vp.x, sy2 = H - vp.y;
                    bool sel = i == _sel;
                    _label.alignment = TextAnchor.UpperCenter;
                    _label.fontSize = Mathf.Max(10, Mathf.RoundToInt(13 * z));
                    _label.normal.textColor = sel ? KY : new Color(KW.r, KW.g, KW.b, 0.88f);
                    Label(new Rect(sx2 - 110 * z, sy2 + 10 * z, 220 * z, 22 * z), (i + 1).ToString("00") + "   " + c.name, _label);
                    Fill(new Rect(sx2 - 70 * z, sy2 + 7 * z, 140 * z, Mathf.Max(1, 1.5f * z)), sel ? KY : new Color(KC.r, KC.g, KC.b, 0.6f));
                }
                _label.alignment = TextAnchor.UpperLeft;
            }

            // --- header
            float x = 34f * z, y = 26f * z;
            Label(new Rect(x, y, 520 * z, 18 * z), "NEON KAIDO // NX-9 DISPATCH FEED", _tag);
            Label(new Rect(x, y + 20 * z, 620 * z, 46 * z), "VEHICLE CATALOG", _title);
            Fill(new Rect(x, y + 68 * z, 300 * z, 2 * z), KY);
            Label(new Rect(x, y + 76 * z, 640 * z, 18 * z), "KABUKICHO-9 SHOWROOM · ALL UNITS DISPLAY READY", _tiny);

            // --- list (right rail)
            float lw = 340f * z, lx = W - 34f * z - lw, ly = 176f * z;
            for (int i = 0; i < _cars.Count; i++)
            {
                var c = _cars[i];
                bool sel = i == _sel;
                var row = new Rect(lx, ly + i * 64f * z, lw, 56f * z);
                Fill(row, sel ? new Color(0.988f, 0.929f, 0.039f, 0.12f) : new Color(0.03f, 0.01f, 0.015f, 0.58f));
                if (sel) { Fill(new Rect(row.x, row.y, 3 * z, row.height), KY); Border(row, KY, 1); }
                Label(new Rect(row.x + 14 * z, row.y + 8 * z, 34 * z, 18 * z), (i + 1).ToString("00"), _tiny);
                Label(new Rect(row.x + 52 * z, row.y + 4 * z, lw - 64 * z, 28 * z), c.name, sel ? _nameSel : _name);
                Label(new Rect(row.x + 52 * z, row.y + 30 * z, lw - 64 * z, 18 * z), c.klass, _klass);
            }

            // --- detail card
            var c0 = _cars.Count > 0 ? _cars[_sel] : null;
            float dy = ly + _cars.Count * 64f * z + 16f * z;
            var card = new Rect(lx, dy, lw, 256f * z);
            Fill(card, KDark);
            Fill(new Rect(card.x, card.y, card.width, 2 * z), new Color(KY.r, KY.g, KY.b, 0.8f));
            if (c0 != null)
            {
                Label(new Rect(card.x + 16 * z, card.y + 12 * z, lw - 32 * z, 16 * z), "SELECTED UNIT  //  " + (char)('A' + _sel), _tiny);
                Label(new Rect(card.x + 16 * z, card.y + 28 * z, lw - 32 * z, 32 * z), c0.name, _big);
                _klass.alignment = TextAnchor.UpperLeft;
                Label(new Rect(card.x + 16 * z, card.y + 62 * z, lw - 32 * z, 18 * z), c0.klass + "  ·  REAR-WHEEL DRIVE", _klass);
                Label(new Rect(card.x + 16 * z, card.y + 80 * z, lw - 32 * z, 18 * z), c0.blurb, _blurb);

                float sx = card.x + 16 * z, sw = (lw - 46 * z) * 0.5f;
                Spec(new Rect(sx, card.y + 104 * z, sw, 34 * z), "MASS", c0.mass.ToString("N0") + " KG");
                Spec(new Rect(sx + sw + 14 * z, card.y + 104 * z, sw, 34 * z), "TOP SPEED", Mathf.RoundToInt(c0.top * 3.6f) + " KM/H");
                Spec(new Rect(sx, card.y + 138 * z, sw, 34 * z), "LENGTH", c0.length.ToString("0.00") + " M");
                Spec(new Rect(sx + sw + 14 * z, card.y + 138 * z, sw, 34 * z), "PAINT", (c0.paint + 1) + " / " + c0.palette.Length);

                // colour chips
                float chx = sx, chy = card.y + 178 * z, ch = 16 * z;
                for (int i = 0; i < c0.palette.Length && i < 10; i++)
                {
                    var chip = new Rect(chx + i * 24 * z, chy, ch, ch);
                    Fill(chip, c0.palette[i]);
                    if (i == c0.paint) Border(chip, KY, 2);
                }
                Label(new Rect(card.x + 16 * z, card.y + 202 * z, lw - 32 * z, 16 * z), "NX-9 DELIVERY  ·  FROM €$120  ·  PHONE APP", _tiny);
                Label(new Rect(card.x + 16 * z, card.y + 222 * z, lw - 32 * z, 18 * z), "DISPATCH FROM KABUKICHO-9 DEPOT", _klass);
            }

            // --- view mode tag
            string mode = _overview ? "OVERVIEW" : (_rear ? "REAR 3/4" : "FRONT 3/4");
            _tag.alignment = TextAnchor.UpperRight;
            Label(new Rect(W - 34 * z - 340 * z, 132 * z, 340 * z, 18 * z), "VIEW  ·  " + mode, _tag);
            _tag.alignment = TextAnchor.UpperLeft;

            // --- bottom hint bar
            float hy = H - 56 * z;
            Fill(new Rect(0, hy - 10 * z, W, 66 * z), new Color(0.02f, 0.008f, 0.012f, 0.62f));
            Fill(new Rect(0, hy - 10 * z, W, 1 * z), new Color(KY.r, KY.g, KY.b, 0.55f));
            _hint.alignment = TextAnchor.MiddleCenter;
            Label(new Rect(W * 0.5f - 620 * z, hy, 1240 * z, 36 * z),
                  "← →  SELECT     V  OVERVIEW     B  REAR VIEW     C  PAINT     L  LIGHTS     G / ESC  EXIT", _hint);
            _hint.alignment = TextAnchor.UpperLeft;
        }

        static void Spec(Rect r, string k, string v)
        {
            float z = Z;
            Label(new Rect(r.x, r.y, r.width, 14 * z), k, _specK);
            Label(new Rect(r.x, r.y + 14 * z, r.width, 20 * z), v, _specV);
        }

        static void Fill(Rect r, Color c)
        {
            if (_white == null)
            {
                _white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var cs = new Color[16];
                for (int i = 0; i < 16; i++) cs[i] = Color.white;
                _white.SetPixels(cs); _white.Apply();
            }
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

        static void EnsureStyles()
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_tiny != null) return;
            GUIStyle Make(int size, Color c, FontStyle fs, TextAnchor an)
            {
                var s = new GUIStyle { font = _font, fontSize = size, alignment = an, fontStyle = fs, richText = true };
                s.normal.textColor = c; s.stretchWidth = false;
                return s;
            }
            float z = Z;
            _tiny = Make(Mathf.RoundToInt(12 * z), new Color(KC.r, KC.g, KC.b, 0.9f), FontStyle.Normal, TextAnchor.UpperLeft);
            _tag = Make(Mathf.RoundToInt(12 * z), new Color(KY.r, KY.g, KY.b, 0.72f), FontStyle.Normal, TextAnchor.UpperLeft);
            _title = Make(Mathf.RoundToInt(38 * z), KY, FontStyle.Bold, TextAnchor.UpperLeft);
            _name = Make(Mathf.RoundToInt(20 * z), KW, FontStyle.Bold, TextAnchor.UpperLeft);
            _nameSel = Make(Mathf.RoundToInt(20 * z), KY, FontStyle.Bold, TextAnchor.UpperLeft);
            _klass = Make(Mathf.RoundToInt(11 * z), new Color(KC.r, KC.g, KC.b, 0.7f), FontStyle.Normal, TextAnchor.UpperLeft);
            _big = Make(Mathf.RoundToInt(28 * z), KY, FontStyle.Bold, TextAnchor.UpperLeft);
            _hint = Make(Mathf.RoundToInt(13 * z), new Color(KW.r, KW.g, KW.b, 0.9f), FontStyle.Normal, TextAnchor.MiddleCenter);
            _label = Make(Mathf.RoundToInt(11 * z), KW, FontStyle.Normal, TextAnchor.MiddleCenter);
            _specK = Make(Mathf.RoundToInt(10 * z), new Color(KR.r, KR.g, KR.b, 0.7f), FontStyle.Normal, TextAnchor.UpperLeft);
            _specV = Make(Mathf.RoundToInt(16 * z), KW, FontStyle.Bold, TextAnchor.UpperLeft);
            _blurb = Make(Mathf.RoundToInt(11 * z), new Color(KW.r, KW.g, KW.b, 0.72f), FontStyle.Normal, TextAnchor.UpperLeft);
            _blurb.wordWrap = true;
        }
    }
}
