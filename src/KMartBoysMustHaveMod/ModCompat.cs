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
    // Compatibility with Equipment and Quick Slots (RandyKnapp).
    // EAQS stores equipment, quick-slot and Better Archery quiver items in
    // extra cells of the player inventory. Those must never be deposited.
    // Soft dependency through reflection: works with or without EAQS.
    // ------------------------------------------------------------------
    internal static class ModCompat
    {
        private static bool _resolved;
        private static MethodInfo _eaqsIsSlotCell;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type api = AccessTools.TypeByName("EquipmentAndQuickSlots.API");
                if (api != null)
                {
                    _eaqsIsSlotCell = AccessTools.Method(api, "IsSlotCell",
                        new[] { typeof(int), typeof(int), typeof(string).MakeByRefType() });
                }
                if (_eaqsIsSlotCell != null)
                    KMartBoysPlugin.Log("Equipment and Quick Slots detected: its slots will be skipped.");
            }
            catch (Exception e)
            {
                KMartBoysPlugin.Log("EAQS compatibility lookup failed: " + e.Message);
                _eaqsIsSlotCell = null;
            }
        }

        public static bool IsSpecialSlot(Vector2i pos)
        {
            Resolve();
            if (_eaqsIsSlotCell == null) return false;
            try
            {
                object[] args = { pos.x, pos.y, null };
                return (bool)_eaqsIsSlotCell.Invoke(null, args);
            }
            catch
            {
                return false;
            }
        }
    }
}
