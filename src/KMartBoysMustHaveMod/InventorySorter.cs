using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KMartBoysMustHaveMod
{
    public enum SortMode
    {
        Name,
        Type,
        Value,
        Quality,
        Power,
        Weight
    }

    // ------------------------------------------------------------------
    // Sort button on the inventory screen (your inventory and open chests).
    // Left-click sorts, right-click switches the sort mode.
    // Your hotbar, locked slots and Equipment and Quick Slots cells never move.
    // ------------------------------------------------------------------
    internal static class InventorySorter
    {
        private static readonly MethodInfo InventoryChanged =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        public static void Sort(Inventory inv, bool isPlayer)
        {
            if (inv == null) return;
            Player player = Player.m_localPlayer;

            // Which cells stay put.
            Func<Vector2i, bool> fixedCell = pos =>
            {
                if (!isPlayer) return false;
                if (KMartBoysPlugin.SortSkipHotbar.Value && pos.y == 0) return true;
                if (ModCompat.IsSpecialSlot(pos)) return true;
                if (player != null && KMartBoysPlugin.SlotLockEnabled.Value && SlotLocks.IsLocked(player, pos)) return true;
                return false;
            };

            ItemDrop.ItemData dragging = null;
            if (InventoryGui.instance != null)
                dragging = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem")(InventoryGui.instance);

            List<ItemDrop.ItemData> movable = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item == null || item == dragging) continue;
                if (fixedCell(item.m_gridPos)) continue;
                movable.Add(item);
            }
            if (movable.Count == 0) return;

            if (KMartBoysPlugin.SortMergeStacks.Value) MergeStacks(inv, movable);

            movable.Sort(Comparer(KMartBoysPlugin.SortModeSetting.Value));

            // Free cells in reading order (left to right, top to bottom).
            List<Vector2i> cells = new List<Vector2i>();
            HashSet<long> occupiedByFixed = new HashSet<long>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (item != null && !movable.Contains(item)) occupiedByFixed.Add(SlotLocks.Key(item.m_gridPos));

            for (int y = 0; y < inv.GetHeight(); y++)
            for (int x = 0; x < inv.GetWidth(); x++)
            {
                Vector2i pos = new Vector2i(x, y);
                if (fixedCell(pos) || occupiedByFixed.Contains(SlotLocks.Key(pos))) continue;
                cells.Add(pos);
            }
            if (cells.Count < movable.Count) return; // shouldn't happen; never risk overlapping items

            for (int i = 0; i < movable.Count; i++) movable[i].m_gridPos = cells[i];

            if (InventoryChanged != null) InventoryChanged.Invoke(inv, new object[] { false, false });
        }

        private static void MergeStacks(Inventory inv, List<ItemDrop.ItemData> items)
        {
            Dictionary<string, ItemDrop.ItemData> open = new Dictionary<string, ItemDrop.ItemData>();
            List<ItemDrop.ItemData> emptied = new List<ItemDrop.ItemData>();

            foreach (ItemDrop.ItemData item in items)
            {
                int max = item.m_shared.m_maxStackSize;
                if (max <= 1 || item.m_equipped) continue;
                string key = item.m_shared.m_name + "|" + item.m_quality + "|" + item.m_worldLevel;

                ItemDrop.ItemData target;
                if (open.TryGetValue(key, out target) && target.m_stack < max)
                {
                    int move = Math.Min(max - target.m_stack, item.m_stack);
                    target.m_stack += move;
                    item.m_stack -= move;
                    if (item.m_stack <= 0)
                    {
                        emptied.Add(item);
                        continue;
                    }
                }
                if (item.m_stack < max) open[key] = item;
                else open.Remove(key);
            }

            foreach (ItemDrop.ItemData e in emptied)
            {
                items.Remove(e);
                inv.RemoveItem(e);
            }
        }

        private static string DisplayName(ItemDrop.ItemData item)
        {
            string n = item.m_shared.m_name;
            return Localization.instance != null ? Localization.instance.Localize(n) : n;
        }

        private static float Power(ItemDrop.ItemData item)
        {
            float p = 0f;
            try
            {
                p = Math.Max(p, item.GetDamage().GetTotalDamage());
                p = Math.Max(p, item.GetArmor());
                p = Math.Max(p, item.GetBaseBlockPower());
            }
            catch
            {
                // Some modded items throw here; treat as 0.
            }
            return p;
        }

        private static int TypeOrder(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir: return 0;
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return 1;
                case ItemDrop.ItemData.ItemType.Shield: return 2;
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Hands: return 3;
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket: return 4;
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch: return 5;
                case ItemDrop.ItemData.ItemType.Consumable: return 6;
                case ItemDrop.ItemData.ItemType.Fish: return 7;
                case ItemDrop.ItemData.ItemType.Material: return 8;
                case ItemDrop.ItemData.ItemType.Trophy: return 9;
                default: return 10;
            }
        }

        private static Comparison<ItemDrop.ItemData> Comparer(SortMode mode)
        {
            Comparison<ItemDrop.ItemData> byName = (a, b) =>
            {
                int c = string.Compare(DisplayName(a), DisplayName(b), StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                c = b.m_quality.CompareTo(a.m_quality);
                return c != 0 ? c : b.m_stack.CompareTo(a.m_stack);
            };

            switch (mode)
            {
                case SortMode.Type:
                    return (a, b) =>
                    {
                        int c = TypeOrder(a).CompareTo(TypeOrder(b));
                        return c != 0 ? c : byName(a, b);
                    };
                case SortMode.Value:
                    return (a, b) =>
                    {
                        int c = ((long)b.m_shared.m_value * b.m_stack).CompareTo((long)a.m_shared.m_value * a.m_stack);
                        return c != 0 ? c : byName(a, b);
                    };
                case SortMode.Quality:
                    return (a, b) =>
                    {
                        int c = b.m_quality.CompareTo(a.m_quality);
                        return c != 0 ? c : byName(a, b);
                    };
                case SortMode.Power:
                    return (a, b) =>
                    {
                        int c = Power(b).CompareTo(Power(a));
                        return c != 0 ? c : byName(a, b);
                    };
                case SortMode.Weight:
                    return (a, b) =>
                    {
                        int c = (b.m_shared.m_weight * b.m_stack).CompareTo(a.m_shared.m_weight * a.m_stack);
                        return c != 0 ? c : byName(a, b);
                    };
                default:
                    return byName;
            }
        }
    }

    // Click handler: left = sort, right = next sort mode.
    internal class SortButtonClick : MonoBehaviour, IPointerClickHandler
    {
        public bool ForPlayer;

        public void OnPointerClick(PointerEventData e)
        {
            try
            {
                if (e.button == PointerEventData.InputButton.Right)
                {
                    int count = Enum.GetValues(typeof(SortMode)).Length;
                    KMartBoysPlugin.SortModeSetting.Value = (SortMode)(((int)KMartBoysPlugin.SortModeSetting.Value + 1) % count);
                    SortButtons.RefreshLabels();
                    return;
                }
                if (e.button != PointerEventData.InputButton.Left) return;

                Inventory inv = null;
                if (ForPlayer)
                {
                    if (Player.m_localPlayer != null) inv = Player.m_localPlayer.GetInventory();
                }
                else
                {
                    Container c = SortButtons.OpenContainer();
                    if (c != null)
                    {
                        ContainerSource.ClaimOwnership(c); // the owner saves the chest; make sure that's us
                        inv = c.GetInventory();
                    }
                }
                InventorySorter.Sort(inv, ForPlayer);
            }
            catch (Exception ex)
            {
                KMartBoysPlugin.LogOnce("sortclick", "Sorting failed: " + ex);
            }
        }
    }

    internal static class SortButtons
    {
        private const string PlayerButtonName = "KMartBoys_SortPlayer";
        private const string ContainerButtonName = "KMartBoys_SortContainer";

        private static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static GameObject _playerButton;
        private static GameObject _containerButton;

        public static Container OpenContainer()
        {
            return InventoryGui.instance != null ? CurrentContainer(InventoryGui.instance) : null;
        }

        public static void Ensure(InventoryGui gui)
        {
            bool on = KMartBoysPlugin.ModEnabled.Value && KMartBoysPlugin.SortEnabled.Value;
            if (gui == null || gui.m_takeAllButton == null) return;

            if (on && _playerButton == null && gui.m_player != null)
                _playerButton = Create(gui, gui.m_player, PlayerButtonName, true);
            if (on && _containerButton == null && gui.m_container != null)
                _containerButton = Create(gui, gui.m_container, ContainerButtonName, false);

            if (_playerButton != null)
            {
                _playerButton.SetActive(on);
                Place(_playerButton, true);
            }
            if (_containerButton != null)
            {
                _containerButton.SetActive(on);
                Place(_containerButton, false);
            }
            RefreshLabels();
        }

        private static GameObject Create(InventoryGui gui, RectTransform parent, string name, bool forPlayer)
        {
            // Clone the game's "Take all" button so it looks native.
            GameObject go = UnityEngine.Object.Instantiate(gui.m_takeAllButton.gameObject, parent, false);
            go.name = name;

            // Remove anything that would re-label it or bind it to a gamepad key.
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string type = mb.GetType().Name;
                if (type == "Localize" || type == "UIGamePad") UnityEngine.Object.DestroyImmediate(mb);
            }

            Button button = go.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent(); // drop the cloned "take all" action
                button.interactable = true;
            }

            go.AddComponent<SortButtonClick>().ForPlayer = forPlayer;

            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(140f, rt.sizeDelta.y > 0f ? rt.sizeDelta.y : 32f);
            return go;
        }

        private static void Place(GameObject go, bool forPlayer)
        {
            RectTransform rt = (RectTransform)go.transform;
            // Just above the panel's top-right corner.
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0f);
            Vector2 offset = forPlayer
                ? new Vector2(KMartBoysPlugin.SortPlayerOffsetX.Value, KMartBoysPlugin.SortPlayerOffsetY.Value)
                : new Vector2(KMartBoysPlugin.SortChestOffsetX.Value, KMartBoysPlugin.SortChestOffsetY.Value);
            rt.anchoredPosition = offset;
        }

        public static void RefreshLabels()
        {
            string label = "Sort: " + KMartBoysPlugin.SortModeSetting.Value;
            SetLabel(_playerButton, label);
            SetLabel(_containerButton, label);
        }

        private static void SetLabel(GameObject go, string text)
        {
            if (go == null) return;
            TMP_Text t = go.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = text;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class InventoryGui_Show_SortButtons_Patch
    {
        private static void Postfix(InventoryGui __instance)
        {
            try
            {
                SortButtons.Ensure(__instance);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("sortbuttons", "Creating sort buttons failed: " + e);
            }
        }
    }
}
