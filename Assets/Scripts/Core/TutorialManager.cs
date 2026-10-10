using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Step-by-step tutorial for a brand new game. Created by ProgressionBootstrap only when
///  - the game was started as a NEW game (not a loaded save), and
///  - the tutorial has not been finished or skipped before (PlayerPrefs flag).
/// Action steps complete themselves when the player does the thing; every step can also be
/// skipped, and "Skip tutorial" ends it for good.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public const string PrefKey = "BoxFactory.TutorialDone";

    public static TutorialManager Instance { get; private set; }

    public static bool IsDone { get { return PlayerPrefs.GetInt(PrefKey, 0) == 1; } }

    public static void MarkDone()
    {
        PlayerPrefs.SetInt(PrefKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Makes the tutorial show again on the next new game.</summary>
    public static void ResetFlag()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    private class Step
    {
        public string title;
        public string body;
        public Func<bool> done;   // null = info step (Next button)
        public Action begin;
    }

    private readonly List<Step> steps = new List<Step>();
    private int index = -1;
    private float pollTimer;
    private bool finished;

    private GameObject canvasGo;
    private Text stepLabel, titleLabel, bodyLabel;
    private Button nextButton;

    // step state
    private Vector2 camStart;
    private float zoomStart;

    void Awake()
    {
        Instance = this;
        MenuUI.EnsureEventSystem();
        BuildSteps();
        BuildUI();
        GoTo(0);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (finished || index < 0) return;

        pollTimer += Time.unscaledDeltaTime;
        if (pollTimer < 0.25f) return;
        pollTimer = 0f;

        Step s = steps[index];
        if (s.done != null && s.done()) GoTo(index + 1);
    }

    // ---------------------------------------------------------------
    // Steps
    // ---------------------------------------------------------------

    private void BuildSteps()
    {
        steps.Add(new Step
        {
            title = "Welcome to Box Factory",
            body = "Mine ore, turn it into parts, research new technology and finally power a Portal.\n\n" +
                   "This short tutorial takes about three minutes. You can skip it at any time."
        });

        steps.Add(new Step
        {
            title = "Move the camera",
            body = "Pan with W A S D or the arrow keys (hold Shift to go faster) or drag with the middle mouse button.\n" +
                   "Scroll the mouse wheel to zoom.",
            begin = () =>
            {
                var c = FindFirstObjectByType<TopDownCameraController>();
                if (c != null) { camStart = c.PlanePosition; zoomStart = c.OrthoSize; }
            },
            done = () =>
            {
                var c = FindFirstObjectByType<TopDownCameraController>();
                return c != null &&
                       ((c.PlanePosition - camStart).sqrMagnitude > 9f || Mathf.Abs(c.OrthoSize - zoomStart) > 1f);
            }
        });

        steps.Add(new Step
        {
            title = "Open the build menu",
            body = "Press Q to open the build menu.\n" +
                   "Tip: drag a building onto the hotbar to pick it with the number keys 1-9.",
            done = () => (UIManager.Instance != null && UIManager.Instance.IsBuildMenuOpen) ||
                         (BuildManager.Instance != null && BuildManager.Instance.IsPlacing) ||
                         Count<Miner>() > 0
        });

        steps.Add(new Step
        {
            title = "Place a Miner",
            body = "Pick Miner Mk1 and click on the map to place it. R rotates it.\n" +
                   "Miners need no power or fuel. Click a placed miner to choose what it mines.",
            done = () => Count<Miner>() > 0
        });

        steps.Add(new Step
        {
            title = "Lay a conveyor belt",
            body = "Pick Belt Mk1 and click-drag to lay a line of belts leading away from the miner.\n" +
                   "Belts connect to each other automatically.",
            done = () => Count<ConveyorBelt>() >= 3
        });

        steps.Add(new Step
        {
            title = "Build a Research Lab",
            body = "Place a Research Lab Mk1 at the end of your belt.\n" +
                   "The lab only takes items the current research needs; everything else waits on the belt.",
            done = () => Count<ResearchLab>() > 0
        });

        steps.Add(new Step
        {
            title = "Start your first research",
            body = "Press T to open the research window, select Smelting and press Start research.\n" +
                   "The cost is shown next to the technology.",
            done = ResearchStarted
        });

        steps.Add(new Step
        {
            title = "Feed the lab",
            body = "Make sure the miner is mining the ore Smelting needs (Iron Ore) and let the belt deliver it.\n" +
                   "Progress is shown at the top left. Wait until the research finishes.",
            done = FirstTechDone
        });

        steps.Add(new Step
        {
            title = "Machines and recipes",
            body = "You unlocked the Smelter. Machines take items from a belt, craft them and push the result out of their front.\n" +
                   "Place them inline: Miner > Belt > Smelter > Belt > Lab. Click any machine to see its recipes and progress."
        });

        steps.Add(new Step
        {
            title = "Science Packs",
            body = "Early technologies cost raw items. Later ones need Science Packs: Automation, Logistics, Chemical and Quantum.\n" +
                   "Research Automation unlocks the Science Assembler. It crafts packs from several ingredients at once, " +
                   "for example Gear + Copper Wire makes an Automation Pack. Belt the packs into your Research Labs."
        });

        steps.Add(new Step
        {
            title = "Handy controls",
            body = "R rotates a building under the cursor.\n" +
                   "Hold the right mouse button on a building to demolish it.\n" +
                   "Splitters share a belt between two outputs. Mk2 and Mk3 buildings are faster.\n" +
                   "Esc opens the pause menu where you can save."
        });

        steps.Add(new Step
        {
            title = "Your goal",
            body = "Research Portal Theory, craft Portal Cores in the Fabricator and deliver them, " +
                   "together with Focus Crystals, to the Portal.\n\nActivate the Portal to win. Good luck!"
        });
    }

    private static int Count<T>() where T : FactoryBuilding
    {
        var grid = GridManager.Instance;
        if (grid == null) return 0;
        int n = 0;
        foreach (var b in grid.GetAllBuildings()) if (b is T) n++;
        return n;
    }

    private static bool ResearchStarted()
    {
        var rm = ResearchManager.Instance;
        return rm != null && (rm.Current != null || rm.CompletedTechs.Any());
    }

    private static bool FirstTechDone()
    {
        var rm = ResearchManager.Instance;
        return rm != null && rm.CompletedTechs.Any();
    }

    // ---------------------------------------------------------------
    // Flow
    // ---------------------------------------------------------------

    private void GoTo(int i)
    {
        if (i >= steps.Count) { Finish(true); return; }

        index = i;
        pollTimer = 0f;
        Step s = steps[i];
        if (s.begin != null) s.begin();

        stepLabel.text = "TUTORIAL   " + (i + 1) + " / " + steps.Count;
        titleLabel.text = s.title;
        bodyLabel.text = s.body;

        bool last = i == steps.Count - 1;
        if (s.done == null) MenuUI.SetButton(nextButton, last ? "Finish" : "Next", MenuUI.ButtonGreen);
        else MenuUI.SetButton(nextButton, "Skip step", MenuUI.ButtonGray);
    }

    private void Finish(bool completed)
    {
        if (finished) return;
        finished = true;
        MarkDone(); // never shown again, whether finished or skipped

        GameToast.Show(completed
            ? "Tutorial complete. Good luck!"
            : "Tutorial skipped. You can replay it from the main menu.", 3f);

        if (canvasGo != null) Destroy(canvasGo);
        Destroy(gameObject);
    }

    // ---------------------------------------------------------------
    // UI
    // ---------------------------------------------------------------

    private void BuildUI()
    {
        Canvas canvas = MenuUI.CreateCanvas("TutorialCanvas", 90);
        canvasGo = canvas.gameObject;
        canvas.transform.SetParent(transform, false);

        RectTransform panel = MenuUI.MakePanel(canvas.transform, "TutorialPanel", MenuUI.PanelBg);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-14f, -14f);
        panel.sizeDelta = new Vector2(340f, 100f);
        MenuUI.MakeVLayout(panel.gameObject, 14, 8f);
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        stepLabel = MenuUI.MakeLabel(panel, "", 12, FontStyle.Bold, TextAnchor.MiddleLeft, 18f);
        stepLabel.color = MenuUI.DimText;

        titleLabel = MenuUI.MakeLabel(panel, "", 20, FontStyle.Bold, TextAnchor.MiddleLeft, 28f);
        titleLabel.color = MenuUI.Accent;

        bodyLabel = MenuUI.MakeLabel(panel, "", 14, FontStyle.Normal, TextAnchor.UpperLeft, 60f);
        bodyLabel.GetComponent<LayoutElement>().preferredHeight = -1f; // size to the wrapped text

        var rowGo = new GameObject("Buttons", typeof(RectTransform));
        rowGo.transform.SetParent(panel, false);
        rowGo.AddComponent<LayoutElement>().preferredHeight = 38f;
        var h = rowGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        h.childControlWidth = true;
        h.childControlHeight = true;

        Button skip = MenuUI.MakeButton(rowGo.transform, "Skip tutorial", MenuUI.ButtonRed, 38f, 14);
        skip.onClick.AddListener(() => Finish(false));

        nextButton = MenuUI.MakeButton(rowGo.transform, "Next", MenuUI.ButtonGreen, 38f, 14);
        nextButton.onClick.AddListener(() => GoTo(index + 1));
    }
}
