using System;
using HarmonyLib;
using UnityEngine;

namespace KMartBoysMustHaveMod
{
    public enum DeathChatChannel
    {
        Shout,
        Normal
    }

    // ------------------------------------------------------------------
    // When you die, announce it in chat so everyone knows.
    // Runs before the game processes the death, while your character still
    // exists to "say" it.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class Player_OnDeath_Patch
    {
        private static float _lastSent = -999f;

        private static void Prefix(Player __instance)
        {
            try
            {
                if (!KMartBoysPlugin.ModEnabled.Value || !KMartBoysPlugin.DeathMessageEnabled.Value) return;
                if (__instance == null || __instance != Player.m_localPlayer) return;
                if (Chat.instance == null) return;
                if (Time.unscaledTime - _lastSent < 3f) return; // never double-post one death
                _lastSent = Time.unscaledTime;

                string text = Pick(KMartBoysPlugin.DeathMessages.Value);
                if (string.IsNullOrEmpty(text)) return;
                text = text.Replace("{name}", __instance.GetPlayerName());

                Talker.Type type = KMartBoysPlugin.DeathMessageChannel.Value == DeathChatChannel.Normal
                    ? Talker.Type.Normal
                    : Talker.Type.Shout;
                Chat.instance.SendText(type, text);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("deathmsg", "Death message failed: " + e.Message);
            }
        }

        // Several messages can be separated with | and one is picked at random.
        private static string Pick(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string[] options = raw.Split('|');
            int tries = options.Length;
            while (tries-- > 0)
            {
                string pick = options[UnityEngine.Random.Range(0, options.Length)].Trim();
                if (pick.Length > 0) return pick;
            }
            return null;
        }
    }
}
