#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives every GameDefinition asset (items, buildings) an ID automatically:
///  - new assets (Create menu, Content Editor, duplicates) get the next free ID when imported
///  - existing assets without an ID are fixed after every script reload
///  - a duplicated asset that copied its original's ID gets a fresh one
/// Menu: Box Factory > Assign Missing IDs
/// </summary>
[InitializeOnLoad]
public class IdAssigner : AssetPostprocessor
{
    static IdAssigner()
    {
        EditorApplication.delayCall += () => AssignAllMissing(false);
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var paths = new List<string>();
        foreach (var p in imported) if (p.EndsWith(".asset")) paths.Add(p);
        if (paths.Count == 0) return;

        EditorApplication.delayCall += () => { foreach (var p in paths) EnsureUnique(p); };
    }

    [MenuItem("Box Factory/Assign Missing IDs")]
    public static void AssignMenu() { AssignAllMissing(true); }

    private static List<GameDefinition> LoadAll()
    {
        var list = new List<GameDefinition>();
        foreach (var t in TypeCache.GetTypesDerivedFrom<GameDefinition>())
        {
            if (t.IsAbstract) continue;
            foreach (string guid in AssetDatabase.FindAssets("t:" + t.Name))
            {
                var a = AssetDatabase.LoadAssetAtPath<GameDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null && a.GetType() == t) list.Add(a);
            }
        }
        list.Sort((x, y) => string.CompareOrdinal(AssetDatabase.GetAssetPath(x), AssetDatabase.GetAssetPath(y)));
        return list;
    }

    private static string NextId(List<GameDefinition> all, string prefix)
    {
        int max = 0;
        foreach (var a in all)
        {
            if (a.IdPrefix != prefix || string.IsNullOrEmpty(a.Id) || !a.Id.StartsWith(prefix)) continue;
            if (int.TryParse(a.Id.Substring(prefix.Length), out int n) && n > max) max = n;
        }
        return prefix + (max + 1).ToString("00");
    }

    private static void SetId(GameDefinition def, string id)
    {
        var so = new SerializedObject(def);
        so.FindProperty("id").stringValue = id;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(def);
        AssetDatabase.SaveAssetIfDirty(def);
    }

    /// <summary>Assigns an ID to this asset if it has none or shares one with another asset.</summary>
    public static bool EnsureUnique(string assetPath)
    {
        var def = AssetDatabase.LoadAssetAtPath<GameDefinition>(assetPath);
        if (def == null) return false;

        var all = LoadAll();
        bool needs = string.IsNullOrEmpty(def.Id);
        if (!needs)
        {
            foreach (var other in all)
                if (other != def && other.IdPrefix == def.IdPrefix && other.Id == def.Id) { needs = true; break; }
        }
        if (!needs) return false;

        string id = NextId(all, def.IdPrefix);
        SetId(def, id);
        Debug.Log("IdAssigner: '" + def.name + "' is now " + id, def);
        return true;
    }

    public static void AssignAllMissing(bool log)
    {
        var all = LoadAll();
        int changed = 0;

        foreach (var def in all)
        {
            if (!string.IsNullOrEmpty(def.Id)) continue;
            SetId(def, NextId(all, def.IdPrefix));
            changed++;
        }

        // Report duplicates (can't know which is the original, so just warn).
        var seen = new Dictionary<string, GameDefinition>();
        foreach (var def in all)
        {
            if (string.IsNullOrEmpty(def.Id)) continue;
            string key = def.IdPrefix + "|" + def.Id;
            if (seen.TryGetValue(key, out var first))
                Debug.LogWarning("IdAssigner: '" + def.name + "' and '" + first.name + "' share ID " + def.Id +
                                 ". Re-import the copy (or clear its ID) to get a new one.", def);
            else seen[key] = def;
        }

        if (log || changed > 0) Debug.Log("IdAssigner: assigned " + changed + " missing ID(s).");
    }
}
#endif