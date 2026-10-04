#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Box Factory content tool.  Menu: Box Factory > Content Editor
///
/// Tabs for Items, Recipes, Buildings and Techs: list, search, create, rename,
/// duplicate, delete and edit assets in one window. Also creates building
/// prefabs, wires items/recipes into Miner/Processor prefabs, registers things
/// in the scene managers, and validates the whole project.
///
/// Put this file in a folder named "Editor" (e.g. Assets/Editor).
/// </summary>
public class BoxFactoryContentEditor : EditorWindow
{
    private enum Tab { Items, Recipes, Buildings, Techs, SceneAndValidate }
    private static readonly string[] TabNames = { "Items", "Recipes", "Buildings", "Techs", "Scene & Validate" };

    // ---- Prefab type dropdown: built automatically from every concrete FactoryBuilding subclass ----

    private static Type[] prefabTypes;
    private static string[] prefabTypeNames;

    private static Type[] PrefabTypes
    {
        get
        {
            if (prefabTypes == null)
                prefabTypes = TypeCache.GetTypesDerivedFrom<FactoryBuilding>()
                    .Where(t => !t.IsAbstract)
                    .OrderBy(t => t.Name)
                    .ToArray();
            return prefabTypes;
        }
    }

    /// <summary>Dropdown labels: "ConveyorBelt" becomes "Conveyor Belt".</summary>
    private static string[] PrefabTypeNames
    {
        get
        {
            if (prefabTypeNames == null)
                prefabTypeNames = PrefabTypes.Select(t => ObjectNames.NicifyVariableName(t.Name)).ToArray();
            return prefabTypeNames;
        }
    }

    private static string CategoryFor(Type t)
    {
        if (t == typeof(ConveyorBelt) || t == typeof(Splitter)) return "Logistics";
        if (t == typeof(Miner) || t == typeof(Processor)) return "Production";
        if (t == typeof(Seller)) return "Selling";
        if (t == typeof(ResearchLab)) return "Research";
        return "Buildings"; // a new building type lands here until you add a line above
    }

    private const string RootPrefKey = "BoxFactory.RootFolder";

    private Tab tab;
    private string rootFolder = "Assets/BoxFactory";
    private string search = "";
    private string newName = "";
    private bool autoRegister = true;
    private bool createPrefab = true;
    private int newPrefabType;

    private Vector2 listScroll, detailScroll, sceneScroll;
    private UnityEngine.Object selected;
    private Editor selectedEditor;
    private Editor prefabEditor;
    private UnityEngine.Object prefabEditorTarget;

    private readonly List<(MessageType type, string text)> issues = new List<(MessageType, string)>();

    [MenuItem("Box Factory/Content Editor")]
    public static void Open()
    {
        var w = GetWindow<BoxFactoryContentEditor>("Box Factory");
        w.minSize = new Vector2(760f, 480f);
    }

    void OnEnable() { rootFolder = EditorPrefs.GetString(RootPrefKey, rootFolder); }
    void OnDisable() { DestroyEditors(); }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static T[] LoadAll<T>() where T : UnityEngine.Object
    {
        return AssetDatabase.FindAssets("t:" + typeof(T).Name)
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null)
            .OrderBy(a => a.name)
            .ToArray();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static Color ColorOf(UnityEngine.Object o)
    {
        switch (o)
        {
            case ItemDefinition i: return i.color;
            case BuildingDefinition b: return b.iconColor;
            case RecipeDefinition r: return r.outputItem != null ? r.outputItem.color : Color.gray;
            case TechDefinition t:
                foreach (var b2 in t.unlocks) if (b2 != null) return b2.iconColor;
                return new Color(0.4f, 0.6f, 0.9f);
        }
        return Color.gray;
    }

    private Type CurrentType
    {
        get
        {
            switch (tab)
            {
                case Tab.Items: return typeof(ItemDefinition);
                case Tab.Recipes: return typeof(RecipeDefinition);
                case Tab.Buildings: return typeof(BuildingDefinition);
                case Tab.Techs: return typeof(TechDefinition);
            }
            return null;
        }
    }

    private string SubFolder
    {
        get
        {
            switch (tab)
            {
                case Tab.Items: return "Items";
                case Tab.Recipes: return "Recipes";
                case Tab.Buildings: return "Buildings";
                default: return "Techs";
            }
        }
    }

