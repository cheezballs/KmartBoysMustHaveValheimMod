// K-MartBoysMustHaveMod for Valheim
//
//   * Deposit key: push items into nearby containers that already hold the same item
//   * Hotbar / equipped / locked-slot filters, Equipment and Quick Slots compatibility
//   * Middle-click slot locking with a purple "will not deposit" marker
//   * Crafting stock: requirement slots show how many you carry + have stored in chests
//   * Craft / upgrade / build with materials from nearby containers
//   * Auto-refuel fires, ovens and smelters from nearby containers
//   * Announces your deaths in chat
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
        public const string PluginVersion = "1.8.0";

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
        internal static ConfigEntry<bool> IgnoreAmmo;
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
        internal static ConfigEntry<bool> CraftEnabled;
        internal static ConfigEntry<bool> CraftAllowBuilding;
        internal static ConfigEntry<float> CraftRadius;
        internal static ConfigEntry<bool> CraftIgnoreMoving;
        internal static ConfigEntry<bool> CraftOnlyPlayerBuilt;
        internal static ConfigEntry<bool> CraftFixColors;
        internal static ConfigEntry<bool> RefuelEnabled;
        internal static ConfigEntry<float> RefuelInterval;
        internal static ConfigEntry<float> RefuelRadius;
        internal static ConfigEntry<bool> RefuelFireplaces;
        internal static ConfigEntry<bool> RefuelOvens;
        internal static ConfigEntry<bool> RefuelSmelters;
        internal static ConfigEntry<float> RefuelBelowPercent;
        internal static ConfigEntry<int> RefuelKeepReserve;
        internal static ConfigEntry<bool> RefuelUseInventory;
        internal static ConfigEntry<string> RefuelExcluded;
        internal static ConfigEntry<bool> RefuelShowMessage;
        internal static ConfigEntry<bool> FillSmelters;
        internal static ConfigEntry<bool> FillEitrRefinery;
        internal static ConfigEntry<bool> FillKilns;
        internal static ConfigEntry<bool> FillWindmills;
        internal static ConfigEntry<bool> FillSpinningWheels;
        internal static ConfigEntry<bool> DeathMessageEnabled;
        internal static ConfigEntry<string> DeathMessages;
        internal static ConfigEntry<DeathChatChannel> DeathMessageChannel;

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
            IgnoreAmmo = Bind(filters, "IgnoreAmmo", "Skip ammo", true,
                "Never deposit ammo (arrows, bolts and other ammo you can equip), even if a chest already has the same kind.");
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

            const string craft = "7 - Craft From Containers";
            CraftEnabled = Bind(craft, "Enabled", "Craft from containers", true,
                "Crafting and upgrading can use materials from nearby containers. Your inventory is used first, then the nearest containers.");
            CraftAllowBuilding = Bind(craft, "AllowBuilding", "Build from containers", true,
                "Building (hammer, hoe, cultivator) can also use materials from nearby containers.");
            CraftRadius = Bind(craft, "ContainerRadius", "Container radius (m)", 30f,
                "How far from you (in meters) containers are used for crafting, building and auto-refuel fuel.",
                new AcceptableValueRange<float>(1f, 100f));
            CraftIgnoreMoving = Bind(craft, "IgnoreCartsAndShips", "Ignore carts and ships", true,
                "Don't use containers on carts or ships (safer in multiplayer).");
            CraftOnlyPlayerBuilt = Bind(craft, "OnlyPlayerBuiltContainers", "Only player-built containers", true,
                "Only use containers built by players, so dungeon and world loot chests are never touched.");
            CraftFixColors = Bind(craft, "CountContainersInRequirementColors", "Requirement colors count containers", true,
                "Requirement slots only flash red when your inventory AND nearby containers together are short.");

            const string refuel = "8 - Auto Refuel";
            RefuelEnabled = Bind(refuel, "Enabled", "Auto refuel", true,
                "Every few seconds, top up fuel in nearby fires, ovens and smelters, and fill smelters, refineries, kilns, windmills and " +
                "spinning wheels with their inputs, all from nearby containers.");
            RefuelInterval = Bind(refuel, "IntervalSeconds", "Check every (s)", 5f,
                "How often (in seconds) to check nearby fuel levels.", new AcceptableValueRange<float>(1f, 120f));
            RefuelRadius = Bind(refuel, "Radius", "Refuel radius (m)", 20f,
                "Fires, ovens, smelters and other processors within this many meters of you get refilled.",
                new AcceptableValueRange<float>(1f, 100f));
            RefuelFireplaces = Bind(refuel, "Fires", "Refuel fires", true,
                "Campfires, hearths, braziers, bonfires, torches, sconces, hot tubs and anything else that burns fuel.");
            RefuelOvens = Bind(refuel, "Ovens", "Refuel ovens", true, "Stone ovens and other cooking stations that use fuel.");
            RefuelSmelters = Bind(refuel, "Smelters", "Refuel smelters", true,
                "Fuel for smelters, blast furnaces (coal) and eitr refineries (sap).");
            FillSmelters = Bind(refuel, "FillSmelterOre", "Fill smelters & furnaces with ore", true,
                "Add ore and scrap (copper, tin, iron, silver, black metal, flametal...) to smelters and blast furnaces.");
            FillEitrRefinery = Bind(refuel, "FillEitrRefinery", "Fill eitr refineries", true,
                "Add soft tissue to eitr refineries.");
            FillKilns = Bind(refuel, "FillKilns", "Fill charcoal kilns", true,
                "Add wood to charcoal kilns. Careful: this turns the wood in your chests into coal. Use KeepReserve to hold some back.");
            FillWindmills = Bind(refuel, "FillWindmills", "Fill windmills", true, "Add barley to windmills.");
            FillSpinningWheels = Bind(refuel, "FillSpinningWheels", "Fill spinning wheels", true, "Add flax to spinning wheels.");
            RefuelBelowPercent = Bind(refuel, "RefillBelowPercent", "Refill below (%)", 100f,
                "Only refuel when fuel drops below this percent of max. 100 = keep everything topped off.",
                new AcceptableValueRange<float>(1f, 100f));
            RefuelKeepReserve = Bind(refuel, "KeepReserve", "Keep in reserve", 0,
                "Leave at least this many of each fuel or input item (wood, ore, coal...) in your containers.", new AcceptableValueRange<int>(0, 10000));
            RefuelUseInventory = Bind(refuel, "UseInventoryFuel", "Also use your inventory", false,
                "If the containers run out, also take fuel and inputs (ore, wood...) from your own inventory.");
            RefuelExcluded = Bind(refuel, "ExcludedObjects", "Never refuel these", "",
                "Comma-separated prefab names to never refuel, e.g. piece_groundtorch_wood, piece_brazierceiling01.");
            RefuelShowMessage = Bind(refuel, "ShowMessage", "Show refuel message", false,
                "Show a small top-left message when something was refueled.");

            const string death = "9 - Death Message";
            DeathMessageEnabled = Bind(death, "Enabled", "Announce your deaths", true,
                "When you die, post a message in chat.");
            DeathMessages = Bind(death, "Messages", "Death message(s)", "I WAS AN IDIOT AND I DIED HAHAHAHAHAHA",
                "What you say in chat when you die. Separate several with | and one is picked at random. {name} = your character's name.");
            DeathMessageChannel = Bind(death, "Channel", "Chat channel", DeathChatChannel.Shout,
                "Shout = everyone in the world sees it. Normal = only players near you.");

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

        // Set at startup if an overlapping mod is loaded; our matching feature then stays off.
        internal static bool OtherCraftModLoaded;
        internal static bool OtherRefuelModLoaded;

        private static readonly string[] CraftFromContainerMods =
        {
            "com.mikeg.valheim.nearbycrafting", "aedenthorn.CraftFromContainers", "Azumatt.AzuCraftyBoxes",
            "org.bepinex.plugins.craftfromcontainers", "com.maxsch.valheim.MultiUserChest"
        };

        private static readonly string[] RefuelMods = { "simplifydave.autorefillfires", "aedenthorn.AutoFuel" };

        // Runs after every plugin's Awake, so other mods are known by now.
        private void Start()
        {
            foreach (string guid in CraftFromContainerMods)
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(guid)) continue;
                OtherCraftModLoaded = true;
                Logger.LogWarning("Another craft-from-containers mod is installed (" + guid + "). " +
                                  "This mod's Craft From Containers is turned OFF to avoid counting chests twice. Remove that mod to use ours.");
            }
            foreach (string guid in RefuelMods)
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(guid)) continue;
                OtherRefuelModLoaded = true;
                Logger.LogWarning("Another auto-refuel mod is installed (" + guid + "). " +
                                  "This mod's Auto Refuel is turned OFF to avoid double refueling. Remove that mod to use ours.");
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
            Refueler.Tick(player);

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
