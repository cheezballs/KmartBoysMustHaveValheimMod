using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace KMartBoysMustHaveMod
{
    // ------------------------------------------------------------------
    // Crafting stock: under every required material at a crafting station,
    // show how many you carry and how many are sitting in storage boxes.
    // ------------------------------------------------------------------

    // Counts items across nearby containers. Cached and refreshed on an interval
    // so the crafting UI (which updates every frame) stays cheap.
    internal static class StorageStock
    {
        private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerNView =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");

        private static readonly MethodInfo ContainerCheckAccess =
            AccessTools.Method(typeof(Container), "CheckAccess", new[] { typeof(long) });

        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static float _lastScan = -999f;
        private static int _containersCounted;

        public static int ContainersCounted
        {
            get { return _containersCounted; }
        }

        public static int Get(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return 0;
            float refresh = Mathf.Max(0.25f, KMartBoysPlugin.StockRefreshSeconds.Value);
            if (Time.unscaledTime - _lastScan >= refresh) Rescan();
            int n;
            return Counts.TryGetValue(itemName, out n) ? n : 0;
        }

        public static void Invalidate()
        {
            _lastScan = -999f;
        }

        private static void Rescan()
        {
            _lastScan = Time.unscaledTime;
            Counts.Clear();
            _containersCounted = 0;

            Player player = Player.m_localPlayer;
            if (player == null) return;

            Vector3 origin = player.transform.position;
            float radius = KMartBoysPlugin.StockRadius.Value;
            bool onlyAccessible = KMartBoysPlugin.StockOnlyAccessible.Value;
            long playerId = player.GetPlayerID();

            foreach (Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                try
                {
                    if (c == null || !c.isActiveAndEnabled) continue;
                    if (radius > 0f && Vector3.Distance(origin, c.transform.position) > radius) continue;

                    ZNetView nview = ContainerNView(c);
                    if (nview == null || !nview.IsValid()) continue;

                    Inventory inv = c.GetInventory();
                    if (inv == null) continue;

                    if (c.GetComponentInParent<TombStone>() != null) continue; // graves aren't storage

                    if (onlyAccessible)
                    {
                        if (ContainerCheckAccess != null && !(bool)ContainerCheckAccess.Invoke(c, new object[] { playerId })) continue;
                        if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false, false)) continue;
                    }

                    _containersCounted++;
                    foreach (ItemDrop.ItemData item in inv.GetAllItems())
                    {
                        if (item == null || item.m_shared == null) continue;
                        string name = item.m_shared.m_name;
                        int existing;
                        Counts.TryGetValue(name, out existing);
                        Counts[name] = existing + item.m_stack;
                    }
                }
                catch (Exception e)
                {
                    KMartBoysPlugin.LogOnce("stockscan", "Storage scan skipped a container: " + e.Message);
                }
            }
        }

        public static int CountInInventory(Player player, string itemName)
        {
            if (player == null) return 0;
            Inventory inv = player.GetInventory();
            if (inv == null) return 0;
            int total = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item != null && item.m_shared != null && item.m_shared.m_name == itemName) total += item.m_stack;
            }
            return total;
        }
    }

    // Vanilla InventoryGui.SetupRequirement fills one requirement slot (icon, name,
    // amount) for both the crafting panel (craft = true) and the build menu (craft = false).
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class InventoryGui_SetupRequirement_Patch
    {
        private const string LabelName = "KMartBoys_Stock";
        private static bool _loggedLayout;

        private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, bool craft,
            int quality, int craftMultiplier, bool __result)
        {
            try
            {
                bool show = __result
                            && KMartBoysPlugin.ModEnabled.Value
                            && KMartBoysPlugin.StockEnabled.Value
                            && (craft || KMartBoysPlugin.StockInBuildMenu.Value)
                            && elementRoot != null && req != null && req.m_resItem != null && player != null;

                TMP_Text label = FindLabel(elementRoot);
                if (!show)
                {
                    if (label != null) label.gameObject.SetActive(false);
                    return;
                }

                TMP_Text amountText = FindAmountText(elementRoot);
                if (label == null) label = CreateLabel(elementRoot, amountText);
                if (label == null) return;

                LogLayoutOnce(elementRoot);

                string itemName = req.m_resItem.m_itemData.m_shared.m_name;
                int need = ReadNeed(amountText, req, quality, craftMultiplier);
                int have = StorageStock.CountInInventory(player, itemName);
                int stored = StorageStock.Get(itemName);
                int total = have + stored;

                label.text = Format(KMartBoysPlugin.StockFormat.Value, need, have, stored, total);
                label.color = total >= need ? KMartBoysPlugin.StockEnoughColor.Value : KMartBoysPlugin.StockShortColor.Value;
                label.fontSize = KMartBoysPlugin.StockFontSize.Value;

                RectTransform rt = (RectTransform)label.transform;
                rt.anchoredPosition = new Vector2(KMartBoysPlugin.StockOffsetX.Value, KMartBoysPlugin.StockOffsetY.Value);

                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("stockui", "Crafting stock display error: " + e);
            }
        }

        internal static TMP_Text FindLabel(Transform elementRoot)
        {
            if (elementRoot == null) return null;
            Transform t = elementRoot.Find(LabelName);
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        private static TMP_Text FindAmountText(Transform elementRoot)
        {
            Transform t = elementRoot.Find("res_amount");
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        // Build our label by cloning the vanilla amount text, so it uses the game's font and style.
        private static TMP_Text CreateLabel(Transform elementRoot, TMP_Text template)
        {
            GameObject go;
            if (template != null)
            {
                go = UnityEngine.Object.Instantiate(template.gameObject, elementRoot, false);
                // Strip anything cloned from children of the template.
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
            }
            else
            {
                go = new GameObject(LabelName, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(elementRoot, false);
            }
            go.name = LabelName;

            TMP_Text text = go.GetComponent<TMP_Text>();
            if (text == null) return null;

            // Sit just below the requirement slot, centered, free to overflow sideways.
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(120f, 40f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            text.alignment = TextAlignmentOptions.Top;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            SetNoWrap(text);

            rt.SetAsLastSibling();
            return text;
        }

        // TMP renamed its word-wrap setting between versions; set whichever exists.
        private static void SetNoWrap(TMP_Text text)
        {
            try
            {
                PropertyInfo mode = typeof(TMP_Text).GetProperty("textWrappingMode");
                if (mode != null && mode.PropertyType.IsEnum)
                {
                    mode.SetValue(text, Enum.ToObject(mode.PropertyType, 0), null); // NoWrap
                    return;
                }
                PropertyInfo wrap = typeof(TMP_Text).GetProperty("enableWordWrapping");
                if (wrap != null) wrap.SetValue(text, false, null);
            }
            catch
            {
                // Not critical.
            }
        }

        private static int ReadNeed(TMP_Text amountText, Piece.Requirement req, int quality, int craftMultiplier)
        {
            int parsed;
            if (amountText != null && int.TryParse(StripTags(amountText.text), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return req.GetAmount(quality) * Math.Max(1, craftMultiplier);
        }

        private static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char ch in s)
            {
                if (ch == '<') inTag = true;
                else if (ch == '>') inTag = false;
                else if (!inTag) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        private static string Format(string template, int need, int have, int stored, int total)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return template
                .Replace("\\n", "\n")
                .Replace("{need}", need.ToString())
                .Replace("{have}", have.ToString())
                .Replace("{stored}", stored.ToString())
                .Replace("{total}", total.ToString())
                .Replace("{missing}", Math.Max(0, need - total).ToString());
        }

        // Writes the requirement slot's layout to the BepInEx log once, to help tune OffsetX/OffsetY.
        private static void LogLayoutOnce(Transform elementRoot)
        {
            if (_loggedLayout) return;
            _loggedLayout = true;
            try
            {
                StringBuilder sb = new StringBuilder("Requirement slot layout: ");
                sb.Append(Path(elementRoot));
                RectTransform root = elementRoot as RectTransform;
                if (root != null) sb.Append(" size=").Append(root.rect.size);
                for (int i = 0; i < elementRoot.childCount; i++)
                {
                    Transform ch = elementRoot.GetChild(i);
                    RectTransform crt = ch as RectTransform;
                    sb.Append(" | ").Append(ch.name);
                    if (crt != null) sb.Append(" pos=").Append(crt.anchoredPosition).Append(" size=").Append(crt.rect.size);
                }
                KMartBoysPlugin.Log(sb.ToString());
            }
            catch
            {
                // Diagnostics only.
            }
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            int depth = 0;
            while (t.parent != null && depth++ < 4)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }
    }

    // Hide our label when the game hides a requirement slot.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.HideRequirement))]
    internal static class InventoryGui_HideRequirement_Patch
    {
        private static void Postfix(Transform elementRoot)
        {
            TMP_Text label = InventoryGui_SetupRequirement_Patch.FindLabel(elementRoot);
            if (label != null) label.gameObject.SetActive(false);
        }
    }
}
