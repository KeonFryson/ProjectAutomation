using UnityEngine;

/// <summary>
/// Defines a single-input, single-output recipe used by Processor machines (Smelter, Assembler, etc).
/// Create via: Assets > Create > Box Factory > Recipe Definition
/// </summary>
[CreateAssetMenu(fileName = "NewRecipe", menuName = "Box Factory/Recipe Definition")]
public class RecipeDefinition : ScriptableObject
{
    public ItemDefinition inputItem;
    public ItemDefinition outputItem;

    [Tooltip("Seconds it takes to turn one input item into one output item, at level 1.")]
    public float processTime = 2f;
}
