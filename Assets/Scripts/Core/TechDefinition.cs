using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ItemAmount
{
    public ItemDefinition item;
    [Min(1)] public int amount = 10;
}

/// <summary>
/// One node in the tech tree. Researching it (by delivering its item cost to a
/// Research Lab) unlocks the listed buildings.
/// Create via: Assets > Create > Box Factory > Tech Definition
/// </summary>
[CreateAssetMenu(fileName = "NewTech", menuName = "Box Factory/Tech Definition")]
public class TechDefinition : ScriptableObject
{
    public string displayName = "Technology";

    [TextArea] public string description;

    [Tooltip("Techs that must be finished before this one can be researched.")]
    public List<TechDefinition> prerequisites = new List<TechDefinition>();

    [Tooltip("Items that must be delivered to a Research Lab.")]
    public List<ItemAmount> cost = new List<ItemAmount>();

    [Tooltip("Buildings that become available once this tech is researched.")]
    public List<BuildingDefinition> unlocks = new List<BuildingDefinition>();
}
