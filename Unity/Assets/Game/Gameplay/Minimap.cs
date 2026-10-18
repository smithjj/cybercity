// NEON KAIDO — rotating CP2077-style minimap. Port of src/ui/minimap.js.
// A static city map is baked once from LAYOUT + the real building lots (BuildPlan.Lots);
// the route to the active gig is a Dijkstra path over the same road graph as the JS.
//
// Coordinate note: the Unity port mirrors the JS world in Z (Unity z = -sim z) and its compass
// calls Unity -Z "north" (spawn yaw = PI faces down -Z). The map follows that convention, so the
// bake is vertically flipped relative to the browser map: map y = (Z1 - simZ) * S with Z1 = +160.
// Everything else (graph, route, projection) uses plain sim coordinates and the JS math verbatim.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Minimap
    {
        // ---------------------------------------------------------------- constants (minimap.js)
        const float X0 = -280f, X1 = 300f, Z0 = -740f, Z1 = 160f, S = 3f;    // 3 px per metre
        const int TX = 1740, TY = 2700;                                     // (X1-X0)*S, (Z1-Z0)*S
        const int MAP_W = 290, MAP_H = 240;                                 // css box, hud.css
        const int CANVAS_W = 288, CANVAS_H = 238;                           // box inset 1px
        const float CHAMFER = 25f;                                          // css clip-path corner

        // palette (hud.css)
        static readonly Color KY = new Color(0.988f, 0.929f, 0.039f);
        static readonly Color KR = new Color(1f, 0.382f, 0.345f);
        static readonly Color KC = new Color(0.369f, 0.965f, 1f);
        static readonly Color KW = new Color(0.914f, 0.965f, 0.969f);
        static readonly Color KMapBg = new Color(0.016f, 0.035f, 0.043f);
        static readonly Color KArrowCar = new Color(0.988f, 0.929f, 0.039f);

        // ---------------------------------------------------------------- runtime state
        static Texture2D _map;
        static bool _bakedWithLots;
        static float _scale = 1.7f;                      // css px per metre, eased (minimap.js)
        static readonly List<Vector2> _nodes = new();
        static readonly List<Vector2Int> _edges = new();
        static List<Vector2> _route;
        static float _routeT;
        static bool _graphBuilt;

        // small sprites baked once (IMGUI cannot draw polygons/gradients directly)
        static Texture2D _arrowTex, _coneTex, _discTex, _vignetteTex;
        static Texture2D _white;

        // gui styles
        static GUIStyle _time, _timeAm, _weather, _loc, _tiny, _corner, _badge;
        static Font _font;

        // ================================================================ bake
        static float PX(float x) => (x - X0) * S;
        static float PY(float z) => (Z1 - z) * S;        // map-y from the top (north)

        static Color Hex(string hx)
        {
            var c = ColorUtility.TryParseHtmlString(hx, out var col) ? col : Color.magenta;
            return new Color(c.r, c.g, c.b, c.a);
        }

        static void Blend(Color32[] P, int px, int py, Color32 c)
        {
            if (px < 0 || px >= TX || py < 0 || py >= TY) return;
            int i = (TY - 1 - py) * TX + px;             // texture rows are bottom-up
            if (c.a >= 254) { P[i] = c; return; }
            var d = P[i];
            float a = c.a / 255f;
            P[i] = new Color32((byte)(d.r + (c.r - d.r) * a), (byte)(d.g + (c.g - d.g) * a),
                               (byte)(d.b + (c.b - d.b) * a), 255);
        }

        static void Set(Color32[] P, int px, int py, Color32 c)
        {
            if (px < 0 || px >= TX || py < 0 || py >= TY) return;
            P[(TY - 1 - py) * TX + px] = c;
        }

        static void Bound(float x0, float z0, float x1, float z1,
                          out int px0, out int py0, out int px1, out int py1)
        {
            float ax = Mathf.Min(x0, x1), bx = Mathf.Max(x0, x1);
            float az = Mathf.Min(z0, z1), bz = Mathf.Max(z0, z1);
            px0 = Mathf.Clamp(Mathf.RoundToInt(PX(ax)), 0, TX - 1);
            px1 = Mathf.Clamp(Mathf.RoundToInt(PX(bx)), 0, TX - 1);
            py0 = Mathf.Clamp(Mathf.RoundToInt(PY(bz)), 0, TY - 1);   // larger z -> up
            py1 = Mathf.Clamp(Mathf.RoundToInt(PY(az)), 0, TY - 1);
        }

        // fillRect in sim metres
        static void Rf(Color32[] P, Color c, float x0, float z0, float x1, float z1, bool blend = false)
        {
            Bound(x0, z0, x1, z1, out int px0, out int py0, out int px1, out int py1);
            var c32 = (Color32)c;
            for (int py = py0; py <= py1; py++)
                for (int px = px0; px <= px1; px++)
                {
                    if (blend) Blend(P, px, py, c32);
                    else Set(P, px, py, c32);
                }
        }

        static void StrokeR(Color32[] P, Color c, float x0, float z0, float x1, float z1, int th = 1)
        {
            float ax = Mathf.Min(x0, x1), bx = Mathf.Max(x0, x1);
            float az = Mathf.Min(z0, z1), bz = Mathf.Max(z0, z1);
            var c32 = (Color32)c;
            int ax0 = Mathf.Clamp(Mathf.RoundToInt(PX(ax)), 0, TX - 1);
            int ax1 = Mathf.Clamp(Mathf.RoundToInt(PX(bx)), 0, TX - 1);
            int az0 = Mathf.Clamp(Mathf.RoundToInt(PY(bz)), 0, TY - 1);
            int az1 = Mathf.Clamp(Mathf.RoundToInt(PY(az)), 0, TY - 1);
            for (int q = 0; q < th; q++)
            {
                for (int px = ax0; px <= ax1; px++)
                {
                    Blend(P, px, az0 + q, c32);
                    Blend(P, px, az1 + q, c32);
                }
                for (int py = az0; py <= az1; py++)
                {
                    Blend(P, ax0 + q, py, c32);
                    Blend(P, ax1 + q, py, c32);
                }
            }
        }

        // dashed vertical / horizontal line in metres (canvas setLineDash equivalents)
        static void VDash(Color32[] P, Color c, float x, float zA, float zB, float dashM, float gapM)
        {
            int px = Mathf.Clamp(Mathf.RoundToInt(PX(x)), 0, TX - 1);
            int y0 = Mathf.RoundToInt(PY(Mathf.Max(zA, zB)));
            int y1 = Mathf.RoundToInt(PY(Mathf.Min(zA, zB)));
            int period = Mathf.Max(1, Mathf.RoundToInt((dashM + gapM) * S));
            int dash = Mathf.Max(1, Mathf.RoundToInt(dashM * S));
            for (int y = y0; y <= y1; y++)
                if (((y - y0) % period + period) % period < dash) Blend(P, px, y, c);
        }

        static void HDash(Color32[] P, Color c, float z, float xA, float xB, int dashPx, int gapPx)
        {
            int py = Mathf.Clamp(Mathf.RoundToInt(PY(z)), 0, TY - 1);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(PX(Mathf.Min(xA, xB))), 0, TX - 1);
            int x1 = Mathf.Clamp(Mathf.RoundToInt(PX(Mathf.Max(xA, xB))), 0, TX - 1);
            int period = Mathf.Max(1, dashPx + gapPx);
            for (int x = x0; x <= x1; x++)
                if (((x - x0) % period + period) % period < dashPx) Blend(P, x, py, c);
        }

        static void Disc(Color32[] P, Color32 c, float x, float z, float rM)
        {
            int cx = Mathf.RoundToInt(PX(x)), cy = Mathf.RoundToInt(PY(z));
            int r = Mathf.Max(1, Mathf.RoundToInt(rM * S));
            for (int py = cy - r; py <= cy + r; py++)
                for (int px = cx - r; px <= cx + r; px++)
                {
                    float dx = px - cx, dy = py - cy;
                    if (dx * dx + dy * dy <= r * r) Blend(P, px, py, c);
                }
        }

        static void EnsureBaked()
        {
            bool haveLots = BuildPlan.Lots != null && BuildPlan.Lots.Count > 20;
            if (_map != null && (_bakedWithLots || !haveLots)) return;
            Bake(haveLots);
        }

        static void Bake(bool haveLots)
        {
            var P = new Color32[TX * TY];
            var bg = (Color32)Hex("#050c0f");
            for (int i = 0; i < P.Length; i++) P[i] = bg;

            // built-up district base
            Rf(P, Hex("#0a171b"), -230, 90, (float)Layout.PromenadeXMin - 2, -460);
            Rf(P, Hex("#09151a"), (float)Layout.RiverXCenter + (float)Layout.RiverHalfWidth + 4, 120, 290, -720);

            if (haveLots)
            {
                // real building footprints from the buildings system, shaded by height
                var lots = new List<BLot>(BuildPlan.Lots);
                lots.Sort((a, b) => a.H.CompareTo(b.H));
                foreach (var b in lots)
                {
                    double h = b.H > 0 ? b.H : 12;
                    float k = Mathf.Clamp01((float)(h / 70.0));
                    var col = new Color32((byte)Mathf.RoundToInt(16 + k * 22),
                                          (byte)Mathf.RoundToInt(34 + k * 34),
                                          (byte)Mathf.RoundToInt(40 + k * 36), 255);
                    float x0 = (float)Mathf.Min((float)b.X0, (float)b.X1), x1 = (float)Mathf.Max((float)b.X0, (float)b.X1);
                    float z0 = (float)Mathf.Min((float)b.Z0, (float)b.Z1), z1 = (float)Mathf.Max((float)b.Z0, (float)b.Z1);
                    if (x1 < X0 || x0 > X1 || z1 < Z0 || z0 > Z1) continue;
                    x0 += 0.5f; z0 += 0.5f; x1 -= 0.5f; z1 -= 0.5f;
                    if (x1 <= x0 || z1 <= z0) continue;
                    Rf(P, col, x0, z0, x1, z1);
                    StrokeR(P, new Color(0.369f, 0.965f, 1f, 0.06f + k * 0.14f), x0, z0, x1, z1);
                }
            }
            else
            {
                // seeded fallback blocks (buildStatic's lot() when lots are unavailable)
                var rnd = new Mulberry32(7u);
                void Lot(float x0, float x1, float z0, float z1)
                {
                    for (float x = x0; x < x1;)
                    {
                        float w = 12f + (float)rnd.Next() * 22f;
                        for (float z = z0; z < z1;)
                        {
                            float d = 10f + (float)rnd.Next() * 24f;
                            double v = rnd.Next();
                            var col = v > 0.92 ? Hex("#1b3940") : v > 0.6 ? Hex("#14292f") : Hex("#112328");
                            Rf(P, col, x + 1.4f, z + 1.4f, Mathf.Min(x + w, x1) - 1.4f, Mathf.Min(z + d, z1) - 1.4f);
                            z += d;
                        }
                        x += w;
                    }
                }
                Lot(-230, (float)Layout.PromenadeXMin - 3, -460, 90);
                Lot((float)Layout.RiverXCenter + (float)Layout.RiverHalfWidth + 5, 290, -720, 120);
            }

            // river + promenade
            float rvx0 = (float)(Layout.RiverXCenter - Layout.RiverHalfWidth);
            float rvx1 = (float)(Layout.RiverXCenter + Layout.RiverHalfWidth);
            int rx0 = Mathf.Clamp(Mathf.RoundToInt(PX(rvx0)), 0, TX - 1);
            int rx1 = Mathf.Clamp(Mathf.RoundToInt(PX(rvx1)), 0, TX - 1);
            int ry0 = Mathf.Clamp(Mathf.RoundToInt(PY((float)Layout.RiverZStart)), 0, TY - 1);
            int ry1 = Mathf.Clamp(Mathf.RoundToInt(PY((float)Layout.RiverZEnd)), 0, TY - 1);
            var rvA = (Color32)Hex("#0b3442");
            var rvB = (Color32)Hex("#0e4252");
            for (int px = rx0; px <= rx1; px++)
            {
                float t = Mathf.Abs(((px - rx0) / (float)Mathf.Max(1, rx1 - rx0)) * 2f - 1f);
                var col = Color32.Lerp(rvB, rvA, t);
                for (int py = ry0; py <= ry1; py++) Set(P, px, py, col);
            }
            StrokeR(P, new Color(0.369f, 0.965f, 1f, 0.35f), rvx0, (float)Layout.RiverZEnd, rvx1, (float)Layout.RiverZStart, 2);

            var pr = (Color32)Hex("#26343a");
            Rf(P, pr, (float)Layout.PromenadeXMin, (float)Layout.PromenadeZStart, (float)Layout.PromenadeXMax, (float)Layout.PromenadeZEnd);
            for (float z = (float)Layout.PromenadeZStart - 6; z > (float)Layout.PromenadeZEnd; z -= 11)
                Disc(P, new Color32(255, 127, 200, 140), (float)Layout.PromenadeXMax - 2, z, 2.2f);

            // roads (sidewalk then asphalt)
            var sw = (Color32)Hex("#2a4249");
            var rd = (Color32)Hex("#36565e");
            float avX = (float)Layout.AvenueX, avHW = (float)Layout.AvenueRoadHalfWidth, avSW = (float)Layout.AvenueSidewalkWidth;
            float avZ0 = (float)Layout.AvenueZStart, avZ1 = (float)Layout.AvenueZEnd;
            float csHW = (float)Layout.CrossRoadHalfWidth, csSW = (float)Layout.CrossSidewalkWidth;
            Rf(P, sw, avX - avHW - avSW, avZ0, avX + avHW + avSW, avZ1);
            foreach (var z in Layout.CrossZs)
                Rf(P, sw, (float)Layout.CrossXStart, (float)z - csHW - csSW, (float)Layout.CrossXEnd, (float)z + csHW + csSW);
            Rf(P, rd, avX - avHW, avZ0, avX + avHW, avZ1);
            foreach (var z in Layout.CrossZs)
                Rf(P, rd, (float)Layout.CrossXStart, (float)z - csHW, (float)Layout.CrossXEnd, (float)z + csHW);

            // lane markings
            var lane = new Color32(200, 230, 235, 46);
            VDash(P, lane, avX, avZ0, avZ1, 3f, 3f);
            foreach (var z in Layout.CrossZs) HDash(P, lane, (float)z, (float)Layout.CrossXStart, (float)Layout.CrossXEnd, 9, 9);

            // alley + stairs
            float alZ = (float)Layout.AlleyZ, alW = (float)Layout.AlleyWidth;
            float alX0 = (float)Layout.AlleyXStart, alX1 = (float)Layout.AlleyXEnd;
            Rf(P, Hex("#3a2a2c"), alX0, alZ - alW / 2, alX1, alZ + alW / 2);
            float stX0 = (float)Layout.AlleyStairsXStart, stX1 = (float)Layout.AlleyStairsXEnd;
            var stairC = new Color32(255, 97, 88, 140);
            for (float x = stX0; x > stX1; x -= 1.2f)
            {
                int px = Mathf.Clamp(Mathf.RoundToInt(PX(x)), 0, TX - 1);
                int py0 = Mathf.Clamp(Mathf.RoundToInt(PY(alZ + alW / 2)), 0, TY - 1);
                int py1 = Mathf.Clamp(Mathf.RoundToInt(PY(alZ - alW / 2)), 0, TY - 1);
                for (int py = py0; py <= py1; py++) Blend(P, px, py, stairC);
            }
            // lantern dots along the alley (x % 12 js parity on integer-ish steps)
            var lanternC = new Color32(255, 90, 60, 204);
            for (float x = alX0 - 4; x > alX1; x -= 6)
                Disc(P, lanternC, x, alZ + (Mathf.Abs(x % 12f) > 1f ? 1.6f : -1.6f), 0.6f);

            // bridges
            foreach (var b in new[] { Layout.Bridge0Z })
                Rf(P, Hex("#3d5a62"), (float)Layout.PromenadeXMin, (float)b - 5, (float)Layout.RiverXCenter + (float)Layout.RiverHalfWidth + 10, (float)b + 5);

            // elevated rail + expressway (overpasses)
            float erZ = (float)Layout.RailZ, erW = (float)Layout.RailWidth;
            float exZ = (float)Layout.ExpresswayZ, exW = (float)Layout.ExpresswayWidth;
            Rf(P, new Color(0.369f, 0.965f, 1f, 0.10f), X0, erZ - erW / 2, X1, erZ + erW / 2, blend: true);
            Rf(P, new Color(0.369f, 0.965f, 1f, 0.08f), X0, exZ - exW / 2, X1, exZ + exW / 2, blend: true);
            var railC = new Color32(94, 246, 255, 90);
            HDash(P, railC, erZ - erW / 2, X0, X1, 6, 4);
            HDash(P, railC, erZ + erW / 2, X0, X1, 6, 4);
            HDash(P, railC, exZ - exW / 2, X0, X1, 6, 4);
            HDash(P, railC, exZ + exW / 2, X0, X1, 6, 4);

            // overlook building highlight
            float ovx = (float)Layout.OverlookPosition[0], ovz = (float)Layout.OverlookPosition[2];
            StrokeR(P, new Color(0.369f, 0.965f, 1f, 0.6f), ovx - 8, ovz - 8, ovx + 8, ovz + 8, 2);

            if (_map != null)
            {
                if (Application.isPlaying) Object.Destroy(_map);
                else Object.DestroyImmediate(_map);
            }
            _map = new Texture2D(TX, TY, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1,
                name = "nk-minimap",
            };
            _map.SetPixels32(P);
            _map.Apply(false, false);
            _bakedWithLots = haveLots;
        }

        // ================================================================ graph (buildGraph)
        static int Node(float x, float z)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (Mathf.Abs(_nodes[i].x - x) < 0.5f && Mathf.Abs(_nodes[i].y - z) < 0.5f) return i;
            _nodes.Add(new Vector2(x, z));
            return _nodes.Count - 1;
        }

        static void Chain(params Vector2[] pts)
        {
            for (int i = 0; i < pts.Length - 1; i++)
                _edges.Add(new Vector2Int(Node(pts[i].x, pts[i].y), Node(pts[i + 1].x, pts[i + 1].y)));
        }

        static void BuildGraph()
        {
            _nodes.Clear(); _edges.Clear();
            float avX = (float)Layout.AvenueX;
            float alZ = (float)Layout.AlleyZ, alX0 = (float)Layout.AlleyXStart, alX1 = (float)Layout.AlleyXEnd;
            float stX0 = (float)Layout.AlleyStairsXStart, stX1 = (float)Layout.AlleyStairsXEnd;
            float px = (float)((Layout.PromenadeXMin + Layout.PromenadeXMax) / 2);
            float prZ0 = (float)Layout.PromenadeZStart, prZ1 = (float)Layout.PromenadeZEnd;

            var az = new List<float> { (float)Layout.AvenueZStart, -60, alZ, -180, -300, (float)Layout.AvenueZEnd };
            az.Sort((a, b) => b.CompareTo(a));
            var avPts = new Vector2[az.Count];
            for (int i = 0; i < az.Count; i++) avPts[i] = new Vector2(avX, az[i]);
            Chain(avPts);

            foreach (var z in Layout.CrossZs)
                Chain(new Vector2((float)Layout.CrossXStart, (float)z), new Vector2(alX1, (float)z),
                      new Vector2(avX, (float)z), new Vector2(px, (float)z), new Vector2((float)Layout.CrossXEnd, (float)z));

            Chain(new Vector2(avX, alZ), new Vector2(alX0, alZ), new Vector2(stX0, alZ),
                  new Vector2(stX1, alZ), new Vector2(alX1, alZ));

            Chain(new Vector2(px, prZ0), new Vector2(px, -60), new Vector2(px, -120), new Vector2(px, -180),
                  new Vector2(px, (float)Layout.Bridge0Z), new Vector2(px, -300), new Vector2(px, prZ1));

            foreach (var b in new[] { Layout.Bridge0Z })
                Chain(new Vector2(px, (float)b),
                      new Vector2((float)Layout.RiverXCenter + (float)Layout.RiverHalfWidth + 8, (float)b));

            _graphBuilt = true;
        }

        struct Proj { public float D; public Vector2 Q; public int E, A, B; }

        static Proj Project(Vector2 p)
        {
            Proj best = new Proj { D = float.MaxValue };
            for (int i = 0; i < _edges.Count; i++)
            {
                var A = _nodes[_edges[i].x];
                var B = _nodes[_edges[i].y];
                float dx = B.x - A.x, dz = B.y - A.y;
                float l2 = dx * dx + dz * dz;
                if (l2 < 1e-9f) l2 = 1;
                float t = Mathf.Clamp01(((p.x - A.x) * dx + (p.y - A.y) * dz) / l2);
                var q = new Vector2(A.x + dx * t, A.y + dz * t);
                float d = Vector2.Distance(p, q);
                if (d < best.D) best = new Proj { D = d, Q = q, E = i, A = _edges[i].x, B = _edges[i].y };
            }
            return best;
        }

        static List<Vector2> ComputeRoute(Vector2 from, Vector2 to)
        {
            if (!_graphBuilt) BuildGraph();
            var s = Project(from);
            var t = Project(to);
            var path = new List<Vector2>();
            path.Add(from);
            if (s.E < 0 || t.E < 0) { path.Add(to); return path; }
            if (s.E == t.E) { path.Add(s.Q); path.Add(t.Q); path.Add(to); return path; }

            int n = _nodes.Count;
            int S0 = n, T0 = n + 1;
            Vector2 PosAt(int i) => i == S0 ? s.Q : i == T0 ? t.Q : _nodes[i];

            var adj = new List<(int v, float w)>[n + 2];
            for (int i = 0; i < adj.Length; i++) adj[i] = new List<(int, float)>();
            void Link(int a, int b)
            {
                float d = Vector2.Distance(PosAt(a), PosAt(b));
                adj[a].Add((b, d)); adj[b].Add((a, d));
            }
            foreach (var e in _edges) Link(e.x, e.y);
            Link(S0, s.A); Link(S0, s.B); Link(T0, t.A); Link(T0, t.B);

            var dist = new float[n + 2];
            var prev = new int[n + 2];
            var done = new bool[n + 2];
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[S0] = 0;
            for (;;)
            {
                int u = -1;
                for (int i = 0; i < n + 2; i++)
                    if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || dist[u] == float.MaxValue || u == T0) break;
                done[u] = true;
                foreach (var (v, w) in adj[u])
                    if (dist[u] + w < dist[v]) { dist[v] = dist[u] + w; prev[v] = u; }
            }
            var back = new List<Vector2>();
            for (int u = T0; u >= 0; u = prev[u]) { back.Add(PosAt(u)); if (prev[u] < 0 && u != S0) break; }
            back.Reverse();
            path.AddRange(back);
            path.Add(to);
            return path;
        }

        /// <summary>Toggle rotating / north-up (hud.js "ROTATING MINIMAP" setting).</summary>
        public static void ToggleRotate()
        {
            Settings.Set("minimapRotate", !Settings.MinimapRotate);
            GameplayHud.Note(Settings.MinimapRotate ? "MINIMAP: ROTATING" : "MINIMAP: NORTH-UP",
                             Settings.MinimapRotate ? "HEADING-UP ORIENTATION ENGAGED" : "FIXED NORTH ORIENTATION");
        }

        // ================================================================ sprite assets
        static void EnsureAssets()
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_white == null)
            {
                _white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "nk-mm-white" };
                var cs = new Color32[16];
                for (int i = 0; i < 16; i++) cs[i] = new Color32(255, 255, 255, 255);
                _white.SetPixels32(cs); _white.Apply(false, false);
            }
            if (_arrowTex == null) _arrowTex = BakeArrow();
            if (_coneTex == null) _coneTex = BakeCone();
            if (_discTex == null) _discTex = BakeDisc();
            if (_vignetteTex == null) _vignetteTex = BakeVignette();
            if (_time == null) BuildStyles();
        }

        static void BuildStyles()
        {
            GUIStyle Make(int size, Color c, FontStyle fs, TextAnchor an, bool rich = true)
            {
                var s = new GUIStyle { font = _font, fontSize = size, alignment = an, fontStyle = fs, richText = rich };
                s.normal.textColor = c;
                s.stretchWidth = false;
                return s;
            }
            _time = Make(18, KC, FontStyle.Bold, TextAnchor.MiddleLeft);
            _timeAm = Make(11, new Color(KC.r, KC.g, KC.b, 0.8f), FontStyle.Normal, TextAnchor.MiddleLeft);
            _weather = Make(13, KC, FontStyle.Normal, TextAnchor.MiddleRight);
            _loc = Make(13, KW, FontStyle.Normal, TextAnchor.MiddleLeft);
            _tiny = Make(10, new Color(KR.r, KR.g, KR.b, 0.75f), FontStyle.Normal, TextAnchor.MiddleRight);
            _corner = Make(9, new Color(KC.r, KC.g, KC.b, 0.6f), FontStyle.Normal, TextAnchor.MiddleLeft);
            _badge = Make(11, new Color(0.035f, 0.075f, 0.086f), FontStyle.Bold, TextAnchor.MiddleCenter, rich: false);
        }

        // 4x4-supersampled polygon fill for the player arrow (canvas polygon in minimap.js)
        static Texture2D BakeArrow()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "nk-mm-arrow", filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            // canvas: a=9, points (0,-a) (0.72a,0.8a) (0,0.38a) (-0.72a,0.8a); texture a = 20
            const float a = 20f;
            var pts = new Vector2[] {
                new Vector2(0, -a), new Vector2(a * 0.72f, a * 0.8f),
                new Vector2(0, a * 0.38f), new Vector2(-a * 0.72f, a * 0.8f),
            };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float u = x + (sx + 0.5f) / 4f - N / 2f;
                            float v = y + (sy + 0.5f) / 4f - N / 2f;
                            if (Inside(pts, new Vector2(u, v))) hits++;
                        }
                    byte al = (byte)(hits * 255 / 16);
                    px[y * N + x] = new Color32(255, 255, 255, al);
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return tex;
        }

        static bool Inside(Vector2[] pts, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            {
                if (((pts[i].y > p.y) != (pts[j].y > p.y)) &&
                    (p.x < (pts[j].x - pts[i].x) * (p.y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x))
                    c = !c;
            }
            return c;
        }

        // view cone: radial gradient wedge, ±0.6 rad around up, radius = texture ratio
        static Texture2D BakeCone()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "nk-mm-cone", filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            float half = N / 2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f - half) / half;
                    float v = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(u * u + v * v);
                    // angle from up (0,-1): atan2(u, -v)
                    float ang = Mathf.Atan2(u, -v);
                    float f = 1f - Mathf.Clamp01((r - 0.02f) / 0.98f);
                    if (Mathf.Abs(ang) > 0.6f || r > 1f) f = 0;
                    float edge = Mathf.Clamp01((0.6f - Mathf.Abs(ang)) / 0.12f);
                    byte al = (byte)Mathf.RoundToInt(255 * 0.28f * f * edge);
                    px[y * N + x] = new Color32(94, 246, 255, al);
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return tex;
        }

        static Texture2D BakeDisc()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "nk-mm-disc", filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            float half = N / 2f, r = half - 1.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r + 0.75f - d);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return tex;
        }

        static Texture2D BakeVignette()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "nk-mm-vig", filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f;
                    float v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01((r - 0.62f) / 0.8f) * 0.75f;
                    px[y * N + x] = new Color32(4, 9, 11, (byte)Mathf.RoundToInt(a * 255));
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return tex;
        }

        // ================================================================ per-frame prep + draw
        // The rotated map is blitted on the GPU into a RenderTexture in Prepare() (driven from
        // PlayerController.Update). GUI.matrix rotates correctly, but assigning it replaces the
        // clip matrix so GUI.BeginClip stops clipping; an identity-matrix RT draw clips properly.
        static RenderTexture _rt;
        static Material _rotMat;
        static bool _ready;
        static int _fw, _fh;
        static float _fZoom = 1f, _fCx, _fCy, _fK = 1f, _fSc = 1f, _fRotDeg, _fRotRad, _fHeading;
        static float _fPpx, _fPpy, _fElev;
        static Vector2 _fPos, _fTarget;
        static bool _fHasTarget, _fRotate, _fCar;

        static float HeadingFrom(Camera cam)
        {
            var f = cam.transform.forward;
            return (Mathf.Atan2(f.x, -f.z) * Mathf.Rad2Deg + 360f) % 360f;
        }

        public static void Prepare(float dt)
        {
            _ready = false;
            if (!GameplayHud.Enabled) return;
            if (Screen.width < 64 || Screen.height < 64) return;
            var cam = Camera.main;
            if (cam == null) return;
            EnsureAssets();
            EnsureBaked();

            float z = Mathf.Clamp(Screen.height / 1080f, 0.55f, 2.2f);
            int W = Mathf.RoundToInt(CANVAS_W * z), H = Mathf.RoundToInt(CANVAS_H * z);
            if (W < 32 || H < 32) return;

            bool car = NeonKaido.Driving.Active;
            var posU = car ? NeonKaido.Driving.CarPosition
                           : (PlayerController.I != null ? PlayerController.I.transform.position : cam.transform.position);
            var simV = SimCoord.FromUnity(posU);
            var ps = new Vector2(simV.x, simV.z);
            float heading = HeadingFrom(cam);

            _scale += ((car ? 1.05f : 1.75f) - _scale) * Mathf.Min(1f, dt * 3f);
            float sc = _scale * z, k = sc / S;

            bool rotate = Settings.MinimapRotate;
            float rotDeg = rotate ? -heading : 0f;
            float rotRad = rotDeg * Mathf.Deg2Rad;
            float ppx = PX(ps.x), ppy = PY(ps.y);

            var gigs = Gameplay.Gigs;
            bool hasTarget = gigs != null && gigs.Marker != null && gigs.Marker.activeSelf;
            var tgt = Vector2.zero;
            if (hasTarget)
            {
                var tv = SimCoord.FromUnity(gigs.Marker.transform.position);
                tgt = new Vector2(tv.x, tv.z);
                _routeT -= dt;
                if (_route == null || _routeT <= 0f) { _route = ComputeRoute(ps, tgt); _routeT = 0.4f; }
                if (_route != null && _route.Count > 0) _route[0] = ps;
            }

            if (_rt == null || _rt.width != W || _rt.height != H)
            {
                if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
                _rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "nk-minimap-rt",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                _rt.Create();
            }
            if (_rotMat == null)
            {
                var sh = Shader.Find("Hidden/NEON KAIDO/MinimapRotate");
                if (sh == null) { Debug.LogWarning("[NK] minimap rotate shader missing"); return; }
                _rotMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            }
            _rotMat.SetVector("_TexSize", new Vector4(TX, TY, 0f, 0f));
            _rotMat.SetVector("_Player", new Vector4(ppx, ppy, 0f, 0f));
            _rotMat.SetVector("_Rot", new Vector4(Mathf.Cos(rotRad), Mathf.Sin(rotRad), 0f, 0f));
            _rotMat.SetVector("_Size", new Vector4(W, H, 0f, 0f));
            _rotMat.SetFloat("_Scale", Mathf.Max(0.01f, k));
            Graphics.Blit(_map, _rt, _rotMat);

            _fw = W; _fh = H; _fZoom = z;
            _fCx = W * 0.5f; _fCy = H * 0.56f;
            _fK = k; _fSc = sc; _fRotDeg = rotDeg; _fRotRad = rotRad; _fHeading = heading;
            _fPpx = ppx; _fPpy = ppy; _fPos = ps; _fTarget = tgt; _fHasTarget = hasTarget;
            _fRotate = rotate;
            _fElev = car ? posU.y + 0.3f : posU.y - 1.6f;   // feet height (eye-root rig) / street-level car
            _fCar = car;
            _ready = true;
        }

        public static void Draw(float rainNow, double clock, Gigs gigs)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            if (!_ready || _rt == null) return;

            float z = _fZoom;
            float bw = MAP_W * z, bh = MAP_H * z;
            float mx0 = Screen.width - (34f + MAP_W) * z;
            float my0 = 26f * z;
            float W = _fw, H = _fh;
            float clipX = mx0 + (bw - W) * 0.5f, clipY = my0 + (bh - H) * 0.5f;

            // 1px cyan frame; the canvas is inset 1px inside it (hud.css .nk-map)
            Fill(new Rect(mx0, my0, bw, bh), new Color(KC.r, KC.g, KC.b, 0.55f));

            var savedMat = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            GUI.BeginClip(new Rect(clipX, clipY, W, H));
            Fill(new Rect(0, 0, W, H), KMapBg);
            GUI.DrawTexture(new Rect(0, 0, W, H), _rt);
            DrawOverlays(z, W, H, gigs);

            GUI.EndClip();
            GUI.matrix = savedMat;

            DrawChamfer(clipX, clipY, W, H, z);
            DrawHeader(mx0, my0, bw, z, clock, rainNow);
            DrawFoot(mx0, my0 + bh, bw, z, _fElev);
        }

        static void DrawOverlays(float z, float W, float H, Gigs gigs)
        {
            float cx = _fCx, cy = _fCy, sc = _fSc, rotRad = _fRotRad;
            float c = Mathf.Cos(rotRad), s = Mathf.Sin(rotRad);

            Vector2 ToS(float simx, float simz)
            {
                float dx = (simx - _fPos.x) * sc;
                float dy = (_fPos.y - simz) * sc;
                return new Vector2(cx + dx * c - dy * s, cy + dx * s + dy * c);
            }
            Vector2 ClampEdge(Vector2 p, float m)
                => new Vector2(Mathf.Clamp(p.x, m, W - m), Mathf.Clamp(p.y, m, H - m));

            // route to the active gig
            if (_fHasTarget) DrawRouteScreen(z, _fPos, sc, rotRad, cx, cy);

            // POIs: other gigs in the journal (minimap.js opts.pois; the browser hook is unfed)
            if (gigs != null)
            {
                for (int i = 0; i < gigs.List.Count; i++)
                {
                    if (i == gigs.Index % gigs.List.Count) continue;
                    var sp = ClampEdge(ToS((float)gigs.List[i].Pos[0], (float)gigs.List[i].Pos[2]), 10f * z);
                    DrawDisc(sp, 3f * z, new Color(KC.r, KC.g, KC.b, 0.9f));
                }
            }

            // target diamond (pulses, clamps to the map edge)
            if (_fHasTarget)
            {
                var raw = ToS(_fTarget.x, _fTarget.y);
                var tp = ClampEdge(raw, 14f * z);
                bool edge = (tp - raw).sqrMagnitude > 0.01f;
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.15f;
                float r = 6f * z * pulse;
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, tp);
                Fill(new Rect(tp.x - r * 1.9f, tp.y - r * 1.9f, r * 3.8f, r * 3.8f), new Color(0.988f, 0.929f, 0.039f, 0.12f));
                Fill(new Rect(tp.x - r, tp.y - r, r * 2f, r * 2f), edge ? new Color(KY.r, KY.g, KY.b, 0.85f) : KY);
                Fill(new Rect(tp.x - r * 0.35f, tp.y - r * 0.35f, r * 0.7f, r * 0.7f), new Color(0.1f, 0.078f, 0f));
                GUI.matrix = saved;
            }

            // player: view cone + arrow (rotate about the map centre)
            float arrowDeg = _fRotate ? 0f : _fHeading;
            var mat = GUI.matrix;
            GUIUtility.RotateAroundPivot(arrowDeg, new Vector2(cx, cy));
            GUI.color = new Color(1f, 1f, 1f, 0.62f);
            GUI.DrawTexture(new Rect(cx - 70f * z, cy - 70f * z, 140f * z, 140f * z), _coneTex);
            GUI.color = _fCar ? KArrowCar : KW;      // minimap.js: yellow in a car, white on foot
            GUI.DrawTexture(new Rect(cx - 13f * z, cy - 13f * z, 26f * z, 26f * z), _arrowTex);
            GUI.color = Color.white;
            GUI.matrix = mat;

            // edge vignette (canvas radial gradient)
            GUI.DrawTexture(new Rect(0, 0, W, H), _vignetteTex);

            // scanlines (.scan: 1px dark every 3px)
            var scan = new Color(0f, 0f, 0f, 0.18f);
            for (float y = 0; y < H; y += 3f * z) Fill(new Rect(0, y, W, Mathf.Min(z, H - y)), scan);

            // north badge on the rim
            float ix = Mathf.Sin(rotRad), iy = -Mathf.Cos(rotRad);
            float inset = 14f * z;
            float tx = ix > 1e-5f ? (W - inset - cx) / ix : ix < -1e-5f ? (inset - cx) / ix : float.MaxValue;
            float ty = iy > 1e-5f ? (H - inset - cy) / iy : iy < -1e-5f ? (inset - cy) / iy : float.MaxValue;
            float t = Mathf.Min(tx, ty);
            if (float.IsInfinity(t) || t < 0f) t = 0f;
            var np = new Vector2(cx + ix * t, cy + iy * t);
            DrawDisc(np, 13f * z, new Color(KY.r, KY.g, KY.b, 0.18f));   // css glow
            DrawDisc(np, 9f * z, KY);
            Label(new Rect(np.x - 14f * z, np.y - 11f * z, 28f * z, 22f * z), "N", _badge);

            // NET//KBK-09 corner tag
            _corner.fontSize = Mathf.Max(8, Mathf.RoundToInt(9f * z));
            Label(new Rect(8f * z, H - 16f * z, 120f * z, 14f * z), "NET//KBK-09", _corner);
        }

        // Route: yellow dashed line with round caps, drawn as screen-space dots (see Prepare note).
        static void DrawRouteScreen(float z, Vector2 ps, float sc, float rotRad, float cx, float cy)
        {
            if (_route == null || _route.Count < 2) return;
            float c = Mathf.Cos(rotRad), s = Mathf.Sin(rotRad);
            var pts = new Vector2[_route.Count];
            for (int i = 0; i < _route.Count; i++)
            {
                float dx = (_route[i].x - ps.x) * sc;
                float dy = (ps.y - _route[i].y) * sc;
                pts[i] = new Vector2(cx + dx * c - dy * s, cy + dx * s + dy * c);
            }

            float dash = 2f * z, gap = 6f * z, width = 2.6f * z;
            float period = dash + gap;
            float phase = Time.unscaledTime * 20f * z;              // JS: lineDashOffset = -now*0.02*dpr
            float step = Mathf.Max(0.6f, 0.8f * z);
            var glow = new Color(KY.r, KY.g, KY.b, 0.10f);
            var core = new Color(KY.r, KY.g, KY.b, 0.95f);
            float dist = 0f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                Vector2 dir = (b - a) / len;
                for (float t = 0f; t <= len; t += step)
                {
                    Vector2 p = a + dir * t;
                    DrawDisc(p, width * 1.15f, glow);
                    if (Mathf.Repeat(dist + t + phase, period) < dash)
                        DrawDisc(p, width * 0.62f, core);
                }
                dist += len;
            }
        }

        // chamfered top-left corner (CSS clip-path) and its 1px border
        static void DrawChamfer(float x0, float y0, float W, float H, float z)
        {
            float cut = Mathf.Min(CHAMFER * z, Mathf.Min(W, H) * 0.4f);
            int rows = Mathf.CeilToInt(cut);
            for (int i = 0; i < rows; i++)
                Fill(new Rect(x0, y0 + i, Mathf.Max(0f, cut - i), 1f), KMapBg);
            // diagonal frame edge
            int steps = Mathf.Max(8, Mathf.RoundToInt(cut));
            var c = new Color(KC.r, KC.g, KC.b, 0.55f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Fill(new Rect(x0 + cut * (1f - t), y0 + cut * t, Mathf.Max(1f, z * 0.8f), Mathf.Max(1f, z * 0.8f)), c);
            }
        }

        // ---------------------------------------------------------------- header / footer
        static void DrawHeader(float mx0, float my0, float mapW, float z, double clock, float rainNow)
        {
            int mins = (int)clock % (24 * 60);
            int hh = mins / 60, m = mins % 60;
            int h12 = ((hh + 11) % 12) + 1;
            string tstr = h12 + ":" + m.ToString("00");
            _time.fontSize = Mathf.Max(10, Mathf.RoundToInt(18f * z));
            _timeAm.fontSize = Mathf.Max(8, Mathf.RoundToInt(11f * z));

            var sz = _time.CalcSize(new GUIContent(tstr));
            float baseY = my0 - 5f * z - 20f * z;
            // glow
            var gl = _time;
            var oldC = gl.normal.textColor;
            gl.normal.textColor = new Color(KC.r, KC.g, KC.b, 0.25f);
            Label(new Rect(mx0 + 1.5f * z, baseY + 1.5f * z, sz.x + 10f * z, 20f * z), tstr, gl);
            gl.normal.textColor = oldC;
            Label(new Rect(mx0, baseY, sz.x + 10f * z, 20f * z), tstr, _time);
            Label(new Rect(mx0 + sz.x + 4f * z, baseY + 4f * z, 30f * z, 16f * z),
                  hh >= 12 ? "PM" : "AM", _timeAm);

            string wx = (rainNow > 0.75f ? "HEAVY RAIN" : rainNow > 0.35f ? "RAIN" : rainNow > 0.05f ? "DRIZZLE" : "OVERCAST")
                      + " · " + Mathf.RoundToInt(14 - rainNow * 2) + "°C";
            _weather.fontSize = Mathf.Max(8, Mathf.RoundToInt(13f * z));
            var wsz = _weather.CalcSize(new GUIContent(wx));
            float right = mx0 + mapW;
            Label(new Rect(right - wsz.x - 2f * z, baseY + 3f * z, wsz.x + 4f * z, 16f * z), wx, _weather);
            // rain-cloud glyph (the SVG in hud.js): three puffs, a base, three slanted drops
            float gx = right - wsz.x - 6f * z - 17f * z, gy = baseY + 5f * z;
            var ic = new Color(KC.r, KC.g, KC.b, 0.9f);
            DrawDisc(new Vector2(gx + 4f * z, gy + 3.5f * z), 3.2f * z, ic);
            DrawDisc(new Vector2(gx + 8.5f * z, gy + 2.6f * z), 4f * z, ic);
            DrawDisc(new Vector2(gx + 13f * z, gy + 3.5f * z), 3.2f * z, ic);
            Fill(new Rect(gx + 1.5f * z, gy + 3.5f * z, 14f * z, 3.4f * z), ic);
            for (int i = 0; i < 3; i++)
            {
                float x = gx + 4f * z + i * 4.5f * z;
                Fill(new Rect(x, gy + 8f * z, Mathf.Max(1f, 1.2f * z), 2.8f * z), ic);
                Fill(new Rect(x - 1f * z, gy + 10.4f * z, Mathf.Max(1f, 1.2f * z), 2.8f * z), ic);
            }
        }

        static void DrawFoot(float mx0, float top, float mapW, float z, float elev)
        {
            var zone = Gameplay.CurrentZone;
            _loc.fontSize = Mathf.Max(8, Mathf.RoundToInt(13f * z));
            Label(new Rect(mx0, top + 4f * z, mapW - 80f * z, 18f * z),
                  "<b>" + zone.District + "</b> · " + zone.Area, _loc);
            _tiny.fontSize = Mathf.Max(7, Mathf.RoundToInt(10f * z));
            Label(new Rect(mx0 + mapW - 90f * z, top + 5f * z, 90f * z, 16f * z),
                  "ELEV " + Mathf.RoundToInt(elev) + "M", _tiny);
        }

        // ---------------------------------------------------------------- diagnostics
        public static string DebugState() =>
            "screen=" + Screen.width + "x" + Screen.height + " z=" + _fZoom.ToString("F3") +
            " scale=" + _scale.ToString("F2") + " tex=" + (_map != null ? _map.width + "x" + _map.height : "-") +
            " lotsBaked=" + _bakedWithLots + " graph=" + _edges.Count + " route=" + (_route != null ? _route.Count : 0) +
            " ready=" + _ready + " rt=" + (_rt != null ? _rt.width + "x" + _rt.height : "-") +
            " heading=" + _fHeading.ToString("F1") + " rot=" + _fRotDeg.ToString("F1") +
            " px=" + _fPpx.ToString("F0") + " py=" + _fPpy.ToString("F0") +
            " sc=" + _fSc.ToString("F2") + " rtSize=" + _fw + "x" + _fh +
            " target=" + (_fHasTarget ? _fTarget.x.ToString("F0") + "," + _fTarget.y.ToString("F0") : "-");

        public static string DebugPixels()
        {
            EnsureBaked();
            var sb = new System.Text.StringBuilder();
            foreach (int py in new[] { 200, 390, 600 })
                foreach (int px in new[] { 700, 850, 1000 })
                    sb.Append(px + "," + py + "=" + _map.GetPixel(px, TY - 1 - py) + " ");
            return sb.ToString();
        }

        /// <summary>Editor helper: write the baked static map out as a PNG and report its size.</summary>
        public static string DumpStaticMap(string path)
        {
            EnsureBaked();
            System.IO.File.WriteAllBytes(path, _map.EncodeToPNG());
            return "map " + TX + "x" + TY + " lots=" + _bakedWithLots + " -> " + path;
        }

        // ---------------------------------------------------------------- primitives
        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        static void DrawDisc(Vector2 c, float radius, Color tint)
        {
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), _discTex);
            GUI.color = old;
        }

        static void Label(Rect r, string s, GUIStyle st)
        {
            if (!string.IsNullOrEmpty(s)) GUI.Label(r, s, st);
        }
    }
}
