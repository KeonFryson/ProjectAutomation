using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// A tiny, code-only "particle" burst: a handful of small squares fly outward
/// and fade. Particles are pooled and returned when their lifetime ends.
/// </summary>
public class SellBurstEffect : MonoBehaviour
{
    private const float Lifetime = 0.4f;
    private float age;
    private Vector3 velocity;
    private Color baseColor;
    private SpriteRenderer sr;

    private static ObjectPool<SellBurstEffect> pool;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { pool = null; }

    private static ObjectPool<SellBurstEffect> Pool =>
        pool ??= new ObjectPool<SellBurstEffect>(
            createFunc: CreateNew,
            actionOnGet: p => p.gameObject.SetActive(true),
            actionOnRelease: p => p.gameObject.SetActive(false),
            actionOnDestroy: p => { if (p != null) Destroy(p.gameObject); },
            collectionCheck: true,
            defaultCapacity: 50,
            maxSize: 500);

    private static SellBurstEffect CreateNew()
    {
        var go = new GameObject("SellParticle");
        go.transform.SetParent(PoolRoot.Get(), false);
        go.transform.localScale = Vector3.one * 0.15f;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = SquareSpriteFactory.GetSquareSprite();
        renderer.sortingOrder = 20;

        var fx = go.AddComponent<SellBurstEffect>();
        fx.sr = renderer;
        return fx;
    }

    public static void Spawn(Vector3 worldPosition, Color color)
    {
        const int particleCount = 5;
        for (int i = 0; i < particleCount; i++)
        {
            var fx = Pool.Get();
            fx.transform.position = worldPosition;
            fx.age = 0f;
            fx.baseColor = color;
            fx.sr.color = color;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(1f, 2.5f);
            fx.velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += velocity * Time.deltaTime;

        Color c = baseColor;
        c.a = Mathf.Lerp(1f, 0f, age / Lifetime);
        sr.color = c;

        if (age >= Lifetime) Pool.Release(this);
    }
}