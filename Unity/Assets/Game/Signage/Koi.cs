// Port of the holographic koi from signage/billboards.js — 2 fish on elliptical
// swim paths above the avenue, tail sway + scan/flicker in the shader, path and
// heading updated on the CPU each frame from PlayerController.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Koi
    {
        class Spec
        {
            public Vector3 c;      // sim centre
            public float r, h, sp, ph;
            public Color cA, cB;
        }

        static readonly Spec[] Specs = new Spec[]
        {
            new Spec { c = new Vector3(0, 19, -60 + 22), r = 9,  h = 2.2f, sp = 0.22f,
                       cA = new Color(1.0f, 0.35f, 0.08f), cB = new Color(0.9f, 0.95f, 1.0f), ph = 0 },
            new Spec { c = new Vector3(1, 23, -60 - 30), r = 12, h = 3,   sp = -0.16f,
                       cA = new Color(0.2f, 0.8f, 1.0f),  cB = new Color(1.0f, 0.3f, 0.8f),  ph = 3 },
        };

        static Transform[] _go;

        static Vector3 Mir(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        // raw dump: sim space (head at +Z); mirror z + flip winding -> head at -Z in unity
        static Mesh LoadMesh()
        {
            string dir = "Assets/Game/Models/Signage/dump";
            var meta = System.IO.File.ReadAllText($"{dir}/koi_meta.txt").Trim().Split(' ');
            int nVerts = int.Parse(meta[0]), nIdx = int.Parse(meta[1]);
            var posB = System.IO.File.ReadAllBytes($"{dir}/koi_pos.bin");
            var norB = System.IO.File.ReadAllBytes($"{dir}/koi_nor.bin");
            var idxB = System.IO.File.ReadAllBytes($"{dir}/koi_idx.bin");

            var V = new Vector3[nVerts];
            var N = new Vector3[nVerts];
            for (int i = 0; i < nVerts; i++)
            {
                V[i] = new Vector3(System.BitConverter.ToSingle(posB, i * 12), System.BitConverter.ToSingle(posB, i * 12 + 4), -System.BitConverter.ToSingle(posB, i * 12 + 8));
                N[i] = new Vector3(System.BitConverter.ToSingle(norB, i * 12), System.BitConverter.ToSingle(norB, i * 12 + 4), -System.BitConverter.ToSingle(norB, i * 12 + 8));
            }
            var I = new int[nIdx];
            for (int i = 0; i < nIdx; i += 3)
            {
                int a = (int)System.BitConverter.ToUInt32(idxB, i * 4);
                int b = (int)System.BitConverter.ToUInt32(idxB, i * 4 + 4);
                int c = (int)System.BitConverter.ToUInt32(idxB, i * 4 + 8);
                I[i] = a; I[i + 1] = c; I[i + 2] = b; // mirror winding flip
            }
            var m = new Mesh { name = "koi" };
            m.vertices = V;
            m.normals = N;
            m.triangles = I;
            m.RecalculateBounds();
            return m;
        }

        /// Called from FacadeSigns.Build before the light budget (adds 2 pool lights).
        public static string Build(Transform root)
        {
            var old = GameObject.Find("/FacadeSigns/koi_0");
            if (old != null) Object.DestroyImmediate(old);
            var old2 = GameObject.Find("/FacadeSigns/koi_1");
            if (old2 != null) Object.DestroyImmediate(old2);

            var sh = Shader.Find("NEON KAIDO/Koi");
            if (sh == null) return "koi: shader missing";
            Mesh mesh;
            try { mesh = LoadMesh(); }
            catch (System.Exception e) { return "koi: dump missing (" + e.Message + ")"; }

            _go = new Transform[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                var s = Specs[i];
                var mat = new Material(sh);
                mat.SetColor("_ColA", s.cA);
                mat.SetColor("_ColB", s.cB);
                mat.SetFloat("_Phase", s.ph);
                var go = new GameObject("koi_" + i);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _go[i] = go.transform;
                // pool light at the swim centre (sim space)
                FacadeSigns.AddPoolLight(s.c, s.cA.r, s.cA.g, s.cA.b, 25, 18, 1.5f);
            }
            return $"koi: {Specs.Length} (2 lights)";
        }

        /// Per-frame swim path (sim math, mirrored) — call with sim time.
        public static void Update(double t)
        {
            if (_go == null) _go = new Transform[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                // scene reloads at play entry invalidate cached refs — re-find as needed
                if (_go[i] == null)
                {
                    var g = GameObject.Find($"/FacadeSigns/koi_{i}");
                    if (g == null) continue;
                    _go[i] = g.transform;
                }
                var s = Specs[i];
                double a = t * s.sp + s.ph;
                var p = new Vector3(
                    s.c.x + (float)System.Math.Cos(a) * s.r * 0.55f,
                    s.c.y + (float)System.Math.Sin(a * 2.1) * s.h * 0.5f,
                    s.c.z + (float)System.Math.Sin(a) * s.r);
                var d = new Vector3(
                    -(float)System.Math.Sin(a) * s.r * 0.55f * s.sp,
                    (float)System.Math.Cos(a * 2.1) * s.h * 0.5f * 2.1f * s.sp,
                    (float)System.Math.Cos(a) * s.r * s.sp);
                // mesh head is at -Z after the sim->unity mirror
                _go[i].position = Mir(p);
                _go[i].rotation = Quaternion.LookRotation(-Mir(d).normalized, Vector3.up);
            }
        }
    }
}
