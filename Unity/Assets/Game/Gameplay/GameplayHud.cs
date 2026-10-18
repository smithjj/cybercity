// NEON KAIDO in-game HUD — port of src/ui/hud.js + hud.css (+src/ui/minimap.js) to IMGUI.
// Cyberpunk-2077-style overlay: status stack, compass strip, minimap, objective tracker,
// eddies wallet, crosshair, interact prompt, waypoint, district pop-up, toasts,
// bottom slots + stance. All positioned for a 1920x1080 reference, scaled by zoom.
using UnityEngine;

namespace NeonKaido
{
    public static class GameplayHud
    {
        // runtime API kept from the previous HUD so call sites stay unchanged
        public static bool Enabled = true;

        struct Toast { public string K, H, L1, L2; public bool Info; public float T; }
        static readonly System.Collections.Generic.List<Toast> _toasts = new();

        static string _zoneDistrict, _zoneArea, _zoneJp, _zoneTint;
        static float _zoneT;
        static float _deltaT; static long _deltaVal;   // eddies payout flash


        // clock runs 4× real time from 23:47 (hud.js)
        static double _clock = 23 * 60 + 47;
        static float _fpsAcc; static int _fpsN; static int _fpsShown = 60;

        // styles
        static Texture2D _white;
        static GUIStyle _mono10, _tinyC, _tinyR, _lblBig, _lblSm, _numBig, _numLg, _amber, _cyan, _whiteTxt, _titleA, _districtName, _districtArea;
        static GUIStyle _speedNum, _speedLbl;

        static float Z => Mathf.Clamp(Screen.height / 1080f, 0.55f, 2.2f);

        // ---------- palette (hud.css) ----------
        const string HxA = "#fcee0a", HxR = "#ff6158", HxC = "#5ef6ff", HxW = "#e9f6f7", HxHp = "#ff5a4e";
        static readonly Color KY = Hex(HxA), KR = Hex(HxR), KC = Hex(HxC), KW = Hex(HxW), KHP = Hex(HxHp);
        static readonly Color KYa = new(0.988f, 0.929f, 0.039f, 0.55f), KRa = new(1f, 0.382f, 0.345f, 0.55f), KCa = new(0.369f, 0.965f, 1f, 0.55f);
        static readonly Color KDark = new(0.043f, 0.008f, 0.016f, 0.55f), KToast = new(0.157f, 0.031f, 0.039f, 0.92f);

        static Color Hex(string hx)
        {
            var c = UnityEngine.ColorUtility.TryParseHtmlString(hx, out var col) ? col : Color.magenta;
            return new Color(c.r, c.g, c.b, 1f);
        }
        static Color A(Color c, float a) => new(c.r, c.g, c.b, c.a * a);

        // ---------- shared ----------
        public static void Reset()
        {
            _toasts.Clear();
            _clock = 23 * 60 + 47;
            _deltaT = 0; _deltaVal = 0; _zoneT = 0;
            Enabled = true;
        }
        public static void Toggle() { Enabled = !Enabled; }

        public static void NewGigToast(GigDef g, bool silent = false)
        {
            if (silent) return;
            AddToast("GIG DOWNLOADED // " + g.Area, "NEW GIG: " + g.Title, g.Text, null, info: false, 6f);
        }

        public static void Note(string h, string l1)
        {
            AddToast("SYS//KBK-09", h, l1, null, info: true, 3.5f);
        }

        /// Dispatch-app toast (NX-9 delivery): yellow gig-style card, 5 s.
        public static void DispatchToast(string h, string l1)
        {
            AddToast("NX-9 // DISPATCH", h, l1, null, info: false, 5f);
        }

