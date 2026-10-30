using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KMartBoysMustHaveMod
{
    // ------------------------------------------------------------------
    // Auto-repair: when you open a crafting station, everything in your
    // inventory that this station can repair is repaired at once.
    // Uses the game's own rules (InventoryGui.CanRepair), so items only get
    // repaired at the right station type and level.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
    internal static class CraftingStation_Interact_AutoRepair_Patch
    {
        private static readonly MethodInfo CanRepair =
            AccessTools.Method(typeof(InventoryGui), "CanRepair", new[] { typeof(ItemDrop.ItemData) });

        private static void Postfix(CraftingStation __instance, Humanoid user, bool repeat, bool __result)
        {
            try
            {
                if (!__result || repeat) return;
                if (!KMartBoysPlugin.ModEnabled.Value || !KMartBoysPlugin.AutoRepairEnabled.Value) return;
                if (KMartBoysPlugin.OtherRepairModLoaded) return;

                Player player = user as Player;
                if (player == null || player != Player.m_localPlayer) return;
                if (player.GetCurrentCraftingStation() != __instance) return;

                InventoryGui gui = InventoryGui.instance;
                if (gui == null || CanRepair == null) return;

                List<ItemDrop.ItemData> worn = new List<ItemDrop.ItemData>();
                player.GetInventory().GetWornItems(worn);

                int repaired = 0;
                foreach (ItemDrop.ItemData item in worn)
                {
                    if (item == null) continue;
                    if (!(bool)CanRepair.Invoke(gui, new object[] { item })) continue;
                    float max = item.GetMaxDurability();
                    if (item.m_durability >= max) continue;
                    item.m_durability = max;
                    repaired++;
                }

                if (repaired <= 0) return;

                if (__instance.m_repairItemDoneEffects != null)
                    __instance.m_repairItemDoneEffects.Create(__instance.transform.position, Quaternion.identity);

                if (KMartBoysPlugin.AutoRepairShowMessage.Value)
                    player.Message(MessageHud.MessageType.Center,
                        "Repaired " + repaired + " item" + (repaired == 1 ? "" : "s"), 0, null, false);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("autorepair", "Auto-repair failed: " + e.Message);
            }
        }
    }
}
