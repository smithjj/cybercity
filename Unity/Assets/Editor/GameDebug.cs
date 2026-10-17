// NEON KAIDO port — Editor-only driver. Tooling how-to: ../../Unity/EDITOR-CLI.md
// World-facing calls take SIM coordinates: sim (x,y,z) == Unity (x,y,-z).
// Yaw convention (from JS): forward_sim = (-sin yaw, -cos yaw) in (x,z); yaw 0 = north = -Z.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace NeonKaido.EditorTools
{
    public static class GameDebug
    {
        public static Vector3 ToU(float x, float y, float z) => new Vector3(x, y, -z);

        // ============================ setup ============================

        // One-time project setup (idempotent): HDRP asset, DX12, Main scene + camera.
        // Run:  tools/ev.sh 'D.Setup();'
        public static string Setup()
        {
            var sb = new StringBuilder();
            // HDRP requires linear color space (hand-rolled projects default to gamma).
            // Switching triggers a one-time reimport — may blow the 5s eval budget; it
            // completes on the main thread anyway. Re-runs are cheap.
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                sb.AppendLine("colorSpace -> Linear (reimport in progress, re-run Setup)");
            }
            const string rpPath = "Assets/Settings/HDRenderPipelineAsset.asset";
            if (!File.Exists(rpPath))
            {
                Directory.CreateDirectory("Assets/Settings");
                string tmpl = FindRpTemplate();
                if (tmpl != null) File.Copy(tmpl, rpPath, true);
                else AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<HDRenderPipelineAsset>(), rpPath);
                sb.AppendLine("created HDRP asset");
            }
            var rp = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(rpPath);
            if (rp == null) return "Setup FAILED: no HDRenderPipelineAsset at " + rpPath;
            if (GraphicsSettings.defaultRenderPipeline != rp)
            {
                GraphicsSettings.defaultRenderPipeline = rp;
                sb.AppendLine("set HDRP as default render pipeline");
            }
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows);
            if (apis.Length == 0 || apis[0] != GraphicsDeviceType.Direct3D12)
            {
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows, new[] { GraphicsDeviceType.Direct3D12 });
                sb.AppendLine("set DX12 for StandaloneWindows");
            }
            const string scenePath = "Assets/Main.unity";
            if (!File.Exists(scenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var cam = Camera.main;
                if (cam == null)
                {
                    var go = new GameObject("Main Camera");
                    go.tag = "MainCamera";
                    cam = go.AddComponent<Camera>();
                    go.AddComponent<AudioListener>();
                }
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 4000f;
                cam.transform.SetPositionAndRotation(ToU(3, 1.7f, 20), Quaternion.Euler(0, 180, 0)); // spawn, facing north
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.localScale = new Vector3(200, 1, 200);
                var mat = new Material(Shader.Find("Standard")) { color = new Color(0.12f, 0.12f, 0.14f) };
                ground.GetComponent<MeshRenderer>().sharedMaterial = mat;
                var ok = EditorSceneManager.SaveScene(scene, scenePath);
                sb.AppendLine(ok ? "created Main.unity" : "SAVE FAILED: Main.unity");
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return sb.Length == 0 ? "setup: already up to date" : sb.ToString();
        }

        static string FindRpTemplate()
        {
            string[] found = Directory.GetFiles("Packages/com.unity.render-pipelines.high-definition",
                "HDRenderPipelineAsset.asset", SearchOption.AllDirectories);
            return found.Length > 0 ? found[0] : null;
        }

        // ============================ camera / shots ============================

        // Pose the scene's Main Camera in sim coordinates. pitch>0 looks down.
        // ---------- walk mode (edit-mode spectator: mouse look + WASD) ----------
        // Robust across domain reloads: desired state kept in SessionState; GameDebug's static
        // ctor re-arms the tick + cursor after any recompile (via delayCall).
        const string WalkKey = "NKWalkActive";
        static bool _walk;
        static float _yaw, _pitch;
        static Vector3 _lastMouse;
        static EditorApplication.CallbackFunction _walkTick;

        static GameDebug()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(WalkKey, false) && !_walk) WalkArm();
            };
        }

        public static string WalkStatus()
        {
            var cam = Camera.main;
            return $"walk={_walk} cam={(cam ? cam.name : "null")} mouse={Input.mousePosition} mx={Input.GetAxisRaw("Mouse X"):F3} my={Input.GetAxisRaw("Mouse Y"):F3} W={Input.GetKey(KeyCode.W)} lock={Cursor.lockState}";
        }

        public static string Walk()
        {
            // play mode: real Update loop — legacy Input + cursor lock work reliably there
            if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return "exiting play mode"; }
            EditorApplication.isPlaying = true;
            return "entering play mode — click the Game view: mouse look, WASD, Shift run, Space up, C down, Esc (x2) exit";
        }

        static void WalkArm()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _walk = true;
            SessionState.SetBool(WalkKey, true);
            var e = cam.transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180 ? e.x - 360 : e.x;
            _lastMouse = Input.mousePosition;
            if (_walkTick == null) { _walkTick = WalkTick; EditorApplication.update += _walkTick; }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        static void WalkStop()
        {
            _walk = false;
            SessionState.SetBool(WalkKey, false);
            if (_walkTick != null) { EditorApplication.update -= _walkTick; _walkTick = null; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static void WalkTick()
        {
            var cam = Camera.main;
            if (!_walk || cam == null) { WalkStop(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { WalkStop(); return; }
            if (!Application.isPlaying) EditorApplication.QueuePlayerLoopUpdate();

            // mouse look: GetAxisRaw first; fall back to raw mousePosition deltas (edit mode)
            float mx = Input.GetAxisRaw("Mouse X"), my = Input.GetAxisRaw("Mouse Y");
            var mp = Input.mousePosition;
            if (Mathf.Abs(mx) < 1e-4f && Mathf.Abs(my) < 1e-4f && mp != Vector3.zero)
            {
                mx = mp.x - _lastMouse.x;
                my = mp.y - _lastMouse.y;
            }
            _lastMouse = mp;
            if (Mathf.Abs(mx) > 200f || Mathf.Abs(my) > 200f) { mx = 0; my = 0; } // focus-jump guard
            _yaw += mx * 2.2f;
            _pitch = Mathf.Clamp(_pitch - my * 2.2f, -89f, 89f);
            var t = cam.transform;
            t.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float sp = (run ? 9f : 3.2f) / 60f;
            var pos = t.position;
            if (Input.GetKey(KeyCode.W)) pos += t.forward * sp;
            if (Input.GetKey(KeyCode.S)) pos -= t.forward * sp;
            if (Input.GetKey(KeyCode.D)) pos += t.right * sp;
            if (Input.GetKey(KeyCode.A)) pos -= t.right * sp;
            if (Input.GetKey(KeyCode.Space)) pos += Vector3.up * sp;
            if (Input.GetKey(KeyCode.C)) pos -= Vector3.up * sp;
            if (pos.y < 0.4f) pos.y = 0.4f;
            t.position = pos;
        }

        public static string PoseCam(double simX, double simZ, double eyeH, double yawDeg, double pitchDeg, double fov = 55)
        {
            var cam = Camera.main;
            if (cam == null) return "PoseCam: no MainCamera in active scene";
            cam.fieldOfView = (float)fov;
            // Unity camera yaw = 180 - simYaw (sim north/-Z is Unity +Z); Euler x>0 looks down.
            cam.transform.SetPositionAndRotation(
                new Vector3((float)simX, (float)eyeH, (float)-simZ),
                Quaternion.Euler((float)pitchDeg, (float)(180 - yawDeg), 0f));
            return "cam pos=" + cam.transform.position + " fov=" + fov;
        }

        public static string Shot(string name, int w = 1280, int h = 720)
        {
            Directory.CreateDirectory("Temp/shots");
            var path = Path.GetFullPath($"Temp/shots/{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            return "captured next frame -> " + path;
        }

        // Sample a grid of pixels from a saved PNG — text verification for shots.
        // Returns "r,g,b @c,r" rows (rows top->bottom).
        public static string Peek(string pngPath, int cols = 4, int rows = 3)
        {
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(File.ReadAllBytes(Path.GetFullPath(pngPath))))
                return "Peek: failed to load " + pngPath;
            var sb = new StringBuilder();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var p = tex.GetPixelBilinear((float)((c + 0.5) / cols), (float)(1.0 - (r + 0.5) / rows));
                    sb.Append($"{Mathf.Round(p.r * 255)},{Mathf.Round(p.g * 255)},{Mathf.Round(p.b * 255)}@");
                }
                sb.AppendLine($"row {r}");
            }
            UnityEngine.Object.DestroyImmediate(tex);
            return sb.ToString();
        }

        // ============================ state ============================

        public static string State()
        {
            var cam = Camera.main;
            return $"play={EditorApplication.isPlaying} frame={Time.frameCount} ts={Time.timeScale} " +
                   $"scene={EditorSceneManager.GetActiveScene().name} " +
                   $"cam={(cam ? cam.transform.position.ToString() : "null")}";
        }

        public static string Ensure()
        {
            if (GameObject.Find("Game") == null)
            {
                var go = new GameObject("Game");
                go.AddComponent<GameContext>();
            }
            return "game ok";
        }

        // ============================ phase 1: ground ============================

        // Build (or rebuild) the static ground: base, sidewalks, curbs, alley, promenade, walls.
        // Runs in edit mode; materials are placeholders until the wet-road shader lands.
        public static string AsyncResult = "";
        public static bool AsyncBusy = false;

        /// Run a slow build off the eval timeout: kick off, then poll AsyncResult.
        public static string BuildVehicles()
        {
            var root = GameObject.Find("VehiclesRoot");
            if (root == null) root = new GameObject("VehiclesRoot");
            return VehicleTraffic.Build(root.transform);
        }

        public static string BuildWeather()
        {
            var root = GameObject.Find("CityRoot");
            if (root == null) root = GameObject.Find("VehiclesRoot")?.transform.parent?.gameObject;
            return NeonKaido.WeatherDriver.Build(root != null ? root.transform : null);
        }

        public static string BuildCrowd()
        {
            var root = GameObject.Find("CityRoot");
            if (root == null) root = GameObject.Find("VehiclesRoot")?.transform.parent?.gameObject;
            return NeonKaido.Crowd.Build(root != null ? root.transform : null);
        }

        static System.Collections.Generic.List<string> _audioQueue;
        static int _audioIdx;

        public static string AudioStep(int batch = 1)
        {
            if (_audioQueue == null) { _audioQueue = new System.Collections.Generic.List<string>(); foreach (var o in NeonKaido.AudioBus.Order) _audioQueue.Add(o.name); }
            for (int b = 0; b < batch; b++)
            {
                float p = NeonKaido.AudioBus.GenerateNext();
                if (p >= 1f) break;
            }
            return $"audio gen {_audioIdx}/{_audioQueue.Count} ready={NeonKaido.AudioBus.Ready}";
        }

        public static string BuildAudio()
        {
            NeonKaido.Soundscape.Build();
            return "audio: " + NeonKaido.Soundscape.Describe();
        }

        public static string BuildGameplay()
        {
            var root = GameObject.Find("CityRoot");
            if (root == null) root = GameObject.Find("VehiclesRoot")?.transform.parent?.gameObject;
            NeonKaido.Gameplay.Build(root != null ? root.transform : null);
            return NeonKaido.Gameplay.Describe();
        }

        public static string RunAsync(string which)
        {
            System.Func<string> f = which == "buildings" ? (System.Func<string>)BuildBuildings
                : which == "landmarks" ? () => BuildLandmarks()
                : which == "atmosphere" ? () => BuildAtmosphere()
                : which == "ground" ? () => BuildGround()
                : which == "signage" ? () => BuildSignage()
                : which == "vehicles" ? () => BuildVehicles()
                : which == "weather" ? () => BuildWeather()
                : which == "crowd" ? () => BuildCrowd()
                : which == "gameplay" ? () => BuildGameplay()
                : which == "audiostep" ? () => AudioStep(4)
                : which == "all" ? () => BuildAtmosphere() + " | " + BuildGround() + " | " + BuildSignage() + " | " + BuildLandmarks() + " | " + BuildVehicles() + " | " + BuildWeather() + " | " + BuildCrowd() + " | " + BuildGameplay() + " | audio queued (run 'audiostep' to synthesize)"
                : () => "unknown async target";
            if (AsyncBusy) return "busy: " + AsyncResult;
            AsyncBusy = true;
            AsyncResult = "";
            UnityEditor.EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                try { AsyncResult = f(); }
                catch (System.Exception e) { AsyncResult = "EX " + e.Message; }
                finally { AsyncBusy = false; UnityEditor.EditorApplication.update -= cb; }
            };
            UnityEditor.EditorApplication.update += cb;
            return "started " + which;
        }

        public static string BuildBuildings()
        {
            var mats = new Dictionary<string, Material>
            {
                { "facade", BuildingMat("NEON KAIDO/Facade", "Facade", true) },
                { "detail", BuildingMat("NEON KAIDO/Detail", "Detail", false) },
                { "glass", BuildingMat("NEON KAIDO/Glass", "Glass", false) },
            };
            var acMat = BuildingMat("NEON KAIDO/AC", "AC", false);
            var r = NeonKaido.BuildingsRuntime.BuildAll(mats, acMat);
            // street lamps + alley utility-pole lamps (signage/street.js port)
            var lb = BuildingMat("NEON KAIDO/Lamp", "LampBody", false);
            lb.SetFloat("_Rough", 0.38f); lb.SetFloat("_Metal", 0.85f); lb.SetFloat("_LensIntensity", 0f);
            var ll = BuildingMat("NEON KAIDO/Lamp", "LampLens", true);
            ll.SetFloat("_Rough", 0.2f); ll.SetFloat("_Metal", 0f); ll.SetFloat("_LensIntensity", 9f);
            var pb = BuildingMat("NEON KAIDO/Lamp", "PoleBody", false);
            pb.SetFloat("_Rough", 0.85f); pb.SetFloat("_Metal", 0f); pb.SetFloat("_LensIntensity", 0f);
            var pl = BuildingMat("NEON KAIDO/Lamp", "PoleLens", true);
            pl.SetFloat("_Rough", 0.2f); pl.SetFloat("_Metal", 0f); pl.SetFloat("_LensIntensity", 8f);
            var sl = NeonKaido.StreetLamps.Build(lb, ll, pb, pl);
            EnsureGroundMats(); // re-apply fog + textures to road/surf mats every build

            var sg = BuildSignage(lb);
            // traffic + ped signals + holo ring (signage/street.js)
            var sigMat = BuildingMat("NEON KAIDO/Signal", "Signal", false);
            sigMat.SetTexture("_MainTex", SignTex("Assets/Game/Textures/Signage/color_atlas.png", true));
            ApplyFog(sigMat, false);
            var hsMat = BuildingMat("NEON KAIDO/SignHousing", "SignalHousing", false);
            ApplyFog(hsMat, false);
            var ts = NeonKaido.TrafficSignals.Build(hsMat, sigMat);
            var ringMat = BuildingMat("NEON KAIDO/HoloRing", "HoloRing", false);
            ringMat.SetTexture("_MainTex", SignTex("Assets/Game/Textures/Signage/color_atlas.png", true));
            ApplyFog(ringMat, false);
            var tr = NeonKaido.TrafficSignals.BuildRing(ringMat);
            sg = sg + " | " + ts + " | " + tr;
            return r + " | " + sl + " | " + sg;
        }

        static string TexDir => "Assets/Game/Textures/Buildings";

        // HDRP transparent setup for custom shaders: queue + surface type + no shadows
        static void SetupGlassTransparency(Material mat)
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; // 3000
            mat.SetFloat("_SurfaceType", 1f);
            mat.SetFloat("_BlendMode", 0f);   // alpha
            mat.SetFloat("_ZWrite", 0f);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.SetShaderPassEnabled("DepthOnly", false);
        }

        // texture import: linear/sRGB + wrap mode
        static Texture2D TexB(string path, bool linear, bool repeat)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (imp.sRGBTexture == linear || imp.wrapMode != (repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp)))
            {
                imp.sRGBTexture = !linear;
                imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // scene fog uniforms (atmo.js fogNodeFn defaults) + noise binding for fog patches
        static void ApplyFog(Material mat, bool bindNoise)
        {
            mat.SetFloat("_FogDensity", 0.0022f);
            mat.SetFloat("_FogFalloff", 0.028f);
            mat.SetFloat("_FogBase", 0f);
            mat.SetFloat("_FogStart", 14f);
            mat.SetFloat("_HazeDensity", 0.0002f);
            mat.SetFloat("_FogMax", 0.985f);
            mat.SetFloat("_GroundGlow", 0.18f);
            mat.SetFloat("_Rain", 1f);
            mat.SetVector("_CloudOffset", new Vector4(0f, 0f, 0, 0));
            if (bindNoise) mat.SetTexture("_Noise", TexB(TexDir + "/noise512.png", true, true));
        }

        static Material BuildingMat(string shaderName, string assetName, bool facade)
        {
            string path = $"Assets/Settings/Materials/{assetName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { ApplyFog(existing, shaderName == "NEON KAIDO/AC"); if (shaderName == "NEON KAIDO/Glass") SetupGlassTransparency(existing); return existing; }
            var mat = new Material(Shader.Find(shaderName));
            if (facade)
            {
                mat.SetTexture("_AtlasC", TexB($"{TexDir}/atlas_albedo.png", false, false));
                mat.SetTexture("_AtlasN", TexB($"{TexDir}/atlas_normal.png", true, false));
                mat.SetTexture("_Interior", TexB($"{TexDir}/interior_atlas.png", false, false));
                mat.SetTexture("_Noise", TexB($"{TexDir}/noise512.png", true, true));
            }
            else if (shaderName == "NEON KAIDO/Detail")
            {
                mat.SetTexture("_AtlasC", TexB($"{TexDir}/shop_atlas.png", false, false));
                mat.SetTexture("_Noise", TexB($"{TexDir}/noise512.png", true, true));
            }
            else if (shaderName == "NEON KAIDO/Glass")
            {
                mat.SetTexture("_Noise", TexB($"{TexDir}/noise512.png", true, true));
                SetupGlassTransparency(mat);
            }
            mat.SetVector("_SkyLightDir", new Vector4(0.5f, 0.76f, -0.43f, 0));
            mat.SetVector("_SkyLightColor", new Vector4(Atmo.HemiSky.r, Atmo.HemiSky.g, Atmo.HemiSky.b, 1) * 0.35f);
            mat.SetVector("_AmbientSky", new Vector4(0.012f, 0.014f, 0.03f, 1));
            mat.SetVector("_AmbientGround", new Vector4(0.03f, 0.02f, 0.05f, 1));
            mat.SetVector("_FogColor", Atmo.FogColor);
            mat.SetVector("_MidColor", Atmo.MidColor);
            mat.SetVector("_ZenithColor", Atmo.ZenithColor);
            mat.SetVector("_GlowMagenta", Atmo.GlowMagenta);
            mat.SetVector("_GlowOrange", Atmo.GlowOrange);
            mat.SetVector("_GlowBlue", Atmo.GlowBlue);
            mat.SetVector("_GroundHaze", Atmo.GroundHaze);
            mat.SetFloat("_SkyBrightness", Atmo.SkyBrightness);
            mat.SetFloat("_Wetness", 1f);
            ApplyFog(mat, shaderName == "NEON KAIDO/AC");
            Directory.CreateDirectory("Assets/Settings/Materials");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static string BuildGround()
        {
            var existing = GameObject.Find("Ground");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            var data = GroundGeo.BuildGroundGeometry();
            var root = new GameObject("Ground");
            var roadMat = RoadMaterial();
            var swMat = SurfMaterial(0);
            var curbMat = SurfMaterial(1);
            var promMat = SurfMaterial(2);
            var alleyMat = SurfMaterial(3);
            var wallMat = SurfMaterial(4);

            AddMesh(root, "Base", GroundMeshes.BuildBase(data.BaseRects, out _), roadMat);

            // physical colliders: base slabs, sidewalks (AABB approx), promenade, alley slabs
            var colRoot = new GameObject("Colliders");
            colRoot.transform.SetParent(root.transform, false);
            colRoot.isStatic = true;
            foreach (var r in data.BaseRects)
            {
                var go = new GameObject("col");
                go.transform.SetParent(colRoot.transform, false);
                go.transform.position = new Vector3((float)((r.X0 + r.X1) / 2), -0.15f, (float)(-(r.Z0 + r.Z1) / 2));
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3((float)(r.X1 - r.X0), 0.3f, (float)(r.Z1 - r.Z0));
            }
            foreach (var sw in data.Sidewalks)
            {
                // AABB of the collider poly (sim [x,z] pairs)
                float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
                if (sw.Collider == null) continue;
                foreach (var pt in sw.Collider)
                {
                    minx = Mathf.Min(minx, (float)pt[0]); maxx = Mathf.Max(maxx, (float)pt[0]);
                    minz = Mathf.Min(minz, (float)-pt[1]); maxz = Mathf.Max(maxz, (float)-pt[1]);
                }
                if (minx > maxx) continue;
                var go = new GameObject("colsw");
                go.transform.SetParent(colRoot.transform, false);
                go.transform.position = new Vector3((minx + maxx) / 2, (float)GroundPlan.CurbH, (minz + maxz) / 2);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(maxx - minx, 0.3f, maxz - minz);
            }
            {
                var p = data.Promenade;
                var go = new GameObject("colprom");
                go.transform.SetParent(colRoot.transform, false);
                go.transform.position = new Vector3((float)((p.X0 + p.X1) / 2), (float)GroundPlan.PromY, (float)(-(p.Z0 + p.Z1) / 2));
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3((float)(p.X1 - p.X0), 0.3f, (float)(p.Z1 - p.Z0));
            }
            foreach (var r in data.AlleyRects)
            {
                double y = r.X0 == GroundPlan.StairsXStart ? 0 : GroundPlan.StairsRise;
                var go = new GameObject("colalley");
                go.transform.SetParent(colRoot.transform, false);
                go.transform.position = new Vector3((float)((r.X0 + r.X1) / 2), (float)y, (float)(-(r.Z0 + r.Z1) / 2));
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3((float)(r.X1 - r.X0), 0.3f, (float)(r.Z1 - r.Z0));
            }

            var alleyQuads = new List<Tuple<GroundGeo.Rect2, double>>();
            foreach (var r in data.AlleyRects)
            {
                double y = r.X0 == GroundPlan.StairsXStart ? 0 : GroundPlan.StairsRise;
                alleyQuads.Add(Tuple.Create(r, y));
            }
            AddMesh(root, "Alley", GroundMeshes.BuildQuads(alleyQuads), alleyMat);
            AddMesh(root, "Promenade", GroundMeshes.BuildQuads(new List<Tuple<GroundGeo.Rect2, double>> { Tuple.Create(data.Promenade, GroundPlan.PromY) }), promMat);

            AddMesh(root, "Sidewalks", GroundMeshes.BuildSidewalks(data.Sidewalks), swMat);
            AddMesh(root, "Curbs", GroundMeshes.BuildCurb(data.Curb), curbMat);
            AddMesh(root, "Walls", GroundMeshes.BuildWalls(data), wallMat);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            return $"ground built: base={data.BaseRects.Count} rects, sidewalks={data.Sidewalks.Count}, curbVerts={data.Curb.Pos.Count / 3} tris";
        }

        // Road material with the full ground texture set. _c sRGB, _n linear (JS loader semantics).
        static Material RoadMaterial()
        {
            const string path = "Assets/Settings/Materials/Road.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("NEON KAIDO/Road"));
                Directory.CreateDirectory("Assets/Settings/Materials");
                AssetDatabase.CreateAsset(mat, path);
            }
            // always: fog uniforms + missing textures (older .mat assets predate the texture set;
            // raw-shader properties without Properties entries also don't persist across restarts)
            ApplyFog(mat, true);
            BindRoadTex(mat);
            mat.SetFloat("_Wetness", 1f);
            mat.SetFloat("_Rain", 1f);
            mat.SetVector("_SkyLightDir", new Vector4(0.5f, 0.76f, -0.43f, 0));
            mat.SetVector("_SkyLightColor", new Vector4(Atmo.HemiSky.r, Atmo.HemiSky.g, Atmo.HemiSky.b, 1) * 0.35f);
            mat.SetVector("_AmbientSky", new Vector4(0.012f, 0.014f, 0.03f, 1));
            mat.SetVector("_AmbientGround", new Vector4(0.03f, 0.02f, 0.05f, 1));
            mat.SetVector("_FogColor", Atmo.FogColor);
            mat.SetVector("_MidColor", Atmo.MidColor);
            mat.SetVector("_ZenithColor", Atmo.ZenithColor);
            mat.SetVector("_GlowMagenta", Atmo.GlowMagenta);
            mat.SetVector("_GlowOrange", Atmo.GlowOrange);
            mat.SetVector("_GlowBlue", Atmo.GlowBlue);
            mat.SetVector("_GroundHaze", Atmo.GroundHaze);
            mat.SetFloat("_SkyBrightness", Atmo.SkyBrightness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Ground textures (road). Re-binding on the load path is REQUIRED: older .mat assets
        // predate the texture set, and textures/fog uniforms must also survive editor restarts.
        static void BindRoadTex(Material mat)
        {
            const string dir = "Assets/Textures/Ground";
            Texture2D Tex(string name, bool linear)
            {
                var p = $"{dir}/{name}.png";
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp != null && imp.sRGBTexture == linear)
                {
                    imp.sRGBTexture = !linear;
                    imp.anisoLevel = 8;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            if (mat.GetTexture("_AsphaltC") == null) mat.SetTexture("_AsphaltC", Tex("asphalt_a_c", false));
            if (mat.GetTexture("_AsphaltN") == null) mat.SetTexture("_AsphaltN", Tex("asphalt_a_n", true));
            if (mat.GetTexture("_AsphaltBC") == null) mat.SetTexture("_AsphaltBC", Tex("asphalt_b_c", false));
            if (mat.GetTexture("_AsphaltBN") == null) mat.SetTexture("_AsphaltBN", Tex("asphalt_b_n", true));
            if (mat.GetTexture("_PaintWearC") == null) mat.SetTexture("_PaintWearC", Tex("paint_wear_c", false));
            if (mat.GetTexture("_ConcreteC") == null) mat.SetTexture("_ConcreteC", Tex("concrete_c", false));
            if (mat.GetTexture("_ConcreteN") == null) mat.SetTexture("_ConcreteN", Tex("concrete_n", true));
            if (mat.GetTexture("_GrateC") == null) mat.SetTexture("_GrateC", Tex("drain_grate_c", false));
            if (mat.GetTexture("_GrateN") == null) mat.SetTexture("_GrateN", Tex("drain_grate_n", true));
            if (mat.GetTexture("_ManholeC") == null) mat.SetTexture("_ManholeC", Tex("manhole_c", false));
            if (mat.GetTexture("_ManholeN") == null) mat.SetTexture("_ManholeN", Tex("manhole_n", true));
            if (mat.GetTexture("_MarkAtlas") == null) mat.SetTexture("_MarkAtlas", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Ground/mark_atlas.png"));
        }

        public static string BuildSignage(Material steelBody = null)
        {
            var bMat = BuildingMat("NEON KAIDO/Billboard", "BillboardMirai", false);
            bMat.SetTexture("_MainTex", SignTex("Assets/Game/Textures/Signage/billboard_mirai.png", false));
            ApplyFog(bMat, false);
            var sMat = BuildingMat("NEON KAIDO/VideoScreen", "VideoScreens", false);
            sMat.SetTexture("_MainTex", SignTex("Assets/Game/Textures/Signage/ads_grid.png", false));
            ApplyFog(sMat, false);
            var hMat = BuildingMat("NEON KAIDO/Holo", "Holo", false);
            hMat.SetTexture("_MainTex", SignTex("Assets/Game/Textures/Signage/ads_grid.png", false));
            ApplyFog(hMat, false);
            var kMat = BuildingMat("NEON KAIDO/NeonQuad", "KazeTech", false);
            kMat.SetTexture("_MainTex", SignTex("Assets/Game/Textures/Signage/kaze_tech.png", true));
            kMat.SetFloat("_Intensity", 2.2f);
            ApplyFog(kMat, false);
            if (steelBody == null)
            {
                var lb2 = BuildingMat("NEON KAIDO/Lamp", "LampBody", false);
                lb2.SetFloat("_Rough", 0.38f); lb2.SetFloat("_Metal", 0.85f); lb2.SetFloat("_LensIntensity", 0f);
                steelBody = lb2;
            }
            var sg = NeonKaido.Billboards.Build(bMat, sMat, hMat, kMat, steelBody);
            // facade neon signs (signs.js + populate.js port)
            var fMat = BuildingMat("NEON KAIDO/NeonSign", "NeonSignFaces", false);
            fMat.SetTexture("_NeonTex", SignTex("Assets/Game/Textures/Signage/neon_atlas.png", false));
            fMat.SetTexture("_ColorTex", SignTex("Assets/Game/Textures/Signage/color_atlas.png", true));
            ApplyFog(fMat, false);
            var gMat = BuildingMat("NEON KAIDO/SignHousing", "SignHousing", false);
            ApplyFog(gMat, false);
            var fs = NeonKaido.FacadeSigns.Build(fMat, gMat);
            Shader.SetGlobalFloat("_SimTime", 0f);
            return sg + " | " + fs + " | " + BuildLandmarks();
        }

        /// Landmarks system (river/embankments; rail).
        public static string BuildLandmarks()
        {
            var r = "";
            var old = GameObject.Find("LandmarksRiver");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject("LandmarksRiver");
            r = NeonKaido.River.Build(root.transform);

            var oldR = GameObject.Find("LandmarksRail");
            if (oldR != null) UnityEngine.Object.DestroyImmediate(oldR);
            var rootR = new GameObject("LandmarksRail");
            var rail = NeonKaido.Rail.Build(rootR.transform);
            var trains = NeonKaido.Trains.Build(rootR.transform); // lights land under /LandmarksRail (scanned)

            var oldE = GameObject.Find("LandmarksExpress");
            if (oldE != null) UnityEngine.Object.DestroyImmediate(oldE);
            var rootE = new GameObject("LandmarksExpress");
            var exp = NeonKaido.Expressway.Build(rootE.transform);

            var oldC = GameObject.Find("LandmarksCherry");
            if (oldC != null) UnityEngine.Object.DestroyImmediate(oldC);
            var rootC = new GameObject("LandmarksCherry");
            var cherry = NeonKaido.Cherry.Build(rootC.transform);
            var oldP = GameObject.Find("LandmarksProps");
            if (oldP != null) UnityEngine.Object.DestroyImmediate(oldP);
            var rootP = new GameObject("LandmarksProps");
            var props = NeonKaido.PromenadeProps.Build(rootP.transform);

            var oldB = GameObject.Find("LandmarksBridge");
            if (oldB != null) UnityEngine.Object.DestroyImmediate(oldB);
            var rootB = new GameObject("LandmarksBridge");
            var bridge = NeonKaido.Bridge.Build(rootB.transform);

            var oldS = GameObject.Find("LandmarksSkyline");
            if (oldS != null) UnityEngine.Object.DestroyImmediate(oldS);
            var rootS = new GameObject("LandmarksSkyline");
            var sky = NeonKaido.Skyline.Build(rootS.transform);

            var rootV = GameObject.Find("VehiclesRoot");
            if (rootV != null) UnityEngine.Object.DestroyImmediate(rootV);
            var rootVeh = new GameObject("VehiclesRoot");
            var veh = NeonKaido.VehicleTraffic.Build(rootVeh.transform);
            return r + " | " + rail + " | " + trains + " | " + exp + " | " + cherry + " | " + props + " | " + bridge + " | " + sky + " | " + veh;
        }

        static Texture2D SignTex(string path, bool srgb)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (imp.sRGBTexture != srgb || imp.wrapMode != TextureWrapMode.Clamp))
            {
                imp.sRGBTexture = srgb;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Re-apply fog + missing textures to every ground material (call each build).
        public static string EnsureGroundMats()
        {
            int n = 0;
            RoadMaterial(); n++;
            for (int i = 0; i < 5; i++) { SurfMaterial(i); n++; }
            return $"ground mats refreshed: {n}";
        }

        // Ground surface material (Surf.shader) for a given mode; textures _c sRGB, _n linear.
        static void BindSurfTex(Material mat)
        {
            const string dir = "Assets/Textures/Ground";
            Texture2D Tex(string name, bool linear)
            {
                var p = $"{dir}/{name}.png";
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp != null && imp.sRGBTexture == linear)
                {
                    imp.sRGBTexture = !linear;
                    imp.anisoLevel = 8;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            if (mat.GetTexture("_SidewalkC") == null) mat.SetTexture("_SidewalkC", Tex("sidewalk_tiles_c", false));
            if (mat.GetTexture("_SidewalkN") == null) mat.SetTexture("_SidewalkN", Tex("sidewalk_tiles_n", true));
            if (mat.GetTexture("_TactLineC") == null) mat.SetTexture("_TactLineC", Tex("tactile_paving_c", false));
            if (mat.GetTexture("_TactLineN") == null) mat.SetTexture("_TactLineN", Tex("tactile_paving_n", true));
            if (mat.GetTexture("_TactDotsC") == null) mat.SetTexture("_TactDotsC", Tex("tactile_dots_c", false));
            if (mat.GetTexture("_TactDotsN") == null) mat.SetTexture("_TactDotsN", Tex("tactile_dots_n", true));
            if (mat.GetTexture("_GraniteC") == null) mat.SetTexture("_GraniteC", Tex("granite_c", false));
            if (mat.GetTexture("_GraniteN") == null) mat.SetTexture("_GraniteN", Tex("granite_n", true));
            if (mat.GetTexture("_StoneC") == null) mat.SetTexture("_StoneC", Tex("stone_pavers_c", false));
            if (mat.GetTexture("_StoneN") == null) mat.SetTexture("_StoneN", Tex("stone_pavers_n", true));
            if (mat.GetTexture("_AlleyC") == null) mat.SetTexture("_AlleyC", Tex("alley_pavers_c", false));
            if (mat.GetTexture("_AlleyN") == null) mat.SetTexture("_AlleyN", Tex("alley_pavers_n", true));
            if (mat.GetTexture("_ConcreteC") == null) mat.SetTexture("_ConcreteC", Tex("concrete_c", false));
            if (mat.GetTexture("_ConcreteN") == null) mat.SetTexture("_ConcreteN", Tex("concrete_n", true));
        }

        static Material SurfMaterial(int mode)
        {
            string[] names = { "SurfSidewalk", "SurfCurb", "SurfProm", "SurfAlley", "SurfWall" };
            var path = $"Assets/Settings/Materials/{names[mode]}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("NEON KAIDO/Surf"));
                AssetDatabase.CreateAsset(mat, path);
            }
            ApplyFog(mat, true);
            BindSurfTex(mat);
            mat.SetFloat("_Mode", mode);
            mat.SetFloat("_Wetness", 1f);
            mat.SetFloat("_Rain", 1f);
            mat.SetVector("_SkyLightDir", new Vector4(0.5f, 0.76f, -0.43f, 0));
            mat.SetVector("_SkyLightColor", new Vector4(Atmo.HemiSky.r, Atmo.HemiSky.g, Atmo.HemiSky.b, 1) * 0.35f);
            mat.SetVector("_AmbientSky", new Vector4(0.012f, 0.014f, 0.03f, 1));
            mat.SetVector("_AmbientGround", new Vector4(0.03f, 0.02f, 0.05f, 1));
            mat.SetVector("_FogColor", Atmo.FogColor);
            mat.SetVector("_MidColor", Atmo.MidColor);
            mat.SetVector("_ZenithColor", Atmo.ZenithColor);
            mat.SetVector("_GlowMagenta", Atmo.GlowMagenta);
            mat.SetVector("_GlowOrange", Atmo.GlowOrange);
            mat.SetVector("_GlowBlue", Atmo.GlowBlue);
            mat.SetVector("_GroundHaze", Atmo.GroundHaze);
            mat.SetFloat("_SkyBrightness", Atmo.SkyBrightness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static string ShaderCheck()
        {
            var sb = new StringBuilder();
            foreach (var name in new[] { "NEON KAIDO/Road", "NEON KAIDO/Sky", "NEON KAIDO/Surf" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { sb.AppendLine(name + ": NOT FOUND"); continue; }
                var msgs = ShaderUtil.GetShaderMessages(sh);
                if (msgs.Length == 0) sb.AppendLine(name + ": clean");
                foreach (var m in msgs)
                    sb.AppendLine($"{name}: {m.severity} L{m.line}: {m.message}");
            }
            return sb.ToString();
        }

        // Preflight health aggregate — run BEFORE captures (shot.sh refuses when unhealthy).
        // Unhealthy = console errors since last Clear, or shader messages, or camera/scene broken.
        public static string Health()
        {
            var sb = new StringBuilder();
            bool ok = true;
            if (ErrorWatch.Count > 0)
            {
                ok = false;
                sb.AppendLine($"console-errors={ErrorWatch.Count} last: {ErrorWatch.Last.Substring(0, Math.Min(220, ErrorWatch.Last.Length))}");
            }
            foreach (var name in new[] { "NEON KAIDO/Road", "NEON KAIDO/Sky", "NEON KAIDO/Surf" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { ok = false; sb.AppendLine("shader missing: " + name); continue; }
                var msgs = ShaderUtil.GetShaderMessages(sh);
                if (msgs.Length > 0)
                {
                    ok = false;
                    sb.AppendLine($"shader {name}: {msgs.Length} message(s), first: {msgs[0].severity} L{msgs[0].line} {msgs[0].message}");
                }
            }
            var cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled || cam.GetComponent<HDAdditionalCameraData>() == null)
            {
                ok = false;
                sb.AppendLine("camera: missing/disabled/no HDAdditionalCameraData (legacy camera renders sky only!)");
            }
            bool FindActive(string n) => UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Any(g => g.name == n);
            if (!FindActive("Ground") || !FindActive("Skydome"))
            {
                ok = false;
                sb.AppendLine("scene: Ground or Skydome missing (run BuildAtmosphere + BuildGround)");
            }
            return (ok ? "HEALTHY" : "UNHEALTHY") + (sb.Length > 0 ? "\n" + sb : "");
        }

        public static string ClearErrors()
        {
            ErrorWatch.Clear();
            return "error watch cleared (domain total " + ErrorWatch.SinceDomain + ")";
        }

        static Material StdMat(string name, float r, float g, float b)
        {
            // Materials must be ASSETS: runtime-created materials die on domain reload
            // (every recompile) and leave renderers magenta.
            const string dir = "Assets/Settings/Materials";
            var path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Directory.CreateDirectory(dir);
                var shader = Shader.Find("HDRP/Lit");
                mat = new Material(shader);
                mat.name = name;
                mat.color = new Color(r, g, b);
                mat.SetFloat("_SurfaceRotation", 0f);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static void AddMesh(GameObject parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            go.isStatic = true;
        }

        // ============================ phase 1: atmosphere ============================

        // Build (or rebuild) the night atmosphere: noise texture asset, custom skydome
        // (verbatim skyColor port), remove default sun. The real fog is the
        // shader port (NkFogApply / atmo.js fogNodeFn) applied per-material via ApplyFog;
        // HDRP's Fog volume component is explicitly DISABLED (it used to double-fog the
        // lower half of the view).
        public static string BuildAtmosphere()
        {
            var sb = new StringBuilder();
            // 1) noise texture asset (port of createNoiseTexture(256, 4242))
            const string noisePath = "Assets/Settings/noiseTex.png";
            if (!File.Exists(noisePath))
            {
                File.WriteAllBytes(noisePath, ImageConversion.EncodeToPNG(NoiseTexture()));
                sb.AppendLine("noise texture written");
            }
            // 2) skydome
            // destroy ALL existing skydomes (GameObject.Find misses inactive ones -> duplicates)
            foreach (var d in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                if (d.name == "Skydome") UnityEngine.Object.DestroyImmediate(d.gameObject);
            var domeGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.DestroyImmediate(domeGo.GetComponent<Collider>());
            domeGo.name = "Skydome";
            domeGo.transform.localScale = new Vector3(2000, 2000, 2000);
            var skyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Materials/Sky.mat");
            if (skyMat == null)
            {
                skyMat = new Material(Shader.Find("NEON KAIDO/Sky"));
                AssetDatabase.CreateAsset(skyMat, "Assets/Settings/Materials/Sky.mat");
            }
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(noisePath);
            if (noise != null) { noise.wrapMode = TextureWrapMode.Repeat; noise.filterMode = FilterMode.Bilinear; }
            skyMat.SetTexture("_NoiseTex", noise);
            skyMat.SetVector("_FogColor", Atmo.FogColor);
            skyMat.SetVector("_MidColor", Atmo.MidColor);
            skyMat.SetVector("_ZenithColor", Atmo.ZenithColor);
            skyMat.SetVector("_GlowMagenta", Atmo.GlowMagenta);
            skyMat.SetVector("_GlowOrange", Atmo.GlowOrange);
            skyMat.SetVector("_GlowBlue", Atmo.GlowBlue);
            skyMat.SetVector("_GroundHaze", Atmo.GroundHaze);
            skyMat.SetFloat("_SkyBrightness", Atmo.SkyBrightness);
            skyMat.SetFloat("_CloudCover", Atmo.CloudCover);
            // lightning dir: sim (0.3, 0.35, -0.9) -> unity (0.3, 0.35, 0.9)
            skyMat.SetVector("_LightningDir", new Vector3(Atmo.LightningDir.x, Atmo.LightningDir.y, -Atmo.LightningDir.z));
            var mf = domeGo.GetComponent<MeshFilter>();
            var sm = domeGo.GetComponent<MeshRenderer>();
            sm.sharedMaterial = skyMat;
            domeGo.isStatic = true;
            sb.AppendLine("skydome built");
            // 3) remove default sun
            var sun = GameObject.Find("Directional Light");
            if (sun != null) { UnityEngine.Object.DestroyImmediate(sun); sb.AppendLine("default sun removed"); }
            // 4) sky/ambient light: REMOVED — all city lighting is shader-based (NkBPointLights +
            // hemi/sky uniforms); HDRP lights are unused (see lighting spike) and their gizmos
            // cluttered the scene view ("inverted bowl" at the origin).
            // 5) fog + sky volume from copied template profile
            const string profPath = "Assets/Settings/SkyandFogSettingsProfile.asset";
            if (!File.Exists(profPath))
            {
                File.Copy(Path.Combine("ref-profile", "SkyandFogSettingsProfile.asset"), profPath);
            }
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (profile != null)
            {
                foreach (var comp in profile.components)
                {
                    if (comp == null) continue;
                    var tn = comp.GetType().Name;
                    var so = new SerializedObject(comp);
                    if (tn == "Fog")
                    {
                        // REAL shader fog (NkFogApply, port of atmo.js fogNodeFn) is live in all
                        // materials now — HDRP height fog would DOUBLE-FOG the lower half of the
                        // view (opaque dark blue haze). Kill it permanently.
                        var en = so.FindProperty("m_Enabled");
                        if (en != null)
                        {
                            en.boolValue = false;
                            so.ApplyModifiedProperties();
                            EditorUtility.SetDirty(comp);
                            sb.AppendLine("HDRP fog component DISABLED (shader fog is authoritative)");
                        }
                    }
                    else if (tn == "PhysicallyBasedSky" || tn.Contains("Sky"))
                    {
                        var en = so.FindProperty("enabled");
                        if (en != null)
                        {
                            en.FindPropertyRelative("m_OverrideState").intValue = 1;
                            en.FindPropertyRelative("m_Value").intValue = 0;
                            so.ApplyModifiedProperties();
                            sb.AppendLine("HDRP sky component disabled (custom dome does background)");
                        }
                    }
                    else if (tn == "Exposure")
                    {
                        so.FindProperty("mode.m_OverrideState").intValue = 1;
                        so.FindProperty("mode.m_Value").intValue = 0; // Fixed
                        so.FindProperty("fixedExposure.m_OverrideState").intValue = 1;
                        so.FindProperty("fixedExposure.m_Value").floatValue = 0f;
                        so.ApplyModifiedProperties();
                        sb.AppendLine("exposure fixed 0 EV");
                    }
                }
            }
            else sb.AppendLine("WARNING: volume profile missing");
            var vol = GameObject.Find("AtmoVolume");
            if (vol == null)
            {
                vol = new GameObject("AtmoVolume");
                var v = vol.AddComponent<Volume>();
                v.isGlobal = true;
                v.sharedProfile = profile;
                sb.AppendLine("volume added");
            }
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            return sb.Length == 0 ? "atmosphere: up to date" : sb.ToString().TrimEnd();
        }

        static Texture2D NoiseTexture()
        {
            var bytes = AtmoNoise.Create(256, 4242);
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, true, true); // linear, mipmaps
            tex.SetPixelData(bytes, 0);
            tex.Apply(true, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.name = "noiseTex";
            return tex;
        }

        public static string Weather(double rain, double lightning)
        {
            if (GameContext.I == null) return "no GameContext (play mode?)";
            GameContext.I.weather.rain = (float)rain;
            GameContext.I.weather.lightning = (float)lightning;
            return $"weather rain={rain} lightning={lightning}";
        }

        // Exposure override for review shots (the correct-exposure night is nearly black pre-neon).
        public static string Exposure(double ev)
        {
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SkyandFogSettingsProfile.asset");
            foreach (var c in prof.components)
            {
                if (c == null || c.GetType().Name != "Exposure") continue;
                var so = new SerializedObject(c);
                so.FindProperty("mode.m_OverrideState").intValue = 1;
                so.FindProperty("mode.m_Value").intValue = 0; // Fixed
                so.FindProperty("fixedExposure.m_OverrideState").intValue = 1;
                so.FindProperty("fixedExposure.m_Value").floatValue = (float)ev;
                so.ApplyModifiedProperties();
            }
            return "exposure EV=" + ev;
        }

        // ============================ phase 1: markings atlas ============================

        // C# port of src/systems/ground/markings.js buildMarkingAtlas: 6 cells (256x768 each),
        // white paint mask on black. Rendered with OS JP fonts + GL polygons into an RT, saved as PNG.
        public static string BuildMarkingAtlas()
        {
            const int cellW = 256, cellH = 768, nCells = 6;
            const string path = "Assets/Textures/Ground/mark_atlas.png";
            if (File.Exists(path)) return "marking atlas: exists";
            var rt = new RenderTexture(cellW * nCells, cellH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, cellW * nCells, 0, cellH); // y up (JS canvas had y down; glyph v coords handled at sample time)
            // arrows + diamond (geometry, y measured from TOP as in JS: top = cellH)
            DrawArrow(cellW * 1, cellW, cellH, -1);
            DrawArrow(cellW * 2, cellW, cellH, 1);
            DrawDiamond(cellW * 4, cellW, cellH);
            // text glyphs via Unity font glyph quads (GUI is unavailable outside OnGUI)
            var font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 240);
            DrawFontText(font, 0, cellW, cellH, "止まれ", 92, 0.5f);
            DrawFontText(font, 3, cellW, cellH, "40", 210, 0.5f);
            DrawFontText(font, 5, cellW, cellH, "バス", 120, 0.27f);
            DrawFontText(font, 5, cellW, cellH, "専用", 120, 0.75f);
            GL.PopMatrix();
            var tex = new Texture2D(cellW * nCells, cellH, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, cellW * nCells, cellH), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            AssetDatabase.ImportAsset(path);
            return "marking atlas written (1536x768)";
        }

        // Text via font glyph quads (JS text(): shrink-to-fit width, centered, stretch on y).
        static void DrawFontText(Font font, int cell, int cellW, int cellH, string str, float px, float y0FromTop)
        {
            font.RequestCharactersInTexture(str, (int)px, FontStyle.Normal);
            float total = 0;
            var infos = new List<CharacterInfo>();
            foreach (char c in str)
            {
                if (font.GetCharacterInfo(c, out var ci, (int)px)) { infos.Add(ci); total += ci.advance; }
                else { infos.Add(new CharacterInfo()); total += px * 0.5f; }
            }
            float sx = Mathf.Min(1f, cellW * 0.94f / Mathf.Max(total, 1));
            float cx = cell * cellW + cellW / 2f - total * sx / 2f;
            float penY = cellH - cellH * y0FromTop;   // y-up pixel matrix; JS y0 measured from top
            float baseline = penY + px * 0.35f;       // approximate baseline above the vertical anchor
            float pen = cx;
            font.material.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(Color.white);
            foreach (var ci in infos)
            {
                if (ci.glyphWidth > 0)
                {
                    float x0 = pen + ci.bearing * sx;
                    float yBot = baseline + ci.minY;
                    float gh = ci.maxY - ci.minY;
                    GL.TexCoord2(ci.uvBottomLeft.x, ci.uvBottomLeft.y); GL.Vertex3(x0, yBot, 0);
                    GL.TexCoord2(ci.uvTopRight.x, ci.uvBottomLeft.y); GL.Vertex3(x0 + ci.glyphWidth * sx, yBot, 0);
                    GL.TexCoord2(ci.uvTopRight.x, ci.uvTopRight.y); GL.Vertex3(x0 + ci.glyphWidth * sx, yBot + gh, 0);
                    GL.TexCoord2(ci.uvBottomLeft.x, ci.uvBottomLeft.y); GL.Vertex3(x0, yBot, 0);
                    GL.TexCoord2(ci.uvTopRight.x, ci.uvTopRight.y); GL.Vertex3(x0 + ci.glyphWidth * sx, yBot + gh, 0);
                    GL.TexCoord2(ci.uvBottomLeft.x, ci.uvTopLeft.y); GL.Vertex3(x0, yBot + gh, 0);
                }
                pen += ci.advance * sx;
            }
            GL.End();
        }

        // JS arrow(cell, branch): shaft + head + optional bent branch; y down from cell top
        static void DrawArrow(int ox, int cellW, int cellH, int branch)
        {
            float cx = ox + cellW * (branch != 0 ? (branch < 0 ? 0.62f : 0.38f) : 0.5f);
            float sw = 30;
            // shaft: rect (cx-sw/2, top 0.3*cellH, sw, 0.68*cellH) — convert y-down to y-up
            Quad(cx - sw / 2, cellH - cellH * 0.3f, sw, cellH * 0.68f);
            // head triangle: (cx, 0.02H) (cx-52, 0.32H) (cx+52, 0.32H)
            Tri(new Vector2(cx, cellH - cellH * 0.02f), new Vector2(cx - 52, cellH - cellH * 0.32f), new Vector2(cx + 52, cellH - cellH * 0.32f));
            if (branch != 0)
            {
                float bx = cx + branch * 92;
                Quad2(new Vector2(cx, cellH - cellH * 0.72f), new Vector2(cx, cellH - cellH * 0.62f),
                      new Vector2(bx, cellH - cellH * 0.46f), new Vector2(bx, cellH - cellH * 0.56f));
                Tri(new Vector2(bx + branch * 34, cellH - cellH * 0.40f), new Vector2(bx - branch * 10, cellH - cellH * 0.36f),
                    new Vector2(bx + branch * 2, cellH - cellH * 0.62f));
            }
        }

        // JS diamond outline: lineWidth 34 stroke of the 4-gon
        static void DrawDiamond(int ox, int cellW, int cellH)
        {
            float midX = ox + cellW / 2f;
            var pts = new[] {
                new Vector2(midX, cellH - 30), new Vector2(ox + cellW - 40, cellH / 2f),
                new Vector2(midX, 30), new Vector2(ox + 40, cellH / 2f),
            };
            float lw = 34;
            for (int i = 0; i < 4; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % 4];
                var dir = (b - a).normalized;
                var n = new Vector2(-dir.y, dir.x) * (lw / 2f);
                Tri(a + n, b + n, b - n);
                Tri(a + n, b - n, a - n);
            }
        }

        static Material _glMat;
        static void GlPass()
        {
            if (_glMat == null) _glMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            _glMat.SetPass(0);
        }

        static void Quad(float xBottom, float yBottom, float w, float h)
        {
            GlPass();
            GL.Begin(GL.TRIANGLES);
            GL.Color(Color.white);
            GL.Vertex3(xBottom, yBottom, 0); GL.Vertex3(xBottom + w, yBottom, 0); GL.Vertex3(xBottom + w, yBottom + h, 0);
            GL.Vertex3(xBottom, yBottom, 0); GL.Vertex3(xBottom + w, yBottom + h, 0); GL.Vertex3(xBottom, yBottom + h, 0);
            GL.End();
        }

        static void Quad2(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            GlPass();
            GL.Begin(GL.TRIANGLES);
            GL.Color(Color.white);
            GL.Vertex3(a.x, a.y, 0); GL.Vertex3(b.x, b.y, 0); GL.Vertex3(c.x, c.y, 0);
            GL.Vertex3(a.x, a.y, 0); GL.Vertex3(c.x, c.y, 0); GL.Vertex3(d.x, d.y, 0);
            GL.End();
        }

        static void Tri(Vector2 a, Vector2 b, Vector2 c)
        {
            GlPass();
            GL.Begin(GL.TRIANGLES);
            GL.Color(Color.white);
            GL.Vertex3(a.x, a.y, 0); GL.Vertex3(b.x, b.y, 0); GL.Vertex3(c.x, c.y, 0);
            GL.End();
        }

        // ============================ sim (stubs until phase 1+) ============================

        public static string Run(double seconds) => "Run: not implemented (phase 1: fixed-step driver)";

        public static string Advance(double seconds) => "Advance: not implemented (phase 1)";

        public static string At(double simX, double simZ, double yawDeg)
        {
            // Phase-0 placeholder: position a marker. Phase 3 replaces this with the real player.
            var go = GameObject.Find("Player");
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Player";
                go.GetComponent<Collider>().enabled = false; // placeholder marker, no collision
            }
            go.transform.SetPositionAndRotation(
                new Vector3((float)simX, 0f, (float)-simZ),
                Quaternion.Euler(0, (float)(180 - yawDeg), 0f));
            return "player @ " + go.transform.position;
        }

        public static string Hour(double h) => "Hour: not implemented (phase 1: day/night clock)";
        public static string Weather(double rain) => "Weather: not implemented (phase 4)";
        public static string Walk(string input, float seconds) => "Walk: not implemented (phase 3: player)";
        public static string Drive(int carId) => "Drive: not implemented (phase 3: vehicles)";
        public static string Gig(int n) => "Gig: not implemented (phase 3: gigs)";
    }
}
#endif

// touch

// touch

// touch2

// touch

// touch

// touch

// touch

// touch

// touch

// touch

// touch

// touch

// touch

// touch

// touch

// touch

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// t

// wx

// wx2

// wx3

// wx4

// wx5

// wx6

// cr

// cr2

// cr3

// cr4

// cr5

// cr6

// cr7

// cr8

// cr9

// cr10

// cr11

// cr12

// cr13

// cr14

// gp1

// gp2

// gp3

// gp4

// gp5

// gp6

// gp7

// gp8

// gp9

// au1

// au2

// au3

// au4

// au5

// au6

// au7

// au8

// au9

// au10

// au11

// au12

// au13

// au14

// au15

// wk1

// wk2
