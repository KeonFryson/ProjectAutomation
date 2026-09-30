using UnityEngine;

/// <summary>
/// One entry in the build menu: what prefab to spawn, what it costs, and
/// what color represents it (since the whole game is colored squares).
/// Create via: Assets > Create > Box Factory > Building Definition
/// </summary>
[CreateAssetMenu(fileName = "NewBuilding", menuName = "Box Factory/Building Definition")]
public class BuildingDefinition : ScriptableObject
{
    public string displayName = "Building";

    [Tooltip("Tab this building appears under in the build menu (e.g. Logistics, Production). Tabs are hidden if every building shares one category.")]
    public string category = "Buildings";

    [Tooltip("Prefab must have a FactoryBuilding-derived component (Miner, ConveyorBelt, Processor, Seller).")]
    public FactoryBuilding prefab;

    public int buildCost = 10;

    [Tooltip("Cost to upgrade this building from level N to N+1 is baseUpgradeCost * N.")]
    public int baseUpgradeCost = 15;

    [Tooltip("Color of the building's square in the world and on its build menu icon.")]
    public Color iconColor = Color.gray;
}