using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TLDRevamp.Multiplayer;
using UnityEngine;

namespace WorldMap
{
    /// Stores sampled global positions as explored trails. The network carries small deltas, never textures.
    ///
    /// Trails are kept by who walked them, not by session player ids: your own under SelfKey, everyone else's under
    /// their name ("p:<name>"). Ids restart in every session (a joiner is 1, then 2 after a reconnect, and 0 is the
    /// host's id and also yours alone), so trails kept by id mixed players up: a joiner's own single-player trail
    /// showed as the host's, the host's snapshot merged into the joiner's "You", and every reconnect started a new
    /// trail that was saved for good.
    internal static partial class MapSession
    {
        private sealed class Trail { public string Name; public Color Color; public readonly List<Vector3d> Points = new List<Vector3d>(); public readonly HashSet<long> Seen = new HashSet<long>(); }
        private sealed class Landmark { public string Key, Name; public Vector3d Pos; }
        internal const string SelfKey = "self";
        private static readonly Dictionary<string, Trail> Trails = new Dictionary<string, Trail>();
        /// Live markers of the other players, by their session id (cleared when they leave).
        private static readonly Dictionary<int, Vector3d> Positions = new Dictionary<int, Vector3d>();
        private static readonly List<Landmark> Landmarks = new List<Landmark>();
        private static readonly Queue<KeyValuePair<string, Action<RecordWriter>>> SnapshotQueue = new Queue<KeyValuePair<string, Action<RecordWriter>>>();
        private static float _nextSample, _nextSend;
        private static float _nextSave, _nextSnapshot, _nextDiscover;
        private static bool _open;
        private static bool _dirty;
        private static float _range = 3000f;
        private static double _panX, _panZ;
        private static string _worldKey = "unknown";
        private static int _seed = int.MinValue;
        private static Texture2D _texture;
        private static float _nextTexture, _lastTexture;
        private const float SampleDistance = 24f;
        /// Two points further apart than this are not joined by a line: a gap in the record (a teleport, a respawn,
        /// points that came in a snapshot after newer ones), not a road travelled.
        private const float BreakDistance = 250f;
        private const int MaxPoints = 30000, SaveEveryS = 30;
        private const int W = 960, H = 540;
        private static readonly Color32[] Pixels = new Color32[W * H];
        /// A new game's map, put in the start house a moment after the house is placed (MapItem's StartHouse patch).
        private static Vector3? _spawnAt; private static Quaternion _spawnRot; private static float _spawnTime;

        public static void Tick()
        {
            if (mainscript.s == null || mainscript.s.player == null || !mainscript.s.started) { Close(); return; }
            if (mainscript.s.pauseMenuOpen || kaposztaleves.s != null && kaposztaleves.s.isOpen) { Close(); return; }
            var player = mainscript.s.player;
            if (!IsHolding) Close();
            else if (!_open && !player.Bsitting) HandControls();
            var pos = mainscript.GlobalFromUnityPos(player.transform.position);
            EnsureWorld();
            SpawnStartMap();
            if (Time.unscaledTime >= _nextDiscover) { _nextDiscover = Time.unscaledTime + 5f; DiscoverLandmarks(pos); }
            if (_dirty && Time.unscaledTime >= _nextSave) Save();
            if (MpApi.IsHost && SnapshotQueue.Count > 0 && Time.unscaledTime >= _nextSnapshot)
            {
                _nextSnapshot = Time.unscaledTime + .15f;
                var packet = SnapshotQueue.Peek();
                // in order: a packet the rate limit refused goes again next time, not behind the later ones
                if (MpApi.SendEvent(Plugin.Mod, packet.Key, packet.Value, EventTarget.Others)) SnapshotQueue.Dequeue();
            }
            if (Time.unscaledTime >= _nextSample)
            {
                _nextSample = Time.unscaledTime + 1f;
                var t = Get(SelfKey, "You");
                if (t.Points.Count == 0 || Flat(t.Points[t.Points.Count - 1], pos) >= SampleDistance)
                {
                    Add(t, pos);
                    _dirty = true;
                    MpApi.SendEvent(Plugin.Mod, "explore", w => { w.Int(Quantize(pos.x)); w.Int(Quantize(pos.z)); }, EventTarget.Others);
                }
            }
            if (MpApi.InSession && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 1f;
                MpApi.SendEvent(Plugin.Mod, "position", w => { w.Int(Quantize(pos.x)); w.Int(Quantize(pos.z)); w.String(""); }, EventTarget.Others);
            }
        }

