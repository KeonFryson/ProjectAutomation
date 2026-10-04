using System;
using UnityEngine;

/// <summary>
/// Slices Resources/ConveyorArrows.png into sprites at runtime, the same way
/// SquareSpriteFactory builds its square, so no manual sprite slicing is needed.
///
/// Sheet layout: 12 rows (top to bottom) of 32 frames, each 32x32 px.
/// Row names are "incoming_outgoing" travel directions, e.g. "left_up" enters
/// moving left and leaves moving up.
/// </summary>
public static class ConveyorArrowSprites
{
    public const int Frames = 32;
    private const int Tile = 32;

    private static readonly string[] Rows =
    {
        "right", "left", "up", "down",
        "left_up", "right_up", "right_down", "left_down",
        "down_left", "down_right", "up_right", "up_left"
    };

    private static Sprite[][] cache;
    private static bool failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { cache = null; failed = false; }

    /// <summary>Returns null if the art is missing.</summary>
    public static Sprite Get(Direction incoming, Direction outgoing, int frame)
    {
        if (!Load()) return null;

        string key = incoming == outgoing
            ? Name(outgoing)
            : Name(incoming) + "_" + Name(outgoing);

        int row = Array.IndexOf(Rows, key);
        if (row < 0)
        {
            // Opposite directions (belt facing its own feeder): show it as straight.
            row = Array.IndexOf(Rows, Name(outgoing));
        }
        return cache[row][((frame % Frames) + Frames) % Frames];
    }

    private static bool Load()
    {
        if (cache != null) return true;
        if (failed) return false;

        var tex = Resources.Load<Texture2D>("ConveyorArrows");
        if (tex == null)
        {
            Debug.LogWarning("ConveyorArrowSprites: Assets/Resources/ConveyorArrows.png not found, belts will use the plain facing square.");
            failed = true;
            return false;
        }
        if (tex.width != Frames * Tile || tex.height != Rows.Length * Tile)
        {
            Debug.LogWarning("ConveyorArrows.png imported at " + tex.width + "x" + tex.height
                + " instead of " + (Frames * Tile) + "x" + (Rows.Length * Tile)
                + ". Right-click it > Reimport (NPOT scaling must be 'None').");
        }

        cache = new Sprite[Rows.Length][];
        for (int r = 0; r < Rows.Length; r++)
        {
            cache[r] = new Sprite[Frames];
            for (int f = 0; f < Frames; f++)
            {
                var rect = new Rect(f * Tile, tex.height - (r + 1) * Tile, Tile, Tile);
                cache[r][f] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), Tile, 0, SpriteMeshType.FullRect);
            }
        }
        return true;
    }

    private static string Name(Direction d)
    {
        switch (d)
        {
            case Direction.Up: return "up";
            case Direction.Right: return "right";
            case Direction.Down: return "down";
            default: return "left";
        }
    }
}
