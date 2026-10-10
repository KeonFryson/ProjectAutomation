#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: Box Factory > Build Full Progression
///
/// Builds (or updates) the whole game progression in one click. Safe to run again: existing
/// assets are found by name and updated, nothing is duplicated.
///
///  - Science Pack items (Automation, Logistics, Chemical, Quantum) and the Portal Core item
///  - Multi-item recipes: packs and Portal Core
///  - Buildings + prefabs: Science Assembler, Fabricator, Portal
///  - The full tech tree (costs, prerequisites, unlocks) using science packs
///  - Registers everything in the BuildManager / ResearchManager of the game scene
///  - Checks that the whole tree can actually be completed
///
/// Tweak the DESIGN DATA section below to rebalance, then run it again.
/// </summary>
public static class ProgressionBuilder
{
    private const string Root = "Assets/BoxFactory";
    private const string GameScenePath = "Assets/Scenes/" + SaveSystem.GameScene + ".unity";

    // =================================================================
    // DESIGN DATA
    // =================================================================

    private struct Amt
    {
        public string Item; public int Amount;
        public Amt(string item, int amount) { Item = item; Amount = amount; }
    }

    private static Amt S(string item, int amount) { return new Amt(item, amount); }

    private struct ItemSpec
    {
        public string Name; public Color Color;
        public ItemSpec(string name, Color color) { Name = name; Color = color; }
    }

    private class RecipeSpec
    {
        public float Time; public Amt[] In; public Amt[] Out;
        public string Name
        {
            get { return string.Join("+", In.Select(a => a.Item)) + "_to_" + string.Join("+", Out.Select(a => a.Item)); }
        }
    }

    private class TechSpec
    {
        public string Name, Desc;
        public string[] Prereq; public Amt[] Cost; public string[] Unlocks;
    }

    private static RecipeSpec R(float time, Amt[] i, Amt[] o) { return new RecipeSpec { Time = time, In = i, Out = o }; }

    private static TechSpec T(string name, string desc, string[] prereq, Amt[] cost, params string[] unlocks)
    {
        return new TechSpec { Name = name, Desc = desc, Prereq = prereq, Cost = cost, Unlocks = unlocks };
    }

    private static string[] P(params string[] names) { return names; }
    private static Amt[] C(params Amt[] amts) { return amts; }

    private const string A = "Automation Pack", L = "Logistics Pack", Ch = "Chemical Pack", Q = "Quantum Pack";

    private static readonly ItemSpec[] NewItems =
    {
        new ItemSpec(A, new Color(0.90f, 0.25f, 0.25f)),
        new ItemSpec(L, new Color(0.30f, 0.85f, 0.35f)),
        new ItemSpec(Ch, new Color(0.30f, 0.55f, 1.00f)),
        new ItemSpec(Q, new Color(0.75f, 0.35f, 1.00f)),
        new ItemSpec("Portal Core", new Color(0.20f, 1.00f, 0.95f)),
    };

    // IMPORTANT: recipes of ONE machine must not share an input item (the first item that
    // arrives picks the recipe). The science recipes below use four disjoint input pairs.
    private static readonly RecipeSpec[] SciencePackRecipes =
    {
        R(4f, new[] { S("Gear", 1), S("Copper Wire", 1) },        new[] { S(A, 1) }),
        R(5f, new[] { S("Iron Plate", 1), S("Circuit", 1) },      new[] { S(L, 1) }),
        R(6f, new[] { S("Lens", 1), S("Silicon", 1) },            new[] { S(Ch, 1) }),
        R(8f, new[] { S("Focus Crystal", 1), S("Crystal Shard", 1) }, new[] { S(Q, 1) }),
    };

    private static readonly RecipeSpec PortalCoreRecipe =
        R(10f, new[] { S("Focus Crystal", 2), S("Lens", 2), S("Circuit", 2), S("Gear", 1) }, new[] { S("Portal Core", 1) });

    /// <summary>What the Portal needs. Change the numbers here to rebalance the ending.</summary>
    private static readonly Amt[] PortalRequirements = { S("Portal Core", 10), S("Focus Crystal", 50) };

    private static readonly string[] DefaultUnlocked = { "Belt Mk1", "Miner Mk1", "Splitter", "Research Lab Mk1" };

