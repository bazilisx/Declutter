using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Biome = Heightmap.Biome;

namespace Declutter
{
    // Category paths are "Group > Sub" using the wiki's own category names (see WikiData.cs).
    // Single-level wiki categories (Trinket, Accessories, ...) use the group name as the sub too.
    static class Categories
    {
        public static string Of(ItemDrop item)
        {
            string c = WikiData.Items.TryGetValue(item.name, out var w) ? w.category : "";
            if (c == "" && Biomes.FermentsTo(item.name) is ItemDrop mead && mead != item) return Of(mead); // mead bases take their mead's group
            // Uncooked/raw items and feast materials take the food they become (Oven Pancake Batter -> Oven Pancake: Eitr).
            if (c == "" && FoodItBecomes(item) is ItemDrop food) return Of(food);
            if (c == "Food > Feast") c = "Food"; // feasts go by their own stats (Black Forest Buffet: 35/35 -> Balanced)
            if (c == "" || c == "Food") c = Fallback(item.m_itemData.m_shared); // not on wiki, or wiki gives no food type
            if (c == "Armor > Body") c = "Armor > Chest"; // shown as Chest, matching the game's own slot name
            if (c == "Misc Items > Forsaken offerings") c = "Misc Items > Boss"; // Bell, Sealbreaker
            if (c == "Light sources") c = "Light";
            if (c == "Mead > Bog Witch") c = "Mead > Utility"; // speciality meads, not all from the Bog Witch
            string path = c.Contains(" > ") ? c : c + " > " + c;
            // Casts (Deep North "X Uncooked" items) get their own checklist entries by what they turn into.
            if (path == "Materials > Materials" && Biomes.ProductOf(item.name) is ItemDrop product && product != item)
            {
                string pp = Of(product), pg = Group(pp);
                string kind = pp == "Weapons > Shields" ? "shields" : pg == "Weapons" ? "weapons" : pg == "Armor" ? "armor"
                    : pg != "Food" && item.name.EndsWith("Uncooked", StringComparison.Ordinal) ? "other" : null; // not oven pies etc.
                if (kind != null) path = "Materials > Casts: " + kind;
            }
            return path;
        }

        // What a crafted item cooks/bakes into, or the feast a feast material places, when that's a food.
        static ItemDrop FoodItBecomes(ItemDrop item)
        {
            var next = Biomes.ProductOf(item.name) ?? item.m_itemData.m_shared.m_appendToolTip; // feasts link their stats this way
            return next && next != item && Group(Of(next)) == "Food" ? next : null;
        }

        // Axes made for harvesting first (weapon second) also show under Tools; they stay sorted with the weapons.
        // Weapon-first axes that happen to chop (Jotun Bane, Nord...) stay weapons only.
        static readonly HashSet<string> harvestAxes = new HashSet<string>
        {
            "AxeStone", "AxeFlint", "AxeBronze", "AxeIron", "AxeSilver_TW", "AxeBlackMetal",
        };
        public static bool AlsoTool(ItemDrop item) => harvestAxes.Contains(item.name);

        public static string Group(string path) => path.Substring(0, path.IndexOf(" > ", StringComparison.Ordinal));
        public static string Sub(string path) => path.Substring(path.IndexOf(" > ", StringComparison.Ordinal) + 3);
        public static bool SingleLevel(string path) => Group(path) == Sub(path);
        public static string SubLabel(string path) => SingleLevel(path) ? Group(path) : Sub(path);

        // The left rail: four groups at most stations, the five food types at food stations.
        public static readonly string[] GearGroups = { "Tools", "Materials", "Weapons", "Armor" };
        public static readonly string[] FoodGroups = { "Health", "Stamina", "Eitr", "Balanced", "Misc" };
        // Mead Ketill: FoodsNav's mead groups. Named apart from the food groups so hiding one doesn't hide the other.
        public static readonly string[] MeadGroups = { "Health mead", "Stamina mead", "Eitr mead", "Resistance mead", "Utility mead", "Misc" };

        public static bool IsFood(ItemDrop.ItemData.SharedData s) => s.m_food + s.m_foodStamina + s.m_foodEitr > 0f;

        // The game's own inventory food-marker rule (InventoryGrid), used for foods the wiki doesn't type.
        public static string FoodType(ItemDrop.ItemData.SharedData s)
        {
            float h = s.m_food, st = s.m_foodStamina, e = s.m_foodEitr;
            if (h < e / 2f && st < e / 2f) return "Eitr";
            if (st < h / 2f) return "Health";
            if (h < st / 2f) return "Stamina";
            return "Balanced";
        }

