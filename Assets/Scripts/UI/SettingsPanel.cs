using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings window, built in code like SaveSlotPanel. Used by the main menu and the pause menu:
///   var panel = new SettingsPanel(parentTransform, onBack);
///   panel.Show();  panel.Close();  panel.IsOpen
/// Every control reads/writes GameSettings directly. Settings are saved when the panel closes.
/// </summary>
public class SettingsPanel
{
    public GameObject Root { get; private set; }
    public bool IsOpen { get { return Root != null && Root.activeSelf; } }

    private static readonly float[] AutosaveChoices = { 60f, 120f, 300f, 600f };

    private readonly List<Action> refreshers = new List<Action>();
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    public SettingsPanel(Transform parent, Action onBack)
    {
        BuildResolutionList();

        RectTransform rt = MenuUI.MakePanel(parent, "SettingsPanel", MenuUI.PanelBg);
        Root = rt.gameObject;
        MenuUI.Center(rt, new Vector2(620f, 100f));
        MenuUI.MakeVLayout(Root, 16, 6f);
        Root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text title = MenuUI.MakeLabel(Root.transform, "Settings", 26, FontStyle.Bold, TextAnchor.MiddleCenter, 40f);
        title.color = MenuUI.Accent;

        Header("Audio");
        AddSlider("Master volume", 0f, 1f,
            () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v,
            v => Mathf.RoundToInt(v * 100f) + "%");

        Header("Display");
        AddToggle("Fullscreen", () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);
        AddCycle("Resolution", ResolutionLabel, NextResolution);
        AddToggle("VSync", () => GameSettings.VSync, v => GameSettings.VSync = v);

        Header("Camera and interface");
        AddSlider("Camera pan speed", 0.25f, 3f,
            () => GameSettings.PanSpeed, v => GameSettings.PanSpeed = v, v => "x" + v.ToString("0.00"));
        AddSlider("Zoom speed", 0.25f, 3f,
            () => GameSettings.ZoomSpeed, v => GameSettings.ZoomSpeed = v, v => "x" + v.ToString("0.00"));
        AddToggle("Edge-of-screen scrolling", () => GameSettings.EdgePan, v => GameSettings.EdgePan = v);
        AddToggle("Show controls hint", () => GameSettings.ShowHints, v => GameSettings.ShowHints = v);

        Header("Saving");
        AddToggle("Autosave", () => GameSettings.AutosaveEnabled, v => GameSettings.AutosaveEnabled = v);
        AddCycle("Autosave interval", IntervalLabel, NextInterval);

        MenuUI.MakeLabel(Root.transform, "", 8, FontStyle.Normal, TextAnchor.MiddleCenter, 6f);

        MenuUI.MakeButton(Root.transform, "Reset to defaults", MenuUI.ButtonRed, 38f, 15).onClick.AddListener(() =>
        {
            GameSettings.ResetToDefaults();
            RefreshAll();
        });

        Button back = MenuUI.MakeButton(Root.transform, "Back", MenuUI.ButtonGray, 40f, 16);
        back.onClick.AddListener(() =>
        {
            Close();
            if (onBack != null) onBack();
        });

        Root.SetActive(false);
    }

    public void Show()
    {
        Root.SetActive(true);
        RefreshAll();
    }

    public void Close()
    {
        if (!IsOpen) return;
        GameSettings.Save();
        Root.SetActive(false);
    }

    private void RefreshAll()
    {
        foreach (var r in refreshers) r();
    }

    // ---------------------------------------------------------------
    // Resolution / interval helpers
    // ---------------------------------------------------------------

    private void BuildResolutionList()
    {
        var seen = new HashSet<Vector2Int>();
        foreach (Resolution r in Screen.resolutions)
        {
            var v = new Vector2Int(r.width, r.height);
            if (seen.Add(v)) resolutions.Add(v);
        }
        var cur = new Vector2Int(Screen.width, Screen.height);
        if (seen.Add(cur)) resolutions.Add(cur);

        resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
    }

    private static Vector2Int CurrentResolution()
    {
        return GameSettings.ResolutionWidth > 0
            ? new Vector2Int(GameSettings.ResolutionWidth, GameSettings.ResolutionHeight)
            : new Vector2Int(Screen.width, Screen.height);
    }

    private string ResolutionLabel()
    {
        Vector2Int r = CurrentResolution();
        return r.x + " x " + r.y;
    }

    private void NextResolution()
    {
        if (resolutions.Count == 0) return;
        int i = resolutions.IndexOf(CurrentResolution());
        Vector2Int next = resolutions[(i + 1) % resolutions.Count];
        GameSettings.SetResolution(next.x, next.y);
    }

