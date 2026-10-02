using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Math for buildings that cover more than one grid cell.
///
/// A building's Size is (length along its facing, width across it).
/// Its "anchor" cell is the back-right corner when looking in the facing
/// direction; cells extend forward (length) and to the left (width).
/// Because the footprint is expressed relative to the facing, rotating a
/// building rotates its footprint with it.
/// </summary>
public static class Footprint
{
    public static Vector2Int ClampSize(Vector2Int s)
    {
        return new Vector2Int(Mathf.Max(1, s.x), Mathf.Max(1, s.y));
    }

    public static Vector2Int Left(Direction d)
    {
        Vector2Int f = DirectionUtil.ToVector(d);
        return new Vector2Int(-f.y, f.x);
    }

    public static void GetCells(Vector2Int anchor, Vector2Int size, Direction facing, List<Vector2Int> result)
    {
        result.Clear();
        Vector2Int f = DirectionUtil.ToVector(facing);
        Vector2Int l = Left(facing);
        for (int i = 0; i < size.x; i++)
            for (int j = 0; j < size.y; j++)
                result.Add(anchor + f * i + l * j);
    }

    private static Vector2Int CenterOffset(Vector2Int size)
    {
        return new Vector2Int((size.x - 1) / 2, (size.y - 1) / 2);
    }

    /// <summary>The footprint cell closest to the middle (used to keep position when rotating).</summary>
    public static Vector2Int CenterCell(Vector2Int anchor, Vector2Int size, Direction facing)
    {
        Vector2Int o = CenterOffset(size);
        return anchor + DirectionUtil.ToVector(facing) * o.x + Left(facing) * o.y;
    }

    /// <summary>Anchor that puts the footprint's center cell on 'centerCell' (e.g. under the mouse).</summary>
    public static Vector2Int AnchorFromCenter(Vector2Int centerCell, Vector2Int size, Direction facing)
    {
        Vector2Int o = CenterOffset(size);
        return centerCell - DirectionUtil.ToVector(facing) * o.x - Left(facing) * o.y;
    }

    /// <summary>The cell just in front of the middle of the building's front edge: where output goes.</summary>
    public static Vector2Int OutputCell(Vector2Int anchor, Vector2Int size, Direction facing)
    {
        Vector2Int o = CenterOffset(size);
        return anchor + DirectionUtil.ToVector(facing) * size.x + Left(facing) * o.y;
    }

    /// <summary>Footprint extent along world X / Y.</summary>
    public static Vector2Int WorldSize(Vector2Int size, Direction facing)
    {
        bool vertical = facing == Direction.Up || facing == Direction.Down;
        return vertical ? new Vector2Int(size.y, size.x) : size;
    }

    public static Vector3 WorldCenter(Vector2Int anchor, Vector2Int size, Direction facing, float cellSize)
    {
        Vector2 f = (Vector2)DirectionUtil.ToVector(facing);
        Vector2 l = (Vector2)Left(facing);
        Vector2 c = (Vector2)anchor + f * ((size.x - 1) * 0.5f) + l * ((size.y - 1) * 0.5f);
        return new Vector3(c.x * cellSize, c.y * cellSize, 0f);
    }
}
