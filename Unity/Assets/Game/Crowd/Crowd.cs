// NPC crowd — port of src/systems/crowd/{index,agents,characters,vat}.js.
// Near LOD (<=34m): real skinned characters (FBX imported from the GLBs via Blender, legacy
// Animation, per-garment tinted material instances, umbrellas/phone accessories).
// Far LOD (34-170m): walk/idle poses baked at build time (anim.Sample + BakeMesh, body+outfit
// merged with a uv2 body/garment flag) and drawn with Graphics.DrawMeshInstanced, pose picked
// by stride phase. Population/lanes/avoidance are a direct agents.js port.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Crowd
    {
        // ---------------------------------------------------------------- palettes (characters.js)
        static readonly Dictionary<string, string[]> Palette = new Dictionary<string, string[]>
        {
            { "cloth_msuit", new[] { "1,1,1", "0.55,0.58,0.7", "0.45,0.42,0.4", "0.35,0.35,0.38" } },
            { "cloth_mjacket", new[] { "1,1,1", "0.45,0.45,0.5", "0.6,0.45,0.35", "0.3,0.32,0.38" } },
            { "cloth_mshirt", new[] { "1,1,1", "0.5,0.6,0.8", "0.35,0.35,0.35" } },
            { "cloth_mtee", new[] { "0.35,0.35,0.38", "0.18,0.18,0.2", "0.6,0.62,0.65" } },
            { "cloth_felegant", new[] { "1,1,1", "0.5,0.55,0.75", "0.35,0.3,0.3" } },
            { "cloth_fcasual", new[] { "1,1,1", "0.35,0.35,0.4", "0.75,0.55,0.6" } },
            { "cloth_fsport", new[] { "1,1,1", "0.6,0.4,0.5", "0.35,0.4,0.45" } },
            { "hair", new[] { "0.16,0.13,0.12", "0.09,0.08,0.08", "0.3,0.22,0.16", "0.12,0.1,0.1", "0.5,0.2,0.55", "0.15,0.45,0.6", "0.1,0.09,0.09", "0.75,0.72,0.7" } },
        };
        static readonly Dictionary<string, string[]> Garment = new Dictionary<string, string[]>
        {
            { "rain_black", new[] { "0.012,0.012,0.014", "0.01,0.014,0.03", "0.02,0.025,0.015", "0.08,0.006,0.01", "0.03,0.03,0.032" } },
            { "rain_red", new[] { "0.22,0.012,0.02", "0.012,0.012,0.014", "0.35,0.22,0.01", "0.01,0.07,0.07", "0.06,0.012,0.09" } },
            { "trench_tan", new[] { "0.30,0.24,0.15", "0.16,0.15,0.12", "0.05,0.05,0.055", "0.04,0.05,0.08" } },
            { "trench_beige", new[] { "0.42,0.34,0.24", "0.2,0.17,0.13", "0.06,0.06,0.065", "0.25,0.09,0.06" } },
            { "tech_black", new[] { "0.025,0.026,0.03", "0.05,0.055,0.045", "0.06,0.06,0.065", "0.015,0.02,0.035" } },
            { "tech_white", new[] { "0.55,0.56,0.58", "0.28,0.29,0.31", "0.03,0.03,0.035", "0.35,0.3,0.38" } },
            { "knit_charcoal", new[] { "0.06,0.06,0.065", "0.02,0.02,0.022", "0.15,0.15,0.16", "0.02,0.03,0.07", "0.12,0.02,0.03", "0.05,0.06,0.035" } },
            { "knit_grey", new[] { "0.22,0.21,0.23", "0.03,0.03,0.035", "0.35,0.22,0.3", "0.04,0.08,0.12", "0.5,0.48,0.45" } },
            { "skirt_black", new[] { "0.02,0.02,0.022", "0.12,0.03,0.035", "0.05,0.05,0.06" } },
            { "tights_black", new[] { "0.03,0.03,0.035", "0.015,0.015,0.018" } },
            { "boots_black", new[] { "0.02,0.018,0.017", "0.05,0.035,0.025" } },
            { "trim_cyan", new[] { "0.1,0.9,1.0", "1.0,0.2,0.65", "1.0,0.45,0.08", "0.5,1.0,0.2" } },
            { "trim_magenta", new[] { "1.0,0.15,0.6", "0.1,0.9,1.0", "0.75,0.3,1.0" } },
        };
        static readonly string[] WalkClips = { "walk_slow", "walk", "walk", "walk", "walk_fast" };

        // ---------------------------------------------------------------- mini JSON (enough for the meta files)
        static int JsonSkip(string s, int i)
        { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; return i; }
        static object JsonParse(string s, ref int i)
        {
            i = JsonSkip(s, i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                i = JsonSkip(s, i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    i = JsonSkip(s, i);
                    var key = JsonString(s, ref i);
                    i = JsonSkip(s, i); i++; // colon
                    d[key] = JsonParse(s, ref i);
                    i = JsonSkip(s, i);
                    if (s[i] == ',') { i++; continue; }
                    i++; break; // }
                }
                return d;
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                i = JsonSkip(s, i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(JsonParse(s, ref i));
                    i = JsonSkip(s, i);
                    if (s[i] == ',') { i++; continue; }
                    i++; break; // ]
                }
                return l;
            }
            if (c == '"') return JsonString(s, ref i);
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            return double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
        }
        static string JsonString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new System.Text.StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; sb.Append(s[i] == 'n' ? '\n' : s[i]); i++; }
                else sb.Append(s[i++]);
            }
            i++;
            return sb.ToString();
        }
        static Dictionary<string, object> AsDict(object o) => o as Dictionary<string, object>;
        static List<object> AsList(object o) => o as List<object>;
        static double AsNum(object o) => o is double d ? d : 0;

        // ---------------------------------------------------------------- state
        class Meta
        {
            public string sex;
            public List<string> outfits = new List<string>();
            public Dictionary<string, double> clipPeriod = new Dictionary<string, double>();
            public Dictionary<string, Vector4> matColor = new Dictionary<string, Vector4>();
            public Dictionary<string, float> matRough = new Dictionary<string, float>();
            public Dictionary<string, string> matMap = new Dictionary<string, string>();
            public Dictionary<string, string> matNormal = new Dictionary<string, string>();
        }

        class Lane { public double ax, az, bx, bz, len, hw, density; public double dx, dz; }
        class Agent
        {
            public int id, lane;
            public string kind;
            public double s, dir, lat, x, z, y, yaw, phase, v, speedK;
            public int sex, outfit, tint;
            public float scale;
            public string clip;
            public string umbrella;
            public bool phone;
            public int slot = -1; // near pool slot
        }

        class CrowdNormScale : MonoBehaviour { public float scale = 1f; }

        class NearSlot
        {
            public GameObject go;
            public Animation anim;
            public Transform armature;
            public Renderer bodyR;
            public Renderer outfitR;
            public Transform umbrellaB, umbrellaC, umbrellaL, phoneT;
            public float normScale = 1f;
            public int sex;
            public Agent agent;
            public string curClip;
            public int curOutfit = -1;
        }

        static Meta[] _meta = new Meta[2]; // 0 male, 1 female
        static GameObject[] _prefab = new GameObject[2];
        static Material _matBody, _matGarment; // far pose shader materials (tint via MPB per draw)
        static Dictionary<string, Material> _nearMats = new Dictionary<string, Material>();
        static Dictionary<string, Material> _farBodyMats = new Dictionary<string, Material>();

        // far pose meshes [sex][outfit][pose] pose 0..3 walk, 4 idle_a, 5 idle_b
        static Mesh[,,] _poseMesh;
        static Dictionary<string, Material> _poseMat = new Dictionary<string, Material>(); // "sex:outfit:tintv" -> material

        static List<Agent> _agents = new List<Agent>();
        static List<Lane> _lanes = new List<Lane>();
        static NearSlot[] _slots;
        static Mulberry32 _rnd;
        static float _nextScan = -1f;

        const float NearDist = 34f;
        const float FarDist = 170f;
        const int MaxNear = 24;
        const string Dir = "Assets/Game/Models/Crowd";

        public static bool Built { get; private set; }

        // ---------------------------------------------------------------- build
        public static string Build(Transform parent)
        {
            var old = GameObject.Find("Crowd");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject("Crowd");
            root.transform.SetParent(parent, false);

            for (int sex = 0; sex < 2; sex++)
            {
                _meta[sex] = LoadMeta(sex);
                _prefab[sex] = UnityEngine.Resources.Load<UnityEngine.GameObject>($"Crowd/{(sex == 0 ? "male" : "female")}")
                    ?? UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{Dir}/dump/{(sex == 0 ? "male" : "female")}.fbx");
                if (_prefab[sex] == null) return $"crowd: prefab {(sex == 0 ? "male" : "female")} missing";
            }

            _matBody = new Material(Shader.Find("NEON KAIDO/Crowd"));
            _matGarment = new Material(Shader.Find("NEON KAIDO/Crowd"));

            _rnd = new Mulberry32(20771u);
            BuildLanes();
            SpawnAgents();

            // far pose baking
            _poseMesh = new Mesh[2, 12, 6];
            for (int sex = 0; sex < 2; sex++)
                for (int oi = 0; oi < _meta[sex].outfits.Count; oi++)
                    BakePoses(sex, oi);

            // near pool
            _slots = new NearSlot[MaxNear];
            for (int i = 0; i < MaxNear; i++)
            {
                _slots[i] = CreateNearSlot(root.transform, sex: i % 2);
                _slots[i].go.SetActive(false);
            }

            Built = true;
            int walkers = 0, idlers = 0;
            foreach (var a in _agents) { if (a.kind == "idle") idlers++; else walkers++; }
            return $"crowd: agents={_agents.Count} (walk {walkers}, idle {idlers}) poses=12x6 near={MaxNear}";
        }

        static Meta LoadMeta(int sex)
        {
            var txt = System.IO.File.ReadAllText($"{Dir}/dump/{(sex == 0 ? "male" : "female")}.json");
            int p = 0;
            var rootJ = AsDict(JsonParse(txt, ref p));
            var m = new Meta { sex = sex == 0 ? "male" : "female" };
            foreach (var k in AsDict(rootJ["outfits"]).Keys) m.outfits.Add(k);
            foreach (var kv in AsDict(rootJ["clips"]))
            {
                var cd = AsDict(kv.Value);
                m.clipPeriod[kv.Key] = AsNum(cd["period"]);
            }
            foreach (var kv in AsDict(rootJ["materials"]))
            {
                var md = AsDict(kv.Value);
                var col = AsList(md["color"]);
                m.matColor[kv.Key] = new Vector4((float)AsNum(col[0]), (float)AsNum(col[1]), (float)AsNum(col[2]), 1f);
                m.matRough[kv.Key] = (float)AsNum(md["roughness"]);
            }
            foreach (var kv in AsDict(rootJ["textures"]))
            {
                var td = AsDict(kv.Value);
                if (td.TryGetValue("map", out var map)) m.matMap[kv.Key] = TexPath((string)map);
                if (td.TryGetValue("normal", out var nrm)) m.matNormal[kv.Key] = TexPath((string)nrm);
            }
            return m;
        }

        static string TexPath(string jsPath)
        {
            var name = jsPath.Substring(jsPath.LastIndexOf('/') + 1);
            return $"{Dir}/tex/{name}";
        }

        static Material GetNearMat(Meta meta, string matName)
        {
            if (_nearMats.TryGetValue(matName, out var m)) return m;
            var shader = Shader.Find("NEON KAIDO/Crowd");
            var mat = new Material(shader);
            bool bodyLike = matName == "skin" || matName == "eyes" || matName == "brow" || matName == "hair" || matName == "lashes";
            meta.matColor.TryGetValue(matName, out var col);
            meta.matRough.TryGetValue(matName, out var rough);
            mat.SetColor("_Tint", bodyLike ? new Vector4(col.x, col.y, col.z, 1) : new Vector4(1, 1, 1, 1));
            mat.SetColor("_SkinColor", new Vector4(col.x, col.y, col.z, 1));
            mat.SetFloat("_UseMap", meta.matMap.ContainsKey(matName) ? 1f : 0f);
            mat.SetFloat("_Rough", rough);
            if (meta.matMap.TryGetValue(matName, out var map))
            {
                var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(map);
                if (t != null) { mat.SetTexture("_BaseMap", t); mat.SetTextureScale("_BaseMap", new Vector2(4, 4)); }
            }
            if (meta.matNormal.TryGetValue(matName, out var nrm))
            {
                var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(nrm);
                if (t != null) mat.SetTexture("_NormalMap", t);
            }
            mat.enableInstancing = true;
            _nearMats[matName] = mat;
            return mat;
        }

        // material for a far pose mesh: garment tint = palette pick
        static Material GetPoseMat(int sex, string outfit, int tintV)
        {
            var key = $"{sex}:{outfit}:{tintV}";
            if (_poseMat.TryGetValue(key, out var m)) return m;
            var meta = _meta[sex];
            var mat = new Material(Shader.Find("NEON KAIDO/Crowd"));
            // garment tint from palette (fallback: outfit meta color)
            Vector3 tint = new Vector3(1, 1, 1);
            var pk = PickPaletteKey(outfit, sex);
            if (pk != null)
            {
                var pal = Palette[pk];
                var pick = pal[(tintV * 7919 + 13) % pal.Length].Split(',');
                tint = new Vector3(float.Parse(pick[0]), float.Parse(pick[1]), float.Parse(pick[2]));
            }
            mat.SetColor("_Tint", new Vector4(tint.x, tint.y, tint.z, 1));
            var skin = meta.matColor.TryGetValue("skin", out var sc) ? sc : new Vector4(0.55f, 0.42f, 0.34f, 1);
            mat.SetColor("_SkinColor", skin);
            // first texture found among the outfit's garment mats
            foreach (var kv in meta.matMap)
            {
                if (kv.Key.StartsWith("cloth") || Garment.ContainsKey(kv.Key) || kv.Key.StartsWith("outfit"))
                {
                    var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(kv.Value);
                    if (t != null) { mat.SetTexture("_BaseMap", t); mat.SetTextureScale("_BaseMap", new Vector2(4, 4)); mat.SetFloat("_UseMap", 1f); }
                    break;
                }
            }
            mat.enableInstancing = true;
            _poseMat[key] = mat;
            return mat;
        }

        static string PickPaletteKey(string outfit, int sex)
        {
            foreach (var k in Palette.Keys)
                if (k.StartsWith("cloth_") && outfit.Contains(k.Substring(6))) return k;
            return null;
        }

        static GameObject MakeTemp(Transform parent, int sex)
        {
            var go = UnityEngine.Object.Instantiate(_prefab[sex], parent);
            go.name = "crowdTemp";
            // normalize height to ~1.7 m
            var bounds = BoundsOf(go);
            float h = bounds.size.y;
            if (h > 0.01f)
            {
                float f = 1.7f / h;
                go.transform.localScale = go.transform.localScale * f;
            }
            go.AddComponent<CrowdNormScale>().scale = go.transform.localScale.x;
            go.SetActive(true);
            return go;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                b.Encapsulate(r.bounds);
            return b;
        }

        // bake 4 walk + 2 idle poses: body+outfit merged, uv2.x = 0 body / 1 garment
        static void BakePoses(int sex, int oi)
        {
            var meta = _meta[sex];
            var outfit = meta.outfits[oi];
            var temp = MakeTemp(null, sex);
            var anim = temp.GetComponent<Animation>() ?? temp.AddComponent<Animation>();
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath($"{Dir}/dump/{(sex == 0 ? "male" : "female")}.fbx"))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__"))
                    clips[c.name.Substring(c.name.IndexOf('|') + 1)] = c;
            }
            foreach (var kv in clips) anim.AddClip(kv.Value, kv.Key);
            var bodyTr = temp.transform.Find("Icosphere");
            var outfitTr = temp.transform.Find("outfit_" + outfit);
            // disable all outfits except the chosen one; hide accessories
            foreach (Transform ch in temp.transform)
            {
                bool on = ch == bodyTr || ch == outfitTr || ch.name.StartsWith("Armature");
                ch.gameObject.SetActive(on);
            }
            var bodyMR = bodyTr.GetComponent<MeshRenderer>();
            var bodyMF = bodyTr.GetComponent<MeshFilter>();
            var outfitR = outfitTr.GetComponent<SkinnedMeshRenderer>();
            var baked = new[] { "walk", "walk", "walk", "walk", "idle", "idle_b" };
            var bakedT = new[] { 0.05f, 0.3f, 0.55f, 0.8f, 0.25f, 0.6f };
            for (int pose = 0; pose < 6; pose++)
            {
                var clipName = baked[pose];
                var state = anim[FindClipKey(clips, clipName)];
                state.enabled = true;
                state.weight = 1;
                state.time = bakedT[pose] * (float)meta.clipPeriod[clipName];
                anim.Sample();
                Mesh mb;
                if (bodyMR != null)
                {
                    // bone-attached static body: transform local verts by its animated world matrix
                    var src = bodyMF.sharedMesh;
                    var srcV = src.vertices;
                    var toRoot = temp.transform.worldToLocalMatrix * bodyTr.localToWorldMatrix;
                    var moved = new Vector3[srcV.Length];
                    for (int vi = 0; vi < srcV.Length; vi++) moved[vi] = toRoot.MultiplyPoint3x4(srcV[vi]);
                    mb = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mb.vertices = moved;
                    mb.normals = src.normals;
                    mb.uv = src.uv;
                    mb.triangles = src.triangles;
                }
                else
                {
                    mb = new Mesh();
                    bodyTr.GetComponent<SkinnedMeshRenderer>().BakeMesh(mb);
                }
                var mo = new Mesh();
                outfitR.BakeMesh(mo);
                _poseMesh[sex, oi, pose] = MergePoses(mb, mo);
            }
            UnityEngine.Object.DestroyImmediate(temp);
        }

        static string FindClipKey(Dictionary<string, AnimationClip> clips, string name)
        {
            foreach (var k in clips.Keys) if (k == name) return k;
            return clips.Keys.Count > 0 ? new List<string>(clips.Keys)[0] : null;
        }

        static Mesh MergePoses(Mesh body, Mesh outfit)
        {
            int nb = body.vertexCount, no = outfit.vertexCount;
            var verts = new Vector3[nb + no];
            var norms = new Vector3[nb + no];
            var uv0 = new Vector2[nb + no];
            var uv2 = new Vector2[nb + no];
            var tris = new int[(body.triangles.Length + outfit.triangles.Length)];
            Array.Copy(body.vertices, verts, nb);
            Array.Copy(outfit.vertices, 0, verts, nb, no);
            Array.Copy(body.normals, norms, nb);
            Array.Copy(outfit.normals, 0, norms, nb, no);
            for (int i = 0; i < nb + no; i++) { uv0[i] = new Vector2(0.5f, 0.5f); uv2[i] = new Vector2(i < nb ? 0f : 1f, 0); }
            var tb = body.triangles;
            var to = outfit.triangles;
            for (int i = 0; i < tb.Length; i++) tris[i] = tb[i];
            for (int i = 0; i < to.Length; i++) tris[tb.Length + i] = to[i] + nb;
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = verts;
            m.normals = norms;
            m.uv = uv0;
            m.uv2 = uv2;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- near pool
        static NearSlot CreateNearSlot(Transform parent, int sex)
        {
            var temp = MakeTemp(parent, sex);
            var slot = new NearSlot { go = temp, anim = temp.GetComponent<Animation>() ?? temp.AddComponent<Animation>(), sex = sex };
            var fbxPath = $"{Dir}/dump/{(sex == 0 ? "male" : "female")}.fbx";
            foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__"))
                {
                    var key = c.name.Substring(c.name.IndexOf('|') + 1);
                    slot.anim.AddClip(c, key);
                }
            }
            var bodyTr = temp.transform.Find("Icosphere");
            slot.bodyR = bodyTr.GetComponent<Renderer>();
            slot.armature = temp.transform.Find($"Armature_{(sex == 0 ? "male" : "female")}");
            slot.umbrellaB = temp.transform.Find("umbrella_black");
            slot.umbrellaC = temp.transform.Find("umbrella_clear");
            slot.umbrellaL = temp.transform.Find("umbrella_led");
            slot.phoneT = temp.transform.Find("phone");
            slot.normScale = temp.GetComponent<CrowdNormScale>().scale;
            UnityEngine.Object.DestroyImmediate(temp.GetComponent<CrowdNormScale>());
            slot.curOutfit = -1;
            return slot;
        }

        static void ConfigureSlot(NearSlot slot, Agent ag)
        {
            int sex = ag.sex;
            var meta = _meta[sex];
            // outfit switch: enable only the chosen outfit mesh, reassign materials
            if (slot.curOutfit != ag.outfit)
            {
                var outfitName = meta.outfits[ag.outfit];
                foreach (Transform ch in slot.go.transform)
                {
                    if (ch.name.StartsWith("outfit_"))
                        ch.gameObject.SetActive(ch.name == "outfit_" + outfitName);
                }
                var outfitTr = slot.go.transform.Find("outfit_" + outfitName);
                slot.outfitR = outfitTr.GetComponent<SkinnedMeshRenderer>();
                slot.curOutfit = ag.outfit;
            }
            // per-garment tinted materials (material instances per slot)
            if (slot.outfitR != null)
            {
                var mats = slot.outfitR.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var mn = mats[i].name.Replace("(Instance)", "").Trim();
                    var m = UnityEngine.Object.Instantiate(GetNearMat(meta, mn));
                    m.name = mn;
                    var tint = GarmentTint(mn, ag.tint + i);
                    m.SetColor("_Tint", new Vector4(tint.x, tint.y, tint.z, 1));
                    if (mn == "hair")
                    {
                        var hp = Palette["hair"];
                        var pick = hp[(ag.tint * 7 + 3) % hp.Length].Split(',');
                        m.SetColor("_Tint", new Vector4(float.Parse(pick[0]), float.Parse(pick[1]), float.Parse(pick[2]), 1));
                    }
                    mats[i] = m;
                }
                slot.outfitR.sharedMaterials = mats;
            }
            if (slot.bodyR != null)
            {
                var mats = slot.bodyR.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var mn = mats[i].name.Replace("(Instance)", "").Trim();
                    var m = UnityEngine.Object.Instantiate(GetNearMat(meta, mn));
                    m.name = mn;
                    if (mn == "hair")
                    {
                        var hp = Palette["hair"];
                        var pick = hp[(ag.tint * 7 + 3) % hp.Length].Split(',');
                        m.SetColor("_Tint", new Vector4(float.Parse(pick[0]), float.Parse(pick[1]), float.Parse(pick[2]), 1));
                    }
                    mats[i] = m;
                }
                slot.bodyR.sharedMaterials = mats;
            }
            // accessories
            if (slot.umbrellaB != null) slot.umbrellaB.gameObject.SetActive(ag.umbrella == "black");
            if (slot.umbrellaC != null) slot.umbrellaC.gameObject.SetActive(ag.umbrella == "clear");
            if (slot.umbrellaL != null) slot.umbrellaL.gameObject.SetActive(ag.umbrella == "led");
            if (slot.phoneT != null) slot.phoneT.gameObject.SetActive(ag.phone);
        }

        static Vector3 GarmentTint(string matName, int seed)
        {
            string pk = null;
            foreach (var k in Garment.Keys)
                if (matName.Contains(k)) { pk = k; break; }
            if (pk == null)
                foreach (var k in Palette.Keys)
                    if (matName.Contains(k)) { pk = k; break; }
            if (pk == null) return new Vector3(1, 1, 1);
            var src = Garment.ContainsKey(pk) ? Garment[pk] : Palette[pk];
            var pick = src[(seed * 31 + 7) % src.Length].Split(',');
            return new Vector3(float.Parse(pick[0]), float.Parse(pick[1]), float.Parse(pick[2]));
        }

        // ---------------------------------------------------------------- population (agents.js port)
        static void BuildLanes()
        {
            void add(double ax, double az, double bx, double bz, double hw, double density)
            {
                var len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                _lanes.Add(new Lane { ax = ax, az = az, bx = bx, bz = bz, len = len, hw = hw, density = density, dx = (bx - ax) / len, dz = (bz - az) / len });
            }
            double sx = 6 + 4.0 / 2; // 8
            // avenue sidewalks
            add(sx + 0.15, 60, sx + 0.15, -420, 4.0 / 2 - 0.55, 1 / 2.6);
            add(-sx - 0.15, 60, -sx - 0.15, -420, 4.0 / 2 - 0.55, 1 / 2.6);
            // cross streets
            double cz = 5 + 3.0 / 2;
            double xIn = 6 + 4 + 0.5;
            foreach (var z in new[] { -60, -180, -300 })
                foreach (var s in new[] { 1, -1 })
                {
                    add(-xIn, z + s * cz, -200, z + s * cz, 3.0 / 2 - 0.45, 1 / 9.0);
                    add(xIn, z + s * cz, 127, z + s * cz, 3.0 / 2 - 0.45, 1 / 9.0);
                }
            // promenade
            add(134 - 1.2, 40, 134 - 1.2, -500, 6 - 2.2, 1 / 4.5);
            // alley
            add(-11 - 1.5, -100, -90 + 4, -100, 4.5 / 2 - 0.7, 1 / 5.0);
        }

        static void SpawnAgents()
        {
            var r = _rnd;
            for (int li = 0; li < _lanes.Count; li++)
            {
                var ln = _lanes[li];
                int n = (int)(ln.len * ln.density);
                for (int i = 0; i < n; i++)
                {
                    var ag = new Agent
                    {
                        id = _agents.Count,
                        kind = "walk",
                        lane = li,
                        s = r.Next() * ln.len,
                        dir = r.Next() < 0.5 ? 1 : -1,
                    };
                    ag.lat = (ag.dir > 0 ? -0.35 : 0.35) * ln.hw + (r.Next() - 0.5) * ln.hw * 1.1;
                    ag.lat = Math.Max(-ln.hw, Math.Min(ln.hw, ag.lat));
                    Look(r, ag);
                    RainGear(r, ag);
                    ag.clip = ag.umbrella != null ? "umbrella_walk" : ag.phone ? "phone_walk" : WalkClips[(int)(r.Next() * WalkClips.Length)];
                    ag.speedK = 0.93 + r.Next() * 0.14;
                    PlaceOnLane(ag);
                    ag.yaw = Math.Atan2(ln.dx * ag.dir, ln.dz * ag.dir);
                    _agents.Add(ag);
                }
            }
            // idlers: fallback avenue frontage (facade spots deferred)
            for (double z = 60; z > -420; z -= 7 + r.Next() * 10)
            {
                foreach (var s in new[] { 1, -1 })
                {
                    if (r.Next() < 0.5) continue;
                    var ag = new Agent { id = _agents.Count, kind = "idle", x = s * (6 + 4 - 0.45), z = z + r.Next() * 3, yaw = 0 };
                    ag.yaw = (s > 0 ? -Math.PI / 2 : Math.PI / 2) + (r.Next() - 0.5);
                    ag.y += 0;
                    ag.phase = r.Next();
                    Look(r, ag);
                    RainGear(r, ag);
                    if (r.Next() < 0.25) ag.phone = ag.umbrella == null;
                    ag.clip = ag.umbrella != null ? "umbrella_idle" : ag.phone ? "phone_idle" : (r.Next() < 0.5 ? "idle" : "idle_b");
                    _agents.Add(ag);
                }
            }
        }

        static void Look(Mulberry32 r, Agent ag)
        {
            ag.sex = (int)(r.Next() * 2) % 2;
            ag.outfit = (int)(r.Next() * _meta[ag.sex].outfits.Count);
            ag.tint = (int)(r.Next() * 997);
            ag.scale = 0.95f + (float)r.Next() * 0.09f;
        }

        static void RainGear(Mulberry32 r, Agent ag)
        {
            var u = r.Next();
            if (u < 0.22) ag.umbrella = "black";
            else if (u < 0.36) ag.umbrella = "clear";
            else if (u < 0.45) ag.umbrella = "led";
            else ag.umbrella = null;
            ag.phone = ag.umbrella == null && r.Next() < 0.18;
        }

        static void PlaceOnLane(Agent ag)
        {
            var ln = _lanes[ag.lane];
            ag.x = ln.ax + ln.dx * ag.s + ln.dz * ag.lat;
            ag.z = ln.az + ln.dz * ag.s - ln.dx * ag.lat;
        }

        // ---------------------------------------------------------------- update
        static readonly Dictionary<long, List<Agent>> _grid = new Dictionary<long, List<Agent>>();

        public static void Update(float dt, float t, Vector3 playerPos)
        {
            if (GameObject.Find("Crowd") == null) Built = false; // play-mode scene reload destroyed the root
            if (!Built)
            {
                var root0 = new GameObject("Crowd");
                Build(root0.transform);
            }
            Simulate(dt);
            // ground heights only refresh near the player is unnecessary; compute lazily per agent
            UpdateNear(playerPos, dt);
            DrawFar(playerPos);
        }

        static void Simulate(float dt)
        {
            _grid.Clear();
            foreach (var ag in _agents)
            {
                long k = ((long)Math.Floor(ag.x / 2.0) + 4096) * 8192 + (long)Math.Floor(ag.z / 2.0) + 4096;
                if (!_grid.TryGetValue(k, out var list)) { list = new List<Agent>(); _grid[k] = list; }
                list.Add(ag);
            }
            foreach (var ag in _agents)
            {
                if (ag.kind == "idle")
                {
                    var per = _meta[ag.sex].clipPeriod.TryGetValue(ag.clip, out var pp) ? pp : 6.0;
                    ag.phase = (ag.phase + dt / per) % 1;
                    continue;
                }
                var ln = _lanes[ag.lane];
                double fx = ln.dx * ag.dir, fz = ln.dz * ag.dir;
                double baseSpeed = ag.clip == "walk_slow" ? 1.15 : ag.clip == "walk_fast" ? 1.9 : 1.5;
                double v = baseSpeed * ag.speedK;
                double steer = 0;
                long cx = (long)Math.Floor(ag.x / 2.0), cz2 = (long)Math.Floor(ag.z / 2.0);
                for (long i = -1; i <= 1; i++)
                    for (long j = -1; j <= 1; j++)
                    {
                        if (!_grid.TryGetValue((cx + i + 4096) * 8192 + (cz2 + j + 4096), out var cell)) continue;
                        foreach (var o in cell)
                        {
                            if (o == ag) continue;
                            double dx = o.x - ag.x, dz = o.z - ag.z;
                            double ahead = dx * fx + dz * fz;
                            if (ahead < -0.2 || ahead > 2.6) continue;
                            double side = dx * fz - dz * fx;
                            double rad = (ag.umbrella != null && o.umbrella != null) ? 1.05 : 0.62;
                            if (Math.Abs(side) > rad) continue;
                            bool sameDir = o.kind == "walk" && o.lane == ag.lane && o.dir == ag.dir;
                            double urgency = 1 - Math.Max(0, ahead) / 2.6;
                            steer += (side >= 0 ? -1 : 1) * urgency * 1.1;
                            if (ahead < 1.1)
                            {
                                if (sameDir && o.v > 0) v = Math.Min(v, o.v * 0.98);
                                else v *= 0.85;
                            }
                        }
                    }
                double pref = (ag.dir > 0 ? -0.35 : 0.35) * ln.hw;
                steer += (pref - ag.lat) * 0.12;
                double latVel = Math.Max(-0.6, Math.Min(0.6, steer)) * ag.dir * -1;
                ag.lat = Math.Max(-ln.hw, Math.Min(ln.hw, ag.lat + latVel * dt * 0.8));
                ag.v = v;
                ag.s += ag.dir * v * dt;
                if (ag.s < 0 || ag.s > ln.len)
                {
                    ag.dir *= -1;
                    ag.s = Math.Max(0, Math.Min(ln.len, ag.s));
                }
                double ox = ag.x, oz = ag.z;
                PlaceOnLane(ag);
                double mvx = (ag.x - ox) / Math.Max(dt, 1e-4), mvz = (ag.z - oz) / Math.Max(dt, 1e-4);
                double targetYaw = Math.Atan2(mvx * 0.5 + fx * v, mvz * 0.5 + fz * v);
                double dy = targetYaw - ag.yaw;
                dy = Math.Atan2(Math.Sin(dy), Math.Cos(dy));
                ag.yaw += dy * Math.Min(1, dt * 6);
                var per2 = _meta[ag.sex].clipPeriod.TryGetValue(ag.clip, out var p2) ? p2 : 1.1;
                double stride = baseSpeed * per2;
                ag.phase = (ag.phase + (v * dt) / stride) % 1;
            }
        }

        static float GroundY(double x, double zsim)
        {
            var h = GroundPlan.HeightAt(x, zsim);
            return double.IsFinite(h) ? (float)h : 0f;
        }

        static void UpdateNear(Vector3 playerPos, float dt)
        {
            // near set: closest walkers+idlers within NearDist
            nearBuf.Clear();
            foreach (var ag in _agents)
            {
                float dx = (float)ag.x - playerPos.x;
                float dz = (float)(-ag.z) - playerPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < NearDist * NearDist) nearBuf.Add((ag, d2));
            }
            nearBuf.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            var chosen = new HashSet<Agent>();
            int cap = Math.Min(MaxNear, nearBuf.Count);
            for (int i = 0; i < cap; i++) chosen.Add(nearBuf[i].Item1);
            // free slots not chosen
            foreach (var s in _slots)
                if (s.agent != null && !chosen.Contains(s.agent)) { s.go.SetActive(false); s.agent.slot = -1; s.agent = null; }
            // assign
            for (int i = 0; i < cap; i++)
            {
                var ag = nearBuf[i].Item1;
                if (ag.slot >= 0) continue;
                NearSlot free = null;
                foreach (var s in _slots) if (s.agent == null) { free = s; break; }
                if (free == null) break;
                if (free.sex != ag.sex) continue;
                free.agent = ag;
                ag.slot = Array.IndexOf(_slots, free);
                free.go.SetActive(true);
                ConfigureSlot(free, ag);
            }
            // place + animate
            foreach (var s in _slots)
            {
                if (s.agent == null) continue;
                var ag = s.agent;
                float uy = GroundY(ag.x, -ag.z);
                s.go.transform.position = new Vector3((float)ag.x, uy, (float)(-ag.z));
                s.go.transform.rotation = Quaternion.Euler(0, (float)(ag.yaw * 180.0 / Math.PI), 0);
                s.go.transform.localScale = Vector3.one * s.normScale;
                // manual phase: set clip time
                string key = ag.clip;
                if (s.curClip != key && s.anim[key] != null)
                {
                    foreach (AnimationState st in s.anim) st.enabled = false;
                    var st2 = s.anim[key];
                    st2.enabled = true;
                    st2.weight = 1;
                    st2.time = 0;
                    s.curClip = key;
                }
                var state = s.anim[key];
                if (state != null)
                {
                    var meta = _meta[ag.sex];
                    var per = meta.clipPeriod.TryGetValue(ag.clip, out var pp) ? pp : 1.1;
                    state.enabled = true;
                    state.weight = 1;
                    state.time = (float)(ag.phase * per);
                    s.anim.Sample();
                }
            }
        }

        static readonly List<(Agent, float)> nearBuf = new List<(Agent, float)>();
        static readonly Dictionary<string, List<Matrix4x4>> _farBuckets = new Dictionary<string, List<Matrix4x4>>();
        static readonly Dictionary<string, Material> _farMatFor = new Dictionary<string, Material>();
        static readonly Matrix4x4[] _mxBuf = new Matrix4x4[1023];

        static void DrawFar(Vector3 playerPos)
        {
            _farBuckets.Clear();
            foreach (var ag in _agents)
            {
                if (ag.slot >= 0) continue; // near
                float dx = (float)ag.x - playerPos.x;
                float dz = (float)(-ag.z) - playerPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > FarDist * FarDist) continue;
                int pose = ag.kind == "idle" ? (ag.clip == "idle_b" ? 5 : 4) : Math.Min(3, (int)(ag.phase * 4));
                var key = $"{ag.sex}:{ag.outfit}:{pose}:{ag.tint % 2}";
                if (!_farBuckets.TryGetValue(key, out var list)) { list = new List<Matrix4x4>(); _farBuckets[key] = list; }
                float uy = GroundY(ag.x, -ag.z);
                var rot = Quaternion.Euler(0, (float)(ag.yaw * 180.0 / Math.PI), 0);
                var mx = Matrix4x4.TRS(new Vector3((float)ag.x, uy, (float)(-ag.z)), rot, new Vector3(ag.scale, ag.scale, ag.scale));
                list.Add(mx);
            }
            foreach (var kv in _farBuckets)
            {
                var parts = kv.Key.Split(':');
                int sex = int.Parse(parts[0]), outfit = int.Parse(parts[1]), pose = int.Parse(parts[2]), tv = int.Parse(parts[3]);
                var mesh = _poseMesh[sex, outfit, pose];
                if (mesh == null) continue;
                var mk = $"{sex}:{outfit}:{tv}";
                if (!_farMatFor.TryGetValue(mk, out var mat))
                {
                    mat = new Material(Shader.Find("NEON KAIDO/Crowd"));
                    var src = GetPoseMat(sex, _meta[sex].outfits[outfit], tv);
                    mat.CopyPropertiesFromMaterial(src);
                    mat.enableInstancing = true;
                    _farMatFor[mk] = mat;
                }
                var list = kv.Value;
                for (int i = 0; i < list.Count; i += 1023)
                {
                    int n = Math.Min(1023, list.Count - i);
                    for (int j = 0; j < n; j++) _mxBuf[j] = list[i + j];
                    Graphics.DrawMeshInstanced(mesh, 0, mat, _mxBuf, n, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                }
            }
        }
    }
}
