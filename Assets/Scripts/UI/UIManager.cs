using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Builds the entire HUD in code at runtime:
///  - a money label
///  - a Factorio-style build menu (press Q): category tabs on top, a grid of
///    square icons, and an info line that shows the hovered building
///  - an inspector panel (click a building) with Upgrade / Demolish, plus
///    building specific options:
///       Miner     -> pick which item it produces
///       Processor -> lists the recipes it can run
///
/// No Canvas needs to be built by hand: put this on an empty GameObject named
/// "UIManager" next to BuildManager and press Play.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private Font uiFont;
    private Text moneyText;
    private Text hintText;

    // Build menu
    private GameObject menuPanel;
    private RectTransform menuTabsParent;
    private RectTransform menuGridParent;
    private Text menuInfoText;
    private string activeCategory;

    // Inspector
    private GameObject inspectorPanel;
    private Text inspectorNameText;
    private Text inspectorLevelText;
    private Text inspectorUpgradeCostText;
    private RectTransform inspectorOptionsParent;
    private Button upgradeButton;
    private Button demolishButton;

    private FactoryBuilding selectedBuilding;

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
            if (keyboard.escapeKey.wasPressedThisFrame && IsBuildMenuOpen) SetBuildMenuOpen(false);
        }

        // Selected building was demolished some other way (e.g. right-click hold).
        if (inspectorPanel.activeSelf && selectedBuilding == null)
            inspectorPanel.SetActive(false);
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
        activeCategory = category;

        // Tab highlight
        foreach (Transform tab in menuTabsParent)
        {
            var img = tab.GetComponent<Image>();
            if (img != null)
                img.color = tab.name == "Tab_" + category
                    ? new Color(0.85f, 0.55f, 0.15f)
                    : new Color(0.25f, 0.25f, 0.3f);
        }

        // Clear old icons
        for (int i = menuGridParent.childCount - 1; i >= 0; i--)
        {
            var child = menuGridParent.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

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
        var trigger = go.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerEnter, () => menuInfoText.text = info);
        AddTrigger(trigger, EventTriggerType.PointerExit, () => menuInfoText.text = "Hover a building for details");
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
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
        rt.sizeDelta = new Vector2(240f, 190f);

        var panelImg = inspectorPanel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.65f);

        var vlg = inspectorPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 6f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;

        // Panel grows with its content (miner item list, recipe list, ...).
        var fitter = inspectorPanel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        inspectorNameText = CreateText(inspectorPanel.transform, "Name", "Building", 18, FontStyle.Bold, TextAnchor.MiddleLeft, 26f);
        inspectorLevelText = CreateText(inspectorPanel.transform, "Level", "Level 1", 15, FontStyle.Normal, TextAnchor.MiddleLeft, 23f);
        inspectorUpgradeCostText = CreateText(inspectorPanel.transform, "UpgradeCost", "Upgrade: $0", 14, FontStyle.Normal, TextAnchor.MiddleLeft, 22f);

        // Building specific options live here and are rebuilt on every selection.
        var optionsGo = new GameObject("Options");
        optionsGo.transform.SetParent(inspectorPanel.transform, false);
        inspectorOptionsParent = optionsGo.AddComponent<RectTransform>();
        var optVlg = optionsGo.AddComponent<VerticalLayoutGroup>();
        optVlg.spacing = 4f;
        optVlg.childForceExpandWidth = true;
        optVlg.childForceExpandHeight = false;
        optVlg.childControlWidth = true;
        optVlg.childControlHeight = true;

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
        // Clear old option widgets
        for (int i = inspectorOptionsParent.childCount - 1; i >= 0; i--)
        {
            var child = inspectorOptionsParent.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        if (building is Miner miner)
        {
            CreateText(inspectorOptionsParent, "Header", "Produces:", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);

            foreach (var item in miner.availableItems)
            {
                if (item == null) continue;
                bool active = miner.producedItem == item;
                Color c = item.color;
                Color shown = active ? c : new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 1f);

                var btn = CreateButton(inspectorOptionsParent,
                    item.itemName + (active ? "  (active)" : ""), shown, 30f, 14);

                var captured = item;
                btn.onClick.AddListener(() =>
                {
                    miner.SetProducedItem(captured);
                    RefreshInspectorOptions(miner);
                });
            }
        }
        else if (building is Processor processor)
        {
            CreateText(inspectorOptionsParent, "Header", "Recipes:", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);

            foreach (var r in processor.GetAllRecipes())
            {
                if (r == null || r.inputItem == null || r.outputItem == null) continue;
                CreateText(inspectorOptionsParent, "Recipe",
                    r.inputItem.itemName + " -> " + r.outputItem.itemName, 13,
                    FontStyle.Normal, TextAnchor.MiddleLeft, 20f);
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