# K-MartBoysMustHaveMod

Press one key and everything in your bags that already lives in a nearby chest goes home. Great Job, Jackass.

## What it does

- Press **F6** to deposit items into every nearby container that already holds the same item.
- Existing stacks get topped up first, then empty slots are used.
- Your hotbar can be skipped, and equipped gear is never touched.
- **Middle-click** an inventory slot to lock it. Locked slots glow purple and are never deposited.
- A center-screen message reacts to how much you hauled, from "One whole item" all the way to "Odin weeps".
- Works with Equipment and Quick Slots: quick slots, equipment slots and the quiver are always skipped.
- Skips graves, chests someone else has open, private chests and warded chests you cannot access.
- At a crafting station, every required material shows how many you carry and how many are sitting in your storage boxes.
- Client side only. Only you need it, even on a server.

## Controls

| Action | Default |
|---|---|
| Deposit into nearby matching chests | F6 |
| Lock or unlock a slot | Middle-click (inventory open) |
| See stored materials | Just open a crafting station |

## Config

Every setting lives in `matt.valheim.kmartboysmusthavemod.cfg` and shows up in Configuration Manager (F1). Changes apply instantly, no restart needed.

- **General:** on/off, deposit key, modifier key, search radius, works with inventory open
- **Filters:** skip hotbar, skip equipped, deposit non-stackables
- **Slot Locking:** on/off, lock mouse button, optional modifier + click, marker color
- **Messages:** center and summary messages on/off, plus every bit of wording
- **Center Message Tiers:** six tiers, each with its own minimum item count and message
- **Crafting Stock:** on/off, build menu too, text format, colors, font size, position, storage radius, refresh rate

Messages can use `{items}`, `{containers}`, `{itemsWord}` and `{containersWord}`. The crafting stock text can use `{need}`, `{have}`, `{stored}`, `{total}` and `{missing}`.

## Install

Install with a mod manager, or drop `K-MartBoysMustHaveMod.dll` into `BepInEx/plugins`. Requires BepInExPack Valheim.

## Credits

Made for the K-Mart Boys. If you are reading this the right way around, something has gone terribly wrong.
