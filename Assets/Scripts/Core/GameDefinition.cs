using UnityEngine;

/// <summary>
/// Base for assets that have a stable ID (items = I01.., buildings = B01..).
/// The ID is assigned automatically in the editor (see IdAssigner) and never shown as editable.
/// </summary>
public abstract class GameDefinition : ScriptableObject
{
    [SerializeField, HideInInspector] private string id;

    /// <summary>Stable ID like "I01" / "B03". Empty only until the editor has assigned one.</summary>
    public string Id => id;

    /// <summary>"I" for items, "B" for buildings, ...</summary>
    public abstract string IdPrefix { get; }

    /// <summary>What gets written to save files: the ID (asset name only as an emergency fallback).</summary>
    public string SaveKey => string.IsNullOrEmpty(id) ? name : id;

    /// <summary>True if key is this asset's ID, or (for old saves) its asset name.</summary>
    public bool Matches(string key)
    {
        return !string.IsNullOrEmpty(key) && (key == id || key == name);
    }
}