        public static string RailGroup(ItemDrop item, bool foodStation, bool meadStation)
        {
            string path = Of(item), g = Group(path), sub = Sub(path);
            if (meadStation) return g == "Mead" && Array.IndexOf(MeadGroups, sub + " mead") >= 0 ? sub + " mead" : "Misc"; // modded meads -> Misc
            if (foodStation)
            {
                if (g == "Food" && Array.IndexOf(FoodGroups, sub) >= 0) return sub;
                var s = item.m_itemData.m_shared;
                return IsFood(s) ? FoodType(s) : "Misc"; // anything non-food at a food station (e.g. modded Spirit Bomb)
            }
            switch (g)
            {
                case "Tools": case "Light": case "Bait": return "Tools";
                case "Weapons": return "Weapons";
                case "Armor": case "Accessories": case "Trinket": return "Armor";
                default: return "Materials";
            }
        }

        // Modded weapons with a skill the game doesn't have (throwing axes, blood scythes...): tools only when they can't fight.
        static string UnknownSkillWeapon(ItemDrop.ItemData.SharedData s)
        {
            if (s.m_attack != null && s.m_attack.m_attackType == Attack.AttackType.Projectile)
                return (s.m_name ?? "").IndexOf("bomb", StringComparison.OrdinalIgnoreCase) >= 0 ? "Weapons > Bombs" : "Weapons > Throwing";
            return s.m_damages.GetTotalDamage() > 0f ? "Weapons > Other" : "Tools";
        }

        // Items the wiki doesn't categorise (mostly modded): game data mapped onto the wiki's category names.
        static string Fallback(ItemDrop.ItemData.SharedData s)
        {
            if (IsFood(s)) return "Food > " + FoodType(s);
            switch (s.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                    switch (s.m_skillType)
                    {
                        case Skills.SkillType.Swords: return "Weapons > Swords";
                        case Skills.SkillType.Knives: return "Weapons > Knives";
                        case Skills.SkillType.Clubs: return "Weapons > Clubs";
                        case Skills.SkillType.Axes: return "Weapons > Axes";
                        case Skills.SkillType.Polearms: return "Weapons > Polearms";
                        case Skills.SkillType.Spears: return "Weapons > Spears";
                        case Skills.SkillType.Unarmed: return "Weapons > Unarmed";
                        case Skills.SkillType.Bows: return "Weapons > Bows";
                        case Skills.SkillType.Crossbows: return "Weapons > Crossbows";
                        case Skills.SkillType.ElementalMagic: return "Weapons > Elemental Magic";
                        case Skills.SkillType.BloodMagic: return "Weapons > Blood Magic";
                        case Skills.SkillType.Pickaxes: return "Tools"; // too few pickaxes for their own section
                        case Skills.SkillType.Fishing: return "Tools";
                        default: return UnknownSkillWeapon(s);
                    }
                case ItemDrop.ItemData.ItemType.Shield: return "Weapons > Shields";
                case ItemDrop.ItemData.ItemType.Helmet: return "Armor > Head";
                case ItemDrop.ItemData.ItemType.Chest: return "Armor > Body";
                case ItemDrop.ItemData.ItemType.Legs: return "Armor > Legs";
                case ItemDrop.ItemData.ItemType.Shoulder: return "Armor > Capes";
                case ItemDrop.ItemData.ItemType.Hands: return "Armor";
                case ItemDrop.ItemData.ItemType.Utility: return "Accessories";
                case ItemDrop.ItemData.ItemType.Trinket: return "Trinket";
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    if ((s.m_ammoType ?? "").IndexOf("bait", StringComparison.OrdinalIgnoreCase) >= 0) return "Bait"; // fishing bait is ammo to the game
                    if (s.m_ammoType == "$ammo_arrows") return "Weapons > Arrows";
                    if (s.m_ammoType == "$ammo_bolts") return "Weapons > Bolts";
                    return "Weapons > Missiles";
                case ItemDrop.ItemData.ItemType.Tool: return "Tools";
                case ItemDrop.ItemData.ItemType.Torch: return "Light";
                case ItemDrop.ItemData.ItemType.Consumable: return "Food > Mead";
                case ItemDrop.ItemData.ItemType.Material: return "Materials";
                case ItemDrop.ItemData.ItemType.Fish: return "Fish";
                case ItemDrop.ItemData.ItemType.Trophy: return "Trophies";
                default: return "Misc Items";
            }
        }
    }

