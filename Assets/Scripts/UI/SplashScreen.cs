using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Splash screen: big extruded 3D title with colored boxes dropping onto a belt underneath.
/// Built entirely in code (same style as MainMenu). Shows once per app launch, then fades
/// into the main menu. Click or press any key to skip.
/// </summary>
public class SplashScreen : MonoBehaviour
{
    private static bool shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { shown = false; }

    /// <summary>Creates the splash if it hasn't been shown yet this session.</summary>
    public static void ShowOnce()
    {
        if (shown) return;
        shown = true;
        new GameObject("SplashScreen").AddComponent<SplashScreen>();
    }

    // ---- Tuning ----
    private const string TitleText = "BOX FACTORY";
    private const int TitleSize = 118;
    private const int Depth = 16;          // number of extrusion layers
    private const float DepthStep = 1.6f;  // pixels per layer
    private const float HoldTime = 4.6f;   // auto-continue time
    private const float FadeTime = 0.5f;
    private const float MinSkipTime = 0.6f;

    // Colors taken from the game's items so the splash matches the factory.
    private static readonly Color[] BoxColors =
    {
        new Color(0.90f, 0.25f, 0.25f), // Automation Pack
        new Color(1.00f, 0.67f, 0.00f), // Copper Wire
        new Color(0.68f, 0.90f, 0.36f), // Circuit
        new Color(0.30f, 0.85f, 0.35f), // Logistics Pack
        new Color(0.39f, 0.80f, 0.84f), // Lens
        new Color(0.30f, 0.55f, 1.00f), // Chemical Pack
        new Color(0.75f, 0.35f, 1.00f), // Quantum Pack
        new Color(0.92f, 0.00f, 1.00f), // Focus Crystal
        new Color(0.20f, 1.00f, 0.95f), // Portal Core
    };

    private class BoxView
    {
        public RectTransform Rt;
        public float Delay;
        public float BaseX;
    }

    private readonly List<RectTransform> titleLayers = new List<RectTransform>();
    private readonly List<BoxView> boxes = new List<BoxView>();
    private RectTransform titleRoot;
    private CanvasGroup group;
    private CanvasGroup titleGroup;
    private CanvasGroup subGroup;
    private Text prompt;
    private float startTime;
    private float fadeStart = -1f;

    void Awake()
    {
        startTime = Time.unscaledTime;
        Build();
    }

    // ---------------------------------------------------------------
    // Build
    // ---------------------------------------------------------------

    private void Build()
    {
        Canvas canvas = MenuUI.CreateCanvas("SplashCanvas", 500);
        canvas.transform.SetParent(transform, false);
        group = canvas.gameObject.AddComponent<CanvasGroup>();

        // Background (also blocks clicks on the menu underneath)
        RectTransform bg = MenuUI.MakePanel(canvas.transform, "Background", new Color(0.07f, 0.08f, 0.10f, 1f));
        MenuUI.Stretch(bg);

        // Faint grid-like floor glow behind the title
        RectTransform glow = MenuUI.MakePanel(bg, "Glow", new Color(0.95f, 0.62f, 0.12f, 0.05f));
        MenuUI.Center(glow, new Vector2(1000f, 220f));
        glow.anchoredPosition = new Vector2(0f, 70f);
        glow.GetComponent<Image>().raycastTarget = false;

        BuildTitle(bg);
        BuildSubtitle(bg);
        BuildBelt(bg);

        prompt = MakeText(bg, "Click or press any key", 16, FontStyle.Normal, MenuUI.DimText);
        SetRect(prompt.rectTransform, new Vector2(0f, -285f), new Vector2(600f, 30f));
        prompt.alignment = TextAnchor.MiddleCenter;
    }

