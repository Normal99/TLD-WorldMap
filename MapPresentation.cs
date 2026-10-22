using System;
using System.Collections.Generic;
using TLDRevamp.Multiplayer;
using UnityEngine;

namespace WorldMap
{
    internal static partial class MapSession
    {
        private static readonly Color32[] PaperPixels = new Color32[W * H];
        private static bool _paperReady, _dragging;
        private static Texture2D _paper, _frame, _ink, _button, _buttonHover;
        private static GUIStyle _titleStyle, _bodyStyle, _smallStyle, _hudStyle, _buttonStyle;
        private static readonly Color32 RouteInk = new Color32(89, 86, 69, 255);
        private static readonly Color32 SelfInk = new Color32(156, 69, 47, 255);
        private static readonly Color32 PlaceInk = new Color32(112, 98, 58, 255);

        internal static bool IsOpen => _open;
        internal static bool IsHolding
        {
            get
            {
                var player = mainscript.s != null ? mainscript.s.player : null;
                return player != null && (MapItemRegistration.IsMap(player.pickedUp) || MapItemRegistration.IsMap(player.inHandP));
            }
        }

        private static void SetOpen(bool open)
        {
            _open = open;
            _dragging = false;
            if (open)
            {
                if (mainscript.s != null) mainscript.s.crsrLocked = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        internal static void SetOpenForTest(bool open) => SetOpen(open);
        internal static void SaveForTest() => Save();
        internal static string TrailList()
        {
            var rows = new List<string>();
            foreach (var kv in Trails) rows.Add(kv.Key + "=" + kv.Value.Points.Count);
            foreach (var kv in Positions) rows.Add("marker " + PlayerName(kv.Key));
            return string.Join("; ", rows);
        }
        internal static void ZoomForTest(float factor) => Zoom(factor);
        internal static void PanForTest(double dx, double dz) { _panX += dx; _panZ += dz; Invalidate(); }

        /// For Probe: the map's state and how much ink its picture has.
        internal static string Describe()
        {
            int ink = 0;
            if (_texture != null) for (int i = 0; i < Pixels.Length; i += 7) if (!Pixels[i].Equals(PaperPixels[i])) ink++;
            int mine = Trails.TryGetValue(SelfKey, out var t) ? t.Points.Count : 0;
            return "world " + _worldKey + " trails " + Trails.Count + " my points " + mine + " landmarks " + Landmarks.Count +
                " positions " + Positions.Count + " range " + _range + " pan " + _panX.ToString("0") + "," + _panZ.ToString("0") + " ink " + ink;
        }

        internal static void Close()
        {
            if (_open) SetOpen(false);
        }

        internal static void Toggle() => SetOpen(!_open);

        /// A menu or the inventory over the game: no map controls drawn over it.
        private static bool MenuOpen
        {
            get
            {
                try { return mainscript.s == null || mainscript.s.pauseMenuOpen || mainscript.s.SomeMenuOpen() || kaposztaleves.s != null && kaposztaleves.s.isOpen; }
                catch { return false; }
            }
        }

        /// The picture again soon, but at most 20 times a second: every mouse-drag event rebuilt the whole 960×540
        /// picture, several times a frame while dragging.
        private static void Invalidate() => _nextTexture = Mathf.Min(_nextTexture, _lastTexture + .05f);

        private static void HandControls()
        {
            bool moved = false;
            double step = _range * .18;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { _panX -= step; moved = true; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { _panX += step; moved = true; }
            if (Input.GetKeyDown(KeyCode.UpArrow)) { _panZ += step; moved = true; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { _panZ -= step; moved = true; }
            if (Input.GetKeyDown(Plugin.ZoomIn.Value) || Input.GetKeyDown(KeyCode.KeypadPlus)) { Zoom(1f / 1.5f); moved = true; }
            if (Input.GetKeyDown(Plugin.ZoomOut.Value) || Input.GetKeyDown(KeyCode.KeypadMinus)) { Zoom(1.5f); moved = true; }
            if (Input.GetKeyDown(Plugin.Center.Value)) { _panX = _panZ = 0; moved = true; }
            if (moved) Invalidate();
        }

        private static void Zoom(float factor)
        {
            _range = Mathf.Clamp(_range * factor, 350f, 20000f);
            Invalidate();
        }

        private static Texture2D Solid(Color32 color)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }

        private static void EnsureStyle()
        {
            if (_paper != null) return;
            _paper = Solid(new Color32(224, 208, 169, 255));
            _frame = Solid(new Color32(71, 67, 55, 255));
            _ink = Solid(new Color32(123, 105, 73, 255));
            _button = Solid(new Color32(190, 173, 133, 255));
            _buttonHover = Solid(new Color32(209, 191, 149, 255));
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _titleStyle.normal.textColor = new Color(0.96f, 0.91f, 0.77f);
            _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            _bodyStyle.normal.textColor = new Color(0.22f, 0.21f, 0.18f);
            _smallStyle = new GUIStyle(_bodyStyle) { fontSize = 12 };
            _hudStyle = new GUIStyle(_smallStyle);
            _hudStyle.normal.textColor = new Color(0.94f, 0.89f, 0.74f);
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            _buttonStyle.normal.background = _button;
            _buttonStyle.hover.background = _buttonHover;
            _buttonStyle.active.background = _ink;
            _buttonStyle.normal.textColor = new Color(0.20f, 0.19f, 0.16f);
            _buttonStyle.hover.textColor = _buttonStyle.normal.textColor;
            _buttonStyle.active.textColor = Color.white;
        }

        internal static void DrawHud(bool holding)
        {
            if (!holding || _open || MenuOpen) return;
            EnsureStyle();
            int oldDepth = GUI.depth;
            GUI.depth = -100;
            float width = Mathf.Min(330f, Screen.width - 32f);
            // top right: the game's own key hints are on the left and bottom, RevaMP's overlay top left
            float x = Screen.width - width - 16f, y = 16f;
            bool seated = mainscript.s.player.Bsitting;
            GUI.DrawTexture(new Rect(x, y, width, seated ? 50 : 68), _frame);
            GUI.Label(new Rect(x + 12, y + 2, width - 24, 24), "MAP CONTROLS", _titleStyle);
            GUI.Label(new Rect(x + 12, y + 27, width - 24, 18), MapKeyName() + "  FULL VIEW" + (seated ? "" : "   ·   ARROWS  PAN"), _hudStyle);
            if (!seated)
                GUI.Label(new Rect(x + 12, y + 45, width - 24, 18), KeyName(Plugin.ZoomIn.Value) + " / " + KeyName(Plugin.ZoomOut.Value) + "  ZOOM   ·   " + KeyName(Plugin.Center.Value) + "  CENTER", _hudStyle);
            GUI.depth = oldDepth;
        }

        private static string KeyName(KeyCode k) => k == KeyCode.Equals ? "=" : k == KeyCode.Minus ? "-" : k.ToString().ToUpperInvariant();

        /// The game's own map key, as the player has it bound.
        private static string MapKeyName()
        {
            try
            {
                var keys = settingsscript.s.S.inputs[(int)inputscript.IN.map].keys;
                if (keys != null && keys.Length > 0 && keys[0] != null && keys[0].key != KeyCode.None) return KeyName(keys[0].key);
            }
            catch { }
            return "M";
        }

        public static void Draw()
        {
            if (!_open || mainscript.s == null || mainscript.s.player == null || mainscript.s.pauseMenuOpen) return;
            EnsureStyle();
            int oldDepth = GUI.depth;
            GUI.depth = -100;
            float w = Mathf.Min(1050f, Screen.width - 20f);
            float h = Mathf.Min(730f, Screen.height - 20f);
            var outer = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
            float header = h < 450f ? 38f : 52f, footer = h < 450f ? 51f : 67f;
            GUI.DrawTexture(new Rect(outer.x - 3, outer.y - 3, w + 6, h + 6), _frame);
            GUI.DrawTexture(outer, _paper);
            GUI.DrawTexture(new Rect(outer.x, outer.y, w, header), _frame);
            GUI.Label(new Rect(outer.x + 17, outer.y + 2, 250, header - 4), "WORLD MAP", _titleStyle);
            GUI.Label(new Rect(outer.x + w - 235, outer.y + 3, 220, header - 6), "THE LONG DRIVE   /   N ↑", _hudStyle);

            var slot = new Rect(outer.x + 14, outer.y + header + 10, w - 28, h - header - footer - 20);
            float ratio = W / (float)H;
            var map = slot.width / slot.height > ratio
                ? new Rect(slot.x + (slot.width - slot.height * ratio) * .5f, slot.y, slot.height * ratio, slot.height)
                : new Rect(slot.x, slot.y + (slot.height - slot.width / ratio) * .5f, slot.width, slot.width / ratio);
            GUI.DrawTexture(new Rect(map.x - 2, map.y - 2, map.width + 4, map.height + 4), _ink);
            GUI.DrawTexture(map, GetTexture());
            HandleMapMouse(map);
            DrawMapLabels(map);

            float fy = outer.y + h - footer;
            GUI.DrawTexture(new Rect(outer.x, fy, w, 1), _ink);
            GUI.Label(new Rect(outer.x + 15, fy + 5, w - 25, 21),
                "DRAG TO PAN   ·   WHEEL TO ZOOM   ·   " + MapKeyName() + " OR ESC TO CLOSE", _smallStyle);
            float bx = outer.x + w - 282, by = fy + footer - 32;
            if (GUI.Button(new Rect(bx, by, 68, 26), "CENTER", _buttonStyle)) { _panX = _panZ = 0; Invalidate(); }
            if (GUI.Button(new Rect(bx + 74, by, 29, 26), "−", _buttonStyle)) Zoom(1.5f);
            GUI.Label(new Rect(bx + 106, by, 75, 26), (_range * 2 / 1000f).ToString("0.#") + " km", _bodyStyle);
            if (GUI.Button(new Rect(bx + 183, by, 29, 26), "+", _buttonStyle)) Zoom(1f / 1.5f);
            if (GUI.Button(new Rect(bx + 218, by, 64, 26), "CLOSE", _buttonStyle)) Close();
            GUI.Label(new Rect(outer.x + 15, fy + footer - 31, Mathf.Max(0, w - 315), 25),
                "YOU  ·  VISITED PLACE  ·  EXPLORED ROUTE", _smallStyle);
            GUI.depth = oldDepth;
        }

        private static void HandleMapMouse(Rect map)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && map.Contains(e.mousePosition))
            {
                Zoom(e.delta.y > 0 ? 1.22f : 1f / 1.22f);
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && map.Contains(e.mousePosition))
            {
                _dragging = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                double metresPerPixel = 2.0 * _range / map.height;
                _panX -= e.delta.x * metresPerPixel;
                _panZ += e.delta.y * metresPerPixel;
                Invalidate();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _dragging)
            {
                _dragging = false;
                e.Use();
            }
        }

        private static void DrawMapLabels(Rect map)
        {
            var me = mainscript.GlobalFromUnityPos(mainscript.s.player.transform.position);
            var center = new Vector3d(me.x + _panX, me.y, me.z + _panZ);
            float scale = H / (_range * 2f);
            var occupied = new List<Vector2>();
            DrawLabel("You", me, center, map, scale, occupied);
            foreach (var kv in Positions) DrawLabel(PlayerName(kv.Key), kv.Value, center, map, scale, occupied);
            foreach (var place in Landmarks) DrawLabel(place.Name, place.Pos, center, map, scale, occupied);
        }

        private static void DrawLabel(string label, Vector3d p, Vector3d center, Rect map, float scale, List<Vector2> occupied)
        {
            float px = W * .5f + (float)(p.x - center.x) * scale;
            float py = H * .5f + (float)(p.z - center.z) * scale;
            if (px < 0 || px >= W || py < 0 || py >= H) return;
            var at = new Vector2(map.x + px * map.width / W + 7,
                map.y + (H - py) * map.height / H - 13);
            foreach (var other in occupied)
                if (Mathf.Abs(at.x - other.x) < 80f && Mathf.Abs(at.y - other.y) < 20f) return;
            occupied.Add(at);
            GUI.Label(new Rect(at.x, at.y, 145, 21), label, _smallStyle);
        }

        internal static Texture2D GetTexture()
        {
            if (_texture == null) _texture = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            if (Time.unscaledTime < _nextTexture) return _texture;
            _nextTexture = Time.unscaledTime + .3f; _lastTexture = Time.unscaledTime;
            PreparePaper();
            Array.Copy(PaperPixels, Pixels, Pixels.Length);
            if (mainscript.s == null || mainscript.s.player == null) return _texture;
            var me = mainscript.GlobalFromUnityPos(mainscript.s.player.transform.position);
            var center = new Vector3d(me.x + _panX, me.y, me.z + _panZ);
            float scale = H / (_range * 2f);
            foreach (var kv in Trails)
            {
                var t = kv.Value;
                var color = kv.Key == SelfKey ? RouteInk : (Color32)t.Color;
                for (int i = 1; i < t.Points.Count; i++)
                {
                    var a = t.Points[i - 1]; var b = t.Points[i];
                    if (Flat(a, b) > BreakDistance || Math.Abs(a.x - center.x) > _range * 2 || Math.Abs(a.z - center.z) > _range * 2) continue;
                    Line((int)(W * .5 + (a.x - center.x) * scale), (int)(H * .5 + (a.z - center.z) * scale),
                         (int)(W * .5 + (b.x - center.x) * scale), (int)(H * .5 + (b.z - center.z) * scale), color, 2);
                }
            }
            foreach (var kv in Positions)
            {
                var color = Trails.TryGetValue(KeyOf(PlayerName(kv.Key)), out var t) ? (Color32)t.Color : (Color32)PlayerColor(KeyOf(PlayerName(kv.Key)));
                Marker(kv.Value, center, scale, color, 6);
            }
            foreach (var place in Landmarks) Marker(place.Pos, center, scale, PlaceInk, 4);
            Marker(me, center, scale, SelfInk, 6);
            PrintScale();
            _texture.SetPixels32(Pixels);
            _texture.Apply(false);
            return _texture;
        }

        private static void PreparePaper()
        {
            if (_paperReady) return;
            _paperReady = true;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int grain = (((x / 3) * 73856093) ^ ((y / 3) * 19349663)) & 7;
                int edge = Math.Min(Math.Min(x, W - 1 - x), Math.Min(y, H - 1 - y));
                int crease = Math.Min(Math.Abs(x - W / 3), Math.Min(Math.Abs(x - W * 2 / 3), Math.Abs(y - H / 2)));
                int shade = (edge < 17 ? (17 - edge) / 3 : 0) + (crease < 3 ? 10 - crease * 3 : 0);
                PaperPixels[y * W + x] = new Color32((byte)(224 + grain - shade),
                    (byte)(210 + grain - shade), (byte)(174 + grain - shade), 255);
            }
            var grid = new Color32(194, 184, 154, 255);
            for (int x = 72; x < W; x += 90) for (int y = 0; y < H; y += 5) PaperPixels[y * W + x] = grid;
            for (int y = 72; y < H; y += 90) for (int x = 0; x < W; x += 5) PaperPixels[y * W + x] = grid;
            var border = new Color32(125, 114, 87, 255);
            for (int x = 8; x < W - 8; x++) { PaperPixels[8 * W + x] = border; PaperPixels[(H - 9) * W + x] = border; }
            for (int y = 8; y < H - 8; y++) { PaperPixels[y * W + 8] = border; PaperPixels[y * W + W - 9] = border; }
            var ink = new Color32(98, 89, 66, 255);
            PrintText(PaperPixels, "FIELD MAP", 32, H - 56, 3, ink);
            PrintText(PaperPixels, "EXPLORED ROUTES", 34, H - 73, 1, ink);
            PrintText(PaperPixels, "N", W - 66, H - 35, 2, ink);
            for (int y = H - 92; y < H - 45; y++) PaperPixels[y * W + W - 61] = ink;
            for (int i = 0; i < 13; i++)
            {
                PaperPixels[(H - 45 - i) * W + W - 61 - i] = ink;
                PaperPixels[(H - 45 - i) * W + W - 61 + i] = ink;
            }
        }

