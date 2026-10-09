using UnityEngine;

/// <summary>
/// One entry in the build menu. Buildings are free to place; they are gated only by the
/// tech tree (unlockedByDefault / TechDefinition.unlocks). A "better" building is simply
/// another BuildingDefinition (e.g. Miner Mk2) with a higher speedMultiplier.
/// Create via: Assets > Create > Box Factory > Building Definition
/// </summary>
[CreateAssetMenu(fileName = "NewBuilding", menuName = "Box Factory/Building Definition")]
public class BuildingDefinition : GameDefinition
{
    public override string IdPrefix => "B";

    public string displayName = "Building";

    [TextArea, Tooltip("Shown in the build menu tooltip.")]
    public string description;

    [Tooltip("Tab this building appears under in the build menu. Tabs are hidden if every unlocked building shares one category.")]
    public string category = "Buildings";

    [Tooltip("Prefab must have a FactoryBuilding-derived component (Miner, ConveyorBelt, Processor, Splitter, ResearchLab).")]
    public FactoryBuilding prefab;

    [Tooltip("Footprint in grid cells: X = length along the facing direction, Y = width across it. (1,1) is a normal single tile.")]
    public Vector2Int size = Vector2Int.one;

    [Tooltip("Multiplies this building's speed: belt travel speed, miner/processor output rate, lab research rate. Higher tiers use bigger numbers.")]
    public float speedMultiplier = 1f;

    [Tooltip("Tick for starter buildings. Everything else must be unlocked by a TechDefinition.")]
    public bool unlockedByDefault = true;

    [Tooltip("Color of the building's square in the world and on its build menu icon. Used when no sprite is set.")]
    public Color iconColor = Color.gray;

    [Header("Sprite (optional)")]
    [Tooltip("Optional artwork. Stretched to fill the building's whole footprint. Leave empty to use the colored square.")]
    public Sprite sprite;

    [Tooltip("Draw the sprite facing RIGHT and rotate it with the building (good for belts, miners, etc). Untick for art that should always stay upright.")]
    public bool rotateSpriteWithFacing = true;

    [Header("Directional sprites (optional)")]
    [Tooltip("Drawn when the building faces Up. Overrides the default sprite for that direction (no rotation applied). Leave empty to fall back to the default sprite.")]
    public Sprite spriteUp;
    [Tooltip("Drawn when the building faces Right. Leave empty to fall back to the default sprite.")]
    public Sprite spriteRight;
    [Tooltip("Drawn when the building faces Down. Leave empty to fall back to the default sprite.")]
    public Sprite spriteDown;
    [Tooltip("Drawn when the building faces Left. Leave empty to fall back to the default sprite.")]
    public Sprite spriteLeft;

    /// <summary>The sprite made specifically for this facing, or null if none was assigned.</summary>
    public Sprite GetDirectionalSprite(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return spriteUp;
            case Direction.Right: return spriteRight;
            case Direction.Down: return spriteDown;
            case Direction.Left: return spriteLeft;
            default: return null;
        }
    }

    /// <summary>Sprite used for UI icons: the default sprite, else the first directional one found.</summary>
    public Sprite PreviewSprite
    {
        get
        {
            if (sprite != null) return sprite;
            if (spriteRight != null) return spriteRight;
            if (spriteUp != null) return spriteUp;
            if (spriteDown != null) return spriteDown;
            return spriteLeft;
        }
    }
}