    private UnityEngine.Object[] CurrentAssets()
    {
        switch (tab)
        {
            case Tab.Items: return LoadAll<ItemDefinition>();
            case Tab.Recipes: return LoadAll<RecipeDefinition>();
            case Tab.Buildings: return LoadAll<BuildingDefinition>();
            case Tab.Techs: return LoadAll<TechDefinition>();
        }
        return new UnityEngine.Object[0];
    }

    private void DestroyEditors()
    {
        if (selectedEditor != null) DestroyImmediate(selectedEditor);
        if (prefabEditor != null) DestroyImmediate(prefabEditor);
        selectedEditor = null;
        prefabEditor = null;
        prefabEditorTarget = null;
    }

    private void Select(UnityEngine.Object obj)
    {
        DestroyEditors();
        selected = obj;
        if (obj != null) selectedEditor = Editor.CreateEditor(obj);
        GUI.FocusControl(null);
    }

    // ---------------------------------------------------------------
    // Main GUI
    // ---------------------------------------------------------------

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        int newTab = GUILayout.Toolbar((int)tab, TabNames, GUILayout.Height(26));
        if (newTab != (int)tab)
        {
            tab = (Tab)newTab;
            Select(null);
            search = "";
            newName = "";
        }

        EditorGUI.BeginChangeCheck();
        rootFolder = EditorGUILayout.TextField("Output folder", rootFolder);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(RootPrefKey, rootFolder);

        EditorGUILayout.Space(4);