    // Biome tier, in the wiki's order (MaterialsNav): Meadows, Black Forest, Ocean, Swamp, ... Deep North.
    //   1. Declutter.overrides.txt
    //   2. WikiData.cs (wiki navboxes, item-page "X-tier" statements, creature locations, wiki recipes)
    //   3. Not on the wiki (modded): same rule the wiki data uses -- latest biome among recipe ingredients;
    //      raw modded drops take the earliest biome they spawn/grow in, read from the live world tables.
    static class Biomes
    {
        static readonly Biome[] progression =
        {
            Biome.Meadows, Biome.BlackForest, Biome.Ocean, Biome.Swamp, Biome.Mountain,
            Biome.Plains, Biome.Mistlands, Biome.AshLands, Biome.DeepNorth,
        };
        static readonly string[] labels =
        {
            "Meadows", "Black Forest", "Ocean", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands", "Deep North", "Unsorted",
        };
        public static readonly int Unknown = progression.Length;

        static readonly Dictionary<string, int> cache = new Dictionary<string, int>();
        static Dictionary<string, int> overrides, natural;
        static Dictionary<string, List<Recipe>> recipesFor;
        static Dictionary<string, Piece> stationPieces;
        static Dictionary<string, List<Piece>> upgradesOf;
        static readonly Dictionary<string, int> stationCache = new Dictionary<string, int>();
        static Dictionary<string, List<string>> conversionsFor;
        static readonly Dictionary<string, ItemDrop> fermentsTo = new Dictionary<string, ItemDrop>(), convertsTo = new Dictionary<string, ItemDrop>();

        // The mead a mead base ferments into, if any.
        public static ItemDrop FermentsTo(string name)
        {
            Build();
            return fermentsTo.TryGetValue(name, out var m) ? m : null;
        }
        static bool dumped;

        public static string Label(int tier) => labels[Math.Min(tier, Unknown)];
        static int RankOfLabel(string label) => Array.IndexOf(labels, label) is int i && i >= 0 && i < Unknown ? i : Unknown;

        public static void Reset()
        {
            cache.Clear();
            stationCache.Clear();
            fermentsTo.Clear();
            convertsTo.Clear();
            natural = null;
            dumped = false;
        }

        static int Rank(Biome b)
        {
            for (int i = 0; i < progression.Length; i++)
                if ((b & progression[i]) != 0) return i;
            return Unknown;
        }

        // Unknown means "no info" -- ignore it rather than letting it swallow known tiers.
        static int MaxKnown(int a, int b) => a == Unknown ? b : b == Unknown ? a : Math.Max(a, b);
        static int MinKnown(int a, int b) => a == Unknown ? b : b == Unknown ? a : Math.Min(a, b);

        static bool Wiki(string prefab, out int tier)
        {
            tier = corrections.TryGetValue(prefab, out var c) ? Rank(c)
                : WikiData.Items.TryGetValue(prefab, out var w) ? RankOfLabel(w.biome) : Unknown;
            return tier != Unknown;
        }

