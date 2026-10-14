using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A machine that converts items according to recipes (Smelter, Assembler, Refinery, ...).
///
///  - The player can pick a recipe in the machine window (SelectedRecipe). A machine with a
///    selected recipe only accepts that recipe's inputs.
///  - With no selection ("Auto") the first item that arrives picks the recipe (the first
///    recipe that uses it), like before.
///  - A recipe can need several different input items (and several of each) and can
///    produce several different output items.
///  - When every input is present the machine consumes them, works for processTime,
///    then queues the outputs. Outputs leave through the front one at a time.
/// </summary>
public class Processor : FactoryBuilding
{
    [Tooltip("All recipes this machine can run. The player can choose one in the machine window.")]
    public List<RecipeDefinition> recipes = new List<RecipeDefinition>();

    [HideInInspector, Tooltip("Legacy single recipe field, still honoured so old prefabs keep working.")]
    public RecipeDefinition recipe;

    [Min(1), Tooltip("How many crafts' worth of each input the machine can hold while it works. 2 keeps it busy without a gap between crafts.")]
    public int inputBufferBatches = 2;

    private readonly Dictionary<ItemDefinition, int> buffer = new Dictionary<ItemDefinition, int>();
    private readonly List<ItemDefinition> pendingOutputs = new List<ItemDefinition>();
    private RecipeDefinition activeRecipe;     // recipe currently in use (auto-picked or selected)
    private RecipeDefinition selectedRecipe;   // recipe chosen by the player (null = auto)
    private bool crafting;
    private float processTimer;

    // ---- Read-only state for the machine window ----

    /// <summary>The recipe this machine is currently locked to by incoming items (null when idle).</summary>
    public RecipeDefinition ActiveRecipe => activeRecipe;

    /// <summary>The recipe the player chose (null = automatic).</summary>
    public RecipeDefinition SelectedRecipe => selectedRecipe;

    /// <summary>What the window should show: the active recipe, else the selected one.</summary>
    public RecipeDefinition DisplayRecipe => activeRecipe != null ? activeRecipe : selectedRecipe;

    /// <summary>True while the craft timer is running.</summary>
    public bool IsCrafting => crafting;

    /// <summary>0..1 progress of the current craft.</summary>
    public float Progress01 =>
        crafting && activeRecipe != null && activeRecipe.processTime > 0f
            ? Mathf.Clamp01(processTimer / activeRecipe.processTime)
            : 0f;

    /// <summary>How many of this item are waiting in the input buffer.</summary>
    public int GetBuffered(ItemDefinition item)
    {
        if (item == null) return 0;
        buffer.TryGetValue(item, out int n);
        return n;
    }

    /// <summary>Most of this item the buffer will take for the given recipe.</summary>
    public int GetCapacity(RecipeDefinition r, ItemDefinition item)
    {
        if (r == null || item == null) return 0;
        return r.GetInputAmount(item) * Mathf.Max(1, inputBufferBatches);
    }

    /// <summary>Finished items still waiting to leave the machine.</summary>
    public int PendingOutputCount => pendingOutputs.Count;

    /// <summary>Finds the recipe that consumes the given item, or null.</summary>
    public RecipeDefinition FindRecipe(ItemDefinition input)
    {
        if (input == null) return null;

        foreach (var r in recipes)
            if (r != null && r.UsesInput(input)) return r;

        if (recipe != null && recipe.UsesInput(input)) return recipe;
        return null;
    }

    /// <summary>Every recipe this machine knows (used by the machine window).</summary>
    public IEnumerable<RecipeDefinition> GetAllRecipes()
    {
        foreach (var r in recipes)
            if (r != null) yield return r;

        if (recipe != null && !recipes.Contains(recipe)) yield return recipe;
    }

    public bool CanRun(RecipeDefinition r)
    {
        if (r == null) return false;
        foreach (var x in GetAllRecipes()) if (x == r) return true;
        return false;
    }

    // ---------------------------------------------------------------
    // Recipe selection
    // ---------------------------------------------------------------

    /// <summary>
    /// Player picks a recipe (null = automatic). Changing the recipe empties the input
    /// buffer and cancels the current craft; the item already leaving the machine is kept.
    /// </summary>
    public void SetSelectedRecipe(RecipeDefinition r)
    {
        if (r == selectedRecipe) return;
        if (r != null && !CanRun(r)) return;

        selectedRecipe = r;
        ResetWork();
    }

    private void ResetWork()
    {
        buffer.Clear();
        pendingOutputs.Clear();
        activeRecipe = null;
        crafting = false;
        processTimer = 0f;
    }

    // ---------------------------------------------------------------
    // Copy / paste settings
    // ---------------------------------------------------------------

    public override bool CopySettings(BuildingSettings clip)
    {
        clip.kind = "Processor";
        clip.recipe = DisplayRecipe;
        return true;
    }

    public override bool PasteSettings(BuildingSettings clip)
    {
        if (clip == null || clip.kind != "Processor") return false;
        if (clip.recipe != null && !CanRun(clip.recipe)) return false; // this machine can't make it
        SetSelectedRecipe(clip.recipe);
        return true;
    }

    // ---------------------------------------------------------------
    // Item flow
    // ---------------------------------------------------------------