        public static double ClockMinutes => _clock;
        public static string ClockString
        {
            get
            {
                int m = Mathf.FloorToInt((float)(_clock % 1440.0));
                if (m < 0) m += 1440;
                return (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
            }
        }

        public static void GigCompleteToast(GigDef g, int eddies)
        {
            AddToast(g.Fixer + " // GIG COMPLETE", g.Title,
                "<b>EDDIES</b><c:>+ " + eddies.ToString("N0") + " €$</c>   <b>CRED</b><c:>+" + g.Cred + "</c>",
                null, info: false, 6f);
            _deltaVal = eddies; _deltaT = 3.2f;
        }

        public static void ShowDistrict(ZoneDef z)
        {
            _zoneDistrict = z.District; _zoneArea = z.Area; _zoneJp = z.Jp; _zoneTint = z.Tint;
            _zoneT = 4.6f;
        }

        static void AddToast(string k, string h, string l1, string l2, bool info, float t)
        {
            _toasts.Add(new Toast { K = k, H = h, L1 = l1, L2 = l2, Info = info, T = t });
            if (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        static string StripTags(string s)
        {
            // tiny parser for <b>..</b> <c:>..</c:> markup used in toast lines
            return s == null ? "" : s.Replace("<b>", "").Replace("</b>", "").Replace("<c:>", "").Replace("</c>", "");
        }

        // ===================================================== DRAW =====================================================
        public static void Draw(Gigs gigs, Vector3 playerFeet)
        {
            var pc = PlayerController.I;
            Draw(gigs, playerFeet, pc);
        }
        public static void Draw(Gigs gigs, Vector3 playerFeet, PlayerController pc)
        {
            float dt = Time.unscaledDeltaTime * 0.25f;   // JS clock advances game-min = real-s / 4
            _clock += dt;
            if (!Enabled || gigs == null) { DrawNone(); return; }
            EnsureStyles();

            // fps meter (real time, 0.5s window)
            _fpsN++;
            if (_fpsAcc > 0.5f)
            {
                _fpsShown = Mathf.RoundToInt(_fpsN / _fpsAcc);
                _fpsAcc = 0; _fpsN = 0;
            }
            if (_deltaT > 0) _deltaT -= dt;
            if (_zoneT > 0) _zoneT -= dt;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i]; t.T -= dt; _toasts[i] = t;
                if (t.T <= 0) _toasts.RemoveAt(i);
            }

            var g = gigs.Current;
            bool active = gigs.State == "active";
            var cam = Camera.main;
            float W = Screen.width, H = Screen.height, z = Z;
            float zoom = Z; // reference 1920x1080 → scaled units
            var fwd = pc != null && pc.GetComponentInChildren<Camera>() != null ? pc.GetComponentInChildren<Camera>().transform.forward : Vector3.forward;
            float heading = (Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + 360f) % 360f;  // 0 = +Z = north
            Vector3 targetPos = TargetWorld(gigs, g);
            float targetDist = targetPos.sqrMagnitude > 0 ? Vector2.Distance(new Vector2(playerFeet.x, playerFeet.z), new Vector2(targetPos.x, targetPos.z)) : 0f;
            bool rain = true;
            float rainNow = WeatherDriver.RainNow;

            DrawStatus(pc, zoom);        // upper-left stack (health/armor/ram/netwatch/optics)
            DrawCompass(heading, targetPos, targetDist, zoom);   // heading strip
            Minimap.Draw(rainNow, _clock, gigs);   // upper-right (geometry prepared in Update)
            DrawObjective(gigs, g, active, targetDist, zoom);   // right tracker
            DrawEddies(gigs, zoom);      // wallet
            DrawCrosshair(pc, zoom);
            DrawPrompt(zoom);
            DrawWaypoint(cam, targetPos, targetDist, zoom);
            DrawDistrict(zoom);
            DrawToasts(zoom);
            DrawBottom(pc, zoom);
            if (NeonKaido.Driving.Active) DrawVehicle(pc, zoom);
        }

        static Vector3 TargetWorld(Gigs gigs, GigDef g)
        {
            if (gigs.Marker == null) return Vector3.zero;
            return gigs.Marker.transform.position;
        }

        // ----------------------------------------------------------------
        // Upper-left status stack — health bar, armor/ram chips, netwatch/optics
        // ----------------------------------------------------------------
        static void DrawStatus(PlayerController pc, float z)
        {
            int hp = 100, armor = 214, ram = 8, maxRam = 8;
            float x = 34 * z, y = 30 * z, w = 420 * z;

            // head
            Label(new Rect(x, y, w, 14 * z), "HEALTH INDICATOR", _tinyR);
            Label(new Rect(x + 128 * z, y, w, 14 * z), "// SYS.OK · KIROSHI MK.III", _tinyC);
            y += 20 * z;

            // level box + hp bar + hp number
            var levelRect = new Rect(x, y, 40 * z, 40 * z);
            Fill(levelRect, A(KC, 0.18f));
            Border(levelRect, A(KC, 0.6f), (int)(z));
            Label(new Rect(x, y + 9 * z, 40 * z, 22 * z), "12", _numBig);
            //
            float bx = x + 50 * z, bw = 246 * z, bh = 12 * z;
            Fill(new Rect(bx, y + 12 * z, bw, bh), new Color(0.235f, 0.04f, 0.047f, 0.65f));
            Fill(new Rect(bx, y + 12 * z, bw * (hp / 100f), bh), A(KHP, 0.95f));
            for (int i = 1; i < 11; i++) Fill(new Rect(bx + i * 22 * z, y + 12 * z, 1.6f * z, bh), new Color(0.04f, 0f, 0f, 0.85f));
            Label(new Rect(bx, y + 26 * z, bw, 14 * z), "BIOMON · NOMINAL", _tinyR);
            Label(new Rect(bx + bw - 62 * z, y + 26 * z, bw, 14 * z), "ARMOR " + armor, _tinyR);
            Label(new Rect(bx + bw + 10 * z, y + 4 * z, 70 * z, 30 * z), hp + "/100", _numLg);
            y += 58 * z;

            // RAM cells
            Label(new Rect(x + 50 * z, y, 34 * z, 16 * z), "RAM", _cyan);
            for (int i = 0; i < maxRam; i++)
            {
                var c = i < ram ? A(KC, 1f) : A(KC, 0.15f);
                var r = new Rect(x + 84 * z + i * 20 * z, y, 18 * z, 9 * z);
                Fill(r, c);
            }
            Label(new Rect(x + 84 * z + maxRam * 20 * z + 8 * z, y - 2 * z, 48 * z, 16 * z), ram + "/" + maxRam, _cyan);
            y += 18 * z;

            // buffs
            Chip(new Rect(x + 50 * z, y, 158 * z, 18 * z), "NETWATCH: CLEAR", KY);
            Chip(new Rect(x + 214 * z, y, 132 * z, 18 * z), "OPTICS ONLINE", KC);
        }

        static void Chip(Rect r, string txt, Color c)
        {
            Fill(r, A(c, 0.06f));
            Border(r, A(c, 0.45f), 1);
            var st = _mono10;
            st.normal.textColor = c;
            st.fontSize = (int)(10f * Z);
            st.alignment = TextAnchor.MiddleCenter;
            var old = GUI.color; GUI.color = Color.white;
            GUI.Label(r, txt, st);
            st.alignment = TextAnchor.UpperLeft;
        }

        // ----------------------------------------------------------------
        // Compass strip (560px wide, 4px/deg, ticks 5°, cards & numeral labels)
        // ----------------------------------------------------------------
        static void DrawCompass(float heading, Vector3 target, float tdist, float z)
        {
            float stripW = 560 * z, x0 = Screen.width / 2f - stripW / 2f, y = 28 * z;
            string[] cards = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int d = -360; d <= 720; d += 5)
            {
                int dd = ((d % 360) + 360) % 360;
                float rel = (((dd - heading + 180f) % 360f) + 360f) % 360f - 180f;
                float px = Screen.width / 2f + rel * 4 * z;
                if (Mathf.Abs(rel) > 68) continue;
                float a = Mathf.Clamp01(1f - Mathf.Abs(rel) / 78f);
                // cardinal labels
                if (dd % 45 == 0)
                {
                    bool cardn = dd % 90 == 0;
                    var c = dd == 0 ? KY : cardn ? KW : KR;
                    var st = cardn ? _lblBig : _cyan;
                    st.normal.textColor = A(St2(c), a);
                    st.fontSize = (int)((cardn ? 17 : 15) * z);
                    Label(new Rect(px - 20 * z, y - 2 * z, 40 * z, 20 * z), cards[dd / 45], st);
                }
                else if (dd % 15 == 0)
                {
                    var st = _cyan; st.normal.textColor = A(KR, 0.6f * a); st.fontSize = (int)(11 * z);
                    Label(new Rect(px - 18 * z, y + 0 * z, 36 * z, 14 * z), dd.ToString(), st);
                }
                // tick
                var tickC = A(KR, ((dd % 15 == 0) ? 0.8f : 0.55f) * a);
                float thick = dd % 15 == 0 ? 10 * z : 6 * z;
                Fill(new Rect(px - 0.5f * z, y + (dd % 15 == 0 ? 10f : 14f) * z, 1 * z, thick), tickC);
            }
            // base line
            Fill(new Rect(x0 + stripW * 0.1f, y + 26 * z, stripW * 0.8f, 1 * z), A(KR, 0.6f));
            // amber caret marker at center (diamond standing in for the CSS triangle)
            var caret = new Rect(Screen.width / 2f - 4 * z, y + 21 * z, 8 * z, 8 * z);
            var mvC = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, caret.center);
            Fill(caret, KY);
            GUI.matrix = mvC;
            // heading readout "000° N"
            var card2 = cards[Mathf.RoundToInt(heading / 45f) % 8];
            _mono10.normal.textColor = A(KR, 0.7f);
            _mono10.fontSize = (int)(11 * z);
            Label(new Rect(Screen.width / 2f - 60 * z, y + 50 * z, 120 * z, 16 * z), ((int)heading % 360).ToString("000") + "°  " + card2, _mono10);

