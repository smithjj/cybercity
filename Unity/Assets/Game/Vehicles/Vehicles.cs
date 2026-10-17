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
            ["hero"] = new[] { new Color(0x0a, 0x0a, 0x0c) },
            ["kei"] = new[] { C(0xd8d6d0), C(0x2a3a52), C(0x8a1c22), C(0x101012), C(0xb7b9bc), C(0x5b6b4a), C(0xc9b48a), C(0x1d4a5c) },
            ["taxi"] = new[] { C(0xf0b21a), C(0x12161a), C(0x1c3f2a), C(0xe8e4da) },
            ["van"] = new[] { C(0xb4b4b0), C(0xaaa8a2), C(0x7d838a), C(0x24425e), C(0xbcbcb6) },
            ["av"] = new[] { C(0x0c0c10), C(0x2b2f38), C(0xd9d9dd), C(0x4a0d14), C(0x0e2a3a) },
        };
        static Color C(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

        class Manifest { public float length, width, height, axleF, axleR, wheelRadius; public float[] min, max; }

        class Lane
        {
            public Vector3 p0, dir; public float len, v0; public string kind; public float yaw;
            public List<(float s, string group, float off)> stops = new();
            public List<Car> cars = new();
            public Vector3 Point(float s) => p0 + dir * s;
        }

        class Car
        {
            public string type; public Color paint; public GameObject go; public MaterialPropertyBlock mpb;
            public float s, v, v0, len, spin, brake;
            public Lane lane;
            // sky
            public float th0, hOff, bob, laneOff, h, x, dir, skyV, s0; public Lane loop; // loop fields reused
            public Vector3 pos;
        }

        static Dictionary<string, (Mesh l2, Mesh l3, Manifest mf)> _models;
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
            m.vertices = mp; m.normals = mn; m.uv = mu;
            m.SetUVs(1, m1); m.SetUVs(2, m2);
            var idx = new int[mp.Length];
            for (int i = 0; i < mp.Length; i++) idx[i] = i;
            m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }

        static GameObject SpawnCar(Transform parent, string type, string suffix, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = suffix == "_l3" ? _models[type].l3 : _models[type].l2;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mats[suffix == "_l3" ? "car3" : "car"];
            return go;
        }

        // ---------------------------------------------------------------- build
        public static string Build(Transform root)
        {
            if (_models == null)
            {
                _models = new Dictionary<string, (Mesh, Mesh, Manifest)>();
                _mats = new Dictionary<string, Material>();
                var sh = Shader.Find("NEON KAIDO/VehicleCar");
                if (sh == null) return "vehicles: shader missing";
                _mats["car"] = new Material(sh);
                _mats["car3"] = new Material(sh);
            }
            var dir = "Assets/Game/Models/Vehicles/dump";
            foreach (var type in new[] { "hero", "kei", "taxi", "van", "av" })
            {
                if (_models.ContainsKey(type)) continue;
                if (!System.IO.File.Exists($"{dir}/veh_{type}.json")) return "vehicles: missing dump for " + type;
                var mf = JsonUtility.FromJson<Manifest>(System.IO.File.ReadAllText($"{dir}/veh_{type}.json"));
                _models[type] = (LoadCar(type, ""), LoadCar(type, "_l3"), mf);
            }

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

        static int BuildParked(Transform parent)
        {
            var rng = new Mulberry32(90210u);
            int count = 0;
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
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_PaintColor", paint);
                mpb.SetFloat("_Spin", 0);
                mpb.SetVector("_LightLv", Vector3.zero);
                go.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
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
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_PaintColor", new Color(0x07 / 255f, 0x07 / 255f, 0x0a / 255f));
                mpb.SetFloat("_Spin", 0);
                mpb.SetVector("_LightLv", new Vector3(1, 1, 0));
                go.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
                count++;
            }
            return count;
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
            c.mpb.SetColor("_PaintColor", c.paint);
            c.mpb.SetFloat("_Spin", c.spin);
            c.mpb.SetVector("_LightLv", new Vector3(1, 1, c.brake));
            c.go.GetComponent<MeshRenderer>().SetPropertyBlock(c.mpb);
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
            c.mpb.SetColor("_PaintColor", c.paint);
            c.mpb.SetFloat("_Spin", c.spin);
            c.mpb.SetVector("_LightLv", new Vector3(1, 1, 0));
            c.go.GetComponent<MeshRenderer>().SetPropertyBlock(c.mpb);
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
                }
            int j = 0;
            foreach (var c in _sky)
            {
                c.go = skT.transform.GetChild(j++).gameObject;
                c.mpb = new MaterialPropertyBlock();
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
                    c.s += c.v * dt;
                    c.brake = acc < -0.8f || c.v < 0.3f ? 1f : 0f;
                    if (c.s > lane.len) c.s -= lane.len;
                    float wr = _models[c.type].mf.wheelRadius;
                    c.spin -= (c.v * dt) / (wr != 0 ? wr : 0.33f);
                }
                foreach (var c in cars) PlaceCar(c);
            }
            foreach (var c in _sky) PlaceSkyCar(c, t);
        }


    }
}