        private static int Quantize(double v) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(v)));
        private static Vector3d ReadPosition(RecordReader r) => new Vector3d(r.Int(), 0, r.Int());

        private static void EnsureWorld()
        {
            var map = menuhandler.s?.currentMainMap; if (map == null || map.seed == _seed) return;
            if (_dirty) Save();   // the old world's last points, before its trails are replaced
            _seed = map.seed;
            _worldKey = "seed-" + map.seed.ToString(CultureInfo.InvariantCulture);
            Load();
        }

        private static float Flat(Vector3d a, Vector3d b) { double x = a.x - b.x, z = a.z - b.z; return (float)Math.Sqrt(x * x + z * z); }

        /// A player's name as the session knows it ("Player 3" when it doesn't), safe for the route file.
        private static string PlayerName(int id)
        {
            foreach (var p in MpApi.Players)
                if (p.Id == id && !string.IsNullOrEmpty(p.Name)) return Clean(p.Name);
            return "Player " + id;
        }
        private static string MyName => MpApi.InSession ? PlayerName(MpApi.MyPlayerId) : null;
        private static string Clean(string s) { s = s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' '); return s.Length > 40 ? s.Substring(0, 40) : s; }
        private static string KeyOf(string name) => "p:" + name;

        private static Trail Get(string key, string name)
        {
            if (!Trails.TryGetValue(key, out var t))
                Trails[key] = t = new Trail { Name = name ?? key, Color = key == SelfKey ? new Color(0.95f, 0.8f, 0.25f) : PlayerColor(key) };
            return t;
        }
        private static Color PlayerColor(string key)
        {
            Color[] ink = { new Color(.32f,.43f,.40f), new Color(.51f,.34f,.29f),
                new Color(.36f,.40f,.51f), new Color(.42f,.42f,.30f) };
            int h = 0; foreach (char c in key) h = h * 31 + c;
            return ink[(h & int.MaxValue) % ink.Length];
        }

        public static void ReceivePosition(ModEvent e)
        {
            if (e.From == MpApi.MyPlayerId) return;
            var r = e.Data; var p = ReadPosition(r);
            if (!r.IsValid) return;
            Positions[e.From] = p;
        }

        /// Trails the host had when this player joined. Only the host sends them; your own trail as the host saw it
        /// joins yours.
        public static void ReceiveSnapshot(ModEvent e)
        {
            if (MpApi.IsHost || e.From != 0) return;
            EnsureWorld();
            var r = e.Data; string name = r.String(); int count = r.Byte();
            if (!r.IsValid || count > 96 || string.IsNullOrEmpty(name)) return;
            name = Clean(name);
            var t = name == MyName ? Get(SelfKey, "You") : Get(KeyOf(name), name);
            var points = new List<Vector3d>(count);
            for (int i = 0; i < count; i++) { var p = ReadPosition(r); if (!r.IsValid) return; points.Add(p); }
            foreach (var p in points) Add(t, p);
            _dirty = true;
        }

        public static void PlayerJoined(PlayerInfo player)
        {
            if (!MpApi.IsHost) return;
            SnapshotQueue.Clear();
            string me = MyName ?? "Host";
            foreach (var kv in Trails)
            {
                string name = kv.Key == SelfKey ? me : kv.Value.Name;
                for (int offset = 0; offset < kv.Value.Points.Count; offset += 96)
                {
                    int count = Math.Min(96, kv.Value.Points.Count - offset);
                    var points = kv.Value.Points.GetRange(offset, count).ToArray();
                    SnapshotQueue.Enqueue(new KeyValuePair<string, Action<RecordWriter>>("snapshot", w => { w.String(name); w.Byte((byte)count); foreach (var p in points) { w.Int(Quantize(p.x)); w.Int(Quantize(p.z)); } }));
                }
            }
            foreach (var item in Landmarks)
            {
                var saved = item;
                SnapshotQueue.Enqueue(new KeyValuePair<string, Action<RecordWriter>>("landmark", w => { w.String(saved.Key); w.String(saved.Name); w.Int(Quantize(saved.Pos.x)); w.Int(Quantize(saved.Pos.z)); }));
            }
        }
        public static void PlayerLeft(PlayerInfo player) => Positions.Remove(player.Id);
        public static void SessionEnded() { Positions.Clear(); SnapshotQueue.Clear(); if (_dirty) Save(); }
        public static void Quitting() { if (_dirty) Save(); }

        public static void ReceiveExplore(ModEvent e)
        {
            if (e.From == MpApi.MyPlayerId) return;
            EnsureWorld();
            var r = e.Data; var p = ReadPosition(r);
            if (!r.IsValid) return;
            string name = PlayerName(e.From);
            var t = Get(KeyOf(name), name);
            if (t.Points.Count == 0 || Flat(t.Points[t.Points.Count - 1], p) >= SampleDistance * .5f) { Add(t, p); _dirty = true; }
        }

        private static long PointKey(Vector3d p) => ((long)Quantize(p.x) << 32) ^ (uint)Quantize(p.z);
        private static bool AddTrailPoint(Trail t, Vector3d p) { long key = PointKey(p); if (!t.Seen.Add(key)) return false; t.Points.Add(p); return true; }

        /// A point added; past MaxPoints the oldest thousand go (also for snapshots and the route file).
        private static void Add(Trail t, Vector3d p)
        {
            if (!AddTrailPoint(t, p) || t.Points.Count <= MaxPoints) return;
            t.Points.RemoveRange(0, 1000);
            t.Seen.Clear(); foreach (var q in t.Points) t.Seen.Add(PointKey(q));
        }

        public static void ReceiveLandmark(ModEvent e)
        {
            if (e.From == MpApi.MyPlayerId) return;
            EnsureWorld();
            var r = e.Data; string key = r.String(), name = r.String(); var p = ReadPosition(r);
            if (!r.IsValid || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name) || Landmarks.Count > 5000) return;
            AddLandmark(key.Replace('|', '/'), Clean(name), p, false);
        }

        private static void DiscoverLandmarks(Vector3d player)
        {
            var map = menuhandler.s?.currentMainMap;
            if (map?.poiGens == null) return;
            foreach (var gen in map.poiGens)
            {
                if (gen?.chunks == null) continue;
                foreach (var chunk in gen.chunks.Values)
                {
                    if (chunk?.pois == null) continue;
                    foreach (var poi in chunk.pois.Values)
                    {
                        if (poi == null || string.IsNullOrEmpty(poi.poiName) || Flat(player, poi.pos) > 120f) continue;
                        string key = poi.id + ":" + Math.Round(poi.posID.x).ToString(CultureInfo.InvariantCulture) + ":" + Math.Round(poi.posID.z).ToString(CultureInfo.InvariantCulture);
                        AddLandmark(key, poi.poiName, poi.pos, true);
                    }
                }
            }
        }

        private static void AddLandmark(string key, string name, Vector3d pos, bool send)
        {
            if (Landmarks.Exists(x => x.Key == key)) return;
            if (name.Length > 80) name = name.Substring(0, 80);
            Landmarks.Add(new Landmark { Key = key, Name = name, Pos = pos }); _dirty = true;
            if (send) MpApi.SendEvent(Plugin.Mod, "landmark", w => { w.String(key); w.String(name); w.Int(Quantize(pos.x)); w.Int(Quantize(pos.z)); }, EventTarget.Others);
        }

        /// From the route file. Files of 0.2.0 kept trails by session id: 0 (yourself alone) becomes yours, the
        /// others "Player <id>".
        private static void Load()
        {
            Trails.Clear(); Positions.Clear(); Landmarks.Clear(); SnapshotQueue.Clear(); _dirty = false;
            var path = Path.Combine(Plugin.DataPath, _worldKey + ".txt");
            try
            {
                if (!File.Exists(path)) return;
                var ic = CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(path))
                {
                    var a = line.Split('|');
                    if (a.Length == 5 && a[0] == "P") { if (!Landmarks.Exists(x => x.Key == a[1])) Landmarks.Add(new Landmark { Key = a[1], Name = a[2], Pos = new Vector3d(double.Parse(a[3], ic), 0, double.Parse(a[4], ic)) }); continue; }
                    if (a.Length == 4 && a[0] == "T") { Add(TrailFor(a[1]), new Vector3d(double.Parse(a[2], ic), 0, double.Parse(a[3], ic))); continue; }
                    var old = line.Split(',');
                    if (old.Length == 3) Add(TrailFor(old[0]), new Vector3d(double.Parse(old[1], ic), 0, double.Parse(old[2], ic)));
                }
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Could not read map cache: " + e.Message); }
        }

        private static Trail TrailFor(string key)
        {
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return id == 0 ? Get(SelfKey, "You") : Get(KeyOf("Player " + id), "Player " + id);
            if (key == SelfKey) return Get(SelfKey, "You");
            return Get(key, key.StartsWith("p:") ? key.Substring(2) : key);
        }

        private static void Save()
        {
            _nextSave = Time.unscaledTime + SaveEveryS;
            try
            {
                var ic = CultureInfo.InvariantCulture;
                var path = Path.Combine(Plugin.DataPath, _worldKey + ".txt"); var tmp = path + ".tmp";
                using (var w = new StreamWriter(tmp, false))
                {
                    foreach (var kv in Trails)
                        foreach (var p in kv.Value.Points)
                        { w.Write("T|"); w.Write(kv.Key); w.Write('|'); w.Write(p.x.ToString("R", ic)); w.Write('|'); w.WriteLine(p.z.ToString("R", ic)); }
                    foreach (var p in Landmarks)
                        w.WriteLine("P|" + p.Key + "|" + p.Name.Replace('|', '/') + "|" + p.Pos.x.ToString("R", ic) + "|" + p.Pos.z.ToString("R", ic));
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _dirty = false;
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Could not save the map: " + e.Message); }
        }

        /// A new game (host or alone): one map in the start house, where the player starts (MapItem's patch).
        internal static void StartHousePlaced(Vector3 at, Quaternion rot) { _spawnAt = at; _spawnRot = rot; _spawnTime = Time.unscaledTime + 3f; NewGame(); }

        /// A new game starts with a blank map. The route file is kept by the world's seed (a save has no name of its
        /// own to keep it by), so a new game on a seed played before showed the old game's trails: that file is set
        /// aside first, renamed with the time, not deleted.
        private static void NewGame()
        {
            var map = menuhandler.s?.currentMainMap; if (map == null) return;
            if (_dirty) Save();
            _seed = map.seed;
            _worldKey = "seed-" + map.seed.ToString(CultureInfo.InvariantCulture);
            Trails.Clear(); Positions.Clear(); Landmarks.Clear(); SnapshotQueue.Clear(); _dirty = false;
            try
            {
                var path = Path.Combine(Plugin.DataPath, _worldKey + ".txt");
                if (!File.Exists(path)) return;
                int points = 0; foreach (var line in File.ReadAllLines(path)) if (line.StartsWith("T|")) points++;
                if (points < 5) File.Delete(path);   // nothing walked yet (the first moments of this same new game)
                else File.Move(path, Path.Combine(Plugin.DataPath, _worldKey + "-before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt"));
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Could not set the old map aside: " + e.Message); }
        }

        private static void SpawnStartMap()
        {
            if (_spawnAt == null || Time.unscaledTime < _spawnTime) return;
            var at = _spawnAt.Value; _spawnAt = null;
            var prefab = MapItemRegistration.Prefab;
            if (prefab == null || mainscript.s == null) return;
            try
            {
                var flat = _spawnRot * Vector3.forward; flat.y = 0f;
                var forward = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
                var go = UnityEngine.Object.Instantiate(prefab, at + forward * .6f + Vector3.up * 1.1f, Quaternion.LookRotation(Vector3.down, forward));
                go.SetActive(true);
                mainscript.s.PostSpawn(go);
                BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogInfo("World Map placed in the start house");
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Could not place the start map: " + e.Message); }
        }
    }
}
