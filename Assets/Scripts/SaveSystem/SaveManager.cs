using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Captures / restores the game state and runs autosave.
/// Created automatically by GameBootstrap (or add it to the scene yourself).
/// Buildings and items are saved by their ID (B01, I01, ...), see GameDefinition.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Tooltip("Write the Autosave slot on a timer.")]
    public bool autosaveEnabled = true;
    [Min(10f), Tooltip("Seconds of play time between autosaves.")]
    public float autosaveIntervalSeconds = 120f;
    [Tooltip("Also autosave when the application closes.")]
    public bool autosaveOnQuit = true;

    public float PlayTime { get; private set; }

    private bool ready;
    private float autosaveTimer;

    void Awake()
    {
        Instance = this;
        if (GetComponent<PauseMenu>() == null) gameObject.AddComponent<PauseMenu>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    IEnumerator Start()
    {
        yield return null; // let every other Start() (UI etc.) finish first

        int pending = SaveSystem.PendingLoadSlot;
        SaveSystem.PendingLoadSlot = SaveSystem.NoSlot;
        if (pending != SaveSystem.NoSlot) LoadFromSlot(pending);

        ready = true;
    }

    void Update()
    {
        if (!ready) return;
        PlayTime += Time.deltaTime; // stops while paused (timeScale 0)

        if (!autosaveEnabled) return;
        autosaveTimer += Time.deltaTime;
        if (autosaveTimer >= autosaveIntervalSeconds) Autosave(true);
    }

    void OnApplicationQuit()
    {
        if (ready && autosaveOnQuit) Autosave(false);
    }

    // ---------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------

    public bool SaveToSlot(int slot)
    {
        SaveData data = Capture();
        bool ok = data != null && SaveSystem.Write(slot, data);
        GameToast.Show(ok ? "Saved to " + SaveSystem.SlotName(slot) : "Save failed!");
        return ok;
    }

    /// <summary>Writes the Autosave slot. Skipped when there is nothing built yet.</summary>
    public void Autosave(bool showToast)
    {
        autosaveTimer = 0f;
        SaveData data = Capture();
        if (data == null || data.buildings.Count == 0) return;
        if (SaveSystem.Write(SaveSystem.AutosaveSlot, data) && showToast)
            GameToast.Show("Autosaved");
    }

    public bool LoadFromSlot(int slot)
    {
        SaveData data = SaveSystem.Read(slot);
        if (data == null)
        {
            GameToast.Show("Could not load " + SaveSystem.SlotName(slot));
            return false;
        }

        Apply(data);
        GameToast.Show("Loaded " + SaveSystem.SlotName(slot));
        return true;
    }

    public void ReturnToMainMenu()
    {
        if (ready) Autosave(false);
        ready = false;       // no autosave of the emptied world on quit
        ClearWorld();        // releases pooled item visuals (they survive scene loads)
        Time.timeScale = 1f;
        SceneManager.LoadScene(SaveSystem.MenuScene);
    }

    // ---------------------------------------------------------------
    // Capture
    // ---------------------------------------------------------------

    private SaveData Capture()
    {
        if (GridManager.Instance == null) return null;

        var d = new SaveData
        {
            savedAtTicks = System.DateTime.UtcNow.Ticks,
            playTimeSeconds = PlayTime
        };

        foreach (FactoryBuilding b in GridManager.Instance.GetAllBuildings())
        {
            if (b == null || b.Definition == null) continue;
            var bs = new BuildingSave
            {
                definition = b.Definition.SaveKey, // building ID, e.g. "B02"
                x = b.GridPosition.x,
                y = b.GridPosition.y,
                facing = (int)b.Facing
            };
            b.CaptureState(bs); // held item, miner/processor/splitter state
            d.buildings.Add(bs);
        }

        ResearchManager rm = ResearchManager.Instance;
        if (rm != null)
        {
            foreach (TechDefinition t in rm.CompletedTechs)
                if (t != null) d.completedTechs.Add(t.name);

            if (rm.Current != null) d.currentTech = rm.Current.name;

            foreach (var kv in rm.AllDelivered)
            {
                if (kv.Key == null) continue;
                var tp = new TechProgressSave { tech = kv.Key.name };
                foreach (var ic in kv.Value)
                    if (ic.Key != null && ic.Value > 0)
                        tp.items.Add(new ItemCountSave { item = ic.Key.SaveKey, count = ic.Value });
                if (tp.items.Count > 0) d.techProgress.Add(tp);
            }
        }

        if (UIManager.Instance != null)
            d.hotbar.AddRange(UIManager.Instance.GetHotbarIds());

        var cam = FindFirstObjectByType<TopDownCameraController>();
        if (cam != null)
        {
            Vector2 p = cam.PlanePosition;
            d.hasCamera = true;
            d.cameraX = p.x;
            d.cameraY = p.y;
            d.cameraSize = cam.OrthoSize;
        }

        return d;
    }

    // ---------------------------------------------------------------
    // Apply
    // ---------------------------------------------------------------

    private void ClearWorld()
    {
        if (GridManager.Instance == null) return;
        foreach (FactoryBuilding b in GridManager.Instance.GetAllBuildings())
            if (b != null) b.Demolish();
    }

    private void Apply(SaveData d)
    {
        if (d.version > 3) Debug.LogWarning("SaveManager: save was made by a newer version of the game.");

        DefinitionRegistry.Rebuild();

        if (BuildManager.Instance != null) BuildManager.Instance.CancelPlacement();
        ClearWorld();

        ApplyResearch(d);   // before the hotbar: locked buildings are dropped from it
        ApplyBuildings(d);

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideInspector();
            if (d.hotbar != null && d.hotbar.Count > 0) UIManager.Instance.SetHotbarFromIds(d.hotbar);
        }

        if (d.hasCamera)
        {
            var cam = FindFirstObjectByType<TopDownCameraController>();
            if (cam != null) cam.SetView(new Vector2(d.cameraX, d.cameraY), d.cameraSize);
        }

        PlayTime = d.playTimeSeconds;
        autosaveTimer = 0f;
    }

    private void ApplyResearch(SaveData d)
    {
        ResearchManager rm = ResearchManager.Instance;
        if (rm == null) return;

        var techs = new Dictionary<string, TechDefinition>();
        foreach (var t in rm.allTechs) if (t != null) techs[t.name] = t;

        rm.ResetState();

        foreach (string name in d.completedTechs)
            if (techs.TryGetValue(name, out TechDefinition t)) rm.RestoreCompleted(t);

        foreach (TechProgressSave tp in d.techProgress)
        {
            if (!techs.TryGetValue(tp.tech, out TechDefinition t)) continue;
            foreach (ItemCountSave ic in tp.items)
            {
                ItemDefinition item = DefinitionRegistry.GetItem(ic.item);
                if (item != null) rm.RestoreDelivered(t, item, ic.count);
            }
        }

        if (!string.IsNullOrEmpty(d.currentTech) &&
            techs.TryGetValue(d.currentTech, out TechDefinition cur) && !rm.IsCompleted(cur))
            rm.RestoreCurrent(cur);

        rm.NotifyRestored();
    }

    private void ApplyBuildings(SaveData d)
    {
        var grid = GridManager.Instance;
        if (grid == null || BuildManager.Instance == null) return;

        var cells = new List<Vector2Int>();
        foreach (BuildingSave bs in d.buildings)
        {
            BuildingDefinition def = DefinitionRegistry.GetBuilding(bs.definition);
            if (def == null || def.prefab == null)
            {
                Debug.LogWarning("SaveManager: unknown building '" + bs.definition + "' skipped (e.g. the removed Seller).");
                continue;
            }

            Vector2Int size = Footprint.ClampSize(def.size);
            Vector2Int anchor = new Vector2Int(bs.x, bs.y);
            Direction facing = (Direction)Mathf.Clamp(bs.facing, 0, 3);

            Footprint.GetCells(anchor, size, facing, cells);
            bool blocked = false;
            foreach (var c in cells) if (grid.IsOccupied(c)) { blocked = true; break; }
            if (blocked) continue;

            FactoryBuilding instance = Instantiate(def.prefab);
            instance.Initialize(anchor, facing, def);
            instance.RestoreState(bs, DefinitionRegistry.GetItem); // items in transit, machine progress
        }
    }
}
