using UnityEngine;

/// <summary>
/// One entry in the build menu. Upgrades no longer exist: a "better" building
/// is simply another BuildingDefinition (e.g. Miner Mk2) with a higher
/// speedMultiplier that the player unlocks through the tech tree.
/// Create via: Assets > Create > Box Factory > Building Definition
/// </summary>
[CreateAssetMenu(fileName = "NewBuilding", menuName = "Box Factory/Building Definition")]
public class BuildingDefinition : ScriptableObject
{
    public string displayName = "Building";

    [TextArea, Tooltip("Shown in the build menu tooltip.")]
    public string description;

    [Tooltip("Tab this building appears under in the build menu. Tabs are hidden if every unlocked building shares one category.")]
    public string category = "Buildings";

    [Tooltip("Prefab must have a FactoryBuilding-derived component (Miner, ConveyorBelt, Processor, Seller, ResearchLab).")]
    public FactoryBuilding prefab;

    public int buildCost = 10;

    [Tooltip("Footprint in grid cells: X = length along the facing direction, Y = width across it. (1,1) is a normal single tile.")]
    public Vector2Int size = Vector2Int.one;

    [Tooltip("Multiplies this building's speed: belt travel speed, miner/processor output rate, lab research rate. Higher tiers use bigger numbers.")]
    public float speedMultiplier = 1f;

    [Tooltip("Sellers only: multiplies the money earned per item.")]
    public float sellMultiplier = 1f;

    [Tooltip("Tick for starter buildings. Everything else must be unlocked by a TechDefinition.")]
    public bool unlockedByDefault = true;

    [Tooltip("Color of the building's square in the world and on its build menu icon.")]
    public Color iconColor = Color.gray;
}