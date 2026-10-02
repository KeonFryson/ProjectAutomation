using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Builds the whole HUD in code, styled after Factorio (gray panels, dark inset
/// areas, orange accents, square item slots):
///  - top-left: money + current research progress
///  - bottom: hotbar (keys 1-9) with the first unlocked buildings
///  - [Q] build menu with category tabs
///  - [T] tech tree window: nodes in columns, prerequisite lines, detail panel
///  - click a building: machine window (miner / processor / research lab widgets)
/// Put this on an empty GameObject named "UIManager" next to BuildManager.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // ---- Factorio-ish palette ----
    private static readonly Color PanelBg = new Color(0.20f, 0.20f, 0.21f, 0.97f);
    private static readonly Color PanelInner = new Color(0.10f, 0.10f, 0.11f, 1f);
    private static readonly Color TitleBar = new Color(0.31f, 0.31f, 0.33f, 1f);
    private static readonly Color Accent = new Color(0.95f, 0.62f, 0.12f, 1f);
    private static readonly Color ButtonGray = new Color(0.36f, 0.36f, 0.39f, 1f);
    private static readonly Color ButtonGreen = new Color(0.25f, 0.62f, 0.32f, 1f);
    private static readonly Color ButtonRed = new Color(0.65f, 0.25f, 0.25f, 1f);
    private static readonly Color SlotBorder = new Color(0.45f, 0.45f, 0.5f, 1f);
    private static readonly Color SlotBorderActive = Accent;
    private static readonly Color SlotInner = new Color(0.14f, 0.14f, 0.17f, 1f);
    private static readonly Color BarBg = new Color(0.12f, 0.12f, 0.15f, 1f);
    private static readonly Color BarFill = Accent;
    private static readonly Color DimText = new Color(1f, 1f, 1f, 0.65f);

    private const float NodeW = 190f, NodeH = 60f, ColSpacing = 250f, RowSpacing = 80f;
    private const string DefaultInfo = "Hover a building for details";

    private class Slot
    {
        public GameObject Root;
        public Image Border;
        public Image Icon;
        public ItemDefinition Item;
    }

    private class UiWindow
    {
        public GameObject Root;
        public Text Title;
        public RectTransform Body;
    }

    private class RecipeRow { public Image Background; public RecipeDefinition Recipe; }
    private class CostView { public ItemDefinition Item; public int Needed; public Text Label; }
    private class TechView
    {
        public TechDefinition Tech;
        public Image Bg;
        public RectTransform Fill;
        public Outline Outline;
        public Text Label;
    }
    private class HotbarView { public BuildingDefinition Def; public Slot Slot; }

    private Font uiFont;
    private Text moneyText;
    private Text hintText;

    // HUD research box
    private RectTransform hudResearchParent;
    private RectTransform hudBarFill;
    private readonly List<CostView> hudCosts = new List<CostView>();
    private TechDefinition hudTech;

    // Hotbar
    private GameObject hotbarPanel;
    private RectTransform hotbarParent;
    private Text hotbarInfoText;
    private readonly List<HotbarView> hotbarViews = new List<HotbarView>();
    private const string HotbarPrefKey = "BoxFactory.Hotbar";

    // Drag & drop from the build menu to the hotbar
    private GameObject dragGhost;
    private Image dragGhostImage;
    private BuildingDefinition dragDef;

    // Build menu
    private UiWindow menuWindow;
    private RectTransform menuTabsParent;
    private RectTransform menuGridParent;
    private Text menuInfoText;
    private string currentCategory;

    // Tech window
    private UiWindow techWindow;
    private RectTransform techContent;
    private readonly List<TechView> techViews = new List<TechView>();
    private TechDefinition selectedTech;
    private Text techDetailTitle, techDetailDesc, techUnlockText, techActionLabel;
    private RectTransform techCostRow;
    private Button techActionButton;
    private readonly List<CostView> detailCosts = new List<CostView>();

    // Machine window
    private UiWindow inspectorWindow;
    private Text inspectorStatsText;
    private Text inspectorTooltipText;
    private RectTransform inspectorOptionsParent;
    private Button demolishButton;
    private FactoryBuilding selectedBuilding;

    private Miner liveMiner;
    private Processor liveProcessor;
    private bool liveLab;
    private Text labStatusText;
    private Slot liveInputSlot;
    private Slot liveOutputSlot;
    private RectTransform liveBarFill;
    private readonly List<RecipeRow> recipeRows = new List<RecipeRow>();

    public bool IsBuildMenuOpen => menuWindow != null && menuWindow.Root.activeSelf;
    public bool IsTechOpen => techWindow != null && techWindow.Root.activeSelf;
    /// <summary>True while a big window is open (world clicks should be ignored).</summary>
    public bool IsMenuOpen => IsBuildMenuOpen || IsTechOpen;

    void Awake()
    {
        Instance = this;
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
    }

    void Start()
    {
        BuildTechTree();
        RebuildBuildMenu();
        LoadHotbar();

        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnMoneyChanged += UpdateMoneyText;
            UpdateMoneyText(EconomyManager.Instance.Money);
        }
        if (ResearchManager.Instance != null)
            ResearchManager.Instance.OnTechCompleted += OnTechCompleted;
    }

    void OnDestroy()
    {
        if (EconomyManager.Instance != null)
            EconomyManager.Instance.OnMoneyChanged -= UpdateMoneyText;
        if (ResearchManager.Instance != null)
            ResearchManager.Instance.OnTechCompleted -= OnTechCompleted;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.qKey.wasPressedThisFrame) SetBuildMenuOpen(!IsBuildMenuOpen);
            if (keyboard.tKey.wasPressedThisFrame) SetTechOpen(!IsTechOpen);

            for (int i = 0; i < hotbarViews.Count && i < 9; i++)
            {
                if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                    PickBuilding(hotbarViews[i].Def);
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (IsTechOpen) SetTechOpen(false);
                else if (IsBuildMenuOpen) SetBuildMenuOpen(false);
                else if (inspectorWindow.Root.activeSelf) HideInspector();
            }
        }

        if (inspectorWindow.Root.activeSelf)
        {
            if (selectedBuilding == null) inspectorWindow.Root.SetActive(false);
            else RefreshLive();
        }

        if (IsTechOpen) RefreshTechViews();
        RefreshResearchHud();
        RefreshHotbarHighlight();
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

        BuildHud(canvasGo.transform);
        BuildHotbar(canvasGo.transform);
        BuildHint(canvasGo.transform);
        BuildMenuPanel(canvasGo.transform);
        BuildTechWindow(canvasGo.transform);
        BuildInspectorPanel(canvasGo.transform);

        // Floating square that follows the mouse while dragging a building icon (must be last = on top).
        dragGhost = new GameObject("DragGhost");
        dragGhost.transform.SetParent(canvasGo.transform, false);
        var grt = dragGhost.AddComponent<RectTransform>();
        grt.sizeDelta = new Vector2(48f, 48f);
        dragGhostImage = dragGhost.AddComponent<Image>();
        dragGhostImage.raycastTarget = false;
        dragGhost.SetActive(false);
    }

    private void BuildHud(Transform parent)
    {
        var go = new GameObject("Hud");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(14f, -14f);
        rt.sizeDelta = new Vector2(260f, 10f);

        var bg = go.AddComponent<Image>();
        bg.color = PanelBg;
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 6, 8);
        vlg.spacing = 4f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        moneyText = CreateText(go.transform, "Money", "$0", 26, FontStyle.Bold, TextAnchor.MiddleLeft, 34f);

        var box = new GameObject("ResearchBox");
        box.transform.SetParent(go.transform, false);
        hudResearchParent = box.AddComponent<RectTransform>();
        var boxVlg = box.AddComponent<VerticalLayoutGroup>();
        boxVlg.spacing = 3f;
        boxVlg.childForceExpandWidth = true;
        boxVlg.childForceExpandHeight = false;
        boxVlg.childControlWidth = true;
        boxVlg.childControlHeight = true;
        box.SetActive(false);
    }

    private void BuildHotbar(Transform parent)
    {
        hotbarPanel = new GameObject("Hotbar");
        hotbarPanel.transform.SetParent(parent, false);
        hotbarParent = hotbarPanel.AddComponent<RectTransform>();
        hotbarParent.anchorMin = hotbarParent.anchorMax = hotbarParent.pivot = new Vector2(0.5f, 0f);
        hotbarParent.anchoredPosition = new Vector2(0f, 44f);

        var bg = hotbarPanel.AddComponent<Image>();
        bg.color = PanelBg;
        var hlg = hotbarPanel.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(6, 6, 6, 6);
        hlg.spacing = 4f;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        var fitter = hotbarPanel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Always 9 slots; they start empty and are filled by dragging from the build menu.
        for (int i = 0; i < 9; i++)
        {
            int index = i;
            var slot = CreateSlot(hotbarParent, 52f);
            CreateSlotText(slot.Root.transform, (i + 1).ToString(), 12, TextAnchor.UpperLeft);
            hotbarViews.Add(new HotbarView { Slot = slot });

            AddTriggerData(slot.Root, EventTriggerType.PointerClick, d => OnHotbarClicked(index, (PointerEventData)d));
            AddTriggerData(slot.Root, EventTriggerType.Drop, d => { if (dragDef != null) AssignHotbar(index, dragDef); });
            AddTrigger(slot.Root, EventTriggerType.PointerEnter, () =>
                hotbarInfoText.text = hotbarViews[index].Def != null
                    ? BuildingInfo(hotbarViews[index].Def)
                    : "Empty slot - drag a building here from the build menu");
            AddTrigger(slot.Root, EventTriggerType.PointerExit, () => hotbarInfoText.text = "");
        }

        var infoGo = new GameObject("HotbarInfo");
        infoGo.transform.SetParent(parent, false);
        var irt = infoGo.AddComponent<RectTransform>();
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0f);
        irt.anchoredPosition = new Vector2(0f, 114f);
        irt.sizeDelta = new Vector2(700f, 54f);
        hotbarInfoText = infoGo.AddComponent<Text>();
        hotbarInfoText.font = uiFont;
        hotbarInfoText.fontSize = 14;
        hotbarInfoText.alignment = TextAnchor.LowerCenter;
        hotbarInfoText.color = Color.white;
        hotbarInfoText.raycastTarget = false;
        hotbarInfoText.text = "";
    }

    private void BuildHint(Transform parent)
    {
        var go = new GameObject("HintText");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 14f);
        rt.sizeDelta = new Vector2(900f, 26f);

        hintText = go.AddComponent<Text>();
        hintText.font = uiFont;
        hintText.fontSize = 15;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.color = DimText;
        hintText.text = "[Q] Build   [T] Research   [1-9] Hotbar (drag from menu, double-click to clear)   [R] Rotate   [Right click] Cancel / hold to delete";
        hintText.raycastTarget = false;
    }

    private void BuildMenuPanel(Transform parent)
    {
        menuWindow = CreateWindow(parent, "BuildMenu", "Build  ([Q] to close)", new Vector2(600f, 460f),
            Vector2.zero, new Vector2(0.5f, 0.5f), false, () => SetBuildMenuOpen(false));
        var body = menuWindow.Body;

        var tabsGo = new GameObject("Tabs");
        tabsGo.transform.SetParent(body, false);
        menuTabsParent = tabsGo.AddComponent<RectTransform>();
        tabsGo.AddComponent<LayoutElement>().preferredHeight = 36f;
        var tabsHlg = tabsGo.AddComponent<HorizontalLayoutGroup>();
        tabsHlg.spacing = 6f;
        tabsHlg.childForceExpandWidth = false;
        tabsHlg.childForceExpandHeight = true;
        tabsHlg.childControlWidth = true;
        tabsHlg.childControlHeight = true;

        var gridGo = new GameObject("Grid");
        gridGo.transform.SetParent(body, false);
        menuGridParent = gridGo.AddComponent<RectTransform>();
        var gridBg = gridGo.AddComponent<Image>();
        gridBg.color = new Color(1f, 1f, 1f, 0.05f);
        gridBg.raycastTarget = false;
        var gridLe = gridGo.AddComponent<LayoutElement>();
        gridLe.flexibleHeight = 1f;
        gridLe.preferredHeight = 200f;
        var grid = gridGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(72f, 72f);
        grid.spacing = new Vector2(6f, 6f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.childAlignment = TextAnchor.UpperLeft;

        menuInfoText = CreateText(body, "Info", DefaultInfo, 14, FontStyle.Normal, TextAnchor.UpperLeft, 64f);
        menuWindow.Root.SetActive(false);
    }

    private void BuildInspectorPanel(Transform parent)
    {
        inspectorWindow = CreateWindow(parent, "MachineWindow", "Building", new Vector2(320f, 100f),
            new Vector2(-14f, -14f), new Vector2(1f, 1f), true, HideInspector);
        var body = inspectorWindow.Body;

        inspectorStatsText = CreateText(body, "Stats", "", 13, FontStyle.Normal, TextAnchor.MiddleLeft, 22f);
        inspectorStatsText.color = DimText;

        var optionsGo = new GameObject("Options");
        optionsGo.transform.SetParent(body, false);
        inspectorOptionsParent = optionsGo.AddComponent<RectTransform>();
        var optVlg = optionsGo.AddComponent<VerticalLayoutGroup>();
        optVlg.spacing = 6f;
        optVlg.childForceExpandWidth = true;
        optVlg.childForceExpandHeight = false;
        optVlg.childControlWidth = true;
        optVlg.childControlHeight = true;

        inspectorTooltipText = CreateText(body, "Tooltip", "", 13, FontStyle.Italic, TextAnchor.MiddleLeft, 20f);
        inspectorTooltipText.color = DimText;

        demolishButton = CreateButton(body, "Demolish", ButtonRed, 34f, 14);
        demolishButton.onClick.AddListener(OnDemolishClicked);

        inspectorWindow.Root.SetActive(false);
    }

    // ---------------------------------------------------------------
    // Windows
    // ---------------------------------------------------------------

    private UiWindow CreateWindow(Transform parent, string name, string title, Vector2 size, Vector2 pos,
        Vector2 anchorAndPivot, bool fitHeight, UnityEngine.Events.UnityAction onClose)
    {
        var w = new UiWindow();

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        w.Root = go;
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchorAndPivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        go.AddComponent<Image>().color = PanelBg;
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 6f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        if (fitHeight) go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Title bar
        var bar = new GameObject("TitleBar");
        bar.transform.SetParent(go.transform, false);
        bar.AddComponent<Image>().color = TitleBar;
        bar.AddComponent<LayoutElement>().preferredHeight = 30f;
        var hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(10, 4, 2, 2);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        w.Title = CreateText(bar.transform, "Title", title, 16, FontStyle.Bold, TextAnchor.MiddleLeft, 26f);
        w.Title.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var close = CreateButton(bar.transform, "X", ButtonRed, 26f, 14);
        close.GetComponent<LayoutElement>().preferredWidth = 28f;
        close.onClick.AddListener(onClose);

        // Dark inset body
        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(go.transform, false);
        bodyGo.AddComponent<Image>().color = PanelInner;
        var ble = bodyGo.AddComponent<LayoutElement>();
        if (!fitHeight) ble.flexibleHeight = 1f;
        var bvlg = bodyGo.AddComponent<VerticalLayoutGroup>();
        bvlg.padding = new RectOffset(8, 8, 8, 8);
        bvlg.spacing = 6f;
        bvlg.childForceExpandWidth = true;
        bvlg.childForceExpandHeight = false;
        bvlg.childControlWidth = true;
        bvlg.childControlHeight = true;
        w.Body = bodyGo.GetComponent<RectTransform>();

        return w;
    }

    public void SetBuildMenuOpen(bool open)
    {
        if (menuWindow == null) return;
        if (open)
        {
            techWindow.Root.SetActive(false);
            inspectorWindow.Root.SetActive(false);
            RebuildBuildMenu();
        }
        menuWindow.Root.SetActive(open);
    }

    public void SetTechOpen(bool open)
    {
        if (techWindow == null) return;
        if (open)
        {
            menuWindow.Root.SetActive(false);
            inspectorWindow.Root.SetActive(false);
            var rm = ResearchManager.Instance;
            if (selectedTech == null)
            {
                if (rm != null && rm.Current != null) ShowTechDetail(rm.Current);
                else if (techViews.Count > 0) ShowTechDetail(techViews[0].Tech);
            }
        }
        techWindow.Root.SetActive(open);
        if (open) RefreshTechViews();
    }

    // ---------------------------------------------------------------
    // Build menu + hotbar
    // ---------------------------------------------------------------

    private static string GetCategory(BuildingDefinition def)
    {
        return string.IsNullOrEmpty(def.category) ? "Buildings" : def.category;
    }

    private void OnTechCompleted()
    {
        RebuildBuildMenu();
        RebuildHotbar();
    }

    private void RebuildBuildMenu()
    {
        if (BuildManager.Instance == null) return;

        ClearChildren(menuTabsParent);

        var categories = new List<string>();
        foreach (var def in BuildManager.Instance.availableBuildings)
        {
            if (def == null || !ResearchManager.IsBuildingUnlocked(def)) continue;
            string cat = GetCategory(def);
            if (!categories.Contains(cat)) categories.Add(cat);
        }

        foreach (var cat in categories)
        {
            string captured = cat;
            var btn = CreateButton(menuTabsParent, cat, ButtonGray, 36f, 14);
            btn.GetComponent<LayoutElement>().preferredWidth = 120f;
            btn.name = "Tab_" + cat;
            btn.onClick.AddListener(() => ShowCategory(captured));
        }

        menuTabsParent.gameObject.SetActive(categories.Count > 1);

        if (categories.Count == 0)
        {
            ClearChildren(menuGridParent);
            menuInfoText.text = "Nothing unlocked yet.";
            return;
        }

        if (currentCategory == null || !categories.Contains(currentCategory))
            currentCategory = categories[0];
        ShowCategory(currentCategory);
    }

    private void ShowCategory(string category)
    {
        currentCategory = category;

        foreach (Transform tab in menuTabsParent)
        {
            var img = tab.GetComponent<Image>();
            if (img != null) img.color = tab.name == "Tab_" + category ? Accent : ButtonGray;
        }

        ClearChildren(menuGridParent);

        var defs = BuildManager.Instance.availableBuildings;
        for (int i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            if (def == null || !ResearchManager.IsBuildingUnlocked(def) || GetCategory(def) != category) continue;
            CreateBuildIcon(def);
        }

        menuInfoText.text = DefaultInfo;
    }

    private void CreateBuildIcon(BuildingDefinition def)
    {
        var slot = CreateSlot(menuGridParent, 72f);
        SetSlotColor(slot, def.iconColor, false);
        slot.Root.name = def.displayName + "Icon";

        var label = CreateSlotText(slot.Root.transform, def.displayName, 11, TextAnchor.LowerCenter);
        label.fontStyle = FontStyle.Bold;

        var btn = slot.Root.AddComponent<Button>();
        btn.targetGraphic = slot.Border;
        btn.onClick.AddListener(() =>
        {
            if (dragDef != null) return; // the release at the end of a drag is not a click
            PickBuilding(def);
        });

        AddTriggerData(slot.Root, EventTriggerType.BeginDrag, d => BeginIconDrag(def, (PointerEventData)d));
        AddTriggerData(slot.Root, EventTriggerType.Drag, d => dragGhost.transform.position = ((PointerEventData)d).position);
        AddTriggerData(slot.Root, EventTriggerType.EndDrag, d => EndIconDrag());

        string info = BuildingInfo(def);
        AddTrigger(slot.Root, EventTriggerType.PointerEnter, () => menuInfoText.text = info);
        AddTrigger(slot.Root, EventTriggerType.PointerExit, () => menuInfoText.text = DefaultInfo);
    }

    private static string BuildingInfo(BuildingDefinition def)
    {
        var size = Footprint.ClampSize(def.size);
        var sb = new System.Text.StringBuilder();
        sb.Append(def.displayName).Append("    $").Append(def.buildCost);
        sb.Append("\nSize ").Append(size.x).Append("x").Append(size.y);
        sb.Append("    Speed x").Append(def.speedMultiplier.ToString("0.##"));
        if (def.prefab is Seller) sb.Append("    Sell value x").Append(def.sellMultiplier.ToString("0.##"));
        if (!string.IsNullOrEmpty(def.description)) sb.Append("\n").Append(def.description);
        return sb.ToString();
    }

    private void PickBuilding(BuildingDefinition def)
    {
        if (BuildManager.Instance == null || def == null) return;
        int index = BuildManager.Instance.availableBuildings.IndexOf(def);
        if (index < 0) return;
        BuildManager.Instance.SelectBuildingToPlace(index);
        menuWindow.Root.SetActive(false);
        techWindow.Root.SetActive(false);
    }

    private void BeginIconDrag(BuildingDefinition def, PointerEventData e)
    {
        dragDef = def;
        var c = def.iconColor;
        dragGhostImage.color = new Color(c.r, c.g, c.b, 0.85f);
        dragGhost.transform.position = e.position;
        dragGhost.SetActive(true);
    }

    private void EndIconDrag()
    {
        dragDef = null;
        dragGhost.SetActive(false);
    }

    private void OnHotbarClicked(int index, PointerEventData e)
    {
        var hv = hotbarViews[index];
        if (hv.Def == null) return;

        if (e.clickCount >= 2)
        {
            // Double click empties the slot.
            if (BuildManager.Instance != null && BuildManager.Instance.SelectedDefinition == hv.Def)
                BuildManager.Instance.CancelPlacement();
            hv.Def = null;
            RebuildHotbar();
            SaveHotbar();
        }
        else
        {
            PickBuilding(hv.Def);
        }
    }

    /// <summary>Puts a building in a slot, replacing whatever was there. A building lives in one slot at a time.</summary>
    private void AssignHotbar(int index, BuildingDefinition def)
    {
        if (index < 0 || index >= hotbarViews.Count || def == null) return;
        if (!ResearchManager.IsBuildingUnlocked(def)) return;

        foreach (var hv in hotbarViews)
            if (hv.Def == def) hv.Def = null;
        hotbarViews[index].Def = def;

        RebuildHotbar();
        SaveHotbar();
    }

    private void LoadHotbar()
    {
        if (BuildManager.Instance == null) return;
        var defs = BuildManager.Instance.availableBuildings;
        for (int i = 0; i < hotbarViews.Count; i++)
        {
            int idx = PlayerPrefs.GetInt(HotbarPrefKey + i, -1);
            hotbarViews[i].Def = idx >= 0 && idx < defs.Count ? defs[idx] : null;
        }
        RebuildHotbar();
    }

    private void SaveHotbar()
    {
        if (BuildManager.Instance == null) return;
        var defs = BuildManager.Instance.availableBuildings;
        for (int i = 0; i < hotbarViews.Count; i++)
            PlayerPrefs.SetInt(HotbarPrefKey + i, hotbarViews[i].Def != null ? defs.IndexOf(hotbarViews[i].Def) : -1);
        PlayerPrefs.Save();
    }

    /// <summary>Refreshes slot visuals from the assignments (also drops buildings that are no longer unlocked).</summary>
    private void RebuildHotbar()
    {
        foreach (var hv in hotbarViews)
        {
            if (hv.Def != null && !ResearchManager.IsBuildingUnlocked(hv.Def)) hv.Def = null;

            if (hv.Def != null)
            {
                SetSlotColor(hv.Slot, hv.Def.iconColor, false);
            }
            else
            {
                hv.Slot.Item = null;
                hv.Slot.Icon.enabled = false;
                hv.Slot.Border.color = SlotBorder;
            }
        }
    }

    private void RefreshHotbarHighlight()
    {
        if (BuildManager.Instance == null) return;
        var selected = BuildManager.Instance.SelectedDefinition;
        foreach (var hv in hotbarViews)
            hv.Slot.Border.color = hv.Def != null && hv.Def == selected ? SlotBorderActive : SlotBorder;
    }

    // ---------------------------------------------------------------
    // Research HUD
    // ---------------------------------------------------------------

    private void RefreshResearchHud()
    {
        var rm = ResearchManager.Instance;
        TechDefinition cur = rm != null ? rm.Current : null;

        if (cur != hudTech)
        {
            hudTech = cur;
            ClearChildren(hudResearchParent);
            hudCosts.Clear();
            hudBarFill = null;
            hudResearchParent.gameObject.SetActive(cur != null);

            if (cur != null)
            {
                var title = CreateText(hudResearchParent, "Title", "Researching: " + cur.displayName,
                    14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);
                title.color = Accent;
                hudBarFill = CreateProgressBar(hudResearchParent);
                BuildCostViews(hudResearchParent, cur, hudCosts, 22f, 12, 0f, 24f);
            }
        }

        if (cur == null) return;
        UpdateCostViews(hudCosts, cur);
        if (hudBarFill != null) hudBarFill.anchorMax = new Vector2(rm.Progress01(cur), 1f);
    }

    private void BuildCostViews(Transform parent, TechDefinition tech, List<CostView> list,
        float slotSize, int fontSize, float labelWidth, float rowHeight)
    {
        foreach (var c in tech.cost)
        {
            if (c == null || c.item == null) continue;
            var row = CreateRow(parent, rowHeight);
            var slot = CreateSlot(row, slotSize);
            SetSlot(slot, c.item);
            var label = CreateLabel(row, "", fontSize, labelWidth);
            list.Add(new CostView { Item = c.item, Needed = c.amount, Label = label });
        }
    }

    private void UpdateCostViews(List<CostView> list, TechDefinition tech)
    {
        var rm = ResearchManager.Instance;
        if (rm == null) return;
        foreach (var cv in list)
        {
            int have = Mathf.Min(cv.Needed, rm.GetDelivered(tech, cv.Item));
            cv.Label.text = cv.Item.itemName + "  " + have + "/" + cv.Needed;
            cv.Label.color = have >= cv.Needed ? new Color(0.5f, 1f, 0.55f) : Color.white;
        }
    }

    // ---------------------------------------------------------------
    // Tech tree window
    // ---------------------------------------------------------------

    private void BuildTechWindow(Transform parent)
    {
        techWindow = CreateWindow(parent, "TechWindow", "Research  ([T] to close)", new Vector2(980f, 660f),
            Vector2.zero, new Vector2(0.5f, 0.5f), false, () => SetTechOpen(false));
        var body = techWindow.Body;

        // Scrollable tree area
        var scrollGo = new GameObject("TechScroll");
        scrollGo.transform.SetParent(body, false);
        scrollGo.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.07f, 1f);
        var sle = scrollGo.AddComponent<LayoutElement>();
        sle.flexibleHeight = 1f;
        sle.preferredHeight = 300f;
        var scroll = scrollGo.AddComponent<ScrollRect>();

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vrt = viewport.AddComponent<RectTransform>();
        Stretch(vrt, 0f);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        techContent = content.AddComponent<RectTransform>();
        techContent.anchorMin = techContent.anchorMax = new Vector2(0f, 1f);
        techContent.pivot = new Vector2(0f, 1f);
        techContent.anchoredPosition = Vector2.zero;
        techContent.sizeDelta = new Vector2(100f, 100f);

        scroll.viewport = vrt;
        scroll.content = techContent;
        scroll.horizontal = true;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        // Detail panel
        var detail = new GameObject("Detail");
        detail.transform.SetParent(body, false);
        detail.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        var dle = detail.AddComponent<LayoutElement>();
        dle.preferredHeight = 210f;
        dle.minHeight = 210f;
        var dv = detail.AddComponent<VerticalLayoutGroup>();
        dv.padding = new RectOffset(8, 8, 6, 6);
        dv.spacing = 4f;
        dv.childForceExpandWidth = true;
        dv.childForceExpandHeight = false;
        dv.childControlWidth = true;
        dv.childControlHeight = true;

        techDetailTitle = CreateText(detail.transform, "Title", "Select a technology", 18, FontStyle.Bold, TextAnchor.MiddleLeft, 26f);
        techDetailTitle.color = Accent;
        techDetailDesc = CreateText(detail.transform, "Desc", "", 13, FontStyle.Normal, TextAnchor.UpperLeft, 40f);
        techCostRow = CreateRow(detail.transform, 44f);
        techUnlockText = CreateText(detail.transform, "Unlocks", "", 13, FontStyle.Italic, TextAnchor.MiddleLeft, 22f);
        techActionButton = CreateButton(detail.transform, "Start research", ButtonGreen, 34f, 15);
        techActionLabel = techActionButton.GetComponentInChildren<Text>();
        techActionButton.onClick.AddListener(OnTechActionClicked);

        techWindow.Root.SetActive(false);
    }

    private static int ComputeDepth(TechDefinition t, Dictionary<TechDefinition, int> memo, HashSet<TechDefinition> stack)
    {
        if (memo.TryGetValue(t, out int d)) return d;
        if (!stack.Add(t)) return 0; // cycle guard
        int best = 0;
        foreach (var p in t.prerequisites)
            if (p != null) best = Mathf.Max(best, ComputeDepth(p, memo, stack) + 1);
        stack.Remove(t);
        memo[t] = best;
        return best;
    }

    private void BuildTechTree()
    {
        var rm = ResearchManager.Instance;
        if (rm == null) return;

        var depth = new Dictionary<TechDefinition, int>();
        var colCount = new Dictionary<int, int>();
        var pos = new Dictionary<TechDefinition, Vector2>();
        var order = new List<TechDefinition>();
        float maxX = 0f, maxY = 0f;

        foreach (var t in rm.allTechs)
        {
            if (t == null || pos.ContainsKey(t)) continue;
            int d = ComputeDepth(t, depth, new HashSet<TechDefinition>());
            colCount.TryGetValue(d, out int row);
            colCount[d] = row + 1;

            var p = new Vector2(24f + d * ColSpacing, -(24f + row * RowSpacing));
            pos[t] = p;
            order.Add(t);
            maxX = Mathf.Max(maxX, p.x + NodeW);
            maxY = Mathf.Max(maxY, -p.y + NodeH);
        }

        techContent.sizeDelta = new Vector2(maxX + 24f, maxY + 24f);

        // Lines first so nodes draw on top of them.
        foreach (var t in order)
        {
            foreach (var pre in t.prerequisites)
            {
                if (pre == null || !pos.TryGetValue(pre, out var pp)) continue;
                Vector2 a = pp + new Vector2(NodeW, -NodeH * 0.5f);
                Vector2 b = pos[t] + new Vector2(0f, -NodeH * 0.5f);
                CreateLine(techContent, a, b);
            }
        }

        foreach (var t in order) CreateTechNode(t, pos[t]);
    }

    private void CreateLine(RectTransform parent, Vector2 a, Vector2 b)
    {
        var go = new GameObject("Line");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        Vector2 d = b - a;
        rt.sizeDelta = new Vector2(d.magnitude, 3f);
        rt.anchoredPosition = a;
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.55f, 0.55f, 0.6f, 1f);
        img.raycastTarget = false;
    }

    private void CreateTechNode(TechDefinition t, Vector2 p)
    {
        var go = new GameObject("Tech_" + t.displayName);
        go.transform.SetParent(techContent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = p;
        rt.sizeDelta = new Vector2(NodeW, NodeH);

        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.enabled = false;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 14f);
        trt.offsetMax = new Vector2(-8f, -4f);
        var label = textGo.AddComponent<Text>();
        label.font = uiFont;
        label.fontSize = 14;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = Color.white;
        label.text = t.displayName;
        label.raycastTarget = false;

        var barGo = new GameObject("Bar");
        barGo.transform.SetParent(go.transform, false);
        var brt = barGo.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.offsetMin = new Vector2(4f, 4f);
        brt.offsetMax = new Vector2(-4f, 10f);
        var barImg = barGo.AddComponent<Image>();
        barImg.color = BarBg;
        barImg.raycastTarget = false;

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(barGo.transform, false);
        var frt = fillGo.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = new Vector2(0f, 1f);
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.color = BarFill;
        fillImg.raycastTarget = false;

        var captured = t;
        btn.onClick.AddListener(() => ShowTechDetail(captured));

        techViews.Add(new TechView { Tech = t, Bg = img, Fill = frt, Outline = outline, Label = label });
    }

    private void ShowTechDetail(TechDefinition t)
    {
        selectedTech = t;
        if (t == null) return;

        techDetailTitle.text = t.displayName;

        var req = new List<string>();
        foreach (var p in t.prerequisites) if (p != null) req.Add(p.displayName);
        string desc = t.description ?? "";
        if (req.Count > 0) desc += (desc.Length > 0 ? "\n" : "") + "Requires: " + string.Join(", ", req);
        techDetailDesc.text = desc;

        var unlocks = new List<string>();
        foreach (var b in t.unlocks) if (b != null) unlocks.Add(b.displayName);
        techUnlockText.text = unlocks.Count > 0 ? "Unlocks: " + string.Join(", ", unlocks) : "Unlocks: nothing";

        ClearChildren(techCostRow);
        detailCosts.Clear();
        foreach (var c in t.cost)
        {
            if (c == null || c.item == null) continue;
            var slot = CreateSlot(techCostRow, 40f);
            SetSlot(slot, c.item);
            var label = CreateLabel(techCostRow, "", 14, 140f);
            detailCosts.Add(new CostView { Item = c.item, Needed = c.amount, Label = label });
        }

        RefreshTechViews();
    }

    private void RefreshTechViews()
    {
        var rm = ResearchManager.Instance;
        if (rm == null) return;

        foreach (var v in techViews)
        {
            bool done = rm.IsCompleted(v.Tech);
            bool active = rm.Current == v.Tech;
            bool can = rm.CanStart(v.Tech);

            Color c;
            if (done) c = new Color(0.20f, 0.45f, 0.25f);
            else if (active) c = new Color(0.72f, 0.46f, 0.10f);
            else if (can) c = new Color(0.30f, 0.34f, 0.42f);
            else c = new Color(0.16f, 0.16f, 0.18f);

            v.Bg.color = c;
            v.Label.color = (done || active || can) ? Color.white : DimText;
            v.Fill.anchorMax = new Vector2(done ? 1f : rm.Progress01(v.Tech), 1f);
            v.Outline.enabled = v.Tech == selectedTech;
        }

        if (selectedTech == null) return;

        UpdateCostViews(detailCosts, selectedTech);

        bool sDone = rm.IsCompleted(selectedTech);
        bool sActive = rm.Current == selectedTech;
        bool sCan = rm.CanStart(selectedTech);

        techActionLabel.text = sDone ? "Researched" : sActive ? "Cancel research" : sCan ? "Start research" : "Locked (finish prerequisites first)";
        techActionButton.interactable = !sDone && (sActive || sCan);
        techActionButton.targetGraphic.color = sActive ? ButtonRed : sCan ? ButtonGreen : ButtonGray;
    }

    private void OnTechActionClicked()
    {
        var rm = ResearchManager.Instance;
        if (rm == null || selectedTech == null) return;

        if (rm.Current == selectedTech) rm.CancelResearch();
        else if (rm.CanStart(selectedTech)) rm.StartResearch(selectedTech);

        RefreshTechViews();
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

    private static void AddTriggerData(GameObject go, EventTriggerType type,
        UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
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

    private RectTransform CreateRow(Transform parent, float height)
    {
        var go = new GameObject("Row");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        go.AddComponent<LayoutElement>().preferredHeight = height;

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        return rt;
    }

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

    private Text CreateSlotText(Transform slotRoot, string content, int fontSize, TextAnchor anchor)
    {
        var go = new GameObject("SlotText");
        go.transform.SetParent(slotRoot, false);
        Stretch(go.AddComponent<RectTransform>(), 3f);
        var txt = go.AddComponent<Text>();
        txt.font = uiFont;
        txt.fontSize = fontSize;
        txt.alignment = anchor;
        txt.color = Color.white;
        txt.text = content;
        txt.raycastTarget = false;
        return txt;
    }

    // ---------------------------------------------------------------
    // Slots + progress bar
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

    private static void SetSlotColor(Slot slot, Color color, bool active)
    {
        slot.Item = null;
        slot.Icon.enabled = true;
        slot.Icon.color = color;
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
    // Machine window
    // ---------------------------------------------------------------

    private void UpdateMoneyText(int amount)
    {
        if (moneyText != null) moneyText.text = "$" + amount;
    }

    public void ShowInspector(FactoryBuilding building)
    {
        if (IsMenuOpen) return;

        selectedBuilding = building;
        inspectorWindow.Root.SetActive(true);

        inspectorWindow.Title.text = building.Definition != null ? building.Definition.displayName : building.name;

        string stats = "Size " + building.Size.x + "x" + building.Size.y
                       + "    Speed x" + building.SpeedMultiplier.ToString("0.##");
        if (building is Seller && building.Definition != null)
            stats += "    Sell value x" + building.Definition.sellMultiplier.ToString("0.##");
        inspectorStatsText.text = stats;

        RefreshInspectorOptions(building);
    }

    public void HideInspector()
    {
        selectedBuilding = null;
        if (inspectorWindow != null) inspectorWindow.Root.SetActive(false);
    }

    private void RefreshInspectorOptions(FactoryBuilding building)
    {
        ClearChildren(inspectorOptionsParent);
        inspectorTooltipText.text = "";
        liveMiner = null;
        liveProcessor = null;
        liveLab = false;
        labStatusText = null;
        liveInputSlot = null;
        liveOutputSlot = null;
        liveBarFill = null;
        recipeRows.Clear();

        if (building is Miner miner) BuildMinerWindow(miner);
        else if (building is Processor processor) BuildProcessorWindow(processor);
        else if (building is ResearchLab) BuildLabWindow();

        RefreshLive();
    }

    private void BuildLabWindow()
    {
        liveLab = true;
        CreateText(inspectorOptionsParent, "Header", "Research Lab", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);
        labStatusText = CreateText(inspectorOptionsParent, "Status", "", 13, FontStyle.Normal, TextAnchor.UpperLeft, 40f);
    }

    private void BuildMinerWindow(Miner miner)
    {
        liveMiner = miner;

        CreateText(inspectorOptionsParent, "Header", "Mining", 14, FontStyle.Bold, TextAnchor.MiddleLeft, 22f);

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
                RefreshInspectorOptions(miner);
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
        else if (liveLab && labStatusText != null)
        {
            var rm = ResearchManager.Instance;
            labStatusText.text = rm != null && rm.Current != null
                ? "Researching: " + rm.Current.displayName + " (" + Mathf.RoundToInt(rm.Progress01(rm.Current) * 100f) + "%)"
                : "No active research. Press [T] to pick one.";
        }
    }

    private void OnDemolishClicked()
    {
        if (selectedBuilding == null) return;
        selectedBuilding.Demolish();
        inspectorWindow.Root.SetActive(false);
        selectedBuilding = null;
    }
}