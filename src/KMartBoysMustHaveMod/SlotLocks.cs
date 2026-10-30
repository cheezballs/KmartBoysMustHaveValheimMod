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
    // Slot locks (saved in the character's custom data)
    // ------------------------------------------------------------------
    internal static class SlotLocks
    {
        private const string DataKey = "KMartBoysMustHaveMod.LockedSlots";

        private static Player _cachedPlayer;
        private static HashSet<long> _cached = new HashSet<long>();

        public static long Key(Vector2i pos)
        {
            return ((long)pos.x << 32) | (uint)pos.y;
        }

        public static HashSet<long> GetLocks(Player player)
        {
            if (player == null) return new HashSet<long>();
            if (player != _cachedPlayer)
            {
                _cachedPlayer = player;
                _cached = Load(player);
            }
            return _cached;
        }

        public static bool IsLocked(Player player, Vector2i pos)
        {
            return GetLocks(player).Contains(Key(pos));
        }

        public static void Toggle(Player player, Vector2i pos)
        {
            HashSet<long> locks = GetLocks(player);
            long k = Key(pos);
            if (!locks.Remove(k)) locks.Add(k);
            Save(player, locks);
        }

        private static HashSet<long> Load(Player player)
        {
            HashSet<long> result = new HashSet<long>();
            string raw;
            if (player.m_customData == null || !player.m_customData.TryGetValue(DataKey, out raw) || string.IsNullOrEmpty(raw))
                return result;

            foreach (string part in raw.Split(';'))
            {
                string[] xy = part.Split(',');
                int x, y;
                if (xy.Length == 2 && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y))
                    result.Add(Key(new Vector2i(x, y)));
            }
            return result;
        }

        private static void Save(Player player, HashSet<long> locks)
        {
            if (player.m_customData == null) return;
            List<string> parts = new List<string>();
            foreach (long k in locks)
            {
                int x = (int)(k >> 32);
                int y = (int)(k & 0xffffffff);
                parts.Add(x + "," + y);
            }
            if (parts.Count == 0) player.m_customData.Remove(DataKey);
            else player.m_customData[DataKey] = string.Join(";", parts.ToArray());
        }
    }

    public enum LockClick
    {
        Disabled,
        MiddleClick,
        Mouse4,
        Mouse5
    }
}
