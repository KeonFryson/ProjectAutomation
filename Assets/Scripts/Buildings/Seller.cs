using UnityEngine;

/// <summary>
/// The end of every production line: accepts any item, sells it for its
/// ItemDefinition.sellValue, and spawns a small satisfying burst effect.
/// Has unlimited capacity, so it never blocks the belt feeding it.
/// </summary>
public class Seller : FactoryBuilding
{
    public override bool TryAcceptInput(ItemVisual item)
    {
        if (item == null || item.Definition == null) return false;

        // Read everything we need BEFORE releasing: Release() clears Definition.
        ItemDefinition def = item.Definition;
        int value = def.sellValue * (1 + (Level - 1)); // upgrades increase payout
        if (EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(value);

        SellBurstEffect.Spawn(transform.position, def.color);
        item.Release();
        return true;
    }
}