            // gig marker on the strip
            if (target.sqrMagnitude > 0 && PlayerController.I != null)
            {
                var fwdv = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
                float bt = Mathf.Atan2(target.x - PlayerController.I.transform.position.x, target.z - PlayerController.I.transform.position.z) * Mathf.Rad2Deg;
                float rel = ((bt - heading + 540f) % 360f) - 180f;
                bool vis = Mathf.Abs(rel) < 68f;
                float clamped = Mathf.Clamp(rel, -68f, 68f);
                float gxp = Screen.width / 2f + clamped * 4 * z;
                GUI.color = Color.white;
                var dm = new Rect(gxp - 5.5f * z, y - 8 * z, 11 * z, 11 * z);
                var mv = GUI.matrix; GUI.matrix = Matrix4x4.identity; GUIUtility.RotateAroundPivot(45, dm.center); GUI.matrix *= Matrix4x4.identity;
                GUIUtility.RotateAroundPivot(45f, dm.center);
                Fill(dm, vis ? KY : A(KY, 0.55f));
                GUI.matrix = mv;
                _cyan.fontSize = (int)(11 * z);
                _cyan.normal.textColor = KY;
                Label(new Rect(gxp - 30 * z, y + 12 * z, 60 * z, 14 * z), FmtDist(tdist), _cyan);
            }
        }

