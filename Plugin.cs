using System;
using BepInEx;
using HarmonyLib;
using TLDRevamp.Multiplayer;
using UnityEngine;

namespace WorldMap
{
    [BepInPlugin(Guid, "World Map", Version)]
    [BepInDependency("tldrevamp.core", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "tldrevamp.worldmap";
        public const string Version = "0.1.2";
        internal static ModInfo Mod;
        internal static string DataPath;

        private void Awake()
        {
            DataPath = System.IO.Path.Combine(Paths.ConfigPath, "WorldMap");
            System.IO.Directory.CreateDirectory(DataPath);
            Mod = MpApi.RegisterMod("worldmap", "World Map", Version);
            MpApi.OnEvent(Mod, "position", MapSession.ReceivePosition);
            MpApi.OnEvent(Mod, "explore", MapSession.ReceiveExplore);
            MpApi.OnEvent(Mod, "snapshot", MapSession.ReceiveSnapshot);
            MpApi.OnEvent(Mod, "landmark", MapSession.ReceiveLandmark);
            MpApi.PlayerJoined += MapSession.PlayerJoined;
            MpApi.PlayerLeft += MapSession.PlayerLeft;
            MpApi.SessionEnded += MapSession.SessionEnded;
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            var go = new GameObject("WorldMapRunner") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<MapRunner>();
            Logger.LogInfo("World Map loaded (multiplayer API " + (MpApi.Available ? "available" : "offline mode") + ")");
        }
    }

    public sealed class MapRunner : MonoBehaviour
    {
        private void Update() => MapSession.Tick();
        private void OnGUI() => MapSession.Draw();
    }
}
