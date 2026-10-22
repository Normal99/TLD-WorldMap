using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldMap
{
    /// The map is a real saved game item based on the game's book item, so the game's own spawn/save/share paths handle it.
    [HarmonyPatch(typeof(itemdatabase), "Awake")]
    internal static class MapItemRegistration
    {
        private const int ItemId = 9400, LootCategoryId = 91;
        private static GameObject _holder, _prefab;
        /// The map item as the database has it (to place one in a new game's start house).
        internal static GameObject Prefab => _prefab;

        private static void Postfix(itemdatabase __instance)
        {
            try
            {
                var source = __instance.items != null && __instance.items.Length > (int)itemdatabase.i.book ? __instance.items[(int)itemdatabase.i.book] : null;
                if (source == null) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Map item not registered: book item prefab missing"); return; }
                if (__instance.items.Length > ItemId && __instance.items[ItemId] != null && __instance.items[ItemId] != _prefab)
                {
                    BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogError("Map item ID " + ItemId + " is already in use");
                    return;
                }
                if (__instance.items.Length <= ItemId) Array.Resize(ref __instance.items, ItemId + 1);
                if (_holder == null) { _holder = new GameObject("WorldMapItemPrefab"); _holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(_holder); }
                if (__instance.categories.Length > LootCategoryId && __instance.categories[LootCategoryId] != null && _prefab == null)
                {
                    BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogError("Loot category " + LootCategoryId + " is already in use: map not registered");
                    return;
                }
                // The database is a scene object: it fills its items again on every scene load (new game, load, menu).
                // The map is built once and only put back after that (a new clone every time piled up in the holder).
                if (_prefab != null) { __instance.items[ItemId] = _prefab; AddCategories(__instance); return; }
                var prefab = UnityEngine.Object.Instantiate(source, _holder.transform);
                prefab.name = "paper_worldmap";
                var save = prefab.GetComponent<tosaveitemscript>();
                if (save != null) { save.id = ItemId; save.searchtags = new List<string>(save.searchtags ?? new List<string>()) { "world map", "map", "navigation" }; }
                var pickup = prefab.GetComponent<pickupable>();
                if (pickup != null)
                {
                    // Keep normal item handling and enable right-hand equip.
                    pickup.tavcso = null;
                    pickup.weapon = null;
                    pickup.canInventory = true;
                    pickup.canHoldRightHand = true;
                }
                prefab.AddComponent<HeldMapDisplay>();
                _prefab = prefab;
                __instance.items[ItemId] = prefab;
                AddCategories(__instance);
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogError("Could not register map item: " + e); }
        }

        /// Misc household tables are used by the game's house loot points. Keep it rare (every new game also gets one
        /// in its start house: StartHouseMap).
        private static void AddCategories(itemdatabase db)
        {
            if (db.categories.Length <= LootCategoryId) Array.Resize(ref db.categories, LootCategoryId + 1);
            db.categories[LootCategoryId] = new itemdatabase.category(new[] { ItemId }, false, new[] { 1 });
            AddToCategory(db, 34);
            AddToCategory(db, 35);
        }

        internal static bool IsMap(pickupable item)
        {
            var save = item != null ? item.GetComponent<tosaveitemscript>() : null;
            return save != null && save.id == ItemId;
        }

        private static void AddToCategory(itemdatabase db, int index)
        {
            if (db.categories == null || index >= db.categories.Length) return;
            var c = db.categories[index]; if (c == null || c.categories == null || Array.IndexOf(c.categories, LootCategoryId) >= 0) return;
            var cats = new List<int>(c.categories) { LootCategoryId };
            var weights = new List<int>();
            for (int i = 0; i < c.categories.Length; i++) weights.Add(c.categorychances != null && i < c.categorychances.Length ? c.categorychances[i] : 100);
            weights.Add(1); c.categories = cats.ToArray(); c.categorychances = weights.ToArray();
        }
    }

    /// A new game (alone, or the host's): the player starts in the start house, and a World Map is put there with
    /// them, as the mod promises. Joiners get the host's (the session shares it); a loaded save gets none.
    [HarmonyPatch(typeof(poiGenScript), nameof(poiGenScript.StartHouseStart))]
    internal static class StartHouseMap
    {
        private static void Prefix(poiGenScript __instance, out bool __state) => __state = __instance.startHouseStarted;

        private static void Postfix(poiGenScript __instance, bool __state)
        {
            try
            {
                if (__state || !__instance.placeStartHouse || DataFromMenuScript.s == null || !DataFromMenuScript.s.newgame || !syncScript.IsHostOrSingle()) return;
                var player = mainscript.s != null ? mainscript.s.player : null;
                if (player == null) return;
                MapSession.StartHousePlaced(player.transform.position, player.transform.rotation);
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Start house map: " + e.Message); }
        }
    }
}
