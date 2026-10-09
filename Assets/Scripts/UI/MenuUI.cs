using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Small code-built UI helpers shared by the main menu, pause menu and toasts (Factorio-ish palette).</summary>
public static class MenuUI
{
    public static readonly Color PanelBg = new Color(0.20f, 0.20f, 0.21f, 0.97f);
    public static readonly Color PanelInner = new Color(0.10f, 0.10f, 0.11f, 1f);
    public static readonly Color Accent = new Color(0.95f, 0.62f, 0.12f, 1f);
    public static readonly Color ButtonGray = new Color(0.36f, 0.36f, 0.39f, 1f);
    public static readonly Color ButtonGreen = new Color(0.25f, 0.62f, 0.32f, 1f);
    public static readonly Color ButtonRed = new Color(0.65f, 0.25f, 0.25f, 1f);
    public static readonly Color DimText = new Color(1f, 1f, 1f, 0.65f);

    private static Font font;
    public static Font UiFont
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    public static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        var module = go.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    public static Canvas CreateCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform MakePanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go.GetComponent<RectTransform>();
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    public static VerticalLayoutGroup MakeVLayout(GameObject go, int padding, float spacing)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(padding, padding, padding, padding);
        v.spacing = spacing;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childControlWidth = true;
        v.childControlHeight = true;
        return v;
    }

    public static Text MakeLabel(Transform parent, string content, int fontSize, FontStyle style,
        TextAnchor anchor, float height)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;

        var txt = go.AddComponent<Text>();
        txt.font = UiFont;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = anchor;
        txt.color = Color.white;
        txt.text = content;
        txt.raycastTarget = false;
        return txt;
    }

    public static Button MakeButton(Transform parent, string label, Color color, float height, int fontSize,
        float width = 0f)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        if (width > 0f) le.preferredWidth = width;

        var img = go.AddComponent<Image>();
        img.color = color;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.pressedColor = new Color(0.70f, 0.70f, 0.70f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        btn.colors = colors;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var txt = textGo.AddComponent<Text>();
        txt.font = UiFont;
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.text = label;
        txt.raycastTarget = false;

        return btn;
    }

    public static void SetButton(Button b, string label, Color color)
    {
        b.GetComponentInChildren<Text>().text = label;
        b.targetGraphic.color = color;
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