        static Color St2(Color c) => c;
        static GUIStyle _tinyC2() => _mono10;

        // ----------------------------------------------------------------
        // Objective tracker (right, below minimap)
        // ----------------------------------------------------------------
        static void DrawObjective(Gigs gigs, GigDef g, bool active, float dist, float z)
        {
            float w = 390 * z, x0 = Screen.width - 34 * z - w, y = 380 * z;
            if (active && !string.IsNullOrEmpty(g.Id))
            {
                Fill(new Rect(x0, y, w, 96 * z), new Color(0.047f, 0.012f, 0.02f, 0.4f));
                Fill(new Rect(x0, y + 4 * z, 2 * z, 88 * z), KY);
                _mono10.fontSize = (int)(10 * z); _mono10.normal.textColor = A(KY, 0.65f);
                Label(new Rect(x0 + 12 * z, y + 4 * z, w, 14 * z), "GIG · FIXER: " + g.Fixer, _mono10);
                _amber.fontSize = (int)(21 * z);
                Label(new Rect(x0 + 12 * z, y + 18 * z, w - 20 * z, 26 * z), g.Title, _amber);
                // line: diamond + first text clause
                var dmr = new Rect(x0 + 12 * z, y + 46 * z, 12 * z, 12 * z);
                var mv = GUI.matrix; GUIUtility.RotateAroundPivot(45f, dmr.center);
                Border(dmr, KY, 2); GUI.matrix = mv;
                Fill(new Rect(dmr.center.x - 2.6f * z, dmr.center.y - 2.6f * z, 5.2f * z, 5.2f * z), KY);
                _whiteTxt.fontSize = (int)(15 * z);
                var txt = g.Text;
                if (txt.Length > 58) txt = txt.Substring(0, 58) + "…";
                Label(new Rect(x0 + 30 * z, y + 44 * z, w - 44 * z, 22 * z), txt, _whiteTxt);
                _amber.fontSize = (int)(13 * z);
                Label(new Rect(x0 + 26 * z, y + 68 * z, w, 16 * z), FmtDist(dist) + " · " + g.Area, _amber);
            }
        }

        // ----------------------------------------------------------------
        // Eddies (upper right under tower)
        // ----------------------------------------------------------------
        static long _eddiesShown = 1200;
        static void DrawEddies(Gigs gigs, float z)
        {
            long target = gigs.Eddies;
            _eddiesShown += (long)((target - _eddiesShown) * Mathf.Min(1f, Time.unscaledDeltaTime * 3f));
            if (Mathf.Abs(target - _eddiesShown) < 1) _eddiesShown = target;
            float y = 26 * z + (315) * z;
            var x0 = Screen.width - 34 * z - 130 * z;
            _amber.fontSize = (int)(20 * z);
            Label(new Rect(x0, y, 130 * z, 26 * z), "€$ " + _eddiesShown.ToString("N0"), _amber);
            if (_deltaT > 0)
            {
                float a = Mathf.Clamp01(_deltaT);
                _cyan.fontSize = (int)(14 * z);
                _cyan.normal.textColor = A(KC, a);
                Label(new Rect(x0, y + 24 * z, 130 * z, 18 * z), "+" + _deltaVal.ToString("N0"), _cyan);
                _cyan.normal.textColor = KC;
            }
        }