        if (tab == Tab.SceneAndValidate) DrawSceneTab();
        else DrawAssetTab();
    }

    // ---------------------------------------------------------------
    // Asset tabs
    // ---------------------------------------------------------------

    private void DrawAssetTab()
    {
        EditorGUILayout.BeginHorizontal();

        // ---- Left: create + list ----
        EditorGUILayout.BeginVertical(GUILayout.Width(260f));
        DrawCreateBox();

        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);

        listScroll = EditorGUILayout.BeginScrollView(listScroll, "box");
        foreach (var obj in CurrentAssets())
        {
            if (!string.IsNullOrEmpty(search) &&
                obj.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            DrawListRow(obj);
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ---- Right: details ----
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        if (selected == null)
            EditorGUILayout.HelpBox("Select an asset on the left, or create a new one.", MessageType.Info);
        else
            DrawDetails();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawListRow(UnityEngine.Object obj)
    {
        Rect r = EditorGUILayout.GetControlRect(false, 22f);
        if (obj == selected) EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.9f, 0.35f));
        EditorGUI.DrawRect(new Rect(r.x + 2f, r.y + 3f, 16f, 16f), ColorOf(obj));
        GUI.Label(new Rect(r.x + 24f, r.y, r.width - 24f, r.height), obj.name);

        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            Select(obj);
            Event.current.Use();
        }
    }

    private void DrawCreateBox()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Create new " + TabNames[(int)tab].TrimEnd('s'), EditorStyles.boldLabel);
        newName = EditorGUILayout.TextField("Name", newName);

        if (tab == Tab.Buildings)
        {
            createPrefab = EditorGUILayout.Toggle("Also create prefab", createPrefab);
            if (createPrefab) newPrefabType = EditorGUILayout.Popup("Prefab type", newPrefabType, PrefabTypeNames);
        }
        if (tab == Tab.Buildings || tab == Tab.Techs)
            autoRegister = EditorGUILayout.Toggle("Add to scene manager", autoRegister);

        if (GUILayout.Button("Create"))
        {
            CreateNew();
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndVertical();
    }

    private void CreateNew()
    {
        string name = string.IsNullOrWhiteSpace(newName) ? "New" + SubFolder.TrimEnd('s') : newName.Trim();
        string folder = rootFolder + "/Data/" + SubFolder;
        EnsureFolder(folder);

        var asset = ScriptableObject.CreateInstance(CurrentType);
        switch (asset)
        {
            case ItemDefinition i:
                i.itemName = name;
                i.color = Color.HSVToRGB(UnityEngine.Random.value, 0.6f, 0.9f);
                break;
            case BuildingDefinition b:
                b.displayName = name;
                b.iconColor = Color.HSVToRGB(UnityEngine.Random.value, 0.55f, 0.85f);
                if (createPrefab)
                {
                    newPrefabType = Mathf.Clamp(newPrefabType, 0, PrefabTypes.Length - 1);
                    Type prefabType = PrefabTypes[newPrefabType];
                    b.prefab = CreatePrefab(name, prefabType);
                    b.category = CategoryFor(prefabType);
                    if (prefabType == typeof(Splitter)) b.size = new Vector2Int(1, 2); // 1 long, 2 wide
                }
                break;
            case TechDefinition t:
                t.displayName = name;
                break;
        }

        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        if (autoRegister)
        {
            if (asset is BuildingDefinition bd) RegisterBuilding(bd);
            if (asset is TechDefinition td) RegisterTech(td);
        }

        newName = "";
        Select(asset);
    }

    private FactoryBuilding CreatePrefab(string name, Type componentType)
    {
        string folder = rootFolder + "/Prefabs";
        EnsureFolder(folder);

        var go = new GameObject(name);
        go.AddComponent<SpriteRenderer>();
        go.AddComponent(componentType);

        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".prefab");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        DestroyImmediate(go);
        return prefab.GetComponent<FactoryBuilding>();
    }

    private void DrawDetails()
    {
        string path = AssetDatabase.GetAssetPath(selected);

        // Header: rename + actions
        EditorGUILayout.BeginHorizontal();
        string renamed = EditorGUILayout.DelayedTextField("File name", selected.name);
        if (!string.IsNullOrWhiteSpace(renamed) && renamed != selected.name)
        {
            AssetDatabase.RenameAsset(path, renamed);
            AssetDatabase.SaveAssets();
        }
        if (GUILayout.Button("Ping", GUILayout.Width(44f))) EditorGUIUtility.PingObject(selected);
        if (GUILayout.Button("Duplicate", GUILayout.Width(70f)))
        {
            string copy = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CopyAsset(path, copy);
            AssetDatabase.SaveAssets();
            Select(AssetDatabase.LoadMainAssetAtPath(copy));
            GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("Delete", GUILayout.Width(54f)))
        {
            if (EditorUtility.DisplayDialog("Delete asset", "Delete '" + selected.name + "'? References to it will become empty.", "Delete", "Cancel"))
            {
                AssetDatabase.DeleteAsset(path);
                Select(null);
            }
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        // Default inspector (all fields + tooltips)
        EditorGUI.BeginChangeCheck();
        if (selectedEditor != null) selectedEditor.OnInspectorGUI();
        if (EditorGUI.EndChangeCheck()) AssetDatabase.SaveAssets();

        EditorGUILayout.Space(8);
        switch (selected)
        {
            case ItemDefinition item: DrawItemExtras(item); break;
            case RecipeDefinition recipe: DrawRecipeExtras(recipe); break;
            case BuildingDefinition building: DrawBuildingExtras(building); break;
            case TechDefinition tech: DrawTechExtras(tech); break;
        }
    }

    // ---------------------------------------------------------------
    // Per-type extras
    // ---------------------------------------------------------------

    private static IEnumerable<T> PrefabsOf<T>() where T : FactoryBuilding
    {
        return LoadAll<BuildingDefinition>().Select(d => d.prefab).OfType<T>().Distinct();
    }

    private void DrawItemExtras(ItemDefinition item)
    {
        EditorGUILayout.LabelField("Miners that can mine this item", EditorStyles.boldLabel);
        var miners = PrefabsOf<Miner>().ToList();
        if (miners.Count == 0) EditorGUILayout.HelpBox("No Miner prefabs yet. Create a Building with prefab type Miner.", MessageType.None);
        foreach (var m in miners)
        {
            bool has = m.availableItems.Contains(item);
            bool now = EditorGUILayout.ToggleLeft(m.name, has);
            if (now == has) continue;
            Undo.RecordObject(m, "Edit miner items");
            if (now) m.availableItems.Add(item); else m.availableItems.Remove(item);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Used in recipes", EditorStyles.boldLabel);
        bool any = false;
        foreach (var r in LoadAll<RecipeDefinition>())
        {
            if (r.inputItem != item && r.outputItem != item) continue;
            any = true;
            EditorGUILayout.LabelField((r.inputItem == item ? "Input of: " : "Output of: ") + r.name);
        }
        if (!any) EditorGUILayout.LabelField("(none)");
    }

    private void DrawRecipeExtras(RecipeDefinition recipe)
    {
        string inName = recipe.inputItem != null ? recipe.inputItem.itemName : "?";
        string outName = recipe.outputItem != null ? recipe.outputItem.itemName : "?";
        EditorGUILayout.LabelField(inName + "  ->  " + outName + "   (" + recipe.processTime.ToString("0.#") + "s)", EditorStyles.boldLabel);

        if (recipe.inputItem != null && recipe.outputItem != null && GUILayout.Button("Auto-name file from items"))
        {
            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(recipe), recipe.inputItem.itemName + "_to_" + recipe.outputItem.itemName);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Processors that run this recipe", EditorStyles.boldLabel);
        var procs = PrefabsOf<Processor>().ToList();
        if (procs.Count == 0) EditorGUILayout.HelpBox("No Processor prefabs yet. Create a Building with prefab type Processor.", MessageType.None);
        foreach (var p in procs)
        {
            bool has = p.recipes.Contains(recipe);
            bool now = EditorGUILayout.ToggleLeft(p.name, has);
            if (now == has) continue;
            Undo.RecordObject(p, "Edit processor recipes");
            if (now) p.recipes.Add(recipe); else p.recipes.Remove(recipe);
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
        }
    }

    private void DrawBuildingExtras(BuildingDefinition def)
    {
        // Unlock info
        var unlockers = LoadAll<TechDefinition>().Where(t => t.unlocks.Contains(def)).ToList();
        if (def.unlockedByDefault)
            EditorGUILayout.HelpBox("Available from the start.", MessageType.None);
        else if (unlockers.Count == 0)
            EditorGUILayout.HelpBox("Locked and no tech unlocks it. The player can never build this.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Unlocked by: " + string.Join(", ", unlockers.Select(t => t.displayName)), MessageType.None);

        var bm = FindFirstObjectByType<BuildManager>();
        if (bm != null && !bm.availableBuildings.Contains(def) && GUILayout.Button("Add to BuildManager in scene"))
            RegisterBuilding(def);

        EditorGUILayout.Space(6);

        // Prefab
        if (def.prefab == null)
        {
            EditorGUILayout.HelpBox("This definition has no prefab.", MessageType.Warning);
            newPrefabType = EditorGUILayout.Popup("Prefab type", newPrefabType, PrefabTypeNames);
            if (GUILayout.Button("Create prefab"))
            {
                Undo.RecordObject(def, "Create prefab");
                def.prefab = CreatePrefab(def.displayName, PrefabTypes[Mathf.Clamp(newPrefabType, 0, PrefabTypes.Length - 1)]);
                EditorUtility.SetDirty(def);
                AssetDatabase.SaveAssets();
            }
            return;
        }

        EditorGUILayout.LabelField("Prefab settings (" + def.prefab.name + ")", EditorStyles.boldLabel);
        if (prefabEditorTarget != def.prefab)
        {
            if (prefabEditor != null) DestroyImmediate(prefabEditor);
            prefabEditor = Editor.CreateEditor(def.prefab);
            prefabEditorTarget = def.prefab;
        }
        EditorGUI.BeginChangeCheck();
        if (prefabEditor != null) prefabEditor.OnInspectorGUI();
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(def.prefab);
            AssetDatabase.SaveAssets();
        }
    }

    private void DrawTechExtras(TechDefinition tech)
    {
        if (HasCycle(tech, new HashSet<TechDefinition>(), new HashSet<TechDefinition>()))
            EditorGUILayout.HelpBox("Prerequisite loop detected.", MessageType.Error);
        if (tech.cost.Count == 0 || tech.cost.Any(c => c == null || c.item == null))
            EditorGUILayout.HelpBox("Cost is empty or has a missing item (a tech with no cost completes instantly).", MessageType.Warning);
        if (tech.unlocks.Count == 0)
            EditorGUILayout.HelpBox("This tech unlocks nothing.", MessageType.Warning);

        var rm = FindFirstObjectByType<ResearchManager>();
        if (rm != null && !rm.allTechs.Contains(tech) && GUILayout.Button("Add to ResearchManager in scene"))
            RegisterTech(tech);

        var dependents = LoadAll<TechDefinition>().Where(t => t.prerequisites.Contains(tech)).ToList();
        if (dependents.Count > 0)
            EditorGUILayout.LabelField("Leads to: " + string.Join(", ", dependents.Select(t => t.displayName)), EditorStyles.wordWrappedLabel);
    }

    private static bool HasCycle(TechDefinition t, HashSet<TechDefinition> stack, HashSet<TechDefinition> done)
    {
        if (done.Contains(t)) return false;
        if (!stack.Add(t)) return true;
        foreach (var p in t.prerequisites)
            if (p != null && HasCycle(p, stack, done)) return true;
        stack.Remove(t);
        done.Add(t);
        return false;
    }

    // ---------------------------------------------------------------
    // Scene registration
    // ---------------------------------------------------------------

    private static void RegisterBuilding(BuildingDefinition def)
    {
        var bm = FindFirstObjectByType<BuildManager>();
        if (bm == null || bm.availableBuildings.Contains(def)) return;
        Undo.RecordObject(bm, "Add building");
        bm.availableBuildings.Add(def);
        EditorUtility.SetDirty(bm);
        EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
    }

    private static void RegisterTech(TechDefinition tech)
    {
        var rm = FindFirstObjectByType<ResearchManager>();
        if (rm == null || rm.allTechs.Contains(tech)) return;
        Undo.RecordObject(rm, "Add tech");
        rm.allTechs.Add(tech);
        EditorUtility.SetDirty(rm);
        EditorSceneManager.MarkSceneDirty(rm.gameObject.scene);
    }

    private static void EnsureManager<T>(string name) where T : Component
    {
        if (FindFirstObjectByType<T>() != null) return;
        var go = new GameObject(name);
        go.AddComponent<T>();
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
    }

    // ---------------------------------------------------------------
    // Scene & validate tab
    // ---------------------------------------------------------------

    private void DrawSceneTab()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Create missing scene managers", GUILayout.Height(26)))
        {
            EnsureManager<GridManager>("GridManager");
            EnsureManager<EconomyManager>("EconomyManager");
            EnsureManager<BuildManager>("BuildManager");
            EnsureManager<ResearchManager>("ResearchManager");
            EnsureManager<UIManager>("UIManager");
            EditorSceneManager.MarkAllScenesDirty();
        }
        if (GUILayout.Button("Sync all buildings & techs into managers", GUILayout.Height(26)))
        {
            var bm = FindFirstObjectByType<BuildManager>();
            if (bm != null)
            {
                Undo.RecordObject(bm, "Sync buildings");
                bm.availableBuildings.RemoveAll(b => b == null);
                foreach (var b in LoadAll<BuildingDefinition>())
                    if (!bm.availableBuildings.Contains(b)) bm.availableBuildings.Add(b);
                EditorUtility.SetDirty(bm);
                EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
            }
            var rm = FindFirstObjectByType<ResearchManager>();
            if (rm != null)
            {
                Undo.RecordObject(rm, "Sync techs");
                rm.allTechs.RemoveAll(t => t == null);
                foreach (var t in LoadAll<TechDefinition>())
                    if (!rm.allTechs.Contains(t)) rm.allTechs.Add(t);
                EditorUtility.SetDirty(rm);
                EditorSceneManager.MarkSceneDirty(rm.gameObject.scene);
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Validate project", GUILayout.Height(26))) Validate();

        EditorGUILayout.Space(4);
        sceneScroll = EditorGUILayout.BeginScrollView(sceneScroll);
        if (issues.Count == 0)
            EditorGUILayout.HelpBox("Press 'Validate project' to check items, recipes, buildings and techs.", MessageType.Info);
        foreach (var (type, text) in issues) EditorGUILayout.HelpBox(text, type);
        EditorGUILayout.EndScrollView();
    }

    private void Validate()
    {
        issues.Clear();
        var items = LoadAll<ItemDefinition>();
        var recipes = LoadAll<RecipeDefinition>();
        var buildings = LoadAll<BuildingDefinition>();
        var techs = LoadAll<TechDefinition>();
        var bm = FindFirstObjectByType<BuildManager>();
        var rm = FindFirstObjectByType<ResearchManager>();

        if (bm == null) Add(MessageType.Error, "No BuildManager in the scene.");
        if (rm == null && techs.Length > 0) Add(MessageType.Warning, "Techs exist but there is no ResearchManager in the scene (everything counts as unlocked).");
        if (FindFirstObjectByType<GridManager>() == null) Add(MessageType.Error, "No GridManager in the scene.");
        if (FindFirstObjectByType<EconomyManager>() == null) Add(MessageType.Error, "No EconomyManager in the scene.");
        if (FindFirstObjectByType<UIManager>() == null) Add(MessageType.Warning, "No UIManager in the scene.");

        // Items that something can actually produce
        var producible = new HashSet<ItemDefinition>();
        foreach (var m in PrefabsOf<Miner>())
        {
            if (m.availableItems.Count == 0 && m.producedItem == null)
                Add(MessageType.Warning, "Miner prefab '" + m.name + "' has no items to mine.");
            foreach (var i in m.availableItems) if (i != null) producible.Add(i);
            if (m.producedItem != null) producible.Add(m.producedItem);
        }
        foreach (var r in recipes) if (r.outputItem != null) producible.Add(r.outputItem);

        foreach (var i in items)
            if (i.sellValue <= 0) Add(MessageType.Warning, "Item '" + i.name + "' has a sell value of " + i.sellValue + ".");

        var usedRecipes = new HashSet<RecipeDefinition>();
        foreach (var p in PrefabsOf<Processor>()) foreach (var r in p.recipes) if (r != null) usedRecipes.Add(r);
        foreach (var r in recipes)
        {
            if (r.inputItem == null || r.outputItem == null) Add(MessageType.Error, "Recipe '" + r.name + "' is missing its input or output item.");
            else if (r.inputItem == r.outputItem) Add(MessageType.Warning, "Recipe '" + r.name + "' produces the same item it consumes.");
            if (r.processTime <= 0f) Add(MessageType.Warning, "Recipe '" + r.name + "' has a process time of 0.");
            if (!usedRecipes.Contains(r)) Add(MessageType.Warning, "Recipe '" + r.name + "' is not used by any Processor prefab.");
        }

        var unlockedByTech = new HashSet<BuildingDefinition>(techs.SelectMany(t => t.unlocks).Where(b => b != null));
        foreach (var b in buildings)
        {
            if (b.prefab == null) Add(MessageType.Error, "Building '" + b.name + "' has no prefab.");
            if (b.size.x < 1 || b.size.y < 1) Add(MessageType.Error, "Building '" + b.name + "' has an invalid size.");
            if (b.prefab is Splitter && b.size != new Vector2Int(1, 2))
                Add(MessageType.Error, "Splitter '" + b.name + "' needs a size of (1, 2).");
            if (!b.unlockedByDefault && !unlockedByTech.Contains(b))
                Add(MessageType.Warning, "Building '" + b.name + "' is locked and no tech unlocks it.");
            if (b.unlockedByDefault && unlockedByTech.Contains(b))
                Add(MessageType.Info, "Building '" + b.name + "' is unlocked by default but a tech also unlocks it.");
            if (bm != null && !bm.availableBuildings.Contains(b))
                Add(MessageType.Warning, "Building '" + b.name + "' is not in BuildManager.availableBuildings.");
        }

        if (techs.Length > 0 && !buildings.Any(b => b.prefab is ResearchLab && b.unlockedByDefault))
            Add(MessageType.Error, "There are techs but no Research Lab building that is unlocked from the start, so nothing can be researched.");

        foreach (var t in techs)
        {
            if (HasCycle(t, new HashSet<TechDefinition>(), new HashSet<TechDefinition>()))
                Add(MessageType.Error, "Tech '" + t.name + "' is part of a prerequisite loop.");
            if (t.prerequisites.Any(p => p == null)) Add(MessageType.Warning, "Tech '" + t.name + "' has an empty prerequisite slot.");
            if (t.cost.Count == 0) Add(MessageType.Warning, "Tech '" + t.name + "' has no cost.");
            foreach (var c in t.cost)
            {
                if (c == null || c.item == null) { Add(MessageType.Error, "Tech '" + t.name + "' has a cost entry with no item."); continue; }
                if (!producible.Contains(c.item))
                    Add(MessageType.Warning, "Tech '" + t.name + "' needs '" + c.item.name + "' but no miner or recipe produces it.");
            }
            if (t.unlocks.Count == 0) Add(MessageType.Warning, "Tech '" + t.name + "' unlocks nothing.");
            if (rm != null && !rm.allTechs.Contains(t)) Add(MessageType.Warning, "Tech '" + t.name + "' is not in ResearchManager.allTechs.");
        }

        if (issues.Count == 0) Add(MessageType.Info, "No problems found.");
    }

    private void Add(MessageType type, string text) { issues.Add((type, text)); }
}
#endif