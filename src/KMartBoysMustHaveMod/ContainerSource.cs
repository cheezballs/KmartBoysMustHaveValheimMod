using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KMartBoysMustHaveMod
{
    // ------------------------------------------------------------------
    // Nearby containers used as a resource pool for crafting, building and
    // refueling. The container list is cached briefly; item counts are
    // memoized per frame because the game asks for them many times per frame.
    // ------------------------------------------------------------------
    internal static class ContainerSource
    {
        private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerNView =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");

        private static readonly AccessTools.FieldRef<InventoryGui, Container> GuiCurrentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static readonly MethodInfo ContainerCheckAccess =
            AccessTools.Method(typeof(Container), "CheckAccess", new[] { typeof(long) });

        private const float CacheSeconds = 1f;

        private static readonly List<Container> Cached = new List<Container>();
        private static float _cachedAt = -999f;

        private static readonly Dictionary<string, int> CountMemo = new Dictionary<string, int>();
        private static int _memoFrame = -1;

        // Containers within the craft-from-containers radius, nearest first.
        public static List<Container> GetContainers()
        {
            if (Time.unscaledTime - _cachedAt >= CacheSeconds) Rebuild();
            // Drop anything destroyed since the last rebuild.
            for (int i = Cached.Count - 1; i >= 0; i--)
                if (Cached[i] == null) Cached.RemoveAt(i);
            return Cached;
        }

        public static void Invalidate()
        {
            _cachedAt = -999f;
            CountMemo.Clear();
        }

        private static void Rebuild()
        {
            _cachedAt = Time.unscaledTime;
            CountMemo.Clear();
            Cached.Clear();
            Player player = Player.m_localPlayer;
            if (player == null) return;
            Cached.AddRange(FindAround(player.transform.position, KMartBoysPlugin.CraftRadius.Value));
        }

        private static bool IsUsable(Container c, long playerId, Container openContainer)
        {
            ZNetView nview = ContainerNView(c);
            if (nview == null || !nview.IsValid()) return false;
            if (c.GetInventory() == null) return false;

            if (c.GetComponentInParent<TombStone>() != null) return false; // graves
            if (c.IsInUse() && c != openContainer) return false;           // someone else has it open

            if (KMartBoysPlugin.CraftIgnoreMoving.Value &&
                (c.m_wagon != null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null))
                return false;

            if (KMartBoysPlugin.CraftOnlyPlayerBuilt.Value)
            {
                Piece piece = c.GetComponentInParent<Piece>();
                if (piece == null || !piece.IsPlacedByPlayer()) return false;
            }

            if (ContainerCheckAccess != null && !(bool)ContainerCheckAccess.Invoke(c, new object[] { playerId })) return false;
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false, false)) return false;
            return true;
        }

        // How many of an item the nearby containers hold (same semantics as Inventory.CountItems).
        public static int Count(string name, int quality, bool matchWorldLevel)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (_memoFrame != Time.frameCount)
            {
                _memoFrame = Time.frameCount;
                CountMemo.Clear();
            }

            string key = name + "|" + quality + "|" + (matchWorldLevel ? "1" : "0");
            int cached;
            if (CountMemo.TryGetValue(key, out cached)) return cached;

            int total = 0;
            foreach (Container c in GetContainers())
            {
                Inventory inv = c.GetInventory();
                if (inv != null) total += inv.CountItems(name, quality, matchWorldLevel);
            }
            CountMemo[key] = total;
            return total;
        }

        public static bool Have(string name, bool matchWorldLevel)
        {
            foreach (Container c in GetContainers())
            {
                Inventory inv = c.GetInventory();
                if (inv != null && inv.HaveItem(name, matchWorldLevel)) return true;
            }
            return false;
        }

        // Removes up to `amount` from nearby containers, nearest first. Returns how many were removed.
        public static int Remove(string name, int amount, int itemQuality, bool worldLevelBased)
        {
            return Remove(GetContainers(), name, amount, itemQuality, worldLevelBased);
        }

        // keepPerContainer: leave at least this many in every container (so it still "matches" for auto-deposit).
        public static int Remove(List<Container> containers, string name, int amount, int itemQuality, bool worldLevelBased,
            int keepPerContainer = 0)
        {
            int remaining = amount;
            foreach (Container c in containers)
            {
                if (remaining <= 0) break;
                if (c == null) continue;
                Inventory inv = c.GetInventory();
                if (inv == null) continue;

                int inChest = inv.CountItems(name, itemQuality, worldLevelBased);
                int available = inChest - Math.Max(0, keepPerContainer);
                if (available <= 0) continue;
                if (!ClaimOwnership(c)) continue;

                int take = Math.Min(available, remaining);
                inv.RemoveItem(name, take, itemQuality, worldLevelBased);
                int removed = inChest - inv.CountItems(name, itemQuality, worldLevelBased);
                remaining -= Math.Max(0, removed);
            }

            CountMemo.Clear();
            StorageStock.Invalidate();
            return amount - remaining;
        }

        public static bool ClaimOwnership(Container c)
        {
            ZNetView nview = ContainerNView(c);
            if (nview == null || !nview.IsValid()) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            return nview.IsOwner();
        }

        // Fresh (uncached) list for a given center and radius, using the same filters.
        public static List<Container> FindAround(Vector3 origin, float radius)
        {
            List<Container> result = new List<Container>();
            Player player = Player.m_localPlayer;
            if (player == null) return result;
            long playerId = player.GetPlayerID();
            Container openContainer = InventoryGui.instance != null ? GuiCurrentContainer(InventoryGui.instance) : null;

            List<KeyValuePair<float, Container>> found = new List<KeyValuePair<float, Container>>();
            foreach (Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                try
                {
                    if (c == null || !c.isActiveAndEnabled) continue;
                    float dist = Vector3.Distance(origin, c.transform.position);
                    if (dist > radius) continue;
                    if (!IsUsable(c, playerId, openContainer)) continue;
                    found.Add(new KeyValuePair<float, Container>(dist, c));
                }
                catch (Exception e)
                {
                    KMartBoysPlugin.LogOnce("containersource2", "Skipped a container while scanning: " + e.Message);
                }
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<float, Container> kv in found) result.Add(kv.Value);
            return result;
        }
    }
}