        // ----------------------------------------------------------------
        // Crosshair + interact prompt + waypoint
        // ----------------------------------------------------------------
        static void DrawCrosshair(PlayerController pc, float z)
        {
            if (NeonKaido.Driving.Active) return;
            var fly = pc != null && !pc.WalkMode;
            if (fly) return;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            float k = (pc != null && pc.SprintGait ? 1.35f : 1f);
            Fill(new Rect(cx - 1 * z, cy - 1 * z, 2 * z, 2 * z), new Color(1f, 1f, 1f, 0.85f));
            Color tick = new(1, 1, 1, 0.85f);
            Fill(new Rect(cx - 0.5f * z, cy - 12 * z * k, 1 * z, 6 * z * k), tick);
            Fill(new Rect(cx - 0.5f * z, cy + 6 * z * k, 1 * z, 6 * z * k), tick);
            Fill(new Rect(cx - 12 * z * k, cy - 0.5f * z, 6 * z * k, 1 * z), tick);
            Fill(new Rect(cx + 6 * z * k, cy - 0.5f * z, 6 * z * k, 1 * z), tick);
        }

        static void DrawPrompt(float z)
        {
            string pKey, pText, pTag;
            var p = Gameplay.Prompt;
            if (p != null)
            {
                pKey = p.Value.Key; pText = p.Value.Text; pTag = "INTERACT";
            }
            else if (NeonKaido.Driving.Prompt != null)
            {
                pKey = "F"; pText = NeonKaido.Driving.Prompt;
                pTag = NeonKaido.Driving.PromptName ?? "VEHICLE";
            }
            else return;
            float cx = Screen.width / 2f + 70 * z, cy = Screen.height / 2f + 36 * z;
            _mono10.fontSize = (int)(10 * z); _mono10.normal.textColor = A(KC, 0.7f);
            Label(new Rect(cx, cy, 320 * z, 14 * z), pTag, _mono10);
            // amber opt row: key chip + text
            var rowH = 36 * z;
            RowAccent(new Rect(cx - 10 * z, cy + 16 * z, 300 * z, rowH));
            var key = new Rect(cx - 4 * z, cy + 22 * z, 26 * z, 24 * z);
            var old = _mono10.fontSize;
            _mono10.fontSize = (int)(13 * z); _mono10.alignment = TextAnchor.MiddleCenter; _mono10.fontStyle = FontStyle.Bold;
            _mono10.normal.textColor = new Color(0.07f, 0.04f, 0f);
            Label(key, pKey, _mono10);
            _mono10.alignment = TextAnchor.UpperLeft; _mono10.fontStyle = FontStyle.Normal;
            _mono10.normal.textColor = new Color(0.07f, 0.04f, 0f); _mono10.fontSize = (int)(16 * z);
            Label(new Rect(cx + 30 * z, cy + 24 * z, 250 * z, 26 * z), pText, _mono10);
            _mono10.fontSize = old;
        }

