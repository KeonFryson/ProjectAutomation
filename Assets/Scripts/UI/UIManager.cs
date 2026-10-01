using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Builds the entire HUD in code at runtime:
///  - a money label
///  - a Factorio-style build menu (press Q): category tabs, icon grid, hover info
///  - a Factorio-style machine window (click a building):
///       Miner     -> resource slot, progress bar, output slot, and a grid
///                    of resource slots to click to change what it mines
///       Processor -> input slot, progress bar, output slot, and the list
///                    of recipes (the active one is highlighted)
///       Others    -> just Upgrade / Demolish
///    Slots and the progress bar update live while the window is open.
///
/// No Canvas needs to be built by hand: put this on an empty GameObject named
/// "UIManager" next to BuildManager and press Play.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private static readonly Color SlotBorder = new Color(0.45f, 0.45f, 0.5f, 1f);
    private static readonly Color SlotBorderActive = new Color(0.95f, 0.6f, 0.1f, 1f);
    private static readonly Color SlotInner = new Color(0.14f, 0.14f, 0.17f, 1f);
    private static readonly Color BarBg = new Color(0.12f, 0.12f, 0.15f, 1f);
    private static readonly Color BarFill = new Color(0.95f, 0.65f, 0.15f, 1f);

    /// <summary>One square inventory slot (border + dark inner + colored item icon).</summary>
    private class Slot
    {
        public GameObject Root;
        public Image Border;
        public Image Icon;
        public ItemDefinition Item;
    }

    private class RecipeRow
    {
        public Image Background;
        public RecipeDefinition Recipe;
    }

    private Font uiFont;
    private Text moneyText;
    private Text hintText;

    // Build menu
    private GameObject menuPanel;
    private RectTransform menuTabsParent;
    private RectTransform menuGridParent;
    private Text menuInfoText;

    // Machine window
    private GameObject inspectorPanel;
    private Text inspectorNameText;
    private Text inspectorLevelText;
    private Text inspectorUpgradeCostText;
    private Text inspectorTooltipText;
    private RectTransform inspectorOptionsParent;
    private Button upgradeButton;
    private Button demolishButton;

    private FactoryBuilding selectedBuilding;

    // Live widgets (rebuilt every time a machine is selected)
    private Miner liveMiner;
    private Processor liveProcessor;
    private Slot liveInputSlot;   // miner: resource, processor: input
    private Slot liveOutputSlot;
    private RectTransform liveBarFill;
    private readonly List<RecipeRow> recipeRows = new List<RecipeRow>();

    public bool IsBuildMenuOpen => menuPanel != null && menuPanel.activeSelf;

    void Awake()
    {
        Instance = this;
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
    }

    void Start()
    {
        BuildMenuContents();
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnMoneyChanged += UpdateMoneyText;
            UpdateMoneyText(EconomyManager.Instance.Money);
        }
    }

    void OnDestroy()
    {
        if (EconomyManager.Instance != null)
            EconomyManager.Instance.OnMoneyChanged -= UpdateMoneyText;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.qKey.wasPressedThisFrame) SetBuildMenuOpen(!IsBuildMenuOpen);
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (IsBuildMenuOpen) SetBuildMenuOpen(false);
                else if (inspectorPanel.activeSelf) HideInspector();
            }
        }

        if (inspectorPanel.activeSelf)
        {
            // Selected building was demolished some other way (e.g. right-click hold).
            if (selectedBuilding == null) inspectorPanel.SetActive(false);
            else RefreshLive();
        }
    }

    // ---------------------------------------------------------------
    // UI construction
    // ---------------------------------------------------------------

    private void BuildUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasGo.AddComponent<GraphicRaycaster>();

        BuildMoneyLabel(canvasGo.transform);
        BuildHint(canvasGo.transform);
        BuildMenuPanel(canvasGo.transform);
        BuildInspectorPanel(canvasGo.transform);
    }

    private void BuildMoneyLabel(Transform parent)
    {
        var go = new GameObject("MoneyText");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(20f, -20f);
        rt.sizeDelta = new Vector2(220f, 40f);

        moneyText = go.AddComponent<Text>();
        moneyText.font = uiFont;
        moneyText.fontSize = 26;
        moneyText.fontStyle = FontStyle.Bold;
        moneyText.alignment = TextAnchor.MiddleLeft;
        moneyText.color = Color.white;
        moneyText.text = "$0";
        moneyText.raycastTarget = false;
    }

    private void BuildHint(Transform parent)
    {
        var go = new GameObject("HintText");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 14f);
        rt.sizeDelta = new Vector2(600f, 30f);

        hintText = go.AddComponent<Text>();
        hintText.font = uiFont;
        hintText.fontSize = 16;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.color = new Color(1f, 1f, 1f, 0.75f);
        hintText.text = "[Q] Build menu    [R] Rotate    [Right click] Cancel / hold to delete";
        hintText.raycastTarget = false;
    }

    private void BuildMenuPanel(Transform parent)
    {
        menuPanel = new GameObject("BuildMenu");
        menuPanel.transform.SetParent(parent, false);

        var rt = menuPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(560f, 400f);

        var bg = menuPanel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

        var vlg = menuPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 12, 12);
        vlg.spacing = 8f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        CreateText(menuPanel.transform, "Title", "Build  ([Q] to close)", 20, FontStyle.Bold, TextAnchor.MiddleLeft, 28f);

        // Tab row
        var tabsGo = new GameObject("Tabs");
        tabsGo.transform.SetParent(menuPanel.transform, false);
        menuTabsParent = tabsGo.AddComponent<RectTransform>();
        var tabsLe = tabsGo.AddComponent<LayoutElement>();
        tabsLe.preferredHeight = 36f;
        var tabsHlg = tabsGo.AddComponent<HorizontalLayoutGroup>();
        tabsHlg.spacing = 6f;
        tabsHlg.childForceExpandWidth = false;
        tabsHlg.childForceExpandHeight = true;
        tabsHlg.childControlWidth = true;
        tabsHlg.childControlHeight = true;

        // Icon grid
        var gridGo = new GameObject("Grid");
        gridGo.transform.SetParent(menuPanel.transform, false);
        menuGridParent = gridGo.AddComponent<RectTransform>();
        var gridBg = gridGo.AddComponent<Image>();
        gridBg.color = new Color(1f, 1f, 1f, 0.06f);
        var gridLe = gridGo.AddComponent<LayoutElement>();
        gridLe.flexibleHeight = 1f;
        gridLe.preferredHeight = 200f;
        var grid = gridGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(76f, 76f);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.childAlignment = TextAnchor.UpperLeft;

        // Hover info line
        menuInfoText = CreateText(menuPanel.transform, "Info", "Hover a building for details", 15,
            FontStyle.Normal, TextAnchor.UpperLeft, 44f);

        menuPanel.SetActive(false);
    }

    private void BuildMenuContents()
    {
        if (BuildManager.Instance == null) return;

        // Collect categories in the order they first appear.
        var categories = new List<string>();
        foreach (var def in BuildManager.Instance.availableBuildings)
        {
            if (def == null) continue;
            string cat = GetCategory(def);
            if (!categories.Contains(cat)) categories.Add(cat);
        }

        foreach (var cat in categories)
        {
            string captured = cat;
            var btn = CreateButton(menuTabsParent, cat, new Color(0.25f, 0.25f, 0.3f), 36f, 14);
            var le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = 120f;
            btn.name = "Tab_" + cat;
            btn.onClick.AddListener(() => ShowCategory(captured));
        }

        // Tabs are pointless with a single category.
        menuTabsParent.gameObject.SetActive(categories.Count > 1);

        if (categories.Count > 0) ShowCategory(categories[0]);
    }

    private static string GetCategory(BuildingDefinition def)
    {
        return string.IsNullOrEmpty(def.category) ? "Buildings" : def.category;
    }

    private void ShowCategory(string category)
    {
        // Tab highlight
        foreach (Transform tab in menuTabsParent)
        {
            var img = tab.GetComponent<Image>();
            if (img != null)
                img.color = tab.name == "Tab_" + category
                    ? new Color(0.85f, 0.55f, 0.15f)
                    : new Color(0.25f, 0.25f, 0.3f);
        }

        ClearChildren(menuGridParent);

        var defs = BuildManager.Instance.availableBuildings;
        for (int i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            if (def == null || GetCategory(def) != category) continue;
            CreateBuildIcon(def, i);
        }

        menuInfoText.text = "Hover a building for details";
    }

    private void CreateBuildIcon(BuildingDefinition def, int index)
    {
        var go = new GameObject(def.displayName + "Icon");
        go.transform.SetParent(menuGridParent, false);

        var img = go.AddComponent<Image>();
        img.color = def.iconColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            BuildManager.Instance.SelectBuildingToPlace(index);
            SetBuildMenuOpen(false);
        });

        // Label
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(2f, 2f);
        labelRt.offsetMax = new Vector2(-2f, -2f);
        var label = labelGo.AddComponent<Text>();
        label.font = uiFont;
        label.fontSize = 12;
        label.alignment = TextAnchor.LowerCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = def.displayName;

        // Hover -> info line
        string info = def.displayName + "   $" + def.buildCost + "   (upgrade base $" + def.baseUpgradeCost + ")";
        AddTrigger(go, EventTriggerType.PointerEnter, () => menuInfoText.text = info);
        AddTrigger(go, EventTriggerType.PointerExit, () => menuInfoText.text = "Hover a building for details");
    }

    private void BuildInspectorPanel(Transform parent)
    {
        inspectorPanel = new GameObject("InspectorPanel");
        inspectorPanel.transform.SetParent(parent, false);

        var rt = inspectorPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-20f, -20f);
        rt.sizeDelta = new Vector2(300f, 200f);

        var panelImg = inspectorPanel.AddComponent<Image>();
        panelImg.color = new Color(0.08f, 0.08f, 0.1f, 0.94f);

        var vlg = inspectorPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 6f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;

        // Window grows with its content.
        var fitter = inspectorPanel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        inspectorNameText = CreateText(inspectorPanel.transform, "Name", "Building", 18, FontStyle.Bold, TextAnchor.MiddleLeft, 26f);
        inspectorLevelText = CreateText(inspectorPanel.transform, "Level", "Level 1", 15, FontStyle.Normal, TextAnchor.MiddleLeft, 23f);
        inspectorUpgradeCostText = CreateText(inspectorPanel.transform, "UpgradeCost", "Upgrade: $0", 14, FontStyle.Normal, TextAnchor.MiddleLeft, 22f);

        // Machine specific content lives here and is rebuilt on every selection.
        var optionsGo = new GameObject("Options");
        optionsGo.transform.SetParent(inspectorPanel.transform, false);
        inspectorOptionsParent = optionsGo.AddComponent<RectTransform>();
        var optVlg = optionsGo.AddComponent<VerticalLayoutGroup>();
        optVlg.spacing = 6f;
        optVlg.childForceExpandWidth = true;
        optVlg.childForceExpandHeight = false;
        optVlg.childControlWidth = true;
        optVlg.childControlHeight = true;

        inspectorTooltipText = CreateText(inspectorPanel.transform, "Tooltip", "", 13, FontStyle.Italic, TextAnchor.MiddleLeft, 20f);
        inspectorTooltipText.color = new Color(1f, 1f, 1f, 0.7f);

        upgradeButton = CreateButton(inspectorPanel.transform, "Upgrade", new Color(0.25f, 0.65f, 0.35f), 34f, 14);
        upgradeButton.onClick.AddListener(OnUpgradeClicked);

        demolishButton = CreateButton(inspectorPanel.transform, "Demolish", new Color(0.65f, 0.25f, 0.25f), 34f, 14);
        demolishButton.onClick.AddListener(OnDemolishClicked);

        inspectorPanel.SetActive(false);
    }

    // ---------------------------------------------------------------
    // Small UI helpers
    // ---------------------------------------------------------------

    private Text CreateText(Transform parent, string objName, string content, int fontSize,
        FontStyle style, TextAnchor anchor, float height)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        var txt = go.AddComponent<Text>();
        txt.font = uiFont;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = anchor;
        txt.color = Color.white;
        txt.text = content;
        txt.raycastTarget = false;
        return txt;
    }

    private Button CreateButton(Transform parent, string label, Color color, float height, int fontSize)
    {
        var go = new GameObject(label + "Button");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        var img = go.AddComponent<Image>();
        img.color = color;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var txt = textGo.AddComponent<Text>();
        txt.font = uiFont;
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.text = label;
        txt.raycastTarget = false;

        return btn;
    }

    private static void AddTrigger(GameObject go, EventTriggerType type, UnityEngine.Events.UnityAction action)
    {
        var trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    private static void ClearChildren(RectTransform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            child.SetActive(false); // leave layout immediately; Destroy happens end of frame
            Destroy(child);
        }
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Horizontal row that vertically centers its children.</summary>
    private RectTransform CreateRow(Transform parent, float height)
    {
        var go = new GameObject("Row");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        return rt;
    }

    /// <summary>Grid with a fixed number of columns so its height is predictable.</summary>
    private RectTransform CreateSlotGrid(Transform parent, int columns, float cell, float spacing)
    {
        var go = new GameObject("SlotGrid");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var grid = go.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cell, cell);
        grid.spacing = new Vector2(spacing, spacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperLeft;
        return rt;
    }

    private Text CreateLabel(Transform parent, string content, int fontSize, float width)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        if (width > 0f) le.preferredWidth = width; else le.flexibleWidth = 1f;
        le.preferredHeight = 24f;

        var txt = go.AddComponent<Text>();
        txt.font = uiFont;
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.color = Color.white;
        txt.text = content;
        txt.raycastTarget = false;
        return txt;
    }

    // ---------------------------------------------------------------
    // Slots + progress bar (the Factorio-style widgets)
    // ---------------------------------------------------------------

    private Slot CreateSlot(Transform parent, float size)
    {
        var go = new GameObject("Slot");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size;
        le.preferredHeight = size;
        le.minWidth = size;
        le.minHeight = size;

        var border = go.AddComponent<Image>();
        border.color = SlotBorder;

        var innerGo = new GameObject("Inner");
        innerGo.transform.SetParent(go.transform, false);
        Stretch(innerGo.AddComponent<RectTransform>(), 2f);
        var inner = innerGo.AddComponent<Image>();
        inner.color = SlotInner;
        inner.raycastTarget = false;

        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(go.transform, false);
        Stretch(iconGo.AddComponent<RectTransform>(), size * 0.2f);
        var icon = iconGo.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.enabled = false;

        return new Slot { Root = go, Border = border, Icon = icon };
    }

    private static void SetSlot(Slot slot, ItemDefinition item, bool active = false)
    {
        if (slot == null) return;
        slot.Item = item;
        slot.Icon.enabled = item != null;
        if (item != null) slot.Icon.color = item.color;
        slot.Border.color = active ? SlotBorderActive : SlotBorder;
    }

    private void AddSlotHover(Slot slot, string prefix)
    {
        AddTrigger(slot.Root, EventTriggerType.PointerEnter, () =>
            inspectorTooltipText.text = prefix + (slot.Item != null ? slot.Item.itemName : "empty"));
        AddTrigger(slot.Root, EventTriggerType.PointerExit, () => inspectorTooltipText.text = "");
    }

    private RectTransform CreateProgressBar(Transform parent)
    {
        var go = new GameObject("ProgressBar");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 14f;
        le.minHeight = 14f;

        var bg = go.AddComponent<Image>();
        bg.color = BarBg;
        bg.raycastTarget = false;

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(go.transform, false);
        var fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fill = fillGo.AddComponent<Image>();
        fill.color = BarFill;
        fill.raycastTarget = false;
        return fillRt;
    }

    private void SetBar(float progress)
    {
        if (liveBarFill != null)
            liveBarFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }

    // ---------------------------------------------------------------
    // Runtime updates
    // ---------------------------------------------------------------

    public void SetBuildMenuOpen(bool open)
    {
        if (menuPanel == null) return;
        menuPanel.SetActive(open);
        if (open) inspectorPanel.SetActive(false);
    }

    private void UpdateMoneyText(int amount)
    {
        if (moneyText != null) moneyText.text = "$" + amount;
    }

    public void ShowInspector(FactoryBuilding building)
    {
        if (IsBuildMenuOpen) return;

        selectedBuilding = building;
        inspectorPanel.SetActive(true);

        string buildingName = building.Definition != null ? building.Definition.displayName : building.name;
        inspectorNameText.text = buildingName;
        inspectorLevelText.text = "Level " + building.Level;
        inspectorUpgradeCostText.text = "Upgrade: $" + building.GetUpgradeCost();

        RefreshInspectorOptions(building);
    }

    public void HideInspector()
    {
        selectedBuilding = null;
        if (inspectorPanel != null) inspectorPanel.SetActive(false);
    }

    private void RefreshInspectorOptions(FactoryBuilding building)
    {
        ClearChildren(inspectorOptionsParent);
        inspectorTooltipText.text = "";
        liveMiner = null;
        liveProcessor = null;
        liveInputSlot = null;
        liveOutputSlot = null;
        liveBarFill = null;
        recipeRows.Clear();

        if (building is Miner miner) BuildMinerWindow(miner);
        else if (building is Processor processor) BuildProcessorWindow(processor);

        RefreshLive();
    }

    private void BuildMinerWindow(Miner miner)
    {
        liveMiner = miner;

        CreateText(inspectorOptionsParent, "Header", "Mining", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);

        // [resource] ====progress==== [output]
        var row = CreateRow(inspectorOptionsParent, 56f);
        liveInputSlot = CreateSlot(row, 52f);
        liveBarFill = CreateProgressBar(row);
        liveOutputSlot = CreateSlot(row, 52f);
        AddSlotHover(liveInputSlot, "Mining: ");
        AddSlotHover(liveOutputSlot, "Output: ");

        CreateText(inspectorOptionsParent, "Header2", "Choose resource:", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);

        var grid = CreateSlotGrid(inspectorOptionsParent, 5, 48f, 6f);
        foreach (var item in miner.availableItems)
        {
            if (item == null) continue;

            var slot = CreateSlot(grid, 48f);
            SetSlot(slot, item, miner.producedItem == item);

            var btn = slot.Root.AddComponent<Button>();
            btn.targetGraphic = slot.Border;

            var captured = item;
            btn.onClick.AddListener(() =>
            {
                miner.SetProducedItem(captured);
                RefreshInspectorOptions(miner); // re-highlight the chosen slot
            });

            AddTrigger(slot.Root, EventTriggerType.PointerEnter, () =>
                inspectorTooltipText.text = "Click to mine " + captured.itemName);
            AddTrigger(slot.Root, EventTriggerType.PointerExit, () => inspectorTooltipText.text = "");
        }
    }

    private void BuildProcessorWindow(Processor processor)
    {
        liveProcessor = processor;

        CreateText(inspectorOptionsParent, "Header", "Processing", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);

        // [input] ====progress==== [output]
        var row = CreateRow(inspectorOptionsParent, 56f);
        liveInputSlot = CreateSlot(row, 52f);
        liveBarFill = CreateProgressBar(row);
        liveOutputSlot = CreateSlot(row, 52f);
        AddSlotHover(liveInputSlot, "Input: ");
        AddSlotHover(liveOutputSlot, "Output: ");

        CreateText(inspectorOptionsParent, "Header2", "Recipes:", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);

        foreach (var r in processor.GetAllRecipes())
        {
            if (r == null || r.inputItem == null || r.outputItem == null) continue;

            var recipeRow = CreateRow(inspectorOptionsParent, 32f);
            var bg = recipeRow.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = false;

            var inSlot = CreateSlot(recipeRow, 28f);
            SetSlot(inSlot, r.inputItem);
            CreateLabel(recipeRow, "->", 14, 22f);
            var outSlot = CreateSlot(recipeRow, 28f);
            SetSlot(outSlot, r.outputItem);
            CreateLabel(recipeRow, r.inputItem.itemName + " -> " + r.outputItem.itemName
                                   + "  (" + r.processTime.ToString("0.#") + "s)", 12, 0f);

            recipeRows.Add(new RecipeRow { Background = bg, Recipe = r });
        }
    }

    /// <summary>Called every frame while the window is open: slots + bar follow the machine.</summary>
    private void RefreshLive()
    {
        if (liveMiner != null)
        {
            SetSlot(liveInputSlot, liveMiner.producedItem);
            SetSlot(liveOutputSlot, liveMiner.HeldItemDefinition);
            SetBar(liveMiner.Progress01);
        }
        else if (liveProcessor != null)
        {
            SetSlot(liveInputSlot, liveProcessor.InputItem);
            SetSlot(liveOutputSlot, liveProcessor.HeldItemDefinition);
            SetBar(liveProcessor.Progress01);

            RecipeDefinition active = liveProcessor.ActiveRecipe;
            foreach (var rr in recipeRows)
            {
                rr.Background.color = rr.Recipe == active
                    ? new Color(0.95f, 0.6f, 0.1f, 0.35f)
                    : new Color(1f, 1f, 1f, 0.05f);
            }
        }
    }

    private void OnUpgradeClicked()
    {
        if (selectedBuilding == null) return;
        if (selectedBuilding.TryUpgrade())
            ShowInspector(selectedBuilding); // refresh displayed level/cost
    }

    private void OnDemolishClicked()
    {
        if (selectedBuilding == null) return;
        selectedBuilding.Demolish();
        inspectorPanel.SetActive(false);
        selectedBuilding = null;
    }
}