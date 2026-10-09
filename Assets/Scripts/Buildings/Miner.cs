using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A generator machine: produces one item every productionInterval seconds
/// with no input required (e.g. a Miner pulling Ore out of the ground).
/// Never accepts input from other buildings.
///
/// The player picks what it mines from the machine window (click the miner).
/// The choices come from availableItems.
/// </summary>
public class Miner : FactoryBuilding
{
    [Tooltip("The raw item this miner currently produces (e.g. Iron Ore). Changed at runtime from the machine window.")]
    public ItemDefinition producedItem;

    [Tooltip("All items the player can choose from in this miner's window (Iron Ore, Copper Ore, ...).")]
    public List<ItemDefinition> availableItems = new List<ItemDefinition>();

    [Tooltip("Seconds between each item produced, at level 1.")]
    public float productionInterval = 2f;

    private float timer;

    /// <summary>0..1 progress toward the next item (used by the machine window).</summary>
    public float Progress01 =>
        productionInterval > 0f ? Mathf.Clamp01(timer / productionInterval) : 0f;

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
            timer += Time.deltaTime * SpeedMultiplier;
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

    // ---- Save / load ----

    public override void CaptureState(BuildingSave s)
    {
        base.CaptureState(s);
        if (producedItem != null) s.item = producedItem.SaveKey;
        s.timer = timer;
    }

    public override void RestoreState(BuildingSave s, Func<string, ItemDefinition> findItem)
    {
        base.RestoreState(s, findItem);

        if (!string.IsNullOrEmpty(s.item))
        {
            ItemDefinition it = null;
            foreach (var a in availableItems)
                if (a != null && a.Matches(s.item)) { it = a; break; }
            if (it == null) it = findItem(s.item);
            if (it != null) SetProducedItem(it);
        }

        timer = Mathf.Clamp(s.timer, 0f, productionInterval);
    }
}