        static void RowAccent(Rect r)
        {
            Fill(new Rect(r.x, r.y, 4, r.height), KY);
            Fill(new Rect(r.x + 4, r.y, r.width - 4, r.height), new Color(0.988f, 0.929f, 0.039f, 0.95f));
            // notch bottom right
            var mv = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(r.x + r.width - 8, r.y + r.height - 4));
            RectFill(new Rect(r.x + r.width - 30, r.y + r.height - 30, 44, 44), new Color(0, 0, 0, 0));
            GUI.matrix = mv;
        }

        static void DrawWaypoint(Camera cam, Vector3 target, float dist, float z)
        {
            if (target.sqrMagnitude <= 0 || cam == null) return;
            var vp = cam.WorldToViewportPoint(target);
            bool vis = vp.z > 0.5f;
            float sx = vp.x * Screen.width, sy = (1 - vp.y) * Screen.height;
            bool edge = false;
            if (!vis) { // project on edge: flip
                sx = Screen.width - sx; sy = Screen.height - sy; }
            if (sx < 60 * z || sx > Screen.width - 60 * z || sy < 60 * z || sy > Screen.height - 60 * z)
            {
                edge = true;
                float cx = Screen.width / 2f, cy = Screen.height / 2f;
                var d = new Vector2(sx - cx, sy - cy);
                if (d.sqrMagnitude < 1) d = Vector2.right;
                float hmax = Screen.width / 2f - 80 * z, vmax = Screen.height / 2f - 80 * z;
                float k = Mathf.Min(Mathf.Abs(hmax / d.x), Mathf.Abs(vmax / d.y));
                sx = cx + d.x * k; sy = cy + d.y * k;
            }
            if (!vis && !edge) return;
            GUI.color = new Color(1, 1, 1, vis ? 1 : 0.75f);
            var dmr = new Rect(sx - 11 * z, sy - 11 * z, 22 * z, 22 * z);
            var mv = GUI.matrix; GUIUtility.RotateAroundPivot(45f, dmr.center);
            Border(dmr, KY, 2);
            GUI.matrix = mv;
            _amber.fontSize = (int)(13 * z); _amber.alignment = TextAnchor.MiddleCenter;
            Label(dmr, "!", _amber);
            _amber.alignment = TextAnchor.UpperLeft;
            _amber.fontSize = (int)(14 * z);
            Label(new Rect(sx - 40 * z, sy + 14 * z, 80 * z, 16 * z), FmtDist(dist), _amber);
            GUI.color = Color.white;
        }

        // ----------------------------------------------------------------
        // District pop-up + toasts
        // ----------------------------------------------------------------
        static void DrawDistrict(float z)
        {
            if (_zoneT <= 0 || _zoneDistrict == null) return;
            float a = Mathf.Clamp01(Mathf.Min(_zoneT / 0.8f, (4.6f - _zoneT) / 0.6f));
            float cx = Screen.width / 2f, y = Screen.height * 0.19f;
            _mono10.fontSize = (int)(12 * z); _mono10.normal.textColor = A(KC, 0.8f * a);
            Label(new Rect(cx - 200 * z, y - 6 * z, 400 * z, 16 * z), "ENTERING", _mono10);
            // big name with chromatic fake slices
            _districtName.fontSize = (int)(58 * z);
            var r = new Rect(cx - 400 * z, y + 8 * z, 800 * z, 72 * z);
            _districtName.normal.textColor = A(KR, 0.8f * a);
            GUI.Label(new Rect(r.x - 3 * z, r.y, r.width, r.height), _zoneDistrict, _districtName);
            _districtName.normal.textColor = A(KC, 0.8f * a);
            GUI.Label(new Rect(r.x + 3 * z, r.y, r.width, r.height), _zoneDistrict, _districtName);
            _districtName.normal.textColor = A(KW, a);
            Label(r, _zoneDistrict, _districtName);
            // rule lines + area
            var tcol = _zoneTint == "cyan" ? KC : _zoneTint == "pink" ? new Color(1f, 0.5f, 0.78f) : KR;
            _districtArea.fontSize = (int)(22 * z);
            _districtArea.normal.textColor = A(tcol, a);
            var ar = new Rect(cx - 260 * z, y + 86 * z, 520 * z, 30 * z);
            Label(ar, _zoneArea, _districtArea);
            float w = Mathf.Min(240 * z, ar.x);
            Fill(new Rect(cx - 260 * z - w - 14 * z, y + 96 * z, w, 1), A(KY, a));
            Fill(new Rect(cx + 264 * z, y + 96 * z, w, 1), A(KY, a));
            _tinyR.fontSize = (int)(15 * z); _tinyR.normal.textColor = A(KR, 0.9f * a);
            Label(new Rect(cx - 200 * z, y + 118 * z, 400 * z, 20 * z), _zoneJp ?? "", _tinyR);
        }

        static void DrawToasts(float z)
        {
            float x0 = Screen.width - 34 * z - 380 * z;
            float y = 560 * z;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                float a = Mathf.Clamp01(Mathf.Min(t.T / 0.6f, (6f - t.T) / 0.45f));
                var bgc = t.Info ? new Color(0.05f, 0.09f, 0.1f, 0.92f) : KToast;
                Fill(new Rect(x0, y, 380 * z, 86 * z), bgc);
                Fill(new Rect(x0, y, 4, 86 * z), t.Info ? KC : KY);
                Border(new Rect(x0, y, 380 * z, 86 * z), A(KR, 0.35f * a), 1);
                _mono10.fontSize = (int)(10 * z); _mono10.normal.textColor = A(t.Info ? KC : KC, 0.8f * a);
                Label(new Rect(x0 + 22 * z, y + 8 * z, 340 * z, 14 * z), t.K, _mono10);
                var hc = t.Info ? KC : KY;
                _lblBig.fontStyle = FontStyle.Bold; _lblBig.fontSize = (int)(20 * z); _lblBig.normal.textColor = A(hc, a);
                Label(new Rect(x0 + 20 * z, y + 22 * z, 340 * z, 26 * z), t.H, _lblBig);
                _whiteTxt.fontSize = (int)(15 * z); _whiteTxt.normal.textColor = A(KW, a);
                Label(new Rect(x0 + 20 * z, y + 50 * z, 356 * z, 30 * z), StripTags(t.L1), _whiteTxt);
                if (t.L2 != null) Label(new Rect(x0 + 20 * z, y + 68 * z, 356 * z, 20 * z), StripTags(t.L2), _whiteTxt);
                y += 96 * z;
            }
        }

        // ----------------------------------------------------------------
        // Bottom-left slots + bottom-right stance
        // ----------------------------------------------------------------
        static void DrawBottom(PlayerController pc, float z)
        {
            float x = 34 * z, y = Screen.height - 30 * z - 54 * z;
            var fly = pc != null && !pc.WalkMode;
            Slot(x, y, fly, "V", "VIEW");
            Slot(x + 62 * z, y, NeonKaido.Driving.Active || NeonKaido.Driving.Prompt != null
                || (Gameplay.Prompt.HasValue && Gameplay.Prompt.Value.Key == "F"), "F",
                NeonKaido.Driving.Active ? "EXIT" : "VEHICLE");
            Slot(x + 124 * z, y, !Enabled, "H", "HUD");
            Slot(x + 186 * z, y, NeonKaido.Delivery.PhoneOpen || NeonKaido.Delivery.Pending, "P", "NX-9");
            _tinyC2().fontSize = (int)(10 * z); _tinyC2().normal.textColor = A(KR, 0.65f);
            // status line above the slot row (below the row it collided with the slot labels)
            Label(new Rect(x, Screen.height - 118 * z, 900 * z, 14 * z),
                  "CONNECTION 2077.11.04 · NODE KBK-09 · " + _fpsShown + " FPS · G SHOWROOM · P NX-9", _tinyC2());

            // stance (bottom-right)
            if (pc != null && pc.WalkMode && !NeonKaido.Driving.Active)
            {
                string kind = pc.Crouched ? "CROUCHED · STEALTH" : pc.SprintGait ? "SPRINTING" : "STANDING";
                var c = pc.Crouched ? KC : KR;
                var st = _cyan; st.fontSize = (int)(13 * z); st.normal.textColor = c; st.alignment = TextAnchor.MiddleRight;
                var sw = 220 * z;
                var r = new Rect(Screen.width - 34 * z - sw, Screen.height - 42 * z, sw - 34 * z, 22 * z);
                Label(r, kind, st);
                StanceIcon(Screen.width - 44 * z, Screen.height - 44 * z, c, pc.Crouched, z);
                st.alignment = TextAnchor.UpperLeft;
            }
        }

        // Bottom-right driving cluster: car name, km/h, gear, speed bar, key hints.
        static void DrawVehicle(PlayerController pc, float z)
        {
            float x = Screen.width - 34 * z;
            float y = Screen.height - 34 * z;
            float barW = 300 * z;

            _tinyC.fontSize = (int)(10 * z);
            _tinyC.alignment = TextAnchor.MiddleRight;
            _tinyC.normal.textColor = A(KC, 0.6f);
            Label(new Rect(x - 470 * z, y - 14 * z, 470 * z, 14 * z),
                "F EXIT   ·   R RESET   ·   SPACE HANDBRAKE", _tinyC);

            _cyan.fontSize = (int)(13 * z);
            _cyan.alignment = TextAnchor.MiddleRight;
            _cyan.normal.textColor = A(KW, 0.92f);
            Label(new Rect(x - barW, y - 148 * z, barW, 18 * z),
                NeonKaido.Driving.CarName ?? "VEHICLE", _cyan);

            _speedNum.fontSize = (int)(62 * z);
            Label(new Rect(x - barW, y - 134 * z, barW, 72 * z),
                Mathf.RoundToInt(NeonKaido.Driving.SpeedKmh).ToString("0"), _speedNum);

            _speedLbl.fontSize = (int)(12 * z);
            Label(new Rect(x - barW, y - 60 * z, barW, 16 * z), "KM/H", _speedLbl);

            var gr = new Rect(x - barW - 58 * z, y - 126 * z, 48 * z, 48 * z);
            Fill(gr, A(KY, 0.10f)); Border(gr, A(KY, 0.85f), 1);
            _numBig.fontSize = (int)(26 * z); _numBig.normal.textColor = KY;
            Label(new Rect(gr.x, gr.y + 7 * z, gr.width, 30 * z),
                NeonKaido.Driving.Reverse ? "R" : NeonKaido.Driving.Gear.ToString(), _numBig);
            _numBig.normal.textColor = KC;

            var back = new Rect(x - barW, y - 42 * z, barW, 3 * z);
            Fill(back, A(KC, 0.18f));
            float k = Mathf.Clamp01(NeonKaido.Driving.SpeedKmh / 220f);
            Fill(new Rect(x - barW, y - 42 * z, barW * k, 3 * z), KY);

            _cyan.alignment = TextAnchor.UpperLeft;
            _tinyC.alignment = TextAnchor.UpperLeft;
        }

        static void Slot(float x, float y, bool act, string key, string lb)
        {
            var r = new Rect(x, y, 54 * Z, 54 * Z);
            Color c = act ? KY : new Color(0.369f, 0.965f, 1f, 0.65f);
            Fill(r, act ? A(KY, 0.08f) : new Color(0.078f, 0.02f, 0.027f, 0.55f));
            Border(r, act ? KY : A(KC, 0.45f), 1);
            // icon approximated: eye → 2 rects, car → glyph, hud → corners
            _cyan.fontSize = (int)(17 * Z); _cyan.alignment = TextAnchor.MiddleCenter; _cyan.normal.textColor = c;
            string glyph = key == "V" ? "◉" : key == "F" ? "⌁" : key == "P" ? "▯" : "▣";
            Label(new Rect(x, y + 14 * Z, 54 * Z, 26 * Z), glyph, _cyan);
            _cyan.alignment = TextAnchor.UpperLeft;
            _cyan.fontSize = (int)(12 * Z); _cyan.normal.textColor = A(c, 0.9f);
            Label(new Rect(x, y + 58 * Z, 54 * Z, 16 * Z), key, _cyan);
            _cyan.fontSize = (int)(11 * Z);
            Label(new Rect(x - 3 * Z, y + 72 * Z, 62 * Z, 14 * Z), lb, _cyan);
        }

        static void StanceIcon(float cx, float cy, Color c, bool crouch, float z)
        {
            // tiny stick figure: head, body, legs
            float s = z;
            Fill(new Rect(cx - 3 * s, cy - 16 * s, 6 * s, 6 * s), c);           // head
            if (crouch)
            {
                Fill(new Rect(cx - 4 * s, cy - 8 * s, 8 * s, 8 * s), c);       // body
                Fill(new Rect(cx - 7 * s, cy + 0, 10 * s, 3 * s), c);          // legs
            }
            else
            {
                Fill(new Rect(cx - 3 * s, cy - 9 * s, 6 * s, 12 * s), c);      // body
                Fill(new Rect(cx - 4 * s, cy + 3 * s, 2.5f * s, 9 * s), c);    // legs
                Fill(new Rect(cx + 1.5f * s, cy + 3 * s, 2.5f * s, 9 * s), c);
            }
        }

        // ===================================================== helpers =====================================================
        static void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _white != null ? _white : Texture2D.whiteTexture);
            GUI.color = old;
        }
        static void RectFill(Rect r, Color c) => Fill(r, c);
        static void Border(Rect r, Color c, int th)
        {
            Fill(new Rect(r.x, r.y, r.width, th), c);
            Fill(new Rect(r.x, r.yMax - th, r.width, th), c);
            Fill(new Rect(r.x, r.y, th, r.height), c);
            Fill(new Rect(r.xMax - th, r.y, th, r.height), c);
        }
        static void Label(Rect r, string s, GUIStyle st)
        {
            if (!string.IsNullOrEmpty(s)) GUI.Label(r, s, st);
        }
        static string FmtDist(float m) => m >= 1000 ? (m / 1000f).ToString("0.0") + "KM" : Mathf.RoundToInt(m) + "M";

        static void DrawNone()
        {
            // HUD hidden: nothing (H toggles Enabled)
        }

        static void EnsureStyles()
        {
            if (_white == null) { _white = new Texture2D(4, 4, TextureFormat.RGBA32, false); var cs = new Color[16]; for (int i = 0; i < 16; i++) cs[i] = Color.white; _white.SetPixels(cs); _white.Apply(); }
            if (_mono10 != null) return;
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GUIStyle MakeFS(int size, Color c, FontStyle fs = FontStyle.Normal, TextAnchor an = TextAnchor.UpperLeft)
            { var s = new GUIStyle { font = f, fontSize = size, alignment = an, fontStyle = fs, richText = true }; s.normal.textColor = c; s.stretchWidth = false; return s; }
            _mono10 = MakeFS(10, A(KC, 0.7f), FontStyle.Normal);
            _tinyC = MakeFS(10, A(KC, 0.55f));
            _tinyR = MakeFS(10, A(KR, 0.75f));
            _lblBig = MakeFS(15, KW, FontStyle.Bold);
            _lblSm = MakeFS(17, KW);
            _numBig = MakeFS(20, KC, FontStyle.Bold, TextAnchor.MiddleCenter);
            _numLg = MakeFS(26, KHP, FontStyle.Bold);
            _amber = MakeFS(14, KY, FontStyle.Bold);
            _cyan = MakeFS(13, KC, FontStyle.Normal);
            _whiteTxt = MakeFS(15, KW);
            _titleA = MakeFS(21, KY, FontStyle.Bold);
            _districtName = MakeFS(58, KW, FontStyle.Bold, TextAnchor.MiddleCenter);
            _districtArea = MakeFS(22, KY, FontStyle.Bold, TextAnchor.MiddleCenter);
            _speedNum = MakeFS(62, KY, FontStyle.Bold, TextAnchor.MiddleRight);
            _speedLbl = MakeFS(12, A(KC, 0.85f), FontStyle.Normal, TextAnchor.MiddleRight);
        }
    }
}
