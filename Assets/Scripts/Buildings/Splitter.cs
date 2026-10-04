using UnityEngine;

/// <summary>
/// Factorio-style splitter: 1 cell long, 2 cells wide (set the BuildingDefinition size to X=1, Y=2).
///
///  - Two input lanes, one per footprint cell. A lane takes items from whatever feeds its cell
///    (the belt behind it, or one from the side).
///  - Two outputs, the cells directly in front of each lane. Items alternate between the outputs,
///    and if one output is blocked (or has no building) everything goes to the other.
///  - Each lane buffers one item, so both belts can run at full speed through it.
///  - Neighbours that push INTO the splitter are never treated as outputs.
///
/// Footprint recap (see Footprint.cs): the anchor is the back-right cell looking in the facing
/// direction, and the second cell is to its left.
/// </summary>
public class Splitter : FactoryBuilding
{
    private const int Lanes = 2;

    private readonly ItemVisual[] laneItem = new ItemVisual[Lanes];
    private readonly float[] laneProgress = new float[Lanes];
    private int nextOut; // output that should get the next item

    private Vector2Int F => DirectionUtil.ToVector(Facing);
    private Vector2Int L => Footprint.Left(Facing);
    private Vector2Int LaneCell(int i) { return GridPosition + L * i; }
    private Vector2Int OutCell(int i) { return LaneCell(i) + F; }

    void Start()
    {
        if (Size != new Vector2Int(1, 2))
            Debug.LogWarning("Splitter '" + name + "' expects a BuildingDefinition size of (1, 2) but has " + Size + ".", this);
    }

    // ---- Wiring ----

    /// <summary>Feeds both of its front cells (belts use this to draw corners).</summary>
    public override bool FeedsCell(Vector2Int cell)
    {
        for (int i = 0; i < Lanes; i++)
            if (OutCell(i) == cell) return true;
        return false;
    }

    /// <summary>The building on output i if it can receive from us, otherwise null.</summary>
    private FactoryBuilding Candidate(int i)
    {
        FactoryBuilding n = GridManager.Instance.GetBuilding(OutCell(i));
        if (n == null || n == this || !n.HasInput) return null;
        foreach (var c in Cells)
            if (n.FeedsCell(c)) return null; // it pushes into us: that's an input, not an output
        return n;
    }

    /// <summary>The output a waiting item is visually heading for.</summary>
    private int TargetOut()
    {
        for (int k = 0; k < Lanes; k++)
        {
            int idx = (nextOut + k) % Lanes;
            if (Candidate(idx) != null) return idx;
        }
        return nextOut;
    }

    // ---- World positions ----

    private Vector3 EntryPos(int lane)
    {
        var grid = GridManager.Instance;
        Vector2Int f = F;
        return grid.GridToWorld(LaneCell(lane)) - new Vector3(f.x, f.y, 0f) * (0.5f * grid.cellSize);
    }

    private Vector3 ExitPos(int output)
    {
        var grid = GridManager.Instance;
        Vector2Int f = F;
        return grid.GridToWorld(LaneCell(output)) + new Vector3(f.x, f.y, 0f) * (0.5f * grid.cellSize);
    }

    /// <summary>Get position along a path with hard angles (L-shaped movement).</summary>
    private Vector3 GetPathPosition(Vector3 start, Vector3 end, float progress)
    {
        // Move horizontally first, then vertically
        Vector3 midpoint = new Vector3(end.x, start.y, start.z);

        if (progress < 0.5f)
        {
            // First half: horizontal movement from start to midpoint
            return Vector3.Lerp(start, midpoint, progress * 2f);
        }
        else
        {
            // Second half: vertical movement from midpoint to end
            return Vector3.Lerp(midpoint, end, (progress - 0.5f) * 2f);
        }
    }

    // ---- Item flow ----

    public override bool TryAcceptInput(ItemVisual item)
    {
        if (item == null) return false;

        // The feeder has just placed the item at its own exit edge, so the nearest lane cell
        // tells us which lane it arrived on.
        var grid = GridManager.Instance;
        Vector3 p = item.transform.position;
        int lane = 0;
        float best = float.MaxValue;
        for (int i = 0; i < Lanes; i++)
        {
            float d = (grid.GridToWorld(LaneCell(i)) - p).sqrMagnitude;
            if (d < best) { best = d; lane = i; }
        }

        if (laneItem[lane] != null) return false;
        laneItem[lane] = item;
        laneProgress[lane] = 0f;
        return true;
    }

    protected override void Update()
    {
        base.Update(); // demolish overlay animation (base never sees an item: heldItem stays null)

        for (int i = 0; i < Lanes; i++)
        {
            ItemVisual item = laneItem[i];
            if (item == null) continue;

            laneProgress[i] = Mathf.Min(1f, laneProgress[i] + itemTravelSpeed * SpeedMultiplier * Time.deltaTime);
            item.transform.position = GetPathPosition(EntryPos(i), ExitPos(TargetOut()), laneProgress[i]);

            if (laneProgress[i] >= 1f) TryPush(i);
        }
    }

    private void TryPush(int lane)
    {
        ItemVisual item = laneItem[lane];
        Vector3 resting = item.transform.position;

        for (int k = 0; k < Lanes; k++)
        {
            int idx = (nextOut + k) % Lanes;
            FactoryBuilding n = Candidate(idx);
            if (n == null) continue;

            item.transform.position = ExitPos(idx); // receivers read the item's position
            if (n.TryAcceptInput(item))
            {
                laneItem[lane] = null;
                laneProgress[lane] = 0f;
                nextOut = (idx + 1) % Lanes;
                return;
            }
            item.transform.position = resting;
        }
    }

    public override void Demolish()
    {
        for (int i = 0; i < Lanes; i++)
        {
            if (laneItem[i] != null) { laneItem[i].Release(); laneItem[i] = null; }
        }
        base.Demolish();
    }
}