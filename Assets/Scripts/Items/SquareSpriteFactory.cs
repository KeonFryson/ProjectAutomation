using UnityEngine;

/// <summary>
/// Generates one small reusable white square sprite at runtime.
/// Every building and item in Box Factory reuses this same sprite and is
/// colored with SpriteRenderer.color, which is exactly how the game achieves
/// its "everything is a colored square" minimalist look without any art files.
/// </summary>
public static class SquareSpriteFactory
{
    private static Sprite cachedSprite;

    public static Sprite GetSquareSprite()
    {
        if (cachedSprite != null) return cachedSprite;

        const int size = 4;
        var texture = new Texture2D(size, size)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply();

        cachedSprite = Sprite.Create(
            texture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            size); // pixelsPerUnit == size => sprite is exactly 1x1 world units

        return cachedSprite;
    }
}
