using UnityEngine;

/// <summary>
/// All the tweakable look-and-feel values of the in-game UI (colors, sizes, fonts, texts, tech tree layout).
/// UIManager reads this at startup. Edit it in: Box Factory > Content Editor > UI tab (with live preview).
///
/// The asset must live at Assets/Resources/UITheme.asset so the built game can load it.
/// If no asset exists, the built-in defaults below are used (they match the old hard-coded look).
/// Put this file in Assets/Scripts/UI/.
/// </summary>
[CreateAssetMenu(fileName = "UITheme", menuName = "Box Factory/UI Theme")]
public class UITheme : ScriptableObject
{
    public const string ResourceName = "UITheme";

    [Header("Panels")]
    public Color panelBg = new Color(0.20f, 0.20f, 0.21f, 0.97f);
    public Color panelInner = new Color(0.10f, 0.10f, 0.11f, 1f);
    public Color titleBar = new Color(0.31f, 0.31f, 0.33f, 1f);
    public Color accent = new Color(0.95f, 0.62f, 0.12f, 1f);
    public Color dimText = new Color(1f, 1f, 1f, 0.65f);

    [Header("Buttons")]
    public Color buttonGray = new Color(0.36f, 0.36f, 0.39f, 1f);
    public Color buttonGreen = new Color(0.25f, 0.62f, 0.32f, 1f);
    public Color buttonRed = new Color(0.65f, 0.25f, 0.25f, 1f);

    [Header("Slots and bars")]
    public Color slotBorder = new Color(0.45f, 0.45f, 0.5f, 1f);
    public Color slotInner = new Color(0.14f, 0.14f, 0.17f, 1f);
    public Color barBg = new Color(0.12f, 0.12f, 0.15f, 1f);
    public Color barFill = new Color(0.95f, 0.62f, 0.12f, 1f);

    [Header("Tech tree")]
    public Vector2 techNodeSize = new Vector2(190f, 60f);
    public float techColumnSpacing = 250f;
    public float techRowSpacing = 80f;
    public int techNodeFontSize = 14;
    public Color techTreeBg = new Color(0.06f, 0.06f, 0.07f, 1f);
    public Color techLineColor = new Color(0.55f, 0.55f, 0.6f, 1f);
    public float techLineThickness = 3f;
    public Color techDone = new Color(0.20f, 0.45f, 0.25f, 1f);
    public Color techActive = new Color(0.72f, 0.46f, 0.10f, 1f);
    public Color techAvailable = new Color(0.30f, 0.34f, 0.42f, 1f);
    public Color techLocked = new Color(0.16f, 0.16f, 0.18f, 1f);

    [Header("Window sizes (reference pixels, canvas is 1280x720)")]
    public Vector2 buildMenuSize = new Vector2(600f, 460f);
    public Vector2 techWindowSize = new Vector2(980f, 660f);
    public Vector2 machineWindowSize = new Vector2(820f, 620f);

    [Header("Hotbar and build menu")]
    public float hotbarSlotSize = 52f;
    public float hotbarBottomOffset = 44f;
    public float buildIconSize = 72f;

    [Header("Fonts")]
    public int windowTitleFontSize = 16;
    public int bodyFontSize = 14;
    public int hintFontSize = 15;

    [Header("Texts")]
    public string buildMenuTitle = "Build  ([Q] to close)";
    public string techWindowTitle = "Research  ([T] to close)";
    [TextArea]
    public string hintText = "[Q] Build   [T] Research   [1-9] Hotbar (drag from menu, double-click to clear)   [R] Rotate   [Right click] Cancel / hold to delete";

    // ---------------------------------------------------------------

    private static UITheme current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { current = null; }

    /// <summary>The theme asset from Resources, or a default-valued instance if there is none.</summary>
    public static UITheme Current
    {
        get
        {
            if (current == null)
            {
                current = Resources.Load<UITheme>(ResourceName);
                if (current == null)
                {
                    current = CreateInstance<UITheme>();
                    current.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            return current;
        }
    }
}
