// Per-car look that Unity will not serialize on its own.
//
// A car is one shared mesh + one shared material; everything individual about it — paint
// colour, wheel spin, head/tail lamp level — is written per renderer through a
// MaterialPropertyBlock, and property blocks are runtime state: they are not saved with the
// scene and come back empty on the next load. Vehicles.Update re-applied them every frame,
// which is why Play looked right while the Editor showed every car black (paint 0,0,0 on the
// shared material) with dead lamps (_LightLv 0 kills the 38x headlamp and 3.2x taillight
// emission in VehicleCar.shader).
//
// This component stores those three values on the object so the scene is self-describing, and
// Apply() is the single place that pushes them back into a property block. Play keeps pushing
// every frame (brake lamps and wheel spin change); the Editor calls Apply() from
// NkEditorLighting so the Scene/Game view matches the Game view.
using UnityEngine;

namespace NeonKaido
{
    [DisallowMultipleComponent]
    public class CarVisual : MonoBehaviour
    {
        public Color paint = Color.gray;
        public float spin;
        public Vector3 lightLv;      // x = headlamp, y = position/tail lamps, z = brake
        public string type = "kei";  // model key, for beam/pool placement in the Editor
        public float length = 4.4f;  // manifest length, same reason

        MaterialPropertyBlock _mpb;

        public void Set(Color p, float s, Vector3 lv)
        {
            paint = p; spin = s; lightLv = lv;
        }

        /// Writes the stored look into this renderer's property block.
        public void Apply()
        {
            var r = GetComponent<MeshRenderer>();
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetColor("_PaintColor", paint);
            _mpb.SetFloat("_Spin", spin);
            _mpb.SetVector("_LightLv", lightLv);
            r.SetPropertyBlock(_mpb);
        }
    }
}
