using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TLDRevamp.Multiplayer;
using UnityEngine;

namespace WorldMap
{
    /// Stores sampled global positions as explored trails. The network carries small deltas, never textures.
    internal static partial class MapSession
    {
        private sealed class Trail { public string Name; public Color Color; public readonly List<Vector3d> Points = new List<Vector3d>(); public readonly HashSet<long> Seen = new HashSet<long>(); }
        private sealed class Landmark { public string Key, Name; public Vector3d Pos; }
        private static readonly Dictionary<int, Trail> Trails = new Dictionary<int, Trail>();
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
        private static Texture2D _texture;
        private static float _nextTexture;
        private const float SampleDistance = 24f;
        private const int W = 960, H = 540;
        private static readonly Color32[] Pixels = new Color32[W * H];

        public static void Tick()
        {
            if (mainscript.s == null || mainscript.s.player == null || !mainscript.s.started) { Close(); return; }
            if (mainscript.s.pauseMenuOpen || kaposztaleves.s != null && kaposztaleves.s.isOpen) { Close(); return; }
            var player=mainscript.s.player;
            var heldMap = MapItemRegistration.IsMap(player.inHandP) ? player.inHandP :
                MapItemRegistration.IsMap(player.pickedUp) ? player.pickedUp : null;
            bool holding = heldMap != null;
            if (holding && heldMap.GetComponent<HeldMapDisplay>() == null)
                heldMap.gameObject.AddComponent<HeldMapDisplay>();
            if (!holding) Close();
            else if (Input.GetKeyDown(KeyCode.M)) SetOpen(!_open);
            else if (!_open) HandControls();
            var pos = mainscript.GlobalFromUnityPos(mainscript.s.player.transform.position);
            EnsureWorld();
            if(Time.unscaledTime>=_nextDiscover){_nextDiscover=Time.unscaledTime+5f;DiscoverLandmarks(pos);}
            if (_dirty && Time.unscaledTime >= _nextSave) Save();
            if (MpApi.IsHost && SnapshotQueue.Count > 0 && Time.unscaledTime >= _nextSnapshot)
            {
                _nextSnapshot = Time.unscaledTime + .15f;
                var packet = SnapshotQueue.Dequeue();
                if (!MpApi.SendEvent(Plugin.Mod, packet.Key, packet.Value)) SnapshotQueue.Enqueue(packet);
            }
            if (Time.unscaledTime >= _nextSample)
            {
                _nextSample = Time.unscaledTime + 1f;
                string key = "seed-" + (menuhandler.s?.currentMainMap?.seed ?? 0).ToString(CultureInfo.InvariantCulture);
                if (key != _worldKey) { _worldKey = key; Load(); }
                var t = Get(MpApi.MyPlayerId, "You");
                if (t.Points.Count == 0 || Flat(t.Points[t.Points.Count - 1], pos) >= SampleDistance)
                {
                    AddTrailPoint(t,pos); if (t.Points.Count > 30000) { t.Seen.Clear(); t.Points.RemoveAt(0); foreach(var retained in t.Points)t.Seen.Add(PointKey(retained)); }
                    _dirty = true;
                    MpApi.SendEvent(Plugin.Mod, "explore", w => { w.Int(Quantize(pos.x)); w.Int(Quantize(pos.z)); });
                }
            }
            if (MpApi.InSession && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 1f;
                var p = mainscript.GlobalFromUnityPos(mainscript.s.player.transform.position);
                MpApi.SendEvent(Plugin.Mod, "position", w => { w.Int(Quantize(p.x)); w.Int(Quantize(p.z)); w.String("Player " + MpApi.MyPlayerId); });
            }
        }

