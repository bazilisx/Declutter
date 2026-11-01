using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Declutter
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bazilisx.Declutter";
        public const string Name = "Declutter";
        public const string Version = "1.0.1";

        internal static BepInEx.Logging.ManualLogSource Log;
        static ConfigEntry<bool> dumpCsv;
        static ConfigEntry<Vector2> toolbarOffset, railOffset;
        // F1 settings (Configuration Manager). Percentages are relative to the mod's default look (100%).
        static ConfigEntry<int> buttonScale, iconScale, buttonSpacing, biomeRows;
        static ConfigEntry<int> itemText, itemSpacing, headerText, headerSpacing, headerIconScale, headerIconGap;
        static ConfigEntry<bool> headerIcons;
        static ConfigEntry<string> hiddenBiomesCfg, hiddenGroupsCfg, hiddenSubsCfg, stationSortCfg, collapsedCfg;

        // Persisted state. Sort flags + collapsed headers are per station; hidden lists are shared by all stations.
        static HashSet<string> hiddenBiomes, hiddenGroups, hiddenSubs;   // biome labels / rail groups / "Group>Sub"
        static Dictionary<string, string> stationSort;                   // station -> any of "BCAK" (Biome, Category, Alphabetical, K = craftable first)
        static Dictionary<string, HashSet<string>> collapsed;            // station -> collapsed biome headers

        static readonly string[] biomeIcons =
        {
            "biome_meadows", "biome_blackforest", "biome_ocean", "biome_swamp", "biome_mountain",
            "biome_plains", "biome_mistlands", "biome_ashlands", "biome_deepnorth",
        };
        static readonly Dictionary<string, Color> forkColors = new Dictionary<string, Color>
        {   // the game's own food-marker tints (InventoryGrid)
            ["Health"] = new Color(1f, 0.5f, 0.5f), ["Stamina"] = new Color(1f, 1f, 0.5f),
            ["Eitr"] = new Color(0.6f, 0.6f, 1f), ["Balanced"] = Color.white,
        };
        static readonly Dictionary<string, Color> meadColors = new Dictionary<string, Color>
        {   // fork colors for the three stat meads, gray for resistance, teal for utility
            ["Health mead"] = forkColors["Health"], ["Stamina mead"] = forkColors["Stamina"], ["Eitr mead"] = forkColors["Eitr"],
            ["Resistance mead"] = new Color(0.71f, 0.70f, 0.66f), ["Utility mead"] = new Color(0.36f, 0.79f, 0.65f),
        };
        static readonly Color gold = new Color(0.94f, 0.71f, 0.29f), tan = new Color(0.8f, 0.71f, 0.54f);
        static readonly Color textCol = new Color(0.91f, 0.86f, 0.77f), dim = new Color(0.42f, 0.36f, 0.27f);
        static readonly Color lineCol = new Color(0.35f, 0.27f, 0.19f), link = new Color(0.5f, 0.7f, 1f);
        static readonly Color ringOff = new Color(0.36f, 0.30f, 0.23f), ringDisabled = new Color(0.22f, 0.18f, 0.14f);
        static readonly Color iconDisabled = new Color(0.27f, 0.23f, 0.18f), plainBg = new Color(0.17f, 0.15f, 0.13f);
        static readonly Color panelCol = new Color(0.1f, 0.07f, 0.04f, 0.97f);

        static RectTransform toolbar, rail, flyout;
        static IconButton[] sortButtons, biomeButtons;
        static Image biomeArrow;
        static readonly List<GameObject> headers = new List<GameObject>();
        static readonly List<string> headerLabels = new List<string>(); // biomes with a header in the current list
        static ScrollRect descScroll;
        static string lastDesc;
        static (Transform parent, int index, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 oMin, Vector2 oMax) descOriginal;
        static string lastHeader;
        static float lastHeaderTime;
        static GameObject tooltipPrefab;
        static TMP_FontAsset font;
        static Sprite frame;
        static Color headerGold; // gold times the list font's own face tint, so header icon, text and line render identically
        static float fontSize, size;
        static string flyGroup;
        static bool foodStation, meadStation;
        static string dragging;   // row being drag-toggled ("biome" / "rail"), null when not dragging
        static bool dragValue;    // the state every button dragged over is set to
        static TMP_InputField searchField;
        static string search = "";
        static int typingFrame = -10;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        static readonly FieldInfo availableField = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
        static readonly FieldInfo baseSizeField = AccessTools.Field(typeof(InventoryGui), "m_recipeListBaseSize");
        static readonly MethodInfo updateCraftingPanel = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");
        static PropertyInfo pairRecipe, pairCanCraft, pairElement; // InventoryGui.RecipeDataPair is a private struct

        internal static string ConfigDir;

        void Awake()
        {
            Log = Logger;
            // Shown in the F1 Configuration Manager in this order. Every change applies straight away (the toolbar,
            // rail and list are rebuilt), and each range keeps things inside the crafting panel.
            int order = 1000;
            ConfigEntry<T> Setting<T>(string section, string key, T def, string desc, AcceptableValueBase range = null)
            {
                var e = Config.Bind(section, key, def, new ConfigDescription(desc, range, new ConfigurationManagerAttributes { Order = order-- }));
                e.SettingChanged += (_, __) => RebuildUi();
                return e;
            }
            const string buttons = "1 - Buttons", listSec = "2 - Recipe list", adv = "3 - Advanced";
            buttonScale = Setting(buttons, "Button size %", 100, "Size of every toolbar and category button (both always match). Capped so the toolbar never runs past the crafting panel.", new AcceptableValueRange<int>(60, 200));
            iconScale = Setting(buttons, "Icon size %", 100, "Size of the icon inside each button.", new AcceptableValueRange<int>(60, 125));
            buttonSpacing = Setting(buttons, "Button spacing", 0, "Gap between neighbouring buttons on the toolbar and the category buttons, in pixels. 0 = frames touch.", new AcceptableValueRange<int>(0, 10));
            biomeRows = Setting(buttons, "Biome rows", 1, "1 = all nine biome buttons in one row. 2 = two rows (5 + 4), for narrow screens or big buttons.", new AcceptableValueList<int>(1, 2));
            itemText = Setting(listSec, "Item text size %", 100, "Recipe names in the list.", new AcceptableValueRange<int>(60, 140));
            itemSpacing = Setting(listSec, "Item spacing %", 100, "Space from one recipe row to the next.", new AcceptableValueRange<int>(90, 150));
            headerText = Setting(listSec, "Header text size %", 100, "Biome header text.", new AcceptableValueRange<int>(60, 140));
            headerSpacing = Setting(listSec, "Header spacing %", 100, "Space taken by each biome header (collapsed headers always stack a little tighter).", new AcceptableValueRange<int>(80, 150));
            headerIcons = Setting(listSec, "Header icons", true, "Show the biome icon in front of each header.");
            headerIconScale = Setting(listSec, "Header icon size %", 100, "Size of the biome icon in each header.", new AcceptableValueRange<int>(50, 150));
            headerIconGap = Setting(listSec, "Header icon gap", 5, "Space between a header's icon and its text, in pixels.", new AcceptableValueRange<int>(0, 20));
            dumpCsv = Config.Bind(adv, "DumpCsv", false, new ConfigDescription("Write BepInEx/config/Declutter.dump.csv the first time a crafting menu opens, listing every recipe's biome and category and where each came from. Handy for bug reports and for writing overrides.", null, new ConfigurationManagerAttributes { Order = order--, IsAdvanced = true }));
            toolbarOffset = Setting(adv, "ToolbarOffset", Vector2.zero, "Nudge the sort/biome toolbar (pixels) if it sits in the wrong spot.");
            railOffset = Setting(adv, "CategoryOffset", Vector2.zero, "Nudge the category buttons (pixels) if it sits in the wrong spot.");
            var hidden = new ConfigurationManagerAttributes { Browsable = false };
            ConfigEntry<string> State(string key) => Config.Bind("State", key, "", new ConfigDescription("Managed in game.", null, hidden));
            hiddenBiomesCfg = State("HiddenBiomes");
            hiddenGroupsCfg = State("HiddenGroups");
            hiddenSubsCfg = State("HiddenCategories");
            stationSortCfg = State("StationSort");
            collapsedCfg = State("CollapsedBiomes");
            LoadState();
            ConfigDir = System.IO.Path.GetDirectoryName(Config.ConfigFilePath);
            new Harmony(Guid).PatchAll(Assembly.GetExecutingAssembly());
        }

        void Update()
        {
            if (dragging != null && !Input.GetMouseButton(0)) { dragging = null; Changed(); } // drag ended: save + refresh once
            if (searchField && searchField.isFocused) typingFrame = Time.frameCount;
            // a different item's description starts scrolled to the top (the game rewrites the text every frame)
            if (descScroll && descScroll.content && InventoryGui.instance && InventoryGui.instance.m_recipeDecription.text != lastDesc)
            {
                lastDesc = InventoryGui.instance.m_recipeDecription.text;
                descScroll.verticalNormalizedPosition = 1f;
            }
            if (!flyout) return;
            if (!InventoryGui.IsVisible()) { CloseFlyout(); return; }
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !Over(flyout) && !(rail && Over(rail)))
                CloseFlyout();
        }

        // True while the search box has focus, plus one frame after (so the Esc/Enter that leaves it can't also close
        // the inventory, whatever order the game's Update and ours run in).
        static bool Typing() => (searchField && searchField.isFocused) || Time.frameCount - typingFrame <= 1;

        static void RefreshList()
        {
            var gui = InventoryGui.instance;
            if (gui && InventoryGui.IsVisible() && Player.m_localPlayer) updateCraftingPanel.Invoke(gui, new object[] { false });
        }

        static bool MatchesSearch(Recipe r) =>
            CultureInfo.InvariantCulture.CompareInfo.IndexOf(Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name),
                search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        static bool Over(RectTransform rt)
        {
            var c = rt.GetComponentInParent<Canvas>().rootCanvas;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition,
                c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera);
        }

        // ---- F1 settings ----

        // Read by Configuration Manager mods by name (fields only), so no reference to them is needed.
        sealed class ConfigurationManagerAttributes
        {
            public bool? Browsable;
            public bool? IsAdvanced;
            public int? Order;
        }

        // 100% is the default look. The base factor for each setting lives where it's used.
        static float Pct(ConfigEntry<int> e) => e.Value / 100f;
        const float ListTopPad = 4f; // keeps the first header/item off the top edge of the list

        // Panel rects we've moved or shrunk, with their original offsets, so a rebuild starts from the game's layout.
        static readonly Dictionary<RectTransform, (Vector2 min, Vector2 max)> originalOffsets = new Dictionary<RectTransform, (Vector2, Vector2)>();
        static void Remember(RectTransform rt)
        {
            if (!originalOffsets.ContainsKey(rt)) originalOffsets[rt] = (rt.offsetMin, rt.offsetMax);
        }

        // A setting changed: tear our UI down, put the game's layout back, and let the next list refresh rebuild it.
        static void RebuildUi()
        {
            CloseFlyout();
            foreach (var h in headers) if (h) Destroy(h);
            headers.Clear();
            if (toolbar) Destroy(toolbar.gameObject);
            if (rail) Destroy(rail.gameObject);
            if (searchField) Destroy(searchField.gameObject);
            RestoreDescription();
            toolbar = rail = null;
            searchField = null;
            foreach (var kv in originalOffsets)
                if (kv.Key) { kv.Key.offsetMin = kv.Value.min; kv.Key.offsetMax = kv.Value.max; }
            originalOffsets.Clear();
            RefreshList();
        }

        // ---- state ----

        static void LoadState()
        {
            hiddenBiomes = new HashSet<string>(Split(hiddenBiomesCfg.Value, ','));
            hiddenGroups = new HashSet<string>(Split(hiddenGroupsCfg.Value, ','));
            hiddenSubs = new HashSet<string>(Split(hiddenSubsCfg.Value, ','));
            stationSort = new Dictionary<string, string>();
            collapsed = new Dictionary<string, HashSet<string>>();
            foreach (var kv in Split(stationSortCfg.Value, ';').Select(s => s.Split('=')).Where(p => p.Length == 2))
                stationSort[kv[0]] = kv[1];
            foreach (var kv in Split(collapsedCfg.Value, ';').Select(s => s.Split('=')).Where(p => p.Length == 2))
                collapsed[kv[0]] = new HashSet<string>(Split(kv[1], '|'));
        }

        static void SaveState()
        {
            hiddenBiomesCfg.Value = string.Join(",", hiddenBiomes);
            hiddenGroupsCfg.Value = string.Join(",", hiddenGroups);
            hiddenSubsCfg.Value = string.Join(",", hiddenSubs);
            stationSortCfg.Value = string.Join(";", stationSort.Where(kv => kv.Value != "").Select(kv => kv.Key + "=" + kv.Value));
            collapsedCfg.Value = string.Join(";", collapsed.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key + "=" + string.Join("|", kv.Value)));
        }

        static string[] Split(string s, char sep) => s.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries);

        static string Station()
        {
            var s = Player.m_localPlayer ? Player.m_localPlayer.GetCurrentCraftingStation() : null;
            return s ? s.m_name : "hand";
        }

        static string Flags() => stationSort.TryGetValue(Station(), out var f) ? f : "";
        static bool On(char flag) => Flags().IndexOf(flag) >= 0;

        static HashSet<string> Collapsed()
        {
            string k = Station();
            if (!collapsed.TryGetValue(k, out var s)) collapsed[k] = s = new HashSet<string>();
            return s;
        }

        static void Toggle(HashSet<string> set, string key)
        {
            if (!set.Remove(key)) set.Add(key);
        }

        static void Changed()
        {
            SaveState();
            RefreshList();
            if (flyout) FillFlyout();
        }

        // ---- grouping ----

        static List<Recipe> Available()
        {
            var all = new List<Recipe>();
            Player.m_localPlayer.GetAvailableRecipes(ref all);
            return all.Where(r => r.m_item).ToList();
        }

        static bool IsFoodRecipe(Recipe r) =>
            Categories.Group(Categories.Of(r.m_item)) == "Food" || Categories.IsFood(r.m_item.m_itemData.m_shared);

        // Cauldron, prep table and modded kitchens: mostly food -> the rail shows the five food types.
        static bool FoodStation(List<Recipe> recipes) =>
            recipes.Count > 0 && recipes.Count(r => r.m_item && IsFoodRecipe(r)) * 2 >= recipes.Count(r => r.m_item);

        // Mead Ketill (and modded kettles): mostly mead bases -> the rail shows the wiki's mead groups.
        static bool MeadStation(List<Recipe> recipes) =>
            recipes.Count > 0 && recipes.Count(r => r.m_item && Categories.Group(Categories.Of(r.m_item)) == "Mead") * 2 >= recipes.Count(r => r.m_item);

        static string Group(Recipe r) => Categories.RailGroup(r.m_item, foodStation, meadStation);
        static string SubKey(Recipe r) => Group(r) + ">" + Categories.SubLabel(Categories.Of(r.m_item));
        static string KeyGroup(string key) => key.Substring(0, key.IndexOf('>'));

        // Every rail group + sub-category a recipe appears under: its own, plus Tools for harvesting axes.
        static IEnumerable<string> Keys(Recipe r)
        {
            yield return SubKey(r);
            if (!foodStation && !meadStation && Categories.AlsoTool(r.m_item)) yield return "Tools>Tools"; // with the other tools, no checkbox of its own
        }
        static string[] GroupOrder() => meadStation ? Categories.MeadGroups : foodStation ? Categories.FoodGroups : Categories.GearGroups;

        // Filters apply only while their sort is on.
        static bool Hidden(Recipe r) =>
            (On('B') && hiddenBiomes.Contains(Biomes.Label(Biomes.OfRecipe(r)))) ||
            (On('C') && Keys(r).All(k => hiddenGroups.Contains(KeyGroup(k)) || hiddenSubs.Contains(k))); // shown if visible anywhere

        // ---- recipe list ----

        static void Relayout(InventoryGui gui)
        {
            foreach (var h in headers) if (h) Destroy(h);
            headers.Clear();
            headerLabels.Clear();
            if (!gui.InCraftTab()) return;

            var list = (IList)availableField.GetValue(gui);
            if (list.Count == 0) return;
            if (pairRecipe == null)
            {
                var t = list[0].GetType();
                pairRecipe = t.GetProperty("Recipe");
                pairCanCraft = t.GetProperty("CanCraft");
                pairElement = t.GetProperty("InterfaceElement");
            }
            // The rail takes list width in every mode, so rows are always a little smaller and end in "…" if too long.
            foreach (var p in list) FitText(((GameObject)pairElement.GetValue(p)).transform.Find("name").GetComponent<TMP_Text>(), 0.9f * Pct(itemText));

            bool byBiome = On('B'), byCat = On('C'), alpha = On('A'), craftFirst = On('K');
            float space = gui.m_recipeListSpace * 0.85f * Pct(itemSpacing); // 100% = 85% of vanilla
            if (!byBiome && !byCat && !alpha && !craftFirst)
            {
                // vanilla order, just with the item spacing setting applied
                for (int i = 0; i < list.Count; i++)
                    ((RectTransform)((GameObject)pairElement.GetValue(list[i])).transform).anchoredPosition = new Vector2(0f, -(ListTopPad + i * space));
                gui.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max((float)baseSizeField.GetValue(gui), ListTopPad + list.Count * space));
                return;
            }

            // Biome sections -> craftable before uncraftable (if on) -> category -> A-Z (if on) or the game's own order.
            // Item types (axes, bows...) only group within a category when A-Z is off; with A-Z on each category is one A-Z list.
            var order = GroupOrder();
            var rows = list.Cast<object>().Select((p, index) =>
            {
                var r = (Recipe)pairRecipe.GetValue(p);
                return new
                {
                    pair = p,
                    index,
                    biome = byBiome ? Biomes.OfRecipe(r) : 0,
                    group = byCat ? Array.IndexOf(order, Group(r)) : 0,
                    sub = byCat && !alpha ? Categories.SubLabel(Categories.Of(r.m_item)) : "",
                    canCraft = (bool)pairCanCraft.GetValue(p),
                    name = alpha ? Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name) : "",
                    element = (GameObject)pairElement.GetValue(p),
                };
            })
            .OrderBy(x => x.biome)
            .ThenBy(x => craftFirst && !x.canCraft ? 1 : 0)
            .ThenBy(x => x.group)
            .ThenBy(x => x.sub, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.index)
            .ToList();

            // Only biome headers; categories just order and filter.
            var fold = Collapsed();
            float y = ListTopPad;
            int? lastBiome = null;
            list.Clear();
            foreach (var x in rows)
            {
                string label = Biomes.Label(x.biome);
                if (byBiome && x.biome != lastBiome)
                {
                    lastBiome = x.biome;
                    headers.Add(MakeHeader(gui, x.biome, rows.Count(o => o.biome == x.biome), ref y));
                }
                if (byBiome && fold.Contains(label)) { Destroy(x.element); continue; }
                list.Add(x.pair);
                ((RectTransform)x.element.transform).anchoredPosition = new Vector2(0f, -y);
                y += space;
            }
            gui.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max((float)baseSizeField.GetValue(gui), y));
        }

        // Click: fold/unfold this biome. Double-click: every biome in the list takes the state the first click gave this one.
        // (Our own timing: the list is rebuilt between the two clicks, so Unity's clickCount never reaches 2.)
        static void HeaderClicked(string label)
        {
            var fold = Collapsed();
            if (label == lastHeader && Time.unscaledTime - lastHeaderTime < 0.4f)
            {
                bool collapse = fold.Contains(label);
                foreach (var l in headerLabels) { if (collapse) fold.Add(l); else fold.Remove(l); }
                lastHeader = null;
            }
            else
            {
                Toggle(fold, label);
                lastHeader = label;
                lastHeaderTime = Time.unscaledTime;
            }
            Changed();
        }

        // 100% on the text settings is 75% of vanilla, so names still fit with the rail taking some of the list's width.
        static void FitText(TMP_Text t, float scale)
        {
            t.enableAutoSizing = false;
            t.fontSize = fontSize * 0.75f * scale;
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Built from the game's own recipe-row prefab so it lines up with the items. Biome icon sits in the item-icon
        // slot with the text pulled in next to it; gold text + full-width underline; collapse arrow on the right.
        static GameObject MakeHeader(InventoryGui gui, int biome, int count, ref float y)
        {
            string label = Biomes.Label(biome);
            var go = Instantiate(gui.m_recipeElementPrefab, gui.m_recipeListRoot);
            go.name = "CCD_Header";
            go.SetActive(true);
            foreach (var child in new[] { "Durability", "QualityLevel", "selected" })
            {
                var c = go.transform.Find(child);
                if (c) c.gameObject.SetActive(false);
            }
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            if (btn.targetGraphic) btn.targetGraphic.color = Color.clear;
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(() => HeaderClicked(label));
            headerLabels.Add(label);

            var icon = go.transform.Find("icon")?.GetComponent<Image>();
            float iconLeft = 0f, iconRight = 0f, iconPx = 0f;
            var rowRt = (RectTransform)go.transform;
            if (icon)
            {
                icon.sprite = Icon(biome < biomeIcons.Length ? biomeIcons[biome] : "sort_biome");
                icon.color = headerGold;
                icon.preserveAspect = true;
                icon.transform.localScale = Vector3.one * 0.63f * Pct(headerIconScale);
                var ic = new Vector3[4];
                icon.rectTransform.GetWorldCorners(ic);
                iconLeft = rowRt.InverseTransformPoint(ic[0]).x;
                iconRight = rowRt.InverseTransformPoint(ic[2]).x;
                iconPx = icon.rectTransform.rect.height * 0.63f; // stroke weight follows the default icon size
                icon.gameObject.SetActive(headerIcons.Value);
            }

            var text = go.transform.Find("name").GetComponent<TMP_Text>();
            FitText(text, Pct(headerText));
            text.richText = true;
            text.text = label + " <size=80%>(" + count + ")";
            text.enableVertexGradient = false; // the row prefab's gradient tints the text; flat gold matches the icon and line
            text.color = gold;
            text.fontStyle = FontStyles.Bold;
            bool open = !Collapsed().Contains(label);
            if (icon)
            {
                // pull the text in to just after the icon (or to where the icon would start, if icons are hidden)
                var tc = new Vector3[4];
                text.rectTransform.GetWorldCorners(tc);
                float target = headerIcons.Value ? iconRight + headerIconGap.Value : iconLeft + 6f; // hidden icons still leave a little padding from the edge
                text.rectTransform.offsetMin += new Vector2(target - rowRt.InverseTransformPoint(tc[0]).x, 0f);
            }

            if (open)
            {
                // Underline only on open sections, as thick as the icon's lines (Tabler strokes are 2/24 of a glyph drawn 52px in a 64px PNG).
                float stroke = iconPx > 0f ? Mathf.Max(1f, iconPx * (52f / 64f) / 12f) : 1.5f;
                var line = NewUI("underline", go.transform, typeof(Image));
                var lrt = (RectTransform)line.transform;
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.offsetMin = new Vector2(4f, 2f); lrt.offsetMax = new Vector2(-4f, 2f + stroke);
                line.GetComponent<Image>().color = headerGold;
                line.GetComponent<Image>().raycastTarget = false;
            }

            var arrow = Arrow(go.transform, 12f, open, headerGold);
            var art = (RectTransform)arrow.transform;
            art.anchorMin = art.anchorMax = new Vector2(1f, 0.5f);
            art.anchoredPosition = new Vector2(-14f, 0f);

            ((RectTransform)go.transform).anchoredPosition = new Vector2(0f, -y);
            // Collapsed headers stack tighter (their rows overlap a little; the later one is on top for clicks).
            y += gui.m_recipeListSpace * 0.9f * Pct(headerSpacing) * (open ? 1f : 0.72f);
            return go;
        }

        // ---- toolbar + rail ----

        static void EnsureUi(InventoryGui gui)
        {
            if (toolbar) return;
            // Size text from the recipe rows (the tab label auto-sizes and reports a tiny size).
            var nameTpl = gui.m_recipeElementPrefab.transform.Find("name").GetComponent<TMP_Text>();
            font = nameTpl.font;
            fontSize = nameTpl.enableAutoSizing ? nameTpl.fontSizeMax : nameTpl.fontSize;
            var tt = gui.m_craftButton ? gui.m_craftButton.GetComponent<UITooltip>() : null;
            tooltipPrefab = tt ? tt.m_tooltipPrefab : null;
            // Button background: the Craft button's own sprite (gray chamfered frame, interior darkening toward the edges).
            var cb = gui.m_craftButton ? gui.m_craftButton.targetGraphic as Image ?? gui.m_craftButton.GetComponent<Image>() : null;
            frame = cb ? cb.sprite : null;
            Plugin.Log.LogInfo(frame ? $"button frame: {frame.name}, border {frame.border}, ppu {frame.pixelsPerUnit}"
                            : "Craft button sprite not found; using a plain chamfered background");

            var mat = nameTpl.fontSharedMaterial;
            Color face = mat && mat.HasProperty(ShaderUtilities.ID_FaceColor) ? mat.GetColor(ShaderUtilities.ID_FaceColor) : Color.white;
            headerGold = new Color(gold.r * face.r, gold.g * face.g, gold.b * face.b, 1f);
            Plugin.Log.LogInfo($"list font face tint {face}, row text gradient {nameTpl.enableVertexGradient}");

            var panel = gui.m_crafting;
            var up = (RectTransform)gui.m_tabUpgrade.transform;
            var craftTab = (RectTransform)gui.m_tabCraft.transform;
            var uc = new Vector3[4]; var cc = new Vector3[4]; var pc = new Vector3[4]; var corners = new Vector3[4];
            up.GetWorldCorners(uc);
            craftTab.GetWorldCorners(cc);
            panel.GetWorldCorners(pc);
            var scroll = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>();
            var scrollRt = (RectTransform)(scroll ? scroll.transform : gui.m_recipeListRoot.parent);
            scrollRt.GetWorldCorners(corners);

            // One button size for the toolbar and the rail: the setting, capped so the toolbar row (rail + sort buttons
            // + arrow + biomes) fits inside the panel and the rail (up to six groups) fits beside the list.
            float gap = buttonSpacing.Value;
            int rows = biomeRows.Value == 2 ? 2 : 1, perRow = rows == 2 ? 5 : biomeIcons.Length, n = 4 + perRow;
            float listLeft = panel.InverseTransformPoint(corners[0]).x, panelRight = panel.InverseTransformPoint(pc[2]).x - 8f;
            float listH = panel.InverseTransformPoint(corners[1]).y - panel.InverseTransformPoint(corners[0]).y;
            float maxW = (panelRight - listLeft - 32f - (n + 3) * gap) / (n + 1.32f); // rail + n buttons + arrow, 2 spacers, gaps
            float maxH = (listH * 0.55f - 5f * gap) / 6f;
            float want = Mathf.Clamp(up.rect.height * 0.85f, 18f, 30f) * 1.4f * Pct(buttonScale);
            size = Mathf.Max(16f, Mathf.Min(want, maxW, maxH));
            float toolbarH = rows * size + (rows - 1) * gap;

            // Toolbar: its own row directly under the Craft/Upgrade tabs. Placed by screen position on the crafting
            // panel (inventory mods reshape the tab row, so its anchors/clipping can't be trusted).
            toolbar = (RectTransform)NewUI("CCD_Toolbar", panel, typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter)).transform;
            toolbar.anchorMin = toolbar.anchorMax = new Vector2(0.5f, 0.5f);
            bool roomMade = MakeRoomBelowTabs(gui, panel.InverseTransformPoint(uc[0]).y, toolbarH + 18f); // row(s) + breathing room above and below
            if (roomMade)
            {
                toolbar.pivot = new Vector2(0f, 1f);
                toolbar.position = cc[0]; // bottom-left of the Craft tab
                toolbar.anchoredPosition += new Vector2(0f, -9f) + toolbarOffset.Value;
            }
            else
            {
                // Couldn't find the list/description to push down: fall back to the space right of the Upgrade tab.
                toolbar.pivot = new Vector2(0f, 0.5f);
                toolbar.position = (uc[2] + uc[3]) / 2f;
                toolbar.anchoredPosition += new Vector2(8f, 0f) + toolbarOffset.Value;
            }
            toolbar.SetAsLastSibling();
            Layout(toolbar.gameObject, horizontal: true);

            // Group 1: the four sort toggles. Then an arrow (lit while Biome sort is on) leading to group 2: the biomes.
            sortButtons = new[]
            {
                MakeButton(toolbar, Icon("sort_az"), "Sort by: Alphabetical", () => SetFlag('A')),
                MakeButton(toolbar, Icon("sort_category"), "Sort by: Category", () => SetFlag('C')),
                MakeButton(toolbar, Icon("sort_craftable"), "Sort by: Craftable first", () => SetFlag('K')),
                MakeButton(toolbar, Icon("sort_biome"), "Sort by: Biome", () => SetFlag('B')),
            };
            Spacer(toolbar, 10f);
            biomeArrow = Arrow(toolbar, size * 0.32f, false, dim).GetComponent<Image>();
            var ale = biomeArrow.gameObject.AddComponent<LayoutElement>();
            ale.preferredWidth = ale.preferredHeight = size * 0.32f;
            Spacer(toolbar, 10f);
            // Biomes: one row of nine, or two rows (5 + 4) stacked beside the sort buttons.
            Transform row1 = toolbar, row2 = toolbar;
            if (rows == 2)
            {
                var block = Group(toolbar, horizontal: false);
                row1 = Group(block, horizontal: true);
                row2 = Group(block, horizontal: true);
            }
            biomeButtons = new IconButton[biomeIcons.Length];
            for (int i = 0; i < biomeIcons.Length; i++)
            {
                string label = Biomes.Label(i);
                var b = biomeButtons[i] = MakeButton(i < perRow ? row1 : row2, Icon(biomeIcons[i]), label, null);
                b.dragRow = "biome";
                b.isOn = () => !hiddenBiomes.Contains(label);
                b.setOn = on => { if (on) hiddenBiomes.Remove(label); else hiddenBiomes.Add(label); };
                b.onDisabledClick = () => SetFlag('B', true);
            }

            // Rail: a slim column carved off the left edge of the recipe list (re-measured: the list just moved down).
            scrollRt.GetWorldCorners(corners);
            rail = (RectTransform)NewUI("CCD_Rail", scrollRt.parent, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).transform;
            rail.anchorMin = rail.anchorMax = new Vector2(0.5f, 0.5f);
            rail.pivot = new Vector2(0f, 1f);
            rail.position = corners[1];
            rail.anchoredPosition += new Vector2(2f, -6f) + railOffset.Value;
            Layout(rail.gameObject, horizontal: false);
            Remember(scrollRt);
            scrollRt.offsetMin += new Vector2(size + 12f, 0f); // rail width + a gap before the list
            MakeSearchBox(scrollRt, scroll ? scroll.verticalScrollbar : null);
            MakeDescriptionScroll(gui);
            if (roomMade)
            {
                // line the toolbar up with the item list (right of the rail)
                var lc = new Vector3[4];
                scrollRt.GetWorldCorners(lc);
                toolbar.position = new Vector3(lc[0].x, toolbar.position.y, toolbar.position.z);
                toolbar.anchoredPosition += new Vector2(toolbarOffset.Value.x, 0f);
            }
            Plugin.Log.LogInfo($"toolbar at {toolbar.anchoredPosition} ({(roomMade ? "own row under the tabs" : "fallback: right of Upgrade")}), button {size}");
        }

        // Push everything that starts right under the tabs (the recipe list and description columns) down by one row,
        // keeping their bottom edges where they are so the requirements and Craft button don't move.
        static bool MakeRoomBelowTabs(InventoryGui gui, float tabBottom, float rowHeight)
        {
            var panel = gui.m_crafting;
            var moved = new List<string>();
            void Visit(Transform t, int depth)
            {
                foreach (Transform ch in t)
                {
                    var rt = ch as RectTransform;
                    if (!rt || ch.name.StartsWith("CCD_")) continue;
                    if (gui.m_tabCraft.transform.IsChildOf(ch) || gui.m_tabUpgrade.transform.IsChildOf(ch))
                    {
                        if (depth < 4) Visit(ch, depth + 1); // the container holding the tabs: look inside it
                        continue;
                    }
                    var c = new Vector3[4];
                    rt.GetWorldCorners(c);
                    float top = panel.InverseTransformPoint(c[1]).y, bottom = panel.InverseTransformPoint(c[0]).y;
                    // starts at the tab bottom and is tall (skips thin borders/decorations)
                    if (Mathf.Abs(top - tabBottom) > 16f || top - bottom < rowHeight * 3f) continue;
                    float local = rowHeight * panel.lossyScale.y / Mathf.Max(0.0001f, ((RectTransform)rt.parent).lossyScale.y);
                    Remember(rt);
                    rt.offsetMax -= new Vector2(0f, local);
                    moved.Add(ch.name);
                }
            }
            Visit(panel, 0);
            Plugin.Log.LogInfo("made room under the tabs by moving down: " + (moved.Count > 0 ? string.Join(", ", moved) : "(nothing found)"));
            return moved.Count > 0;
        }

        // Long item descriptions (now the column starts lower) ran into the requirements row. The description text goes
        // into a masked, mouse-wheel scroll area that ends just above the requirements; RebuildUi puts it back.
        static void MakeDescriptionScroll(InventoryGui gui)
        {
            var desc = gui.m_recipeDecription;
            var req = gui.m_recipeRequirementList != null && gui.m_recipeRequirementList.Length > 0 ? gui.m_recipeRequirementList[0] : null;
            if (!desc || !req) return;
            var drt = desc.rectTransform;
            var parent = (RectTransform)drt.parent;
            var dc = new Vector3[4]; var rc = new Vector3[4];
            drt.GetWorldCorners(dc);
            ((RectTransform)req.transform).GetWorldCorners(rc);
            float top = parent.InverseTransformPoint(dc[1]).y, bottom = parent.InverseTransformPoint(rc[1]).y + 6f;
            float left = parent.InverseTransformPoint(dc[0]).x, right = parent.InverseTransformPoint(dc[2]).x;
            if (top - bottom < 40f) { Plugin.Log.LogInfo("description: no room for a scroll area"); return; }

            var vp = NewUI("CCD_DescScroll", parent, typeof(RectMask2D), typeof(ScrollRect), typeof(Image));
            vp.GetComponent<Image>().color = Color.clear; // catches the mouse wheel over empty space too
            var vrt = (RectTransform)vp.transform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0.5f, 0.5f);
            vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(left, top) - parent.rect.center;
            vrt.sizeDelta = new Vector2(right - left, top - bottom);
            vrt.SetSiblingIndex(drt.GetSiblingIndex());

            descOriginal = (parent, drt.GetSiblingIndex(), drt.anchorMin, drt.anchorMax, drt.pivot, drt.offsetMin, drt.offsetMax);
            drt.SetParent(vrt, false);
            drt.anchorMin = new Vector2(0f, 1f); drt.anchorMax = Vector2.one; drt.pivot = new Vector2(0.5f, 1f);
            drt.offsetMin = drt.offsetMax = Vector2.zero;
            var fit = desc.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            descScroll = vp.GetComponent<ScrollRect>();
            descScroll.viewport = vrt;
            descScroll.content = drt;
            descScroll.horizontal = false;
            descScroll.movementType = ScrollRect.MovementType.Clamped;
            descScroll.scrollSensitivity = 25f;
        }

        static void RestoreDescription()
        {
            if (!descScroll) return;
            var drt = (RectTransform)descScroll.content;
            if (drt && descOriginal.parent)
            {
                Destroy(drt.GetComponent<ContentSizeFitter>());
                drt.SetParent(descOriginal.parent, false);
                drt.SetSiblingIndex(descOriginal.index);
                drt.anchorMin = descOriginal.aMin; drt.anchorMax = descOriginal.aMax; drt.pivot = descOriginal.pivot;
                drt.offsetMin = descOriginal.oMin; drt.offsetMax = descOriginal.oMax;
            }
            Destroy(descScroll.gameObject);
            descScroll = null;
        }

        // Live search across the top of the recipe list (same spot on every screen, whatever tabs other mods add).
        // The list (and its scrollbar) give up one row plus a little breathing room for it.
        static void MakeSearchBox(RectTransform list, Scrollbar bar)
        {
            float height = Mathf.Clamp(size / 1.4f, 18f, 32f), gap = 6f; // about an icon tall, not a framed button
            var lc = new Vector3[4];
            list.GetWorldCorners(lc);
            var go = NewUI("CCD_Search", list.parent, typeof(Image), typeof(LayoutElement));
            go.SetActive(false); // wire everything up before the input field initialises
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.position = lc[1]; // the list's top-left, right of the rail
            rt.sizeDelta = new Vector2(list.rect.width, height);
            Remember(list);
            list.offsetMax -= new Vector2(0f, height + gap);
            if (bar && !bar.transform.IsChildOf(list))
            {
                Remember((RectTransform)bar.transform);
                ((RectTransform)bar.transform).offsetMax -= new Vector2(0f, height + gap);
            }
            SetFrame(go.GetComponent<Image>(), dimmed: false);

            var glass = NewUI("magnifier", go.transform, typeof(Image));
            var grt = (RectTransform)glass.transform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0f, 0.5f);
            grt.sizeDelta = Vector2.one * height * 0.5f;
            grt.anchoredPosition = new Vector2(height * 0.3f, 0f);
            var gi = glass.GetComponent<Image>();
            gi.sprite = Icon("search");
            gi.color = tan;
            gi.raycastTarget = false;

            var area = NewUI("text area", go.transform, typeof(RectMask2D));
            var art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(height * 0.95f, 2f); art.offsetMax = new Vector2(-8f, -2f);
            float pt = Mathf.Min(fontSize * 0.75f * 0.9f * Pct(itemText), height * 0.6f);
            TextMeshProUGUI Text(string name, Color color)
            {
                var t = NewUI(name, area.transform).AddComponent<TextMeshProUGUI>();
                Stretch(t.gameObject, 0f);
                t.font = font;
                t.fontSize = pt;
                t.color = color;
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.richText = false;
                return t;
            }
            var placeholder = Text("placeholder", dim);
            placeholder.text = "Search…";
            placeholder.fontStyle = FontStyles.Italic;
            var text = Text("text", textCol);

            searchField = go.AddComponent<TMP_InputField>();
            searchField.textViewport = art;
            searchField.textComponent = text;
            searchField.placeholder = placeholder;
            searchField.fontAsset = font;
            searchField.pointSize = pt;
            searchField.lineType = TMP_InputField.LineType.SingleLine;
            searchField.restoreOriginalTextOnEscape = false; // Esc just leaves the box
            searchField.customCaretColor = true;
            searchField.caretColor = gold;
            searchField.selectionColor = new Color(gold.r, gold.g, gold.b, 0.35f);
            searchField.text = search;
            searchField.onValueChanged.AddListener(v => { search = v ?? ""; RefreshList(); }); // filters as you type
            go.SetActive(true);
        }

        static void Spacer(Transform parent, float width)
        {
            var le = NewUI("spacer", parent, typeof(LayoutElement)).GetComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = width;
        }

        static void SetFlag(char flag) => SetFlag(flag, !On(flag));

        static void SetFlag(char flag, bool on)
        {
            stationSort[Station()] = Flags().Replace(flag.ToString(), "") + (on ? flag.ToString() : "");
            Changed();
        }

        static void RefreshUi(InventoryGui gui)
        {
            EnsureUi(gui);
            bool craft = gui.InCraftTab();
            toolbar.gameObject.SetActive(craft);
            rail.gameObject.SetActive(craft);
            if (searchField) searchField.gameObject.SetActive(craft);
            if (!craft) { CloseFlyout(); return; }

            var all = Available();
            sortButtons[0].Set(On('A'));
            sortButtons[1].Set(On('C'));
            sortButtons[2].Set(On('K'));
            sortButtons[3].Set(On('B'));
            biomeArrow.color = On('B') ? gold : dim;

            var present = new HashSet<int>(all.Select(Biomes.OfRecipe));
            for (int i = 0; i < biomeButtons.Length; i++)
            {
                // All nine always show, in a fixed order, so muscle memory holds from station to station.
                string label = Biomes.Label(i);
                var tip = biomeButtons[i].GetComponent<UITooltip>();
                if (tip) tip.m_text = present.Contains(i) ? label : label + " (none at this station)";
                biomeButtons[i].Set(!hiddenBiomes.Contains(label), disabled: !On('B'));
            }

            // Rail is rebuilt each time: the station decides gear vs food groups.
            foreach (Transform ch in rail.Cast<Transform>().ToList()) { ch.SetParent(null, false); Destroy(ch.gameObject); }
            bool catOn = On('C');
            // Every group always shows, in a fixed order, even when this station has nothing in it.
            foreach (var g in GroupOrder())
            {
                bool mead = meadColors.ContainsKey(g), food = !mead && forkColors.ContainsKey(g);
                string group = g;
                var subs = all.SelectMany(Keys).Where(k => KeyGroup(k) == g).Distinct().ToList();
                string name = food ? g + " food" : g;
                var b = MakeButton(rail, mead ? Icon("group_mead") : food ? GameSprite("food_icon") : Icon("group_" + g.ToLower()),
                    subs.Count > 0 ? name : name + " (none at this station)", null, size);
                b.dragRow = "rail";
                b.isOn = () => !hiddenGroups.Contains(group) && (subs.Count == 0 || subs.Any(k => !hiddenSubs.Contains(k)));
                b.setOn = on =>
                {
                    if (!on) { hiddenGroups.Add(group); return; }
                    hiddenGroups.Remove(group);
                    // The sub-category choices are remembered; only if every one is unchecked does turning the group on show it all.
                    if (subs.Count > 0 && subs.All(hiddenSubs.Contains)) hiddenSubs.RemoveWhere(k => k.StartsWith(group + ">"));
                };
                b.onDisabledClick = () => SetFlag('C', true);
                bool shown = b.isOn();
                b.Set(shown, partial: shown && subs.Any(hiddenSubs.Contains), disabled: !catOn, tint: mead ? meadColors[g] : food ? forkColors[g] : (Color?)null);
                if (catOn && !foodStation && !meadStation && subs.Count > 1) AddFlyMarker(b, group);
            }
        }

        // A row or column of buttons inside the toolbar (the two-row biome block).
        static Transform Group(Transform parent, bool horizontal)
        {
            var go = NewUI("group", parent, horizontal ? typeof(HorizontalLayoutGroup) : typeof(VerticalLayoutGroup));
            var g = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
            g.spacing = buttonSpacing.Value;
            g.childAlignment = TextAnchor.UpperLeft;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            return go.transform;
        }

        static void Layout(GameObject go, bool horizontal)
        {
            go.AddComponent<LayoutElement>().ignoreLayout = true; // never let the game's own layout groups move us
            HorizontalOrVerticalLayoutGroup g = horizontal ? (HorizontalOrVerticalLayoutGroup)go.GetComponent<HorizontalLayoutGroup>() : go.GetComponent<VerticalLayoutGroup>();
            g.spacing = buttonSpacing.Value; // 0 = outer frames touch; each button's inner ring keeps it distinct
            g.childAlignment = horizontal ? TextAnchor.MiddleLeft : TextAnchor.UpperCenter;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            var fit = go.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static IconButton MakeButton(Transform parent, Sprite sprite, string tooltip, Action onClick, float s = 0f)
        {
            if (s <= 0f) s = size;
            // Two outlines: the frame's gray outer edge, then a thin chamfered inner ring (gold when on).
            var go = NewUI("CCD_Button", parent, typeof(Image), typeof(LayoutElement));
            var b = go.AddComponent<IconButton>();
            b.onClick = onClick;
            b.bg = go.GetComponent<Image>();
            SetFrame(b.bg, dimmed: false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = s;

            var ring = NewUI("ring", go.transform, typeof(Image));
            Stretch(ring, Mathf.Max(2f, s * 0.11f));
            b.ring = ring.GetComponent<Image>();
            b.ring.sprite = Chamfer(filled: false);
            b.ring.type = Image.Type.Sliced;
            b.ring.raycastTarget = false;

            var icon = NewUI("icon", go.transform, typeof(Image));
            Stretch(icon, s * (1f - Mathf.Min(0.6f * Pct(iconScale), 0.74f)) / 2f); // icon fills 60% of the button at 100%, never past the ring
            b.icon = icon.GetComponent<Image>();
            b.icon.sprite = sprite;
            b.icon.preserveAspect = true;
            b.icon.raycastTarget = false;

            var part = NewUI("partial", go.transform, typeof(Image));
            var prt = (RectTransform)part.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 0f);
            prt.sizeDelta = new Vector2(5f, 5f);
            prt.anchoredPosition = new Vector2(-2f, 2f);
            part.GetComponent<Image>().color = gold;
            part.GetComponent<Image>().raycastTarget = false;
            b.partial = part;

            if (tooltipPrefab)
            {
                var t = go.AddComponent<UITooltip>();
                t.m_tooltipPrefab = tooltipPrefab;
                t.m_text = tooltip;
            }
            return b;
        }

        // Small corner square with a right-pointing arrow; opens the sub-category checklist.
        static void AddFlyMarker(IconButton b, string group)
        {
            var m = NewUI("fly", b.transform, typeof(Image), typeof(Outline));
            var rt = (RectTransform)m.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(10f, 10f);
            rt.anchoredPosition = new Vector2(1f, 1f);
            m.GetComponent<Image>().color = panelCol;
            m.GetComponent<Outline>().effectColor = lineCol;
            var a = Arrow(m.transform, 7f, false, tan);
            ((RectTransform)a.transform).anchorMin = ((RectTransform)a.transform).anchorMax = new Vector2(0.5f, 0.5f);
            Clickable(m, () =>
            {
                if (flyout && flyGroup == group) { CloseFlyout(); return; }
                OpenFlyout(b, group);
            });
        }

        // ---- sub-category flyout: a checklist, text only ----

        static void OpenFlyout(IconButton anchor, string group)
        {
            CloseFlyout();
            var gui = InventoryGui.instance;
            flyGroup = group;
            var go = NewUI("CCD_Flyout", gui.m_crafting, typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            flyout = (RectTransform)go.transform;
            flyout.anchorMin = flyout.anchorMax = new Vector2(0.5f, 0.5f);
            flyout.pivot = new Vector2(0f, 1f);
            var c = new Vector3[4];
            ((RectTransform)anchor.transform).GetWorldCorners(c);
            flyout.position = new Vector3(c[2].x, c[1].y, c[1].z);
            flyout.anchoredPosition += new Vector2(6f, 0f);
            flyout.SetAsLastSibling();
            go.GetComponent<Image>().color = panelCol;
            go.GetComponent<Outline>().effectColor = gold;
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 14, 6, 8);
            v.spacing = 2f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fit = go.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            FillFlyout();
        }

        static void FillFlyout()
        {
            foreach (Transform ch in flyout.Cast<Transform>().ToList()) { ch.SetParent(null, false); Destroy(ch.gameObject); }
            string group = flyGroup;
            var keys = Available().SelectMany(Keys).Where(k => KeyGroup(k) == group).Distinct()
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            var title = Label(flyout, group, gold, 0.85f);
            var line = NewUI("underline", title.transform, typeof(Image));
            var lrt = (RectTransform)line.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = new Vector2(1f, 0f);
            lrt.offsetMin = new Vector2(0f, -2f); lrt.offsetMax = new Vector2(0f, -1f);
            line.GetComponent<Image>().color = new Color(gold.r, gold.g, gold.b, 0.6f);

            var links = Row(flyout);
            Clickable(Label(links.transform, "All", link, 0.7f), () => { foreach (var k in keys) hiddenSubs.Remove(k); hiddenGroups.Remove(group); Changed(); });
            Clickable(Label(links.transform, "None", link, 0.7f), () => { foreach (var k in keys) hiddenSubs.Add(k); Changed(); });

            foreach (var key in keys)
            {
                var row = Row(flyout);
                var box = Checkbox(row.transform, !hiddenSubs.Contains(key) && !hiddenGroups.Contains(group));
                // Press and drag down the checkboxes to set several at once (saved on release, like the rails).
                var dc = box.AddComponent<DragCheck>();
                dc.marker = box.transform.Find("marker").gameObject;
                dc.isOn = () => !hiddenSubs.Contains(key) && !hiddenGroups.Contains(group);
                dc.setOn = on =>
                {
                    hiddenGroups.Remove(group); // a group that was off comes back with its remembered choices
                    if (on) hiddenSubs.Remove(key); else hiddenSubs.Add(key);
                };
                Label(row.transform, key.Substring(key.IndexOf('>') + 1), textCol, 0.8f);
            }
        }

        static void CloseFlyout()
        {
            if (flyout) Destroy(flyout.gameObject);
            flyout = null;
            flyGroup = null;
        }

        // ---- small UI helpers ----

        // The game's octagon checkbox; the marker shows when checked.
        static GameObject Checkbox(Transform parent, bool on)
        {
            float s = fontSize * 0.75f;
            var box = NewUI("checkbox", parent, typeof(Image), typeof(LayoutElement));
            var le = box.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = s;
            var img = box.GetComponent<Image>();
            img.sprite = GameSprite("checkbox");
            img.color = img.sprite ? Color.white : tan;
            var mark = NewUI("marker", box.transform, typeof(Image));
            Stretch(mark, 0f);
            var mi = mark.GetComponent<Image>();
            mi.sprite = GameSprite("checkbox_marker");
            mi.color = gold;
            mi.raycastTarget = false;
            if (!mi.sprite) Stretch(mark, s * 0.28f);
            mark.SetActive(on);
            return box;
        }

        // Triangle arrow, pointing down when open and right when collapsed.
        static GameObject Arrow(Transform parent, float s, bool open, Color color)
        {
            var go = NewUI("arrow", parent, typeof(Image));
            var img = go.GetComponent<Image>();
            img.sprite = Triangle();
            img.preserveAspect = true;
            img.color = color;
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(s, s);
            rt.localEulerAngles = new Vector3(0f, 0f, open ? 0f : 90f);
            return go;
        }

        static GameObject Row(Transform parent)
        {
            var row = NewUI("row", parent, typeof(HorizontalLayoutGroup));
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return row;
        }

        static GameObject Label(Transform parent, string s, Color color, float scale)
        {
            var go = NewUI("text", parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = fontSize * scale;
            t.color = color;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.text = s;
            return go;
        }

        static GameObject Stretch(GameObject go, float inset)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return go;
        }

        static GameObject NewUI(string name, Transform parent, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            foreach (var c in components) go.AddComponent(c);
            return go;
        }

        static void Clickable(GameObject go, Action onClick) => go.AddComponent<Clicker>().onClick = onClick;

        // Icons shipped inside the DLL (Icons/*.png, white, tinted here).
        static Sprite Icon(string name)
        {
            if (sprites.TryGetValue(name, out var s) && s) return s;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name + ".png"))
            {
                if (stream == null) { Plugin.Log.LogWarning("missing icon " + name); return null; }
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                // mipmapped so the 64px art shrinks to button size with even line weights (no aliasing)
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                ImageConversion.LoadImage(tex, bytes);
                s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            return sprites[name] = s;
        }

        // Button/search background: the Craft button's sprite, its border scaled down to suit small buttons.
        // Off buttons are dimmed a step, disabled ones two.
        static void SetFrame(Image img, bool dimmed, bool disabled = false)
        {
            float k = disabled ? 0.5f : dimmed ? 0.75f : 1f;
            if (frame)
            {
                img.sprite = frame;
                img.type = Image.Type.Sliced;
                float border = Mathf.Max(frame.border.x, frame.border.y, frame.border.z, frame.border.w) * 100f / frame.pixelsPerUnit;
                img.pixelsPerUnitMultiplier = Mathf.Max(1f, border / 7f);
                img.color = new Color(k, k, k);
            }
            else
            {
                img.sprite = Chamfer(filled: true);
                img.type = Image.Type.Sliced;
                img.color = plainBg * k;
            }
        }

        // White 16px octagon (45° corners cut), 9-sliced so it fits any size: filled, or a 1px ring.
        static Sprite Chamfer(bool filled)
        {
            string key = filled ? "chamfer_fill" : "chamfer_ring";
            if (sprites.TryGetValue(key, out var s) && s) return s;
            const int n = 16; const float cut = 4f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Min(x + 0.5f, n - x - 0.5f), dy = Mathf.Min(y + 0.5f, n - y - 0.5f);
                    float d = Mathf.Min(Mathf.Min(dx, dy), (dx + dy - cut) / 1.4142f); // distance in from the octagon's edge
                    float a = Mathf.Clamp01(d + 0.5f) * (filled ? 1f : Mathf.Clamp01(1.5f - d));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return sprites[key] = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(6f, 6f, 6f, 6f));
        }

        // White 32px triangle pointing down, anti-aliased and mipmapped so it stays clean at small sizes.
        static Sprite Triangle()
        {
            if (sprites.TryGetValue("triangle", out var s) && s) return s;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            float len = Mathf.Sqrt(13f * 13f + 22f * 22f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // base along y = 27 from x 3 to 29, tip at (16, 5); d = distance in from the nearest edge
                    float px = x + 0.5f, py = y + 0.5f;
                    float left = ((px - 3f) * 22f + (py - 27f) * 13f) / len;
                    float right = ((29f - px) * 22f + (py - 27f) * 13f) / len;
                    float d = Mathf.Min(27f - py, Mathf.Min(left, right));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(d + 0.5f)));
                }
            tex.Apply();
            return sprites["triangle"] = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        // Sprites the game already has loaded (food fork marker, octagon checkbox).
        static Sprite GameSprite(string name)
        {
            string key = "game:" + name;
            if (sprites.TryGetValue(key, out var s) && s) return s;
            s = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(x => x.name == name);
            if (!s) Plugin.Log.LogWarning("game sprite not found: " + name);
            return sprites[key] = s;
        }

        // Also takes the pointer-down itself, so e.g. the flyout marker never reaches (and toggles) the rail button under it.
        class Clicker : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
        {
            public Action onClick;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left) onClick?.Invoke();
            }
            public void OnPointerDown(PointerEventData e) { }
        }

        // A flyout checkbox: click to toggle, or press and drag over its neighbours to give them the same state.
        class DragCheck : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
        {
            public GameObject marker;
            public Func<bool> isOn;
            public Action<bool> setOn;

            public void OnPointerDown(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left) return;
                dragValue = !isOn();
                Apply();
                dragging = "flyout";
            }

            public void OnPointerEnter(PointerEventData e)
            {
                if (dragging == "flyout" && Input.GetMouseButton(0) && isOn() != dragValue) Apply();
            }

            void Apply()
            {
                setOn(dragValue);
                marker.SetActive(dragValue);
            }
        }

        class IconButton : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerEnterHandler
        {
            public Action onClick, onDisabledClick;
            public Image bg, ring, icon;
            public GameObject partial;
            // Filter buttons (biomes, rail) toggle by press-and-drag; sort buttons (dragRow == null) are plain clicks.
            public string dragRow;
            public Func<bool> isOn;
            public Action<bool> setOn;
            bool disabled;
            Color? tint;

            // A faded (disabled) filter icon isn't a dead end: clicking it switches its sort on.
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left || dragRow != null) return;
                if (disabled) onDisabledClick?.Invoke();
                else onClick?.Invoke();
            }

            // The pressed button flips, and every button of the same row dragged over while the
            // mouse is held takes that same state. Saved once on release (Plugin.Update).
            public void OnPointerDown(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left || dragRow == null) return;
                if (disabled) { onDisabledClick?.Invoke(); return; }
                dragValue = !isOn();
                Apply();
                dragging = dragRow;
            }

            public void OnPointerEnter(PointerEventData e)
            {
                if (dragRow != null && dragging == dragRow && !disabled && Input.GetMouseButton(0) && isOn() != dragValue) Apply();
            }

            void Apply()
            {
                setOn(dragValue);
                Set(dragValue, false, disabled, tint);
            }

            // on = gold icon + gold inner ring; off = dimmer background, brown ring, muted icon; partial = gold corner
            // square; disabled (its sort is off) = darker still, never gold, so it can't be mistaken for "active".
            public void Set(bool on, bool partial = false, bool disabled = false, Color? tint = null)
            {
                this.disabled = disabled;
                this.tint = tint;
                bool lit = on && !disabled;
                SetFrame(bg, dimmed: !lit, disabled);
                ring.color = lit ? gold : disabled ? ringDisabled : ringOff;
                if (tint.HasValue)
                {
                    var t = tint.Value;
                    icon.color = new Color(t.r, t.g, t.b, lit ? 1f : disabled ? 0.18f : 0.35f);
                }
                else icon.color = lit ? gold : disabled ? iconDisabled : dim;
                this.partial.SetActive(partial && !disabled);
            }
        }

        // ---- patches ----

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        static class Awake_Patch
        {
            static void Postfix()
            {
                Biomes.Reset();
                originalOffsets.Clear(); // a new inventory UI: the old rects are gone
                descScroll = null;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Hide")]
        static class Hide_Patch
        {
            static void Postfix()
            {
                CloseFlyout();
                search = ""; // a forgotten search never hides recipes the next time you open a station
                if (searchField) searchField.SetTextWithoutNotify("");
            }
        }

        // While the search box has focus, the game's keyboard reads (ZInput) report "not pressed", so typing can't
        // close the inventory (Tab/E/Esc), walk, or hit hotbar keys. Mouse clicks go through Unity's UI and still work.
        [HarmonyPatch]
        static class ZInput_TypingBlock
        {
            static IEnumerable<MethodBase> TargetMethods() =>
                new[] { "GetButton", "GetButtonDown", "GetButtonUp", "GetKey", "GetKeyDown", "GetKeyUp" }
                    .SelectMany(n => AccessTools.GetDeclaredMethods(typeof(ZInput))
                        .Where(m => m.Name == n && m.IsStatic && m.ReturnType == typeof(bool)));

            static bool Prefix(ref bool __result)
            {
                if (!Typing()) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        static class UpdateRecipeList_Patch
        {
            static void Prefix(InventoryGui __instance, ref List<Recipe> recipes)
            {
                try
                {
                    if (dumpCsv.Value) Biomes.DumpOnce();
                    if (!__instance.InCraftTab()) return;
                    meadStation = MeadStation(recipes);
                    foodStation = !meadStation && FoodStation(recipes);
                    bool searching = !string.IsNullOrWhiteSpace(search);
                    recipes = recipes.Where(r => r.m_item && !Hidden(r) && (!searching || MatchesSearch(r))).ToList();
                }
                catch (Exception e) { Log.LogError(e); } // never break the game's own list: show it unfiltered instead
            }

            static void Postfix(InventoryGui __instance)
            {
                try
                {
                    EnsureUi(__instance); // sets the base font size Relayout needs
                    Relayout(__instance);
                    RefreshUi(__instance);
                }
                catch (Exception e) { Plugin.Log.LogError(e); }
            }
        }
    }
}
