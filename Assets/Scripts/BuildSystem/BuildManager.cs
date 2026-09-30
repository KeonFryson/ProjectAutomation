using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Controls:
///   Q                            - open/close the build menu
///   Left click a menu icon       - select that building to place
///   Move mouse                   - ghost preview snaps to the grid (green = valid, red = blocked)
///   R                            - rotate the ghost/placement direction clockwise (disables auto-connect)
///   Left click on the grid       - place the building (spends money)
///   Left click + drag            - place a building on every cell you drag over
///   Left click on a building     - open its inspector (when not placing)
///   Right click / Escape         - cancel placement
///   Right click + hold (1 sec)   - delete a placed building
///   R (while hovering building)  - rotate an already placed building
/// </summary>
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance { get; private set; }

    [Tooltip("All building types the player can construct, shown as icons in the build menu.")]
    public List<BuildingDefinition> availableBuildings = new List<BuildingDefinition>();

    [Tooltip("Time in seconds to hold right click for deletion.")]
    public float deletionHoldTime = 1f;

    public bool IsPlacing => selectedDefinition != null;

    private BuildingDefinition selectedDefinition;
    private GameObject ghost;
    private SpriteRenderer ghostRenderer;
    private Direction currentFacing = Direction.Right;
    private bool facingManuallySet;
    private bool isDragging;
    private Vector2Int lastDragCell;
    private Camera mainCamera;

    private FactoryBuilding rightClickTarget;
    private float rightClickHoldTimer;

    void Awake()
    {
        Instance = this;
        mainCamera = Camera.main;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Keyboard keyboard = Keyboard.current;

        // Handle right-click deletion on placed buildings
        HandleDeletionInput(mouse);

        // Handle R key for rotating placed buildings (even when not placing)
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

        // Click a placed building to open its inspector (only when not placing)
        if (!IsPlacing && mouse.leftButton.wasPressedThisFrame)
            HandleSelectBuilding(mouse);

        if (!IsPlacing) return;

        Vector3 mouseWorld = GetMouseWorldPosition(mouse);
        Vector2Int cell = GridManager.Instance.WorldToGrid(mouseWorld);
        UpdateGhost(cell);

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Only start a drag if the press began over the world, not over the menu/inspector.
        if (mouse.leftButton.wasPressedThisFrame && !overUI)
        {
            isDragging = true;
            lastDragCell = cell;
            if (!GridManager.Instance.IsOccupied(cell) && !PlaceBuilding(cell))
                isDragging = false; // couldn't afford it
        }
        else if (isDragging && mouse.leftButton.isPressed)
        {
            DragTo(cell);
        }

        if (!mouse.leftButton.isPressed) isDragging = false;

        bool cancelPressed = mouse.rightButton.wasPressedThisFrame
                             || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
        if (cancelPressed)
            CancelPlacement();
    }

    private void HandleSelectBuilding(Mouse mouse)
    {
        // Ignore clicks on UI (buttons, panels) and while the build menu is open
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (UIManager.Instance == null) return;
        if (UIManager.Instance.IsBuildMenuOpen) return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(GetMouseWorldPosition(mouse));
        FactoryBuilding building = GridManager.Instance.GetBuilding(cell);

        if (building != null)
            UIManager.Instance.ShowInspector(building);
        else
            UIManager.Instance.HideInspector(); // clicking empty ground closes the panel
    }

    /// <summary>
    /// Walks from the last cell to the target one step at a time (no diagonals,
    /// so belts stay connected) and places a building on each free cell.
    /// </summary>
    private void DragTo(Vector2Int target)
    {
        Vector2Int c = lastDragCell;
        while (c != target)
        {
            int dx = target.x - c.x;
            int dy = target.y - c.y;
            if (Mathf.Abs(dx) >= Mathf.Abs(dy)) c.x += (int)Mathf.Sign(dx);
            else c.y += (int)Mathf.Sign(dy);

            lastDragCell = c;
            if (GridManager.Instance.IsOccupied(c)) continue;

            if (!PlaceBuilding(c))
            {
                isDragging = false; // out of money (or invalid definition): stop the drag
                return;
            }
        }
    }

    private void HandleRotatePlacedBuilding(Mouse mouse)
    {
        Vector3 mouseWorld = GetMouseWorldPosition(mouse);
        Vector2Int cell = GridManager.Instance.WorldToGrid(mouseWorld);
        FactoryBuilding building = GridManager.Instance.GetBuilding(cell);

        if (building != null)
        {
            building.SetDirection(DirectionUtil.RotateClockwise(building.GetDirection()));
        }
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
                // Update the overlay progress based on hold duration
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
            // Clear overlay when not holding
            if (rightClickTarget != null)
            {
                rightClickTarget.SetDemolishProgress(0f);
            }
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
        selectedDefinition = availableBuildings[index];
        currentFacing = Direction.Right;
        facingManuallySet = false;
        EnsureGhost();
    }

    private bool PlaceBuilding(Vector2Int cell)
    {
        if (selectedDefinition == null || selectedDefinition.prefab == null) return false;
        if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpend(selectedDefinition.buildCost))
            return false;

        FactoryBuilding prefab = selectedDefinition.prefab;
        Direction facing = currentFacing;

        if (!facingManuallySet)
        {
            if (prefab is ConveyorBelt) AutoConnectNeighbors(cell); // only things that accept input get fed
            if (!(prefab is Seller || prefab is Processor)) facing = ResolveFacing(cell, currentFacing);
        }

        FactoryBuilding instance = Instantiate(prefab);
        instance.Initialize(cell, facing, selectedDefinition);
        return true;
    }

    // ---------------------------------------------------------------
    // Auto-connect
    // ---------------------------------------------------------------

    private static readonly Direction[] AllDirections =
        { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

    private static Direction Opposite(Direction d) => (Direction)(((int)d + 2) % 4);
    private static bool CanOutput(FactoryBuilding b) => !(b is Seller);
    private static bool CanAccept(FactoryBuilding b) => !(b is Miner);

    /// <summary>
    /// Any neighbor whose output points at an empty cell turns to face the
    /// building about to be placed at 'cell'.
    /// </summary>
    private void AutoConnectNeighbors(Vector2Int cell)
    {
        var grid = GridManager.Instance;
        foreach (Direction dir in AllDirections)
        {
            FactoryBuilding n = grid.GetBuilding(cell + DirectionUtil.ToVector(dir));
            if (n == null || !CanOutput(n)) continue;

            // Already connected to something? Leave it alone.
            if (grid.IsOccupied(n.GridPosition + DirectionUtil.ToVector(n.Facing))) continue;

            // 'cell' is behind n (n faces directly away from it): that's n's input
            // side, so don't flip n around to face it.
            if (n.Facing == dir) continue;

            Direction towardNew = Opposite(dir);
            if (n.Facing != towardNew) n.SetDirection(towardNew);
        }
    }

    /// <summary>
    /// Picks the facing for a new building: straight into a receiver if fed by
    /// a belt, else toward any receiver, else continue the feeder's direction,
    /// else keep the default.
    /// </summary>
    private Direction ResolveFacing(Vector2Int cell, Direction fallback)
    {
        var grid = GridManager.Instance;

        Direction? feederFacing = null;
        Direction? anyReceiver = null;

        foreach (Direction dir in AllDirections)
        {
            FactoryBuilding n = grid.GetBuilding(cell + DirectionUtil.ToVector(dir));
            if (n == null) continue;

            bool feedsUs = CanOutput(n) && n.GridPosition + DirectionUtil.ToVector(n.Facing) == cell;
            if (feedsUs)
            {
                feederFacing = n.Facing;
            }
            else if (CanAccept(n) && anyReceiver == null)
            {
                anyReceiver = dir;
            }
        }

        // Prefer going straight through if there's a receiver right ahead.
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

    /// <summary>
    /// Called after a building is removed. Any neighbor that was outputting into
    /// the now-empty cell turns toward another adjacent receiver, if there is one.
    /// </summary>
    public void OnBuildingRemoved(Vector2Int removedCell)
    {
        var grid = GridManager.Instance;
        foreach (Direction dir in AllDirections)
        {
            FactoryBuilding n = grid.GetBuilding(removedCell + DirectionUtil.ToVector(dir));
            if (n == null || !CanOutput(n)) continue;

            // Only care about neighbors that were pointing at the removed cell.
            if (n.GridPosition + DirectionUtil.ToVector(n.Facing) != removedCell) continue;

            Direction? best = null;
            foreach (Direction d in AllDirections)
            {
                Vector2Int otherCell = n.GridPosition + DirectionUtil.ToVector(d);
                if (otherCell == removedCell) continue;

                FactoryBuilding r = grid.GetBuilding(otherCell);
                if (r == null || !CanAccept(r)) continue;

                // Skip buildings that are feeding n, or we'd create a loop.
                if (CanOutput(r) && r.GridPosition + DirectionUtil.ToVector(r.Facing) == n.GridPosition) continue;

                best = d;
                break;
            }

            if (best.HasValue) n.SetDirection(best.Value);
            // else: leave it dangling; placing a new building there auto-connects again.
        }
    }

    // ---------------------------------------------------------------
    // Ghost
    // ---------------------------------------------------------------

    private void CancelPlacement()
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
        ghost.transform.localScale = Vector3.one * 0.92f;
    }

    private void UpdateGhost(Vector2Int cell)
    {
        if (ghost == null) return;
        ghost.transform.position = GridManager.Instance.GridToWorld(cell);

        bool valid = !GridManager.Instance.IsOccupied(cell);
        Color baseColor = selectedDefinition != null ? selectedDefinition.iconColor : Color.white;
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