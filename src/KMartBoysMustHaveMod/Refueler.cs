using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KMartBoysMustHaveMod
{
    // ------------------------------------------------------------------
    // Auto-refuel: every few seconds, top up fires (campfires, hearths,
    // braziers, torches, hot tubs...), ovens and smelters/furnaces near you
    // with fuel, and fill smelters, blast furnaces, eitr refineries, kilns,
    // windmills and spinning wheels with their inputs (ore, scrap, soft
    // tissue, wood, barley, flax...). Everything comes from nearby containers
    // (and optionally your inventory).
    // ------------------------------------------------------------------
    internal static class Refueler
    {
        private static readonly AccessTools.FieldRef<Fireplace, ZNetView> FireplaceNView =
            AccessTools.FieldRefAccess<Fireplace, ZNetView>("m_nview");

        private static readonly AccessTools.FieldRef<Smelter, ZNetView> SmelterNView =
            AccessTools.FieldRefAccess<Smelter, ZNetView>("m_nview");

        private static readonly AccessTools.FieldRef<CookingStation, ZNetView> CookingNView =
            AccessTools.FieldRefAccess<CookingStation, ZNetView>("m_nview");

        private static readonly MethodInfo SmelterGetQueueSize = AccessTools.Method(typeof(Smelter), "GetQueueSize");

        private enum ProcessorKind { Smelter, EitrRefinery, Kiln, Windmill, SpinningWheel }

        private static float _nextRun;
        private static string _excludedRaw;
        private static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Tick(Player player)
        {
            if (!KMartBoysPlugin.RefuelEnabled.Value || KMartBoysPlugin.OtherRefuelModLoaded || player == null || player.IsDead()) return;
            if (Time.time < _nextRun) return;
            _nextRun = Time.time + Mathf.Max(1f, KMartBoysPlugin.RefuelInterval.Value);

            try
            {
                Run(player);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("refuel", "Auto-refuel error: " + e);
            }
        }

        private class FuelPool
        {
            public List<Container> Containers;
            public Inventory PlayerInventory;
        }

        private static void Run(Player player)
        {
            RefreshExcluded();

            Vector3 origin = player.transform.position;
            float radius = KMartBoysPlugin.RefuelRadius.Value;
            float threshold = Mathf.Clamp01(KMartBoysPlugin.RefuelBelowPercent.Value / 100f);

            FuelPool pool = null; // built lazily, only if something needs fuel
            int refueled = 0;

            if (KMartBoysPlugin.RefuelFireplaces.Value)
            {
                foreach (Fireplace f in UnityEngine.Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None))
                {
                    if (f == null || f.m_infiniteFuel || f.m_fuelItem == null || f.m_maxFuel <= 0f) continue;
                    if (!InRange(f, origin, radius) || IsExcluded(f.gameObject)) continue;

                    ZNetView nview = FireplaceNView(f);
                    int need = MissingFuel(nview, f.m_maxFuel, threshold);
                    if (need <= 0) continue;

                    int got = TakeItem(ref pool, player, FuelName(f.m_fuelItem), need);
                    if (got <= 0) continue;
                    f.AddFuel(got);
                    refueled++;
                }
            }

            foreach (Smelter s in UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None))
            {
                if (s == null || !InRange(s, origin, radius) || IsExcluded(s.gameObject)) continue;
                ZNetView nview = SmelterNView(s);
                if (nview == null || !nview.IsValid()) continue;
                bool touched = false;

                // Fuel (coal, sap, ...)
                if (KMartBoysPlugin.RefuelSmelters.Value && s.m_fuelItem != null && s.m_maxFuel > 0)
                {
                    int need = MissingFuel(nview, s.m_maxFuel, threshold);
                    if (need > 0)
                    {
                        int got = TakeItem(ref pool, player, FuelName(s.m_fuelItem), need);
                        for (int i = 0; i < got; i++) nview.InvokeRPC("RPC_AddFuel");
                        touched |= got > 0;
                    }
                }

                // Inputs (ore, scrap, soft tissue, wood, barley, flax, ...)
                if (FillInputsFor(Classify(s)) && s.m_maxOre > 0 && s.m_conversion != null)
                    touched |= FillInputs(ref pool, player, s, nview) > 0;

                if (touched) refueled++;
            }

            if (KMartBoysPlugin.RefuelOvens.Value)
            {
                foreach (CookingStation c in UnityEngine.Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None))
                {
                    if (c == null || !c.m_useFuel || c.m_fuelItem == null || c.m_maxFuel <= 0) continue;
                    if (!InRange(c, origin, radius) || IsExcluded(c.gameObject)) continue;

                    ZNetView nview = CookingNView(c);
                    int need = MissingFuel(nview, c.m_maxFuel, threshold);
                    if (need <= 0) continue;

                    int got = TakeItem(ref pool, player, FuelName(c.m_fuelItem), need);
                    if (got <= 0) continue;
                    for (int i = 0; i < got; i++) nview.InvokeRPC("RPC_AddFuel");
                    refueled++;
                }
            }

            if (refueled > 0 && KMartBoysPlugin.RefuelShowMessage.Value)
                player.Message(MessageHud.MessageType.TopLeft,
                    "Refilled " + refueled + " station" + (refueled == 1 ? "" : "s"), 0, null, false);
        }

        private static ProcessorKind Classify(Smelter s)
        {
            string name = Utils.GetPrefabName(s.gameObject).ToLowerInvariant();
            if (name.Contains("eitrrefinery")) return ProcessorKind.EitrRefinery;
            if (name.Contains("kiln")) return ProcessorKind.Kiln;
            if (name.Contains("windmill")) return ProcessorKind.Windmill;
            if (name.Contains("spinningwheel")) return ProcessorKind.SpinningWheel;
            return ProcessorKind.Smelter; // smelter, blast furnace, and anything modded
        }

        private static bool FillInputsFor(ProcessorKind kind)
        {
            switch (kind)
            {
                case ProcessorKind.EitrRefinery: return KMartBoysPlugin.FillEitrRefinery.Value;
                case ProcessorKind.Kiln: return KMartBoysPlugin.FillKilns.Value;
                case ProcessorKind.Windmill: return KMartBoysPlugin.FillWindmills.Value;
                case ProcessorKind.SpinningWheel: return KMartBoysPlugin.FillSpinningWheels.Value;
                default: return KMartBoysPlugin.FillSmelters.Value;
            }
        }

        // Queues inputs until the processor is full. Returns how many were added.
        private static int FillInputs(ref FuelPool pool, Player player, Smelter s, ZNetView nview)
        {
            int queued = SmelterGetQueueSize != null ? (int)SmelterGetQueueSize.Invoke(s, null) : 0;
            int space = s.m_maxOre - queued;
            if (space <= 0) return 0;

            int added = 0;
            foreach (Smelter.ItemConversion conv in s.m_conversion)
            {
                if (space <= 0) break;
                if (conv == null || conv.m_from == null) continue;
                string itemName = FuelName(conv.m_from);
                string prefabName = conv.m_from.gameObject.name;
                if (string.IsNullOrEmpty(itemName) || string.IsNullOrEmpty(prefabName)) continue;

                int got = TakeItem(ref pool, player, itemName, space);
                for (int i = 0; i < got; i++) nview.InvokeRPC("RPC_AddOre", prefabName, false);
                space -= got;
                added += got;
            }
            return added;
        }

        private static bool InRange(Behaviour c, Vector3 origin, float radius)
        {
            return c.isActiveAndEnabled && Vector3.Distance(origin, c.transform.position) <= radius;
        }

        // Whole units of fuel missing, or 0 if above the refill threshold / not loaded.
        private static int MissingFuel(ZNetView nview, float maxFuel, float threshold)
        {
            if (nview == null || !nview.IsValid()) return 0;
            ZDO zdo = nview.GetZDO();
            if (zdo == null) return 0;
            float current = zdo.GetFloat(ZDOVars.s_fuel, 0f);
            if (current >= maxFuel * threshold && threshold < 1f) return 0;
            return Mathf.FloorToInt(maxFuel - current);
        }

        private static string FuelName(ItemDrop drop)
        {
            return drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_name
                : null;
        }

        // Takes up to `amount` of an item: containers first, then (optionally) the player's inventory.
        // Leaves KeepReserve units untouched in the containers.
        private static int TakeItem(ref FuelPool pool, Player player, string fuelName, int amount)
        {
            if (string.IsNullOrEmpty(fuelName) || amount <= 0) return 0;
            if (pool == null)
            {
                pool = new FuelPool
                {
                    Containers = ContainerSource.FindAround(player.transform.position, KMartBoysPlugin.CraftRadius.Value),
                    PlayerInventory = KMartBoysPlugin.RefuelUseInventory.Value ? player.GetInventory() : null
                };
            }

            int taken = 0;

            int keepEach = KMartBoysPlugin.RefuelLeaveOnePerChest.Value ? 1 : 0;
            int inContainers = 0;
            foreach (Container c in pool.Containers)
            {
                if (c == null) continue;
                Inventory inv = c.GetInventory();
                if (inv != null) inContainers += Math.Max(0, inv.CountItems(fuelName, -1, false) - keepEach);
            }
            int usable = Math.Max(0, inContainers - Math.Max(0, KMartBoysPlugin.RefuelKeepReserve.Value));
            int fromContainers = Math.Min(usable, amount);
            if (fromContainers > 0)
                taken += ContainerSource.Remove(pool.Containers, fuelName, fromContainers, -1, false, keepEach);

            if (taken < amount && pool.PlayerInventory != null)
            {
                int carried = pool.PlayerInventory.CountItems(fuelName, -1, false);
                int fromBag = Math.Min(carried, amount - taken);
                if (fromBag > 0)
                {
                    pool.PlayerInventory.RemoveItem(fuelName, fromBag, -1, false);
                    taken += fromBag;
                }
            }
            return taken;
        }

        private static void RefreshExcluded()
        {
            string raw = KMartBoysPlugin.RefuelExcluded.Value ?? string.Empty;
            if (raw == _excludedRaw) return;
            _excludedRaw = raw;
            Excluded.Clear();
            foreach (string part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                if (name.Length > 0) Excluded.Add(name);
            }
        }

        private static bool IsExcluded(GameObject go)
        {
            if (Excluded.Count == 0) return false;
            return Excluded.Contains(Utils.GetPrefabName(go));
        }
    }
}
