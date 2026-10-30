# Changelog

## 1.8.0
- **Death announcements:** when you die, you shout "I WAS AN IDIOT AND I DIED HAHAHAHAHAHA" in chat. Set your own messages (several, picked at random), use {name}, or switch to nearby-only chat in "9 - Death Message".

## 1.7.0
- **Auto refill now fills inputs too:** ore and scrap into smelters and blast furnaces, soft tissue into eitr refineries, wood into charcoal kilns, barley into windmills, flax into spinning wheels. Each type has its own on/off switch, all on by default.
- Smelter and eitr refinery fuel (coal, sap) is still refilled as before.
- KeepReserve and UseInventoryFuel now apply to inputs as well as fuel.

## 1.6.0
- **Craft from containers:** crafting, upgrading and building can use materials from containers within a configurable radius (default 30 m). Your inventory is used first, then the nearest containers. Requirement slots only flash red when you're short across both.
- **Auto refuel:** every few seconds (default 5), fires, hearths, braziers, torches, hot tubs, ovens and smelters within a configurable radius (default 20 m) are topped up with fuel from those same containers. 
- New config sections "7 - Craft From Containers" and "8 - Auto Refuel": radius, carts/ships, player-built containers only, building on/off, interval, refill threshold, fuel reserve, inventory fuel, excluded objects, per-type toggles.
- Replaces separate craft-from-containers and auto-refill-fire mods. If one of those is still installed, the matching feature here turns itself off and logs a warning, so chests never get counted twice.

## 1.5.0
- **Crafting stock:** at a crafting station, each required material shows how many you carry, how many are in your storage boxes, and the total. Green when you have enough, orange when you're short.
- New config section "6 - Crafting Stock": text format, colors, font size, position, storage radius, refresh rate, chests you can't open, build menu.
- New "Skip ammo" option (on by default): arrows, bolts and other equippable ammo are never deposited.
- Source is now on GitHub.

## 1.4.1
- Version bump for the Thunderstore re-upload.

## 1.4.0
- Renamed to K-MartBoysMustHaveMod.

## 1.3.0
- Every setting and message is configurable, including the six "Great Job, Jackass" tiers.
- Configuration Manager display names and ordering.
- The config file reloads automatically when you edit it while the game is running.

## 1.2.0
- Deposit key changed to F6.
- Purple lock marker.
- Center-screen "Great Job, Jackass" message that scales with how much you deposited.

## 1.1.0
- Equipment and Quick Slots compatibility: its equipment slots, quick slots and quiver slots are never deposited.
- Slot locking moved to middle-click (Left Alt was already taken by other mods).

## 1.0.0
- First version: deposit into matching nearby chests, hotbar and equipped filters, slot locking.
