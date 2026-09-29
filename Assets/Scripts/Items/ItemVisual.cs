using UnityEngine;

/// <summary>
/// The small colored square that visually represents one item as it moves along
/// belts and through machines. Buildings own and move these directly; this
/// component just carries data and a sprite.
/// </summary>
public class ItemVisual : MonoBehaviour
{
    public ItemDefinition Definition { get; private set; }

    public static ItemVisual Spawn(ItemDefinition definition, Vector3 worldPosition)
    {
        var go = new GameObject("Item_" + (definition != null ? definition.itemName : "Unknown"));
        go.transform.position = worldPosition;
        go.transform.localScale = Vector3.one * 0.45f;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SquareSpriteFactory.GetSquareSprite();
        sr.color = definition != null ? definition.color : Color.white;
        sr.sortingOrder = 10;

        var visual = go.AddComponent<ItemVisual>();
        visual.Definition = definition;
        return visual;
    }
}
