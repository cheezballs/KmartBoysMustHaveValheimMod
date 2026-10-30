using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace KMartBoysMustHaveMod
{
    // ------------------------------------------------------------------
    // Floating ship names: a label drawn above every named ship within range.
    // Drawn on the HUD (so it hides with the HUD), positioned each frame.
    // ------------------------------------------------------------------
    internal static class ShipNameTags
    {
        private const float RescanEvery = 1f;

        private static readonly List<ShipCustomizer> Ships = new List<ShipCustomizer>();
        private static readonly List<TextMeshProUGUI> Labels = new List<TextMeshProUGUI>();
        private static float _nextScan;

        public static void Tick()
        {
            try
            {
                bool on = KMartBoysPlugin.ModEnabled.Value && KMartBoysPlugin.ShipNamesEnabled.Value
                          && KMartBoysPlugin.ShowShipNameTags.Value
                          && Hud.instance != null && Hud.instance.m_rootObject != null && Player.m_localPlayer != null;
                if (!on)
                {
                    HideFrom(0);
                    return;
                }

                if (Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + RescanEvery;
                    Ships.Clear();
                    foreach (ShipCustomizer s in UnityEngine.Object.FindObjectsByType<ShipCustomizer>(FindObjectsSortMode.None))
                        if (s != null && s.ShipName.Length > 0) Ships.Add(s);
                }

                Camera cam = Utils.GetMainCamera();
                RectTransform parent = Hud.instance.m_rootObject.transform as RectTransform;
                Canvas canvas = Hud.instance.m_rootObject.GetComponentInParent<Canvas>();
                if (cam == null || parent == null || canvas == null)
                {
                    HideFrom(0);
                    return;
                }
                Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

                float maxDist = KMartBoysPlugin.ShipNameTagDistance.Value;
                Vector3 playerPos = Player.m_localPlayer.transform.position;
                int used = 0;

                foreach (ShipCustomizer s in Ships)
                {
                    if (s == null) continue;
                    string name = s.ShipName;
                    if (name.Length == 0) continue;

                    if (Vector3.Distance(playerPos, s.transform.position) > maxDist) continue; // distance to the ship itself
                    Vector3 world = s.transform.position + Vector3.up * KMartBoysPlugin.ShipNameTagHeight.Value;

                    Vector3 screen = cam.WorldToScreenPoint(world);
                    if (screen.z <= 0f) continue; // behind the camera

                    Vector2 local;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCam, out local)) continue;

                    TextMeshProUGUI label = GetLabel(used++, parent);
                    label.text = name;
                    label.fontSize = KMartBoysPlugin.ShipNameTagFontSize.Value;
                    label.color = KMartBoysPlugin.ShipNameTagColor.Value;
                    ((RectTransform)label.transform).anchoredPosition = local;
                    if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
                }

                HideFrom(used);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("shiptags", "Ship name tag error: " + e.Message);
            }
        }

        private static TextMeshProUGUI GetLabel(int index, RectTransform parent)
        {
            while (Labels.Count <= index || Labels[index] == null)
            {
                TextMeshProUGUI t = ClockHud.CreateHudText("KMartBoys_ShipName", parent);
                GameObject go = t.gameObject;
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(500f, 50f);

                t.alignment = TextAlignmentOptions.Bottom;
                t.raycastTarget = false;
                t.enableAutoSizing = false;
                t.overflowMode = TextOverflowModes.Overflow;
                t.fontStyle = FontStyles.Bold;
                go.SetActive(false);

                if (Labels.Count <= index) Labels.Add(t);
                else Labels[index] = t;
            }
            return Labels[index];
        }

        private static void HideFrom(int index)
        {
            for (int i = index; i < Labels.Count; i++)
                if (Labels[i] != null && Labels[i].gameObject.activeSelf) Labels[i].gameObject.SetActive(false);
        }
    }
}
