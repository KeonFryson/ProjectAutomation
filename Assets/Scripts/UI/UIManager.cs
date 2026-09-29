using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds the entire HUD in code at runtime: a money label, a toolbar of
/// building buttons (one per BuildManager.availableBuildings entry), and an
/// inspector panel with Upgrade/Demolish buttons for the selected building.
///
/// This means you do NOT need to hand-build a Canvas in the Editor — just
/// put this component on an empty GameObject named "UIManager" next to
/// BuildManager, and press Play.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private Text moneyText;
    private RectTransform toolbarParent;

    private GameObject inspectorPanel;
    private Text inspectorNameText;
    private Text inspectorLevelText;
    private Text inspectorUpgradeCostText;
    private Button upgradeButton;
    private Button demolishButton;

    private FactoryBuilding selectedBuilding;

    void Awake()
    {
        Instance = this;
        BuildUI();
    }

    void Start()
    {
        BuildToolbar();
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnMoneyChanged += UpdateMoneyText;
            UpdateMoneyText(EconomyManager.Instance.Money);
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
        BuildToolbarContainer(canvasGo.transform);
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
        moneyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        moneyText.fontSize = 26;
        moneyText.fontStyle = FontStyle.Bold;
        moneyText.alignment = TextAnchor.MiddleLeft;
        moneyText.color = Color.white;
        moneyText.text = "$0";
    }

    private void BuildToolbarContainer(Transform parent)
    {
        var go = new GameObject("Toolbar");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 20f);
        rt.sizeDelta = new Vector2(900f, 80f);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        toolbarParent = rt;
    }

    private void BuildToolbar()
    {
        if (toolbarParent == null || BuildManager.Instance == null) return;

        var defs = BuildManager.Instance.availableBuildings;
        for (int i = 0; i < defs.Count; i++)
        {
            int index = i; // capture for closure
            var def = defs[i];

            var go = new GameObject(def.displayName + "Button");
            go.transform.SetParent(toolbarParent, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 110f;
            le.preferredHeight = 60f;

            var img = go.AddComponent<Image>();
            img.color = def.iconColor;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => BuildManager.Instance.SelectBuildingToPlace(index));

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = def.displayName + "\n$" + def.buildCost;
        }
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
        rt.sizeDelta = new Vector2(230f, 190f);

        var panelImg = inspectorPanel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.65f);

        var vlg = inspectorPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 6f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        inspectorNameText = CreatePanelText(inspectorPanel.transform, "Building", 18, FontStyle.Bold);
        inspectorLevelText = CreatePanelText(inspectorPanel.transform, "Level 1", 15, FontStyle.Normal);
        inspectorUpgradeCostText = CreatePanelText(inspectorPanel.transform, "Upgrade: $0", 14, FontStyle.Normal);

        upgradeButton = CreatePanelButton(inspectorPanel.transform, "Upgrade", new Color(0.25f, 0.65f, 0.35f));
        upgradeButton.onClick.AddListener(OnUpgradeClicked);

        demolishButton = CreatePanelButton(inspectorPanel.transform, "Demolish", new Color(0.65f, 0.25f, 0.25f));
        demolishButton.onClick.AddListener(OnDemolishClicked);

        inspectorPanel.SetActive(false);
    }

    private Text CreatePanelText(Transform parent, string content, int fontSize, FontStyle style)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize + 8;

        var txt = go.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.color = Color.white;
        txt.text = content;
        return txt;
    }

    private Button CreatePanelButton(Transform parent, string label, Color color)
    {
        var go = new GameObject(label + "Button");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 34f;

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
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 14;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.text = label;

        return btn;
    }

    // ---------------------------------------------------------------
    // Runtime updates
    // ---------------------------------------------------------------

    private void UpdateMoneyText(int amount)
    {
        if (moneyText != null) moneyText.text = "$" + amount;
    }

    public void ShowInspector(FactoryBuilding building)
    {
        selectedBuilding = building;
        inspectorPanel.SetActive(true);

        string name = building.Definition != null ? building.Definition.displayName : building.name;
        inspectorNameText.text = name;
        inspectorLevelText.text = "Level " + building.Level;
        inspectorUpgradeCostText.text = "Upgrade: $" + building.GetUpgradeCost();
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
