using UnityEngine;

/// <summary>
/// Base class for every placeable thing in Box Factory: conveyor belts, miners,
/// processors (smelters etc) and sellers all derive from this.
///
/// Responsibilities shared by everything:
///  - Registering itself with the GridManager at its cell.
///  - Auto-generating its own square sprite + collider (no art/prefab setup needed).
///  - Holding at most one "in transit" item and animating it from the entry edge
///    of this tile to the exit edge, then trying to push it onto the neighbor
///    this building is facing.
///  - Upgrading (faster) and demolishing.
///
/// Clicking a building is handled by BuildManager (grid lookup), not here.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public abstract class FactoryBuilding : MonoBehaviour
{
    public Vector2Int GridPosition { get; private set; }
    public Direction Facing { get; private set; } = Direction.Right;
    public BuildingDefinition Definition { get; private set; }
    public int Level { get; protected set; } = 1;

    [Tooltip("Tiles per second an item crosses this building at level 1.")]
    public float itemTravelSpeed = 2f;

    protected ItemVisual heldItem;
    protected float moveProgress;
    private SpriteRenderer demolishOverlay;
    private float demolishProgress;

    // Where a traveling item appears when it starts crossing this tile.
    protected virtual Vector3 EntryLocalOffset => Vector3.zero;

    // Where a traveling item ends up, at the edge it will be pushed out from.
    protected virtual Vector3 ExitLocalOffset =>
        (Vector3)(Vector2)DirectionUtil.ToVector(Facing) * 0.5f;

    /// <summary>
    /// Called once, right after Instantiate, by BuildManager.
    /// </summary>
    public void Initialize(Vector2Int gridPos, Direction facing, BuildingDefinition definition)
    {
        GridPosition = gridPos;
        Facing = facing;
        Definition = definition;

        transform.position = GridManager.Instance.GridToWorld(gridPos);
        transform.rotation = Quaternion.identity; // sprite itself doesn't need to rotate, it's a square

        SetupVisuals();
        GridManager.Instance.Register(gridPos, this);
    }

    private void SetupVisuals()
    {
        var sr = GetComponent<SpriteRenderer>();
        sr.sprite = SquareSpriteFactory.GetSquareSprite();
        sr.color = Definition != null ? Definition.iconColor : Color.white;
        sr.sortingOrder = 1;
        transform.localScale = Vector3.one;

        var collider = GetComponent<BoxCollider2D>();
        if (collider == null) collider = gameObject.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;

        // Small bright square offset toward the facing edge, purely so the
        // player can see which way this building points.
        if (transform.Find("FacingIndicator") != null) Destroy(transform.Find("FacingIndicator").gameObject);
        var indicator = new GameObject("FacingIndicator");
        indicator.transform.SetParent(transform, false);
        indicator.transform.localScale = new Vector3(0.28f, 0.28f, 1f);
        Vector2 dir = DirectionUtil.ToVector(Facing);
        indicator.transform.localPosition = new Vector3(dir.x, dir.y, 0f) * 0.36f;
        var indicatorSr = indicator.AddComponent<SpriteRenderer>();
        indicatorSr.sprite = SquareSpriteFactory.GetSquareSprite();
        indicatorSr.color = new Color(1f, 1f, 1f, 0.9f);
        indicatorSr.sortingOrder = 2;

        // Only create the demolish overlay once (SetDirection calls this again).
        if (demolishOverlay == null)
        {
            var overlayGo = new GameObject("DemolishOverlay");
            overlayGo.transform.SetParent(transform, false);
            overlayGo.transform.localScale = Vector3.zero;
            demolishOverlay = overlayGo.AddComponent<SpriteRenderer>();
            demolishOverlay.sprite = SquareSpriteFactory.GetSquareSprite();
            demolishOverlay.color = new Color(1f, 0.15f, 0.15f, 0.65f);
            demolishOverlay.sortingOrder = 3;
        }
    }

    protected virtual void Update()
    {
        // Update demolish overlay scale based on hold progress
        if (demolishOverlay != null)
        {
            demolishOverlay.transform.localScale = Vector3.one * demolishProgress;
        }

        if (heldItem != null)
        {
            moveProgress += itemTravelSpeed * Time.deltaTime;
            if (moveProgress > 1f) moveProgress = 1f;

            Vector3 worldEntry = transform.position + EntryLocalOffset;
            Vector3 worldExit = transform.position + ExitLocalOffset;
            heldItem.transform.position = Vector3.Lerp(worldEntry, worldExit, moveProgress);

            if (moveProgress >= 1f)
            {
                TryPushOutput();
            }
        }
    }

    protected virtual void TryPushOutput()
    {
        FactoryBuilding neighbor = GetNeighborInFacing();
        if (neighbor != null && neighbor.TryAcceptInput(heldItem))
        {
            heldItem = null;
            moveProgress = 0f;
        }
        // else: stays parked at the exit edge, blocked, until the neighbor has room.
    }

    /// <summary>
    /// Try to hand this building an item. Base implementation is plain
    /// pass-through behaviour (used by conveyor belts). Miners reject all
    /// input; Processors buffer input separately; Sellers consume for money.
    /// </summary>
    public virtual bool TryAcceptInput(ItemVisual item)
    {
        if (heldItem != null) return false;
        heldItem = item;
        moveProgress = 0f;
        return true;
    }

    protected FactoryBuilding GetNeighborInFacing()
    {
        Vector2Int offset = DirectionUtil.ToVector(Facing);
        return GridManager.Instance.GetBuilding(GridPosition + offset);
    }

    public virtual int GetUpgradeCost()
    {
        if (Definition == null) return 0;
        return Definition.baseUpgradeCost * Level;
    }

    public virtual Direction GetDirection()
    {
        return Facing;
    }

    public virtual void SetDirection(Direction newDir)
    {
        Facing = newDir;
        SetupVisuals(); // rebuild the facing indicator
    }

    /// <summary>
    /// Updates the demolish overlay progress. Called by BuildManager while
    /// the player holds right-click. Pass 0 to hide the overlay.
    /// </summary>
    public void SetDemolishProgress(float progress)
    {
        demolishProgress = Mathf.Clamp01(progress);
    }

    /// <summary>
    /// Spends money (if enough) and makes this building 25% faster per level.
    /// Returns false if the player couldn't afford it.
    /// </summary>
    public virtual bool TryUpgrade()
    {
        int cost = GetUpgradeCost();
        if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpend(cost))
            return false;

        Level++;
        itemTravelSpeed *= 1.25f;
        return true;
    }

    public virtual void Demolish()
    {
        Vector2Int cell = GridPosition;
        GridManager.Instance.Unregister(cell);
        if (heldItem != null) { heldItem.Release(); heldItem = null; }
        Destroy(gameObject);

        // Let neighbors re-route now that this cell is empty.
        if (BuildManager.Instance != null) BuildManager.Instance.OnBuildingRemoved(cell);
    }
}