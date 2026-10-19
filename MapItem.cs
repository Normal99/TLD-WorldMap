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
        private const int ItemId = 9100, LootCategoryId = 91;
        private static GameObject _holder;

        private static void Postfix(itemdatabase __instance)
        {
            try
            {
                var source = __instance.items != null && __instance.items.Length > (int)itemdatabase.i.book ? __instance.items[(int)itemdatabase.i.book] : null;
                if (source == null) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("Map item not registered: book item prefab missing"); return; }
                if (__instance.items.Length <= ItemId) Array.Resize(ref __instance.items, ItemId + 1);
                if (_holder == null) { _holder = new GameObject("WorldMapItemPrefab"); _holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(_holder); }
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
                __instance.items[ItemId] = prefab;

                // Misc household tables are used by the game's house loot points. Keep it rare.
                if (__instance.categories.Length <= LootCategoryId) Array.Resize(ref __instance.categories, LootCategoryId + 1);
                __instance.categories[LootCategoryId] = new itemdatabase.category(new[] { ItemId }, false, new[] { 1 });
                AddToCategory(__instance, 34);
                AddToCategory(__instance, 35);
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogError("Could not register map item: " + e); }
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
}
