using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A generator machine: produces one item every productionInterval seconds
/// with no input required (e.g. a Miner pulling Ore out of the ground).
/// Never accepts input from other buildings.
///
/// The player picks what it mines from the inspector panel (click the miner).
/// The choices come from availableItems.
/// </summary>
public class Miner : FactoryBuilding
{
    [Tooltip("The raw item this miner currently produces (e.g. Iron Ore). Changed at runtime from the inspector UI.")]
    public ItemDefinition producedItem;

    [Tooltip("All items the player can choose from in this miner's menu (Iron Ore, Copper Ore, ...).")]
    public List<ItemDefinition> availableItems = new List<ItemDefinition>();

    [Tooltip("Seconds between each item produced, at level 1.")]
    public float productionInterval = 2f;

    private float timer;

    void Start()
    {
        // Make sure the current item is selectable, and pick a default if none set.
        if (producedItem != null && !availableItems.Contains(producedItem))
            availableItems.Insert(0, producedItem);
        if (producedItem == null && availableItems.Count > 0)
            producedItem = availableItems[0];
    }

    public void SetProducedItem(ItemDefinition item)
    {
        if (item == null || item == producedItem) return;
        producedItem = item;
        timer = 0f; // start the next item fresh
    }

    protected override void Update()
    {
        base.Update(); // moves/pushes any item currently in transit

        if (heldItem == null && producedItem != null)
        {
            timer += Time.deltaTime * (1f + (Level - 1) * 0.25f);
            if (timer >= productionInterval)
            {
                timer = 0f;
                heldItem = ItemVisual.Spawn(producedItem, transform.position);
                moveProgress = 0f;
            }
        }
    }

    public override bool TryAcceptInput(ItemVisual item)
    {
        return false; // miners only produce, never receive
    }
}