    private void BuildTitle(Transform parent)
    {
        var rootGo = new GameObject("Title", typeof(RectTransform), typeof(CanvasGroup));
        rootGo.transform.SetParent(parent, false);
        titleRoot = rootGo.GetComponent<RectTransform>();
        titleGroup = rootGo.GetComponent<CanvasGroup>();
        SetRect(titleRoot, new Vector2(0f, 75f), new Vector2(1200f, 180f));

        Color front = MenuUI.Accent;
        Color deepest = new Color(0.30f, 0.14f, 0.02f, 1f);
        Color nearest = new Color(0.72f, 0.40f, 0.05f, 1f);

        // Back-to-front: deepest extrusion layer first, face last.
        for (int i = Depth; i >= 1; i--)
        {
            Color c = Color.Lerp(nearest, deepest, (float)i / Depth);
            Text layer = MakeText(titleRoot, TitleText, TitleSize, FontStyle.Bold, c);
            Stretch(layer.rectTransform);
            titleLayers.Add(layer.rectTransform);
        }

        // Face of the letters, with a dark edge for definition
        Text face = MakeText(titleRoot, TitleText, TitleSize, FontStyle.Bold, front);
        Stretch(face.rectTransform);
        var edge = face.gameObject.AddComponent<Outline>();
        edge.effectColor = new Color(0.45f, 0.22f, 0.02f, 1f);
        edge.effectDistance = new Vector2(2f, -2f);
        titleLayers.Add(face.rectTransform);

        // Soft highlight on the top-left edge of the face
        Text shine = MakeText(titleRoot, TitleText, TitleSize, FontStyle.Bold, new Color(1f, 0.88f, 0.55f, 0.35f));
        Stretch(shine.rectTransform);
        shine.rectTransform.anchoredPosition = new Vector2(-2f, 2f);
    }

    private void BuildSubtitle(Transform parent)
    {
        Text sub = MakeText(parent, "Build.  Automate.  Research.", 22, FontStyle.Italic, MenuUI.DimText);
        SetRect(sub.rectTransform, new Vector2(0f, -22f), new Vector2(700f, 34f));
        sub.alignment = TextAnchor.MiddleCenter;
        subGroup = sub.gameObject.AddComponent<CanvasGroup>();
    }

    private void BuildBelt(Transform parent)
    {
        var holder = new GameObject("BoxRow", typeof(RectTransform));
        holder.transform.SetParent(parent, false);
        var hrt = holder.GetComponent<RectTransform>();
        SetRect(hrt, new Vector2(0f, -140f), new Vector2(900f, 80f));

        // Belt strip under the boxes
        RectTransform belt = MenuUI.MakePanel(hrt, "Belt", new Color(0.20f, 0.20f, 0.21f, 1f));
        belt.anchorMin = belt.anchorMax = new Vector2(0.5f, 0.5f);
        belt.pivot = new Vector2(0.5f, 1f);
        belt.sizeDelta = new Vector2(880f, 16f);
        belt.anchoredPosition = new Vector2(0f, 0f);
        MenuUI.MakePanel(belt, "Rail", new Color(0.36f, 0.36f, 0.39f, 1f)).sizeDelta = new Vector2(0f, 0f);
        var rail = belt.GetChild(0).GetComponent<RectTransform>();
        rail.anchorMin = new Vector2(0f, 1f);
        rail.anchorMax = new Vector2(1f, 1f);
        rail.pivot = new Vector2(0.5f, 1f);
        rail.sizeDelta = new Vector2(0f, 4f);
        rail.anchoredPosition = Vector2.zero;
        belt.GetComponent<Image>().raycastTarget = false;
        rail.GetComponent<Image>().raycastTarget = false;

        // The boxes
        const float spacing = 88f;
        for (int i = 0; i < BoxColors.Length; i++)
        {
            float x = (i - (BoxColors.Length - 1) * 0.5f) * spacing;
            RectTransform box = BuildBox(hrt, BoxColors[i]);
            boxes.Add(new BoxView { Rt = box, Delay = 0.7f + i * 0.14f, BaseX = x });
            box.anchoredPosition = new Vector2(x, 600f);
        }
    }

