using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the tech tree state: which techs are finished, which one is being
/// researched, and how many of each item have been delivered.
/// Attach to a single empty GameObject named "ResearchManager" and fill in allTechs.
/// </summary>
public class ResearchManager : MonoBehaviour
{
    public static ResearchManager Instance { get; private set; }

    [Tooltip("Every tech shown in the tech tree window.")]
    public List<TechDefinition> allTechs = new List<TechDefinition>();

    public TechDefinition Current { get; private set; }

    /// <summary>Fired when progress changes or the current tech changes.</summary>
    public event Action OnResearchChanged;
    /// <summary>Fired when a tech finishes (buildings may have been unlocked).</summary>
    public event Action OnTechCompleted;

    private readonly HashSet<TechDefinition> completed = new HashSet<TechDefinition>();
    private readonly HashSet<BuildingDefinition> unlocked = new HashSet<BuildingDefinition>();
    private readonly Dictionary<TechDefinition, Dictionary<ItemDefinition, int>> delivered =
        new Dictionary<TechDefinition, Dictionary<ItemDefinition, int>>();

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>If there is no ResearchManager in the scene, everything counts as unlocked.</summary>
    public static bool IsBuildingUnlocked(BuildingDefinition def)
    {
        if (def == null) return false;
        if (Instance == null) return true;
        return def.unlockedByDefault || Instance.unlocked.Contains(def);
    }

    public bool IsCompleted(TechDefinition t) => t != null && completed.Contains(t);

    public bool PrerequisitesMet(TechDefinition t)
    {
        foreach (var p in t.prerequisites)
            if (p != null && !completed.Contains(p)) return false;
        return true;
    }

    public bool CanStart(TechDefinition t)
    {
        return t != null && !completed.Contains(t) && PrerequisitesMet(t);
    }

    public void StartResearch(TechDefinition t)
    {
        if (!CanStart(t)) return;
        Current = t;
        if (!CheckComplete(t)) OnResearchChanged?.Invoke();
    }

    public void CancelResearch()
    {
        if (Current == null) return;
        Current = null; // delivered items are kept if the tech is selected again
        OnResearchChanged?.Invoke();
    }

    public int GetDelivered(TechDefinition t, ItemDefinition item)
    {
        if (t != null && item != null && delivered.TryGetValue(t, out var d) && d.TryGetValue(item, out int n))
            return n;
        return 0;
    }

    public float Progress01(TechDefinition t)
    {
        if (t == null) return 0f;
        int total = 0, have = 0;
        foreach (var c in t.cost)
        {
            if (c == null || c.item == null) continue;
            total += c.amount;
            have += Mathf.Min(c.amount, GetDelivered(t, c.item));
        }
        return total > 0 ? (float)have / total : 0f;
    }

    /// <summary>Called by ResearchLab. Returns true if the current tech still needed this item.</summary>
    public bool TryContribute(ItemDefinition item)
    {
        if (Current == null || item == null) return false;

        ItemAmount need = null;
        foreach (var c in Current.cost)
            if (c != null && c.item == item) { need = c; break; }
        if (need == null) return false;

        if (!delivered.TryGetValue(Current, out var d))
        {
            d = new Dictionary<ItemDefinition, int>();
            delivered[Current] = d;
        }

        d.TryGetValue(item, out int have);
        if (have >= need.amount) return false;

        d[item] = have + 1;
        if (!CheckComplete(Current)) OnResearchChanged?.Invoke();
        return true;
    }

    private bool CheckComplete(TechDefinition t)
    {
        foreach (var c in t.cost)
        {
            if (c == null || c.item == null) continue;
            if (GetDelivered(t, c.item) < c.amount) return false;
        }

        completed.Add(t);
        foreach (var b in t.unlocks)
            if (b != null) unlocked.Add(b);

        if (Current == t) Current = null;
        OnTechCompleted?.Invoke();
        OnResearchChanged?.Invoke();
        return true;
    }
}
