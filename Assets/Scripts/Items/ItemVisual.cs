using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// The small colored square that visually represents one item as it moves along
/// belts and through machines. Instances are pooled: get one with Spawn(),
/// and give it back with Release() instead of Destroy().
/// </summary>
public class ItemVisual : MonoBehaviour
{
    public ItemDefinition Definition { get; private set; }

    private SpriteRenderer sr;

    private static ObjectPool<ItemVisual> pool;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { pool = null; }

    private static ObjectPool<ItemVisual> Pool =>
        pool ??= new ObjectPool<ItemVisual>(
            createFunc: CreateNew,
            actionOnGet: v => v.gameObject.SetActive(true),
            actionOnRelease: v => { v.Definition = null; v.gameObject.SetActive(false); },
            actionOnDestroy: v => { if (v != null) Destroy(v.gameObject); },
            collectionCheck: true,
            defaultCapacity: 32,
            maxSize: 1000);

    private static ItemVisual CreateNew()
    {
        var go = new GameObject("Item");
        go.transform.SetParent(PoolRoot.Get(), false);
        go.transform.localScale = Vector3.one * 0.45f;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = SquareSpriteFactory.GetSquareSprite();
        renderer.sortingOrder = 10;

        var visual = go.AddComponent<ItemVisual>();
        visual.sr = renderer;
        return visual;
    }

    public static ItemVisual Spawn(ItemDefinition definition, Vector3 worldPosition)
    {
        var visual = Pool.Get();
        visual.Definition = definition;
        visual.transform.position = worldPosition;
        visual.sr.color = definition != null ? definition.color : Color.white;
        return visual;
    }

    /// <summary>Return this item to the pool. Use instead of Destroy(gameObject).</summary>
    public void Release()
    {
        Pool.Release(this);
    }
}