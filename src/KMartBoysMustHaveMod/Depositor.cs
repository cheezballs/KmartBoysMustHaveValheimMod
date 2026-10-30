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
    // ------------------------------------------------------------------
    // Depositing
    // ------------------------------------------------------------------
    internal static class Depositor
    {
        private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerNView =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");

        private static readonly AccessTools.FieldRef<InventoryGui, Container> GuiCurrentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> GuiDragItem =
            AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");

        private static readonly MethodInfo ContainerCheckAccess =
            AccessTools.Method(typeof(Container), "CheckAccess", new[] { typeof(long) });

        public static void DepositNearby(Player player)
        {
            Inventory playerInv = player.GetInventory();
            if (playerInv == null) return;

            List<Container> containers = FindContainers(player);
            if (containers.Count == 0)
            {
                Notify(player, KMartBoysPlugin.NoContainersMessage.Value, 0, 0);
                return;
            }

            ItemDrop.ItemData dragItem = null;
            if (InventoryGui.instance != null) dragItem = GuiDragItem(InventoryGui.instance);

            HashSet<long> lockedSlots = SlotLocks.GetLocks(player);

            // Snapshot: we modify the inventory while iterating.
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(playerInv.GetAllItems());

            int totalMoved = 0;
            HashSet<Container> touched = new HashSet<Container>();

            foreach (ItemDrop.ItemData item in items)
            {
                if (!ShouldDeposit(item, dragItem, lockedSlots)) continue;

                foreach (Container container in containers)
                {
                    if (!playerInv.ContainsItem(item)) break; // fully deposited

                    Inventory target = container.GetInventory();
                    if (!target.ContainsItemByName(item.m_shared.m_name)) continue;

                    if (!EnsureOwnership(container)) continue;

                    int moved = MoveInto(playerInv, target, item);
                    if (moved > 0)
                    {
                        totalMoved += moved;
                        touched.Add(container);
                    }
                }
            }

            if (totalMoved > 0)
            {
                StorageStock.Invalidate();
                if (KMartBoysPlugin.CenterMessageEnabled.Value)
                {
                    string praise = Format(PickTier(totalMoved), totalMoved, touched.Count);
                    if (!string.IsNullOrEmpty(praise))
                        player.Message(MessageHud.MessageType.Center, praise, 0, null, false);
                }
                Notify(player, KMartBoysPlugin.SummaryMessage.Value, totalMoved, touched.Count);
            }
            else
                Notify(player, KMartBoysPlugin.NothingToDepositMessage.Value, 0, 0);
        }

        private static bool ShouldDeposit(ItemDrop.ItemData item, ItemDrop.ItemData dragItem, HashSet<long> lockedSlots)
        {
            if (item == null || item.m_shared == null) return false;
            if (item == dragItem) return false;
            if (KMartBoysPlugin.IgnoreEquipped.Value && item.m_equipped) return false;
            if (KMartBoysPlugin.IgnoreHotbar.Value && item.m_gridPos.y == 0) return false;
            if (KMartBoysPlugin.IgnoreAmmo.Value && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) return false;
            if (ModCompat.IsSpecialSlot(item.m_gridPos)) return false; // EAQS equipment / quick / quiver slots
            if (KMartBoysPlugin.SlotLockEnabled.Value && lockedSlots.Contains(SlotLocks.Key(item.m_gridPos))) return false;
            if (!KMartBoysPlugin.DepositNonStackables.Value && item.m_shared.m_maxStackSize <= 1) return false;
            return true;
        }

        private static List<Container> FindContainers(Player player)
        {
            float radius = KMartBoysPlugin.Radius.Value;
            Vector3 origin = player.transform.position;
            long playerId = player.GetPlayerID();
            Container openContainer = InventoryGui.instance != null ? GuiCurrentContainer(InventoryGui.instance) : null;

            List<KeyValuePair<float, Container>> found = new List<KeyValuePair<float, Container>>();

            foreach (Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (c == null || !c.isActiveAndEnabled) continue;

                float dist = Vector3.Distance(origin, c.transform.position);
                if (dist > radius) continue;

                ZNetView nview = ContainerNView(c);
                if (nview == null || !nview.IsValid()) continue;
                if (c.GetInventory() == null) continue;

                // Never touch graves.
                if (c.GetComponentInParent<TombStone>() != null) continue;

                // Someone else has it open.
                if (c.IsInUse() && c != openContainer) continue;

                // Private chests you don't own.
                if (ContainerCheckAccess != null && !(bool)ContainerCheckAccess.Invoke(c, new object[] { playerId })) continue;

                // Ward protection.
                if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false, false)) continue;

                found.Add(new KeyValuePair<float, Container>(dist, c));
            }

            found.Sort((a, b) => a.Key.CompareTo(b.Key)); // nearest first
            List<Container> result = new List<Container>(found.Count);
            foreach (KeyValuePair<float, Container> kv in found) result.Add(kv.Value);
            return result;
        }

        private static bool EnsureOwnership(Container c)
        {
            ZNetView nview = ContainerNView(c);
            if (nview == null || !nview.IsValid()) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            return nview.IsOwner();
        }

        private static bool SameStack(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            return a.m_shared.m_name == b.m_shared.m_name
                   && a.m_quality == b.m_quality
                   && a.m_worldLevel == b.m_worldLevel;
        }

        // Moves as much of `item` as fits into `target`: tops up existing
        // matching stacks first, then uses empty slots. Returns amount moved.
        private static int MoveInto(Inventory from, Inventory target, ItemDrop.ItemData item)
        {
            int maxStack = Math.Max(1, item.m_shared.m_maxStackSize);
            int moved = 0;

            // Pass 1: existing partial stacks.
            if (maxStack > 1)
            {
                List<ItemDrop.ItemData> existing = new List<ItemDrop.ItemData>(target.GetAllItems());
                foreach (ItemDrop.ItemData ex in existing)
                {
                    if (!from.ContainsItem(item)) return moved;
                    if (!SameStack(ex, item) || ex.m_stack >= maxStack) continue;
                    int amount = Math.Min(item.m_stack, maxStack - ex.m_stack);
                    moved += MoveSlot(from, target, item, amount, ex.m_gridPos.x, ex.m_gridPos.y);
                }
            }

            // Pass 2: empty slots.
            int w = target.GetWidth();
            int h = target.GetHeight();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!from.ContainsItem(item)) return moved;
                    if (target.GetItemAt(x, y) != null) continue;
                    int amount = Math.Min(item.m_stack, maxStack);
                    moved += MoveSlot(from, target, item, amount, x, y);
                }
            }
            return moved;
        }

        private static int MoveSlot(Inventory from, Inventory target, ItemDrop.ItemData item, int amount, int x, int y)
        {
            if (amount <= 0) return 0;
            int before = item.m_stack;
            target.MoveItemToThis(from, item, amount, x, y);
            int after = from.ContainsItem(item) ? item.m_stack : 0;
            return Math.Max(0, before - after);
        }

        // Highest enabled tier whose MinItems <= items.
        private static string PickTier(int items)
        {
            string best = null;
            int bestMin = -1;
            for (int i = 0; i < KMartBoysPlugin.TierMinItems.Length; i++)
            {
                int min = KMartBoysPlugin.TierMinItems[i].Value;
                string text = KMartBoysPlugin.TierMessages[i].Value;
                if (min <= 0 || string.IsNullOrEmpty(text)) continue;
                if (items >= min && min > bestMin)
                {
                    bestMin = min;
                    best = text;
                }
            }
            return best;
        }

        private static string Format(string template, int items, int containers)
        {
            if (string.IsNullOrEmpty(template)) return template;
            return template
                .Replace("{items}", items.ToString())
                .Replace("{containers}", containers.ToString())
                .Replace("{itemsWord}", items == 1 ? "item" : "items")
                .Replace("{containersWord}", containers == 1 ? "container" : "containers");
        }

        private static void Notify(Player player, string template, int items, int containers)
        {
            if (!KMartBoysPlugin.SummaryMessageEnabled.Value) return;
            string msg = Format(template, items, containers);
            if (string.IsNullOrEmpty(msg)) return;
            player.Message(MessageHud.MessageType.TopLeft, msg, 0, null, false);
        }
    }
}
