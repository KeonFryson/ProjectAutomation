using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A machine that converts items according to recipes (Smelter, Assembler, ...).
/// It can hold several recipes at once: when an item arrives, the processor
/// looks for the recipe whose inputItem matches and uses that one.
///   Smelter: IronOre -> IronBar, CopperOre -> CopperBar, ...
/// Items that match no recipe are rejected, so they stay blocked on the belt.
/// </summary>
public class Processor : FactoryBuilding
{
    [Tooltip("All recipes this machine can run. The input item decides which one is used.")]
    public List<RecipeDefinition> recipes = new List<RecipeDefinition>();

    [HideInInspector, Tooltip("Legacy single recipe field, still honoured so old prefabs keep working.")]
    public RecipeDefinition recipe;

    private bool hasInputBuffered;
    private float processTimer;
    private RecipeDefinition activeRecipe;

    // ---- Read-only state for the machine window ----

    /// <summary>The item currently inside the machine being processed (null if idle).</summary>
    public ItemDefinition InputItem =>
        hasInputBuffered && activeRecipe != null ? activeRecipe.inputItem : null;

    /// <summary>The recipe currently running (null if idle).</summary>
    public RecipeDefinition ActiveRecipe => hasInputBuffered ? activeRecipe : null;

    /// <summary>0..1 progress of the current recipe.</summary>
    public float Progress01 =>
        hasInputBuffered && activeRecipe != null && activeRecipe.processTime > 0f
            ? Mathf.Clamp01(processTimer / activeRecipe.processTime)
            : 0f;

    /// <summary>Finds the recipe that consumes the given item, or null.</summary>
    public RecipeDefinition FindRecipe(ItemDefinition input)
    {
        if (input == null) return null;

        foreach (var r in recipes)
            if (r != null && r.inputItem == input) return r;

        if (recipe != null && recipe.inputItem == input) return recipe;
        return null;
    }

    /// <summary>Every recipe this machine knows (used by the machine window).</summary>
    public IEnumerable<RecipeDefinition> GetAllRecipes()
    {
        foreach (var r in recipes)
            if (r != null) yield return r;

        if (recipe != null && !recipes.Contains(recipe)) yield return recipe;
    }

    public override bool TryAcceptInput(ItemVisual item)
    {
        if (item == null) return false;
        if (hasInputBuffered || heldItem != null) return false;

        RecipeDefinition match = FindRecipe(item.Definition);
        if (match == null || match.outputItem == null) return false;

        activeRecipe = match;
        hasInputBuffered = true;
        processTimer = 0f;
        item.Release(); // item disappears into the machine while it's processed
        return true;
    }

    protected override void Update()
    {
        base.Update(); // moves/pushes a finished product out, if any

        if (hasInputBuffered && heldItem == null && activeRecipe != null)
        {
            processTimer += Time.deltaTime * SpeedMultiplier;
            if (processTimer >= activeRecipe.processTime)
            {
                hasInputBuffered = false;
                heldItem = ItemVisual.Spawn(activeRecipe.outputItem, transform.position);
                moveProgress = 0f;
                activeRecipe = null;
            }
        }
    }

    // ---- Save / load ----

    public override void CaptureState(BuildingSave s)
    {
        base.CaptureState(s);
        if (hasInputBuffered && activeRecipe != null)
        {
            s.recipe = activeRecipe.inputItem != null ? activeRecipe.inputItem.SaveKey : activeRecipe.name;
            s.timer = processTimer;
        }
    }

    public override void RestoreState(BuildingSave s, Func<string, ItemDefinition> findItem)
    {
        base.RestoreState(s, findItem);

        if (string.IsNullOrEmpty(s.recipe)) return;
        foreach (var r in GetAllRecipes())
        {
            bool match = (r.inputItem != null && r.inputItem.Matches(s.recipe)) || r.name == s.recipe;
            if (!match) continue;
            activeRecipe = r;
            hasInputBuffered = true;
            processTimer = Mathf.Max(0f, s.timer);
            break;
        }
    }
}