        private static int Quantize(double v) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(v)));
        private static Vector3d ReadPosition(RecordReader r) => new Vector3d(r.Int(), 0, r.Int());
        private static void EnsureWorld()
        {
            var map=menuhandler.s?.currentMainMap; if(map==null)return;
            string key="seed-"+map.seed.ToString(CultureInfo.InvariantCulture);
            if(key!=_worldKey){_worldKey=key;Load();}
        }

        private static float Flat(Vector3d a, Vector3d b) { double x = a.x - b.x, z = a.z - b.z; return (float)Math.Sqrt(x*x + z*z); }
        private static Trail Get(int id, string name)
        {
            if (!Trails.TryGetValue(id, out var t)) Trails[id] = t = new Trail { Name = name ?? ("Player " + id), Color = id == MpApi.MyPlayerId ? new Color(0.95f, 0.8f, 0.25f) : PlayerColor(id) };
            if (!string.IsNullOrEmpty(name)) t.Name = name;
            return t;
        }
        private static Color PlayerColor(int id)
        {
            Color[] ink = { new Color(.32f,.43f,.40f), new Color(.51f,.34f,.29f),
                new Color(.36f,.40f,.51f), new Color(.42f,.42f,.30f) };
            return ink[(id & int.MaxValue) % ink.Length];
        }

        public static void ReceivePosition(ModEvent e)
        {
            EnsureWorld();
            var r=e.Data; var p=ReadPosition(r); string name=r.String();
            if (!r.IsValid) return;
            Get(e.From,name); Positions[e.From]=p;
        }
        public static void ReceiveSnapshot(ModEvent e)
        {
            EnsureWorld();
            var r=e.Data; int id=r.Int(); int count=r.Byte();
            if(!r.IsValid || count>96)return;
            var t=Get(id,id==MpApi.MyPlayerId?"You":"Player "+id);
            for(int i=0;i<count;i++){ var p=ReadPosition(r); if(!r.IsValid)return; AddTrailPoint(t,p); }
            if(!r.IsValid)return; _dirty=true;
        }
        public static void PlayerJoined(PlayerInfo player)
        {
            if(!MpApi.IsHost)return;
            SnapshotQueue.Clear();
            foreach(var kv in Trails)
                for(int offset=0;offset<kv.Value.Points.Count;offset+=96)
                {
                    int start=offset, count=Math.Min(96,kv.Value.Points.Count-start), id=kv.Key;
                    var points=kv.Value.Points.GetRange(start,count).ToArray();
                    SnapshotQueue.Enqueue(new KeyValuePair<string,Action<RecordWriter>>("snapshot",w=>{w.Int(id);w.Byte((byte)count);foreach(var p in points){w.Int(Quantize(p.x));w.Int(Quantize(p.z));}}));
                }
            foreach(var item in Landmarks)
            {
                var saved=item;
                SnapshotQueue.Enqueue(new KeyValuePair<string,Action<RecordWriter>>("landmark",w=>{w.String(saved.Key);w.String(saved.Name);w.Int(Quantize(saved.Pos.x));w.Int(Quantize(saved.Pos.z));}));
            }
        }
        public static void PlayerLeft(PlayerInfo player) => Positions.Remove(player.Id);
        public static void SessionEnded() => Positions.Clear();
        public static void ReceiveExplore(ModEvent e)
        {
            EnsureWorld();
            var r=e.Data; var p=ReadPosition(r);
            if (!r.IsValid) return;
            var t=Get(e.From,"Player "+e.From);
            if(t.Points.Count==0 || Flat(t.Points[t.Points.Count-1],p)>=SampleDistance*.5f) { AddTrailPoint(t,p); if(t.Points.Count>30000) { t.Seen.Clear();t.Points.RemoveAt(0);foreach(var retained in t.Points)t.Seen.Add(PointKey(retained)); } _dirty=true; }
        }

        private static long PointKey(Vector3d p) => ((long)Quantize(p.x)<<32) ^ (uint)Quantize(p.z);
        private static bool AddTrailPoint(Trail t,Vector3d p) { long key=PointKey(p); if(!t.Seen.Add(key))return false; t.Points.Add(p); return true; }

        public static void ReceiveLandmark(ModEvent e)
        {
            EnsureWorld();
            var r=e.Data; string key=r.String(),name=r.String(); var p=ReadPosition(r);
            if(!r.IsValid || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name))return;
            AddLandmark(key,name,p,false);
        }

        private static void DiscoverLandmarks(Vector3d player)
        {
            var map=menuhandler.s?.currentMainMap;
            if(map?.poiGens==null)return;
            foreach(var gen in map.poiGens)
            {
                if(gen?.chunks==null)continue;
                foreach(var chunk in gen.chunks.Values)
                {
                    if(chunk?.pois==null)continue;
                    foreach(var poi in chunk.pois.Values)
                    {
                        if(poi==null||string.IsNullOrEmpty(poi.poiName)||Flat(player,poi.pos)>120f)continue;
                        string key=poi.id+":"+Math.Round(poi.posID.x).ToString(CultureInfo.InvariantCulture)+":"+Math.Round(poi.posID.z).ToString(CultureInfo.InvariantCulture);
                        AddLandmark(key,poi.poiName,poi.pos,true);
                    }
                }
            }
        }

        private static void AddLandmark(string key,string name,Vector3d pos,bool send)
        {
            if(Landmarks.Exists(x=>x.Key==key))return;
            if(name.Length>80)name=name.Substring(0,80);
            Landmarks.Add(new Landmark{Key=key,Name=name,Pos=pos}); _dirty=true;
            if(send)MpApi.SendEvent(Plugin.Mod,"landmark",w=>{w.String(key);w.String(name);w.Int(Quantize(pos.x));w.Int(Quantize(pos.z));});
        }

        private static void Load()
        {
            Trails.Clear(); Positions.Clear(); Landmarks.Clear(); _dirty=false;
            var path=Path.Combine(Plugin.DataPath,_worldKey+".txt");
            try { if(!File.Exists(path)) return; foreach(var line in File.ReadAllLines(path)) { var a=line.Split('|'); if(a.Length==5&&a[0]=="P"){Landmarks.Add(new Landmark{Key=a[1],Name=a[2],Pos=new Vector3d(double.Parse(a[3],CultureInfo.InvariantCulture),0,double.Parse(a[4],CultureInfo.InvariantCulture))});continue;} if(a.Length==4&&a[0]=="T"){int id=int.Parse(a[1],CultureInfo.InvariantCulture);var t=Get(id,id==MpApi.MyPlayerId?"You":"Player "+id);AddTrailPoint(t,new Vector3d(double.Parse(a[2],CultureInfo.InvariantCulture),0,double.Parse(a[3],CultureInfo.InvariantCulture)));continue;} var old=line.Split(',');if(old.Length==3){int id=int.Parse(old[0],CultureInfo.InvariantCulture);var t=Get(id,id==MpApi.MyPlayerId?"You":"Player "+id);AddTrailPoint(t,new Vector3d(double.Parse(old[1],CultureInfo.InvariantCulture),0,double.Parse(old[2],CultureInfo.InvariantCulture)));} } }
            catch(Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Could not read map cache: "+e.Message); }
        }
        private static void Save()
        {
            try { var path=Path.Combine(Plugin.DataPath,_worldKey+".txt"); var tmp=path+".tmp"; using(var w=new StreamWriter(tmp,false)){foreach(var kv in Trails)foreach(var p in kv.Value.Points)w.WriteLine("T|"+kv.Key.ToString(CultureInfo.InvariantCulture)+"|"+p.x.ToString("R",CultureInfo.InvariantCulture)+"|"+p.z.ToString("R",CultureInfo.InvariantCulture));foreach(var p in Landmarks)w.WriteLine("P|"+p.Key+"|"+p.Name.Replace('|','/')+"|"+p.Pos.x.ToString("R",CultureInfo.InvariantCulture)+"|"+p.Pos.z.ToString("R",CultureInfo.InvariantCulture));} if(File.Exists(path))File.Delete(path); File.Move(tmp,path); _dirty=false; _nextSave=Time.unscaledTime+5f; }
            catch { _nextSave=Time.unscaledTime+5f; }
        }

    }
}