    private static string IntervalLabel()
    {
        float m = GameSettings.AutosaveInterval / 60f;
        return m.ToString("0.#") + (m == 1f ? " minute" : " minutes");
    }

    private static void NextInterval()
    {
        foreach (float c in AutosaveChoices)
            if (c > GameSettings.AutosaveInterval + 0.5f) { GameSettings.AutosaveInterval = c; return; }
        GameSettings.AutosaveInterval = AutosaveChoices[0];
    }

    // ---------------------------------------------------------------
    // Row builders
    // ---------------------------------------------------------------

    private void Header(string text)
    {
        Text h = MenuUI.MakeLabel(Root.transform, text, 14, FontStyle.Bold, TextAnchor.LowerLeft, 24f);
        h.color = MenuUI.DimText;
    }

    private RectTransform MakeRow(string label)
    {
        RectTransform row = MenuUI.MakePanel(Root.transform, "Row", MenuUI.PanelInner);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(12, 12, 4, 4);
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childControlWidth = true;
        h.childControlHeight = true;

        Text l = MenuUI.MakeLabel(row, label, 15, FontStyle.Normal, TextAnchor.MiddleLeft, 30f);
        l.GetComponent<LayoutElement>().flexibleWidth = 1f;
        return row;
    }

    private void AddToggle(string label, Func<bool> get, Action<bool> set)
    {
        RectTransform row = MakeRow(label);
        Button b = MenuUI.MakeButton(row, "On", MenuUI.ButtonGreen, 30f, 14, 120f);
        b.onClick.AddListener(() =>
        {
            set(!get());
            Paint(b, get());
        });
        refreshers.Add(() => Paint(b, get()));
    }

    private static void Paint(Button b, bool on)
    {
        MenuUI.SetButton(b, on ? "On" : "Off", on ? MenuUI.ButtonGreen : MenuUI.ButtonGray);
    }

    /// <summary>Button that shows the current value and steps to the next one when clicked.</summary>
    private void AddCycle(string label, Func<string> current, Action next)
    {
        RectTransform row = MakeRow(label);
        Button b = MenuUI.MakeButton(row, "", MenuUI.ButtonGray, 30f, 14, 200f);
        b.onClick.AddListener(() =>
        {
            next();
            MenuUI.SetButton(b, current(), MenuUI.ButtonGray);
        });
        refreshers.Add(() => MenuUI.SetButton(b, current(), MenuUI.ButtonGray));
    }

    private void AddSlider(string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> format)
    {
        RectTransform row = MakeRow(label);
        Slider slider = MakeSlider(row, min, max);
        Text value = MenuUI.MakeLabel(row, "", 14, FontStyle.Normal, TextAnchor.MiddleRight, 30f);
        value.GetComponent<LayoutElement>().preferredWidth = 60f;

        slider.onValueChanged.AddListener(v =>
        {
            set(v);
            value.text = format(get());
        });
        refreshers.Add(() =>
        {
            slider.SetValueWithoutNotify(get());
            value.text = format(get());
        });
    }

    /// <summary>A slider made entirely from code (plain colored Images, no sprites needed).</summary>
    private static Slider MakeSlider(Transform parent, float min, float max)
    {
        var go = new GameObject("Slider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 220f;
        le.preferredHeight = 26f;

        // Track
        var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(go.transform, false);
        var trt = track.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 0.35f);
        trt.anchorMax = new Vector2(1f, 0.65f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var trackImg = track.GetComponent<Image>();
        trackImg.color = new Color(0.30f, 0.30f, 0.33f, 1f);

        // Fill
        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fart = fillArea.GetComponent<RectTransform>();
        fart.anchorMin = new Vector2(0f, 0.35f);
        fart.anchorMax = new Vector2(1f, 0.65f);
        fart.offsetMin = fart.offsetMax = Vector2.zero;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        fill.GetComponent<Image>().color = MenuUI.Accent;
        var frt = fill.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = frt.offsetMax = Vector2.zero;

        // Handle
        var handleArea = new GameObject("HandleArea", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var hart = handleArea.GetComponent<RectTransform>();
        hart.anchorMin = Vector2.zero;
        hart.anchorMax = Vector2.one;
        hart.offsetMin = new Vector2(8f, 0f);
        hart.offsetMax = new Vector2(-8f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleImg = handle.GetComponent<Image>();
        handleImg.color = Color.white;
        var hrt = handle.GetComponent<RectTransform>();
        hrt.sizeDelta = new Vector2(16f, 0f);

        var slider = go.AddComponent<Slider>();
        slider.fillRect = frt;
        slider.handleRect = hrt;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = false;
        return slider;
    }
}
