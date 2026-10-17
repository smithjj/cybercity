// Street lamps + alley utility-pole lamps — port of signage/street.js (lampAt + avenue/cross/alley loops).
// Merged meshes with vertex-color tint; real Light components so EnsureLightArrays picks them up.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class StreetLamps
    {
        static Transform _root;

        public static string Build(Material lampBody, Material lampLens, Material poleBody, Material poleLens)
        {
            var old = GameObject.Find("/StreetLamps");
            if (old != null) Object.DestroyImmediate(old);
            var rootGo = new GameObject("StreetLamps");
            _root = rootGo.transform;

            var lampBM = Load("lamp_body");
            var lampLM = Load("lamp_lens");
            var poleBM = Load("pole_body");
            var poleLM = Load("pole_lens");

            // slot 0/1 = lamp body/lens, 2/3 = pole body/lens
            var V = new List<Vector3>[4];
            var Ix = new List<int>[4];
            var C = new List<Color>[4];
            for (int s = 0; s < 4; s++) { V[s] = new List<Vector3>(); Ix[s] = new List<int>(); C[s] = new List<Color>(); }
            var lights = new List<(Vector3 pos, Color col, float inten, float range)>();
            int lamps = 0;

            // JS lampAt: y = groundY ?? sideY; body metal; lens tint; light (2.2,7.75,0) rot yaw, 55/22
            // alley pole: y = alleyGroundY; light (0.95,4.5,0) rot yaw, 14/13
            void Place(double x, double z, double yaw, Color tint, bool pole)
            {
                double y = pole ? AlleyGroundY(x) : Layout.AvenueCurbHeight;
                var pos = new Vector3((float)x, (float)y, (float)-z);
                var rot = Quaternion.Euler(0f, (float)(-yaw * 180.0 / System.Math.PI), 0f);
                int slot = pole ? 2 : 0;
                Merge(V[slot], Ix[slot], C[slot], pole ? poleBM : lampBM, pos, rot, pole ? new Color(0.36f, 0.35f, 0.33f) : new Color(0.38f, 0.39f, 0.4f));
                Merge(V[slot + 1], Ix[slot + 1], C[slot + 1], pole ? poleLM : lampLM, pos, rot, tint);
                var lv = pole ? new Vector3(0.95f, 4.5f, 0f) : new Vector3(2.2f, 7.75f, 0f);
                lights.Add((rot * lv + pos, tint, pole ? 14f : 55f, pole ? 13f : 22f));
                lamps++;
            }

            // avenue lamps (sim space: avenue along -Z from zStart=60 to zEnd=-420)
            double rhw = Layout.AvenueRoadHalfWidth;
            int k = 0;
            for (double z = Layout.AvenueZStart - 6; z > Layout.AvenueZEnd + 5; z -= 26)
            {
                if (NearCross(z, 3)) continue;
                bool warm = (k++ % 5) != 3;
                var col = warm ? new Color(1.0f, 0.62f, 0.3f) : new Color(0.85f, 0.92f, 1.0f);
                Place(-(rhw + 0.55), z, 0.0, col, false);
                Place(rhw + 0.55, z - 13, System.Math.PI, col, false);
            }

            // cross-street lamps (all warm)
            double crhw = Layout.CrossRoadHalfWidth;
            foreach (var zc in Layout.CrossZs)
            {
                for (double x = Layout.CrossXStart + 10; x < Layout.CrossXEnd - 5; x += 28)
                {
                    if (System.Math.Abs(x) < Layout.AvenueRoadHalfWidth + Layout.AvenueSidewalkWidth + 4) continue;
                    Place(x, zc - (crhw + 0.5), -System.Math.PI / 2, new Color(1.0f, 0.62f, 0.3f), false);
                    Place(x + 14, zc + (crhw + 0.5), System.Math.PI / 2, new Color(1.0f, 0.62f, 0.3f), false);
                }
            }

            // alley utility poles with lamps
            int i = 0;
            for (double x = Layout.AlleyXStart - 6; x > Layout.AlleyXEnd + 4; x -= 12)
            {
                int side = (i++ % 2) == 1 ? 1 : -1;
                double z = Layout.AlleyZ + side * (Layout.AlleyWidth / 2 - 0.35);
                double yaw = side < 0 ? -System.Math.PI / 2 : System.Math.PI / 2;
                bool warm = i % 3 != 0; // JS uses post-increment i
                var col = warm ? new Color(1.0f, 0.7f, 0.4f) : new Color(0.8f, 0.95f, 1.0f);
                Place(x, z, yaw, col, true);
            }

            Commit(V[0], Ix[0], C[0], "LampBodies", lampBody);
            Commit(V[1], Ix[1], C[1], "LampLenses", lampLens);
            Commit(V[2], Ix[2], C[2], "PoleBodies", poleBody);
            Commit(V[3], Ix[3], C[3], "PoleLenses", poleLens);

            for (int j = 0; j < lights.Count; j++)
            {
                var (p, c, inten, range) = lights[j];
                var go = new GameObject($"lampL_{j}");
                go.transform.SetParent(_root, false);
                go.transform.position = p;
                var pl = go.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.color = c;
                pl.range = range;
                pl.intensity = inten;
                pl.shadows = LightShadows.None;
            }

            // re-push full light arrays (buildings + lamps) so edit-mode view shows lamp pools
            var all = new List<Light>();
            foreach (var rn in new[] { "/Buildings", "/StreetLamps" })
            {
                var r = GameObject.Find(rn);
                if (r != null) all.AddRange(r.GetComponentsInChildren<Light>());
            }
            if (all.Count > 0)
            {
                var blPos = new Vector4[all.Count];
                var blCol = new Vector4[all.Count];
                for (int j = 0; j < all.Count; j++)
                {
                    var l = all[j];
                    blPos[j] = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
                    blCol[j] = new Vector4(l.color.r, l.color.g, l.color.b, l.intensity);
                }
                BuildingsRuntime.PushLightArrays(blPos, blCol, new List<Material>(), null);
            }
            return $"street lamps: {lamps} lamps, {lights.Count} lights, {V[0].Count + V[2].Count} body verts";
        }

        static bool NearCross(double z, double pad)
        {
            foreach (var c in Layout.CrossZs)
                if (System.Math.Abs(z - c) < Layout.CrossRoadHalfWidth + Layout.CrossSidewalkWidth + pad) return true;
            return false;
        }

        // facades.js alleyGroundY: stairs interpolation
        static double AlleyGroundY(double x)
        {
            double xs = Layout.AlleyStairsXStart, xe = Layout.AlleyStairsXEnd, rise = Layout.AlleyStairsRise;
            if (x > xs) return 0;
            if (x < xe) return rise;
            return rise * (xs - x) / (xs - xe);
        }

        static void Merge(List<Vector3> V, List<int> Ix, List<Color> C, Mesh m, Vector3 pos, Quaternion rot, Color tint)
        {
            if (m == null) return;
            int b = V.Count;
            var mv = m.vertices;
            foreach (var v in mv) { V.Add(rot * v + pos); C.Add(tint); }
            foreach (int t in m.triangles) Ix.Add(b + t);
        }

        static void Commit(List<Vector3> V, List<int> Ix, List<Color> C, string name, Material mat)
        {
            if (V.Count == 0) return;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(V);
            mesh.SetColors(C);
            mesh.SetIndices(Ix, MeshTopology.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Mesh Load(string name)
        {
            // pre-dumped raw buffers (node): dump/<name>_pos.bin f32 xyz, _idx.bin u16/u32, _meta.txt "<verts> <idx> <bits>"
            string dir = "Assets/Game/Models/Signage/dump";
            var meta = System.IO.File.ReadAllText($"{dir}/{name}_meta.txt").Trim().Split(' ');
            int nVerts = int.Parse(meta[0]), nIdx = int.Parse(meta[1]);
            bool u32 = meta[2] == "32";
            var posBytes = System.IO.File.ReadAllBytes($"{dir}/{name}_pos.bin");
            var idxBytes = System.IO.File.ReadAllBytes($"{dir}/{name}_idx.bin");
            var verts = new Vector3[nVerts];
            for (int i = 0; i < nVerts; i++)
            {
                float x = System.BitConverter.ToSingle(posBytes, i * 12);
                float y = System.BitConverter.ToSingle(posBytes, i * 12 + 4);
                float z = System.BitConverter.ToSingle(posBytes, i * 12 + 8);
                verts[i] = new Vector3(x, y, -z); // glTF -z forward ≈ sim z: mirror to Unity
            }
            var idx = new int[nIdx];
            for (int i = 0; i < nIdx; i++) idx[i] = u32 ? (int)System.BitConverter.ToUInt32(idxBytes, i * 4) : System.BitConverter.ToUInt16(idxBytes, i * 2);
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = idx;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