    public override bool TryAcceptInput(ItemVisual item)
    {
        if (item == null || item.Definition == null) return false;
        ItemDefinition def = item.Definition;

        RecipeDefinition r = activeRecipe;
        if (r == null)
        {
            r = selectedRecipe != null ? selectedRecipe : FindRecipe(def);
            if (r == null || !r.IsValid) return false;
        }

        if (!r.UsesInput(def)) return false; // not an input of the (selected/locked) recipe

        int have = GetBuffered(def);
        if (have >= GetCapacity(r, def)) return false; // that input is full

        activeRecipe = r;
        buffer[def] = have + 1;
        item.Release(); // the item disappears into the machine
        return true;
    }

    private bool HasAllInputs(RecipeDefinition r)
    {
        foreach (var s in r.Inputs)
        {
            if (s == null || s.item == null) return false;
            if (GetBuffered(s.item) < r.GetInputAmount(s.item)) return false;
        }
        return true;
    }

    private bool BufferIsEmpty()
    {
        foreach (var kv in buffer) if (kv.Value > 0) return false;
        return true;
    }

    private void StartCraft()
    {
        foreach (var s in activeRecipe.Inputs)
        {
            if (s == null || s.item == null) continue;
            int left = GetBuffered(s.item) - Mathf.Max(1, s.amount);
            if (left > 0) buffer[s.item] = left; else buffer.Remove(s.item);
        }
        crafting = true;
        processTimer = 0f;
    }

    private void FinishCraft()
    {
        if (activeRecipe != null)
        {
            foreach (var s in activeRecipe.Outputs)
            {
                if (s == null || s.item == null) continue;
                for (int i = 0; i < Mathf.Max(1, s.amount); i++) pendingOutputs.Add(s.item);
            }
        }
        crafting = false;
        processTimer = 0f;
    }

    protected override void Update()
    {
        base.Update(); // moves/pushes the item currently leaving the machine

        // Send the next finished item out once the front is free.
        if (heldItem == null && pendingOutputs.Count > 0)
        {
            ItemDefinition next = pendingOutputs[0];
            pendingOutputs.RemoveAt(0);
            heldItem = ItemVisual.Spawn(next, transform.position);
            moveProgress = 0f;
        }

        if (crafting)
        {
            processTimer += Time.deltaTime * SpeedMultiplier;
            if (activeRecipe == null || processTimer >= activeRecipe.processTime) FinishCraft();
        }
        else if (activeRecipe != null)
        {
            if (heldItem == null && pendingOutputs.Count == 0 && HasAllInputs(activeRecipe)) StartCraft();
            else if (BufferIsEmpty() && pendingOutputs.Count == 0) activeRecipe = null; // free to pick again
        }
    }

    // ---------------------------------------------------------------
    // Save / load
    // ---------------------------------------------------------------

    public override void CaptureState(BuildingSave s)
    {
        base.CaptureState(s);

        s.procVersion = 1;
        s.crafting = crafting;
        s.timer = processTimer;
        if (activeRecipe != null) s.recipe = activeRecipe.name;
        if (selectedRecipe != null) s.selectedRecipe = selectedRecipe.name;

        foreach (var kv in buffer)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            s.inputItems.Add(kv.Key.SaveKey);
            s.inputCounts.Add(kv.Value);
        }
        foreach (var p in pendingOutputs)
            if (p != null) s.outputItems.Add(p.SaveKey);
    }

    public override void RestoreState(BuildingSave s, Func<string, ItemDefinition> findItem)
    {
        base.RestoreState(s, findItem);

        buffer.Clear();
        pendingOutputs.Clear();
        activeRecipe = null;
        selectedRecipe = null;
        crafting = false;
        processTimer = 0f;

        if (!string.IsNullOrEmpty(s.selectedRecipe))
        {
            foreach (var rec in GetAllRecipes())
                if (rec.name == s.selectedRecipe) { selectedRecipe = rec; break; }
        }

        RecipeDefinition r = null;
        if (!string.IsNullOrEmpty(s.recipe))
        {
            foreach (var rec in GetAllRecipes())
            {
                ItemDefinition first = rec.FirstInput;
                // New saves store the recipe name; old saves stored the input item ID.
                if (rec.name == s.recipe || (first != null && first.Matches(s.recipe))) { r = rec; break; }
            }
        }

        if (s.procVersion >= 1)
        {
            activeRecipe = r;
            crafting = r != null && s.crafting;
            processTimer = crafting ? Mathf.Max(0f, s.timer) : 0f;

            if (s.inputItems != null && s.inputCounts != null)
            {
                for (int i = 0; i < s.inputItems.Count && i < s.inputCounts.Count; i++)
                {
                    ItemDefinition def = findItem(s.inputItems[i]);
                    if (def != null && s.inputCounts[i] > 0) buffer[def] = s.inputCounts[i];
                }
            }
            if (s.outputItems != null)
            {
                foreach (string id in s.outputItems)
                {
                    ItemDefinition def = findItem(id);
                    if (def != null) pendingOutputs.Add(def);
                }
            }
        }
        else if (r != null)
        {
            // Old save: a recipe was running with its input already consumed.
            activeRecipe = r;
            crafting = true;
            processTimer = Mathf.Max(0f, s.timer);
        }
    }
}