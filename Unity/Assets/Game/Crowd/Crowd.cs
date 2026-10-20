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
            public Dictionary<string, float> outfitSole = new Dictionary<string, float>();
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

        /// Baked idler rows (11 floats each). Lives on its own object so a Crowd rebuild does not
        /// delete it: the facade cache that produces the rows only exists in the session that ran
        /// the buildings build, so without this every later load would fall back to the old
        /// avenue-frontage sprinkle and lose the shopfront clusters the browser shows.
        public class CrowdSpawns : MonoBehaviour { public float[] idlers; }

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

        static Shader _shader;
        /// Shader.Find can return null while a scene is still loading (the crowd preview builds
        /// on the first editor tick after open_scene). A material made from a null shader renders
        /// magenta forever, so every create site goes through this and callers bail out instead.
        static Shader CrowdShader
        {
            get
            {
                if (_shader == null) _shader = Shader.Find("NEON KAIDO/Crowd");
                return _shader;
            }
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
        static float[] _idleRows;
        static GameObject _root;

        const float NearDist = 34f;
        const float FarDist = 170f;
        const int MaxNear = 24;
        const string Dir = "Assets/Game/Models/Crowd";

        /// The root is the truth: a scene load destroys the HideAndDontSave crowd while the
        /// statics survive, so a cached bool would claim "built" with dead slots.
        public static bool Built => _root != null;

        // ---------------------------------------------------------------- build
        public static string Build(Transform parent)
        {
            if (CrowdShader == null) return "crowd: shader not loaded yet";
            // runtime materials and baked meshes do not survive a scene load; a cached reference
            // to a destroyed one instantiates to null (magenta or invisible slots)
            _nearMats.Clear(); _poseMat.Clear(); _farMatFor.Clear();
            _poseMesh = null; _canopyMesh = null; _canopyMats = null;
            DestroyStaleRoots();
            var root = new GameObject("Crowd");
            root.transform.SetParent(parent, false);
            // Runtime-only: the agents are rebuilt from the seed on every entry to Play, so a
            // serialized copy in Main.unity is 24 frozen characters that cost load time and get
            // deleted on the first frame anyway. The baked idler table is the only part that
            // needs to persist (see CrowdSpawns).
            root.hideFlags = HideFlags.HideAndDontSave;
            _root = root;

            for (int sex = 0; sex < 2; sex++)
            {
                _meta[sex] = LoadMeta(sex);
                _prefab[sex] = UnityEngine.Resources.Load<UnityEngine.GameObject>($"Crowd/{(sex == 0 ? "male" : "female")}")
                    ?? UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{Dir}/dump/{(sex == 0 ? "male" : "female")}.fbx");
                if (_prefab[sex] == null) return $"crowd: prefab {(sex == 0 ? "male" : "female")} missing";
            }

            _matBody = new Material(CrowdShader) { hideFlags = HideFlags.DontSave };
            _matGarment = new Material(CrowdShader) { hideFlags = HideFlags.DontSave };
            BakeCanopies();

            _rnd = new Mulberry32(20771u);
            _agents.Clear();
            _lanes.Clear();
            _idleRows = LoadBakedIdlers();
            BuildLanes();
            SpawnAgents();
            if (_idleRows != null && FacadeAPI.Collect().Count > 0) StoreBakedIdlers(_idleRows, root.transform.parent);

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

            int walkers = 0, idlers = 0;
            foreach (var a in _agents) { if (a.kind == "idle") idlers++; else walkers++; }
            return $"crowd: agents={_agents.Count} (walk {walkers}, idle {idlers}) poses=12x6 near={MaxNear}";
        }

        /// Every recompile/play session leaves its HideAndDontSave crowd behind (the objects are
        /// in an internal scene, so GameObject.Find returns one of them and FindObjectsByType
        /// returns none). Undestroyed they pile up — eight roots, 192 characters, only one of them
        /// positioned — so the sweep has to use Resources.FindObjectsOfTypeAll and must never
        /// touch anything without the DontSave flag.
        static void DestroyStaleRoots()
        {
            _root = null;
            foreach (var g in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>())
            {
                if (g == null || g.name != "Crowd") continue;
                if ((g.hideFlags & HideFlags.DontSave) == 0) continue;
                UnityEngine.Object.DestroyImmediate(g);
            }
        }

        static float[] LoadBakedIdlers()
        {
            var go = GameObject.Find("CrowdSpawns");
            if (go == null)
            {
                var all = UnityEngine.Object.FindObjectsByType<CrowdSpawns>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (all.Length > 0) go = all[0].gameObject;
            }
            var c = go != null ? go.GetComponent<CrowdSpawns>() : null;
            return c != null && c.idlers != null && c.idlers.Length > 0 ? c.idlers : null;
        }

        static void StoreBakedIdlers(float[] rows, Transform parent)
        {
            var go = GameObject.Find("CrowdSpawns");
            if (go == null)
            {
                go = new GameObject("CrowdSpawns");
                if (parent != null) go.transform.SetParent(parent, false);
            }
            var c = go.GetComponent<CrowdSpawns>();
            if (c == null) c = go.AddComponent<CrowdSpawns>();
            c.idlers = rows;
        }

        static Meta LoadMeta(int sex)
        {
            var txt = System.IO.File.ReadAllText($"{Dir}/dump/{(sex == 0 ? "male" : "female")}.json");
            int p = 0;
            var rootJ = AsDict(JsonParse(txt, ref p));
            var m = new Meta { sex = sex == 0 ? "male" : "female" };
            var outfits = AsDict(rootJ["outfits"]);
            foreach (var k in outfits.Keys)
            {
                m.outfits.Add(k);
                // characters.js lifts each outfit by meta.outfits[o].sole so the soles, not the
                // origin, sit on the pavement.
                var od = AsDict(outfits[k]);
                m.outfitSole[k] = od != null && od.TryGetValue("sole", out var sole) ? (float)AsNum(sole) : 0f;
            }
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
            var shader = CrowdShader;
            if (shader == null) return null;
            var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
            bool bodyLike = matName == "skin" || matName == "eyes" || matName == "brow" || matName == "hair" || matName == "lashes";
            meta.matColor.TryGetValue(matName, out var col);
            meta.matRough.TryGetValue(matName, out var rough);
            // characters.js builds umb_clear from a hard-coded 0xdfe8f0 (translucent plastic) and
            // ignores the meta colour; without this the clear umbrella renders as tan leather.
            if (matName == "umb_clear") col = new Vector4(0.874f, 0.910f, 0.941f, 1f);
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
            if (CrowdShader == null) return null;
            var mat = new Material(CrowdShader) { hideFlags = HideFlags.DontSave };
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
            // The FBX carries a 100x armature scale that the meshes are authored against, so a
            // SkinnedMeshRenderer reports bind-pose bounds of ~197 m for a 1.7 m character. This
            // used to normalise to 1.7 m from those bounds, which shrank every NPC to 1.5 cm —
            // alive, walking, and completely invisible. The skeleton itself is human scale (bones
            // span -0.02..1.57 m, the meta's sole offsets are 25 mm), and the browser uses the
            // model as authored, so nothing is scaled here. CrowdNormScale stays as the one place
            // a future re-scale would go.
            go.AddComponent<CrowdNormScale>().scale = 1f;
            // characters.js makeTemplate() keeps only the outfit and the accessories; the GLB's
            // "Icosphere" is an unused placeholder, and left visible it renders as a 2.5 m
            // skin-coloured sphere around the character's waist.
            var ico = go.transform.Find("Icosphere");
            if (ico != null) ico.gameObject.SetActive(false);
            go.SetActive(true);
            return go;
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
            var outfitTr = temp.transform.Find("outfit_" + outfit);
            // keep the chosen outfit (and the skeleton); the Icosphere placeholder and every
            // accessory stay off — the far LOD is the outfit alone, like the browser's clone
            foreach (Transform ch in temp.transform)
            {
                bool on = ch == outfitTr || ch.name.StartsWith("Armature");
                ch.gameObject.SetActive(on);
            }
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
                var mo = new Mesh();
                outfitR.BakeMesh(mo);
                // BakeMesh is in the renderer's local space, which misses the outfit node's 270 deg
                // X rotation and 100x scale: untransformed the far crowd is baked lying on its
                // back, 1.8 m long down Z. Take it to the character's space, then divide out the
                // node scale the skinning already carries.
                var l2w = outfitR.transform.localToWorldMatrix;
                float inv = 1f / Mathf.Max(1e-4f, outfitR.transform.lossyScale.x);
                var mtx = temp.transform.worldToLocalMatrix * l2w;
                var vs = mo.vertices;
                var ns = mo.normals;
                for (int vi = 0; vi < vs.Length; vi++)
                {
                    vs[vi] = mtx.MultiplyPoint3x4(vs[vi]) * inv;
                    ns[vi] = mtx.MultiplyVector(ns[vi]).normalized;
                }
                mo.vertices = vs;
                mo.normals = ns;
                _poseMesh[sex, oi, pose] = GarmentOnly(mo);
            }
            UnityEngine.Object.DestroyImmediate(temp);
        }

        static string FindClipKey(Dictionary<string, AnimationClip> clips, string name)
        {
            foreach (var k in clips.Keys) if (k == name) return k;
            return clips.Keys.Count > 0 ? new List<string>(clips.Keys)[0] : null;
        }

        /// Far pose mesh from a baked outfit: flag every vertex as garment (uv2.x = 1) so the
        /// crowd shader tints it with the palette colour rather than the skin colour.
        static Mesh GarmentOnly(Mesh outfit)
        {
            int n = outfit.vertexCount;
            var uv0 = new Vector2[n];
            var uv2 = new Vector2[n];
            for (int i = 0; i < n; i++) { uv0[i] = new Vector2(0.5f, 0.5f); uv2[i] = new Vector2(1f, 0); }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = outfit.vertices;
            m.normals = outfit.normals;
            m.uv = uv0;
            m.uv2 = uv2;
            m.triangles = outfit.triangles;
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- umbrella canopies (far LOD)
        // Near agents use the FBX umbrella nodes as they are. The far pose meshes are baked from
        // one outfit and carry no accessories at all, and in the rain that is most of the street's
        // silhouette — so each canopy is baked once into a rigid, correctly sized prop and drawn
        // instanced at 1.72 m over every far agent that has an umbrella (vat.js does the same with
        // a hand matrix; the canopy does not need to sway this far away).
        static readonly string[] UmbrellaNames = { "umbrella_black", "umbrella_clear", "umbrella_led" };
        static Mesh[] _canopyMesh;                 // per kind, shared by both sexes
        static Material[,][] _canopyMats;          // [sex, kind] -> submesh materials
        const float CanopyWidth = 1.15f;           // canopy diameter, metres
        const float CanopyY = 1.72f;               // canopy centre above the soles

        static void BakeCanopies()
        {
            _canopyMesh = new Mesh[3];
            _canopyMats = new Material[2, 3][];
            var temp = MakeTemp(null, 0);
            var toRoot = temp.transform.worldToLocalMatrix;
            for (int k = 0; k < 3; k++)
            {
                var tr = temp.transform.Find(UmbrellaNames[k]);
                var smr = tr != null ? tr.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr == null) continue;
                var baked = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                smr.BakeMesh(baked);
                // BakeMesh is in the renderer's local space, which does not include the node's
                // rotation; take it to the character's space before measuring it.
                var l2w = tr.localToWorldMatrix;
                var verts = baked.vertices;
                var norms = baked.normals;
                var b = new Bounds();
                bool first = true;
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = toRoot.MultiplyPoint3x4(l2w.MultiplyPoint3x4(verts[i]));
                    verts[i] = w;
                    norms[i] = (toRoot.MultiplyVector(l2w.MultiplyVector(norms[i]))).normalized;
                    if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                }
                float wid = Mathf.Max(b.size.x, b.size.z);
                float f = wid > 1e-4f ? CanopyWidth / wid : 1f;
                for (int i = 0; i < verts.Length; i++)
                    verts[i] = (verts[i] - b.center) * f;
                baked.vertices = verts;
                baked.normals = norms;
                baked.RecalculateBounds();
                _canopyMesh[k] = baked;
                var mats = new Material[smr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var mn = smr.sharedMaterials[i] != null ? smr.sharedMaterials[i].name.Replace("(Instance)", "").Trim() : "";
                    mats[i] = mn.Length > 0 ? GetNearMat(_meta[0], mn) : null;
                }
                _canopyMats[0, k] = mats;
                var matsF = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var mn = smr.sharedMaterials[i] != null ? smr.sharedMaterials[i].name.Replace("(Instance)", "").Trim() : "";
                    matsF[i] = mn.Length > 0 ? GetNearMat(_meta[1], mn) : null;
                }
                _canopyMats[1, k] = matsF;
            }
            UnityEngine.Object.DestroyImmediate(temp);
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
                    var src0 = GetNearMat(meta, mn);
                    if (src0 == null) continue;
                    var m = UnityEngine.Object.Instantiate(src0);
                    // Instantiate does not carry hideFlags: without this the per-slot material is
                    // destroyed by the next scene load and every NPC loses its clothing.
                    m.hideFlags = HideFlags.DontSave;
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
                    var src0 = GetNearMat(meta, mn);
                    if (src0 == null) continue;
                    var m = UnityEngine.Object.Instantiate(src0);
                    // Instantiate does not carry hideFlags: without this the per-slot material is
                    // destroyed by the next scene load and every NPC loses its clothing.
                    m.hideFlags = HideFlags.DontSave;
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
            // idlers: under shopfront awnings (agents.js spawnIdlers). Needs the facade cache,
            // which only exists in a session that ran the buildings build — so the list is baked
            // into the scene (CrowdSpawns) and replayed on every later load.
            var spots = new List<SFacade>();
            var facades = FacadeAPI.Collect();
            foreach (var f in facades)
                if (f.Shopfront && f.District != "river") spots.Add(f);
            if (spots.Count > 0) SpawnIdlersAt(spots);
            else if (!ReplayIdlers()) SpawnIdlersFallback();
        }

        // One idler: x/z in sim space, yaw/turn in radians. Packed 12 floats per record:
        // x, z, yaw, phase, sex, outfit, tint, scale, gaits(0 none,1 black,2 clear,3 led), phone, clip, pad
        static readonly string[] IdleClips = { "idle", "idle_b", "phone_idle", "umbrella_idle" };

        static void SpawnIdlersAt(List<SFacade> spots)
        {
            var r = _rnd;
            var rows = new List<float>();
            foreach (var f in spots)
            {
                if (r.Next() > 0.55) continue;
                int n = r.Next() < 0.3 ? 2 : 1;
                // facade vectors are Unity space; sd = sim mirror
                var cs = f.CS(); var ns = f.NS(); var rs = f.RS();
                double off = (r.Next() - 0.5) * Math.Min(f.Width, 10) * 0.7;
                double d = 0.55 + r.Next() * 0.5;
                double x = cs.x + ns.x * d + rs.x * off, z = cs.z + ns.z * d + rs.z * off;
                double outYaw = Math.Atan2(ns.x, ns.z);
                if (n == 2)
                {
                    // two people chatting: face each other
                    double yaw = outYaw + Math.PI / 2 + (r.Next() - 0.5) * 0.4;
                    double fx = Math.Sin(yaw), fz = Math.Cos(yaw);
                    AddIdler(x - fx * 0.5, z - fz * 0.5, yaw, rows);
                    AddIdler(x + fx * 0.5, z + fz * 0.5, yaw + Math.PI, rows);
                }
                else
                {
                    bool lookIn = r.Next() < 0.3; // window shopping
                    AddIdler(x, z, (lookIn ? outYaw + Math.PI : outYaw) + (r.Next() - 0.5) * 0.8, rows);
                }
            }
            _idleRows = rows.ToArray();
        }

        static void AddIdler(double x, double z, double yaw, List<float> rows)
        {
            var r = _rnd;
            var ag = new Agent { id = _agents.Count, kind = "idle", x = x, z = z, yaw = yaw };
            Look(r, ag);
            ag.phase = r.Next();
            RainGear(r, ag);
            if (r.Next() < 0.25) ag.phone = ag.umbrella == null;
            ag.clip = ag.umbrella != null ? "umbrella_idle" : ag.phone ? "phone_idle" : (r.Next() < 0.5 ? "idle" : "idle_b");
            _agents.Add(ag);
            rows.Add((float)x); rows.Add((float)z); rows.Add((float)yaw); rows.Add((float)ag.phase);
            rows.Add(ag.sex); rows.Add(ag.outfit); rows.Add(ag.tint); rows.Add(ag.scale);
            rows.Add(ag.umbrella == "black" ? 1 : ag.umbrella == "clear" ? 2 : ag.umbrella == "led" ? 3 : 0);
            rows.Add(ag.phone ? 1 : 0);
            rows.Add(Array.IndexOf(IdleClips, ag.clip));
        }

        // runtime path: the facade cache is empty, so restore the baked idlers
        static bool ReplayIdlers()
        {
            var rows = _idleRows;
            if (rows == null || rows.Length < 11) return false;
            for (int i = 0; i + 10 < rows.Length; i += 11)
            {
                var ag = new Agent
                {
                    id = _agents.Count, kind = "idle",
                    x = rows[i], z = rows[i + 1], yaw = rows[i + 2], phase = rows[i + 3],
                    sex = (int)rows[i + 4], outfit = (int)rows[i + 5], tint = (int)rows[i + 6],
                    scale = rows[i + 7],
                    umbrella = rows[i + 8] == 1 ? "black" : rows[i + 8] == 2 ? "clear" : rows[i + 8] == 3 ? "led" : null,
                    phone = rows[i + 9] > 0.5f,
                };
                int ci = (int)rows[i + 10];
                ag.clip = ci >= 0 && ci < IdleClips.Length ? IdleClips[ci] : "idle";
                _agents.Add(ag);
            }
            return true;
        }

        static void SpawnIdlersFallback()
        {
            var r = _rnd;
            // no facade data at all: sprinkle the avenue frontage (old behaviour)
            for (double z = 60; z > -420; z -= 7 + r.Next() * 10)
            {
                foreach (var s in new[] { 1, -1 })
                {
                    if (r.Next() < 0.5) continue;
                    var ag = new Agent { id = _agents.Count, kind = "idle", x = s * (6 + 4 - 0.45), z = z + r.Next() * 3, yaw = 0 };
                    ag.yaw = (s > 0 ? -Math.PI / 2 : Math.PI / 2) + (r.Next() - 0.5);
                    Look(r, ag);
                    ag.phase = r.Next();
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
            // A play-mode scene load destroys the root but leaves the statics; a Find here every
            // frame walked the whole 1 GB scene graph, so test the reference instead.
            if (_root == null) _slots = null;
            if (!Built) Build(null);
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
                // player avoidance (Unity add-on): steer around and yield to the walking player
                var pc = NeonKaido.PlayerController.I;
                if (pc != null && pc.WalkMode)
                {
                    var ppos = pc.transform.position;
                    double pdx = ppos.x - ag.x, pdz = (-ppos.z) - ag.z;
                    double pahead = pdx * fx + pdz * fz;
                    if (pahead > -0.3 && pahead < 2.6 && pdx * pdx + pdz * pdz < 7.3)
                    {
                        double pside = pdx * fz - pdz * fx;
                        double purg = 1 - Math.Max(0, pahead) / 2.6;
                        steer += (pside >= 0 ? -1 : 1) * purg * 1.35;
                        if (pahead < 1.2) v *= 0.7;
                    }
                }
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
            if (_slots == null) return;
            // near set: closest walkers+idlers within NearDist, behind-camera agents excluded
            // (index.js culls fd < -0.25 past 4 m before ranking, otherwise the skinned pool is
            // spent on the crowd standing behind the player). Already-skinned agents get 4 m of
            // hysteresis so someone walking the boundary cannot swap LOD every frame.
            var cam = UnityEngine.Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            float fwdLen = Mathf.Sqrt(fwd.x * fwd.x + fwd.z * fwd.z);
            if (fwdLen < 1e-3f) fwdLen = 1e-3f;
            nearBuf.Clear();
            foreach (var ag in _agents)
            {
                float dx = (float)ag.x - playerPos.x;
                float dz = (float)(-ag.z) - playerPos.z;
                float d2 = dx * dx + dz * dz;
                float reach = ag.slot >= 0 ? NearDist + 4f : NearDist;
                if (d2 > reach * reach) continue;
                float d = Mathf.Sqrt(d2);
                if (d > 4f)
                {
                    float fd = (dx * fwd.x + dz * fwd.z) / (d * fwdLen);
                    if (fd < -0.25f) continue;
                }
                nearBuf.Add((ag, d2));
            }
            nearBuf.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            var chosen = new HashSet<Agent>();
            int cap = Math.Min(MaxNear, nearBuf.Count);
            for (int i = 0; i < cap; i++) chosen.Add(nearBuf[i].Item1);
            // free slots not chosen
            foreach (var s in _slots)
                if (s.agent != null && !chosen.Contains(s.agent)) { s.go.SetActive(false); s.agent.slot = -1; s.agent = null; }
            // assign (a slot only ever takes its own sex — continue searched the agent list here,
            // so a mismatched head of the queue used to starve the whole pool)
            for (int i = 0; i < cap; i++)
            {
                var ag = nearBuf[i].Item1;
                if (ag.slot >= 0) continue;
                NearSlot free = null;
                for (int si = 0; si < _slots.Length; si++)
                    if (_slots[si].agent == null && _slots[si].sex == ag.sex) { free = _slots[si]; break; }
                if (free == null) continue;
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
                float sole = _meta[ag.sex].outfitSole.TryGetValue(_meta[ag.sex].outfits[ag.outfit], out var sl) ? sl : 0f;
                float uy = GroundY(ag.x, ag.z) + sole * ag.scale;
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
        static readonly List<Matrix4x4>[] _umbBuckets = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        static readonly Dictionary<string, Material> _farMatFor = new Dictionary<string, Material>();
        static readonly Matrix4x4[] _mxBuf = new Matrix4x4[1023];

        static void DrawFar(Vector3 playerPos)
        {
            _farBuckets.Clear();
            foreach (var l in _umbBuckets) l.Clear();
            foreach (var ag in _agents)
            {
                if (ag.slot >= 0) continue; // near
                float dx = (float)ag.x - playerPos.x;
                float dz = (float)(-ag.z) - playerPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > FarDist * FarDist) continue;
                int pose = ag.kind == "idle" ? (ag.clip == "idle_b" ? 5 : 4) : Math.Min(3, (int)(ag.phase * 4));
                // rain gear at distance: the pose meshes carry no accessories, so umbrellas are
                // instanced separately from the baked canopy (as vat.js does with hand matrices)
                if (ag.umbrella != null)
                {
                    int uk = ag.umbrella == "black" ? 0 : ag.umbrella == "clear" ? 1 : 2;
                    float uy2 = GroundY(ag.x, ag.z) + CanopyY * ag.scale;
                    _umbBuckets[uk].Add(Matrix4x4.TRS(new Vector3((float)ag.x, uy2, (float)(-ag.z)),
                        Quaternion.Euler(0, (float)(ag.yaw * 180.0 / Math.PI), 0), new Vector3(ag.scale, ag.scale, ag.scale)));
                }
                var key = $"{ag.sex}:{ag.outfit}:{pose}:{ag.tint % 2}";
                if (!_farBuckets.TryGetValue(key, out var list)) { list = new List<Matrix4x4>(); _farBuckets[key] = list; }
                float sole = _meta[ag.sex].outfitSole.TryGetValue(_meta[ag.sex].outfits[ag.outfit], out var sl) ? sl : 0f;
                float uy = GroundY(ag.x, ag.z) + sole * ag.scale;
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
                    mat = new Material(CrowdShader) { hideFlags = HideFlags.DontSave };
                    var src = GetPoseMat(sex, _meta[sex].outfits[outfit], tv);
                    if (src != null) mat.CopyPropertiesFromMaterial(src);
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
            DrawFarUmbrellas();
        }

        static void DrawFarUmbrellas()
        {
            if (_canopyMesh == null) return;
            for (int k = 0; k < 3; k++)
            {
                var list = _umbBuckets[k];
                var mesh = _canopyMesh[k];
                if (list.Count == 0 || mesh == null) continue;
                var mats = _canopyMats[0, k];
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var mat = mats != null && sub < mats.Length ? mats[sub] : null;
                    if (mat == null) continue;
                    for (int i = 0; i < list.Count; i += 1023)
                    {
                        int n = Math.Min(1023, list.Count - i);
                        for (int j = 0; j < n; j++) _mxBuf[j] = list[i + j];
                        Graphics.DrawMeshInstanced(mesh, sub, mat, _mxBuf, n, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- editor preview
        // The sim is driven from PlayerController.Update, which the Editor never runs, so outside
        // Play mode the crowd was simply absent — no agents, no far instances, nothing. This is
        // the same treatment the cars get: build once, place the skinned pool around the camera,
        // and issue the far instancing draws. Everything is HideAndDontSave, so Main.unity never
        // gains a frozen crowd (the only thing the scene keeps is the baked idler table).
        static bool _previewHooked;

        /// Drop the preview hierarchy. A scene load destroys the runtime materials but can leave
        /// the HideAndDontSave root itself alive, so the slots would keep dead material refs.
        public static void ResetPreview()
        {
            DestroyStaleRoots();
            _slots = null;
        }

        public static void PreviewVisuals(Vector3 camPos)
        {
            if (_root == null) _slots = null;
            if (!Built) Build(null);
            if (!Built) return;
            if (!_previewHooked)
            {
                _previewHooked = true;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += (ctx, camera) =>
                {
                    // One Graphics.DrawMeshInstanced call survives exactly one frame, so the far
                    // LOD has to be re-issued inside every camera render — an editor Tick would
                    // leave the distance empty by the time anything is captured.
                    if (Application.isPlaying || !Built) return;
                    DrawFar(camera.transform.position);
                };
            }
            Simulate(0f);
            UpdateNear(camPos, 0f);
            DrawFar(camPos);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void DropEditorPreview()
        {
            // HideAndDontSave objects outlive the edit-mode scene load and would freeze a second
            // crowd under the live one; the sim rebuilds from scratch anyway.
            _slots = null;
            DestroyStaleRoots();
        }
    }
}
