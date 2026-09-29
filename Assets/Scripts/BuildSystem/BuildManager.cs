using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Controls:
///   Left click a toolbar button  - select that building to place
///   Move mouse                   - ghost preview snaps to the grid (green = valid, red = blocked)
///   R                            - rotate the ghost/placement direction clockwise
///   Left click on the grid       - place the building (spends money)
///   Right click / Escape         - cancel placement
///   Right click + hold (1 sec)   - delete a placed building
///   R (while hovering building)  - rotate an already placed building
/// </summary>
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance { get; private set; }

    [Tooltip("All building types the player can construct, shown as toolbar buttons in this order.")]
    public List<BuildingDefinition> availableBuildings = new List<BuildingDefinition>();

    [Tooltip("Time in seconds to hold right click for deletion.")]
    public float deletionHoldTime = 1f;

    public bool IsPlacing => selectedDefinition != null;

    private BuildingDefinition selectedDefinition;
    private GameObject ghost;
    private SpriteRenderer ghostRenderer;
    private Direction currentFacing = Direction.Right;
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
            }
            else
            {
                HandleRotatePlacedBuilding(mouse);
            }
        }

        if (!IsPlacing) return;

        Vector3 mouseWorld = GetMouseWorldPosition(mouse);
        Vector2Int cell = GridManager.Instance.WorldToGrid(mouseWorld);
        UpdateGhost(cell);

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool cellFree = !GridManager.Instance.IsOccupied(cell);

        if (!overUI && cellFree && mouse.leftButton.wasPressedThisFrame)
            PlaceBuilding(cell);

        bool cancelPressed = mouse.rightButton.wasPressedThisFrame
                             || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
        if (cancelPressed)
            CancelPlacement();
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
        EnsureGhost();
    }

    private void PlaceBuilding(Vector2Int cell)
    {
        if (selectedDefinition == null || selectedDefinition.prefab == null) return;
        if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpend(selectedDefinition.buildCost))
            return;

        FactoryBuilding instance = Instantiate(selectedDefinition.prefab);
        instance.Initialize(cell, currentFacing, selectedDefinition);
    }

    private void CancelPlacement()
    {
        selectedDefinition = null;
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