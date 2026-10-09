using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry of what building (if any) occupies each grid cell.
/// Attach to a single empty GameObject named "GridManager" in the scene.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Tooltip("World size of one grid cell. Keep at 1 unless you also scale all sprites.")]
    public float cellSize = 1f;

    private readonly Dictionary<Vector2Int, FactoryBuilding> buildings = new Dictionary<Vector2Int, FactoryBuilding>();

    void Awake()
    {
        Instance = this;
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / cellSize),
            Mathf.RoundToInt(worldPos.y / cellSize));
    }

    public Vector3 GridToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * cellSize, cell.y * cellSize, 0f);
    }

    public bool IsOccupied(Vector2Int cell)
    {
        return buildings.ContainsKey(cell);
    }

    public FactoryBuilding GetBuilding(Vector2Int cell)
    {
        buildings.TryGetValue(cell, out var building);
        return building;
    }

    public void Register(Vector2Int cell, FactoryBuilding building)
    {
        buildings[cell] = building;
    }

    public void Unregister(Vector2Int cell)
    {
        buildings.Remove(cell);
    }

    /// <summary>Every distinct building on the grid (multi-cell buildings appear once). Returns a copy.</summary>
    public List<FactoryBuilding> GetAllBuildings()
    {
        return new List<FactoryBuilding>(new HashSet<FactoryBuilding>(buildings.Values));
    }
}
