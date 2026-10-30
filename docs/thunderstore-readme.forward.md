# K-MartBoysMustHaveMod

Press one key and everything in your bags that already lives in a nearby chest goes home. Great Job, Jackass.

## What it does

- Press **F6** to deposit items into every nearby container that already holds the same item.
- Existing stacks get topped up first, then empty slots are used.
- Your hotbar and ammo (arrows, bolts) can be skipped, and equipped gear is never touched.
- **Middle-click** an inventory slot to lock it. Locked slots glow purple and are never deposited.
- A center-screen message reacts to how much you hauled, from "One whole item" all the way to "Odin weeps".
- Works with Equipment and Quick Slots: quick slots, equipment slots and the quiver are always skipped.
- Skips graves, chests someone else has open, private chests and warded chests you cannot access.
- Craft, upgrade and build using materials from containers near you. Your inventory is used first, then the nearest chests.
- Fires, hearths, braziers, torches, hot tubs, ovens and smelters near you get refueled automatically from those same chests.
- Smelters, blast furnaces, eitr refineries, kilns, windmills and spinning wheels get filled with ore, scrap, soft tissue, wood, barley and flax from those chests too.
- At a crafting station, every ingredient shows how many you have across your bag and chests next to how many you need, like 125/20.
- A Sort button on your inventory and every chest sorts by name, type, value, quality, power or weight.
- Opening a crafting station repairs all your gear that the station can fix, in one go.
- A 12-hour clock with AM/PM and the current weather sits at the top of your screen.
- Put any image from the internet on your ship sails and banners, and name your ships. Everyone with the mod sees them, and no server mod is needed.
- When you die, you shout "I WAS AN IDIOT AND I DIED HAHAHAHAHAHA" in chat for everyone to enjoy.
- Client side only. Only you need it, even on a server.

## Controls

| Action | Default |
|---|---|
| Deposit into nearby matching chests | F6 |
| Lock or unlock a slot | Middle-click (inventory open) |
| See stored materials | Just open a crafting station |
| Sort inventory or chest | Click Sort (right-click to change the sort mode) |
| Set a sail or banner image | Left Ctrl + Use on the ship or banner |
| Name a ship | Left Ctrl + Shift + Use on the ship |

## Config

Every setting lives in `matt.valheim.kmartboysmusthavemod.cfg` and shows up in Configuration Manager (F1). Changes apply instantly, no restart needed.

- **General:** on/off, deposit key, modifier key, search radius, works with inventory open
- **Filters:** skip hotbar, skip equipped, skip ammo, deposit non-stackables
- **Slot Locking:** on/off, lock mouse button, optional modifier + click, marker color
- **Messages:** center and summary messages on/off, plus every bit of wording
- **Center Message Tiers:** six tiers, each with its own minimum item count and message
- **Craft From Containers:** on/off, building on/off, container radius, ignore carts and ships, only player-built chests, requirement colors
- **Auto Refuel:** on/off, check interval, radius, fires/ovens/smelters toggles, ore/refinery/kiln/windmill/spinning wheel toggles, refill threshold, fuel reserve, leave 1 in each chest, use inventory fuel, excluded objects
- **Auto Repair:** on/off, repair message on/off
- **Clock:** on/off, text ({time}, {weather}, {conditions}, {day}), minute rounding, position, offset, font size, color
- **Ships & Banners:** edit key, show images from everyone / trusted players / nobody, trusted player list, favorite sail image, fit/flip banner images, ship names, floating name distance, height, size and color
- **Sorting:** on/off, sort mode, keep hotbar in place, merge stacks, button positions
- **Death Message:** on/off, your own messages (several separated by |, one picked at random, {name} = your name), shout or nearby chat
- **Crafting Stock:** on/off, build menu too, text format, colors, font size, position, storage radius, refresh rate

Messages can use `{items}`, `{containers}`, `{itemsWord}` and `{containersWord}`. The crafting stock text can use `{need}`, `{have}`, `{stored}`, `{total}` and `{missing}`.

## Install

Install with a mod manager, or drop `K-MartBoysMustHaveMod.dll` into `BepInEx/plugins`. Requires BepInExPack Valheim.

## Credits

Made for the K-Mart Boys. If you are reading this the right way around, something has gone terribly wrong.
