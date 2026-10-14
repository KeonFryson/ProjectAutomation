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
///   Left click on the grid       - place the building (free), facing the ghost's direction
///   Left click + drag            - (1x1 buildings) place on every cell you drag over.
///                                  Belts follow the drag direction (the previous tile turns to make corners).
///   Left click on a building     - open its window (when not placing)
///   Right click / Escape         - cancel placement
///   Right click + hold (1 sec)   - delete a placed building
///   Shift + Right click / Ctrl+C - copy settings of the hovered building (recipe, mined item)
///   Shift + Left click / Ctrl+V  - paste settings onto the hovered building (hold + drag to paste on many)
///
/// Buildings never rotate themselves because they touch: a belt feeds the tile it points at and
/// that is all. The only automatic facing is the drag direction while you drag-place belts.
/// Buildings larger than 1x1 are placed with the mouse over their center cell.
/// Placing is free; which buildings you can place is gated by the tech tree.
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

    /// <summary>Settings copied with Copy (null = nothing copied yet).</summary>
    public BuildingSettings Clipboard { get; private set; }

    private BuildingDefinition selectedDefinition;
    private GameObject ghost;
    private SpriteRenderer ghostRenderer;
    private SpriteRenderer ghostArrows;
    private Direction currentFacing = Direction.Right;
    private bool isDragging;
    private Vector2Int lastDragCell;

    // The last building placed by the current click/drag (so a drag can turn the previous belt).
    private FactoryBuilding lastPlaced;

    // Last cell we pasted settings onto while Shift+dragging (so we paste once per cell).
    private Vector2Int lastPasteCell = new Vector2Int(int.MinValue, int.MinValue);

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
        bool shiftHeld = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

        HandleDeletionInput(mouse, shiftHeld);

        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            if (IsPlacing)
                currentFacing = DirectionUtil.RotateClockwise(currentFacing);
            else
                HandleRotatePlacedBuilding(mouse);
        }

        if (!IsPlacing) HandleSettingsClipboard(mouse, keyboard, shiftHeld);

        if (!IsPlacing && mouse.leftButton.wasPressedThisFrame && !shiftHeld)
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
            lastPlaced = null;
            if (CanPlace(anchor) && !PlaceBuilding(anchor, currentFacing))
                isDragging = false; // building can't be placed (locked or missing prefab)
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

    // ---------------------------------------------------------------
    // Copy / paste building settings
    // ---------------------------------------------------------------

    private void HandleSettingsClipboard(Mouse mouse, Keyboard keyboard, bool shift)
    {
        if (!mouse.leftButton.isPressed) lastPasteCell = new Vector2Int(int.MinValue, int.MinValue);
        if (keyboard == null) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        Vector2Int cell = GridManager.Instance.WorldToGrid(GetMouseWorldPosition(mouse));
        FactoryBuilding hovered = GridManager.Instance.GetBuilding(cell);

        // Copy: Shift + Right click, or Ctrl+C
        if ((shift && mouse.rightButton.wasPressedThisFrame) || (ctrl && keyboard.cKey.wasPressedThisFrame))
            CopySettings(hovered);

        // Paste: Ctrl+V on the hovered building
        if (ctrl && keyboard.vKey.wasPressedThisFrame)
            PasteSettings(hovered);

        // Paste: Shift + Left click (hold and drag to paste on many buildings)
        if (shift && mouse.leftButton.isPressed && cell != lastPasteCell)
        {
            lastPasteCell = cell;
            PasteSettings(hovered);
        }
    }

    public bool CopySettings(FactoryBuilding b)
    {
        if (b == null) return false;

        var clip = new BuildingSettings();
        if (!b.CopySettings(clip))
        {
            GameToast.Show("This building has no settings to copy");
            return false;
        }

        Clipboard = clip;
        GameToast.Show("Copied: " + clip.Describe());
        return true;
    }

    public bool PasteSettings(FactoryBuilding b)
    {
        if (b == null) return false;
        if (Clipboard == null)
        {
            GameToast.Show("Nothing copied yet (Shift + Right click a building)");
            return false;
        }
        if (!b.PasteSettings(Clipboard))
        {
            GameToast.Show("Can't paste these settings here");
            return false;
        }

        GameToast.Show("Pasted: " + Clipboard.Describe());
        if (UIManager.Instance != null) UIManager.Instance.RefreshInspector(b);
        return true;
    }

    // ---------------------------------------------------------------
    // Drag placement
    // ---------------------------------------------------------------

    private static Direction DirFrom(Vector2Int v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y)) return v.x >= 0 ? Direction.Right : Direction.Left;
        return v.y >= 0 ? Direction.Up : Direction.Down;
    }

    /// <summary>
    /// Single-cell drag placement: steps one cell at a time. Belts face the direction of the drag,
    /// and the tile we just dragged from turns to follow it, which makes corners.
    /// </summary>
    private void DragTo(Vector2Int target)
    {
        bool belts = selectedDefinition != null && selectedDefinition.prefab is ConveyorBelt;

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

            Direction dir = belts ? DirFrom(c - prev) : currentFacing;

            // The belt we just placed in this drag follows the drag direction.
            if (belts && lastPlaced != null && lastPlaced.GridPosition == prev && lastPlaced is ConveyorBelt)
                lastPlaced.SetDirection(dir);

            if (!PlaceBuilding(c, dir))
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

    private void HandleDeletionInput(Mouse mouse, bool shiftHeld)
    {
        // Shift + Right click is "copy settings", so it never starts a demolish.
        if (mouse.rightButton.isPressed && !shiftHeld)
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
        EnsureGhost();
    }

    private bool CanPlace(Vector2Int anchor)
    {
        Footprint.GetCells(anchor, SelectedSize, currentFacing, tmpCells);
        foreach (var c in tmpCells)
            if (GridManager.Instance.IsOccupied(c)) return false;
        return true;
    }

    /// <summary>
    /// Places the selected building facing 'facing'. Assumes the cells are free.
    /// Returns false if the building is locked or has no prefab.
    /// </summary>
    private bool PlaceBuilding(Vector2Int anchor, Direction facing)
    {
        if (selectedDefinition == null || selectedDefinition.prefab == null) return false;
        if (!ResearchManager.IsBuildingUnlocked(selectedDefinition)) return false;

        FactoryBuilding instance = Instantiate(selectedDefinition.prefab);
        instance.Initialize(anchor, facing, selectedDefinition);
        lastPlaced = instance;
        return true;
    }

    // ---------------------------------------------------------------
    // Belt helpers (ghost preview only)
    // ---------------------------------------------------------------

    private static readonly Direction[] AllDirections =
        { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

    private static Direction Opposite(Direction d) => (Direction)(((int)d + 2) % 4);

    /// <summary>
    /// Same logic as ConveyorBelt.ResolveIncoming, but for a belt that isn't placed yet:
    /// fed from behind = straight, fed only from the side = corner.
    /// Only tiles that really push into the cell count.
    /// </summary>
    private Direction ResolveGhostIncoming(Vector2Int cell, Direction facing)
    {
        var grid = GridManager.Instance;
        Direction back = Opposite(facing);
        Direction? side = null;

        foreach (Direction d in AllDirections)
        {
            if (d == facing) continue;

            FactoryBuilding n = grid.GetBuilding(cell + DirectionUtil.ToVector(d));
            if (n == null || !n.FeedsCell(cell)) continue;

            if (d == back) return facing;
            if (side == null) side = Opposite(d);
        }

        return side ?? facing;
    }

    /// <summary>
    /// Called for every cell freed by a demolished building. Intentionally empty: belts keep
    /// their facing when a neighbour disappears (they never re-route by themselves).
    /// </summary>
    public void OnBuildingRemoved(Vector2Int removedCell) { }

    // ---------------------------------------------------------------
    // Ghost
    // ---------------------------------------------------------------

    public void CancelPlacement()
    {
        selectedDefinition = null;
        isDragging = false;
        lastPlaced = null;
        if (ghost != null) Destroy(ghost);
        if (ghostArrows != null) Destroy(ghostArrows.gameObject);
        ghostArrows = null;
    }

    private void EnsureGhost()
    {
        if (ghost != null) return;
        ghost = new GameObject("PlacementGhost");
        ghostRenderer = ghost.AddComponent<SpriteRenderer>();
        ghostRenderer.sprite = SquareSpriteFactory.GetSquareSprite();
        ghostRenderer.sortingOrder = 5;

        // Separate object (not a child) so the base sprite's stretch/rotation doesn't distort the arrows.
        var arrowsGo = new GameObject("PlacementGhostArrows");
        ghostArrows = arrowsGo.AddComponent<SpriteRenderer>();
        ghostArrows.sortingOrder = 6;
        ghostArrows.enabled = false;
    }

    private void UpdateGhost(Vector2Int anchor)
    {
        if (ghost == null) return;

        var grid = GridManager.Instance;
        var def = selectedDefinition;
        Vector2Int dims = Footprint.WorldSize(SelectedSize, currentFacing);
        Vector3 center = Footprint.WorldCenter(anchor, SelectedSize, currentFacing, grid.cellSize);
        ghost.transform.position = center;

        // The ghost shows exactly the facing the building will get when placed.
        bool isBelt = def != null && def.prefab is ConveyorBelt && SelectedSize == Vector2Int.one;
        Direction shownFacing = currentFacing;
        Sprite arrowSprite = null;
        if (isBelt)
        {
            Direction incoming = ResolveGhostIncoming(anchor, shownFacing);

            double fps = def.prefab.itemTravelSpeed * Mathf.Max(0.01f, def.speedMultiplier) * 32;
            int frame = (int)((long)(Time.timeAsDouble * fps) % ConveyorArrowSprites.Frames);
            arrowSprite = ConveyorArrowSprites.Get(incoming, shownFacing, frame);
        }

        Color baseColor;
        Sprite dirSprite = def != null ? def.GetDirectionalSprite(shownFacing) : null;

        if (def != null && (dirSprite != null || def.sprite != null))
        {
            // Same layout rules as FactoryBuilding.UpdateSpriteChild.
            Sprite shown = dirSprite != null ? dirSprite : def.sprite;
            Vector2 b = shown.bounds.size;
            b.x = Mathf.Max(0.0001f, b.x);
            b.y = Mathf.Max(0.0001f, b.y);
            float k = (isBelt ? 1f : 0.92f) * grid.cellSize; // belts fill the tile like the placed belt

            ghostRenderer.sprite = shown;
            if (dirSprite == null && def.rotateSpriteWithFacing)
            {
                bool left = shownFacing == Direction.Left;
                ghost.transform.rotation = Quaternion.Euler(0f, 0f, left ? 0f : DirectionUtil.ToAngle(shownFacing));
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
        Color tint = valid
            ? new Color(baseColor.r, baseColor.g, baseColor.b, 0.55f)
            : new Color(1f, 0.2f, 0.2f, 0.55f);
        ghostRenderer.color = tint;

        // Animated arrows on top of the base belt art (same as the placed belt).
        if (ghostArrows != null)
        {
            ghostArrows.enabled = arrowSprite != null;
            if (arrowSprite != null)
            {
                ghostArrows.sprite = arrowSprite;
                ghostArrows.transform.position = center;
                ghostArrows.transform.localScale = Vector3.one * grid.cellSize;
                ghostArrows.color = valid ? new Color(1f, 1f, 1f, 0.55f) : tint;
            }
        }
    }

    private Vector3 GetMouseWorldPosition(Mouse mouse)
    {
        if (mainCamera == null) mainCamera = Camera.main;

        Vector2 mousePos = mouse.position.ReadValue();
        Vector3 screenPos = new Vector3(mousePos.x, mousePos.y, -mainCamera.transform.position.z);
        return mainCamera.ScreenToWorldPoint(screenPos);
    }
}