// Port of rail.js buildTrains — 2 six-car trains on the two viaduct tracks.
// Car bodies from the shared extruded profile (cab nose deformation baked in),
// procedural body shader (windows/passengers/stripe/lamps), undercarriage +
// pantograph meshes, interior spill + headlight point lights, elliptical-free
// linear path with off-screen pause. Movement driven from PlayerController.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Trains
    {
        const int Cars = 6;
        const float CarLen = 20f, Speed = 21f, ShotT = 2f;
        static float _span, _period;
        static Transform[] _all;          // all car transforms, refreshed on demand
        static string[] _names;           // slot names, Build order
        static Transform[] _carLights;    // 6 spill lights
        static Transform[] _headLights;   // 2 headlights
        static float[] _start = new float[2];
        static float[] _phase = new float[2];
        static readonly float[] TrackZ = { Rail.Z - 2f, Rail.Z + 2f };
        static readonly int[] TrackDir = { 1, -1 };
        static readonly float[][] Stripes =
        {
            new[] { 0.05f, 0.55f, 0.22f },
            new[] { 0.9f, 0.35f, 0.05f },
        };

        static Vector3 Mir(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        // car profile relative to rail top (z, y) — normalised to CCW for extrusion
        static Vector2[] CarProfile()
        {
            var pts = new List<Vector2>
            {
                new Vector2(-1.42f, 0.98f), new Vector2(-1.48f, 1.5f), new Vector2(-1.49f, 2.3f), new Vector2(-1.46f, 2.9f),
            };
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.PI * (1 - i / 8f);
                float zz = Mathf.Cos(a) * 1.46f, yy = 2.9f + Mathf.Sin(a) * 0.98f;
                if (i > 0 && i < 8) pts.Add(new Vector2(zz, Mathf.Min(3.92f, yy)));
            }
            pts.Add(new Vector2(1.46f, 2.9f));
            pts.Add(new Vector2(1.49f, 2.3f));
            pts.Add(new Vector2(1.48f, 1.5f));
            pts.Add(new Vector2(1.42f, 0.98f));
            // signed area -> ensure CCW
            float area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b2 = pts[(i + 1) % pts.Count];
                area += a.x * b2.y - b2.x * a.y;
            }
            if (area < 0) pts.Reverse();
            return pts.ToArray();
        }

        static LmGeo MakeCarGeo(bool cab)
        {
            const float len = 19.5f;
            var g = LmUtil.ProfileGeometry(CarProfile(), -len / 2f, len / 2f);
            if (cab)
            {
                for (int i = 0; i < g.pos.Length; i++)
                {
                    var p = g.pos[i];
                    if (p.x > len / 2f - 2.6f)
                    {
                        float t = (p.x - (len / 2f - 2.6f)) / 2.6f;
                        float yN = (p.y - 0.98f) / 3.0f;
                        p.x -= t * (yN * yN) * 1.1f;
                        p.z *= 1 - 0.18f * t * t;
                        g.pos[i] = p;
                    }
                }
            }
            g.ComputeVertexNormals();
            return g;
        }

        static LmGeo MakeUnderGeo()
        {
            var ub = new LmBatch();
            var wheel = LmUtil.GenCylinder(0.43f, 0.43f, 0.14f, 12);
            foreach (var bx in new[] { -7f, 7f })
            {
                ub.Box("d", 2.8f, 0.75f, 2.3f, bx, 0.55f, 0);
                foreach (var wx in new[] { -1.05f, 1.05f })
                    foreach (var wz in new[] { -0.72f, 0.72f })
                        ub.Add("d", wheel, bx + wx, 0.43f, wz, Mathf.PI / 2, 0, 0);
            }
            ub.Box("d", 9f, 0.6f, 2.2f, 0, 0.75f, 0);
            ub.Box("d", 0.5f, 0.9f, 1f, 9.9f, 1.6f, 0);
            var parts = ub.Parts;
            foreach (var kv in parts)
                foreach (var g in kv.Value)
                    g.ComputeVertexNormals();
            // merge into one geo
            int total = 0;
            foreach (var kv in parts) foreach (var g in kv.Value) total += g.pos.Length;
            var m = new LmGeo { pos = new Vector3[total], nor = new Vector3[total], uv = new Vector2[total] };
            int o = 0;
            foreach (var kv in parts)
                foreach (var g in kv.Value)
                {
                    for (int i = 0; i < g.pos.Length; i++)
                    {
                        m.pos[o + i] = g.pos[i];
                        m.nor[o + i] = g.nor != null ? g.nor[i] : Vector3.up;
                        m.uv[o + i] = g.uv != null ? g.uv[i] : Vector2.zero;
                    }
                    o += g.pos.Length;
                }
            return m;
        }

        static LmGeo MakePantoGeo()
        {
            const float bas = 3.92f, top = 5.12f;
            var pb = new LmBatch();
            pb.Box("p", 1.6f, 0.12f, 1.2f, 0, bas + 0.06f, 0);
            LmUtil.BeamBetween(pb, "p", new Vector3(-0.7f, bas + 0.1f, 0), new Vector3(0.25f, bas + 0.55f, 0), 0.06f, 0.06f);
            LmUtil.BeamBetween(pb, "p", new Vector3(0.25f, bas + 0.55f, 0), new Vector3(-0.1f, top - 0.05f, 0), 0.05f, 0.05f);
            pb.Box("p", 0.12f, 0.06f, 1.7f, -0.1f, top - 0.02f, 0);
            var parts = pb.Parts;
            int total = 0;
            foreach (var kv in parts) foreach (var g in kv.Value) total += g.pos.Length;
            var m = new LmGeo { pos = new Vector3[total], nor = new Vector3[total], uv = new Vector2[total] };
            int o = 0;
            foreach (var kv in parts)
                foreach (var g in kv.Value)
                {
                    g.ComputeVertexNormals();
                    for (int i = 0; i < g.pos.Length; i++)
                    {
                        m.pos[o + i] = g.pos[i];
                        m.nor[o + i] = g.nor != null ? g.nor[i] : Vector3.up;
                        m.uv[o + i] = Vector2.zero;
                    }
                    o += g.pos.Length;
                }
            return m;
        }

        static GameObject Place(Transform parent, string name, LmGeo geo, Material mat)
        {
            var mesh = geo.ToUnityMesh(name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Light AddLight(Transform parent, string name, Color c, float i, float r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var li = go.AddComponent<Light>();
            li.type = LightType.Point;
            li.color = c;
            li.intensity = i;
            li.range = r;
            return li;
        }

        static Transform Find(string name)
        {
            var g = GameObject.Find(name);
            return g != null ? g.transform : null;
        }

        public static string Build(Transform root)
        {
            var sh = Shader.Find("NEON KAIDO/TrainCar");
            if (sh == null) return "trains: shader missing";

            var cabGeo = MakeCarGeo(true);
            var midGeo = MakeCarGeo(false);
            var underGeo = MakeUnderGeo();
            var pantoGeo = MakePantoGeo();

            var parent = new GameObject("Trains").transform;
            parent.transform.SetParent(root, false);
            var lp = new GameObject("trainLights").transform;
            lp.transform.SetParent(root, false);

            float H = Rail.Height, Zl = Rail.Z;
            float railTop = H + 0.38f;
            _span = (Rail.XEnd - Rail.XStart) + Cars * CarLen + 80;
            _period = _span / Speed + 14;

            int nCars = 2 * Cars;
            _all = new Transform[nCars * 3 + nCars]; // bodies + under + pantos(4)
            int idx = 0;
            var names = new List<string>();
            var rnd = new System.Random(7);

            for (int ti = 0; ti < 2; ti++)
            {
                // phases: at t=2s train 0 head at x=118, train 1 at x=135
                float targetHead = ti == 0 ? 118f : 135f;
                float start = TrackDir[ti] > 0 ? Rail.XStart - 40f : Rail.XEnd + 40f;
                _start[ti] = start;
                _phase[ti] = Mathf.Abs(targetHead - start) / Speed - ShotT;

                for (int c = 0; c < Cars; c++)
                {
                    bool isCab = c == 0 || c == Cars - 1;
                    string bname = $"train_body_{ti}_{c}";
                    var go = Place(parent, bname, isCab ? cabGeo : midGeo, null);
                    var mr = go.GetComponent<MeshRenderer>();
                    var mat = new Material(sh);
                    mat.SetFloat("_Head", isCab && c == 0 ? 1f : 0f);
                    mat.SetColor("_Stripe", new Color(Stripes[ti][0], Stripes[ti][1], Stripes[ti][2]));
                    mat.SetFloat("_InstId", isCab ? 100f + rnd.Next(50) : rnd.Next(100));
                    mr.sharedMaterial = mat;
                    names.Add(bname);
                    _all[idx++] = go.transform;

                    string uname = $"train_under_{ti}_{c}";
                    var ug = Place(parent, uname, underGeo, null);
                    ug.GetComponent<MeshRenderer>().sharedMaterial =
                        new Material(Shader.Find("NEON KAIDO/LandmarkMetal")) { color = new Color(0.04f, 0.04f, 0.045f) };
                    ug.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_Rough", 0.6f);
                    names.Add(uname);
                    _all[idx++] = ug.transform;

                    if (c == 1 || c == 4)
                    {
                        string pname = $"train_panto_{ti}_{c}";
                        var pg = Place(parent, pname, pantoGeo, null);
                        var pm = new Material(Shader.Find("NEON KAIDO/LandmarkMetal")) { color = new Color(0.25f, 0.25f, 0.27f) };
                        pm.SetFloat("_Rough", 0.4f);
                        pg.GetComponent<MeshRenderer>().sharedMaterial = pm;
                        names.Add(pname);
                        _all[idx++] = pg.transform;
                    }
                }
            }

            _carLights = new Transform[6];
            for (int i = 0; i < 6; i++)
                _carLights[i] = AddLight(lp, $"train_carL_{i}", new Color(1f, 0.94f, 0.86f), 60f, 16f).transform;
            _headLights = new Transform[2];
            for (int i = 0; i < 2; i++)
                _headLights[i] = AddLight(lp, $"train_headL_{i}", new Color(1f, 0.96f, 0.88f), 160f, 24f).transform;

            _names = names.ToArray();
            _all = new Transform[_names.Length];
            _healed = true;
            Update(0);
            return $"trains: 2 x {Cars} cars";
        }

        static bool _healed;

        static void Heal()
        {
            // recompute path constants — statics are wiped by play-entry domain reload
            _span = (Rail.XEnd - Rail.XStart) + Cars * CarLen + 80;
            _period = _span / Speed + 14;
            for (int ti = 0; ti < 2; ti++)
            {
                float targetHead = ti == 0 ? 118f : 135f;
                float start = TrackDir[ti] > 0 ? Rail.XStart - 40f : Rail.XEnd + 40f;
                _start[ti] = start;
                _phase[ti] = Mathf.Abs(targetHead - start) / Speed - ShotT;
            }
            if (_period <= 0f || float.IsNaN(_period)) return; // avoid NaN positions
            _all = new Transform[2 * Cars * 2 + 4];
            _carLights = new Transform[6];
            _headLights = new Transform[2];
            int idx = 0;
            for (int ti = 0; ti < 2; ti++)
                for (int c = 0; c < Cars; c++)
                {
                    _all[idx++] = Find($"train_body_{ti}_{c}");
                    _all[idx++] = Find($"train_under_{ti}_{c}");
                    if (c == 1 || c == 4) _all[idx++] = Find($"train_panto_{ti}_{c}");
                }
            for (int i = 0; i < 6; i++) _carLights[i] = Find($"train_carL_{i}");
            for (int i = 0; i < 2; i++) _headLights[i] = Find($"train_headL_{i}");
            _healed = true;
        }

        /// Per-frame movement — call with sim time. Slots in Build order:
        /// per car: body, under, [panto if c==1||c==4].
        public static void Update(double t)
        {
            if (_all == null || _all[0] == null || !_healed || _period <= 0f) Heal();
            if (_all == null || _all[0] == null || _period <= 0f || float.IsNaN(_period)) return;
            float railTop = Rail.Height + 0.38f;
            int k = 0, li = 0;
            for (int ti = 0; ti < 2; ti++)
            {
                int dir = TrackDir[ti];
                float local = (float)((t + _phase[ti]) % _period + _period) % _period;
                float head = _start[ti] + dir * local * Speed;
                for (int c = 0; c < Cars; c++)
                {
                    float cxm = head - dir * (CarLen * (c + 0.5f));
                    var pos = new Vector3(cxm, railTop, TrackZ[ti]);
                    // cab lead (c==0) faces travel dir; tail cab faces opposite; mids follow dir
                    float yaw = dir > 0 ? 0f : 180f;
                    if (c == Cars - 1) yaw = dir > 0 ? 180f : 0f; // tail cab reversed
                    var rot = Quaternion.Euler(0, yaw, 0);
                    Move(ref k, pos, rot);
                    Move(ref k, pos, rot);
                    if (c == 1 || c == 4) Move(ref k, pos, rot);
                    if (c % 2 == 0 && li < 6 && _carLights[li] != null)
                    {
                        var lp = new Vector3(cxm - dir * CarLen * 0.5f, railTop + 2.2f,
                            TrackZ[ti] + (TrackZ[ti] < Rail.Z ? -1.8f : 1.8f));
                        _carLights[li].position = Mir(lp);
                        li++;
                    }
                }
                if (_headLights[ti] != null)
                    _headLights[ti].position = Mir(new Vector3(head + dir * 6, railTop + 1.5f, TrackZ[ti]));
            }
        }

        static void Move(ref int k, Vector3 pos, Quaternion rot)
        {
            if (k >= _all.Length) return;
            var tr = _all[k];
            if (tr == null)
            {
                var g = GameObject.Find(_names[k]);
                tr = g != null ? g.transform : null;
                _all[k] = tr;
            }
            if (tr != null) { tr.position = Mir(pos); tr.rotation = rot; }
            k++;
        }
    }
}
