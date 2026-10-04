using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Controls:
///   Q                            - open/close the build menu
///   T                            - open/close the tech tree
///   1-9                          - pick a building from the hotbar
///   Move mouse                   - ghost preview snaps to the grid (green = valid, red = blocked)
///   R                            - rotate the ghost / hovered building clockwise
///   Left click on the grid       - place the building (spends money)
///   Left click + drag            - (1x1 buildings) place on every cell you drag over
///   Left click on a building     - open its window (when not placing)
///   Right click / Escape         - cancel placement
///   Right click + hold (1 sec)   - delete a placed building
/// Buildings larger than 1x1 are placed with the mouse over their center cell.
/// </summary>
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance { get; private set; }

    [Tooltip("All building types in the game. Only unlocked ones show up in the menu/hotbar.")]
    public List<BuildingDefinition> availableBuildings = new List<BuildingDefinition>();

    [Tooltip("Time in seconds to hold right click for deletion.")]
    public float deletionHoldTime = 1f;

    public bool IsPlacing => selectedDefinition != null;
    public BuildingDefinition SelectedDefinition => selectedDefinition;

    private BuildingDefinition selectedDefinition;
    private GameObject ghost;
    private SpriteRenderer ghostRenderer;
    private Direction currentFacing = Direction.Right;
    private bool facingManuallySet;
    private bool isDragging;
    private Vector2Int lastDragCell;

    // The tile we just dragged from (only valid while placing a dragged tile).
    // Lets auto-connect tell a corner turn apart from a parallel line.
    private Vector2Int dragPrevCell;
    private bool hasDragPrev;

    private Camera mainCamera;
    private readonly List<Vector2Int> tmpCells = new List<Vector2Int>();

    private FactoryBuilding rightClickTarget;
    private float rightClickHoldTimer;

    void Awake()
    {
        Instance = this;
        mainCamera = Camera.main;
    }

    private Vector2Int SelectedSize =>
        selectedDefinition != null ? Footprint.ClampSize(selectedDefinition.size) : Vector2Int.one;

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Keyboard keyboard = Keyboard.current;

        HandleDeletionInput(mouse);

        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            if (IsPlacing)
            {
                currentFacing = DirectionUtil.RotateClockwise(currentFacing);
                facingManuallySet = true;
            }
            else
            {
                HandleRotatePlacedBuilding(mouse);
            }
        }

        if (!IsPlacing && mouse.leftButton.wasPressedThisFrame)
            HandleSelectBuilding(mouse);

        if (!IsPlacing) return;

        Vector3 mouseWorld = GetMouseWorldPosition(mouse);
        Vector2Int cursorCell = GridManager.Instance.WorldToGrid(mouseWorld);
        Vector2Int anchor = Footprint.AnchorFromCenter(cursorCell, SelectedSize, currentFacing);
        UpdateGhost(anchor);

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool single = SelectedSize == Vector2Int.one;

        if (mouse.leftButton.wasPressedThisFrame && !overUI)
        {
            isDragging = single; // only 1x1 buildings can be drag-placed
            lastDragCell = cursorCell;
            if (CanPlace(anchor) && !PlaceBuilding(anchor))
                isDragging = false; // couldn't afford it
        }
        else if (isDragging && mouse.leftButton.isPressed)
        {
            DragTo(cursorCell);
        }

        if (!mouse.leftButton.isPressed) isDragging = false;

        bool cancelPressed = mouse.rightButton.wasPressedThisFrame
                             || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
        if (cancelPressed)
            CancelPlacement();
    }

    private void HandleSelectBuilding(Mouse mouse)
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (UIManager.Instance == null) return;
        if (UIManager.Instance.IsMenuOpen) return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(GetMouseWorldPosition(mouse));
        FactoryBuilding building = GridManager.Instance.GetBuilding(cell);

        if (building != null)
            UIManager.Instance.ShowInspector(building);
        else
            UIManager.Instance.HideInspector();
    }

    /// <summary>Single-cell drag placement: steps one cell at a time so belts stay connected.</summary>
    private void DragTo(Vector2Int target)
    {
        Vector2Int c = lastDragCell;
        while (c != target)
        {
            Vector2Int prev = c;
            int dx = target.x - c.x;
            int dy = target.y - c.y;
            if (Mathf.Abs(dx) >= Mathf.Abs(dy)) c.x += (int)Mathf.Sign(dx);
            else c.y += (int)Mathf.Sign(dy);

            lastDragCell = c;
            if (GridManager.Instance.IsOccupied(c)) continue;

            dragPrevCell = prev;
            hasDragPrev = true;
            bool placed = PlaceBuilding(c);
            hasDragPrev = false;

            if (!placed)
            {
                isDragging = false;
                return;
            }
        }
    }

    private void HandleRotatePlacedBuilding(Mouse mouse)
    {
        Vector2Int cell = GridManager.Instance.WorldToGrid(GetMouseWorldPosition(mouse));
        FactoryBuilding building = GridManager.Instance.GetBuilding(cell);
        if (building != null)
            building.SetDirection(DirectionUtil.RotateClockwise(building.GetDirection()));
    }

    private void HandleDeletionInput(Mouse mouse)
    {
        if (mouse.rightButton.isPressed)
        {
            rightClickHoldTimer += Time.deltaTime;

            if (rightClickTarget == null)
            {
                Vector3 mouseWorld = GetMouseWorldPosition(mouse);
                rightClickTarget = GridManager.Instance.GetBuilding(GridManager.Instance.WorldToGrid(mouseWorld));
            }

            if (rightClickTarget != null)
            {
                float progress = rightClickHoldTimer / deletionHoldTime;
                rightClickTarget.SetDemolishProgress(progress);

                if (rightClickHoldTimer >= deletionHoldTime)
                {
                    rightClickTarget.Demolish();
                    ResetDeletionState();
                }
            }
        }
        else
        {
            if (rightClickTarget != null) rightClickTarget.SetDemolishProgress(0f);
            ResetDeletionState();
        }
    }

    private void ResetDeletionState()
    {
        rightClickTarget = null;
        rightClickHoldTimer = 0f;
    }

    public void SelectBuildingToPlace(int index)
    {
        if (index < 0 || index >= availableBuildings.Count) return;
        var def = availableBuildings[index];
        if (!ResearchManager.IsBuildingUnlocked(def)) return;

        selectedDefinition = def;
        currentFacing = Direction.Right;
        facingManuallySet = false;
        EnsureGhost();
    }

    private bool CanPlace(Vector2Int anchor)
    {
        Footprint.GetCells(anchor, SelectedSize, currentFacing, tmpCells);
        foreach (var c in tmpCells)
            if (GridManager.Instance.IsOccupied(c)) return false;
        return true;
    }

    /// <summary>Assumes CanPlace(anchor) was checked. Returns false if the player can't pay.</summary>
    private bool PlaceBuilding(Vector2Int anchor)
    {
        if (selectedDefinition == null || selectedDefinition.prefab == null) return false;
        if (!ResearchManager.IsBuildingUnlocked(selectedDefinition)) return false;
        if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpend(selectedDefinition.buildCost))
            return false;

        FactoryBuilding prefab = selectedDefinition.prefab;
        Direction facing = currentFacing;
        bool single = SelectedSize == Vector2Int.one;

        // Auto-facing only makes sense for single tiles; bigger machines use the ghost's rotation.
        if (!facingManuallySet && single)
        {
            // Auto-connect is belts only: other buildings keep the ghost's facing
            // and never get rotated by a newly placed belt.
            if (prefab is ConveyorBelt)
            {
                AutoConnectNeighbors(anchor);
                facing = ResolveFacing(anchor, currentFacing);
            }
        }

        FactoryBuilding instance = Instantiate(prefab);
        instance.Initialize(anchor, facing, selectedDefinition);
        return true;
    }

    // ---------------------------------------------------------------
    // Auto-connect (single-cell buildings only get rotated automatically)
    // ---------------------------------------------------------------

    private static readonly Direction[] AllDirections =
        { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

    private static Direction Opposite(Direction d) => (Direction)(((int)d + 2) % 4);
    private static bool CanOutput(FactoryBuilding b) => b.HasOutput;
    private static bool CanAccept(FactoryBuilding b) => b.HasInput;

    // True if 'n' is a conveyor belt whose facing is perpendicular to the direction
    // from the new tile to it, i.e. the new tile would be touching its side.
    private static bool IsSideBelt(FactoryBuilding n, Direction dirToNeighbor)
    {
        return n is ConveyorBelt && ((int)n.Facing % 2) != ((int)dirToNeighbor % 2);
    }

    private void AutoConnectNeighbors(Vector2Int cell)
    {
        var grid = GridManager.Instance;
        foreach (Direction dir in AllDirections)
        {
            FactoryBuilding n = grid.GetBuilding(cell + DirectionUtil.ToVector(dir));
            if (n == null || !(n is ConveyorBelt) || !CanOutput(n) || !n.IsSingleCell) continue;

            // Don't turn a neighboring belt sideways into us (parallel lines),
            // unless it's the tile we just dragged from (that's a corner).
            if (IsSideBelt(n, dir) && !(hasDragPrev && dragPrevCell == n.GridPosition)) continue;

            if (grid.IsOccupied(n.OutputCell)) continue;
            if (n.Facing == dir) continue;

            Direction towardNew = Opposite(dir);
            if (n.Facing != towardNew) n.SetDirection(towardNew);
        }
    }

    private Direction ResolveFacing(Vector2Int cell, Direction fallback)
    {
        var grid = GridManager.Instance;

        Direction? feederFacing = null;
        Direction? anyReceiver = null;

        foreach (Direction dir in AllDirections)
        {
            FactoryBuilding n = grid.GetBuilding(cell + DirectionUtil.ToVector(dir));
            if (n == null) continue;

            bool feedsUs = CanOutput(n) && n.OutputCell == cell;
            if (feedsUs) feederFacing = n.Facing;
            else if (CanAccept(n) && anyReceiver == null && !IsSideBelt(n, dir)) anyReceiver = dir;
        }

        if (feederFacing.HasValue)
        {
            var ahead = cell + DirectionUtil.ToVector(feederFacing.Value);
            var aheadBuilding = grid.GetBuilding(ahead);
            if (aheadBuilding != null && CanAccept(aheadBuilding)) return feederFacing.Value;
        }

        if (anyReceiver.HasValue) return anyReceiver.Value;
        if (feederFacing.HasValue) return feederFacing.Value;
        return fallback;
    }

    /// <summary>Called for every cell freed by a demolished building.</summary>
    public void OnBuildingRemoved(Vector2Int removedCell)
    {
        var grid = GridManager.Instance;
        foreach (Direction dir in AllDirections)
        {
            FactoryBuilding n = grid.GetBuilding(removedCell + DirectionUtil.ToVector(dir));
            if (n == null || !(n is ConveyorBelt) || !CanOutput(n) || !n.IsSingleCell) continue;
            if (n.OutputCell != removedCell) continue;

            Direction? best = null;
            foreach (Direction d in AllDirections)
            {
                Vector2Int otherCell = n.GridPosition + DirectionUtil.ToVector(d);
                if (otherCell == removedCell) continue;

                FactoryBuilding r = grid.GetBuilding(otherCell);
                if (r == null || !CanAccept(r)) continue;

                // Skip buildings that feed n, or we'd create a loop.
                if (CanOutput(r) && r.OutputCell == n.GridPosition) continue;

                best = d;
                break;
            }

            if (best.HasValue) n.SetDirection(best.Value);
        }
    }

    // ---------------------------------------------------------------
    // Ghost
    // ---------------------------------------------------------------

    public void CancelPlacement()
    {
        selectedDefinition = null;
        isDragging = false;
        if (ghost != null) Destroy(ghost);
    }

    private void EnsureGhost()
    {
        if (ghost != null) return;
        ghost = new GameObject("PlacementGhost");
        ghostRenderer = ghost.AddComponent<SpriteRenderer>();
        ghostRenderer.sprite = SquareSpriteFactory.GetSquareSprite();
        ghostRenderer.sortingOrder = 5;
    }

    private void UpdateGhost(Vector2Int anchor)
    {
        if (ghost == null) return;

        var grid = GridManager.Instance;
        Vector2Int dims = Footprint.WorldSize(SelectedSize, currentFacing);
        ghost.transform.position = Footprint.WorldCenter(anchor, SelectedSize, currentFacing, grid.cellSize);

        var def = selectedDefinition;
        Color baseColor;

        Sprite dirSprite = def != null ? def.GetDirectionalSprite(currentFacing) : null;

        if (def != null && (dirSprite != null || def.sprite != null))
        {
            // Same layout rules as FactoryBuilding.UpdateSpriteChild.
            Sprite shown = dirSprite != null ? dirSprite : def.sprite;
            Vector2 b = shown.bounds.size;
            b.x = Mathf.Max(0.0001f, b.x);
            b.y = Mathf.Max(0.0001f, b.y);
            float k = 0.92f * grid.cellSize;

            ghostRenderer.sprite = shown;
            if (dirSprite == null && def.rotateSpriteWithFacing)
            {
                bool left = currentFacing == Direction.Left;
                ghost.transform.rotation = Quaternion.Euler(0f, 0f, left ? 0f : DirectionUtil.ToAngle(currentFacing));
                ghostRenderer.flipX = left;
                ghost.transform.localScale = new Vector3(SelectedSize.x * k / b.x, SelectedSize.y * k / b.y, 1f);
            }
            else
            {
                ghost.transform.rotation = Quaternion.identity;
                ghostRenderer.flipX = false;
                ghost.transform.localScale = new Vector3(dims.x * k / b.x, dims.y * k / b.y, 1f);
            }
            baseColor = Color.white;
        }
        else
        {
            ghostRenderer.sprite = SquareSpriteFactory.GetSquareSprite();
            ghostRenderer.flipX = false;
            ghost.transform.rotation = Quaternion.identity;
            ghost.transform.localScale = new Vector3(dims.x * 0.92f * grid.cellSize, dims.y * 0.92f * grid.cellSize, 1f);
            baseColor = def != null ? def.iconColor : Color.white;
        }

        bool valid = CanPlace(anchor);
        ghostRenderer.color = valid
            ? new Color(baseColor.r, baseColor.g, baseColor.b, 0.55f)
            : new Color(1f, 0.2f, 0.2f, 0.55f);
    }

    private Vector3 GetMouseWorldPosition(Mouse mouse)
    {
        if (mainCamera == null) mainCamera = Camera.main;

        Vector2 mousePos = mouse.position.ReadValue();
        Vector3 screenPos = new Vector3(mousePos.x, mousePos.y, -mainCamera.transform.position.z);
        return mainCamera.ScreenToWorldPoint(screenPos);
    }
}