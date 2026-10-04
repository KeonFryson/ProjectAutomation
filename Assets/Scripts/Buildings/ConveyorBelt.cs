using UnityEngine;

/// <summary>
/// The simplest building: carries an item across its own tile, from the edge
/// facing away from its direction to the edge it faces, then hands it to
/// whatever is next. Upgrading a belt makes it move items faster.
///
/// Draws animated chevrons on top of its colored square. The chevrons scroll
/// at exactly the speed items travel (itemTravelSpeed * speedMultiplier), all
/// belts share one clock so neighbours stay in phase, and the tile becomes a
/// curved corner when another building feeds it from the side.
/// </summary>
public class ConveyorBelt : FactoryBuilding
{
    private const int PixelsPerTile = 32; // art is 32 px per tile, chevrons move 1 px per frame

    private static readonly Direction[] AllDirections =
        { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

    private SpriteRenderer arrows;
    private Transform facingIndicator;
    private bool visualsReady;

    // Direction items are travelling when they ENTER this tile.
    // Equal to Facing for a straight belt, perpendicular for a corner.
    private Direction incoming;
    private bool incomingValid;

    // Items enter from the edge they come from, so on a corner they start at
    // the side edge instead of the back edge.
    protected override Vector3 EntryLocalOffset
    {
        get
        {
            Direction dir = incomingValid ? incoming : Facing;
            return -(Vector3)(Vector2)DirectionUtil.ToVector(dir) * 0.5f;
        }
    }

    protected override void Update()
    {
        base.Update();

        if (!visualsReady) SetupArrows();

        incoming = ResolveIncoming();
        incomingValid = true;

        // One frame per 1/32 of a tile travelled, from a shared clock.
        double framesPerSecond = itemTravelSpeed * SpeedMultiplier * PixelsPerTile;
        int frame = (int)((long)(Time.timeAsDouble * framesPerSecond) % ConveyorArrowSprites.Frames);

        Sprite sprite = ConveyorArrowSprites.Get(incoming, Facing, frame);
        arrows.sprite = sprite;

        // If the art is missing, fall back to the old white facing square.
        if (facingIndicator != null) facingIndicator.gameObject.SetActive(sprite == null);
    }

    private void SetupArrows()
    {
        visualsReady = true;

        facingIndicator = transform.Find("FacingIndicator");

        var go = new GameObject("BeltArrows");
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * GridManager.Instance.cellSize;
        arrows = go.AddComponent<SpriteRenderer>();
        arrows.sortingOrder = 2; // above the belt square (1), below the demolish overlay (3) and items (10)
    }

    /// <summary>
    /// Works out which way items are travelling as they enter this tile.
    /// Fed from behind (or not fed at all) = straight. Fed from the side = corner.
    /// </summary>
    private Direction ResolveIncoming()
    {
        var grid = GridManager.Instance;
        Direction back = Opposite(Facing);
        Direction? side = null;

        foreach (Direction d in AllDirections)
        {
            if (d == Facing) continue;

            FactoryBuilding n = grid.GetBuilding(GridPosition + DirectionUtil.ToVector(d));
            if (n == null || n == this || !n.HasOutput || n.OutputCell != GridPosition) continue;

            if (d == back) return Facing; // a feeder behind us always wins: straight belt
            if (side == null) side = Opposite(d);
        }

        return side ?? Facing;
    }

    private static Direction Opposite(Direction d)
    {
        return (Direction)(((int)d + 2) % 4);
    }
}
