// Port of src/systems/vehicles/ {models.js, traffic.js, index.js (parked/traffic/sky)}
// — parked cars along the kerbs, IDM car-following traffic with signal stops,
// and flying AV lanes. Cars = one shared mesh per type (part-tagged verts,
// shader-spun wheels), per-car transforms + MaterialPropertyBlock.
// All placement math in SIM space; mirrors at use (pos z->-z, yaw -> atan2(dx, dz)
// since the baked mesh faces +Z after the z-mirror).
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class VehicleTraffic
    {
        const int SIGNAL_CYCLE = 30;

        static readonly Dictionary<string, Color[]> Palette = new()
        {
            // NOTE: must go through C() — `new Color(0x0a, 0x0a, 0x0c)` picks the FLOAT ctor (10,10,12),
            // and SetColor gamma-encodes on upload to linear (158,158,237): an HDR-white body that AgX
            // flattens into flat glowing pink. Hex literals are 0..255 — always divide.
            ["hero"] = new[] { C(0x0a0a0c) },
            ["kei"] = new[] { C(0xd8d6d0), C(0x2a3a52), C(0x8a1c22), C(0x101012), C(0xb7b9bc), C(0x5b6b4a), C(0xc9b48a), C(0x1d4a5c) },
            ["taxi"] = new[] { C(0xf0b21a), C(0x12161a), C(0x1c3f2a), C(0xe8e4da) },
            ["van"] = new[] { C(0xb4b4b0), C(0xaaa8a2), C(0x7d838a), C(0x24425e), C(0xbcbcb6) },
            ["av"] = new[] { C(0x0c0c10), C(0x2b2f38), C(0xd9d9dd), C(0x4a0d14), C(0x0e2a3a) },
        };
        static Color C(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

        public class Manifest { public float length, width, height, axleF, axleR, wheelRadius; public float[] min, max; }

        // A drivable (formerly parked) car. JS keeps these in vehicles/index.js `parked` and
        // moves the record into `loose` on exit; here the GameObject stays put and `taken`
        // just gates re-entry while Driving owns its Rigidbody.
        public class ParkedCar
        {
            public string type, name;
            public GameObject go;
            public BoxCollider col;
            public bool taken;
        }
        static List<ParkedCar> _parked;

        public static Manifest GetManifest(string type) =>
            _models != null && _models.TryGetValue(type, out var m) ? m.mf : null;

        class Lane
        {
            public Vector3 p0, dir; public float len, v0; public string kind; public float yaw;
            public List<(float s, string group, float off)> stops = new();
            public List<Car> cars = new();
            public Vector3 Point(float s) => p0 + dir * s;
        }

        static bool _paintHealed;
        // MaterialPropertyBlocks set at edit-time build don't survive scene saves —
        // re-apply parked-vehicle paints on play (JS re-rolls paints per page load anyway)
        static void HealParkedPaint()
        {
            if (_paintHealed) return;
            _paintHealed = true;
            // Cars carry their look in a CarVisual now; re-apply it first so the hash fallback
            // below only fires for objects that predate the component (or were spawned without it).
            // Property blocks are not serialized, so after a load every renderer starts blank.
            foreach (var cv in Object.FindObjectsByType<CarVisual>(FindObjectsInactive.Exclude)) cv.Apply();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                var nm = r.gameObject.name;
                if (!nm.StartsWith("parked_")) continue;
                if (_drivenGo != null && r.gameObject == _drivenGo) continue; // Driving owns its block
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                if (mpb.GetVector("_PaintColor").magnitude > 0.01f) continue;
                int u2 = nm.IndexOf('_', 7);
                var type = u2 > 7 ? nm.Substring(7, u2 - 7) : nm.Substring(7);
                Color paint;
                if (type == "hero") paint = new Color(0x07 / 255f, 0x07 / 255f, 0x0a / 255f);
                else if (Palette.TryGetValue(type, out var pal) && pal != null && pal.Length > 0)
                    paint = pal[Mathf.Abs(nm.GetHashCode()) % pal.Length];
                else continue;
                mpb.SetColor("_PaintColor", paint);
                mpb.SetFloat("_Spin", 0);
                // The spawn-kerb hero idles with head + tail lamps on (JS index.js sets
                // light (1,1,0) on it); every other parked car has light.set(0,0,0).
                // Heal runs for every near-black parked paint, so it must restore this
                // rather than blanket-zeroing _LightLv.
                mpb.SetVector("_LightLv", nm == "parked_hero" ? new Vector3(1, 1, 0) : Vector3.zero);
                r.SetPropertyBlock(mpb);
            }
        }

        class Car
        {
            public string type; public Color paint; public GameObject go; public MaterialPropertyBlock mpb;
            public CarVisual visual;
            public MeshFilter mf; public MeshRenderer mr;
            public bool lodNear;
            public float s, v, v0, len, spin, brake;
            public Lane lane;
            // sky
            public float th0, hOff, bob, laneOff, h, x, dir, skyV, s0; public Lane loop; // loop fields reused
            public Vector3 pos;
        }

        static Dictionary<string, (Mesh l2, Mesh l3, Manifest mf)> _models;
        // Highest-detail showroom LOD, loaded lazily; only kei has a `_l0` dump.
        static Dictionary<string, Mesh> _showcase;
        static readonly HashSet<string> _showcaseTried = new();
        static Dictionary<string, Material> _mats;
        static Lane[] _lanes;
        static List<Car> _cars, _sky;
        static Transform _rootT, _trafficT;
        static bool _healed;

        // ---------------------------------------------------------------- mesh build
        static Vector3[] ReadV3(string path)
        {
            var b = System.IO.File.ReadAllBytes(path); var v = new Vector3[b.Length / 12];
            for (int i = 0; i < v.Length; i++)
                v[i] = new Vector3(System.BitConverter.ToSingle(b, i * 12), System.BitConverter.ToSingle(b, i * 12 + 4), System.BitConverter.ToSingle(b, i * 12 + 8));
            return v;
        }
        static Vector2[] ReadV2(string path)
        {
            var b = System.IO.File.ReadAllBytes(path); var v = new Vector2[b.Length / 8];
            for (int i = 0; i < v.Length; i++)
                v[i] = new Vector2(System.BitConverter.ToSingle(b, i * 8), System.BitConverter.ToSingle(b, i * 8 + 4));
            return v;
        }
        static Vector4[] ReadV4b(string path)
        {
            var b = System.IO.File.ReadAllBytes(path); var v = new Vector4[b.Length / 16];
            for (int i = 0; i < v.Length; i++)
                v[i] = new Vector4(System.BitConverter.ToSingle(b, i * 16), System.BitConverter.ToSingle(b, i * 16 + 4),
                    System.BitConverter.ToSingle(b, i * 16 + 8), System.BitConverter.ToSingle(b, i * 16 + 12));
            return v;
        }

        // JS bins are sim-space (car forward -Z); mirror to unity (z->-z, winding flip)
        static Mesh LoadCar(string type, string suffix)
        {
            string dir = "Assets/Game/Models/Vehicles/dump";
            var pos = ReadV3($"{dir}/veh_{type}{suffix}_pos.bin");
            var nor = ReadV3($"{dir}/veh_{type}{suffix}_nor.bin");
            var uv = ReadV2($"{dir}/veh_{type}{suffix}_uv.bin");
            var tag2 = ReadV2($"{dir}/veh_{type}{suffix}_tag.bin");
            var tag = new Vector4[tag2.Length];
            for (int i = 0; i < tag2.Length; i++) tag[i] = new Vector4(tag2[i].x, tag2[i].y, 0, 0);
            var ab = System.IO.File.ReadAllBytes($"{dir}/veh_{type}{suffix}_axle.bin");
            var axle = new Vector4[ab.Length / 12];
            for (int i = 0; i < axle.Length; i++)
                axle[i] = new Vector4(System.BitConverter.ToSingle(ab, i * 12), System.BitConverter.ToSingle(ab, i * 12 + 4), 0, 0);
            var mp = new Vector3[pos.Length]; var mn = new Vector3[pos.Length];
            var mu = new Vector2[pos.Length]; var m1 = new Vector4[pos.Length]; var m2 = new Vector4[pos.Length];
            for (int i = 0; i < pos.Length; i += 3)
            {
                mp[i] = new Vector3(pos[i].x, pos[i].y, -pos[i].z);
                mp[i + 2] = new Vector3(pos[i + 1].x, pos[i + 1].y, -pos[i + 1].z);
                mp[i + 1] = new Vector3(pos[i + 2].x, pos[i + 2].y, -pos[i + 2].z);
                for (int k = 0; k < 3; k++)
                {
                    int src = i + (k == 0 ? 0 : k == 1 ? 2 : 1);
                    mn[i + k] = new Vector3(nor[src].x, nor[src].y, -nor[src].z);
                    mu[i + k] = uv[src];
                    m1[i + k] = tag[src];
                    m2[i + k] = new Vector4(axle[src].x, -axle[src].y, 0, 0); // axle z mirrored
                }
            }
            var m = new Mesh { name = $"car_{type}{suffix}" };
            // The dumper emits a non-indexed tri soup. LOD0 (264k verts) and LOD1 (73k
            // verts) are past Unity's 65,535-vertex default, so opt into 32-bit indices.
            if (mp.Length > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = mp; m.normals = mn; m.uv = mu;
            m.SetUVs(1, m1); m.SetUVs(2, m2);
            var idx = new int[mp.Length];
            for (int i = 0; i < mp.Length; i++) idx[i] = i;
            m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }

        /// Kei parked/showroom cars use the translucent-glass material. The near traffic LOD
        /// (LOD1) also has the interior, so it can use the same glass; the far traffic LOD
        /// (LOD2) has no interior and stays on the opaque material.
        static Material CarMaterial(string type, string suffix)
        {
            if (suffix == "_l3") return _mats["car3"];
            if (type == "kei" && _mats.TryGetValue("carGlass", out var glass) && glass != null) return glass;
            return _mats["car"];
        }

        // The browser fleet fades LOD0/LOD1/LOD2 at [16, 45, 110] m. The Unity dump has two
        // street levels for every type, so traffic switches at 45 m — the browser's LOD1
        // boundary — with a small hysteresis band so a car parked at the threshold does not
        // flip mesh/material every frame. Near kei traffic gets LOD1 plus its interior and
        // the translucent-glass material; far traffic stays on LOD2 + the opaque car3 material.
        const float TRAFFIC_LOD_NEAR = 45f;
        const float TRAFFIC_LOD_HYST = 3f;

        static void SetTrafficLod(Car c, bool near)
        {
            if (c == null || c.go == null || c.type == null || _models == null || !_models.ContainsKey(c.type)) return;
            if (c.mf == null) c.mf = c.go.GetComponent<MeshFilter>();
            if (c.mr == null) c.mr = c.go.GetComponent<MeshRenderer>();
            if (c.mf == null || c.mr == null) return;
            c.mf.sharedMesh = near ? _models[c.type].l2 : _models[c.type].l3;
            c.mr.sharedMaterial = near ? CarMaterial(c.type, "") : _mats["car3"];
            c.lodNear = near;
        }

        static void UpdateTrafficLods(Vector3 camPos)
        {
            if (_cars == null) return;
            float enter = TRAFFIC_LOD_NEAR - TRAFFIC_LOD_HYST; enter *= enter;
            float leave = TRAFFIC_LOD_NEAR + TRAFFIC_LOD_HYST; leave *= leave;
            foreach (var c in _cars)
            {
                if (c.go == null) continue;
                float d2 = (c.go.transform.position - camPos).sqrMagnitude;
                bool wantNear = c.lodNear ? d2 < leave : d2 < enter;
                if (wantNear != c.lodNear) SetTrafficLod(c, wantNear);
            }
        }

        static GameObject SpawnCar(Transform parent, string type, string suffix, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = suffix == "_l3" ? _models[type].l3 : _models[type].l2;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = CarMaterial(type, suffix);
            // Serialized home for the per-renderer look (see CarVisual): property blocks do not
            // survive a scene load, so without this every car came back black with dead lamps.
            var cv = go.AddComponent<CarVisual>();
            cv.type = type;
            cv.length = _models[type].mf.length;
            return go;
        }

        // ---------------------------------------------------------------- per-car look
        // The property block is the render side; the CarVisual copy is what the scene saves.
        // Everything that gives a car its individuality (paint, wheel spin, lamp level) has to
        // go through here or it will be missing again the next time Main.unity is opened.

        static void SetCarLook(GameObject go, Color paint, float spin, Vector3 lv)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_PaintColor", paint);
                mpb.SetFloat("_Spin", spin);
                mpb.SetVector("_LightLv", lv);
                r.SetPropertyBlock(mpb);
            }
            var cv = go.GetComponent<CarVisual>();
            if (cv != null) cv.Set(paint, spin, lv);
        }

        // Reuses the Car's own block: this runs for every traffic car every frame.
        static void PushCarLook(Car c, Vector3 lv)
        {
            if (c.go == null) return;
            if (c.mpb == null) c.mpb = new MaterialPropertyBlock();
            c.mpb.SetColor("_PaintColor", c.paint);
            c.mpb.SetFloat("_Spin", c.spin);
            c.mpb.SetVector("_LightLv", lv);
            var r = c.go.GetComponent<MeshRenderer>();
            if (r != null) r.SetPropertyBlock(c.mpb);
            if (c.visual == null) c.visual = c.go.GetComponent<CarVisual>();
            if (c.visual != null) c.visual.Set(c.paint, c.spin, lv);
        }

        // ---------------------------------------------------------------- build
        static bool _modelsLoaded;
        public static bool ModelsReady => _models != null && _modelsLoaded;

        /// Load the five dumped car meshes + manifests without building any traffic.
        /// Returns null on success, an error string otherwise. The gallery uses this
        /// independently of Build so the showroom can exist without parked/traffic cars.
        /// Retries a partially loaded set on the next call (a missing dump is not cached as success).
        public static string LoadModels()
        {
            if (_modelsLoaded) return null;
            if (_models == null)
            {
                _models = new Dictionary<string, (Mesh, Mesh, Manifest)>();
                _mats = new Dictionary<string, Material>();
                var sh = Shader.Find("NEON KAIDO/VehicleCar");
                if (sh == null) return "vehicles: shader missing";
                _mats["car"] = new Material(sh);
                _mats["car3"] = new Material(sh);
                // Kei parked/showroom cars use the translucent-glass variant; the material is
                // shared, and per-car paint/lights still go through the property block.
                var shGlass = Shader.Find("NEON KAIDO/VehicleCarGlass");
                if (shGlass != null)
                {
                    var glass = new Material(shGlass) { name = "VehicleCarGlass" };
                    glass.SetFloat("_GlassAlpha", 0.58f);
                    _mats["carGlass"] = glass;
                }
            }
            var dir = "Assets/Game/Models/Vehicles/dump";
            foreach (var type in new[] { "hero", "kei", "taxi", "van", "av" })
            {
                if (_models.ContainsKey(type)) continue;
                if (!System.IO.File.Exists($"{dir}/veh_{type}.json")) return "vehicles: missing dump for " + type;
                var mf = JsonUtility.FromJson<Manifest>(System.IO.File.ReadAllText($"{dir}/veh_{type}.json"));
                _models[type] = (LoadCar(type, ""), LoadCar(type, "_l3"), mf);
            }
            _modelsLoaded = true;
            return null;
        }

        /// Per-type paint list (index.js `pal` tables) for the gallery's colour cycling.
        public static Color[] PaletteFor(string type) =>
            Palette.TryGetValue(type, out var p) && p != null && p.Length > 0 ? p : new[] { Color.white };

        /// Highest-detail `_l0` mesh for showroom/delivery, loaded on first use.
        /// Only kei ships one; every other type falls back to its parked mesh.
        static Mesh ShowcaseMesh(string type)
        {
            if (_showcase != null && _showcase.TryGetValue(type, out var cached)) return cached;
            if (_showcaseTried.Contains(type)) return null;
            _showcaseTried.Add(type);
            string dir = "Assets/Game/Models/Vehicles/dump";
            if (!System.IO.File.Exists($"{dir}/veh_{type}_l0_pos.bin")) return null;
            var mesh = LoadCar(type, "_l0");
            if (_showcase == null) _showcase = new Dictionary<string, Mesh>();
            _showcase[type] = mesh;
            return mesh;
        }

        /// One display-only car (no collider, not in the traffic/parked lists).
        public static GameObject SpawnShowcase(Transform parent, string type, Vector3 pos, float yawDeg, Color paint, Vector3 lights)
        {
            if (!ModelsReady) LoadModels();
            if (!ModelsReady || !_models.ContainsKey(type)) return null;
            var go = SpawnCar(parent, type, "", "gallery_" + type);
            var hi = ShowcaseMesh(type);
            if (hi != null) go.GetComponent<MeshFilter>().sharedMesh = hi;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            go.transform.localScale = Vector3.one;
            SetCarLook(go, paint, 0f, lights);
            return go;
        }

        /// Clear the fake per-car point-light globals (traffic lights) while the gallery hides traffic.
        public static void ClearCarLightGlobals()
        {
            var zero = new Vector4[42];
            Shader.SetGlobalFloat("_CarLCount", 0f);
            Shader.SetGlobalVectorArray("_CarLPos", zero);
            Shader.SetGlobalVectorArray("_CarLColor", zero);
        }

        static int _deliverySeq;
        /// One player-grade car that is not traffic and not parked yet (NX-9 delivery, en route).
        public static GameObject SpawnDeliveryCar(Transform parent, string type, Vector3 pos, float yawDeg, Color paint, Vector3 lights)
        {
            var go = SpawnShowcase(parent, type, pos, yawDeg, paint, lights);
            if (go != null) go.name = "nx9_delivery_" + (++_deliverySeq);
            return go;
        }

        /// Give a spawned delivery car the static chassis collider and make it enterable via
        /// NearestParked / Driving.Enter. Called when the car has finished pulling up.
        public static ParkedCar RegisterDelivered(string type, GameObject go)
        {
            if (go == null || _models == null || !_models.ContainsKey(type)) return null;
            if (_parked == null) _parked = new List<ParkedCar>();
            foreach (var p in _parked) if (p.go == go) return p;   // idempotent arrival
            var rec = new ParkedCar { type = type, name = go.name, go = go };
            AddCarCollider(rec);
            _parked.Add(rec);
            return rec;
        }

        public static void UnregisterDelivered(ParkedCar rec)
        {
            if (rec == null) return;
            _parked?.Remove(rec);
            if (rec.col != null) Object.Destroy(rec.col);
            rec.col = null;
        }

        // A delivered car is neither in `_cars` nor the driven car, but its headlamps should
        // still light the wet road and throw cones. Delivery keeps this slot refreshed while
        // the car is approaching or waiting at the kerb. Cleared on boarding/despawn.
        static GameObject _extraLitGo;
        static string _extraLitType;
        static float _extraLitBrake;
        public static void SetExtraLitCar(GameObject go, string type, float brake)
        {
            _extraLitGo = go;
            _extraLitType = type;
            _extraLitBrake = brake;
        }

        public static void ClearExtraLitCar()
        {
            _extraLitGo = null;
            _extraLitType = null;
            _extraLitBrake = 0f;
        }

        public static string Build(Transform root)
        {
            var err = LoadModels();
            if (err != null) return err;

            if (root.Find("Vehicles") != null) Object.DestroyImmediate(root.Find("Vehicles").gameObject);
            var vroot = new GameObject("Vehicles");
            vroot.transform.SetParent(root, false);
            _rootT = vroot.transform;
            var parkedT = new GameObject("parked").transform; parkedT.SetParent(_rootT, false);
            _trafficT = new GameObject("traffic").transform; _trafficT.SetParent(_rootT, false);
            var skyT = new GameObject("sky").transform; skyT.SetParent(_rootT, false);

            int parkedCount = BuildParked(parkedT);
            BuildLanesAndTraffic();
            int trafficCount = SpawnTraffic(_trafficT);
            int skyCount = BuildSky(skyT);
            _healed = true;

            return $"vehicles: {parkedCount} parked, {trafficCount} traffic, {skyCount} sky AVs";
        }

        // Static body box so parked cars are solid (JS vehicles/index.js addParkedCollider);
        // on enter the same collider becomes the driven car's chassis collider.
        static void AddCarCollider(ParkedCar rec)
        {
            var mf = _models[rec.type].mf;
            var bc = rec.go.AddComponent<BoxCollider>();
            bc.size = new Vector3(mf.width * 0.92f, mf.height * 0.80f, mf.length * 0.96f);
            bc.center = new Vector3(0f, mf.height * 0.43f, 0f);
            rec.col = bc;
        }

        static int BuildParked(Transform parent)
        {
            var rng = new Mulberry32(90210u);
            int count = 0;
            _parked = new List<ParkedCar>();
            string[] types = { "hero", "kei", "taxi", "van", "av" };
            string ParkType() { var r = (float)rng.Next(); return r < 0.4 ? "kei" : r < 0.6 ? "taxi" : r < 0.82 ? "van" : "hero"; }
            float avZStart = (float)Layout.AvenueZStart, avZEnd = (float)Layout.AvenueZEnd;
            float roadHW = (float)Layout.AvenueRoadHalfWidth;
            float curbX = roadHW - 1.15f;
            var crossZs = Layout.CrossZs;
            float alleyZ = (float)Layout.AlleyZ, alleyW = (float)Layout.AlleyWidth;
            void AddParked(string type, float x, float z, float yawSim)
            {
                var pal = Palette[type];
                var paint = pal[(int)(rng.Next() * pal.Length)];
                var go = SpawnCar(parent, type, "", $"parked_{type}_{count}");
                // sim -> unity: pos (x, 0, -z); mesh faces +Z after mirror; JS yaw (car fwd -Z):
                // unity yaw = PI - (-yawSim)?  derive: JS forward f=(sin yawSim* -1 ...) -> use rotY = -yawSim + PI
                go.transform.position = new Vector3(x, 0, -z);
                go.transform.rotation = Quaternion.Euler(0, -yawSim * Mathf.Rad2Deg, 0);
                go.transform.localScale = Vector3.one;
                SetCarLook(go, paint, 0f, Vector3.zero);
                var rec = new ParkedCar { type = type, name = go.name, go = go };
                AddCarCollider(rec);
                _parked.Add(rec);
                count++;
            }
            for (int si = 0; si < 2; si++)
            {
                float side = si == 0 ? -1f : 1f;
                for (float z = avZStart - 8f; z > avZEnd + 10f; z -= 6.4f + (float)rng.Next() * 1.6f)
                {
                    bool skip = false;
                    foreach (var zc in crossZs) if (Mathf.Abs(z - (float)zc) < (float)Layout.CrossRoadHalfWidth + 8f) skip = true;
                    if (skip) continue;
                    if (side < 0 && Mathf.Abs(z - alleyZ) < alleyW + 4f) continue;
                    if (side > 0 && z > 6f && z < 30f) continue;
                    if (side < 0 && z > -4f && z < 34f) continue;
                    if (rng.Next() > 0.62) continue;
                    AddParked(ParkType(), side * (curbX + ((float)rng.Next() - 0.5f) * 0.15f), z,
                        side < 0 ? 0f : Mathf.PI + ((float)rng.Next() - 0.5f) * 0.04f);
                }
            }
            for (int ci = 0; ci < crossZs.Length; ci++)
            {
                float zc = (float)crossZs[ci];
                for (int si = 0; si < 2; si++)
                {
                    float side = si == 0 ? -1f : 1f;
                    for (float x = (float)Layout.CrossXStart + 15f; x < (float)Layout.CrossXEnd - 10f; x += 6.6f + (float)rng.Next() * 2f)
                    {
                        if (Mathf.Abs(x) < roadHW + (float)Layout.AvenueSidewalkWidth + 8f) continue;
                        if (rng.Next() > 0.4) continue;
                        AddParked(rng.Next() < 0.6 ? "kei" : "taxi", x, zc + side * ((float)Layout.CrossRoadHalfWidth - 0.92f),
                            side < 0 ? -Mathf.PI / 2f : Mathf.PI / 2f);
                    }
                }
            }
            // hero car parked at spawn kerb
            {
                var go = SpawnCar(parent, "hero", "", "parked_hero");
                go.transform.position = new Vector3(curbX, 0, -15f);
                go.transform.rotation = Quaternion.Euler(0, 0, 0);
                SetCarLook(go, new Color(0x07 / 255f, 0x07 / 255f, 0x0a / 255f), 0f, new Vector3(1, 1, 0));
                var rec = new ParkedCar { type = "hero", name = "parked_hero", go = go };
                AddCarCollider(rec);
                _parked.Add(rec);
                count++;
            }
            return count;
        }

        /// Smallest centre-to-centre z distance from a parked car in the same kerb row.
        /// Delivery uses it to nudge its drop point into a gap instead of parking 20 cm
        /// inside a parked car's flank. 999 = no parked car nearby.
        public static float ParkedZClearance(float x, float z)
        {
            if (_parked == null) return 999f;
            float best = 999f;
            foreach (var p in _parked)
            {
                if (p == null || p.go == null) continue;
                var q = p.go.transform.position;
                if (Mathf.Abs(q.x - x) > 2.6f) continue;
                float d = Mathf.Abs(q.z - z);
                if (d < best) best = d;
            }
            return best;
        }

        // Nearest enterable parked car (JS nearestCar over parked + loose). Distances are
        // horizontal so the eye-height player position doesn't bias the test.
        public static ParkedCar NearestParked(Vector3 pos, float maxDist = 4.5f)
        {
            if (_parked == null) return null;
            ParkedCar best = null;
            float bd = maxDist * maxDist;
            foreach (var p in _parked)
            {
                if (p == null || p.taken || p.go == null) continue;
                var d = p.go.transform.position - pos;
                d.y = 0f;
                float d2 = d.sqrMagnitude;
                if (d2 < bd) { bd = d2; best = p; }
            }
            return best;
        }

        // ---------------------------------------------------------------- traffic
        class LaneDef { public string kind; public Vector3 p0, dir; public float len, v0; public List<(float pos, string group, float off)> stops; public char axis; }

        static void BuildLanesAndTraffic()
        {
            var rng = new Mulberry32(4242u);

            _cars = new List<Car>();
            float avZStart = (float)Layout.AvenueZStart, avZEnd = (float)Layout.AvenueZEnd;
            float roadHW = (float)Layout.AvenueRoadHalfWidth, crossHW = (float)Layout.CrossRoadHalfWidth;
            var crossZs = Layout.CrossZs;
            float Offs(float z) => (-z / 60f) * 3f;

            var defs = new List<LaneDef>();
            System.Func<float, List<(float, string, float)>> aStops = (sign) =>
            {
                var l = new List<(float, string, float)>();
                foreach (var z in crossZs) l.Add(((float)z + sign * (crossHW + 2.2f), "avenue", Offs((float)z)));
                return l;
            };
            defs.Add(new LaneDef { kind = "avenue", p0 = new Vector3(-1.5f, 0, avZStart), dir = new Vector3(0, 0, -1), len = avZStart - avZEnd, v0 = 12, stops = aStops(1), axis = 'z' });
            defs.Add(new LaneDef { kind = "avenue", p0 = new Vector3(1.5f, 0, avZEnd), dir = new Vector3(0, 0, 1), len = avZStart - avZEnd, v0 = 12, stops = aStops(-1), axis = 'z' });
            foreach (var zv in crossZs)
            {
                float z = (float)zv;
                float hw = roadHW + 2.2f;
                defs.Add(new LaneDef { kind = "cross", p0 = new Vector3((float)Layout.CrossXStart, 0, z - 2.2f), dir = new Vector3(1, 0, 0), len = (float)Layout.CrossXEnd - (float)Layout.CrossXStart, v0 = 10, stops = new List<(float, string, float)> { ((float)-hw, "cross", Offs(z)) }, axis = 'x' });
                defs.Add(new LaneDef { kind = "cross", p0 = new Vector3((float)Layout.CrossXEnd, 0, z + 2.2f), dir = new Vector3(-1, 0, 0), len = (float)Layout.CrossXEnd - (float)Layout.CrossXStart, v0 = 10, stops = new List<(float, string, float)> { (hw, "cross", Offs(z)) }, axis = 'x' });
            }
            float exZ = (float)Layout.ExpresswayZ, deckY = (float)Layout.ExpresswayHeight;
            var exLanes = new (float z, float dir)[] { (exZ - 5.25f, 1), (exZ - 1.75f, 1), (exZ + 1.75f, -1), (exZ + 5.25f, -1) };
            for (int i = 0; i < exLanes.Length; i++)
            {
                var ln = exLanes[i];
                float v0 = 22f * (i % 2 == 1 ? 1.12f : 0.92f);
                defs.Add(new LaneDef { kind = "express", p0 = new Vector3(ln.dir > 0 ? (float)Layout.ElevatedXStart : (float)Layout.ElevatedXEnd, deckY, ln.z), dir = new Vector3(ln.dir, 0, 0), len = (float)Layout.ElevatedXEnd - (float)Layout.ElevatedXStart, v0 = v0, stops = new List<(float, string, float)>(), axis = 'x' });
            }

            string TypeFor(string kind)
            {
                float r = (float)rng.Next();
                if (kind == "express") return r < 0.35f ? "van" : r < 0.65f ? "kei" : r < 0.85f ? "taxi" : "hero";
                return r < 0.38f ? "kei" : r < 0.66f ? "taxi" : r < 0.86f ? "van" : "hero";
            }

            _lanes = new Lane[defs.Count];
            int li = 0;
            foreach (var def in defs)
            {
                var lane = new Lane
                {
                    kind = def.kind, p0 = def.p0, dir = def.dir.normalized, len = def.len, v0 = def.v0,
                    yaw = Mathf.Atan2(-def.dir.x, -def.dir.z),
                };
                foreach (var st in def.stops)
                {
                    float p = def.axis == 'z' ? st.pos - lane.p0.z : st.pos - lane.p0.x;
                    float ss = def.axis == 'z' ? p / lane.dir.z : p / lane.dir.x;
                    if (ss > 0 && ss < lane.len) lane.stops.Add((ss, st.group, st.off));
                }
                float spacing = def.kind == "express" ? 34f : def.kind == "avenue" ? 26f : 32f;
                float s = (float)rng.Next() * spacing;
                while (s < lane.len - 8f)
                {
                    string type = TypeFor(def.kind);
                    var pal = Palette[type];
                    var car = new Car
                    {
                        type = type, paint = pal[(int)(rng.Next() * pal.Length)], len = _models[type].mf.length,
                        lane = lane, s = s,
                        v = lane.v0 * (0.75f + (float)rng.Next() * 0.3f),
                        v0 = lane.v0 * (0.85f + (float)rng.Next() * 0.3f),
                    };
                    lane.cars.Add(car); _cars.Add(car);
                    s += spacing * (0.6f + (float)rng.Next() * 1.1f);
                }
                _lanes[li++] = lane;
            }
        }

        static int SpawnTraffic(Transform parent)
        {
            int count = 0;
            foreach (var lane in _lanes)
                foreach (var c in lane.cars)
                {
                    var go = SpawnCar(parent, c.type, "_l3", $"car_{count}");
                    c.go = go;
                    c.mf = go.GetComponent<MeshFilter>();
                    c.mr = go.GetComponent<MeshRenderer>();
                    c.lodNear = false;
                    c.mpb = new MaterialPropertyBlock();
                    PlaceCar(c);
                    count++;
                }
            return count;
        }

        static void PlaceCar(Car c)
        {
            var p = c.lane.Point(c.s);
            c.pos = p;
            // sim -> unity: (x, y, -z); mesh faces +Z after mirror: unity yaw = Atan2(du, dzU) where (du,dzU)=(dir.x, -dir.z)
            c.go.transform.position = new Vector3(p.x, p.y, -p.z);
            float yawU = Mathf.Atan2(c.lane.dir.x, -c.lane.dir.z);
            c.go.transform.rotation = Quaternion.Euler(0, yawU * Mathf.Rad2Deg, 0);
            PushCarLook(c, new Vector3(1f, 1f, c.brake));
        }

        // ---------------------------------------------------------------- sky AVs
        static int BuildSky(Transform parent)
        {
            _sky = new List<Car>();
            var rng = new Mulberry32(777u);
            var loops = new (float h, float rx, float rz, int n, float w)[]
            {
                (48, 150, 210, 10, 0.06f), (64, 210, 260, 12, -0.05f), (85, 120, 300, 9, 0.045f),
                (112, 280, 340, 12, -0.035f), (140, 360, 420, 10, 0.03f),
            };
            int count = 0;
            foreach (var L in loops)
                for (int i = 0; i < L.n; i++)
                {
                    var pal = Palette["av"];
                    _sky.Add(new Car
                    {
                        type = "av", paint = pal[(int)(rng.Next() * pal.Length)], len = _models["av"].mf.length,
                        loop = new Lane { p0 = new Vector3(20f, 0, -160f) }, // center stored in p0
                        th0 = (i / (float)L.n) * Mathf.PI * 2f + (float)rng.Next() * 0.3f,
                        hOff = ((float)rng.Next() - 0.5f) * 8f, bob = (float)rng.Next() * 6f, laneOff = ((float)rng.Next() - 0.5f) * 10f,
                        h = L.h, x = L.rx, dir = L.rz, skyV = L.w,
                        s0 = 0,
                    });
                }
            for (int k = 0; k < 4; k++)
            {
                float x = new[] { -70f, -40f, 60f, 95f }[k], h = new[] { 58f, 74f, 96f, 66f }[k];
                float dir = k % 2 == 1 ? 1f : -1f;
                for (int i = 0; i < 8; i++)
                    _sky.Add(new Car
                    {
                        type = "av", paint = Palette["av"][(int)(rng.Next() * 5)], len = _models["av"].mf.length,
                        x = x, h = h, dir = dir, skyV = 30f + (float)rng.Next() * 15f, s0 = i * 120f + (float)rng.Next() * 60f,
                        bob = (float)rng.Next() * 6f,
                    });
            }
            foreach (var c in _sky)
            {
                var go = SpawnCar(parent, "av", "_l3", $"av_{count++}");
                c.go = go;
                c.mpb = new MaterialPropertyBlock();
            }
            return count;
        }

        static void PlaceSkyCar(Car c, float t)
        {
            Vector3 pos;
            float yawU, pitch = 0, roll = 0;
            if (c.loop != null)
            {
                var c0 = c.loop.p0; // center
                float th = c.th0 + c.skyV * t;
                float rx = c.x + c.laneOff, rz = c.dir + c.laneOff;
                pos = new Vector3(c0.x + Mathf.Cos(th) * rx, c.h + c.hOff + Mathf.Sin(t * 0.6f + c.bob) * 0.8f, c0.z + Mathf.Sin(th) * rz);
                float tx = -Mathf.Sin(th) * rx * Mathf.Sign(c.skyV), tz = Mathf.Cos(th) * rz * Mathf.Sign(c.skyV);
                // JS yaw = atan2(-tx, -tz) for -Z forward; unity +Z forward mirror:
                yawU = Mathf.Atan2(tx, tz);
                pitch = 0.03f; roll = Mathf.Sign(c.skyV) * 0.12f;
            }
            else
            {
                float span = 960f;
                float s = ((c.s0 + c.skyV * t) % span + span) % span - span / 2f;
                pos = new Vector3(c.x, c.h + Mathf.Sin(t * 0.5f + c.bob) * 0.7f, -(-160f + s * c.dir));
                yawU = c.dir > 0 ? 0f : Mathf.PI;
                pitch = 0.02f;
            }
            c.go.transform.position = pos;
            c.go.transform.rotation = Quaternion.Euler(pitch * Mathf.Rad2Deg, yawU * Mathf.Rad2Deg, roll * Mathf.Rad2Deg);
            PushCarLook(c, new Vector3(1f, 1f, 0f));
        }

        // ---------------------------------------------------------------- per-frame
        static (string, string) SignalState(float t, float off)
        {
            float p = ((t + off) % SIGNAL_CYCLE + SIGNAL_CYCLE) % SIGNAL_CYCLE;
            string avenue = p < 14 ? "g" : p < 17 ? "y" : "r";
            string cross = p >= 18 && p < 27 ? "g" : p >= 27 && p < 30 ? "y" : "r";
            return (avenue, cross);
        }

        static bool _linked;

        static bool TryLink()
        {
            var trT = GameObject.Find("Vehicles/traffic");
            var skT = GameObject.Find("Vehicles/sky");
            if (trT == null || skT == null) return false;
            int i = 0;
            foreach (var lane in _lanes)
                foreach (var c in lane.cars)
                {
                    c.go = trT.transform.GetChild(i++).gameObject;
                    c.mpb = new MaterialPropertyBlock();
                    c.mf = null; c.mr = null;
                    // The saved scene always holds the far traffic mesh; normalise after a
                    // domain reload before the first distance test runs.
                    SetTrafficLod(c, false);
                }
            int j = 0;
            foreach (var c in _sky)
            {
                c.go = skT.transform.GetChild(j++).gameObject;
                c.mpb = new MaterialPropertyBlock();
            }
            // parked GOs are destroyed by a play-mode scene reload while the static records
            // survive; child order is creation order, so re-link them positionally.
            var pkT = GameObject.Find("Vehicles/parked");
            if (pkT != null && _parked != null)
            {
                int k = 0;
                foreach (var p in _parked)
                {
                    if (k >= pkT.transform.childCount) break;
                    p.go = pkT.transform.GetChild(k++).gameObject;
                    p.col = p.go.GetComponent<BoxCollider>();
                }
            }
            _linked = true;
            return true;
        }

        // audio hook: up to n world positions + speeds of moving traffic cars near the player
        public static List<(Vector3 pos, float speed)> GetTrafficSample(int n, float maxD, Vector3 playerPos)
        {
            var o = new List<(Vector3, float)>();
            if (_cars == null) return o;
            float md2 = maxD * maxD;
            foreach (var c in _cars)
            {
                if (o.Count >= n) break;
                if (c.go == null) continue;
                var p = c.go.transform.position;
                if ((p - playerPos).sqrMagnitude > md2) continue;
                o.Add((p, c.v));
            }
            return o;
        }

        // ---------------------------------------------------------------- traffic collision proxies
        // Port of the 18 kinematic Rapier colliders in index.js: the nearest moving cars get
        // invisible kinematic boxes so the walking player and the driven car hit them. Keeping
        // it pooled (rather than 166 bodies) matches the JS budget.
        const int ProxyN = 18;
        static GameObject[] _proxyGo;
        static Rigidbody[] _proxyRb;
        static BoxCollider[] _proxyCol;
        static readonly List<Car> _proxyNear = new List<Car>();

        static void EnsureProxies()
        {
            if (_proxyGo != null && _proxyGo.Length == ProxyN && _proxyGo[0] != null) return;
            if (_proxyGo != null)
                foreach (var g in _proxyGo) if (g != null) Object.Destroy(g);
            _proxyGo = new GameObject[ProxyN];
            _proxyRb = new Rigidbody[ProxyN];
            _proxyCol = new BoxCollider[ProxyN];
            var parent = new GameObject("traffic_proxies").transform;
            if (_rootT != null) parent.SetParent(_rootT, false);
            for (int i = 0; i < ProxyN; i++)
            {
                var go = new GameObject("proxy_" + i);
                go.transform.SetParent(parent, false);
                var bc = go.AddComponent<BoxCollider>();
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                go.SetActive(false);
                _proxyGo[i] = go; _proxyRb[i] = rb; _proxyCol[i] = bc;
            }
        }

        static void UpdateProxies(Vector3 playerPos)
        {
            if (_cars == null) return;
            EnsureProxies();
            _proxyNear.Clear();
            float md2 = 60f * 60f;
            foreach (var c in _cars)
            {
                if (c.go == null) continue;
                if ((c.go.transform.position - playerPos).sqrMagnitude > md2) continue;
                _proxyNear.Add(c);
            }
            _proxyNear.Sort((a, b) =>
                (a.go.transform.position - playerPos).sqrMagnitude.CompareTo((b.go.transform.position - playerPos).sqrMagnitude));
            // Assign proxies from the nearest cars, but skip any car the player is already
            // inside (spawn/teleport overlap). Otherwise the kinematic box encloses the
            // CharacterController and it cannot move in any direction — the "spawned on a
            // car and stuck" regression.
            int slot = 0;
            for (int i = 0; i < _proxyNear.Count && slot < ProxyN; i++)
            {
                var c = _proxyNear[i];
                var mf = _models[c.type].mf;
                var tr = c.go.transform;
                var local = tr.InverseTransformPoint(playerPos);
                if (Mathf.Abs(local.x) < mf.width * 0.46f + 0.15f &&
                    Mathf.Abs(local.z) < mf.length * 0.48f + 0.15f &&
                    Mathf.Abs(local.y) < mf.height + 0.6f)
                    continue;
                var go = _proxyGo[slot];
                var bc = _proxyCol[slot];
                bc.size = new Vector3(mf.width * 0.92f, mf.height * 0.80f, mf.length * 0.96f);
                bc.center = new Vector3(0f, mf.height * 0.43f, 0f);
                if (!go.activeSelf) go.SetActive(true);
                _proxyRb[slot].position = tr.position;
                _proxyRb[slot].rotation = tr.rotation;
                slot++;
            }
            for (int i = slot; i < ProxyN; i++)
                if (_proxyGo[i].activeSelf) _proxyGo[i].SetActive(false);
        }

        // ---------------------------------------------------------------- driven car hooks
        // The driven car is not in `_cars`, so lights/beams/pools need an explicit extra slot.
        static GameObject _drivenGo;
        static string _drivenType;
        public static GameObject DrivenCar => _drivenGo;

        public static void SetDrivenCar(GameObject go, string type) { _drivenGo = go; _drivenType = type; }
        public static void ClearDrivenCar() { _drivenGo = null; _drivenType = null; }

        public static void Update(float dt, float t, Vector3 playerPos)
        {
            // statics wiped by a domain reload (script recompile): rebuild deterministically
            if (_cars == null || _sky == null || _lanes == null)
            {
                var root0 = GameObject.Find("VehiclesRoot");
                if (root0 == null) root0 = new GameObject("VehiclesRoot");
                Build(root0.transform);
                _linked = false;
            }
            if (!_linked || _cars.Count == 0 || _cars[0].go == null) { if (!TryLink()) return; }
            HealParkedPaint();
            var camPos = Camera.main != null ? Camera.main.transform.position : playerPos;
            foreach (var lane in _lanes)
            {
                var cars = lane.cars;
                cars.Sort((a, b) => b.s.CompareTo(a.s));
                var obsS = new List<float>();
                foreach (var o in new[] { playerPos })
                {
                    var tmp = o - lane.p0;
                    float s = Vector3.Dot(tmp, lane.dir);
                    var lat = tmp - lane.dir * s;
                    if (Mathf.Abs(lat.x) + Mathf.Abs(lat.z) < 2f && Mathf.Abs(lat.y) < 3f && s > 0 && s < lane.len) obsS.Add(s);
                }
                for (int i = 0; i < cars.Count; i++)
                {
                    var c = cars[i];
                    float gap = 1e9f, vLead = c.v0;
                    if (i > 0) { var L = cars[i - 1]; gap = L.s - c.s - (L.len + c.len) * 0.5f; vLead = L.v; }
                    else if (cars.Count > 1) { var L = cars[cars.Count - 1]; gap = L.s + lane.len - c.s - (L.len + c.len) * 0.5f; vLead = L.v; }
                    foreach (var st in lane.stops)
                    {
                        float ds = st.s - c.s - c.len * 0.5f;
                        if (ds < -0.5f || ds > 60f) continue;
                        var sig = SignalState(t, st.off);
                        float stopDist = (c.v * c.v) / (2f * 4f);
                        bool red = (st.group == "avenue" && sig.Item1 == "r") || (st.group == "cross" && sig.Item2 == "r");
                        bool amber = (st.group == "avenue" && sig.Item1 == "y") || (st.group == "cross" && sig.Item2 == "y");
                        if (red || (amber && ds > stopDist + 1f)) { if (ds < gap) { gap = Mathf.Max(ds, 0.01f); vLead = 0; } }
                    }
                    foreach (var os in obsS)
                    {
                        float ds = os - c.s - c.len * 0.5f - 1.2f;
                        if (ds > -1f && ds < gap) { gap = Mathf.Max(ds, 0.01f); vLead = 0; }
                    }
                    const float a = 2.2f, b = 3.5f, s0 = 2.2f, T = 1.2f;
                    float dv = c.v - vLead;
                    float sStar = s0 + Mathf.Max(0, c.v * T + (c.v * dv) / (2f * Mathf.Sqrt(a * b)));
                    float acc = a * (1 - Mathf.Pow(c.v / c.v0, 4) - Mathf.Pow(sStar / Mathf.Max(gap, 0.01f), 2));
                    acc = Mathf.Max(acc, -9f);
                    c.v = Mathf.Max(0, c.v + acc * dt);
                    float prevS = c.s;
                    c.s += c.v * dt;
                    // hard guard: never drive through the player (adjacent-lane passes read as flickering ghosts)
                    var wp = c.lane.Point(c.s);
                    var wu = new Vector3((float)wp.x, playerPos.y, (float)-wp.z);
                    var dh = wu - playerPos; dh.y = 0;
                    if (dh.sqrMagnitude < 4.8f) { c.s = prevS; c.v = 0f; c.brake = 1f; }
                    c.brake = acc < -0.8f || c.v < 0.3f ? 1f : 0f;
                    if (c.s > lane.len) c.s -= lane.len;
                    float wr = _models[c.type].mf.wheelRadius;
                    // The Unity mesh is the z-mirrored bake (forward +Z), so forward travel is
                    // a POSITIVE rotation about car-local +X. This used to be copied from the
                    // browser (`spin -=`, where forward is -Z) and every traffic wheel rolled
                    // backwards; the driven/delivery cars already use the positive convention.
                    c.spin = Mathf.Repeat(c.spin + (c.v * dt) / (wr != 0 ? wr : 0.33f), Mathf.PI * 2f);
                }
                foreach (var c in cars) PlaceCar(c);
            }
            foreach (var c in _sky) PlaceSkyCar(c, t);
            UpdateProxies(playerPos);
            UpdateTrafficLods(camPos);
            PushCarLights(camPos);
            DrawBeams(camPos);
            DrawPools(camPos);
            DrawNavLights();
        }

        // ---------------------------------------------------------------- AV nav lights
        static Mesh _navMesh;
        static Material _navMat;
        static bool _navInit;
        static MeshRenderer _navRenderer;
        static Vector3[] _navVerts;
        static int _navCount = -1;

        static void EnsureNavAssets()
        {
            if (_navInit) return;
            _navInit = true;
            var sh = Shader.Find("NEON KAIDO/CarNavLight");
            if (sh == null) return;
            _navMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            var go = new GameObject("car_nav_lights");
            if (_rootT != null) go.transform.SetParent(_rootT, false);
            go.AddComponent<MeshFilter>();
            _navRenderer = go.AddComponent<MeshRenderer>();
            _navRenderer.sharedMaterial = _navMat;
            _navRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _navRenderer.receiveShadows = false;
            _navMesh = new Mesh { name = "car_nav_lights" };
            _navMesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = _navMesh;
        }

        static void BuildNavMesh(int avCount)
        {
            _navCount = avCount;
            int quads = avCount * 3;
            _navVerts = new Vector3[quads * 4];
            var cols = new Color[quads * 4];
            var uv0 = new Vector2[quads * 4];
            var uv1 = new Vector2[quads * 4];
            var tris = new int[quads * 6];
            var navCols = new[] { new Color(1f, 0.05f, 0.03f), new Color(0.05f, 1f, 0.2f), new Color(1f, 1f, 1f) };
            var corners = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) };
            for (int i = 0; i < avCount; i++)
                for (int k = 0; k < 3; k++)
                {
                    int b = (i * 3 + k) * 4;
                    float phase = (i * 0.61803399f) % 1f;
                    for (int v = 0; v < 4; v++)
                    {
                        cols[b + v] = navCols[k];
                        uv0[b + v] = corners[v];
                        uv1[b + v] = new Vector2(k, phase);
                    }
                    int ti = (i * 3 + k) * 6;
                    tris[ti] = b; tris[ti + 1] = b + 1; tris[ti + 2] = b + 2;
                    tris[ti + 3] = b + 2; tris[ti + 4] = b + 1; tris[ti + 5] = b + 3;
                }
            _navMesh.SetVertices(_navVerts);
            _navMesh.SetColors(cols);
            _navMesh.SetUVs(0, uv0);
            _navMesh.SetUVs(1, uv1);
            _navMesh.SetTriangles(tris, 0);
            _navMesh.bounds = new Bounds(new Vector3(0f, 80f, 0f), new Vector3(3000f, 400f, 3000f));
        }

        static void SetNavQuad(ref int vi, Vector3 center)
        {
            _navVerts[vi++] = center; _navVerts[vi++] = center;
            _navVerts[vi++] = center; _navVerts[vi++] = center;
        }

        static void DrawNavLights()
        {
            if (_sky == null || _sky.Count == 0) { if (_navRenderer != null) _navRenderer.enabled = false; return; }
            EnsureNavAssets();
            if (_navRenderer == null)
            {
                // Vehicles root was rebuilt -> nav GO was destroyed with it
                _navInit = false;
                EnsureNavAssets();
                if (_navRenderer == null) return;
            }
            if (_navMat == null || _navMesh == null) return;
            if (_navCount != _sky.Count || _navVerts == null) BuildNavMesh(_sky.Count);
            int vi = 0;
            for (int i = 0; i < _sky.Count; i++)
            {
                var c = _sky[i];
                if (c.go == null) { vi += 12; continue; }
                var tr = c.go.transform;
                SetNavQuad(ref vi, tr.TransformPoint(new Vector3(-1.25f, 0.75f, 1.55f)));
                SetNavQuad(ref vi, tr.TransformPoint(new Vector3(1.25f, 0.75f, 1.55f)));
                SetNavQuad(ref vi, tr.TransformPoint(new Vector3(0f, 0.95f, -2.62f)));
            }
            _navMesh.SetVertices(_navVerts);
            if (!_navRenderer.enabled) _navRenderer.enabled = true;
        }

        // ---------------------------------------------------------------- car lamp point lights
        // Port of lights.js tail/head PointLights: 28 red tail lights (range 7) and
        // 14 warm head lights (range 16) pooled for the nearest lit ground cars. The
        // port's materials are custom shaders, so these feed the fake light array in
        // AtmoCommon.hlsl rather than real Unity lights.
        static readonly Vector4[] _carLPos = new Vector4[42];
        static readonly Vector4[] _carLColor = new Vector4[42];
        static Transform _heroLampT;
        const float HERO_LEN = 4.958f;
        // 0xff1208 / 0xffe2c0 converted from sRGB to linear (three.js PointLight colors)
        static readonly Vector4 TAIL_COL = new Vector4(1f, 0.0031f, 0.0005f, 1f);
        static readonly Vector4 HEAD_COL = new Vector4(1f, 0.766f, 0.535f, 1f);

        static void PushCarLights(Vector3 camPos)
        {
            int slot = 0, tailN = 0, headN = 0;
            var live = ActiveCars;
            if (live == null) { Shader.SetGlobalFloat("_CarLCount", 0f); return; }
            foreach (var c in live)
            {
                if (c.go == null) continue;
                var tr = c.go.transform;
                if ((tr.position - camPos).sqrMagnitude > 50f * 50f) continue;
                if (tailN < 28)
                {
                    var p = tr.TransformPoint(new Vector3(0f, 0.3f, -(c.len * 0.5f + 1.5f)));
                    _carLPos[slot] = new Vector4(p.x, p.y, p.z, 7f);
                    _carLColor[slot] = new Vector4(TAIL_COL.x, TAIL_COL.y, TAIL_COL.z, 0.9f + c.brake * 2.5f);
                    slot++; tailN++;
                }
                if (headN < 14 && slot < 42)
                {
                    var p = tr.TransformPoint(new Vector3(0f, 0.7f, c.len * 0.5f + 2.6f));
                    _carLPos[slot] = new Vector4(p.x, p.y, p.z, 16f);
                    _carLColor[slot] = new Vector4(HEAD_COL.x, HEAD_COL.y, HEAD_COL.z, 9f);
                    slot++; headN++;
                }
                if (tailN >= 28 && headN >= 14) break;
            }
            // the car currently being driven (lights always on, brake gates the tail intensity)
            if (_drivenGo != null && _drivenType != null && _models.TryGetValue(_drivenType, out var dm))
            {
                var tr = _drivenGo.transform;
                if (tailN < 28 && slot < 42)
                {
                    var p = tr.TransformPoint(new Vector3(0f, 0.3f, -(dm.mf.length * 0.5f + 1.5f)));
                    _carLPos[slot] = new Vector4(p.x, p.y, p.z, 7f);
                    _carLColor[slot] = new Vector4(TAIL_COL.x, TAIL_COL.y, TAIL_COL.z, 0.9f + NeonKaido.Driving.BrakeLight * 2.5f);
                    slot++; tailN++;
                }
                if (headN < 14 && slot < 42)
                {
                    var p = tr.TransformPoint(new Vector3(0f, 0.7f, dm.mf.length * 0.5f + 2.6f));
                    _carLPos[slot] = new Vector4(p.x, p.y, p.z, 16f);
                    _carLColor[slot] = new Vector4(HEAD_COL.x, HEAD_COL.y, HEAD_COL.z, 9f);
                    slot++; headN++;
                }
            }
            // the delivered car (headlamps on, hazards/brake fed by Delivery)
            if (_extraLitGo != null && _extraLitType != null && _models.TryGetValue(_extraLitType, out var xm))
            {
                var tr = _extraLitGo.transform;
                if (tailN < 28 && slot < 42)
                {
                    var p = tr.TransformPoint(new Vector3(0f, 0.3f, -(xm.mf.length * 0.5f + 1.5f)));
                    _carLPos[slot] = new Vector4(p.x, p.y, p.z, 7f);
                    _carLColor[slot] = new Vector4(TAIL_COL.x, TAIL_COL.y, TAIL_COL.z, 0.9f + _extraLitBrake * 2.5f);
                    slot++; tailN++;
                }
                if (headN < 14 && slot < 42)
                {
                    var p = tr.TransformPoint(new Vector3(0f, 0.7f, xm.mf.length * 0.5f + 2.6f));
                    _carLPos[slot] = new Vector4(p.x, p.y, p.z, 16f);
                    _carLColor[slot] = new Vector4(HEAD_COL.x, HEAD_COL.y, HEAD_COL.z, 9f);
                    slot++; headN++;
                }
            }
            // the spawn-kerb hero keeps its head + tail lamps on in normal play
            if (_heroLampT == null) { var h = GameObject.Find("parked_hero"); if (h != null) _heroLampT = h.transform; }
            if (_heroLampT != null && (_drivenGo == null || _heroLampT.gameObject != _drivenGo))
            {
                float d2 = (_heroLampT.position - camPos).sqrMagnitude;
                if (d2 < 140f * 140f)
                {
                    if (tailN < 28 && slot < 42)
                    {
                        var p = _heroLampT.TransformPoint(new Vector3(0f, 0.3f, -(HERO_LEN * 0.5f + 1.5f)));
                        _carLPos[slot] = new Vector4(p.x, p.y, p.z, 7f);
                        _carLColor[slot] = new Vector4(TAIL_COL.x, TAIL_COL.y, TAIL_COL.z, 0.9f);
                        slot++;
                    }
                    if (headN < 14 && slot < 42)
                    {
                        var p = _heroLampT.TransformPoint(new Vector3(0f, 0.7f, HERO_LEN * 0.5f + 2.6f));
                        _carLPos[slot] = new Vector4(p.x, p.y, p.z, 16f);
                        _carLColor[slot] = new Vector4(HEAD_COL.x, HEAD_COL.y, HEAD_COL.z, 9f);
                        slot++;
                    }
                }
            }
            Shader.SetGlobalFloat("_CarLCount", slot);
            Shader.SetGlobalVectorArray("_CarLPos", _carLPos);
            Shader.SetGlobalVectorArray("_CarLColor", _carLColor);
        }

        // ---------------------------------------------------------------- edit-mode preview
        // Everything a moving car contributes is derived per frame from the live traffic list:
        // the lamp point lights, the beam cones, the headlight pools on the road. The Editor
        // never calls Update, so opening Main.unity showed the city with no car lighting at all
        // (and, before CarVisual, black bodies with dead lamps) — while Play looked correct.
        // NkEditorLighting rebuilds the same state from the serialized CarVisual components so a
        // static scene shows the same cars Play does.
        static List<Car> _preview;

        /// Live traffic in Play; the saved scene's traffic in the Editor. The statics survive a
        /// scene load, so `_cars` can still list cars whose GameObjects are gone (the same check
        /// VehicleTraffic.Update makes before re-linking); treat that as "no live traffic".
        static List<Car> ActiveCars =>
            _cars != null && _cars.Count > 0 && _cars[0].go != null ? _cars : _preview;

        /// Rebuild the per-car look, lamp lights, beams and pools for a static (edit-mode) scene.
        public static void PreviewVisuals(Vector3 camPos)
        {
            var comps = Object.FindObjectsByType<CarVisual>(FindObjectsInactive.Exclude);
            if (comps.Length == 0) return;          // scene predates CarVisual: nothing to restore
            if (_models == null && LoadModels() != null) return;
            if (_preview == null) _preview = new List<Car>(comps.Length);
            else _preview.Clear();
            foreach (var cv in comps)
            {
                cv.Apply();
                // Only ground traffic throws beams and pools. Parked rows sit dark (JS parks
                // them with light.set(0,0,0)) and the hero gets its own slot inside
                // PushCarLights; sky AVs only carry the nav lights, which stay Play-only.
                var parent = cv.transform.parent;
                if (parent == null || parent.name != "traffic") continue;
                if (cv.type == null || !_models.ContainsKey(cv.type)) continue;
                _preview.Add(new Car
                {
                    go = cv.gameObject, type = cv.type, len = cv.length,
                    paint = cv.paint, spin = cv.spin, brake = cv.lightLv.z,
                    pos = cv.transform.position,
                });
            }
            PushCarLights(camPos);
            DrawBeams(camPos);
            DrawPools(camPos);
        }

        /// Beam/pool rig parent. Editor-visible but never saved with the scene: it holds a
        /// DontSave material, so a copy baked into Main.unity would come back with no material.
        /// The stale rig is deleted first so a domain reload cannot leave two of them rendering.
        static Transform CreateRigRoot(string name)
        {
            if (_rootT == null)
            {
                var v = GameObject.Find("Vehicles");
                if (v != null) _rootT = v.transform;
            }
            if (_rootT != null)
            {
                var prev = _rootT.Find(name);
                if (prev != null) Object.DestroyImmediate(prev.gameObject);
            }
            var parent = new GameObject(name);
            parent.hideFlags = HideFlags.HideAndDontSave;
            if (_rootT != null) parent.transform.SetParent(_rootT, false);
            return parent.transform;
        }

        // ---------------------------------------------------------------- headlight beams
        // Port of lights.js `beams`: two merged open cones (apex at the lamps, opening
        // forward, ~3.4 deg down), drawn per lit moving car through an additive shader.
        // JS fades by (1 - facing)^3 so a car coming at the camera doesn't plaster a blob.
        static Mesh _beamMesh;
        static Material _beamMat;
        static bool _beamInit;
        static List<GameObject> _beamPool;
        static List<MeshRenderer> _beamRenderers;
        static List<MaterialPropertyBlock> _beamBlocks;

        static void EnsureBeamAssets()
        {
            if (_beamInit) return;
            _beamInit = true;
            var sh = Shader.Find("NEON KAIDO/CarLightBeam");
            if (sh == null) return;
            _beamMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _beamMat.SetColor("_BeamColor", new Color(1.0f, 0.86f, 0.66f));

            const int seg = 20;
            const float radius = 2.4f, len = 14f, tilt = 0.06f;
            var rot = Quaternion.Euler(tilt * Mathf.Rad2Deg, 0f, 0f);
            var verts = new List<Vector3>(2 * seg * 3);
            var nors = new List<Vector3>(2 * seg * 3);
            var uvs = new List<Vector2>(2 * seg * 3);
            var tris = new List<int>(2 * seg * 3);
            for (int s = 0; s < 2; s++)
            {
                float ox = s == 0 ? -0.62f : 0.62f;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i / (float)seg * Mathf.PI * 2f;
                    float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                    var n0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                    var n1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    int b = verts.Count;
                    verts.Add(rot * new Vector3(ox, 0f, 0f));
                    nors.Add(rot * (n0 + n1).normalized);
                    uvs.Add(new Vector2(0.5f, 1f));
                    verts.Add(rot * new Vector3(n0.x * radius + ox, n0.y * radius, len));
                    nors.Add(rot * n0);
                    uvs.Add(new Vector2(i / (float)seg, 0f));
                    verts.Add(rot * new Vector3(n1.x * radius + ox, n1.y * radius, len));
                    nors.Add(rot * n1);
                    uvs.Add(new Vector2((i + 1) / (float)seg, 0f));
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                }
            }
            var m = new Mesh { name = "car_light_beam" };
            m.SetVertices(verts); m.SetNormals(nors); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            _beamMesh = m;

            // pooled renderers rather than Graphics.DrawMesh: HDRP renders MeshRenderers
            // through the same transparent pass as the rest of the custom shaders.
            const int cap = 48;
            _beamPool = new List<GameObject>(cap);
            _beamRenderers = new List<MeshRenderer>(cap);
            _beamBlocks = new List<MaterialPropertyBlock>(cap);
            var parent = CreateRigRoot("car_beams");
            for (int i = 0; i < cap; i++)
            {
                var go = new GameObject("beam_" + i);
                go.transform.SetParent(parent, false);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<MeshFilter>().sharedMesh = _beamMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _beamMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                _beamPool.Add(go);
                _beamRenderers.Add(r);
                _beamBlocks.Add(new MaterialPropertyBlock());
            }
        }

        // ---------------------------------------------------------------- headlight ground pools
        static Mesh _poolMesh;
        static Material _poolMat;
        static bool _poolInit;
        static List<GameObject> _poolPool;
        static List<MeshRenderer> _poolRenderers;
        static List<MaterialPropertyBlock> _poolBlocks;

        static void EnsurePoolAssets()
        {
            if (_poolInit) return;
            _poolInit = true;
            var sh = Shader.Find("NEON KAIDO/CarLightPool");
            if (sh == null) return;
            _poolMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _poolMat.SetColor("_PoolColor", new Color(1f, 0.88f, 0.70f));

            // PlaneGeometry(5.5, 11).rotateX(-90).translate(0, 0.03, -7.5) in JS car space;
            // ours extends along +Z (Unity car forward).
            const float hw = 2.75f, hl = 5.5f, zc = 7.5f, y = 0.03f;
            var m = new Mesh { name = "car_light_pool" };
            m.SetVertices(new List<Vector3> {
                new Vector3(-hw, y, zc - hl), new Vector3(hw, y, zc - hl),
                new Vector3(-hw, y, zc + hl), new Vector3(hw, y, zc + hl) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetTriangles(new int[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.RecalculateBounds();
            _poolMesh = m;

            const int cap = 48;
            _poolPool = new List<GameObject>(cap);
            _poolRenderers = new List<MeshRenderer>(cap);
            _poolBlocks = new List<MaterialPropertyBlock>(cap);
            var parent = CreateRigRoot("car_pools");
            for (int i = 0; i < cap; i++)
            {
                var go = new GameObject("pool_" + i);
                go.transform.SetParent(parent, false);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<MeshFilter>().sharedMesh = _poolMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _poolMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                _poolPool.Add(go);
                _poolRenderers.Add(r);
                _poolBlocks.Add(new MaterialPropertyBlock());
            }
        }

        static void DrawPools(Vector3 camPos)
        {
            var live = ActiveCars;
            if (live == null) return;
            // Leaving Play mode destroys the DontSave material/mesh while _poolInit survives
            // (domain reload is disabled), so the init flag has to be dropped with them or the
            // rig never comes back in the Editor.
            if (_poolMat == null || _poolMesh == null) _poolInit = false;
            EnsurePoolAssets();
            if (_poolMat == null || _poolMesh == null) return;
            if (_poolPool == null || _poolPool.Count == 0 || _poolPool[0] == null)
            {
                _poolInit = false;
                EnsurePoolAssets();
                if (_poolPool == null || _poolPool.Count == 0) return;
            }
            int used = 0;
            foreach (var c in live)
            {
                if (c.go == null) continue;
                var tr = c.go.transform;
                if ((tr.position - camPos).sqrMagnitude > 160f * 160f) continue;
                if (used >= _poolPool.Count) break;
                var origin = tr.TransformPoint(new Vector3(0f, 0f, c.len * 0.5f));
                var go = _poolPool[used];
                go.transform.SetPositionAndRotation(origin, tr.rotation);
                _poolBlocks[used].SetFloat("_Intensity", 1f);
                _poolRenderers[used].SetPropertyBlock(_poolBlocks[used]);
                if (!go.activeSelf) go.SetActive(true);
                used++;
            }
            if (_drivenGo != null && _drivenType != null && _models.TryGetValue(_drivenType, out var dm) && used < _poolPool.Count)
            {
                var tr = _drivenGo.transform;
                var origin = tr.TransformPoint(new Vector3(0f, 0f, dm.mf.length * 0.5f));
                var go = _poolPool[used];
                go.transform.SetPositionAndRotation(origin, tr.rotation);
                _poolBlocks[used].SetFloat("_Intensity", 1f);
                _poolRenderers[used].SetPropertyBlock(_poolBlocks[used]);
                if (!go.activeSelf) go.SetActive(true);
                used++;
            }
            if (_extraLitGo != null && _extraLitType != null && _models.TryGetValue(_extraLitType, out var xm) && used < _poolPool.Count)
            {
                var tr = _extraLitGo.transform;
                var origin = tr.TransformPoint(new Vector3(0f, 0f, xm.mf.length * 0.5f));
                var go = _poolPool[used];
                go.transform.SetPositionAndRotation(origin, tr.rotation);
                _poolBlocks[used].SetFloat("_Intensity", 1f);
                _poolRenderers[used].SetPropertyBlock(_poolBlocks[used]);
                if (!go.activeSelf) go.SetActive(true);
                used++;
            }
            for (int i = used; i < _poolPool.Count; i++)
                if (_poolPool[i].activeSelf) _poolPool[i].SetActive(false);
        }

        static void DrawBeams(Vector3 camPos)
        {
            var live = ActiveCars;
            if (live == null) return;
            // Same as DrawPools: the DontSave material dies on a Play-mode exit but _beamInit
            // (a static, no domain reload) does not, which would leave the Editor with no cones.
            if (_beamMat == null || _beamMesh == null) _beamInit = false;
            EnsureBeamAssets();
            if (_beamMat == null || _beamMesh == null) return;
            if (_beamPool == null || _beamPool.Count == 0 || _beamPool[0] == null)
            {
                // Vehicles root was rebuilt (domain reload / scene reload) -> re-pool
                _beamInit = false;
                EnsureBeamAssets();
                if (_beamPool == null || _beamPool.Count == 0) return;
            }
            int used = 0;
            foreach (var c in live)
            {
                if (c.go == null) continue;
                var mf = _models[c.type].mf;
                var origin = c.go.transform.TransformPoint(new Vector3(0f, mf.height * 0.42f + 0.05f, c.len * 0.5f - 0.1f));
                var toCam = camPos - origin;
                float d = toCam.magnitude;
                if (d > 160f || d < 0.01f) continue;
                float facing = Mathf.Max(0f, Vector3.Dot(c.go.transform.forward, toCam / d));
                float k = Mathf.Pow(1f - facing, 3f);
                if (k < 0.02f) continue;
                if (used >= _beamPool.Count) break;
                var go = _beamPool[used];
                go.transform.SetPositionAndRotation(origin, c.go.transform.rotation);
                _beamBlocks[used].SetFloat("_Intensity", k);
                _beamRenderers[used].SetPropertyBlock(_beamBlocks[used]);
                if (!go.activeSelf) go.SetActive(true);
                used++;
            }
            // the driven car's own cones (JS mixes spotCar into the visible car list)
            if (_drivenGo != null && _drivenType != null && _models.TryGetValue(_drivenType, out var dm) && used < _beamPool.Count)
            {
                var mf = dm.mf;
                var origin = _drivenGo.transform.TransformPoint(new Vector3(0f, mf.height * 0.42f + 0.05f, mf.length * 0.5f - 0.1f));
                var toCam = camPos - origin;
                float d = toCam.magnitude;
                if (d > 0.01f)
                {
                    float facing = Mathf.Max(0f, Vector3.Dot(_drivenGo.transform.forward, toCam / d));
                    float k = Mathf.Pow(1f - facing, 3f);
                    var go = _beamPool[used];
                    go.transform.SetPositionAndRotation(origin, _drivenGo.transform.rotation);
                    _beamBlocks[used].SetFloat("_Intensity", k);
                    _beamRenderers[used].SetPropertyBlock(_beamBlocks[used]);
                    if (!go.activeSelf) go.SetActive(true);
                    used++;
                }
            }
            if (_extraLitGo != null && _extraLitType != null && _models.TryGetValue(_extraLitType, out var xm) && used < _beamPool.Count)
            {
                var mf = xm.mf;
                var origin = _extraLitGo.transform.TransformPoint(new Vector3(0f, mf.height * 0.42f + 0.05f, mf.length * 0.5f - 0.1f));
                var toCam = camPos - origin;
                float d = toCam.magnitude;
                if (d > 0.01f)
                {
                    float facing = Mathf.Max(0f, Vector3.Dot(_extraLitGo.transform.forward, toCam / d));
                    var go = _beamPool[used];
                    go.transform.SetPositionAndRotation(origin, _extraLitGo.transform.rotation);
                    _beamBlocks[used].SetFloat("_Intensity", Mathf.Pow(1f - facing, 3f));
                    _beamRenderers[used].SetPropertyBlock(_beamBlocks[used]);
                    if (!go.activeSelf) go.SetActive(true);
                    used++;
                }
            }
            for (int i = used; i < _beamPool.Count; i++)
                if (_beamPool[i].activeSelf) _beamPool[i].SetActive(false);
        }


    }
}
