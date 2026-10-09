using System;
using System.Collections.Generic;

/// <summary>Everything stored in one save file. Serialized with JsonUtility.</summary>
[Serializable]
public class SaveData
{
    public int version = 3;            // 3: money removed (a leftover "money" field in old saves is ignored)
    public long savedAtTicks;          // UTC ticks, used for "most recent" and display
    public float playTimeSeconds;

    public List<BuildingSave> buildings = new List<BuildingSave>();

    public List<string> completedTechs = new List<string>();
    public string currentTech;
    public List<TechProgressSave> techProgress = new List<TechProgressSave>();

    /// <summary>BuildingDefinition ID per hotbar slot ("" = empty). Empty list = not stored (old save).</summary>
    public List<string> hotbar = new List<string>();

    public bool hasCamera;
    public float cameraX, cameraY, cameraSize;
}

[Serializable]
public class BuildingSave
{
    public string definition;   // BuildingDefinition ID (e.g. "B02")
    public int x, y;            // anchor cell
    public int facing;          // Direction enum value

    // Item being carried across the building (belts, miners, finished products...)
    public string held;         // ItemDefinition ID ("" = none)
    public float progress;      // 0..1 position along the building

    // Miner: item being mined. Miner/Processor: production timer.
    public string item;  // Miner: ItemDefinition ID being mined
    public float timer;

    // Processor: recipe currently running ("" = idle)
    public string recipe; // Processor: input item ID of the running recipe ("" = idle)

    // Splitter
    public int nextOut;
    public List<string> laneItems = new List<string>(); // ItemDefinition IDs
    public List<float> laneProgress = new List<float>();
}

[Serializable]
public class TechProgressSave
{
    public string tech;         // TechDefinition asset name
    public List<ItemCountSave> items = new List<ItemCountSave>();
}

[Serializable]
public class ItemCountSave
{
    public string item;         // ItemDefinition ID
    public int count;
}
