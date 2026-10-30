# K-MartBoysMustHaveMod

A Valheim BepInEx mod for the K-Mart Boys. It's on Thunderstore as **KMartBoysMustHaveMod**.

## Features

- **Auto-deposit (F6):** pushes everything in your inventory into nearby chests that already hold the same item. It tops up existing stacks first, then uses empty slots, nearest chest first.
- **Filters:** it can skip the hotbar, equipped gear and ammo (arrows, bolts). It never deposits the item you're dragging.
- **Slot locking:** middle-click an inventory slot to lock it. Locked slots get a purple marker and are never deposited. Locks are saved per character.
- **Messages:** a center-screen "Great Job, Jackass" message whose wording scales with how much you deposited, plus a small summary in the top-left.
- **Craft from containers:** crafting, upgrading and building can use materials from containers within a configurable radius. Your inventory is used first, then the nearest containers.
- **Auto refill:** every few seconds, fires, hearths, braziers, torches, hot tubs, ovens and smelters near you are topped up with fuel from those same containers. Smelters, blast furnaces, eitr refineries, kilns, windmills and spinning wheels also get their inputs (ore, scrap, soft tissue, wood, barley, flax).
- **Crafting stock:** at a crafting station, each required material shows how many you carry, how many are in your storage boxes, and the total. It's green when you have enough and orange when you're short.
- **Auto repair:** opening a crafting station repairs everything in your inventory that the station can repair.
- **Clock:** a 12-hour in-game clock with AM/PM and the current weather. Format, position, size and color are configurable.
- **Ships & banners:** custom sail and banner images from a URL, and ship names floating above the ships. Values sync through the ship or banner itself, so no server mod is needed. Compatible with sails set by the old Gorko mod.
- **Death announcements:** when you die, you shout "I WAS AN IDIOT AND I DIED HAHAHAHAHAHA" in chat. The messages are configurable.
- **Sorting:** a Sort button on your inventory and on chests sorts by name, type, value, quality, power or weight. Right-click it to switch modes.
- **Compatibility:** detects Equipment and Quick Slots and never touches its equipment, quick-slot or quiver cells.
- **Safety:** skips graves, chests someone else has open, private chests you don't own, and chests in wards you can't access.
- **Fully configurable:** every option works in Configuration Manager (F1), and editing the .cfg file while the game runs takes effect immediately.
- **Client-side only:** nobody else needs it installed, including the server.

## Repo layout

```
src/KMartBoysMustHaveMod/      C# source + project file
  Plugin.cs                    Plugin entry point, all config entries, hotkey handling
  Depositor.cs                 Finds nearby chests and moves items into them
  SlotLocks.cs                 Locked-slot storage (saved in the character's custom data)
  SlotLockUi.cs                Middle-click locking + the purple slot marker
  CraftingStock.cs             Material counts under crafting requirements
  ContainerSource.cs           Nearby-container resource pool (filters, counting, removal)
  CraftFromContainers.cs       Craft / upgrade / build using container materials
  AutoRepair.cs                Repair everything when you open a crafting station
  ClockHud.cs                  12-hour clock + weather on the HUD
  InventorySorter.cs           Sort buttons and sorting logic
  ShipCustomization.cs         Custom sail/banner images, ship names, favorite sail
  ShipNameTags.cs              Floating ship names on the HUD
  DeathMessage.cs              Chat announcement when you die
  Refueler.cs                  Auto-refill fuel and inputs (fires, ovens, smelters, refineries, kilns...)
  ModCompat.cs                 Equipment and Quick Slots soft compatibility
  ConfigurationManagerAttributes.cs
thunderstore/                  manifest.json, icon.png, README.md (backwards), CHANGELOG.md
docs/thunderstore-readme.forward.md   Source for the backwards Thunderstore README
scripts/make_backwards_readme.py      Regenerates thunderstore/README.md from the source above
art/icon.svg                   Source for icon.png
```

## Building

Requirements:
- .NET SDK 8 or newer
- Valheim
- A mod manager profile with BepInExPack Valheim installed

```
dotnet build -c Release
```

The project compiles against your own game files, so nothing from the game is stored in this repo. By default it looks for:

- **Valheim:** `C:\Program Files (x86)\Steam\steamapps\common\Valheim`
- **BepInEx:** `%APPDATA%\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx`

If your paths are different, copy `Local.props.example` to `Local.props` and edit it. `Local.props` is git-ignored.

Every build copies `K-MartBoysMustHaveMod.dll` into that profile's `BepInEx\plugins\K-MartBoysMustHaveMod\` folder, so you can build and launch straight away. Don't also install the Thunderstore version in the same profile, or two copies load. To turn the copy off, set `DeployToProfile` to `false` in `Local.props`.

A **Release** build also creates the Thunderstore upload zip at `dist\KMartBoysMustHaveMod-<version>.zip`.

## Releasing a new version

1. Bump the version in all three places:
   - `<Version>` in `src/KMartBoysMustHaveMod/KMartBoysMustHaveMod.csproj`
   - `PluginVersion` in `src/KMartBoysMustHaveMod/Plugin.cs`
   - `version_number` in `thunderstore/manifest.json`
2. Add an entry to `thunderstore/CHANGELOG.md`.
3. If the README changed, edit `docs/thunderstore-readme.forward.md` and run `python scripts/make_backwards_readme.py`.
4. Run `dotnet build -c Release` and upload `dist\KMartBoysMustHaveMod-<version>.zip` to Thunderstore.

Thunderstore rejects a version number that's already been uploaded.
