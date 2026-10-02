using UnityEngine;

/// <summary>
/// Like a Seller, but instead of money the items go into the current research.
/// It only accepts items the active tech still needs, so everything else stays
/// blocked on the belt. Higher-tier labs (bigger speedMultiplier) consume faster.
/// </summary>
public class ResearchLab : FactoryBuilding
{
    public override bool HasOutput => false;

    [Tooltip("Seconds between accepted items at speedMultiplier 1.")]
    public float secondsPerItem = 0.5f;

    private float nextAcceptTime;

    public override bool TryAcceptInput(ItemVisual item)
    {
        if (item == null || item.Definition == null) return false;
        if (Time.time < nextAcceptTime) return false;

        var research = ResearchManager.Instance;
        if (research == null) return false;

        ItemDefinition def = item.Definition;
        if (!research.TryContribute(def)) return false;

        nextAcceptTime = Time.time + secondsPerItem / SpeedMultiplier;
        SellBurstEffect.Spawn(transform.position, def.color);
        item.Release();
        return true;
    }
}
