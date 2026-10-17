// OnGUI HUD for the gameplay loop: gig objective card, eddies/cred, zone banner, toasts, prompt.
// JS HUD is DOM-based; this is the Unity stand-in (visible in the editor Game view during play).
using UnityEngine;

namespace NeonKaido
{
    public static class GameplayHud
    {
        public static bool Enabled = true;

        static string _toastHead, _toastBody;
        static float _toastT;          // seconds remaining
        static string _zoneText;
        static float _zoneT;           // seconds remaining
        static float _newGigT;         // "NEW GIG" banner
        static GigDef _curGig;
        static GUIStyle _big, _small, _title, _sub;

        public static void Reset()
        {
            _toastT = 0; _zoneT = 0; _newGigT = 0; _toastHead = _toastBody = null; _zoneText = null;
        }

        public static void Toggle() { Enabled = !Enabled; }

        public static void NewGigToast(GigDef g, bool silent = false)
        {
            _curGig = g;
            _newGigT = 3.5f;
            if (!silent)
            {
                _toastHead = "NEW GIG · " + g.Fixer;
                _toastBody = g.Title + " — " + g.Text;
                _toastT = 4.5f;
            }
        }

        public static void GigCompleteToast(GigDef g, int eddies)
        {
            _toastHead = "GIG COMPLETE";
            _toastBody = g.Title + "  ·  +" + g.Reward + " eddies  (total " + eddies + ")";
            _toastT = 4.5f;
            _newGigT = 0;
        }

        public static void ShowDistrict(ZoneDef z)
        {
            _zoneText = z.District + " — " + z.Area + "  " + z.Jp;
            _zoneT = 3.2f;
        }

        public static void Draw(Gigs gigs, Vector3 playerFeet)
        {
            if (!Enabled || gigs == null) return;
            EnsureStyles();
            var g = gigs.Current;
            bool active = gigs.State == "active";
            var cam = Camera.main;
            float W = Screen.width, H = Screen.height;

            // top-left: current gig objective card
            if (active && g.Title != null)
            {
                var m = gigs.Marker != null ? gigs.Marker.transform.position : Vector3.zero;
                float dist = gigs.Marker != null ? Vector2.Distance(new Vector2(playerFeet.x, playerFeet.z), new Vector2(m.x, m.z)) : 0f;
                GUI.Label(new Rect(18, 16, 640, 30), "◤ GIG · " + g.Fixer, _title);
                GUI.Label(new Rect(34, 46, 640, 28), g.Title + "  ·  " + g.Area, _big);
                GUI.Label(new Rect(34, 74, 640, 24), g.Text + (active ? "  —  " + dist.ToString("0") + " m" : ""), _small);
            }

            // top-right: wallet
            GUI.Label(new Rect(W - 320, 16, 300, 28), "eddies " + gigs.Eddies.ToString("N0"), _big);
            GUI.Label(new Rect(W - 320, 44, 300, 22), "street cred " + gigs.Cred + "  ·  " + gigs.Completed + " gigs", _small);

            // bottom-center: interaction prompt
            if (Gameplay.Prompt != null)
            {
                var p = Gameplay.Prompt.Value;
                var txt = "[" + p.Key + "]  " + p.Text;
                var sz = _big.CalcSize(new GUIContent(txt));
                var r = new Rect(W / 2 - sz.x / 2, H - 140, sz.x + 30, sz.y + 8);
                GUI.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.75f);
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 15, r.y, sz.x, sz.y), txt, _big);
            }

            // zone banner (top-center)
            if (_zoneT > 0 && _zoneText != null)
            {
                _zoneT -= Time.deltaTime;
                float a = Mathf.Clamp01(_zoneT / 0.6f);
                var c = _big.normal.textColor; _big.normal.textColor = new Color(1f, 0.886f, 0.35f, a);
                var sz = _big.CalcSize(new GUIContent(_zoneText));
                GUI.Label(new Rect(W / 2 - sz.x / 2, H * 0.14f, sz.x + 10, sz.y), _zoneText, _big);
                _big.normal.textColor = c;
            }

            // toast (bottom-left)
            if (_toastT > 0 && _toastHead != null)
            {
                _toastT -= Time.deltaTime;
                float a = Mathf.Clamp01(_toastT / 0.5f);
                var c1 = _title.normal.textColor; _title.normal.textColor = new Color(c1.r, c1.g, c1.b, a);
                var c2 = _small.normal.textColor; _small.normal.textColor = new Color(c2.r, c2.g, c2.b, a);
                GUI.Label(new Rect(18, H - 110, 700, 26), _toastHead, _title);
                GUI.Label(new Rect(18, H - 84, 700, 24), _toastBody, _small);
                _title.normal.textColor = c1; _small.normal.textColor = c2;
            }
        }

        static void EnsureStyles()
        {
            if (_big != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(1f, 0.65f, 0.25f);
            _big = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _big.normal.textColor = new Color(1f, 0.886f, 0.35f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _small.normal.textColor = new Color(0.9f, 0.9f, 0.95f);
            _sub = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        }
    }
}
