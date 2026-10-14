using System;
using UnityEngine;

/// <summary>
/// Factorio-style inserter: 1x1, takes an item from the building BEHIND it and drops it into
/// the building IN FRONT of it, swinging an arm between the two.
///
///  - Only picks up an item if the target would actually accept it (right recipe input, belt tile free,
///    lab needs it ...), so it never grabs something it cannot deliver.
///  - Picks from any building that has an output slot (belt, miner, machine). It does not need a belt
///    pointing at it, and it never receives items pushed by belts.
///  - If the target is full when the arm arrives, it waits holding the item.
///  - Speed: swingSpeed * BuildingDefinition.speedMultiplier half-swings per second
///    (2.5 -> one full pick-and-drop cycle every 0.8 s, like the basic Factorio inserter).
///
/// Create a BuildingDefinition with prefab type "Inserter" (Content Editor), size (1,1).
/// </summary>
public class Inserter : FactoryBuilding
{
    public override bool HasInput => false;   // belts never push into it
    public override bool HasOutput => false;  // it moves items itself; belts don't draw corners for it

    [Tooltip("Half-swings per second at speedMultiplier 1.")]
    public float swingSpeed = 2.5f;

    private enum State { WaitPickup, Forward, WaitDrop, Back }

    private State state = State.WaitPickup;
    private float t;                 // 0 = arm over the source, 1 = arm over the target
    private ItemVisual carried;
    private Transform pivot;

    private Vector2Int F => DirectionUtil.ToVector(Facing);
    private FactoryBuilding Source => GridManager.Instance.GetBuilding(GridPosition - F);
    private FactoryBuilding Target => GridManager.Instance.GetBuilding(GridPosition + F);

    public override bool TryAcceptInput(ItemVisual item) { return false; }

    // ---------------------------------------------------------------
    // Logic
    // ---------------------------------------------------------------

    protected override void Update()
    {
        base.Update(); // demolish overlay
        if (GridManager.Instance == null) return;

        EnsureArm();
        float step = swingSpeed * SpeedMultiplier * Time.deltaTime;

        switch (state)
        {
            case State.WaitPickup:
                TryPickup();
                break;

            case State.Forward:
                t = Mathf.Min(1f, t + step);
                if (t >= 1f) state = State.WaitDrop;
                break;

            case State.WaitDrop:
                TryDrop();
                break;

            case State.Back:
                t = Mathf.Max(0f, t - step);
                if (t <= 0f) state = State.WaitPickup;
                break;
        }

        UpdateArm();
    }

    private void TryPickup()
    {
        FactoryBuilding src = Source;
        FactoryBuilding dst = Target;
        if (src == null || dst == null || src == dst || !src.HasOutput) return;

        ItemDefinition def = src.HeldItemDefinition;
        if (def == null) return;
        if (!dst.AcceptsFrom(this) || !dst.WouldAccept(def)) return;

        carried = src.TakeHeldItem();
        if (carried == null) return;

        t = 0f;
        state = State.Forward;
    }

    private void TryDrop()
    {
        if (carried == null) { state = State.Back; return; }

        FactoryBuilding dst = Target;
        if (dst == null || !dst.AcceptsFrom(this)) return; // wait with the item in hand

        carried.transform.position = HandPos(1f); // receivers (splitter) read the item's position
        if (dst.TryAcceptInput(carried))
        {
            carried = null;
            state = State.Back;
        }
    }

    // ---------------------------------------------------------------
    // Arm visuals
    // ---------------------------------------------------------------

    private float AngleDeg(float tt)
    {
        // From "behind" (facing + 180) over the left side to "front" (facing).
        return DirectionUtil.ToAngle(Facing) + 180f * (1f - tt);
    }

    private Vector3 HandPos(float tt)
    {
        float a = AngleDeg(tt) * Mathf.Deg2Rad;
        float r = 0.5f * GridManager.Instance.cellSize;
        return transform.position + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r;
    }

    private void EnsureArm()
    {
        if (pivot != null) return;

        float cs = GridManager.Instance.cellSize;
        Sprite square = SquareSpriteFactory.GetSquareSprite();

        var p = new GameObject("InserterArm");
        p.transform.SetParent(transform, false);
        pivot = p.transform;

        var arm = new GameObject("Arm");
        arm.transform.SetParent(pivot, false);
        arm.transform.localPosition = new Vector3(0.25f * cs, 0f, 0f);
        arm.transform.localScale = new Vector3(0.5f * cs, 0.12f * cs, 1f);
        var armSr = arm.AddComponent<SpriteRenderer>();
        armSr.sprite = square;
        armSr.color = new Color(1f, 0.85f, 0.2f, 1f);
        armSr.sortingOrder = 4;

        var hub = new GameObject("Hub");
        hub.transform.SetParent(transform, false);
        hub.transform.localScale = new Vector3(0.24f * cs, 0.24f * cs, 1f);
        var hubSr = hub.AddComponent<SpriteRenderer>();
        hubSr.sprite = square;
        hubSr.color = new Color(0.25f, 0.25f, 0.25f, 1f);
        hubSr.sortingOrder = 5;
    }

    private void UpdateArm()
    {
        pivot.rotation = Quaternion.Euler(0f, 0f, AngleDeg(t));
        if (carried != null) carried.transform.position = HandPos(t);
    }

    // ---------------------------------------------------------------
    // Save / load / demolish
    // ---------------------------------------------------------------

    public override void CaptureState(BuildingSave s)
    {
        base.CaptureState(s);
        if (carried != null && carried.Definition != null)
        {
            s.held = carried.Definition.SaveKey;
            s.progress = t;
        }
    }

    public override void RestoreState(BuildingSave s, Func<string, ItemDefinition> findItem)
    {
        base.RestoreState(s, findItem); // spawns heldItem if the save has one
        if (heldItem == null) return;

        carried = heldItem;
        heldItem = null;
        t = Mathf.Clamp01(s.progress);
        state = t >= 1f ? State.WaitDrop : State.Forward;
    }

    public override void Demolish()
    {
        if (carried != null) { carried.Release(); carried = null; }
        base.Demolish();
    }
}
