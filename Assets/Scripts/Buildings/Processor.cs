using UnityEngine;

/// <summary>
/// A machine that converts one item into another according to a Recipe
/// (e.g. Smelter: Ore -> Metal). Use the same component for any processing
/// step in the chain by swapping the assigned RecipeDefinition — you don't
/// need a separate script per machine type.
/// </summary>
public class Processor : FactoryBuilding
{
    public RecipeDefinition recipe;

    private bool hasInputBuffered;
    private float processTimer;

    public override bool TryAcceptInput(ItemVisual item)
    {
        if (hasInputBuffered || heldItem != null) return false;
        if (recipe == null || recipe.inputItem == null) return false;
        if (item.Definition != recipe.inputItem) return false;

        hasInputBuffered = true;
        processTimer = 0f;
        Destroy(item.gameObject); // item disappears into the machine while it's processed
        return true;
    }

    protected override void Update()
    {
        base.Update(); // moves/pushes a finished product out, if any

        if (hasInputBuffered && heldItem == null && recipe != null)
        {
            
            processTimer += Time.deltaTime * (1f + (Level - 1) * 0.25f);
            if (processTimer >= recipe.processTime)
            {
                hasInputBuffered = false;
                heldItem = ItemVisual.Spawn(recipe.outputItem, transform.position);
                moveProgress = 0f;
            }
        }
    }

   

}
