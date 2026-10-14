/// <summary>
/// What "copy settings" puts on the clipboard. Each building type that supports it
/// fills in the fields it needs (FactoryBuilding.CopySettings) and reads them back
/// in FactoryBuilding.PasteSettings. 'kind' stops a Miner's settings being pasted onto a Processor.
/// Put this file in Assets/Scripts/Buildings/.
/// </summary>
public class BuildingSettings
{
    public string kind;               // "Miner", "Processor", ...
    public ItemDefinition item;       // Miner: item being mined
    public RecipeDefinition recipe;   // Processor: chosen recipe (null = automatic)

    public string Describe()
    {
        if (recipe != null) return recipe.Describe();
        if (item != null) return item.itemName;
        return kind == "Processor" ? "automatic recipe" : "settings";
    }
}
