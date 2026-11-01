<img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/hero.png" alt="Crafting and Cooking Decluttered at the black forge">

Sort, filter, and search every crafting and cooking menu by category, biome, and availability. Works at every crafting station, including the cauldron and Mead Ketill, and with modded items.

Client-side. No server install required. Recipes and balance are untouched.

---

## Features

- **Biome sort** with collapsible headers, in progression order
- **Category filters** for tools, materials, weapons and armor, or health, stamina, eitr and balanced food
- **Live search** that filters as you type
- **Craftable first** sorting
- **Every station**: workbench, forge, black forge, galdr table, cauldron, food preparation table, Mead Ketill, artisan table, and stations added by other mods
- **Modded items** sorted and filtered the same way as vanilla items
- **Per-station memory** of your sort settings
- **Adjustable sizes and spacing** in the F1 config menu

---

## Biome sort

**Sort by: Biome** groups recipes under the biome where they unlock: Meadows, Black Forest, Ocean, Swamp, Mountain, Plains, Mistlands, Ashlands, Deep North. Each biome gets a header with its icon and recipe count. The latest unlocks are always at the bottom.

- Click a header to collapse or expand it.
- Double-click a header to collapse or expand all headers.

### Biome toggles

<img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/toolbar.png" width="700" alt="Sort buttons and biome toggles, Mountain and Plains off">

The nine biome buttons show or hide each biome's recipes. They appear in the same order at every station. A biome's tooltip notes when the current station has nothing from it.

---

## Category filters

**Sort by: Category** turns on the rail along the left side of the list. Each button shows or hides a group.

