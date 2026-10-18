// The ported 'ctx' from src/engine/Engine.js: shared per-frame state all systems read/write,
// plus the fixed-step driver (JS Engine.start: 1/60 accumulator).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public class GameContext : MonoBehaviour
    {
        public static GameContext I; // set in Awake, cleared in OnDestroy

        // Mirrors the JS ctx.weather shared object.
        [System.Serializable]
        public class WeatherState
        {
            public float rain = 1f;
            public float wetness = 1f;
            public Vector2 wind = new Vector2(0.6f, -0.25f); // sim (x,z) components
            public float lightning;
        }

        public WeatherState weather = new WeatherState();
        public bool isShot;            // shot mode: deterministic camera, silent audio
        public string quality = "ultra";

        // ---- fixed-step driver ----
        const float Step = 1f / 60f;
        double _time;
        float _acc;
        readonly List<Action<float, double>> _updates = new List<Action<float, double>>();

        public double Time => _time;

        public void RegisterUpdate(string name, Action<float, double> fn) => _updates.Add(fn);

        // Manual tick for deterministic stepping from GameDebug (edit mode / oracles).
        public void Tick(float dt)
        {
            foreach (var u in _updates) u(dt, _time);
            _time += dt;
        }

        void Update()
        {
            _acc += UnityEngine.Time.deltaTime;
            if (_acc > 0.25f) _acc = 0.25f; // spiral-of-death clamp
            while (_acc >= Step)
            {
                Tick(Step);
                _acc -= Step;
            }
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        /// The scene has no Game object saved and the editor-only PlayMode bootstrap never
        /// called D.Ensure(), so I stayed null in Play: WeatherDriver then treats the rain
        /// as "1" every frame and the R rain override can never ease down (it re-reads its
        /// start value instead of accumulating). Guarantee the shared ctx exists.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (I != null) return;
            var go = new GameObject("Game");
            go.AddComponent<GameContext>();
        }
    }
}
