using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for every placeable thing in Box Factory.
///
///  - Occupies one or more grid cells (BuildingDefinition.size).
///  - Draws a colored square, or the definition's sprite if one is assigned.
///  - Holds at most one "in transit" item and moves it to the front edge,
///    then pushes it into whatever building is in front of it (OutputCell).
///  - Speed comes from BuildingDefinition.speedMultiplier (no paid upgrades).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public abstract class FactoryBuilding : MonoBehaviour
{
    /// <summary>Anchor cell (for 1x1 buildings this is simply the cell).</summary>
    public Vector2Int GridPosition { get; private set; }
    public Direction Facing { get; private set; } = Direction.Right;
    public BuildingDefinition Definition { get; private set; }
    public Vector2Int Size { get; private set; } = Vector2Int.one;

    public bool IsSingleCell => Size.x == 1 && Size.y == 1;
    public IReadOnlyList<Vector2Int> Cells => cells;

    /// <summary>The cell this building pushes items into.</summary>
    public Vector2Int OutputCell => Footprint.OutputCell(GridPosition, Size, Facing);

    /// <summary>False for buildings that never receive items (Miner).</summary>
    public virtual bool HasInput => true;
    /// <summary>False for buildings that never send items out (Seller, ResearchLab).</summary>
    public virtual bool HasOutput => true;

    /// <summary>
    /// True if this building pushes items into the given cell. Buildings with more than one
    /// output (Splitter) override this so belts and the auto-connect logic see all of them.
    /// </summary>
    public virtual bool FeedsCell(Vector2Int cell) => HasOutput && OutputCell == cell;

    public float SpeedMultiplier =>
        Definition != null ? Mathf.Max(0.01f, Definition.speedMultiplier) : 1f;

    [Tooltip("Tiles per second an item crosses this building (multiplied by the definition's speedMultiplier).")]
    public float itemTravelSpeed = 2f;

    protected ItemVisual heldItem;
    protected float moveProgress;

    private readonly List<Vector2Int> cells = new List<Vector2Int>();
    private Transform indicator;
    private SpriteRenderer spriteChild;
    private SpriteRenderer demolishOverlay;
    private float demolishProgress;
    private Vector2 footprintWorldSize = Vector2.one;

    // Where a traveling item appears when it starts crossing this building.
    protected virtual Vector3 EntryLocalOffset => Vector3.zero;

    // Where a traveling item ends up: the middle of the front edge.
    protected virtual Vector3 ExitLocalOffset
    {
        get
        {
            var grid = GridManager.Instance;
            Vector2Int f = DirectionUtil.ToVector(Facing);
            Vector3 frontCell = grid.GridToWorld(OutputCell - f);
            return frontCell + new Vector3(f.x, f.y, 0f) * (0.5f * grid.cellSize) - transform.position;
        }
    }

    /// <summary>The item this building is currently holding/carrying (null if none).</summary>
    public ItemDefinition HeldItemDefinition => heldItem != null ? heldItem.Definition : null;

    /// <summary>
    /// Subclasses can return a sprite that is drawn as-is (no rotation) instead of the normal one.
    /// Return null to use the definition's sprites. ConveyorBelt uses this for curves.
    /// </summary>
    protected virtual Sprite GetOverrideSprite() { return null; }

    /// <summary>Re-applies sprite, size and position (call after something visual changed).</summary>
    protected void RefreshVisuals() { SetupVisuals(); }

    /// <summary>Called once, right after Instantiate, by BuildManager.</summary>
    public void Initialize(Vector2Int anchor, Direction facing, BuildingDefinition definition)
    {
        GridPosition = anchor;
        Facing = facing;
        Definition = definition;
        Size = definition != null ? Footprint.ClampSize(definition.size) : Vector2Int.one;
        Footprint.GetCells(anchor, Size, facing, cells);

        transform.rotation = Quaternion.identity;
        SetupVisuals();

        foreach (var c in cells) GridManager.Instance.Register(c, this);
    }

    // ---------------------------------------------------------------
    // Save / load (override in subclasses that carry extra state)
    // ---------------------------------------------------------------

    /// <summary>Writes the item currently being carried. Subclasses call base and add their own state.</summary>
    public virtual void CaptureState(BuildingSave s)
    {
        if (heldItem != null && heldItem.Definition != null)
        {
            s.held = heldItem.Definition.name;
            s.progress = moveProgress;
        }
    }

    /// <summary>Called right after Initialize when loading a save. findItem maps an item asset name to its definition.</summary>
    public virtual void RestoreState(BuildingSave s, Func<string, ItemDefinition> findItem)
    {
        if (string.IsNullOrEmpty(s.held)) return;
        ItemDefinition def = findItem(s.held);
        if (def == null) return;

        heldItem = ItemVisual.Spawn(def, transform.position);
        moveProgress = Mathf.Clamp01(s.progress);
    }

    private void SetupVisuals()
    {
        var grid = GridManager.Instance;
        float cs = grid.cellSize;

        Vector2Int worldDims = Footprint.WorldSize(Size, Facing);
        footprintWorldSize = new Vector2(worldDims.x, worldDims.y) * cs;
        transform.position = Footprint.WorldCenter(GridPosition, Size, Facing, cs);
        transform.localScale = Vector3.one;

        var sr = GetComponent<SpriteRenderer>();
        // Priority: subclass override (belt curve) > sprite for this exact facing > default sprite.
        Sprite dirSprite = GetOverrideSprite();
        if (dirSprite == null && Definition != null) dirSprite = Definition.GetDirectionalSprite(Facing);
        bool hasSprite = Definition != null && (dirSprite != null || Definition.sprite != null);
        bool rotateSprite = hasSprite && dirSprite == null && Definition.rotateSpriteWithFacing;
        bool spriteShowsDirection = hasSprite && (dirSprite != null || rotateSprite);

        if (hasSprite)
        {
            // The root renderer is hidden; a child renderer draws the artwork.
            sr.enabled = false;
            UpdateSpriteChild(cs, dirSprite, rotateSprite);
        }
        else
        {
            if (spriteChild != null) spriteChild.gameObject.SetActive(false);
            sr.enabled = true;
            sr.sprite = SquareSpriteFactory.GetSquareSprite();
            sr.drawMode = SpriteDrawMode.Tiled; // stretches the flat square over the whole footprint
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = footprintWorldSize;
            sr.color = Definition != null ? Definition.iconColor : Color.white;
            sr.sortingOrder = 1;
        }

        var collider = GetComponent<BoxCollider2D>();
        if (collider == null) collider = gameObject.AddComponent<BoxCollider2D>();
        collider.size = footprintWorldSize;
        collider.offset = Vector2.zero;

        // Small bright square on the front edge showing which way this points.
        if (indicator == null)
        {
            var go = new GameObject("FacingIndicator");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.28f, 0.28f, 1f);
            var isr = go.AddComponent<SpriteRenderer>();
            isr.sprite = SquareSpriteFactory.GetSquareSprite();
            isr.color = new Color(1f, 1f, 1f, 0.9f);
            isr.sortingOrder = 2;
            indicator = go.transform;
        }
        // A directional or rotating sprite already shows which way the building points.
        indicator.gameObject.SetActive(!spriteShowsDirection);
        Vector2Int f = DirectionUtil.ToVector(Facing);
        indicator.position = grid.GridToWorld(OutputCell - f) + new Vector3(f.x, f.y, 0f) * (0.36f * cs);

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

    /// <summary>
    /// Draws the definition's sprite on a child object, stretched over the whole footprint.
    /// If dirSprite is set it is drawn as-is (the art was made for this facing).
    /// Otherwise the default sprite is used, expected to face RIGHT: Up/Down rotate it,
    /// Left mirrors it so it never ends up upside down (when 'rotate' is true).
    /// </summary>
    private void UpdateSpriteChild(float cs, Sprite dirSprite, bool rotate)
    {
        if (spriteChild == null)
        {
            var go = new GameObject("Sprite");
            go.transform.SetParent(transform, false);
            spriteChild = go.AddComponent<SpriteRenderer>();
            spriteChild.sortingOrder = 1;
        }

        spriteChild.gameObject.SetActive(true);
        Sprite shown = dirSprite != null ? dirSprite : Definition.sprite;
        spriteChild.sprite = shown;
        spriteChild.color = Color.white;

        Vector2 spriteSize = shown.bounds.size;
        spriteSize.x = Mathf.Max(0.0001f, spriteSize.x);
        spriteSize.y = Mathf.Max(0.0001f, spriteSize.y);

        Transform t = spriteChild.transform;
        t.localPosition = Vector3.zero;

        if (rotate)
        {
            // Local X = along facing (length), local Y = across it (width).
            bool left = Facing == Direction.Left;
            t.localRotation = Quaternion.Euler(0f, 0f, left ? 0f : DirectionUtil.ToAngle(Facing));
            spriteChild.flipX = left;
            t.localScale = new Vector3(Size.x * cs / spriteSize.x, Size.y * cs / spriteSize.y, 1f);
        }
        else
        {
            t.localRotation = Quaternion.identity;
            spriteChild.flipX = false;
            t.localScale = new Vector3(footprintWorldSize.x / spriteSize.x, footprintWorldSize.y / spriteSize.y, 1f);
        }
    }

    protected virtual void Update()
    {
        if (demolishOverlay != null)
        {
            demolishOverlay.transform.localScale = new Vector3(
                footprintWorldSize.x * demolishProgress,
                footprintWorldSize.y * demolishProgress, 1f);
        }

        if (heldItem != null)
        {
            moveProgress += itemTravelSpeed * SpeedMultiplier * Time.deltaTime;
            if (moveProgress > 1f) moveProgress = 1f;

            Vector3 worldEntry = transform.position + EntryLocalOffset;
            Vector3 worldExit = transform.position + ExitLocalOffset;
            heldItem.transform.position = Vector3.Lerp(worldEntry, worldExit, moveProgress);

            if (moveProgress >= 1f) TryPushOutput();
        }
    }

    protected virtual void TryPushOutput()
    {
        FactoryBuilding neighbor = GetNeighborInFacing();
        if (neighbor != null && neighbor != this && neighbor.TryAcceptInput(heldItem))
        {
            heldItem = null;
            moveProgress = 0f;
        }
        // else: stays parked at the exit edge until the neighbor has room.
    }

    /// <summary>
    /// Try to hand this building an item. Base implementation is plain
    /// pass-through (conveyor belts). Miners reject all input; Processors
    /// buffer it; Sellers and ResearchLabs consume it.
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
        return GridManager.Instance.GetBuilding(OutputCell);
    }

    public virtual Direction GetDirection()
    {
        return Facing;
    }

    /// <summary>
    /// Rotates the building. Multi-cell buildings rotate around their center
    /// cell and refuse to rotate if the new footprint would hit something.
    /// </summary>
    public virtual void SetDirection(Direction newDir)
    {
        if (newDir == Facing) return;

        if (!IsSingleCell)
        {
            var grid = GridManager.Instance;
            Vector2Int center = Footprint.CenterCell(GridPosition, Size, Facing);
            Vector2Int newAnchor = Footprint.AnchorFromCenter(center, Size, newDir);

            var newCells = new List<Vector2Int>();
            Footprint.GetCells(newAnchor, Size, newDir, newCells);
            foreach (var c in newCells)
            {
                FactoryBuilding other = grid.GetBuilding(c);
                if (other != null && other != this) return; // blocked
            }

            foreach (var c in cells) grid.Unregister(c);
            GridPosition = newAnchor;
            cells.Clear();
            cells.AddRange(newCells);
            Facing = newDir;
            foreach (var c in cells) grid.Register(c, this);
        }
        else
        {
            Facing = newDir;
        }

        SetupVisuals();
    }

    /// <summary>Updates the demolish overlay. Pass 0 to hide it.</summary>
    public void SetDemolishProgress(float progress)
    {
        demolishProgress = Mathf.Clamp01(progress);
    }

    public virtual void Demolish()
    {
        var removed = new List<Vector2Int>(cells);
        foreach (var c in removed) GridManager.Instance.Unregister(c);
        if (heldItem != null) { heldItem.Release(); heldItem = null; }
        Destroy(gameObject);

        // Let neighbors re-route now that these cells are empty.
        if (BuildManager.Instance != null)
            foreach (var c in removed) BuildManager.Instance.OnBuildingRemoved(c);
    }
}
