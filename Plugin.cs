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
        public const string Version = "0.2.0";
        internal static ModInfo Mod;
        internal static string DataPath;
        /// Hand keys (standing with the map out). Not Page Up / Page Down: TLD RevaMP's overlay and debug report
        /// are on those, so zooming wrote a report each time.
        internal static BepInEx.Configuration.ConfigEntry<KeyCode> ZoomIn, ZoomOut, Center;

        private void Awake()
        {
            ZoomIn = Config.Bind("Keys", "ZoomIn", KeyCode.Equals, "Zoom the map in your hand in (standing; the larger view opens with the game's map key, M by default)");
            ZoomOut = Config.Bind("Keys", "ZoomOut", KeyCode.Minus, "Zoom the map in your hand out");
            Center = Config.Bind("Keys", "Center", KeyCode.Home, "Center the map in your hand on you again");
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
            Application.quitting += MapSession.Quitting;   // the last points of a drive, saved
            Logger.LogInfo("World Map loaded (multiplayer API " + (MpApi.Available ? "available" : "offline mode") + ")");
        }
    }

    public sealed class MapRunner : MonoBehaviour
    {
        private void Update() => MapSession.Tick();
        private void LateUpdate()
        {
            if (!MapSession.IsOpen) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        private void OnGUI()
        {
            MapSession.DrawHud(MapSession.IsHolding);
            MapSession.Draw();
        }
    }
}
