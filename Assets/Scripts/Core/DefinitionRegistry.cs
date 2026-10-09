using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ID -> definition lookup for items and buildings. Built from everything the scene's
/// BuildManager and ResearchManager can reach (no manual wiring).
/// Old saves that stored asset names still resolve through a name fallback.
/// </summary>
public static class DefinitionRegistry
{
    private static readonly Dictionary<string, ItemDefinition> items = new Dictionary<string, ItemDefinition>();
    private static readonly Dictionary<string, BuildingDefinition> buildings = new Dictionary<string, BuildingDefinition>();
    private static bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { items.Clear(); buildings.Clear(); built = false; }

    public static ItemDefinition GetItem(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!built) Rebuild();
        items.TryGetValue(key, out var item);
        return item;
    }

    public static BuildingDefinition GetBuilding(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!built) Rebuild();
        buildings.TryGetValue(key, out var def);
        return def;
    }

    public static void Rebuild()
    {
        items.Clear();
        buildings.Clear();

        if (BuildManager.Instance != null)
        {
            foreach (var def in BuildManager.Instance.availableBuildings)
            {
                if (def == null) continue;
                Add(buildings, def);

                if (def.prefab is Miner m)
                {
                    foreach (var i in m.availableItems) Add(items, i);
                    Add(items, m.producedItem);
                }
                else if (def.prefab is Processor p)
                {
                    foreach (var r in p.GetAllRecipes())
                    {
                        foreach (var s in r.Inputs) if (s != null) Add(items, s.item);
                        foreach (var s in r.Outputs) if (s != null) Add(items, s.item);
                    }
                }
            }
        }

        if (ResearchManager.Instance != null)
        {
            foreach (var t in ResearchManager.Instance.allTechs)
            {
                if (t == null) continue;
                foreach (var c in t.cost) if (c != null) Add(items, c.item);
                foreach (var b in t.unlocks) Add(buildings, b);
            }
        }

        built = true;
    }

    private static void Add<T>(Dictionary<string, T> map, T def) where T : GameDefinition
    {
        if (def == null) return;

        string key = def.SaveKey;
        if (map.TryGetValue(key, out var existing) && existing != def)
            Debug.LogWarning("DefinitionRegistry: ID '" + key + "' is used by both '" + existing.name + "' and '" + def.name + "'.");
        map[key] = def;

        if (!map.ContainsKey(def.name)) map[def.name] = def; // legacy saves that stored asset names
    }
}
