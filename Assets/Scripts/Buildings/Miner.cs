using UnityEngine;

/// <summary>
/// A generator machine: produces one item every productionInterval seconds
/// with no input required (e.g. a Miner pulling Ore out of the ground).
/// Never accepts input from other buildings.
/// </summary>
public class Miner : FactoryBuilding
{
    [Tooltip("The raw item this miner produces (e.g. Ore).")]
    public ItemDefinition producedItem;

    [Tooltip("Seconds between each item produced, at level 1.")]
    public float productionInterval = 2f;

    private float timer;

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
