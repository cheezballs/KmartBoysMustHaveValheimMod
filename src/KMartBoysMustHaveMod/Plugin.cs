// K-MartBoysMustHaveMod for Valheim
//
//   * Deposit key: push items into nearby containers that already hold the same item
//   * Hotbar / equipped / locked-slot filters, Equipment and Quick Slots compatibility
//   * Middle-click slot locking with a purple "will not deposit" marker
//   * Crafting stock: requirement slots show how many you carry + have stored in chests
//   * Everything configurable, Configuration Manager friendly, live config reload

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
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class KMartBoysPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "matt.valheim.kmartboysmusthavemod";
        public const string PluginName = "K-MartBoysMustHaveMod";
        public const string PluginVersion = "1.5.0";

        internal static KMartBoysPlugin Instance;

        // ---- Config ----
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<KeyCode> DepositKey;
        internal static ConfigEntry<KeyCode> DepositModifier;
        internal static ConfigEntry<float> Radius;
        internal static ConfigEntry<bool> AllowWhileInventoryOpen;
        internal static ConfigEntry<bool> IgnoreHotbar;
        internal static ConfigEntry<bool> IgnoreEquipped;
        internal static ConfigEntry<bool> DepositNonStackables;
        internal static ConfigEntry<bool> SlotLockEnabled;
        internal static ConfigEntry<LockClick> SlotLockClick;
        internal static ConfigEntry<KeyCode> SlotLockModifier;
        internal static ConfigEntry<Color> SlotLockColor;
        internal static ConfigEntry<bool> CenterMessageEnabled;
        internal static ConfigEntry<bool> SummaryMessageEnabled;
        internal static ConfigEntry<string> SummaryMessage;
        internal static ConfigEntry<string> NothingToDepositMessage;
        internal static ConfigEntry<string> NoContainersMessage;
        internal static ConfigEntry<int>[] TierMinItems;
        internal static ConfigEntry<string>[] TierMessages;
        internal static ConfigEntry<bool> StockEnabled;
        internal static ConfigEntry<bool> StockInBuildMenu;
        internal static ConfigEntry<string> StockFormat;
        internal static ConfigEntry<Color> StockEnoughColor;
        internal static ConfigEntry<Color> StockShortColor;
        internal static ConfigEntry<float> StockFontSize;
        internal static ConfigEntry<float> StockOffsetX;
        internal static ConfigEntry<float> StockOffsetY;
        internal static ConfigEntry<float> StockRadius;
        internal static ConfigEntry<float> StockRefreshSeconds;
        internal static ConfigEntry<bool> StockOnlyAccessible;

        private const int TierCount = 6;
        private static readonly int[] DefaultTierMin = { 1, 2, 10, 50, 200, 500 };
        private static readonly string[] DefaultTierText =
        {
            "One whole item. Great Job, Jackass.",
            "Great Job, Jackass",
            "{items} items stashed. Great Job, Jackass!",
            "Look at you go! {items} items. Great Job, Jackass!",
            "{items} items?! Your back thanks you. Great Job, Jackass!",
            "{items} ITEMS ACROSS {containers} CHESTS. Odin weeps. Great Job, Jackass!!"
        };

        private const string PlaceholderHelp =
            " Placeholders: {items} = number of items, {containers} = number of containers, " +
            "{itemsWord} = item/items, {containersWord} = container/containers.";

        private Harmony _harmony;
        private FileSystemWatcher _watcher;
        private volatile bool _configFileChanged;
        private float _reloadAt;
        private int _order;

        // Binds an entry with Configuration Manager metadata (display name + ordering).
        private ConfigEntry<T> Bind<T>(string section, string key, string dispName, T defaultValue,
            string description, AcceptableValueBase range = null)
        {
            ConfigurationManagerAttributes attrs = new ConfigurationManagerAttributes
            {
                DispName = dispName,
                Order = 1000 - (_order++) // Configuration Manager shows higher Order first
            };
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, range, attrs));
        }

        private void Awake()
        {
            Instance = this;
            Config.SaveOnConfigSet = true;

            const string general = "1 - General";
            ModEnabled = Bind(general, "Enabled", "Mod enabled", true, "Turn the whole mod on or off.");
            DepositKey = Bind(general, "DepositKey", "Deposit key", KeyCode.F6,
                "Key that deposits your items into nearby matching containers.");
            DepositModifier = Bind(general, "DepositModifier", "Deposit modifier key", KeyCode.None,
                "Optional key that must be held together with the deposit key (e.g. LeftShift). None = no modifier.");
            Radius = Bind(general, "Radius", "Search radius (m)", 10f,
                "Radius in meters around you to search for containers.",
                new AcceptableValueRange<float>(1f, 50f));
            AllowWhileInventoryOpen = Bind(general, "AllowWhileInventoryOpen", "Works with inventory open", true,
                "Allow the deposit key to work while the inventory screen is open.");

            const string filters = "2 - Filters";
            IgnoreHotbar = Bind(filters, "IgnoreHotbar", "Skip hotbar", true,
                "Never deposit items in your hotbar (the top row of the inventory, slots 1-8).");
            IgnoreEquipped = Bind(filters, "IgnoreEquipped", "Skip equipped items", true,
                "Never deposit items you have equipped (weapons, tools, armor, utility items).");
            DepositNonStackables = Bind(filters, "DepositNonStackables", "Deposit non-stackables", true,
                "Also deposit non-stacking items (tools, weapons, armor) when a container already has the same item.");

            const string locking = "3 - Slot Locking";
            SlotLockEnabled = Bind(locking, "Enabled", "Slot locking enabled", true,
                "Allow locking inventory slots so they are never deposited.");
            SlotLockClick = Bind(locking, "LockClick", "Lock mouse button", LockClick.MiddleClick,
                "Mouse button that locks / unlocks the inventory slot under the cursor (inventory open). Disabled = off.");
            SlotLockModifier = Bind(locking, "LockModifierKey", "Lock modifier + left-click", KeyCode.None,
                "Optional alternative: hold this key and left-click a slot to lock / unlock it. None = off. " +
                "(LeftAlt is already used by Crop Utils, Better Archery and Equipment and Quick Slots.)");
            SlotLockColor = Bind(locking, "LockMarkerColor", "Lock marker color", new Color(0.6f, 0.2f, 0.95f, 0.5f),
                "Color of the marker drawn on locked slots (RGBA). The corner badge uses the same color at full opacity.");

            const string messages = "4 - Messages";
            CenterMessageEnabled = Bind(messages, "CenterMessageEnabled", "Show center message", true,
                "Show the big center-screen message after depositing (wording set by the tiers below).");
            SummaryMessageEnabled = Bind(messages, "SummaryMessageEnabled", "Show summary message", true,
                "Show the small top-left summary / status messages.");
            SummaryMessage = Bind(messages, "SummaryMessage", "Summary text",
                "Deposited {items} {itemsWord} into {containers} {containersWord}",
                "Top-left message after a successful deposit." + PlaceholderHelp);
            NothingToDepositMessage = Bind(messages, "NothingToDepositMessage", "Nothing-to-deposit text",
                "Nothing to deposit", "Top-left message when nothing matched. Leave empty for no message.");
            NoContainersMessage = Bind(messages, "NoContainersMessage", "No-containers text",
                "No containers in range", "Top-left message when no usable containers are nearby. Leave empty for no message.");

            const string tiers = "5 - Center Message Tiers";
            TierMinItems = new ConfigEntry<int>[TierCount];
            TierMessages = new ConfigEntry<string>[TierCount];
            for (int i = 0; i < TierCount; i++)
            {
                int n = i + 1;
                TierMinItems[i] = Bind(tiers, "Tier" + n + "MinItems", "Tier " + n + " - min items", DefaultTierMin[i],
                    "Tier " + n + " is used when at least this many items were deposited (the highest matching tier wins). 0 = tier disabled.",
                    new AcceptableValueRange<int>(0, 100000));
                TierMessages[i] = Bind(tiers, "Tier" + n + "Message", "Tier " + n + " - message", DefaultTierText[i],
                    "Center-screen text for tier " + n + ". Leave empty to disable this tier." + PlaceholderHelp);
            }

            const string stock = "6 - Crafting Stock";
            StockEnabled = Bind(stock, "Enabled", "Show stock at crafting", true,
                "Under each required material at a crafting station, show how many you carry and how many are in your storage boxes.");
            StockInBuildMenu = Bind(stock, "ShowInBuildMenu", "Also show in build menu", false,
                "Also show the stock line under build-piece requirements (hammer, hoe, cultivator).");
            StockFormat = Bind(stock, "Format", "Stock text",
                "{total} total\n<size=80%>{have} bag | {stored} box</size>",
                "Text shown under each requirement. Placeholders: {need} = amount required, {have} = in your inventory, " +
                "{stored} = in storage boxes, {total} = have + stored, {missing} = how many more you need (0 if enough). " +
                "\\n = new line. Unity rich text tags like <size=80%> and <color=#ff0> work.");
            StockEnoughColor = Bind(stock, "EnoughColor", "Color when you have enough", new Color(0.55f, 0.95f, 0.55f, 1f),
                "Text color when inventory + storage covers the requirement.");
            StockShortColor = Bind(stock, "ShortColor", "Color when short", new Color(1f, 0.55f, 0.35f, 1f),
                "Text color when inventory + storage is not enough.");
            StockFontSize = Bind(stock, "FontSize", "Font size", 14f, "Font size of the stock text.",
                new AcceptableValueRange<float>(6f, 40f));
            StockOffsetX = Bind(stock, "OffsetX", "Position offset X", 0f,
                "Move the stock text left (-) or right (+), in UI pixels.", new AcceptableValueRange<float>(-300f, 300f));
            StockOffsetY = Bind(stock, "OffsetY", "Position offset Y", -2f,
                "Move the stock text up (+) or down (-), in UI pixels. 0 = just below the requirement slot.",
                new AcceptableValueRange<float>(-300f, 300f));
            StockRadius = Bind(stock, "StorageRadius", "Storage search radius (m)", 0f,
                "Only count containers within this many meters of you. 0 = every container currently loaded around you " +
                "(roughly your whole base).", new AcceptableValueRange<float>(0f, 500f));
            StockRefreshSeconds = Bind(stock, "RefreshSeconds", "Storage refresh interval (s)", 1f,
                "How often storage boxes are re-counted while a crafting screen is open.",
                new AcceptableValueRange<float>(0.25f, 10f));
            StockOnlyAccessible = Bind(stock, "OnlyAccessibleContainers", "Only count chests you can open", true,
                "Skip private chests you don't own and chests in wards you can't access. Graves are never counted.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            WatchConfigFile();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        // Reload the config when the .cfg file is edited while the game is running.
        private void WatchConfigFile()
        {
            try
            {
                string path = Config.ConfigFilePath;
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path));
                _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                _watcher.Changed += OnConfigFileEvent;
                _watcher.Created += OnConfigFileEvent;
                _watcher.Renamed += OnConfigFileEvent;
                _watcher.IncludeSubdirectories = false;
                _watcher.SynchronizingObject = null;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Could not watch config file for changes: " + e.Message);
            }
        }

        private void OnConfigFileEvent(object sender, FileSystemEventArgs e)
        {
            _configFileChanged = true; // handled on the main thread in Update
        }

        private void ReloadConfigIfChanged()
        {
            if (_configFileChanged)
            {
                _configFileChanged = false;
                _reloadAt = Time.unscaledTime + 0.5f; // debounce editor/CM double-writes
            }
            if (_reloadAt > 0f && Time.unscaledTime >= _reloadAt)
            {
                _reloadAt = 0f;
                try
                {
                    Config.Reload();
                }
                catch (Exception e)
                {
                    Logger.LogWarning("Config reload failed: " + e.Message);
                }
            }
        }

        private void OnDestroy()
        {
            if (_watcher != null) _watcher.Dispose();
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private void Update()
        {
            ReloadConfigIfChanged();
            if (!ModEnabled.Value) return;
            Player player = Player.m_localPlayer;
            if (player == null) return;

            SlotLockInput.Poll(player);

            if (!ZInput.GetKeyDown(DepositKey.Value, false)) return;
            if (DepositModifier.Value != KeyCode.None && !ZInput.GetKey(DepositModifier.Value, false)) return;
            if (!CanTakeHotkey()) return;

            Depositor.DepositNearby(player);
        }

        private static bool CanTakeHotkey()
        {
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Console.IsVisible()) return false;
            if (TextInput.IsVisible()) return false;
            if (Menu.IsVisible()) return false;
            if (Minimap.IsOpen()) return false;
            if (StoreGui.IsVisible()) return false;
            if (GameCamera.InFreeFly()) return false;
            if (!AllowWhileInventoryOpen.Value && InventoryGui.IsVisible()) return false;
            return true;
        }

        internal static void Log(string msg)
        {
            if (Instance != null) Instance.Logger.LogInfo(msg);
        }

        private static readonly HashSet<string> LoggedKeys = new HashSet<string>();

        // Logs a warning only the first time a given key is seen (avoids per-frame log spam).
        internal static void LogOnce(string key, string msg)
        {
            if (Instance == null || !LoggedKeys.Add(key)) return;
            Instance.Logger.LogWarning(msg);
        }
    }
}