        // Where the wiki data's biome is wrong for when an item can first be crafted or bought (checked 2026-11-01).
        // Station-gated items keep their vanilla station's biome even if a mod moves the recipe to an earlier station.
        static readonly Dictionary<string, Biome> corrections = new Dictionary<string, Biome>
        {
            // Crafting station unlocks later than the ingredients
            ["BoltBone"] = Biome.Mistlands,            // Black Forge
            ["BoltIron"] = Biome.Mistlands,            // Black Forge
            ["BoltBlackmetal"] = Biome.Mistlands,      // Black Forge
            ["CrossbowArbalest"] = Biome.Mistlands,    // Black Forge
            ["KnifeSkollAndHati"] = Biome.Mistlands,   // Black Forge
            ["Lantern"] = Biome.Mistlands,             // Dvergr Lantern: Black Forge
            ["TurretBoltWood"] = Biome.Mountain,       // Wooden Missile: Artisan Table (Dragon Tear)
            ["FishingBaitForest"] = Biome.Swamp,       // Food Preparation Table (Iron)
            ["FishingBaitOcean"] = Biome.Swamp,        // Food Preparation Table (Iron)
            ["FishRaw"] = Biome.Swamp,                 // Food Preparation Table (Iron)
            ["CeramicPlate"] = Biome.Mistlands,        // Artisan Press needs the Queen's carapace
            ["ShieldCore"] = Biome.Mistlands,          // Artisan Table level 2; craftable before the Ashlands
            // Trader stock that unlocks after a boss or one of Hildir's chests
            ["FireworksRocket_White"] = Biome.Plains,  // Hildir, after the bronze chest (Sealed Tower)
            ["FireworksRocket_Cyan"] = Biome.Plains,   // Basic Fireworks + Greydwarf Eye
            ["Thunderstone"] = Biome.BlackForest,      // Haldor, after The Elder
            ["BarrelRings"] = Biome.BlackForest,       // Haldor
            ["CandleWick"] = Biome.Swamp,              // Bog Witch
            ["MushroomBzerker"] = Biome.Mountain,      // Toadstool: Bog Witch, after Moder
            ["FragrantBundle"] = Biome.Mountain,       // Bog Witch, after Moder
            ["ScytheHandle"] = Biome.Mountain,         // Bog Witch, after Moder
            ["SpiceMountains"] = Biome.Mountain,       // Bog Witch, after Moder
            ["SpicePlains"] = Biome.Plains,            // Bog Witch, after Yagluth
            ["SpiceMistlands"] = Biome.Mistlands,      // Bog Witch, after The Queen
            ["SpiceAshlands"] = Biome.AshLands,        // Bog Witch, after Fader
            ["SpiceDeepNorth"] = Biome.DeepNorth,      // Bog Witch, after Kall Fimbulbringer
        };

        public static bool FromWiki(Recipe r) => Wiki(r.m_item.name, out _) && !overrides.ContainsKey(r.m_item.name);

        public static int OfRecipe(Recipe r)
        {
            Build();
            if (overrides.TryGetValue(r.m_item.name, out int t)) return t;
            if (feasts.TryGetValue(r.m_item.name, out var feast)) return Rank(feast);
            // Nor before the station it's crafted at can be built. Vanilla station gates are already in the corrections,
            // so this only moves items a mod puts on a later station, and mod-only items.
            return MaxKnown(WithoutStation(r), StationTier(r.m_craftingStation, r.m_minStationLevel));
        }

        // Feasts go under the biome they're named for, ahead of the station and ingredient rules
        // (Black Forest Buffet Platter: Black Forest, not the prep table's Swamp).
        static readonly Dictionary<string, Biome> feasts = new Dictionary<string, Biome>
        {
            ["FeastMeadows_Material"] = Biome.Meadows,
            ["FeastBlackforest_Material"] = Biome.BlackForest,
            ["FeastOceans_Material"] = Biome.Ocean,
            ["FeastSwamps_Material"] = Biome.Swamp,
            ["FeastMountains_Material"] = Biome.Mountain,
            ["FeastPlains_Material"] = Biome.Plains,
            ["FeastMistlands_Material"] = Biome.Mistlands,
            ["FeastAshlands_Material"] = Biome.AshLands,
            ["FeastDeepNorth_Material"] = Biome.DeepNorth,
        };

        static int WithoutStation(Recipe r)
        {
            int made = Ingredients(r);
            // Never earlier than what it's made from: the wiki lists seasonal Pointy Hat under Meadows, but it needs Bronze.
            // (No recipe in the wiki's own recipe data contradicts its wiki biome, so only recipes
            // the wiki lacks or that mods change can move.)
            return Wiki(r.m_item.name, out int t) ? MaxKnown(t, made) : made;
        }

        // A station's tier is the latest biome among its build materials. Its level counts the different upgrades
        // built near it, in any order, so level N also needs the N-1 earliest upgrades. Works for modded stations too.
        static int StationTier(CraftingStation station, int level)
        {
            Build();
            if (!station || !stationPieces.TryGetValue(station.name, out var piece)) return Unknown;
            string key = station.name + "#" + level;
            if (stationCache.TryGetValue(key, out int t)) return t;
            t = PieceTier(piece);
            if (level > 1 && upgradesOf.TryGetValue(station.name, out var ups))
            {
                var tiers = ups.Select(PieceTier).OrderBy(x => x).ToList(); // Unknown sorts last
                t = MaxKnown(t, tiers[Math.Min(level - 1, tiers.Count) - 1]);
            }
            return stationCache[key] = t;
        }

