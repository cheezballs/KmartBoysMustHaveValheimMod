using System;
using HarmonyLib;

namespace KMartBoysMustHaveMod
{
    // ------------------------------------------------------------------
    // Craft, upgrade and build using materials from nearby containers.
    //
    // How it works:
    //   * While the game is CHECKING requirements (HaveRequirementItems,
    //     HaveRequirements for pieces, the requirement slot color), any count of
    //     the local player's inventory also includes nearby containers.
    //   * While the game is CONSUMING requirements (ConsumeResources), removals
    //     from the player's inventory take what the player carries first and
    //     the rest from nearby containers, nearest first.
    // Recipe discovery is left alone, so chests don't unlock recipes.
    // ------------------------------------------------------------------
    internal static class CraftScope
    {
        // Depth counters (calls can nest). Main thread only.
        public static int CountDepth;
        public static int ConsumeDepth;

        public static bool CraftingEnabled
        {
            get { return KMartBoysPlugin.ModEnabled.Value && KMartBoysPlugin.CraftEnabled.Value && !KMartBoysPlugin.OtherCraftModLoaded; }
        }

        public static bool BuildingEnabled
        {
            get { return CraftingEnabled && KMartBoysPlugin.CraftAllowBuilding.Value; }
        }

        public static bool IsLocalPlayer(Player p)
        {
            return p != null && p == Player.m_localPlayer;
        }

        public static bool IsLocalInventory(Inventory inv)
        {
            Player p = Player.m_localPlayer;
            return p != null && inv != null && inv == p.GetInventory();
        }
    }

    // ---- Checking ----

    [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
    internal static class Player_HaveRequirementItems_Patch
    {
        private static void Prefix(Player __instance, bool discover, out bool __state)
        {
            __state = !discover && CraftScope.CraftingEnabled && CraftScope.IsLocalPlayer(__instance);
            if (__state) CraftScope.CountDepth++;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) CraftScope.CountDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Piece), typeof(Player.RequirementMode) })]
    internal static class Player_HaveRequirementsPiece_Patch
    {
        private static void Prefix(Player __instance, Player.RequirementMode mode, out bool __state)
        {
            __state = mode != Player.RequirementMode.IsKnown && CraftScope.BuildingEnabled && CraftScope.IsLocalPlayer(__instance);
            if (__state) CraftScope.CountDepth++;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) CraftScope.CountDepth--;
            return __exception;
        }
    }

    // Requirement slots flash red when you lack materials; count containers there too.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class InventoryGui_SetupRequirement_Color_Patch
    {
        private static void Prefix(Player player, bool craft, out bool __state)
        {
            __state = KMartBoysPlugin.CraftFixColors.Value
                      && (craft ? CraftScope.CraftingEnabled : CraftScope.BuildingEnabled)
                      && CraftScope.IsLocalPlayer(player);
            if (__state) CraftScope.CountDepth++;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) CraftScope.CountDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
    internal static class Inventory_CountItems_Patch
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            if (CraftScope.CountDepth <= 0 || CraftScope.ConsumeDepth > 0) return;
            if (!CraftScope.IsLocalInventory(__instance)) return;
            try
            {
                __result += ContainerSource.Count(name, quality, matchWorldLevel);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("countitems", "Counting container items failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), new[] { typeof(string), typeof(bool) })]
    internal static class Inventory_HaveItem_Patch
    {
        private static void Postfix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
        {
            if (__result || CraftScope.CountDepth <= 0 || CraftScope.ConsumeDepth > 0) return;
            if (!CraftScope.IsLocalInventory(__instance)) return;
            try
            {
                __result = ContainerSource.Have(name, matchWorldLevel);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("haveitem", "Checking container items failed: " + e.Message);
            }
        }
    }

    // ---- Consuming ----

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class Player_ConsumeResources_Patch
    {
        private static void Prefix(Player __instance, out bool __state)
        {
            __state = CraftScope.CraftingEnabled && CraftScope.IsLocalPlayer(__instance);
            if (__state) CraftScope.ConsumeDepth++;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) CraftScope.ConsumeDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem),
        new[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
    internal static class Inventory_RemoveItemByName_Patch
    {
        private static void Prefix(Inventory __instance, string name, ref int amount, int itemQuality, bool worldLevelBased)
        {
            if (CraftScope.ConsumeDepth <= 0 || amount <= 0) return;
            if (!CraftScope.IsLocalInventory(__instance)) return;
            try
            {
                int carried = __instance.CountItems(name, itemQuality, worldLevelBased); // raw: ConsumeDepth > 0
                if (carried >= amount) return;

                int shortBy = amount - carried;
                int taken = ContainerSource.Remove(name, shortBy, itemQuality, worldLevelBased);
                if (taken < shortBy)
                    KMartBoysPlugin.LogOnce("consumeshort",
                        "Containers had " + (shortBy - taken) + " fewer " + name + " than expected while crafting/building.");

                // Let the game remove only what the player actually carries.
                amount = carried;
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("consume", "Taking materials from containers failed: " + e.Message);
            }
        }
    }
}
