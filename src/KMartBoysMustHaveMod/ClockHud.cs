using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace KMartBoysMustHaveMod
{
    public enum ClockAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    // ------------------------------------------------------------------
    // On-screen 12-hour clock (with AM/PM) plus the current weather.
    // Lives under the game's HUD root, so it hides whenever the HUD hides.
    // ------------------------------------------------------------------
    internal static class ClockHud
    {
        private const string ObjectName = "KMartBoys_Clock";
        private const float UpdateInterval = 0.25f;

        private static TextMeshProUGUI _text;
        private static float _nextUpdate;

        // Friendly names for the game's internal environment names.
        private static readonly Dictionary<string, string> WeatherNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Clear", "Clear" },
                { "Twilight_Clear", "Clear" },
                { "Heath clear", "Clear" },
                { "nofogts", "Clear" },
                { "Mistlands_clear", "Clear" },
                { "Misty", "Misty" },
                { "Ashlands_misty", "Misty" },
                { "DeepForest Mist", "Forest Mist" },
                { "Darklands_dark", "Dark" },
                { "Rain", "Rain" },
                { "LightRain", "Light Rain" },
                { "Mistlands_rain", "Rain" },
                { "SwampRain", "Swamp Rain" },
                { "ThunderStorm", "Thunderstorm" },
                { "Mistlands_thunder", "Thunderstorm" },
                { "Snow", "Snow" },
                { "Twilight_Snow", "Snow" },
                { "SnowStorm", "Snowstorm" },
                { "Twilight_SnowStorm", "Snowstorm" },
                { "Ashrain", "Ash Rain" },
                { "Ashlands_ashrain", "Ash Rain" },
                { "Ashlands_storm", "Ash Storm" },
                { "Ashlands_CinderRain", "Cinder Rain" },
                { "Eikthyr", "Boss Storm" },
                { "GDKing", "Eerie" },
                { "Bonemass", "Toxic" },
                { "Moder", "Blizzard" },
                { "GoblinKing", "Ominous" },
                { "Queen", "Ominous" },
                { "Fader", "Inferno" },
                { "Crypt", "Crypt" },
                { "SunkenCrypt", "Sunken Crypt" },
                { "Caves", "Cave" },
                { "InfectedMine", "Infested Mine" }
            };

        public static void Tick()
        {
            try
            {
                bool want = KMartBoysPlugin.ModEnabled.Value && KMartBoysPlugin.ClockEnabled.Value
                            && Hud.instance != null && Hud.instance.m_rootObject != null
                            && EnvMan.instance != null && Player.m_localPlayer != null;

                if (!want)
                {
                    if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
                    return;
                }

                if (_text == null) Create();
                if (_text == null) return;
                if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);

                if (Time.unscaledTime < _nextUpdate) return;
                _nextUpdate = Time.unscaledTime + UpdateInterval;

                ApplyLayout();
                _text.text = BuildText();
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("clock", "Clock error: " + e.Message);
            }
        }

        private static void Create()
        {
            Transform root = Hud.instance.m_rootObject.transform;
            Transform existing = root.Find(ObjectName);
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject(ObjectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            if (existing == null) go.transform.SetParent(root, false);

            _text = go.GetComponent<TextMeshProUGUI>();
            _text.raycastTarget = false;
            _text.richText = true;
            _text.enableAutoSizing = false;
            _text.overflowMode = TextOverflowModes.Overflow;

            // Borrow the game's HUD font so the clock looks native.
            TMP_Text source = Hud.instance.m_hoverName;
            if (source != null && source.font != null)
            {
                _text.font = source.font;
                _text.fontSharedMaterial = source.fontSharedMaterial;
            }

            ((RectTransform)go.transform).sizeDelta = new Vector2(600f, 40f);
            ApplyLayout();
        }

        private static void ApplyLayout()
        {
            RectTransform rt = (RectTransform)_text.transform;
            Vector2 anchor;
            TextAlignmentOptions align;
            switch (KMartBoysPlugin.ClockPosition.Value)
            {
                case ClockAnchor.TopLeft: anchor = new Vector2(0f, 1f); align = TextAlignmentOptions.TopLeft; break;
                case ClockAnchor.TopRight: anchor = new Vector2(1f, 1f); align = TextAlignmentOptions.TopRight; break;
                case ClockAnchor.BottomLeft: anchor = new Vector2(0f, 0f); align = TextAlignmentOptions.BottomLeft; break;
                case ClockAnchor.BottomCenter: anchor = new Vector2(0.5f, 0f); align = TextAlignmentOptions.Bottom; break;
                case ClockAnchor.BottomRight: anchor = new Vector2(1f, 0f); align = TextAlignmentOptions.BottomRight; break;
                default: anchor = new Vector2(0.5f, 1f); align = TextAlignmentOptions.Top; break;
            }

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = new Vector2(KMartBoysPlugin.ClockOffsetX.Value, KMartBoysPlugin.ClockOffsetY.Value);
            _text.alignment = align;
            _text.fontSize = KMartBoysPlugin.ClockFontSize.Value;
            _text.color = KMartBoysPlugin.ClockColor.Value;
        }

        private static string BuildText()
        {
            EnvMan env = EnvMan.instance;
            float frac = env.GetDayFraction(); // 0 = midnight, 0.25 = 6 AM, 0.5 = noon
            int totalMinutes = Mathf.FloorToInt(Mathf.Repeat(frac, 1f) * 24f * 60f);
            int step = Mathf.Max(1, KMartBoysPlugin.ClockMinuteStep.Value);
            totalMinutes -= totalMinutes % step;

            int hour24 = (totalMinutes / 60) % 24;
            int minute = totalMinutes % 60;
            int hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
            string ampm = hour24 < 12 ? "AM" : "PM";
            string time = hour12 + ":" + minute.ToString("00") + " " + ampm;

            EnvSetup setup = env.GetCurrentEnvironment();
            string weather = setup != null ? Friendly(setup.m_name) : "";
            string conditions = Conditions(setup, EnvMan.IsNight());

            string result = (KMartBoysPlugin.ClockFormat.Value ?? "{time}")
                .Replace("\\n", "\n")
                .Replace("{time}", time)
                .Replace("{weather}", weather)
                .Replace("{conditions}", conditions)
                .Replace("{day}", env.GetDay().ToString());

            // Tidy up separators left dangling by an empty {conditions}.
            return result.Replace("()", "").Replace("[]", "").Trim();
        }

        private static string Conditions(EnvSetup setup, bool night)
        {
            if (setup == null) return "";
            List<string> parts = new List<string>(2);
            if (setup.m_isFreezing || (night && setup.m_isFreezingAtNight)) parts.Add("Freezing");
            else if (setup.m_isCold || (night && setup.m_isColdAtNight)) parts.Add("Cold");
            if (setup.m_isWet) parts.Add("Wet");
            return string.Join(", ", parts.ToArray());
        }

        private static string Friendly(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string name;
            if (WeatherNames.TryGetValue(raw, out name)) return name;

            // Unknown (new biome / modded): "DeepNorth_snowstorm" -> "Snowstorm", "LightRain" -> "Light Rain".
            string s = raw;
            int underscore = s.LastIndexOf('_');
            if (underscore >= 0 && underscore < s.Length - 1) s = s.Substring(underscore + 1);
            StringBuilder sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(s[i - 1])) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }
    }
}
