using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KMartBoysMustHaveMod
{
    // Lock / unlock the slot under the mouse with a mouse button.
    internal static class SlotLockInput
    {
        private static readonly MethodInfo GetHoveredElement =
            AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");

        public static void Poll(Player player)
        {
            if (!KMartBoysPlugin.SlotLockEnabled.Value) return;
            int button;
            switch (KMartBoysPlugin.SlotLockClick.Value)
            {
                case LockClick.MiddleClick: button = 2; break;
                case LockClick.Mouse4: button = 3; break;
                case LockClick.Mouse5: button = 4; break;
                default: return;
            }
            if (!InventoryGui.IsVisible()) return;
            if (!ZInput.GetMouseButtonDown(button)) return;

            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_playerGrid == null || GetHoveredElement == null) return;

            InventoryElement el = GetHoveredElement.Invoke(gui.m_playerGrid, null) as InventoryElement;
            if (el == null) return;
            if (ModCompat.IsSpecialSlot(el.Position)) return;

            SlotLocks.Toggle(player, el.Position);
        }
    }

    // Optional: modifier + left-click a slot in your inventory to lock / unlock it.
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class InventoryGui_OnSelectedItem_Patch
    {
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> GuiDragItem =
            AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");

        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, Vector2i pos)
        {
            if (!KMartBoysPlugin.ModEnabled.Value || !KMartBoysPlugin.SlotLockEnabled.Value) return true;
            if (grid == null || grid != __instance.m_playerGrid) return true;
            if (GuiDragItem(__instance) != null) return true; // don't interfere with dragging
            if (KMartBoysPlugin.SlotLockModifier.Value == KeyCode.None) return true;
            if (!ZInput.GetKey(KMartBoysPlugin.SlotLockModifier.Value, false)) return true;
            if (ModCompat.IsSpecialSlot(pos)) return true;

            Player player = Player.m_localPlayer;
            if (player == null) return true;

            SlotLocks.Toggle(player, pos);
            return false; // swallow the click
        }
    }

    // Draws the "will not deposit" marker on locked slots.
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGrid_UpdateGui_Patch
    {
        private const string OverlayName = "KMartBoys_LockTint";
        private const string BadgeName = "KMartBoys_LockBadge";

        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        private static void Postfix(InventoryGrid __instance)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || __instance != gui.m_playerGrid) return;

            List<InventoryElement> elements = Elements(__instance);
            if (elements == null) return;

            Player player = Player.m_localPlayer;
            bool active = KMartBoysPlugin.ModEnabled.Value && KMartBoysPlugin.SlotLockEnabled.Value && player != null;
            Color tint = KMartBoysPlugin.SlotLockColor.Value;
            Color badge = new Color(tint.r, tint.g, tint.b, 1f);

            foreach (InventoryElement el in elements)
            {
                if (el == null) continue;
                bool locked = active && SlotLocks.IsLocked(player, el.Position);

                Image overlay = GetOrCreate(el.transform, OverlayName, locked, true);
                if (overlay != null)
                {
                    overlay.gameObject.SetActive(locked);
                    if (locked) overlay.color = tint;
                }

                Image corner = GetOrCreate(el.transform, BadgeName, locked, false);
                if (corner != null)
                {
                    corner.gameObject.SetActive(locked);
                    if (locked) corner.color = badge;
                }
            }
        }

        private static Image GetOrCreate(Transform parent, string name, bool needed, bool fullTint)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.GetComponent<Image>();
            if (!needed) return null;

            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            if (fullTint)
            {
                // Fill the slot, slightly inset, drawn behind the item icon.
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(2f, 2f);
                rt.offsetMax = new Vector2(-2f, -2f);
                rt.SetAsFirstSibling();
            }
            else
            {
                // Small solid square in the top-left corner, drawn on top.
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(9f, 9f);
                rt.anchoredPosition = new Vector2(3f, -3f);
                rt.SetAsLastSibling();
            }

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }
    }
}
