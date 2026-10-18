// Planar reflection for the flat, wet street plane.
//
// The browser's signature look is the asphalt: PostFX.js runs a half-res SSR pass and then a 7-tap
// vertical smear of it, so the road mirrors every neon sign above it. The port cannot do that — its
// ~45 custom shaders write their own tonemapped colour and never populate HDRP's G-buffer, so HDRP's
// ScreenSpaceReflection has nothing to march, and Road.shader's env term is only an analytic sky
// gradient (NkHazeColor). Asphalt could therefore never reflect the street at all.
//
// The road is a plane, so a mirrored camera reproduces SSR exactly and cheaply: render the scene
// from the camera reflected through y = groundY into a half-res RT and let the road sample it.
// Nothing below the plane is rendered (oblique near plane), otherwise the mirrored camera would see
// the underside of the city.
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonKaido
{
    [DefaultExecutionOrder(100)]
    public class NkPlanarReflection : MonoBehaviour
    {
        public static NkPlanarReflection I;

        /// Mirror plane height (Unity Y of the road surface). Probed from the ground collider at boot.
        public float groundY = 1.7f;
        [Range(0.1f, 1f)] public float resolutionScale = 0.5f;
        /// Extra darkening of the reflected image (wet asphalt is not a perfect mirror).
        [Range(0f, 1f)] public float tint = 1f;

        static readonly int TexId = Shader.PropertyToID("_NkReflTex");
        static readonly int VpId = Shader.PropertyToID("_NkReflVP");
        static readonly int GroundId = Shader.PropertyToID("_NkGroundY");
        static readonly int OnId = Shader.PropertyToID("_NkReflOn");
        static readonly int PassId = Shader.PropertyToID("_NkReflPass");

        Camera _cam, _refl;
        RenderTexture _rt;
        Matrix4x4 _vp = Matrix4x4.identity;
        int _rtW, _rtH;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("NkPlanarReflection") { hideFlags = HideFlags.DontSave };
            I = go.AddComponent<NkPlanarReflection>();
        }

        void LateUpdate()
        {
            var main = Camera.main;
            if (main == null) return;
            ProbeGround(main);
            if (_refl == null || _rt == null || _rtW != Screen.width || _rtH != Screen.height) Create(main);
            Render(main);
        }

        /// The road is y-constant; find it by dropping a ray from the camera onto the ground collider.
        void ProbeGround(Camera main)
        {
            var from = main.transform.position + Vector3.up * 4f;
            RaycastHit hit;
            if (Physics.Raycast(from, Vector3.down, out hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                groundY = hit.point.y;
            Shader.SetGlobalFloat(GroundId, groundY);
        }

        void Create(Camera main)
        {
            _rtW = Mathf.Max(256, Mathf.RoundToInt(Screen.width * resolutionScale));
            _rtH = Mathf.Max(144, Mathf.RoundToInt(Screen.height * resolutionScale));
            if (_rt != null) { _rt.Release(); DestroyImmediate(_rt); }
            _rt = new RenderTexture(_rtW, _rtH, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            {
                name = "NkReflRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            _rt.Create();

            if (_refl == null)
            {
                var go = new GameObject("NkReflCam") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                _refl = go.AddComponent<Camera>();
                _refl.enabled = false;                      // rendered by hand, never by the pipeline
                _refl.clearFlags = CameraClearFlags.SolidColor;
                _refl.backgroundColor = Color.black;
                _refl.allowHDR = true;
                _refl.allowMSAA = false;
                _refl.useOcclusionCulling = false;
                _refl.depth = main.depth - 1f;
            }
            _refl.targetTexture = _rt;
        }

        void Render(Camera main)
        {
            _refl.CopyFrom(main);                 // fov, clip planes, culling mask, HDRP frame settings
            _refl.targetTexture = _rt;
            _refl.enabled = false;
            _refl.clearFlags = CameraClearFlags.SolidColor;

            // Mirror the world through the plane, then use the main camera's view: V' = V * R.
            // (A rotation cannot express this — reflecting flips handedness — which is why
            // worldToCameraMatrix is assigned explicitly rather than via the transform.)
            var refl = Matrix4x4.identity;
            refl.m11 = -1f;
            refl.m13 = 2f * groundY;
            _refl.worldToCameraMatrix = main.worldToCameraMatrix * refl;

            // Oblique near plane on the mirror so nothing under the road is rendered.
            _refl.projectionMatrix = main.projectionMatrix;
            var plane = CameraSpacePlane(_refl.worldToCameraMatrix, new Vector3(0f, groundY, 0f), Vector3.up, 1f);
            _refl.projectionMatrix = _refl.CalculateObliqueMatrix(plane);
            // culling must use the mirrored frustum too, not the (unmirrored) transform
            _refl.cullingMatrix = _refl.projectionMatrix * _refl.worldToCameraMatrix;

            var p = main.transform.position;
            _refl.transform.SetPositionAndRotation(
                new Vector3(p.x, 2f * groundY - p.y, p.z), main.transform.rotation);

            _vp = _refl.projectionMatrix * _refl.worldToCameraMatrix;
            Shader.SetGlobalTexture(TexId, _rt);
            Shader.SetGlobalMatrix(VpId, _vp);
            Shader.SetGlobalFloat(GroundId, groundY);
            Shader.SetGlobalFloat(OnId, 0f);      // road must not read the buffer it is drawing into
            Shader.SetGlobalFloat(PassId, 1f);
            _refl.Render();
            Shader.SetGlobalFloat(PassId, 0f);
            Shader.SetGlobalFloat(OnId, 1f);
        }

        /// Unity's classic CameraSpacePlane (Water4): the plane the oblique projection clips against,
        /// expressed in the mirror camera's space.
        static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 pos, Vector3 normal, float sideSign)
        {
            Vector3 cpos = worldToCamera.MultiplyPoint(pos);
            Vector3 cnormal = worldToCamera.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        /// Read the reflection buffer back to a PNG — the only way to see what the mirror camera saw.
        public static string Dump(string path)
        {
            if (I == null || I._rt == null) return "no reflection RT";
            var prev = RenderTexture.active;
            RenderTexture.active = I._rt;
            var tex = new Texture2D(I._rt.width, I._rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, I._rt.width, I._rt.height), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return "dumped " + I._rt.width + "x" + I._rt.height + " -> " + path;
        }
    }
}
