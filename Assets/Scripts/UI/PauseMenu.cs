using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Esc opens the pause menu (Resume / Save / Load / Main Menu / Quit).
/// Only opens when no other window or placement mode is active, so it never fights
/// with the Esc handling in UIManager and BuildManager.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private GameObject overlay;
    private GameObject menuPanel;
    private SaveSlotPanel slotPanel;

    private bool open;
    private bool blockedLastFrame;
    private readonly List<Behaviour> frozen = new List<Behaviour>();

    public bool IsOpen { get { return open; } }

    void Awake()
    {
        MenuUI.EnsureEventSystem();
        Build();
    }

    void OnDestroy()
    {
        if (open) Time.timeScale = 1f;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

        if (open)
        {
            if (slotPanel.IsOpen) { slotPanel.Close(); menuPanel.SetActive(true); }
            else Close();
        }
        else if (!blockedLastFrame)
        {
            Open();
        }
    }

    void LateUpdate()
    {
        // Remember whether another UI/mode was active this frame. Esc that closes a window
        // is handled by that window; it must not also open the pause menu on the same press.
        UIManager ui = UIManager.Instance;
        BuildManager bm = BuildManager.Instance;
        blockedLastFrame = (ui != null && (ui.IsMenuOpen || ui.IsInspectorOpen))
                           || (bm != null && bm.IsPlacing);
    }

    private void Build()
    {
        Canvas canvas = MenuUI.CreateCanvas("PauseCanvas", 100);
        canvas.transform.SetParent(transform, false);

        RectTransform dim = MenuUI.MakePanel(canvas.transform, "Overlay", new Color(0f, 0f, 0f, 0.65f));
        MenuUI.Stretch(dim);
        overlay = dim.gameObject;

        RectTransform panel = MenuUI.MakePanel(dim, "PausePanel", MenuUI.PanelBg);
        menuPanel = panel.gameObject;
        MenuUI.Center(panel, new Vector2(340f, 100f));
        MenuUI.MakeVLayout(menuPanel, 18, 8f);
        menuPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text title = MenuUI.MakeLabel(panel, "Paused", 30, FontStyle.Bold, TextAnchor.MiddleCenter, 44f);
        title.color = MenuUI.Accent;

        MenuUI.MakeButton(panel, "Resume", MenuUI.ButtonGreen, 46f, 18).onClick.AddListener(Close);
        MenuUI.MakeButton(panel, "Save Game", MenuUI.ButtonGray, 46f, 18).onClick.AddListener(OpenSave);
        MenuUI.MakeButton(panel, "Load Game", MenuUI.ButtonGray, 46f, 18).onClick.AddListener(OpenLoad);
        MenuUI.MakeButton(panel, "Main Menu", MenuUI.ButtonGray, 46f, 18).onClick.AddListener(GoToMainMenu);
        MenuUI.MakeButton(panel, "Quit to Desktop", MenuUI.ButtonRed, 46f, 18).onClick.AddListener(QuitToDesktop);

        slotPanel = new SaveSlotPanel(dim, () => menuPanel.SetActive(true));

        overlay.SetActive(false);
    }

    public void Open()
    {
        if (open) return;
        open = true;

        slotPanel.Close();
        menuPanel.SetActive(true);
        overlay.SetActive(true);

        Freeze(BuildManager.Instance);
        Freeze(UIManager.Instance);
        Freeze(FindFirstObjectByType<TopDownCameraController>());
        Time.timeScale = 0f;
    }

    public void Close()
    {
        if (!open) return;
        open = false;

        overlay.SetActive(false);
        slotPanel.Close();

        foreach (Behaviour b in frozen) if (b != null) b.enabled = true;
        frozen.Clear();
        Time.timeScale = 1f;
    }

    private void Freeze(Behaviour b)
    {
        if (b != null && b.enabled) { b.enabled = false; frozen.Add(b); }
    }

    private void OpenSave()
    {
        menuPanel.SetActive(false);
        slotPanel.Show(SaveSlotPanel.Mode.Save, slot =>
        {
            if (SaveManager.Instance != null) SaveManager.Instance.SaveToSlot(slot);
        });
    }

    private void OpenLoad()
    {
        menuPanel.SetActive(false);
        slotPanel.Show(SaveSlotPanel.Mode.Load, slot =>
        {
            Close();
            if (SaveManager.Instance != null) SaveManager.Instance.LoadFromSlot(slot);
        }, true);
    }

    private void GoToMainMenu()
    {
        Close();
        if (SaveManager.Instance != null) SaveManager.Instance.ReturnToMainMenu();
    }

    private void QuitToDesktop()
    {
        Close();
        MenuUI.QuitGame(); // SaveManager autosaves in OnApplicationQuit
    }
}
