using UnityEngine;

/// <summary>
/// A tiny, code-only "particle" burst: a handful of small squares fly outward
/// and fade. Used whenever an item is sold, to make the factory feel alive
/// without needing any imported particle assets.
/// </summary>
public class SellBurstEffect : MonoBehaviour
{
    private const float Lifetime = 0.4f;
    private float age;
    private Vector3 velocity;
    private SpriteRenderer sr;

    public static void Spawn(Vector3 worldPosition, Color color)
    {
        const int particleCount = 5;
        for (int i = 0; i < particleCount; i++)
        {
            var go = new GameObject("SellParticle");
            go.transform.position = worldPosition;
            go.transform.localScale = Vector3.one * 0.15f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SquareSpriteFactory.GetSquareSprite();
            sr.color = color;
            sr.sortingOrder = 20;

            var fx = go.AddComponent<SellBurstEffect>();
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(1f, 2.5f);
            fx.velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;
            fx.sr = sr;
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += velocity * Time.deltaTime;

        if (sr != null)
        {
            Color c = sr.color;
            c.a = Mathf.Lerp(1f, 0f, age / Lifetime);
            sr.color = c;
        }

        if (age >= Lifetime) Destroy(gameObject);
    }
}
