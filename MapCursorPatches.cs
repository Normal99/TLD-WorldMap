using HarmonyLib;
using UnityEngine;

namespace WorldMap
{
    [HarmonyPatch(typeof(mainscript), "Update")]
    internal static class MainCursorPatch
    {
        private static void Postfix(mainscript __instance)
        {
            if (!MapSession.IsOpen) return;
            __instance.crsrLocked = false;
            __instance.SetCursorVisible(true);
        }
    }

    [HarmonyPatch(typeof(fpscontroller), "Update")]
    internal static class PlayerCursorPatch
    {
        /// The game's own map key (IN.map, M unless rebound) opens and closes the larger view while the map is held.
        /// It is taken here, before the player's Update reads it: seated, the same key opened the game's map camera
        /// at the same time.
        private static void Prefix(fpscontroller __instance)
        {
            var input = __instance.input != null ? __instance.input : inputscript.i;
            if (input != null && input.mapDown && __instance == mainscript.s?.player && MapSession.IsHolding && !mainscript.s.SomeInputSelected())
            {
                input.mapDown = false;
                MapSession.Toggle();
            }
            if (MapSession.IsOpen && mainscript.s != null) mainscript.s.crsrLocked = false;
        }
    }

    [HarmonyPatch(typeof(mainscript), "SetCursorVisible")]
    internal static class CursorVisibilityPatch
    {
        private static void Prefix(ref bool _state)
        {
            if (MapSession.IsOpen) _state = true;
        }
    }

    [HarmonyPatch(typeof(mainscript), "PressedEscape")]
    internal static class EscapeMapPatch
    {
        private static bool Prefix()
        {
            if (!MapSession.IsOpen) return true;
            MapSession.Close();
            return false;
        }
    }
}
