// Gameplay facade: gigs + zones + settings + minimal HUD (port of src/systems/player/index.js gameplay half).
// Static like the other NeonKaido systems; PlayerController.Update drives it.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Gameplay
    {
        public static Gigs Gigs;
        public static bool Built;

        static ZoneDef _zone, _zoneCand;
        static float _zoneT;
        static float _playTime;
        static float _hp = 100f, _hurtT = 10f;

        public static Gigs.Prompt? Prompt;

        public static void Build(Transform parent)
        {
            if (Gigs != null && Gigs.Marker != null) { UnityEngine.Object.Destroy(Gigs.Marker); }
            Settings.Load();
            // sweep orphaned markers from earlier failed/partial builds
            foreach (var go in Object.FindObjectsByType<GameObject>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
                if (go.name == "gig-marker" && go.scene.IsValid()) Object.DestroyImmediate(go);
            var root = parent != null ? parent : new GameObject("Gameplay").transform;
            Gigs = new Gigs(root);
            _zone = default; _zoneCand = default; _zoneT = 0f;
            GameplayHud.Reset();
            GameplayHud.NewGigToast(Gigs.Current, silent: true);
            Built = true;
        }

        public static void Update(float dt, float t, Vector3 playerFeet)
        {
            if (!Built || Gigs == null || Gigs.Marker == null) Built = false; // scene reload self-heal
            if (!Built)
            {
                Build(null);
            }
            _playTime += dt;
            Gigs.Update(dt, t, playerFeet, "foot");

            // districts (JS: 0.7s candidate window before switching)
            var u = SimCoord.FromUnity(playerFeet);
            var z = Zones.At(u.x, u.y, u.z);
            if (z.Id != _zoneCand.Id) { _zoneCand = z; _zoneT = 0f; }
            _zoneT += dt;
            if (_zoneT > 0.7f && (z.Id != _zone.Id || z.Area != _zone.Area))
            {
                _zone = z;
                GameplayHud.ShowDistrict(z);
            }

            // health regen (fall damage wiring exists via player-land events; controller is spectator)
            _hurtT += dt;
            if (_hurtT > 4f && _hp < 100f) _hp = Mathf.Min(100f, _hp + dt * 6f);

            Prompt = _promptPend;
            _promptPend = null;
        }

        static Gigs.Prompt? _promptPend;
        public static void OfferPrompt(Gigs.Prompt p) => _promptPend = p;

        public static void TakeLift()
        {
            if (Prompt == null || Prompt.Value.Key != "F") return;
            var m = Prompt.Value.Target;
            var pc = Object.FindObjectOfType<PlayerController>();
            if (pc != null) pc.Teleport(m.x - 2f, m.y + 3f, m.z + 2f);
        }

        public static void CompleteGig() => Gigs?.Complete();

        public static string Describe()
        {
            if (Gigs == null) return "not built";
            var g = Gigs.Current;
            var z = _zone.Id != null ? _zone.Area : "?";
            return $"gig={g.Id} ({g.Title}) state={Gigs.State} eddies={Gigs.Eddies} cred={Gigs.Cred} done={Gigs.Completed} zone={z} hud={GameplayHud.Enabled}";
        }

        public static float Hp => _hp;
        public static float PlayTime => _playTime;
        public static ZoneDef CurrentZone => _zone.Id != null ? _zone : Zones.Fallback;
    }
}
