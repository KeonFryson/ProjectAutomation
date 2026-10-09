using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable slot picker (Save or Load mode) with Autosave + 3 manual slots.
/// Overwriting, deleting and (optionally) loading need a second click to confirm.
/// </summary>
public class SaveSlotPanel
{
    public enum Mode { Save, Load }

    private class Row
    {
        public GameObject Root;
        public Text Info;
        public Button Action;
        public Button Delete;
    }

    public GameObject Root { get; private set; }
    public bool IsOpen { get { return Root != null && Root.activeSelf; } }

    private readonly Row[] rows = new Row[SaveSystem.ManualSlots + 1]; // index = slot, 0 = autosave
    private readonly Text title;

    private Mode mode;
    private Action<int> onPick;
    private bool confirmLoad;
    private int armedSlot = SaveSystem.NoSlot;
    private bool armedIsDelete;

    public SaveSlotPanel(Transform parent, Action onBack)
    {
        RectTransform rt = MenuUI.MakePanel(parent, "SaveSlotPanel", MenuUI.PanelBg);
        Root = rt.gameObject;
        MenuUI.Center(rt, new Vector2(720f, 100f));
        MenuUI.MakeVLayout(Root, 12, 8f);
        Root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = MenuUI.MakeLabel(Root.transform, "", 26, FontStyle.Bold, TextAnchor.MiddleCenter, 40f);
        title.color = MenuUI.Accent;

        for (int slot = 0; slot <= SaveSystem.ManualSlots; slot++)
            rows[slot] = BuildRow(slot);

        Button back = MenuUI.MakeButton(Root.transform, "Back", MenuUI.ButtonGray, 40f, 16);
        back.onClick.AddListener(() =>
        {
            Close();
            if (onBack != null) onBack();
        });

        Root.SetActive(false);
    }

    /// <summary>Opens the panel. onPick(slot) runs when the player confirms a slot.</summary>
    public void Show(Mode mode, Action<int> onPick, bool confirmLoad = false)
    {
        this.mode = mode;
        this.onPick = onPick;
        this.confirmLoad = confirmLoad;
        armedSlot = SaveSystem.NoSlot;
        Root.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        armedSlot = SaveSystem.NoSlot;
        Root.SetActive(false);
    }

    private Row BuildRow(int slot)
    {
        RectTransform rt = MenuUI.MakePanel(Root.transform, "Row" + slot, MenuUI.PanelInner);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;

        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(12, 12, 8, 8);
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childControlWidth = true;
        h.childControlHeight = true;

        var row = new Row { Root = rt.gameObject };
        row.Info = MenuUI.MakeLabel(rt, "", 14, FontStyle.Normal, TextAnchor.MiddleLeft, 64f);
        row.Info.supportRichText = true;
        row.Info.GetComponent<LayoutElement>().flexibleWidth = 1f;

        row.Action = MenuUI.MakeButton(rt, "Save", MenuUI.ButtonGreen, 44f, 15, 120f);
        row.Delete = MenuUI.MakeButton(rt, "Delete", MenuUI.ButtonRed, 44f, 14, 80f);

        int captured = slot;
        row.Action.onClick.AddListener(() => OnAction(captured));
        row.Delete.onClick.AddListener(() => OnDelete(captured));
        return row;
    }

    private void OnAction(int slot)
    {
        bool exists = SaveSystem.Exists(slot);
        bool needsConfirm = (mode == Mode.Save && exists) || (mode == Mode.Load && confirmLoad);
        if (needsConfirm && !(armedSlot == slot && !armedIsDelete))
        {
            armedSlot = slot;
            armedIsDelete = false;
            Refresh();
            return;
        }

        armedSlot = SaveSystem.NoSlot;
        if (onPick != null) onPick(slot);
        Refresh();
    }

    private void OnDelete(int slot)
    {
        if (!(armedSlot == slot && armedIsDelete))
        {
            armedSlot = slot;
            armedIsDelete = true;
            Refresh();
            return;
        }

        SaveSystem.Delete(slot);
        armedSlot = SaveSystem.NoSlot;
        Refresh();
    }

    private void Refresh()
    {
        if (!IsOpen) return;

        title.text = mode == Mode.Save ? "Save Game" : "Load Game";

        for (int slot = 0; slot <= SaveSystem.ManualSlots; slot++)
        {
            Row row = rows[slot];
            bool visible = mode == Mode.Load || slot != SaveSystem.AutosaveSlot;
            row.Root.SetActive(visible);
            if (!visible) continue;

            bool exists = SaveSystem.Exists(slot);
            SaveData data = exists ? SaveSystem.Read(slot) : null;
            row.Info.text = Describe(slot, exists, data);

            bool armedAction = armedSlot == slot && !armedIsDelete;
            bool armedDelete = armedSlot == slot && armedIsDelete;

            if (mode == Mode.Save)
            {
                string label = !exists ? "Save" : armedAction ? "Confirm?" : "Overwrite";
                Color color = armedAction ? MenuUI.ButtonRed : exists ? MenuUI.ButtonGray : MenuUI.ButtonGreen;
                MenuUI.SetButton(row.Action, label, color);
                row.Action.interactable = true;
            }
            else
            {
                MenuUI.SetButton(row.Action, armedAction ? "Confirm?" : "Load",
                    armedAction ? MenuUI.ButtonRed : MenuUI.ButtonGreen);
                row.Action.interactable = data != null;
            }

            row.Delete.gameObject.SetActive(exists);
            MenuUI.SetButton(row.Delete, armedDelete ? "Sure?" : "Delete", MenuUI.ButtonRed);
        }
    }

    private static string Describe(int slot, bool exists, SaveData d)
    {
        string name = "<b>" + SaveSystem.SlotName(slot) + "</b>";
        if (!exists) return name + "\n<color=#8a8a92>Empty</color>";
        if (d == null) return name + "\n<color=#e06060>Unreadable save file</color>";

        return name + "\n" + d.buildings.Count + " buildings"
               + "   |   " + d.completedTechs.Count + " techs researched"
               + "\n<color=#a8a8b0>Play time " + SaveSystem.FormatPlayTime(d.playTimeSeconds)
               + "   |   " + SaveSystem.FormatSavedAt(d.savedAtTicks) + "</color>";
    }
}
