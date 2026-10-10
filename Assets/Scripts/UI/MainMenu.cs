using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main menu, built entirely in code. Put it on any GameObject in the "MainMenu" scene
/// (GameBootstrap also creates it automatically if the scene is empty).
/// </summary>
public class MainMenu : MonoBehaviour
{
    private GameObject mainPanel;
    private SaveSlotPanel slotPanel;
    private Button continueButton;
    private Text continueInfo;

    void Awake()
    {
        Time.timeScale = 1f;
        EnsureCamera();
        MenuUI.EnsureEventSystem();
        Build();
        RefreshContinue();
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && slotPanel.IsOpen)
        {
            slotPanel.Close();
            ShowMain();
        }
    }

    private static void EnsureCamera()
    {
        if (FindFirstObjectByType<Camera>() != null) return;
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
        go.AddComponent<AudioListener>();
    }

    private void Build()
    {
        Canvas canvas = MenuUI.CreateCanvas("MenuCanvas", 0);
        RectTransform bg = MenuUI.MakePanel(canvas.transform, "Background", new Color(0.07f, 0.08f, 0.10f, 1f));
        MenuUI.Stretch(bg);

        RectTransform panel = MenuUI.MakePanel(bg, "MainPanel", MenuUI.PanelBg);
        mainPanel = panel.gameObject;
        MenuUI.Center(panel, new Vector2(380f, 100f));
        MenuUI.MakeVLayout(mainPanel, 22, 10f);
        mainPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text title = MenuUI.MakeLabel(panel, "BOX FACTORY", 44, FontStyle.Bold, TextAnchor.MiddleCenter, 60f);
        title.color = MenuUI.Accent;
        Text sub = MenuUI.MakeLabel(panel, "Build. Automate. Research.", 14, FontStyle.Italic, TextAnchor.MiddleCenter, 22f);
        sub.color = MenuUI.DimText;
        MenuUI.MakeLabel(panel, "", 12, FontStyle.Normal, TextAnchor.MiddleCenter, 8f);

        continueButton = MenuUI.MakeButton(panel, "Continue", MenuUI.ButtonGreen, 52f, 20);
        continueButton.onClick.AddListener(() => StartGame(SaveSystem.MostRecentSlot()));

        Button newGame = MenuUI.MakeButton(panel, "New Game", MenuUI.ButtonGray, 52f, 20);
        newGame.onClick.AddListener(() => StartGame(SaveSystem.NoSlot));

        Button tutorial = MenuUI.MakeButton(panel, "Tutorial", MenuUI.ButtonGray, 52f, 20);
        tutorial.onClick.AddListener(() => { TutorialManager.ResetFlag(); StartGame(SaveSystem.NoSlot); });

        Button load = MenuUI.MakeButton(panel, "Load Game", MenuUI.ButtonGray, 52f, 20);
        load.onClick.AddListener(OpenLoad);

        Button quit = MenuUI.MakeButton(panel, "Quit", MenuUI.ButtonRed, 52f, 20);
        quit.onClick.AddListener(MenuUI.QuitGame);

        continueInfo = MenuUI.MakeLabel(panel, "", 12, FontStyle.Normal, TextAnchor.MiddleCenter, 20f);
        continueInfo.color = MenuUI.DimText;

        slotPanel = new SaveSlotPanel(bg, ShowMain);
    }

    private void ShowMain()
    {
        mainPanel.SetActive(true);
        RefreshContinue();
    }

    private void OpenLoad()
    {
        mainPanel.SetActive(false);
        slotPanel.Show(SaveSlotPanel.Mode.Load, StartGame);
    }

    private void RefreshContinue()
    {
        int slot = SaveSystem.MostRecentSlot();
        continueButton.interactable = slot != SaveSystem.NoSlot;

        if (slot == SaveSystem.NoSlot)
        {
            continueInfo.text = "No saved games yet";
            return;
        }

        SaveData d = SaveSystem.Read(slot);
        continueInfo.text = "Latest: " + SaveSystem.SlotName(slot)
                            + (d != null ? "  -  " + SaveSystem.FormatSavedAt(d.savedAtTicks) : "");
    }

    private void StartGame(int slot)
    {
        SaveSystem.PendingLoadSlot = slot; // NoSlot = fresh game
        SceneManager.LoadScene(SaveSystem.GameScene);
    }
}
