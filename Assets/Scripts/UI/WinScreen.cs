using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// "Portal activated" win screen. Built in code like the other menus.
/// Sits below the pause menu (sorting 95 vs 100) so Esc still works on top of it.
/// The game keeps running behind it; "Keep Playing" just closes it.
/// </summary>
public static class WinScreen
{
    private static GameObject root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { root = null; }

    public static bool IsOpen { get { return root != null; } }

    public static void Show(Vector3 worldPosition)
    {
        if (root != null) return;

        for (int i = 0; i < 40; i++)
        {
            Vector3 p = worldPosition + (Vector3)(Random.insideUnitCircle * 1.5f);
            SellBurstEffect.Spawn(p, Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.9f, 1f));
        }

        Build();
    }

    private static void Build()
    {
        MenuUI.EnsureEventSystem();
        Canvas canvas = MenuUI.CreateCanvas("WinCanvas", 95);
        root = canvas.gameObject;

        RectTransform dim = MenuUI.MakePanel(canvas.transform, "Overlay", new Color(0f, 0f, 0f, 0.72f));
        MenuUI.Stretch(dim);

        RectTransform panel = MenuUI.MakePanel(dim, "WinPanel", MenuUI.PanelBg);
        MenuUI.Center(panel, new Vector2(540f, 100f));
        MenuUI.MakeVLayout(panel.gameObject, 24, 10f);
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text title = MenuUI.MakeLabel(panel, "PORTAL ACTIVATED", 40, FontStyle.Bold, TextAnchor.MiddleCenter, 56f);
        title.color = MenuUI.Accent;
        MenuUI.MakeLabel(panel, "You win!", 24, FontStyle.Bold, TextAnchor.MiddleCenter, 34f);

        Text body = MenuUI.MakeLabel(panel,
            "The portal hums to life. Every miner, belt and machine you built made this possible.",
            15, FontStyle.Normal, TextAnchor.MiddleCenter, 48f);
        body.color = MenuUI.DimText;

        MenuUI.MakeLabel(panel, "", 10, FontStyle.Normal, TextAnchor.MiddleCenter, 6f);

        Text stats = MenuUI.MakeLabel(panel, BuildStats(), 15, FontStyle.Normal, TextAnchor.MiddleCenter, 70f);
        stats.lineSpacing = 1.2f;

        MenuUI.MakeLabel(panel, "", 10, FontStyle.Normal, TextAnchor.MiddleCenter, 6f);

        MenuUI.MakeButton(panel, "Keep Playing", MenuUI.ButtonGreen, 48f, 20).onClick.AddListener(Close);
        MenuUI.MakeButton(panel, "Main Menu", MenuUI.ButtonGray, 44f, 18).onClick.AddListener(GoToMainMenu);
        MenuUI.MakeButton(panel, "Quit to Desktop", MenuUI.ButtonRed, 44f, 18).onClick.AddListener(MenuUI.QuitGame);
    }

    private static string BuildStats()
    {
        float seconds = SaveManager.Instance != null ? SaveManager.Instance.PlayTime : Time.timeSinceLevelLoad;
        int buildings = GridManager.Instance != null ? GridManager.Instance.GetAllBuildings().Count : 0;

        int techs = 0;
        if (ResearchManager.Instance != null)
            foreach (TechDefinition t in ResearchManager.Instance.CompletedTechs) techs++;

        return "Play time:  " + SaveSystem.FormatPlayTime(seconds)
               + "\nBuildings:  " + buildings
               + "\nTechnologies researched:  " + techs;
    }

    public static void Close()
    {
        if (root != null) Object.Destroy(root);
        root = null;
    }

    private static void GoToMainMenu()
    {
        Close();
        if (SaveManager.Instance != null) SaveManager.Instance.ReturnToMainMenu(); // autosaves first
        else SceneManager.LoadScene(SaveSystem.MenuScene);
    }
}
