// Fixer "gigs" — waypoint beam in the world, objective text, reward toasts, next gig.
// Port of src/systems/player/gigs.js. Sim (x,y,z) coordinates; Unity z = -sim z.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public struct GigDef
    {
        public string Id, Fixer, Title, Area, Text, Jp;
        public double[] Pos;      // sim coords [x, y, z]
        public int Reward, Cred;
        public float Radius;
        public bool Elevator;
    }

    public class Gigs
    {
        public readonly List<GigDef> List = new List<GigDef>();
        public int Index;
        public string State = "active";   // 'active' | 'cooldown'
        public float Cooldown;
        public int Eddies = 1200, Cred, Completed;
        public GameObject Marker;
        public bool SurfaceResolved;

        Transform _beam, _halo, _core, _ring, _inner, _dia;
        Material _beamMat, _haloMat, _coreMat, _ringMat, _innerMat;
        MaterialPropertyBlock _mpb;

        static Material BeamMat(float gain, float nearK, float mode)
        {
            var m = new Material(Shader.Find("NEON KAIDO/GigBeam"));
            m.hideFlags = HideFlags.HideAndDontSave;
            m.SetFloat("_Gain", gain);
            m.SetFloat("_NearK", nearK);
            m.SetFloat("_Mode", mode);
            m.enableInstancing = false;
            return m;
        }

        // open-ended cylinder (caps would break the v-based fade)
        static Mesh OpenCylinder(float radius, float height, int segs)
        {
            var verts = new Vector3[(segs + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var norms = new Vector3[verts.Length];
            var tris = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float a = (float)i / segs * Mathf.PI * 2f;
                float x = Mathf.Sin(a), z = Mathf.Cos(a);
                verts[i] = new Vector3(x, -0.5f, z);
                verts[segs + 1 + i] = new Vector3(x, 0.5f, z);
                uvs[i] = new Vector2((float)i / segs, 0f);
                uvs[segs + 1 + i] = new Vector2((float)i / segs, 1f);
                norms[i] = norms[segs + 1 + i] = new Vector3(x, 0f, z);
            }
            for (int i = 0; i < segs; i++)
            {
                int b = i * 6;
                tris[b] = i; tris[b + 1] = segs + 1 + i; tris[b + 2] = i + 1;
                tris[b + 3] = i + 1; tris[b + 4] = segs + 1 + i; tris[b + 5] = segs + 2 + i;
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = verts; mesh.uv = uvs; mesh.normals = norms; mesh.triangles = tris;
            return mesh;
        }

        // flat disc / ring strip in local XY plane, thetaSegments around (positionLocal.xy drives ring shader)
        static Mesh CircleMesh(float r0, float r1, int segs)
        {
            int n = segs + 1;
            var verts = new Vector3[n * 2];
            var tris = new int[segs * 6];
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / segs * Mathf.PI * 2f;
                verts[i] = new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, 0f);
                verts[n + i] = new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, 0f);
            }
            for (int i = 0; i < segs; i++)
            {
                int b = i * 6;
                tris[b] = i; tris[b + 1] = n + i; tris[b + 2] = i + 1;
                tris[b + 3] = i + 1; tris[b + 4] = n + i; tris[b + 5] = n + i + 1;
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = verts; mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        static Mesh Octahedron(float r)
        {
            var v = new Vector3[6];
            v[0] = new Vector3(0, r, 0); v[1] = new Vector3(0, -r, 0);
            v[2] = new Vector3(r, 0, 0); v[3] = new Vector3(0, 0, r);
            v[4] = new Vector3(-r, 0, 0); v[5] = new Vector3(0, 0, -r);
            int[] t = { 0, 3, 2, 0, 2, 5, 0, 5, 4, 0, 4, 3, 1, 2, 3, 1, 5, 2, 1, 4, 5, 1, 3, 4 };
            var mesh = new Mesh();
            mesh.vertices = v; mesh.triangles = t; mesh.RecalculateNormals();
            return mesh;
        }

        static GigDef G(string id, string fixer, string title, string area, string text, string jp,
                        double[] pos, int reward, int cred, float radius, bool elevator = false)
            => new GigDef { Id = id, Fixer = fixer, Title = title, Area = area, Text = text, Jp = jp, Pos = pos, Reward = reward, Cred = cred, Radius = radius, Elevator = elevator };

        public static List<GigDef> MakeGigList()
        {
            // exact values from makeGigList(L)
            return new List<GigDef>
            {
                G("ramen", "WAKAKO", "NOODLE DROP", "NEON AVENUE", "Meet the courier outside the ramen stand", "ラーメン",
                  new[] { -(Layout.AvenueRoadHalfWidth + 2.2), 0.0, -18.0 }, 850, 4, 3.2f),
                G("river", "WAKAKO", "SAKURA DEAD DROP", "SAKURA RIVERWALK", "Collect the shard hidden under the cherry trees", "桜",
                  new[] { (Layout.PromenadeXMin + Layout.PromenadeXMax) / 2, Layout.PromenadeY, -120.0 }, 2400, 9, 3.5f),
                G("alley", "REGINA", "LANTERN ALLEY HANDOFF", "LANTERN ALLEY", "Climb the alley stairs and meet the netrunner", "提灯",
                  new[] { Layout.AlleyStairsXEnd - 6, (double)Layout.AlleyStairsRise, (double)Layout.AlleyZ }, 3100, 12, 3.2f),
                G("overlook", "REGINA", "EYES ON THE SKYLINE", "SKYLINE OVERLOOK", "Take the service lift to the rooftop overlook", "展望",
                  new[] { Layout.OverlookPosition[0], Layout.OverlookPosition[1] - 1.7, Layout.OverlookPosition[2] }, 4200, 15, 4f, true),
                G("bridge", "WAKAKO", "BRIDGE RENDEZVOUS", "MIRAI BRIDGE", "Meet the fixer's driver at the river crossing", "橋",
                  new[] { Layout.PromenadeXMax + 6, 0.0, Layout.Bridge0Z + 12 }, 5600, 20, 4f),
            };
        }

        public Gigs(Transform parent)
        {
            List.AddRange(MakeGigList());
            _mpb = new MaterialPropertyBlock();
            const float yCol = 0.886f;
            var yellow = new Color(1f, yCol, 0.227f);
            var cream = new Color(1f, 0.965f, 0.753f);
            var red = new Color(1f, 0.231f, 0.306f);

            Marker = new GameObject("gig-marker");
            Marker.transform.SetParent(parent, false);

            _beamMat = BeamMat(1f, 0.4f, 0f); _beamMat.SetColor("_Color", yellow);
            _haloMat = BeamMat(0.28f, 0f, 0f); _haloMat.SetColor("_Color", yellow);
            _coreMat = BeamMat(1f, 0.4f, 0f); _coreMat.SetColor("_Color", cream);
            _ringMat = BeamMat(1f, 1f, 1f); _ringMat.SetColor("_Color", yellow);
            _innerMat = BeamMat(1f, 1f, 1f); _innerMat.SetColor("_Color", red);

            _halo = Child("halo", OpenCylinder(2.6f, 200f, 32), _haloMat); _halo.localPosition = new Vector3(0, 100, 0);
            _beam = Child("beam", OpenCylinder(0.9f, 260f, 32), _beamMat); _beam.localPosition = new Vector3(0, 130, 0);
            _core = Child("core", OpenCylinder(0.08f, 260f, 8), _coreMat); _core.localPosition = new Vector3(0, 130, 0);
            _ring = Child("ring", CircleMesh(0f, 2.2f, 64), _ringMat);
            _ring.localRotation = Quaternion.Euler(-90f, 0, 0); _ring.localPosition = new Vector3(0, 0.06f, 0);
            _inner = Child("inner", CircleMesh(0.85f, 0.95f, 6), _innerMat);
            _inner.localRotation = Quaternion.Euler(-90f, 0, 0); _inner.localPosition = new Vector3(0, 0.07f, 0);
            _dia = Child("dia", Octahedron(0.32f), null);
            _dia.localScale = new Vector3(1f, 1.6f, 1f); _dia.localPosition = new Vector3(0, 2.4f, 0);
            var mr = _dia.GetComponent<MeshRenderer>();
            var diaShader = Shader.Find("HDRP/Unlit");
            if (diaShader != null)
            {
                var diaMat = new Material(diaShader) { hideFlags = HideFlags.HideAndDontSave };
                diaMat.SetColor("_UnlitColor", new Color(4f, 3.5f, 0.9f, 1f)); // HDR-bright yellow
                mr.sharedMaterial = diaMat;
            }

            // (JS adds a small point light; HDRP needs HDAdditionalLightData plumbing — skipped, the
            // emissive beam/ring/diamond carry the marker glow on their own)

            Place();
        }

        Transform Child(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Marker.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        public GigDef Current => List[Index % List.Count];

        public List<(string title, string status)> Journal()
        {
            int cur = Index % List.Count;
            var rows = new List<(string, string)>();
            for (int i = 0; i < List.Count; i++)
                rows.Add((List[i].Title, i < cur ? "done" : i == cur ? (State == "active" ? "active" : "done") : "locked"));
            return rows;
        }

        // ground height under the marker (sim x,z; stairs/river/sidewalk aware like the JS raycast)
        public double SurfaceY(GigDef g)
        {
            return GroundPlan.HeightAt(g.Pos[0], g.Pos[2]);
        }

        public void Place()
        {
            var g = Current;
            Marker.transform.position = SimCoord.ToUnity((float)g.Pos[0], (float)g.Pos[1], (float)g.Pos[2]);
            Marker.SetActive(true);
            SurfaceResolved = false;
        }

        public Vector3 WorldPos()
        {
            var p = Marker.transform.position;
            return new Vector3(p.x, p.y + 1.4f, p.z);
        }

        // returns interaction prompt (e.g. elevator) or null. playerFeet in UNITY coords.
        public struct Prompt { public string Key, Text; public Vector3 Target; }

        public Prompt? Update(float dt, float t, Vector3 playerFeet, string mode)
        {
            if (Marker == null) return null;
            if (!SurfaceResolved)
            {
                var g0 = Current;
                Marker.transform.position = SimCoord.ToUnity((float)g0.Pos[0], (float)SurfaceY(g0), (float)g0.Pos[2]);
                SurfaceResolved = true;
            }
            var mp = Marker.transform.position;
            float dCam = Vector3.Distance(Camera.main ? Camera.main.transform.position : mp, mp);
            Shader.SetGlobalFloat("_GigNear", Mathf.Clamp01((dCam - 8f) / 45f));
            _dia.localRotation = Quaternion.Euler(0, t * 1.6f * Mathf.Rad2Deg % 360f, 0);
            _dia.localPosition = new Vector3(0, 2.4f + Mathf.Sin(t * 2f) * 0.15f, 0);
            _inner.localRotation = Quaternion.Euler(-90f, 0, -t * 0.8f * Mathf.Rad2Deg % 360f);

            if (State == "cooldown")
            {
                Cooldown -= dt;
                if (Cooldown <= 0f)
                {
                    State = "active";
                    Index++;
                    Place();
                    GameplayHud.NewGigToast(Current);
                }
                return null;
            }
            var g = Current;
            float dx = playerFeet.x - mp.x;
            float dz = playerFeet.z - mp.z;
            float d2 = Mathf.Sqrt(dx * dx + dz * dz);
            float dy = playerFeet.y - mp.y;
            if (mode == "foot" && d2 < g.Radius && Mathf.Abs(dy) < 2.6f) { Complete(); return null; }
            if (g.Elevator && mode == "foot" && d2 < 34f && dy < -6f)
                return new Prompt { Key = "F", Text = "TAKE SERVICE LIFT", Target = mp };
            return null;
        }

        public event Action<GigDef> GigCompleted;

        public void Complete()
        {
            var g = Current;
            State = "cooldown";
            Cooldown = 4.5f;
            Marker.SetActive(false);
            Eddies += g.Reward; Cred += g.Cred; Completed++;
            GameplayHud.GigCompleteToast(g, Eddies);
            GigCompleted?.Invoke(g);
        }
    }
}
