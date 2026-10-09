using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>An item plus how many of it (used by recipe inputs and outputs).</summary>
[Serializable]
public class ItemStack
{
    public ItemDefinition item;
    [Min(1)] public int amount = 1;

    public ItemStack() { }
    public ItemStack(ItemDefinition item, int amount) { this.item = item; this.amount = amount; }
}

/// <summary>
/// A recipe run by Processor machines (Smelter, Assembler, ...).
/// Takes any number of input items and produces any number of output items per craft:
///   2 Iron Plate + 1 Gear  ->  1 Circuit + 2 Scrap
/// Outputs leave the machine one at a time through its front.
/// Create via: Assets > Create > Box Factory > Recipe Definition
///
/// Old single-item recipes (inputItem / outputItem) still work: they are read as a
/// 1 -> 1 recipe, and Box Factory > Migrate Recipes copies them into the new lists.
/// </summary>
[CreateAssetMenu(fileName = "NewRecipe", menuName = "Box Factory/Recipe Definition")]
public class RecipeDefinition : ScriptableObject
{
    [Tooltip("Items consumed by one craft. The machine waits until it holds all of them.")]
    public List<ItemStack> inputs = new List<ItemStack>();

    [Tooltip("Items produced by one craft. They are sent out of the machine one by one.")]
    public List<ItemStack> outputs = new List<ItemStack>();

    [Tooltip("Seconds one craft takes at speed x1.")]
    public float processTime = 2f;

    // Legacy single-item fields. Kept (hidden) so old assets still load; see MigrateLegacy.
    [HideInInspector] public ItemDefinition inputItem;
    [HideInInspector] public ItemDefinition outputItem;

    private static readonly List<ItemStack> NoStacks = new List<ItemStack>();
    [NonSerialized] private List<ItemStack> legacyIn;
    [NonSerialized] private List<ItemStack> legacyOut;

    /// <summary>What one craft consumes (falls back to the legacy single input).</summary>
    public IReadOnlyList<ItemStack> Inputs =>
        inputs != null && inputs.Count > 0 ? inputs : Legacy(ref legacyIn, inputItem);

    /// <summary>What one craft produces (falls back to the legacy single output).</summary>
    public IReadOnlyList<ItemStack> Outputs =>
        outputs != null && outputs.Count > 0 ? outputs : Legacy(ref legacyOut, outputItem);

    private static IReadOnlyList<ItemStack> Legacy(ref List<ItemStack> cache, ItemDefinition item)
    {
        if (item == null) return NoStacks;
        if (cache == null || cache.Count != 1 || cache[0].item != item)
            cache = new List<ItemStack> { new ItemStack(item, 1) };
        return cache;
    }

    public ItemDefinition FirstInput => First(Inputs);
    public ItemDefinition FirstOutput => First(Outputs);

    private static ItemDefinition First(IReadOnlyList<ItemStack> list)
    {
        foreach (var s in list) if (s != null && s.item != null) return s.item;
        return null;
    }

    /// <summary>How many of this item one craft consumes (0 = not an input).</summary>
    public int GetInputAmount(ItemDefinition item)
    {
        return Sum(Inputs, item);
    }

    public bool UsesInput(ItemDefinition item) { return item != null && GetInputAmount(item) > 0; }
    public bool MakesOutput(ItemDefinition item) { return item != null && Sum(Outputs, item) > 0; }

    private static int Sum(IReadOnlyList<ItemStack> list, ItemDefinition item)
    {
        int total = 0;
        foreach (var s in list)
            if (s != null && s.item == item) total += Mathf.Max(1, s.amount);
        return total;
    }

    /// <summary>True if there is at least one input and one output and no empty slots.</summary>
    public bool IsValid
    {
        get
        {
            var ins = Inputs;
            var outs = Outputs;
            if (ins.Count == 0 || outs.Count == 0) return false;
            foreach (var s in ins) if (s == null || s.item == null) return false;
            foreach (var s in outs) if (s == null || s.item == null) return false;
            return true;
        }
    }

    /// <summary>"2 Iron Plate + 1 Gear -> 1 Circuit"</summary>
    public string Describe()
    {
        return Join(Inputs) + " -> " + Join(Outputs);
    }

    private static string Join(IReadOnlyList<ItemStack> list)
    {
        var sb = new StringBuilder();
        foreach (var s in list)
        {
            if (s == null || s.item == null) continue;
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append(Mathf.Max(1, s.amount)).Append(' ').Append(s.item.itemName);
        }
        return sb.Length > 0 ? sb.ToString() : "?";
    }

    // ---- Legacy migration (called by the editor tool) ----

    public bool NeedsMigration =>
        (inputItem != null && (inputs == null || inputs.Count == 0)) ||
        (outputItem != null && (outputs == null || outputs.Count == 0));

    /// <summary>Copies the old single input/output into the new lists and clears the old fields.</summary>
    public void MigrateLegacy()
    {
        if (inputs == null) inputs = new List<ItemStack>();
        if (outputs == null) outputs = new List<ItemStack>();

        if (inputItem != null && inputs.Count == 0)
        {
            inputs.Add(new ItemStack(inputItem, 1));
            inputItem = null;
        }
        if (outputItem != null && outputs.Count == 0)
        {
            outputs.Add(new ItemStack(outputItem, 1));
            outputItem = null;
        }
    }
}