        static int PieceTier(Piece p)
        {
            int t = Unknown;
            if (p.m_resources != null) // some modded pieces leave this unset
                foreach (var req in p.m_resources)
                    if (req != null && req.m_resItem && req.m_amount > 0) t = MaxKnown(t, OfItem(req.m_resItem.name));
            return t;
        }

        // Only what's needed to craft it: upgrade-only inputs (idols, amount 0 at level 1) don't gate crafting.
        // Raw fish is made from any one fish, so it unlocks with the earliest one, not the latest.
        // Other "any one of" recipes are left alone until checked against the wiki.
        static readonly HashSet<string> earliestIngredient = new HashSet<string> { "FishRaw" };

        static int Ingredients(Recipe r)
        {
            bool anyOne = r.m_requireOnlyOneIngredient && earliestIngredient.Contains(r.m_item.name);
            int t = Unknown;
            foreach (var req in r.m_resources)
            {
                if (!req.m_resItem || req.m_upgraderResource || req.m_amount <= 0) continue;
                int i = OfItem(req.m_resItem.name);
                t = anyOne ? MinKnown(t, i) : MaxKnown(t, i);
            }
            return t;
        }

        static int OfItem(string name)
        {
            if (cache.TryGetValue(name, out int t)) return t;
            if (overrides.TryGetValue(name, out t) || Wiki(name, out t)) return cache[name] = t;
            // Cycle guard: caches Unknown mid-recursion, so an item inside a recipe loop can come out a tier low. Fine for sorting.
            cache[name] = Unknown;
            t = natural.TryGetValue(name, out int n) ? n : Unknown;
            if (recipesFor.TryGetValue(name, out var rs))
                foreach (var r in rs) t = Math.Min(t, Ingredients(r));
            if (conversionsFor.TryGetValue(name, out var cs))
                foreach (var from in cs) t = Math.Min(t, OfItem(from));
            return cache[name] = t;
        }

        static void Build()
        {
            if (natural != null) return;
            natural = new Dictionary<string, int>();
            stationPieces = new Dictionary<string, Piece>();
            upgradesOf = new Dictionary<string, List<Piece>>();
            recipesFor = new Dictionary<string, List<Recipe>>();
            conversionsFor = new Dictionary<string, List<string>>();
            LoadOverrides();

            foreach (var r in ObjectDB.instance.m_recipes)
                if (r && r.m_item) Add(recipesFor, r.m_item.name, r);

            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                if (!go) continue;
                var piece = go.GetComponent<Piece>();
                if (piece && go.GetComponent<CraftingStation>()) stationPieces[go.name] = piece;
                var ext = go.GetComponent<StationExtension>();
                if (piece && ext && ext.m_craftingStation) Add(upgradesOf, ext.m_craftingStation.name, piece);
                var sm = go.GetComponent<Smelter>();
                if (sm) foreach (var c in sm.m_conversion) AddConversion(c.m_from, c.m_to);
                var cook = go.GetComponent<CookingStation>();
                if (cook) foreach (var c in cook.m_conversion) AddConversion(c.m_from, c.m_to);
                var ferm = go.GetComponent<Fermenter>();
                if (ferm) foreach (var c in ferm.m_conversion) { AddConversion(c.m_from, c.m_to); if (c.m_from && c.m_to) fermentsTo[c.m_from.name] = c.m_to; }
            }

            foreach (var list in Resources.FindObjectsOfTypeAll<SpawnSystemList>())
                foreach (var s in list.m_spawners)
                    if (s.m_enabled) Drops(s.m_prefab, Rank(s.m_biome), 0);

            if (ZoneSystem.instance)
                foreach (var v in ZoneSystem.instance.m_vegetation)
                    if (v.m_enable) Drops(v.m_prefab, Rank(v.m_biome), 0);
        }

        static void AddConversion(ItemDrop from, ItemDrop to)
        {
            if (from && to) { Add(conversionsFor, to.name, from.name); convertsTo[from.name] = to; }
        }

        // What an item is processed into (smelter, oven, fermenter...), or for casts "XUncooked" the item X.
        public static ItemDrop ProductOf(string name)
        {
            Build();
            if (convertsTo.TryGetValue(name, out var p)) return p;
            if (!name.EndsWith("Uncooked", StringComparison.Ordinal) || !ObjectDB.instance) return null;
            var go = ObjectDB.instance.GetItemPrefab(name.Substring(0, name.Length - "Uncooked".Length));
            return go ? go.GetComponent<ItemDrop>() : null;
        }

