#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies old single-item recipes (inputItem / outputItem) into the new multi-item lists
/// so they show up in the inspector. Runs automatically after script reloads, and can be
/// run by hand: Box Factory > Migrate Recipes. Safe to run repeatedly.
/// </summary>
[InitializeOnLoad]
public static class RecipeMigrator
{
    static RecipeMigrator()
    {
        EditorApplication.delayCall += () => Migrate(false);
    }

    [MenuItem("Box Factory/Migrate Recipes")]
    public static void MigrateMenu() { Migrate(true); }

    private static void Migrate(bool log)
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
        {
            var r = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (r == null || !r.NeedsMigration) continue;
            r.MigrateLegacy();
            EditorUtility.SetDirty(r);
            count++;
        }

        if (count > 0) AssetDatabase.SaveAssets();
        if (log || count > 0) Debug.Log("RecipeMigrator: migrated " + count + " recipe(s).");
    }
}
#endif