    /// <summary>One 3D-looking box: dark extrusion behind, colored face, lighter top edge.</summary>
    private RectTransform BuildBox(Transform parent, Color color)
    {
        const float size = 56f;

        var go = new GameObject("Box", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(size, size);

        // depth (offset right and down)
        for (int i = 6; i >= 1; i--)
        {
            Color d = Color.Lerp(color * 0.75f, color * 0.35f, i / 6f);
            d.a = 1f;
            RectTransform layer = MenuUI.MakePanel(rt, "Depth", d);
            Fix(layer, size, new Vector2(i * 1.3f, -i * 1.3f));
        }

        RectTransform face = MenuUI.MakePanel(rt, "Face", color);
        Fix(face, size, Vector2.zero);

        // lighter top strip + left strip for a bevel feel
        Color hi = Color.Lerp(color, Color.white, 0.45f);
        RectTransform top = MenuUI.MakePanel(rt, "Top", hi);
        top.anchorMin = top.anchorMax = new Vector2(0.5f, 0.5f);
        top.pivot = new Vector2(0.5f, 1f);
        top.sizeDelta = new Vector2(size, 8f);
        top.anchoredPosition = new Vector2(0f, size * 0.5f);
        top.GetComponent<Image>().raycastTarget = false;

        // tape line across the middle, like a shipping box
        RectTransform tape = MenuUI.MakePanel(rt, "Tape", new Color(0f, 0f, 0f, 0.18f));
        tape.anchorMin = tape.anchorMax = new Vector2(0.5f, 0.5f);
        tape.sizeDelta = new Vector2(10f, size);
        tape.anchoredPosition = Vector2.zero;
        tape.GetComponent<Image>().raycastTarget = false;

        return rt;
    }

    private static void Fix(RectTransform r, float size, Vector2 offset)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(size, size);
        r.anchoredPosition = new Vector2(0f, size * 0.5f) + offset;
        r.GetComponent<Image>().raycastTarget = false;
    }

    // ---------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------

    void Update()
    {
        float t = Time.unscaledTime - startTime;

        AnimateTitle(t);
        AnimateBoxes(t);

        if (subGroup != null) subGroup.alpha = Mathf.Clamp01((t - 0.6f) / 0.6f);

        if (prompt != null)
        {
            float a = t < 1.8f ? 0f : 0.45f + 0.35f * Mathf.Sin(t * 3f);
            Color c = prompt.color;
            c.a = a;
            prompt.color = c;
        }

        // Skip / auto-continue
        if (fadeStart < 0f)
        {
            bool input = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                         || (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame
                                                       || Mouse.current.rightButton.wasPressedThisFrame));
            if ((input && t > MinSkipTime) || t > HoldTime) fadeStart = Time.unscaledTime;
        }
        else
        {
            float f = (Time.unscaledTime - fadeStart) / FadeTime;
            group.alpha = 1f - Mathf.Clamp01(f);
            if (f >= 1f) Destroy(gameObject);
        }
    }

    private void AnimateTitle(float t)
    {
        // Pop in with a little overshoot
        float k = Mathf.Clamp01(t / 0.8f);
        titleGroup.alpha = Mathf.Clamp01(t / 0.35f);
        float scale = Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(k));
        titleRoot.localScale = new Vector3(scale, scale, 1f);

        // The extrusion direction slowly swings, so the letters look like they turn in 3D.
        float angle = (-40f + Mathf.Sin(t * 0.9f) * 22f) * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        int layers = titleLayers.Count - 1; // last one is the face
        for (int i = 0; i < titleLayers.Count; i++)
        {
            int depthIndex = layers - i; // layer 0 is the deepest
            titleLayers[i].anchoredPosition = dir * (depthIndex * DepthStep);
        }
    }

    private void AnimateBoxes(float t)
    {
        foreach (var b in boxes)
        {
            float local = t - b.Delay;
            float y;
            if (local < 0f)
            {
                y = 600f;
            }
            else if (local < 0.7f)
            {
                y = Mathf.LerpUnclamped(600f, 0f, EaseOutBounce(local / 0.7f));
            }
            else
            {
                // gentle idle hop, travelling along like items on a belt
                y = Mathf.Max(0f, Mathf.Sin(t * 2.2f + b.BaseX * 0.02f)) * 6f;
            }
            b.Rt.anchoredPosition = new Vector2(b.BaseX, y);
        }
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    private static float EaseOutBounce(float x)
    {
        const float n1 = 7.5625f, d1 = 2.75f;
        if (x < 1f / d1) return n1 * x * x;
        if (x < 2f / d1) { x -= 1.5f / d1; return n1 * x * x + 0.75f; }
        if (x < 2.5f / d1) { x -= 2.25f / d1; return n1 * x * x + 0.9375f; }
        x -= 2.625f / d1; return n1 * x * x + 0.984375f;
    }

    // ---------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------

    private static Text MakeText(Transform parent, string content, int size, FontStyle style, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = MenuUI.UiFont;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.text = content;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void SetRect(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}