        static void Add<T>(Dictionary<string, List<T>> d, string key, T value)
        {
            if (!d.TryGetValue(key, out var l)) d[key] = l = new List<T>();
            l.Add(value);
        }

        static void Note(GameObject item, int rank)
        {
            if (item && (!natural.TryGetValue(item.name, out int old) || rank < old)) natural[item.name] = rank;
        }

        static void Note(DropTable table, int rank)
        {
            if (table?.m_drops != null) foreach (var d in table.m_drops) Note(d.m_item, rank);
        }

        // Walk what a spawned creature / placed vegetation drops, following trees->logs and rocks->fragments.
        static void Drops(GameObject go, int rank, int depth)
        {
            if (!go || rank == Unknown || depth > 3) return;
            if (go.GetComponent<ItemDrop>()) Note(go, rank); // fish etc. spawn as the item itself
            var cd = go.GetComponent<CharacterDrop>();
            if (cd) foreach (var d in cd.m_drops) Note(d.m_prefab, rank);
            var pick = go.GetComponent<Pickable>();
            if (pick) { Note(pick.m_itemPrefab, rank); Note(pick.m_extraDrops, rank); }
            var mr = go.GetComponent<MineRock>();
            if (mr) Note(mr.m_dropItems, rank);
            var mr5 = go.GetComponent<MineRock5>();
            if (mr5) Note(mr5.m_dropItems, rank);
            var dod = go.GetComponent<DropOnDestroyed>();
            if (dod) Note(dod.m_dropWhenDestroyed, rank);
            var tree = go.GetComponent<TreeBase>();
            if (tree) { Note(tree.m_dropWhenDestroyed, rank); Drops(tree.m_logPrefab, rank, depth + 1); }
            var log = go.GetComponent<TreeLog>();
            if (log) { Note(log.m_dropWhenDestroyed, rank); Drops(log.m_subLogPrefab, rank, depth + 1); }
            var des = go.GetComponent<Destructible>();
            if (des) Drops(des.m_spawnWhenDestroyed, rank, depth + 1);
        }

        // BepInEx/config/Declutter.overrides.txt -- "PrefabName = Biome", one per line. Wins over everything.
        static void LoadOverrides()
        {
            overrides = new Dictionary<string, int>();
            string path = Path.Combine(Plugin.ConfigDir, "Declutter.overrides.txt");
            if (!File.Exists(path))
            {
                File.WriteAllText(path,
                    "# Force an item's biome. Use the prefab name from Declutter.dump.csv.\n" +
                    "# Biomes: Meadows, BlackForest, Ocean, Swamp, Mountain, Plains, Mistlands, AshLands, DeepNorth\n" +
                    "# Example:\n# SomeModdedOre = Mountain\n");
                return;
            }
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=');
                if (line.TrimStart().StartsWith("#") || parts.Length != 2) continue;
                if (Enum.TryParse(parts[1].Trim(), true, out Biome b)) overrides[parts[0].Trim()] = Rank(b);
                else Plugin.Log.LogWarning("bad override line: " + line);
            }
        }

        public static void DumpOnce()
        {
            if (dumped || !ObjectDB.instance || !ZNetScene.instance) return;
            dumped = true;
            Build();
            var lines = new List<string> { "prefab,name,station,station_level,station_biome,biome_without_station,enabled,biome,biome_source,category,category_source" };
            foreach (var r in ObjectDB.instance.m_recipes)
            {
                if (!r || !r.m_item) continue;
                bool wikiCat = WikiData.Items.TryGetValue(r.m_item.name, out var w) && w.category != "" && w.category != "Food";
                lines.Add(string.Join(",", r.m_item.name,
                    "\"" + Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name).Replace("\"", "'") + "\"",
                    r.m_craftingStation ? r.m_craftingStation.name : "(hand)",
                    r.m_minStationLevel,
                    r.m_craftingStation ? Label(StationTier(r.m_craftingStation, r.m_minStationLevel)) : "",
                    Label(WithoutStation(r)),
                    r.m_enabled ? "yes" : "no", // disabled recipes never reach a crafting menu
                    Label(OfRecipe(r)),
                    overrides.ContainsKey(r.m_item.name) ? "override" : FromWiki(r) ? "wiki" : "derived",
                    Categories.Of(r.m_item),
                    wikiCat ? "wiki" : "derived"));
            }
            File.WriteAllLines(Path.Combine(Plugin.ConfigDir, "Declutter.dump.csv"), lines);
        }
    }
}