    private static readonly TechSpec[] Techs =
    {
        T("Smelting", "Melt ore into bars.", P(),
            C(S("Iron Ore", 10)), "Smelter Mk1"),
        T("Metal Pressing", "Press bars into plates and wire.", P("Smelting"),
            C(S("Iron Bar", 20), S("Copper Bar", 20)), "Press Mk1"),
        T("Assembly", "Machines that combine parts.", P("Metal Pressing"),
            C(S("Iron Plate", 20), S("Copper Wire", 20)), "Assembler Mk1"),
        T("Research Automation", "Craft Science Packs from several ingredients at once.", P("Assembly"),
            C(S("Gear", 20), S("Circuit", 20)), "Science Assembler"),

        T("Improved Mining", "Faster miners.", P("Research Automation"),
            C(S(A, 20)), "Miner Mk2"),
        T("Fast Belts", "Faster conveyor belts.", P("Research Automation"),
            C(S(A, 20)), "Belt Mk2"),
        T("Advanced Research", "Labs that research faster.", P("Research Automation"),
            C(S(A, 30)), "Research Lab Mk2"),

        T("Industrial Machinery", "Faster smelters, presses and assemblers.", P("Improved Mining", "Assembly"),
            C(S(A, 40), S(L, 20)), "Press Mk2", "Smelter Mk2", "Assembler Mk2"),
        T("Deep Mining", "Mine quartz and crystal ore.", P("Improved Mining"),
            C(S(A, 30), S(L, 30)), "Miner Mk3"),
        T("Express Belts", "The fastest belts.", P("Fast Belts", "Deep Mining"),
            C(S(A, 30), S(L, 30)), "Belt Mk3"),

        T("Chemical Refining", "Refine quartz and crystals.", P("Deep Mining"),
            C(S(A, 40), S(L, 40), S("Quartz Ore", 40)), "Refinery"),
        T("High-Throughput Lab", "The fastest research lab.", P("Advanced Research", "Chemical Refining"),
            C(S(A, 50), S(L, 50), S(Ch, 30)), "Research Lab Mk3"),
        T("Crystal Resonance", "Fabricate Portal Cores.", P("Chemical Refining"),
            C(S(A, 50), S(L, 50), S(Ch, 40), S(Q, 20)), "Fabricator"),

        T("Portal Theory", "Everything you learned, focused into one gate.",
            P("Industrial Machinery", "High-Throughput Lab", "Crystal Resonance"),
            C(S(A, 100), S(L, 100), S(Ch, 100), S(Q, 50)), "Portal"),
    };

    // =================================================================
    // STATE
    // =================================================================

    private static int created, updated;
    private static readonly List<string> problems = new List<string>();
    private static readonly Dictionary<Type, Dictionary<string, UnityEngine.Object>> cache =
        new Dictionary<Type, Dictionary<string, UnityEngine.Object>>();

    // =================================================================
    // MENU ITEMS
    // =================================================================

    [MenuItem("Box Factory/Reset Tutorial")]
    public static void ResetTutorial()
    {
        TutorialManager.ResetFlag();
        EditorUtility.DisplayDialog("Tutorial", "The tutorial will show again on the next new game.", "OK");
    }

