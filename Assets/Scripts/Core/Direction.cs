using UnityEngine;

/// <summary>
/// The four cardinal facings a building (belt, miner, processor, seller) can have.
/// Order matters: it is used to rotate clockwise with simple modulo arithmetic.
/// </summary>
public enum Direction
{
    Up,
    Right,
    Down,
    Left
}

public static class DirectionUtil
{
    public static Vector2Int ToVector(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return new Vector2Int(0, 1);
            case Direction.Right: return new Vector2Int(1, 0);
            case Direction.Down: return new Vector2Int(0, -1);
            case Direction.Left: return new Vector2Int(-1, 0);
            default: return Vector2Int.zero;
        }
    }

    public static float ToAngle(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return 90f;
            case Direction.Right: return 0f;
            case Direction.Down: return -90f;
            case Direction.Left: return 180f;
            default: return 0f;
        }
    }

    public static Direction RotateClockwise(Direction dir)
    {
        return (Direction)(((int)dir + 1) % 4);
    }
}