| Station | Rail groups |
|---|---|
| Workbench, forges, galdr table, artisan table | Tools, Materials, Weapons, Armor |
| Cauldron, food preparation table | Health, Stamina, Eitr and Balanced food (the game's food colors), Misc |
| Mead Ketill | Health, Stamina and Eitr meads, Resistance meads, Utility meads, Misc |

| Forge | Cauldron | Mead Ketill |
|:---:|:---:|:---:|
| <img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/rail-forge.png" width="90" alt="Forge rail, Weapons off"> | <img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/rail-cauldron.png" width="80" alt="Cauldron rail, Health and Eitr on"> | <img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/rail-ketill.png" width="80" alt="Mead Ketill rail, Health only"> |

### Sub-categories

<img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/checklist.png" width="300" alt="Weapons checklist">

The small arrow on a rail button opens a checklist of that group's sub-categories, such as Swords, Spears and Bows. Checklist choices are kept when the group is turned off and on again.

- **Casts** (Deep North "Cast:" items) have their own entries under Materials: weapons, shields, armor, other.
- **Harvesting axes** (stone, flint, bronze, iron, black metal) appear under both Weapons and Tools. Combat axes stay under Weapons.
- **Boss** holds boss items such as the Bell and the Sealbreaker.

---

## Search

<img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/search.png" width="500" alt="Searching for pickaxes at the forge">

The search box at the top of the list filters by item name as you type. Case and accents are ignored, and it combines with every sort and filter.

Keyboard input goes to the search box while it has focus, so typing doesn't move your character or close the inventory. Esc or Enter leaves the box. The search clears when the inventory closes.

---

## Controls and details

- **Click and drag to toggle.** Press a biome button, rail button or checkbox and drag across the others to set them all to the same state.
- **Craftable first** moves recipes you can craft now to the top of each section.
- **A-Z** sorts alphabetically within each section. Off keeps the game's order.
- **Sorts combine** in this order: biome, craftable, category, A-Z.
- **Dimmed buttons** belong to a sort that's off. Clicking one turns that sort on.
- **Saved per station:** active sorts and collapsed headers. **Shared across stations:** hidden biomes and categories.
- **Long item descriptions** scroll instead of overlapping the requirements.

---

## Modded items

Vanilla biomes and categories come from the [Valheim Wiki](https://valheim.weirdgloop.org/). Items the wiki doesn't cover, including items from other mods, follow the same rules:

- **Craftable items:** the latest biome among their ingredients. A sword made with Black Metal is a Plains item.
- **Raw materials:** the earliest biome they spawn or drop in.
- **Categories:** the item's type, weapon skill, armor slot or food stats.

Any item can be reassigned with an override (see below).

---

## Configuration

All settings are in the F1 config menu (requires a Configuration Manager mod) and apply immediately, including with a crafting menu open. Sizes are percentages, with **100% as the default**. Ranges are capped so nothing extends past the crafting panel.

<img src="https://raw.githubusercontent.com/bazilisx/CraftingAndCookingDecluttered/main/images/config.png" width="700" alt="Config menu">

**1 - Buttons**

| Setting | Range | Description |
|---|---|---|
| Button size % | 60 to 200 | Toolbar and rail button size. Both always match. Capped at the width of the crafting panel. |
| Icon size % | 60 to 125 | Icon size inside each button. |
| Button spacing | 0 to 10 px | Gap between buttons. |
| Biome rows | 1 or 2 | 2 splits the biome buttons into rows of 5 and 4, for narrow screens or larger buttons. |

**2 - Recipe list**

| Setting | Range | Description |
|---|---|---|
| Item text size % | 60 to 140 | Recipe name size. |
| Item spacing % | 90 to 150 | Space between recipe rows. |
| Header text size % | 60 to 140 | Biome header text size. |
| Header spacing % | 80 to 150 | Space taken by each header. |
| Header icons | on / off | Biome icon on each header. |
| Header icon size % | 50 to 150 | Header icon size. |
| Header icon gap | 0 to 20 px | Space between the header icon and its text. |

**3 - Advanced**

| Setting | Description |
|---|---|
| ToolbarOffset / RailOffset | Nudges the toolbar or rail if another UI mod moves the crafting panel. |
| DumpCsv | Writes `BepInEx/config/CraftingAndCookingDecluttered.dump.csv` with every recipe's biome, category and source. Useful for bug reports. |

### Overrides

Add lines to `BepInEx/config/CraftingAndCookingDecluttered.overrides.txt` to move an item to a different biome:

```
SwordIron = Swamp
```

Use the item's prefab name (listed in the dump file). Biomes: Meadows, BlackForest, Ocean, Swamp, Mountain, Plains, Mistlands, AshLands, DeepNorth.

---

## Compatibility

- Client-side. Other players and the server don't need it.
- Tested with AzuExtendedPlayerInventory, Epic Loot, Therzie's Warfare, Armory, Wizardry and Monstrum, MultiCraft, AzuCraftyBoxes, and Quick Stack Store Sort Trash Restock.
- Other crafting filter mods (such as Crafting Filter) change the same menu. Use one or the other.
- If a UI mod moves the crafting panel, the toolbar repositions itself. The offset settings can fine-tune it.

## Installation

**Mod manager:** r2modman, Gale or Thunderstore Mod Manager.

**Manual:** install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/), then place `CraftingAndCookingDecluttered.dll` in `BepInEx/plugins`.

## Bugs and suggestions

Open an issue on [GitHub](https://github.com/bazilisx/CraftingAndCookingDecluttered/issues). For an item in the wrong biome or category, enable DumpCsv and attach the file.

## Credits

- Biome and category data: the [Valheim Wiki](https://valheim.weirdgloop.org/) and its editors.
- Icons: [Tabler Icons](https://tabler.io/icons) (MIT) and [Lucide](https://lucide.dev) (ISC). License files are included.
- Food markers and checkboxes are the game's own sprites.

## Building from source

Requires the .NET SDK. From the repository folder:

```
.\pack.ps1 -ValheimDir "<Valheim folder>" -BepInExCore "<BepInEx\core folder>"
```

Builds the DLL and creates a Thunderstore package in `release/`.