        private static void Marker(Vector3d p, Vector3d center, float scale, Color32 ink, int radius)
        {
            double px = W * .5 + (p.x - center.x) * scale, py = H * .5 + (p.z - center.z) * scale;
            if (px < 8 || px >= W - 8 || py < 8 || py >= H - 8) return;
            Dot((int)px, (int)py, new Color32(238, 225, 188, 255), radius + 2);
            Dot((int)px, (int)py, ink, radius);
            Dot((int)px, (int)py, new Color32(250, 239, 202, 255), 2);
        }

        private static void Dot(int x, int y, Color32 color, int radius)
        {
            for (int oy = -radius; oy <= radius; oy++) for (int ox = -radius; ox <= radius; ox++)
                if (ox * ox + oy * oy <= radius * radius) Put(x + ox, y + oy, color);
        }

        private static void Put(int x, int y, Color32 color)
        {
            if (x >= 0 && x < W && y >= 0 && y < H) Pixels[y * W + x] = color;
        }

        private static void Line(int x0, int y0, int x1, int y1, Color32 color, int width)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int n = 0; n < 8000; n++)
            {
                Dot(x0, y0, color, width);
                if (x0 == x1 && y0 == y1) break;
                int e = 2 * err;
                if (e >= dy) { err += dy; x0 += sx; }
                if (e <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
