using UnityEngine;

/// <summary>
/// Defines one kind of item that can flow through the factory (Ore, Metal, Gear, etc).
/// Create via: Assets > Create > Box Factory > Item Definition
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Box Factory/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    public string itemName = "Item";

    [Tooltip("Color of the small square used to represent this item in the world.")]
    public Color color = Color.white;

    [Tooltip("Money earned when this item is sold at a Seller building.")]
    public int sellValue = 1;
}
