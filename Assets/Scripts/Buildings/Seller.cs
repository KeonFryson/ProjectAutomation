using UnityEngine;

/// <summary>
/// Accepts any item and sells it for its ItemDefinition.sellValue, scaled by
/// the building's sellMultiplier (better Sellers are unlocked in the tech tree).
/// Unlimited capacity, so it never blocks the belt feeding it.
/// </summary>
public class Seller : FactoryBuilding
{
    public override bool HasOutput => false;

    public override bool TryAcceptInput(ItemVisual item)
    {
        if (item == null || item.Definition == null) return false;

        // Read everything we need BEFORE releasing: Release() clears Definition.
        ItemDefinition def = item.Definition;
        float mult = Definition != null ? Definition.sellMultiplier : 1f;
        int value = Mathf.Max(1, Mathf.RoundToInt(def.sellValue * mult));
        if (EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(value);

        SellBurstEffect.Spawn(transform.position, def.color);
        item.Release();
        return true;
    }
}