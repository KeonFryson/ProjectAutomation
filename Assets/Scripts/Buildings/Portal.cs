using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The end-game building. Belts deliver the items listed in 'requirements' (any cell of its
/// footprint accepts them). Once every requirement is met the portal activates and the win
/// screen opens.
///
/// Progress belongs to this portal and is saved through BuildingSave:
///   inputItems / inputCounts = delivered items, crafting = portal is activated.
/// </summary>
public class Portal : FactoryBuilding
{
    public override bool HasOutput => false;

    [Tooltip("Everything that must be delivered to activate the portal.")]
    public List<ItemAmount> requirements = new List<ItemAmount>();

    [Tooltip("Seconds between accepted items at speedMultiplier 1.")]
    public float secondsPerItem = 0.25f;

    public bool IsActivated { get; private set; }

    private readonly Dictionary<ItemDefinition, int> delivered = new Dictionary<ItemDefinition, int>();
    private float nextAcceptTime;
    private SpriteRenderer glow;

    // ---------------------------------------------------------------
    // Progress
    // ---------------------------------------------------------------

    public int GetDelivered(ItemDefinition item)
    {
        if (item == null) return 0;
        delivered.TryGetValue(item, out int n);
        return n;
    }

    public float Progress01
    {
        get
        {
            if (IsActivated) return 1f;
            int total = 0, have = 0;
            foreach (var r in requirements)
            {
                if (r == null || r.item == null) continue;
                total += r.amount;
                have += Mathf.Min(r.amount, GetDelivered(r.item));
            }
            return total > 0 ? (float)have / total : 0f;
        }
    }

    private ItemAmount FindRequirement(ItemDefinition item)
    {
        foreach (var r in requirements)
            if (r != null && r.item == item) return r;
        return null;
    }

    private bool AllMet()
    {
        bool any = false;
        foreach (var r in requirements)
        {
            if (r == null || r.item == null) continue;
            any = true;
            if (GetDelivered(r.item) < r.amount) return false;
        }
        return any; // a portal with no requirements never activates by accident
    }

    // ---------------------------------------------------------------
    // Item flow
    // ---------------------------------------------------------------

    public override bool TryAcceptInput(ItemVisual item)
    {
        if (IsActivated || item == null || item.Definition == null) return false;
        if (Time.time < nextAcceptTime) return false;

        ItemDefinition def = item.Definition;
        ItemAmount req = FindRequirement(def);
        if (req == null) return false;

        int have = GetDelivered(def);
        if (have >= req.amount) return false; // this requirement is already full

        delivered[def] = have + 1;
        nextAcceptTime = Time.time + secondsPerItem / SpeedMultiplier;
        SellBurstEffect.Spawn(transform.position, def.color);
        item.Release();

        if (AllMet()) Activate();
        return true;
    }

    private void Activate()
    {
        if (IsActivated) return;
        IsActivated = true;
        WinScreen.Show(transform.position);
    }

    // ---------------------------------------------------------------
    // Visuals: a glowing core that grows with progress, then pulses
    // ---------------------------------------------------------------

    protected override void Update()
    {
        base.Update();
        UpdateGlow();
    }

    private void UpdateGlow()
    {
        if (GridManager.Instance == null) return;

        if (glow == null)
        {
            var go = new GameObject("PortalGlow");
            go.transform.SetParent(transform, false);
            glow = go.AddComponent<SpriteRenderer>();
            glow.sprite = SquareSpriteFactory.GetSquareSprite();
            glow.sortingOrder = 2;
        }

        Vector2Int dims = Footprint.WorldSize(Size, Facing);
        float cs = GridManager.Instance.cellSize;
        float p = Progress01;

        float k = IsActivated ? 0.8f + 0.1f * Mathf.Sin(Time.time * 4f) : Mathf.Lerp(0.15f, 0.7f, p);
        glow.transform.localScale = new Vector3(dims.x * cs * k, dims.y * cs * k, 1f);

        Color c = IsActivated
            ? Color.Lerp(new Color(0.2f, 1f, 0.95f), new Color(0.9f, 0.4f, 1f), 0.5f + 0.5f * Mathf.Sin(Time.time * 3f))
            : new Color(0.3f, 0.8f, 1f);
        c.a = IsActivated ? 0.95f : 0.35f + 0.4f * p;
        glow.color = c;
    }

    // ---------------------------------------------------------------
    // Save / load
    // ---------------------------------------------------------------

    public override void CaptureState(BuildingSave s)
    {
        base.CaptureState(s);
        s.crafting = IsActivated; // reused flag: "portal is activated"
        foreach (var kv in delivered)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            s.inputItems.Add(kv.Key.SaveKey);
            s.inputCounts.Add(kv.Value);
        }
    }

    public override void RestoreState(BuildingSave s, Func<string, ItemDefinition> findItem)
    {
        base.RestoreState(s, findItem);

        delivered.Clear();
        IsActivated = s.crafting; // restored silently: no second win screen

        if (s.inputItems == null || s.inputCounts == null) return;
        for (int i = 0; i < s.inputItems.Count && i < s.inputCounts.Count; i++)
        {
            ItemDefinition def = Resolve(s.inputItems[i], findItem);
            if (def != null && s.inputCounts[i] > 0) delivered[def] = s.inputCounts[i];
        }
    }

    private ItemDefinition Resolve(string key, Func<string, ItemDefinition> findItem)
    {
        foreach (var r in requirements)
            if (r != null && r.item != null && r.item.Matches(key)) return r.item;
        return findItem != null ? findItem(key) : null;
    }
}