    [MenuItem("Box Factory/Build Full Progression")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Full Progression", "Leave Play mode first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Build Full Progression",
                "Creates or updates science pack items, multi-item recipes, the Science Assembler, Fabricator and Portal " +
                "(prefabs + definitions) and rewrites the whole tech tree, then registers everything in the game scene.\n\n" +
                "Existing assets are reused by name. Tech costs, prerequisites and unlocks are overwritten.",
                "Build", "Cancel"))
            return;

        created = 0; updated = 0;
        problems.Clear();
        cache.Clear();

        try
        {
            Run();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            problems.Add("Exception: " + e.Message);
        }
        finally
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Report();
    }

    // =================================================================
    // MAIN PIPELINE
    // =================================================================

    private static void Run()
    {
        // 1. Items
        foreach (var s in NewItems) EnsureItem(s);

        // 2. Recipes
        var recipes = new Dictionary<string, RecipeDefinition>();
        foreach (var r in SciencePackRecipes) recipes[r.Name] = EnsureRecipe(r);
        recipes[PortalCoreRecipe.Name] = EnsureRecipe(PortalCoreRecipe);

        // 3. Prefabs
        var sciPrefab = EnsurePrefab<Processor>("Science Assembler", p =>
            p.recipes = SciencePackRecipes.Select(r => recipes[r.Name]).Where(r => r != null).ToList());

        var fabPrefab = EnsurePrefab<Processor>("Fabricator", p =>
            p.recipes = new List<RecipeDefinition> { recipes[PortalCoreRecipe.Name] });

        var portalPrefab = EnsurePrefab<Portal>("Portal", p =>
        {
            p.requirements = new List<ItemAmount>();
            foreach (var a in PortalRequirements)
            {
                var item = Item(a.Item);
                if (item != null) p.requirements.Add(new ItemAmount { item = item, amount = a.Amount });
            }
        });

        // 4. Building definitions
        EnsureBuilding("Science Assembler", "Production", new Vector2Int(1, 1), 1f, new Color(0.90f, 0.45f, 0.45f),
            "Crafts Science Packs from several ingredients at once.", sciPrefab);
        EnsureBuilding("Fabricator", "Production", new Vector2Int(2, 2), 1f, new Color(0.55f, 0.45f, 0.90f),
            "Large machine that crafts Portal Cores from four different ingredients.", fabPrefab);
        EnsureBuilding("Portal", "Endgame", new Vector2Int(3, 3), 1f, new Color(0.20f, 0.90f, 0.95f),
            "Deliver Portal Cores and Focus Crystals to activate the Portal and win the game.", portalPrefab);

        // Only the starter buildings are unlocked from the start; the tech tree gates the rest.
        var starters = new HashSet<string>(DefaultUnlocked.Select(Norm));
        foreach (var def in LoadAll<BuildingDefinition>())
        {
            bool should = starters.Contains(Norm(def.displayName)) || starters.Contains(Norm(def.name));
            if (def.unlockedByDefault != should)
            {
                def.unlockedByDefault = should;
                EditorUtility.SetDirty(def);
                updated++;
            }
        }

        // 5. Techs: create all, then wire prerequisites / costs / unlocks
        foreach (var spec in Techs) EnsureTech(spec);
        foreach (var spec in Techs) WireTech(spec);

        AssetDatabase.SaveAssets();

        // 6. Scene
        RegisterInScene();

        // 7. Can the player actually finish the game?
        Verify();
    }

    // =================================================================
    // ASSET HELPERS
    // =================================================================

    private static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static IEnumerable<string> Keys(UnityEngine.Object o)
    {
        string n = o.name;
        if (n.StartsWith("Item_")) n = n.Substring(5);
        yield return Norm(n);

        switch (o)
        {
            case ItemDefinition i: yield return Norm(i.itemName); break;
            case BuildingDefinition b: yield return Norm(b.displayName); break;
            case TechDefinition t: yield return Norm(t.displayName); break;
        }
    }

    private static Dictionary<string, UnityEngine.Object> Index<T>() where T : UnityEngine.Object
    {
        if (cache.TryGetValue(typeof(T), out var idx)) return idx;

        idx = new Dictionary<string, UnityEngine.Object>();
        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a == null) continue;
            foreach (string k in Keys(a)) if (!idx.ContainsKey(k)) idx[k] = a;
        }
        cache[typeof(T)] = idx;
        return idx;
    }

    private static T Find<T>(string name) where T : UnityEngine.Object
    {
        Index<T>().TryGetValue(Norm(name), out var o);
        return o as T;
    }

    private static void Remember<T>(T asset) where T : UnityEngine.Object
    {
        var idx = Index<T>();
        foreach (string k in Keys(asset)) idx[k] = asset;
    }

    private static List<T> LoadAll<T>() where T : UnityEngine.Object
    {
        var list = new List<T>();
        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) list.Add(a);
        }
        return list;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    private static T CreateAsset<T>(string folder, string fileName) where T : ScriptableObject
    {
        EnsureFolder(folder);
        var a = ScriptableObject.CreateInstance<T>();
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName + ".asset");
        AssetDatabase.CreateAsset(a, path);
        created++;
        return a;
    }

    private static ItemDefinition Item(string name)
    {
        var item = Find<ItemDefinition>(name);
        if (item == null) problems.Add("Item '" + name + "' not found.");
        return item;
    }

    // =================================================================
    // ITEMS / RECIPES
    // =================================================================

    private static ItemDefinition EnsureItem(ItemSpec s)
    {
        var item = Find<ItemDefinition>(s.Name);
        if (item != null) return item;

        item = CreateAsset<ItemDefinition>(Root + "/Data/Items", s.Name);
        item.itemName = s.Name;
        item.color = s.Color;
        Remember(item);
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssets();
        IdAssigner.EnsureUnique(AssetDatabase.GetAssetPath(item));
        return item;
    }

    private static List<ItemStack> ToStacks(Amt[] amts)
    {
        var list = new List<ItemStack>();
        foreach (var a in amts)
        {
            var item = Item(a.Item);
            if (item != null) list.Add(new ItemStack(item, a.Amount));
        }
        return list;
    }

    private static RecipeDefinition EnsureRecipe(RecipeSpec s)
    {
        string name = s.Name;
        var r = Find<RecipeDefinition>(name);
        if (r == null)
        {
            r = CreateAsset<RecipeDefinition>(Root + "/Data/Recipes", name);
            Remember(r);
        }
        else updated++;

        r.inputs = ToStacks(s.In);
        r.outputs = ToStacks(s.Out);
        r.processTime = s.Time;
        r.inputItem = null;
        r.outputItem = null;
        EditorUtility.SetDirty(r);

        if (!r.IsValid) problems.Add("Recipe '" + name + "' is invalid (missing item).");
        return r;
    }

    // =================================================================
    // PREFABS / BUILDINGS
    // =================================================================

    private static T EnsurePrefab<T>(string name, Action<T> configure) where T : FactoryBuilding
    {
        EnsureFolder(Root + "/Prefabs");
        string path = Root + "/Prefabs/" + name + ".prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            var go = new GameObject(name);
            go.AddComponent<SpriteRenderer>();
            var comp = go.AddComponent<T>();
            configure(comp);
            PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            created++;
        }
        else
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            var comp = contents.GetComponent<T>();
            if (comp == null) comp = contents.AddComponent<T>();
            configure(comp);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            updated++;
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<T>();
    }

    private static BuildingDefinition EnsureBuilding(string name, string category, Vector2Int size, float speed,
        Color color, string desc, FactoryBuilding prefab)
    {
        var def = Find<BuildingDefinition>(name);
        bool isNew = def == null;
        if (isNew)
        {
            def = CreateAsset<BuildingDefinition>(Root + "/Data/Buildings", name);
            def.displayName = name;
            def.iconColor = color;
            def.speedMultiplier = speed;
            def.unlockedByDefault = false;
            Remember(def);
        }
        else updated++;

        def.category = category;
        def.prefab = prefab;
        def.size = size;
        def.description = desc;
        EditorUtility.SetDirty(def);

        if (isNew)
        {
            AssetDatabase.SaveAssets();
            IdAssigner.EnsureUnique(AssetDatabase.GetAssetPath(def));
        }
        return def;
    }

    // =================================================================
    // TECHS
    // =================================================================

    private static void EnsureTech(TechSpec spec)
    {
        var t = Find<TechDefinition>(spec.Name);
        if (t == null)
        {
            t = CreateAsset<TechDefinition>(Root + "/Data/Techs", spec.Name);
            t.displayName = spec.Name;
            Remember(t);
        }
        else updated++;
        t.displayName = spec.Name;
        t.description = spec.Desc;
        EditorUtility.SetDirty(t);
    }

    private static void WireTech(TechSpec spec)
    {
        var t = Find<TechDefinition>(spec.Name);
        if (t == null) return;

        t.prerequisites = new List<TechDefinition>();
        foreach (string p in spec.Prereq)
        {
            var pre = Find<TechDefinition>(p);
            if (pre != null) t.prerequisites.Add(pre);
            else problems.Add("Tech '" + spec.Name + "': prerequisite '" + p + "' not found.");
        }

        t.cost = new List<ItemAmount>();
        foreach (var c in spec.Cost)
        {
            var item = Item(c.Item);
            if (item != null) t.cost.Add(new ItemAmount { item = item, amount = c.Amount });
        }

        t.unlocks = new List<BuildingDefinition>();
        foreach (string u in spec.Unlocks)
        {
            var b = Find<BuildingDefinition>(u);
            if (b != null) t.unlocks.Add(b);
            else problems.Add("Tech '" + spec.Name + "': building '" + u + "' not found.");
        }

        EditorUtility.SetDirty(t);
    }

    // =================================================================
    // SCENE
    // =================================================================

    private static bool EnsureGameScene()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BuildManager>() != null) return true;

        if (!System.IO.File.Exists(GameScenePath))
        {
            problems.Add("Game scene not found at " + GameScenePath + ". Open it and run the tool again.");
            return false;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            problems.Add("Scene registration cancelled.");
            return false;
        }

        EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        return UnityEngine.Object.FindFirstObjectByType<BuildManager>() != null;
    }

    private static void RegisterInScene()
    {
        if (!EnsureGameScene()) return;

        var bm = UnityEngine.Object.FindFirstObjectByType<BuildManager>();
        var rm = UnityEngine.Object.FindFirstObjectByType<ResearchManager>();
        if (bm == null || rm == null)
        {
            problems.Add("BuildManager or ResearchManager missing in the scene (Box Factory > Content Editor > Scene & Validate > Create missing scene managers).");
            return;
        }

        Undo.RecordObject(bm, "Register buildings");
        bm.availableBuildings.RemoveAll(b => b == null);
        foreach (var def in LoadAll<BuildingDefinition>())
            if (!bm.availableBuildings.Contains(def)) bm.availableBuildings.Add(def);
        EditorUtility.SetDirty(bm);

        Undo.RecordObject(rm, "Register techs");
        var ordered = new List<TechDefinition>();
        foreach (var spec in Techs)
        {
            var t = Find<TechDefinition>(spec.Name);
            if (t != null && !ordered.Contains(t)) ordered.Add(t);
        }
        foreach (var t in LoadAll<TechDefinition>())
            if (!ordered.Contains(t)) ordered.Add(t);
        rm.allTechs = ordered;
        EditorUtility.SetDirty(rm);

        var scene = bm.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // =================================================================
    // VERIFY: simulate a full playthrough of the tech tree
    // =================================================================

    private static HashSet<ItemDefinition> Obtainable(HashSet<BuildingDefinition> unlocked)
    {
        var items = new HashSet<ItemDefinition>();
        foreach (var b in unlocked)
        {
            if (b == null || !(b.prefab is Miner m)) continue;
            foreach (var i in m.availableItems) if (i != null) items.Add(i);
            if (m.producedItem != null) items.Add(m.producedItem);
        }

        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var b in unlocked)
            {
                if (b == null || !(b.prefab is Processor p)) continue;
                foreach (var r in p.GetAllRecipes())
                {
                    if (!r.IsValid || !r.Inputs.All(s => items.Contains(s.item))) continue;
                    foreach (var o in r.Outputs) if (items.Add(o.item)) grew = true;
                }
            }
        }
        return items;
    }

    private static void Verify()
    {
        var techs = LoadAll<TechDefinition>();
        var buildings = LoadAll<BuildingDefinition>();

        var unlocked = new HashSet<BuildingDefinition>(buildings.Where(b => b.unlockedByDefault));
        var done = new HashSet<TechDefinition>();

        bool changed = true;
        while (changed)
        {
            changed = false;
            var items = Obtainable(unlocked);
            foreach (var t in techs)
            {
                if (done.Contains(t)) continue;
                if (t.prerequisites.Any(p => p != null && !done.Contains(p))) continue;
                if (t.cost.Any(c => c != null && c.item != null && !items.Contains(c.item))) continue;

                done.Add(t);
                foreach (var b in t.unlocks) if (b != null) unlocked.Add(b);
                changed = true;
            }
        }

        foreach (var t in techs)
            if (!done.Contains(t)) problems.Add("Tech '" + t.displayName + "' can never be completed (missing prerequisite or unobtainable cost item).");

        var portal = buildings.FirstOrDefault(b => b.prefab is Portal);
        if (portal == null) problems.Add("No Portal building exists.");
        else if (!unlocked.Contains(portal)) problems.Add("The Portal cannot be unlocked.");
        else
        {
            var items = Obtainable(unlocked);
            foreach (var req in ((Portal)portal.prefab).requirements)
                if (req != null && req.item != null && !items.Contains(req.item))
                    problems.Add("Portal needs '" + req.item.itemName + "' but nothing can produce it.");
        }

        foreach (var b in buildings)
            if (!b.unlockedByDefault && !unlocked.Contains(b))
                problems.Add("Building '" + b.displayName + "' is never unlocked.");
    }

    // =================================================================
    // REPORT
    // =================================================================

    private static void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Created " + created + " asset(s), updated " + updated + ".");
        if (problems.Count == 0) sb.AppendLine("Verification passed: every tech can be researched and the Portal can be built.");
        else
        {
            sb.AppendLine(problems.Count + " problem(s):");
            foreach (var p in problems.Distinct()) sb.AppendLine(" - " + p);
        }

        Debug.Log("ProgressionBuilder:\n" + sb);
        EditorUtility.DisplayDialog("Build Full Progression", sb.ToString(), "OK");
    }
}
#endif
