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
/// Tabs: Items, Recipes, Buildings, Techs, UI, Scene & Validate.
///  - Every asset has collapsible "link" dropdowns with a checkbox per related asset
///    (item -> miners / recipes / tech costs, recipe -> processors, building -> techs / recipes / items,
///    tech -> prerequisites / unlocks / cost items).
///  - Techs show a live preview of the in-game tech tree and detail panel.
///  - UI tab edits the UITheme asset (colors, sizes, fonts, texts) with live mock-ups of the
///    tech window, build menu, hotbar and machine window.
///
/// Put this file in a folder named "Editor" (e.g. Assets/Editor).
/// </summary>
public class BoxFactoryContentEditor : EditorWindow
{
    private enum Tab { Items, Recipes, Buildings, Techs, UI, SceneAndValidate }
    private static readonly string[] TabNames = { "Items", "Recipes", "Buildings", "Techs", "UI", "Scene & Validate" };
    private static readonly string[] PreviewNames = { "Tech window", "Build menu", "Hotbar & hint", "Machine window" };

    private const string ThemePath = "Assets/Resources/UITheme.asset";

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
        if (t == typeof(ResearchLab)) return "Research";
        if (t == typeof(Portal)) return "Endgame";
        return "Buildings";
    }

    private const string RootPrefKey = "BoxFactory.RootFolder";

    private Tab tab;
    private string rootFolder = "Assets/BoxFactory";
    private string search = "";
    private string newName = "";
    private bool autoRegister = true;
    private bool createPrefab = true;
    private int newPrefabType;

    private Vector2 listScroll, detailScroll, sceneScroll, uiScroll, previewScroll, treeScroll;
    private UnityEngine.Object selected;
    private UnityEngine.Object pendingSelect;
    private Editor selectedEditor;
    private Editor prefabEditor;
    private UnityEngine.Object prefabEditorTarget;
    private Editor themeEditor;

    private readonly Dictionary<string, bool> folds = new Dictionary<string, bool>();
    private readonly List<(MessageType type, string text)> issues = new List<(MessageType, string)>();

    // UI preview state
    private UITheme theme, fallbackTheme;
    private float pz = 0.6f;                 // preview zoom
    private bool simulateProgress = true;
    private int previewKind;
    private TechDefinition previewTech;
    private GUIStyle lblStyle;

    [MenuItem("Box Factory/Content Editor")]
    public static void Open()
    {
        var w = GetWindow<BoxFactoryContentEditor>("Box Factory");
        w.minSize = new Vector2(860f, 520f);
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

    private static void Dirty(UnityEngine.Object o)
    {
        EditorUtility.SetDirty(o);
        AssetDatabase.SaveAssets();
    }

    private static Color ColorOf(UnityEngine.Object o)
    {
        switch (o)
        {
            case ItemDefinition i: return i.color;
            case BuildingDefinition b: return b.iconColor;
            case RecipeDefinition r:
            {
                var fo = r.FirstOutput;
                return fo != null ? fo.color : Color.gray;
            }
            case TechDefinition t:
                foreach (var b2 in t.unlocks) if (b2 != null) return b2.iconColor;
                return new Color(0.4f, 0.6f, 0.9f);
        }
        return Color.gray;
    }

    private static string IdOf(UnityEngine.Object o)
    {
        return o is GameDefinition g && !string.IsNullOrEmpty(g.Id) ? g.Id : "";
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
        if (themeEditor != null) DestroyImmediate(themeEditor);
        selectedEditor = null;
        prefabEditor = null;
        themeEditor = null;
        prefabEditorTarget = null;
    }

    private void Select(UnityEngine.Object obj)
    {
        if (selectedEditor != null) DestroyImmediate(selectedEditor);
        if (prefabEditor != null) DestroyImmediate(prefabEditor);
        selectedEditor = null;
        prefabEditor = null;
        prefabEditorTarget = null;

        selected = obj;
        if (obj != null) selectedEditor = Editor.CreateEditor(obj);
        if (obj is TechDefinition td) previewTech = td;
        GUI.FocusControl(null);
    }

    // ---- Collapsible "dropdown" sections ----

    private bool Fold(string key, string label, bool defaultOpen = false)
    {
        if (!folds.TryGetValue(key, out bool open)) open = defaultOpen;
        open = EditorGUILayout.Foldout(open, label, true, EditorStyles.foldoutHeader);
        folds[key] = open;
        return open;
    }

    /// <summary>
    /// A dropdown with one checkbox per candidate. Ticking/unticking calls 'set'.
    /// The title shows how many are linked, e.g. "Miners that can mine this item (2/3)".
    /// </summary>
    private void LinkList<T>(string key, string title, IEnumerable<T> candidates, Func<T, string> label,
        Func<T, bool> has, Action<T, bool> set, string emptyMessage) where T : UnityEngine.Object
    {
        var list = candidates.Where(c => c != null).ToList();
        int count = list.Count(has);
        if (!Fold(key, title + "  (" + count + "/" + list.Count + ")")) return;

        EditorGUI.indentLevel++;
        if (list.Count == 0) EditorGUILayout.HelpBox(emptyMessage, MessageType.None);
        foreach (var c in list)
        {
            bool h = has(c);
            bool now = EditorGUILayout.ToggleLeft(label(c), h);
            if (now != h) set(c, now);
        }
        EditorGUI.indentLevel--;
    }

    // ---------------------------------------------------------------
    // Main GUI
    // ---------------------------------------------------------------

    void OnGUI()
    {
        // Selection changes requested by clicks are applied on the next Layout event (keeps IMGUI happy).
        if (pendingSelect != null && Event.current.type == EventType.Layout)
        {
            var p = pendingSelect;
            pendingSelect = null;
            Select(p);
        }

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
        else if (tab == Tab.UI) DrawUITab();
        else DrawAssetTab();
    }

    // ---------------------------------------------------------------
    // Asset tabs
    // ---------------------------------------------------------------

    private void DrawAssetTab()
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.Width(260f));
        DrawCreateBox();

        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);

        listScroll = EditorGUILayout.BeginScrollView(listScroll, "box");
        foreach (var obj in CurrentAssets())
        {
            if (!string.IsNullOrEmpty(search))
            {
                bool hit = obj.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                           || IdOf(obj).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) continue;
            }
            DrawListRow(obj);
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

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

        string id = IdOf(obj);
        string label = id.Length > 0 ? id + "   " + obj.name : obj.name;
        GUI.Label(new Rect(r.x + 24f, r.y, r.width - 24f, r.height), label);

        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            pendingSelect = obj;
            Event.current.Use();
            Repaint();
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
                    if (prefabType == typeof(Splitter)) b.size = new Vector2Int(1, 2);
                }
                break;
            case TechDefinition t:
                t.displayName = name;
                break;
        }

        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        IdAssigner.EnsureUnique(path);

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
            IdAssigner.EnsureUnique(copy);
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
    // Per-type extras (all the dropdowns)
    // ---------------------------------------------------------------

    private static IEnumerable<T> PrefabsOf<T>() where T : FactoryBuilding
    {
        return LoadAll<BuildingDefinition>().Select(d => d.prefab).OfType<T>().Distinct();
    }

    private static void SetRecipeItem(RecipeDefinition r, ItemDefinition item, bool asInput, bool on)
    {
        Undo.RecordObject(r, "Edit recipe");
        if (r.NeedsMigration) r.MigrateLegacy();
        if (r.inputs == null) r.inputs = new List<ItemStack>();
        if (r.outputs == null) r.outputs = new List<ItemStack>();

        var list = asInput ? r.inputs : r.outputs;
        if (on)
        {
            if (!list.Any(s => s != null && s.item == item)) list.Add(new ItemStack(item, 1));
        }
        else
        {
            list.RemoveAll(s => s == null || s.item == item);
        }
        Dirty(r);
    }

    // ---- Items ----

    private void DrawItemExtras(ItemDefinition item)
    {
        LinkList("item.miners", "Miners that can mine this item", PrefabsOf<Miner>(), m => m.name,
            m => m.availableItems.Contains(item),
            (m, on) =>
            {
                Undo.RecordObject(m, "Edit miner items");
                if (on) m.availableItems.Add(item); else m.availableItems.Remove(item);
                Dirty(m);
            },
            "No Miner prefabs yet. Create a Building with prefab type Miner.");

        var recipes = LoadAll<RecipeDefinition>();
        LinkList("item.recipesIn", "Recipes that consume this item", recipes, r => r.name,
            r => r.UsesInput(item),
            (r, on) => SetRecipeItem(r, item, true, on),
            "No recipes yet.");
        LinkList("item.recipesOut", "Recipes that produce this item", recipes, r => r.name,
            r => r.MakesOutput(item),
            (r, on) => SetRecipeItem(r, item, false, on),
            "No recipes yet.");

        LinkList("item.techs", "Techs that cost this item (amount 10 when added, edit it on the tech)", LoadAll<TechDefinition>(),
            t => t.displayName,
            t => t.cost.Any(c => c != null && c.item == item),
            (t, on) =>
            {
                Undo.RecordObject(t, "Edit tech cost");
                if (on) t.cost.Add(new ItemAmount { item = item, amount = 10 });
                else t.cost.RemoveAll(c => c == null || c.item == item);
                Dirty(t);
            },
            "No techs yet.");

        if (Fold("item.preview", "Preview", false))
        {
            Rect r = FixedRect(60f, 60f);
            DrawSlot(r, Theme.slotBorder);
            DrawIconColor(r, item.color);
            EditorGUILayout.LabelField(item.itemName);
        }
    }

    // ---- Recipes ----

    private void DrawRecipeExtras(RecipeDefinition recipe)
    {
        EditorGUILayout.LabelField(recipe.Describe() + "   (" + recipe.processTime.ToString("0.#") + "s)", EditorStyles.wordWrappedLabel);

        if (recipe.NeedsMigration)
        {
            EditorGUILayout.HelpBox("This recipe still uses the old single input/output fields. They work, but copy them into the lists to edit them.", MessageType.Info);
            if (GUILayout.Button("Migrate to multi-item lists"))
            {
                Undo.RecordObject(recipe, "Migrate recipe");
                recipe.MigrateLegacy();
                Dirty(recipe);
                Select(recipe);
            }
        }

        if (recipe.IsValid && GUILayout.Button("Auto-name file from items"))
        {
            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(recipe), RecipeFileName(recipe));
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.Space(6);

        LinkList("recipe.procs", "Processors that run this recipe", PrefabsOf<Processor>(), p => p.name,
            p => p.recipes.Contains(recipe),
            (p, on) =>
            {
                Undo.RecordObject(p, "Edit processor recipes");
                if (on) p.recipes.Add(recipe); else p.recipes.Remove(recipe);
                Dirty(p);
            },
            "No Processor prefabs yet. Create a Building with prefab type Processor.");

        var items = LoadAll<ItemDefinition>();
        LinkList("recipe.inputs", "Input items", items, i => i.itemName,
            i => recipe.UsesInput(i),
            (i, on) => SetRecipeItem(recipe, i, true, on),
            "No items yet.");
        LinkList("recipe.outputs", "Output items", items, i => i.itemName,
            i => recipe.MakesOutput(i),
            (i, on) => SetRecipeItem(recipe, i, false, on),
            "No items yet.");
    }

    private static string RecipeFileName(RecipeDefinition r)
    {
        return NameList(r.Inputs) + "_to_" + NameList(r.Outputs);
    }

    private static string NameList(IReadOnlyList<ItemStack> list)
    {
        var parts = new List<string>();
        foreach (var s in list) if (s != null && s.item != null) parts.Add(s.item.itemName);
        return string.Join("+", parts);
    }

    // ---- Buildings ----

    private void DrawBuildingExtras(BuildingDefinition def)
    {
        var unlockers = LoadAll<TechDefinition>().Where(t => t.unlocks.Contains(def)).ToList();
        if (def.unlockedByDefault)
            EditorGUILayout.HelpBox("Available from the start.", MessageType.None);
        else if (unlockers.Count == 0)
            EditorGUILayout.HelpBox("Locked and no tech unlocks it. The player can never build this.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Unlocked by: " + string.Join(", ", unlockers.Select(t => t.displayName)), MessageType.None);

        var bm = FindFirstObjectByType<BuildManager>();
        if (bm != null)
        {
            bool reg = bm.availableBuildings.Contains(def);
            bool now = EditorGUILayout.ToggleLeft("Registered in the scene's BuildManager", reg);
            if (now != reg)
            {
                Undo.RecordObject(bm, "Register building");
                if (now) bm.availableBuildings.Add(def); else bm.availableBuildings.Remove(def);
                EditorUtility.SetDirty(bm);
                EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
            }
        }

        LinkList("building.techs", "Unlocked by techs", LoadAll<TechDefinition>(), t => t.displayName,
            t => t.unlocks.Contains(def),
            (t, on) =>
            {
                Undo.RecordObject(t, "Edit tech unlocks");
                if (on) { if (!t.unlocks.Contains(def)) t.unlocks.Add(def); }
                else t.unlocks.Remove(def);
                Dirty(t);
            },
            "No techs yet.");

        if (def.prefab is Processor proc)
        {
            LinkList("building.recipes", "Recipes this building runs", LoadAll<RecipeDefinition>(), r => r.name,
                r => proc.recipes.Contains(r),
                (r, on) =>
                {
                    Undo.RecordObject(proc, "Edit processor recipes");
                    if (on) { if (!proc.recipes.Contains(r)) proc.recipes.Add(r); } else proc.recipes.Remove(r);
                    Dirty(proc);
                },
                "No recipes yet.");
        }

        if (def.prefab is Miner miner)
        {
            LinkList("building.items", "Items this building can mine", LoadAll<ItemDefinition>(), i => i.itemName,
                i => miner.availableItems.Contains(i),
                (i, on) =>
                {
                    Undo.RecordObject(miner, "Edit miner items");
                    if (on) { if (!miner.availableItems.Contains(i)) miner.availableItems.Add(i); } else miner.availableItems.Remove(i);
                    Dirty(miner);
                },
                "No items yet.");
        }

        if (Fold("building.icon", "Build menu icon preview", false))
        {
            float s = Theme.buildIconSize;
            Rect r = FixedRect(s, s);
            DrawSlot(r, Theme.slotBorder);
            DrawBuildingIcon(r, def);
            Lbl(Inset(r, 3f), def.displayName, 11f, FontStyle.Bold, TextAnchor.LowerCenter, Color.white);
        }

        EditorGUILayout.Space(6);

        if (def.prefab == null)
        {
            EditorGUILayout.HelpBox("This definition has no prefab.", MessageType.Warning);
            newPrefabType = EditorGUILayout.Popup("Prefab type", newPrefabType, PrefabTypeNames);
            if (GUILayout.Button("Create prefab"))
            {
                Undo.RecordObject(def, "Create prefab");
                def.prefab = CreatePrefab(def.displayName, PrefabTypes[Mathf.Clamp(newPrefabType, 0, PrefabTypes.Length - 1)]);
                Dirty(def);
            }
            return;
        }

        if (Fold("building.prefab", "Prefab settings (" + def.prefab.name + ")", true))
        {
            if (prefabEditorTarget != def.prefab)
            {
                if (prefabEditor != null) DestroyImmediate(prefabEditor);
                prefabEditor = Editor.CreateEditor(def.prefab);
                prefabEditorTarget = def.prefab;
            }
            EditorGUI.BeginChangeCheck();
            if (prefabEditor != null) prefabEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) Dirty(def.prefab);
        }
    }

    // ---- Techs ----

    private void DrawTechExtras(TechDefinition tech)
    {
        if (HasCycle(tech, new HashSet<TechDefinition>(), new HashSet<TechDefinition>()))
            EditorGUILayout.HelpBox("Prerequisite loop detected.", MessageType.Error);
        if (tech.cost.Count == 0 || tech.cost.Any(c => c == null || c.item == null))
            EditorGUILayout.HelpBox("Cost is empty or has a missing item (a tech with no cost completes instantly).", MessageType.Warning);
        if (tech.unlocks.Count == 0)
            EditorGUILayout.HelpBox("This tech unlocks nothing.", MessageType.Warning);

        var rm = FindFirstObjectByType<ResearchManager>();
        if (rm != null)
        {
            bool reg = rm.allTechs.Contains(tech);
            bool now = EditorGUILayout.ToggleLeft("Registered in the scene's ResearchManager (shown in the tech tree)", reg);
            if (now != reg)
            {
                Undo.RecordObject(rm, "Register tech");
                if (now) rm.allTechs.Add(tech); else rm.allTechs.Remove(tech);
                EditorUtility.SetDirty(rm);
                EditorSceneManager.MarkSceneDirty(rm.gameObject.scene);
            }
        }

        LinkList("tech.prereq", "Prerequisites", LoadAll<TechDefinition>().Where(t => t != tech),
            t => t.displayName,
            t => tech.prerequisites.Contains(t),
            (t, on) =>
            {
                Undo.RecordObject(tech, "Edit prerequisites");
                if (on)
                {
                    tech.prerequisites.Add(t);
                    if (HasCycle(tech, new HashSet<TechDefinition>(), new HashSet<TechDefinition>()))
                    {
                        tech.prerequisites.Remove(t);
                        Debug.LogWarning("Content Editor: '" + t.displayName + "' can't be a prerequisite of '" + tech.displayName + "' (it would create a loop).");
                    }
                }
                else tech.prerequisites.Remove(t);
                Dirty(tech);
            },
            "No other techs yet.");

        LinkList("tech.unlocks", "Buildings this tech unlocks", LoadAll<BuildingDefinition>(), b => b.displayName,
            b => tech.unlocks.Contains(b),
            (b, on) =>
            {
                Undo.RecordObject(tech, "Edit unlocks");
                if (on) { if (!tech.unlocks.Contains(b)) tech.unlocks.Add(b); } else tech.unlocks.Remove(b);
                Dirty(tech);
            },
            "No buildings yet.");

        LinkList("tech.cost", "Cost items (amount 10 when added, edit amounts above)", LoadAll<ItemDefinition>(), i => i.itemName,
            i => tech.cost.Any(c => c != null && c.item == i),
            (i, on) =>
            {
                Undo.RecordObject(tech, "Edit cost");
                if (on) tech.cost.Add(new ItemAmount { item = i, amount = 10 });
                else tech.cost.RemoveAll(c => c == null || c.item == i);
                Dirty(tech);
            },
            "No items yet.");

        var dependents = LoadAll<TechDefinition>().Where(t => t.prerequisites.Contains(tech)).ToList();
        if (dependents.Count > 0)
            EditorGUILayout.LabelField("Leads to: " + string.Join(", ", dependents.Select(t => t.displayName)), EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space(4);
        DrawTechPreviewSection(tech);
    }

    private void DrawTechPreviewSection(TechDefinition tech)
    {
        if (!Fold("tech.preview", "UI preview: how this tech looks in the game", true)) return;

        EditorGUILayout.BeginHorizontal();
        simulateProgress = EditorGUILayout.ToggleLeft("Simulate: prerequisites done, this tech active", simulateProgress);
        EditorGUILayout.LabelField("Zoom", GUILayout.Width(40f));
        pz = GUILayout.HorizontalSlider(pz, 0.3f, 1.5f, GUILayout.Width(110f));
        EditorGUILayout.EndHorizontal();

        if (!HasThemeAsset)
            EditorGUILayout.HelpBox("Previewing with default UI values. Create a UITheme in the UI tab to customise the look.", MessageType.None);

        Rect tree = GUILayoutUtility.GetRect(10f, 300f, GUILayout.ExpandWidth(true));
        DrawTechTree(tree, tech);

        EditorGUILayout.Space(4);
        Rect detail = GUILayoutUtility.GetRect(10f, 210f * pz, GUILayout.ExpandWidth(true));
        DrawTechDetail(detail, tech);

        EditorGUILayout.HelpBox("Click a node to select that tech. Layout and order follow ResearchManager.allTechs, like the game.", MessageType.None);
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
    // UI tab: edit the UITheme + live mock-ups
    // ---------------------------------------------------------------

    private bool HasThemeAsset { get { return AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath) != null || AssetDatabase.FindAssets("t:UITheme").Length > 0; } }

    private UITheme Theme
    {
        get
        {
            if (theme == null)
            {
                theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
                if (theme == null)
                {
                    var guids = AssetDatabase.FindAssets("t:UITheme");
                    if (guids.Length > 0) theme = AssetDatabase.LoadAssetAtPath<UITheme>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (theme != null) return theme;

            if (fallbackTheme == null)
            {
                fallbackTheme = CreateInstance<UITheme>();
                fallbackTheme.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallbackTheme;
        }
    }

    private void CreateTheme()
    {
        EnsureFolder("Assets/Resources");
        var t = CreateInstance<UITheme>();
        AssetDatabase.CreateAsset(t, ThemePath);
        AssetDatabase.SaveAssets();
        theme = t;
        if (themeEditor != null) DestroyImmediate(themeEditor);
        themeEditor = null;
    }

    private void DrawUITab()
    {
        EditorGUILayout.BeginHorizontal();

        // ---- Left: theme fields ----
        EditorGUILayout.BeginVertical(GUILayout.Width(350f));
        if (!HasThemeAsset)
        {
            EditorGUILayout.HelpBox("No UITheme asset yet. The game uses built-in defaults until you create one. " +
                                    "It is created at " + ThemePath + " so builds can load it.", MessageType.Info);
            if (GUILayout.Button("Create UITheme", GUILayout.Height(28f)))
            {
                CreateTheme();
                GUIUtility.ExitGUI();
            }
        }
        else
        {
            var th = Theme;
            EditorGUILayout.ObjectField("Theme asset", th, typeof(UITheme), false);

            if (themeEditor == null || themeEditor.target != th)
            {
                if (themeEditor != null) DestroyImmediate(themeEditor);
                themeEditor = Editor.CreateEditor(th);
            }

            uiScroll = EditorGUILayout.BeginScrollView(uiScroll);
            EditorGUI.BeginChangeCheck();
            themeEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(th);
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Reset to defaults"))
            {
                if (EditorUtility.DisplayDialog("Reset UI theme", "Reset every UI value to the built-in defaults?", "Reset", "Cancel"))
                {
                    var d = CreateInstance<UITheme>();
                    Undo.RecordObject(th, "Reset UI theme");
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d), th);
                    DestroyImmediate(d);
                    Dirty(th);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();

        // ---- Right: preview ----
        EditorGUILayout.BeginVertical();
        previewKind = GUILayout.Toolbar(previewKind, PreviewNames, GUILayout.Height(24f));
        EditorGUILayout.BeginHorizontal();
        pz = EditorGUILayout.Slider("Zoom", pz, 0.3f, 1.5f);
        EditorGUILayout.EndHorizontal();
        if (previewKind == 0)
        {
            EditorGUILayout.BeginHorizontal();
            previewTech = (TechDefinition)EditorGUILayout.ObjectField("Preview tech", previewTech, typeof(TechDefinition), false);
            simulateProgress = EditorGUILayout.ToggleLeft("Simulate progress", simulateProgress, GUILayout.Width(130f));
            EditorGUILayout.EndHorizontal();
        }

        previewScroll = EditorGUILayout.BeginScrollView(previewScroll, "box");
        switch (previewKind)
        {
            case 0: DrawTechWindowMock(); break;
            case 1: DrawBuildMenuMock(); break;
            case 2: DrawHotbarMock(); break;
            default: DrawMachineWindowMock(); break;
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    // ---------------------------------------------------------------
    // Preview drawing primitives
    // ---------------------------------------------------------------

    private static Rect FixedRect(float w, float h) { return GUILayoutUtility.GetRect(w, w, h, h); }

    private static void Fill(Rect r, Color c) { EditorGUI.DrawRect(r, c); }

    private static Rect Inset(Rect r, float d)
    {
        return new Rect(r.x + d, r.y + d, Mathf.Max(0f, r.width - 2f * d), Mathf.Max(0f, r.height - 2f * d));
    }

    private static void Outline(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    private void Lbl(Rect r, string s, float size, FontStyle fs, TextAnchor a, Color c)
    {
        if (lblStyle == null) lblStyle = new GUIStyle(EditorStyles.label);
        lblStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
        lblStyle.fontStyle = fs;
        lblStyle.alignment = a;
        lblStyle.wordWrap = true;
        lblStyle.clipping = TextClipping.Clip;
        lblStyle.normal.textColor = c;
        GUI.Label(r, s, lblStyle);
    }

    private void DrawSlot(Rect r, Color border)
    {
        var th = Theme;
        Fill(r, border);
        Fill(Inset(r, 2f * pz), th.slotInner);
    }

    private static void DrawIconColor(Rect r, Color c)
    {
        Fill(Inset(r, r.width * 0.2f), c);
    }

    private static void DrawSprite(Rect r, Sprite s)
    {
        if (s == null || s.texture == null) return;
        var tex = s.texture;
        Rect sr = s.textureRect;
        var tc = new Rect(sr.x / tex.width, sr.y / tex.height, sr.width / tex.width, sr.height / tex.height);
        float a = sr.width / Mathf.Max(1f, sr.height);
        Rect fit;
        if (a > 1f)
        {
            float h = r.width / a;
            fit = new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h);
        }
        else
        {
            float w = r.height * a;
            fit = new Rect(r.x + (r.width - w) * 0.5f, r.y, w, r.height);
        }
        GUI.DrawTextureWithTexCoords(fit, tex, tc);
    }

    private static void DrawBuildingIcon(Rect slot, BuildingDefinition def)
    {
        Rect icon = Inset(slot, slot.width * 0.2f);
        Sprite sp = def.PreviewSprite;
        if (sp != null) DrawSprite(icon, sp);
        else Fill(icon, def.iconColor);
    }

    /// <summary>Window frame like UIManager.CreateWindow: panel, title bar with X, dark inset body.</summary>
    private Rect DrawWindowFrame(Rect r, string title)
    {
        var th = Theme;
        float z = pz;
        Fill(r, th.panelBg);
        var bar = new Rect(r.x + 6f * z, r.y + 6f * z, r.width - 12f * z, 30f * z);
        Fill(bar, th.titleBar);
        Lbl(new Rect(bar.x + 10f * z, bar.y, bar.width - 50f * z, bar.height), title, th.windowTitleFontSize * z,
            FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        var x = new Rect(bar.xMax - 30f * z, bar.y + 2f * z, 28f * z, 26f * z);
        Fill(x, th.buttonRed);
        Lbl(x, "X", 14f * z, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);

        var body = new Rect(r.x + 6f * z, bar.yMax + 6f * z, r.width - 12f * z, r.yMax - bar.yMax - 12f * z);
        Fill(body, th.panelInner);
        return body;
    }

    // ---------------------------------------------------------------
    // Tech tree + detail panel preview (mirrors UIManager)
    // ---------------------------------------------------------------

    private enum NodeState { Locked, Available, Active, Done }

    private List<TechDefinition> PreviewTechs()
    {
        var rm = FindFirstObjectByType<ResearchManager>();
        if (rm != null && rm.allTechs.Any(t => t != null)) return rm.allTechs.Where(t => t != null).ToList();
        return LoadAll<TechDefinition>().ToList();
    }

    private static int TechDepth(TechDefinition t, Dictionary<TechDefinition, int> memo, HashSet<TechDefinition> stack)
    {
        if (memo.TryGetValue(t, out int d)) return d;
        if (!stack.Add(t)) return 0;
        int best = 0;
        foreach (var p in t.prerequisites)
            if (p != null) best = Mathf.Max(best, TechDepth(p, memo, stack) + 1);
        stack.Remove(t);
        memo[t] = best;
        return best;
    }

    private static void CollectPrereqs(TechDefinition t, HashSet<TechDefinition> set)
    {
        foreach (var p in t.prerequisites)
            if (p != null && set.Add(p)) CollectPrereqs(p, set);
    }

    private Dictionary<TechDefinition, Vector2> LayoutTechs(List<TechDefinition> techs, UITheme th, out Vector2 size)
    {
        var depth = new Dictionary<TechDefinition, int>();
        var colCount = new Dictionary<int, int>();
        var pos = new Dictionary<TechDefinition, Vector2>();
        float maxX = 0f, maxY = 0f;

        foreach (var t in techs)
        {
            if (t == null || pos.ContainsKey(t)) continue;
            int d = TechDepth(t, depth, new HashSet<TechDefinition>());
            colCount.TryGetValue(d, out int row);
            colCount[d] = row + 1;

            var p = new Vector2(24f + d * th.techColumnSpacing, 24f + row * th.techRowSpacing);
            pos[t] = p;
            maxX = Mathf.Max(maxX, p.x + th.techNodeSize.x);
            maxY = Mathf.Max(maxY, p.y + th.techNodeSize.y);
        }
        size = new Vector2(maxX + 24f, maxY + 24f);
        return pos;
    }

    private void DrawTechTree(Rect view, TechDefinition focus)
    {
        var th = Theme;
        float z = pz;
        Fill(view, th.techTreeBg);

        var techs = PreviewTechs();
        if (techs.Count == 0)
        {
            Lbl(Inset(view, 10f), "No techs yet.", 14f, FontStyle.Italic, TextAnchor.MiddleCenter, th.dimText);
            return;
        }

        var pos = LayoutTechs(techs, th, out Vector2 size);
        var done = new HashSet<TechDefinition>();
        if (simulateProgress && focus != null) CollectPrereqs(focus, done);

        var content = new Rect(0f, 0f, Mathf.Max(size.x * z, view.width - 16f), Mathf.Max(size.y * z, view.height - 16f));
        treeScroll = GUI.BeginScrollView(view, treeScroll, content);

        // Lines first so nodes draw on top.
        Handles.color = th.techLineColor;
        foreach (var t in techs)
        {
            if (!pos.ContainsKey(t)) continue;
            foreach (var pre in t.prerequisites)
            {
                if (pre == null || !pos.TryGetValue(pre, out Vector2 pp)) continue;
                Vector2 a = (pp + new Vector2(th.techNodeSize.x, th.techNodeSize.y * 0.5f)) * z;
                Vector2 b = (pos[t] + new Vector2(0f, th.techNodeSize.y * 0.5f)) * z;
                Handles.DrawAAPolyLine(Mathf.Max(1f, th.techLineThickness * z), new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
            }
        }

        foreach (var t in techs)
        {
            if (!pos.TryGetValue(t, out Vector2 p)) continue;
            Rect r = new Rect(p.x * z, p.y * z, th.techNodeSize.x * z, th.techNodeSize.y * z);

            NodeState st;
            if (done.Contains(t)) st = NodeState.Done;
            else if (simulateProgress && t == focus) st = NodeState.Active;
            else st = t.prerequisites.All(q => q == null || done.Contains(q)) ? NodeState.Available : NodeState.Locked;

            Color c = st == NodeState.Done ? th.techDone
                    : st == NodeState.Active ? th.techActive
                    : st == NodeState.Available ? th.techAvailable : th.techLocked;
            Fill(r, c);
            if (t == focus) Outline(r, Color.white, Mathf.Max(1f, 2f * z));

            Rect lr = new Rect(r.x + 8f * z, r.y + 4f * z, r.width - 16f * z, r.height - 18f * z);
            Lbl(lr, t.displayName, th.techNodeFontSize * z, FontStyle.Bold, TextAnchor.MiddleLeft,
                st == NodeState.Locked ? th.dimText : Color.white);

            Rect bar = new Rect(r.x + 4f * z, r.yMax - 10f * z, r.width - 8f * z, 6f * z);
            Fill(bar, th.barBg);
            float prog = st == NodeState.Done ? 1f : st == NodeState.Active ? 0.4f : 0f;
            Fill(new Rect(bar.x, bar.y, bar.width * prog, bar.height), th.barFill);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                pendingSelect = t;
                previewTech = t;
                Event.current.Use();
                Repaint();
            }
        }

        GUI.EndScrollView();
    }

    private void DrawTechDetail(Rect r, TechDefinition t)
    {
        var th = Theme;
        float z = pz;
        Fill(r, new Color(1f, 1f, 1f, 0.05f));
        float x = r.x + 8f * z, w = r.width - 16f * z, y = r.y + 6f * z;

        if (t == null)
        {
            Lbl(new Rect(x, y, w, 26f * z), "Select a technology", 18f * z, FontStyle.Bold, TextAnchor.MiddleLeft, th.accent);
            return;
        }

        Lbl(new Rect(x, y, w, 26f * z), t.displayName, 18f * z, FontStyle.Bold, TextAnchor.MiddleLeft, th.accent);
        y += 30f * z;

        var req = new List<string>();
        foreach (var p in t.prerequisites) if (p != null) req.Add(p.displayName);
        string desc = t.description ?? "";
        if (req.Count > 0) desc += (desc.Length > 0 ? "\n" : "") + "Requires: " + string.Join(", ", req);
        Lbl(new Rect(x, y, w, 40f * z), desc, 13f * z, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
        y += 44f * z;

        float cx = x;
        foreach (var c in t.cost)
        {
            if (c == null || c.item == null) continue;
            Rect slot = new Rect(cx, y, 40f * z, 40f * z);
            DrawSlot(slot, th.slotBorder);
            DrawIconColor(slot, c.item.color);
            Lbl(new Rect(cx + 48f * z, y + 8f * z, 140f * z, 24f * z), c.item.itemName + "  0/" + c.amount,
                14f * z, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            cx += 196f * z;
        }
        y += 48f * z;

        var unlocks = new List<string>();
        foreach (var b in t.unlocks) if (b != null) unlocks.Add(b.displayName);
        Lbl(new Rect(x, y, w, 22f * z), unlocks.Count > 0 ? "Unlocks: " + string.Join(", ", unlocks) : "Unlocks: nothing",
            13f * z, FontStyle.Italic, TextAnchor.MiddleLeft, Color.white);
        y += 26f * z;

        Rect btn = new Rect(x, y, w, 34f * z);
        bool active = simulateProgress;
        Fill(btn, active ? th.buttonRed : th.buttonGreen);
        Lbl(btn, active ? "Cancel research" : "Start research", 15f * z, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);
    }

    // ---------------------------------------------------------------
    // Window mock-ups
    // ---------------------------------------------------------------

    private void DrawTechWindowMock()
    {
        var th = Theme;
        float z = pz;
        Rect frame = FixedRect(th.techWindowSize.x * z, th.techWindowSize.y * z);
        Rect body = DrawWindowFrame(frame, th.techWindowTitle);

        var focus = previewTech;
        if (focus == null) focus = PreviewTechs().FirstOrDefault();

        float detailH = 210f * z;
        float pad = 8f * z;
        Rect tree = new Rect(body.x + pad, body.y + pad, body.width - 2f * pad, body.height - 3f * pad - detailH - 6f * z);
        Rect detail = new Rect(body.x + pad, body.yMax - pad - detailH, body.width - 2f * pad, detailH);
        DrawTechTree(tree, focus);
        DrawTechDetail(detail, focus);
    }

    private void DrawBuildMenuMock()
    {
        var th = Theme;
        float z = pz;
        Rect frame = FixedRect(th.buildMenuSize.x * z, th.buildMenuSize.y * z);
        Rect body = DrawWindowFrame(frame, th.buildMenuTitle);

        var defs = LoadAll<BuildingDefinition>().Where(d => d.unlockedByDefault).ToList();
        var cats = defs.Select(d => string.IsNullOrEmpty(d.category) ? "Buildings" : d.category).Distinct().ToList();

        float x = body.x + 8f * z, y = body.y + 8f * z;
        if (cats.Count > 1)
        {
            for (int i = 0; i < cats.Count; i++)
            {
                Rect tabR = new Rect(x, y, 120f * z, 36f * z);
                Fill(tabR, i == 0 ? th.accent : th.buttonGray);
                Lbl(tabR, cats[i], 14f * z, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);
                x += 126f * z;
            }
            y += 42f * z;
        }

        float infoH = 64f * z;
        Rect grid = new Rect(body.x + 8f * z, y, body.width - 16f * z, body.yMax - y - 8f * z - infoH);
        Fill(grid, new Color(1f, 1f, 1f, 0.05f));

        var shown = cats.Count > 0
            ? defs.Where(d => (string.IsNullOrEmpty(d.category) ? "Buildings" : d.category) == cats[0]).ToList()
            : defs;

        float cell = th.buildIconSize * z, sp = 6f * z;
        float px = grid.x + 8f * z, py = grid.y + 8f * z;
        float cx = px, cy = py;
        foreach (var d in shown)
        {
            if (cx + cell > grid.xMax - 8f * z) { cx = px; cy += cell + sp; }
            if (cy + cell > grid.yMax) break;
            Rect s = new Rect(cx, cy, cell, cell);
            DrawSlot(s, th.slotBorder);
            DrawBuildingIcon(s, d);
            Lbl(Inset(s, 3f * z), d.displayName, 11f * z, FontStyle.Bold, TextAnchor.LowerCenter, Color.white);
            cx += cell + sp;
        }

        Lbl(new Rect(body.x + 8f * z, body.yMax - 8f * z - infoH, body.width - 16f * z, infoH),
            "Hover a building for details", th.bodyFontSize * z, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
    }

    private void DrawHotbarMock()
    {
        var th = Theme;
        float z = pz;
        Rect screen = FixedRect(1280f * z, 260f * z);
        Fill(screen, new Color(0.19f, 0.30f, 0.47f, 1f));

        float s = th.hotbarSlotSize * z, pad = 6f * z, sp = 4f * z;
        float w = pad * 2f + s * 9f + sp * 8f, h = pad * 2f + s;
        Rect bar = new Rect(screen.center.x - w * 0.5f, screen.yMax - th.hotbarBottomOffset * z - h, w, h);
        Fill(bar, th.panelBg);

        var defs = LoadAll<BuildingDefinition>().Where(d => d.unlockedByDefault).ToList();
        for (int i = 0; i < 9; i++)
        {
            Rect sr = new Rect(bar.x + pad + i * (s + sp), bar.y + pad, s, s);
            DrawSlot(sr, i == 0 ? th.accent : th.slotBorder);
            if (i < defs.Count) DrawBuildingIcon(sr, defs[i]);
            Lbl(Inset(sr, 3f * z), (i + 1).ToString(), 12f * z, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
        }

        Lbl(new Rect(screen.center.x - 450f * z, screen.yMax - 14f * z - 26f * z, 900f * z, 26f * z),
            th.hintText, th.hintFontSize * z, FontStyle.Normal, TextAnchor.MiddleCenter, th.dimText);

        // research HUD box (top-left)
        Rect hud = new Rect(screen.x + 14f * z, screen.y + 14f * z, 260f * z, 86f * z);
        Fill(hud, th.panelBg);
        Lbl(new Rect(hud.x + 10f * z, hud.y + 6f * z, hud.width - 20f * z, 22f * z), "Researching: Smelting",
            14f * z, FontStyle.Bold, TextAnchor.MiddleLeft, th.accent);
        Rect pb = new Rect(hud.x + 10f * z, hud.y + 32f * z, hud.width - 20f * z, 14f * z);
        Fill(pb, th.barBg);
        Fill(new Rect(pb.x, pb.y, pb.width * 0.45f, pb.height), th.barFill);
        Lbl(new Rect(hud.x + 10f * z, hud.y + 52f * z, hud.width - 20f * z, 24f * z), "Iron Ore  4/10",
            12f * z, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
    }

    private void DrawMachineWindowMock()
    {
        var th = Theme;
        float z = pz;
        Rect frame = FixedRect(th.machineWindowSize.x * z, th.machineWindowSize.y * z);
        Rect body = DrawWindowFrame(frame, "Miner Mk1");

        float x = body.x + 8f * z, w = body.width - 16f * z, y = body.y + 8f * z;
        Lbl(new Rect(x, y, w, 24f * z), "Size 1x1    Speed x1", th.bodyFontSize * z, FontStyle.Normal, TextAnchor.MiddleLeft, th.dimText);
        y += 30f * z;

        float demolishH = 38f * z;
        Rect opt = new Rect(x, y, w, body.yMax - y - 8f * z - demolishH - 30f * z);
        Fill(opt, new Color(1f, 1f, 1f, 0.03f));

        float ox = opt.x + 10f * z, oy = opt.y + 10f * z;
        Lbl(new Rect(ox, oy, 300f * z, 26f * z), "Mining", 18f * z, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        oy += 32f * z;

        Rect in1 = new Rect(ox, oy, 72f * z, 72f * z);
        Rect out1 = new Rect(opt.xMax - 10f * z - 72f * z, oy, 72f * z, 72f * z);
        DrawSlot(in1, th.slotBorder); DrawIconColor(in1, new Color(0.44f, 0.30f, 0.26f));
        DrawSlot(out1, th.slotBorder); DrawIconColor(out1, new Color(0.44f, 0.30f, 0.26f));
        Rect bar = new Rect(in1.xMax + 8f * z, oy + 29f * z, out1.x - in1.xMax - 16f * z, 14f * z);
        Fill(bar, th.barBg);
        Fill(new Rect(bar.x, bar.y, bar.width * 0.6f, bar.height), th.barFill);
        oy += 80f * z;

        Lbl(new Rect(ox, oy, 300f * z, 24f * z), "Choose resource:", 16f * z, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        oy += 28f * z;

        var items = LoadAll<ItemDefinition>();
        for (int i = 0; i < items.Length && i < 8; i++)
        {
            Rect s = new Rect(ox + i * 72f * z, oy, 64f * z, 64f * z);
            DrawSlot(s, i == 0 ? th.accent : th.slotBorder);
            DrawIconColor(s, items[i].color);
        }

        Lbl(new Rect(x, opt.yMax + 4f * z, w, 24f * z), "Click to mine Iron Ore", th.bodyFontSize * z, FontStyle.Italic,
            TextAnchor.MiddleLeft, th.dimText);

        Rect dem = new Rect(x, body.yMax - 8f * z - demolishH, w, demolishH);
        Fill(dem, th.buttonRed);
        Lbl(dem, "Demolish", 15f * z, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);
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
        if (FindFirstObjectByType<UIManager>() == null) Add(MessageType.Warning, "No UIManager in the scene.");

        if (AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath) == null)
        {
            if (AssetDatabase.FindAssets("t:UITheme").Length > 0)
                Add(MessageType.Warning, "A UITheme exists but not at " + ThemePath + ", so the game cannot load it.");
            else
                Add(MessageType.Info, "No UITheme asset: the game uses built-in UI defaults (UI tab > Create UITheme).");
        }

        foreach (var i in items)
            if (string.IsNullOrEmpty(i.Id)) Add(MessageType.Warning, "Item '" + i.name + "' has no ID yet (Box Factory > Assign Missing IDs).");
        foreach (var b in buildings)
            if (string.IsNullOrEmpty(b.Id)) Add(MessageType.Warning, "Building '" + b.name + "' has no ID yet (Box Factory > Assign Missing IDs).");
        foreach (var dup in items.Where(i => !string.IsNullOrEmpty(i.Id)).GroupBy(i => i.Id).Where(g => g.Count() > 1))
            Add(MessageType.Error, "Items share the ID " + dup.Key + ": " + string.Join(", ", dup.Select(a => a.name)));
        foreach (var dup in buildings.Where(b => !string.IsNullOrEmpty(b.Id)).GroupBy(b => b.Id).Where(g => g.Count() > 1))
            Add(MessageType.Error, "Buildings share the ID " + dup.Key + ": " + string.Join(", ", dup.Select(a => a.name)));

        var producible = new HashSet<ItemDefinition>();
        foreach (var m in PrefabsOf<Miner>())
        {
            if (m.availableItems.Count == 0 && m.producedItem == null)
                Add(MessageType.Warning, "Miner prefab '" + m.name + "' has no items to mine.");
            foreach (var i in m.availableItems) if (i != null) producible.Add(i);
            if (m.producedItem != null) producible.Add(m.producedItem);
        }
        foreach (var r in recipes)
            foreach (var st in r.Outputs)
                if (st != null && st.item != null) producible.Add(st.item);

        var usedRecipes = new HashSet<RecipeDefinition>();
        foreach (var p in PrefabsOf<Processor>()) foreach (var r in p.recipes) if (r != null) usedRecipes.Add(r);
        foreach (var p in PrefabsOf<Processor>())
        {
            var seen = new Dictionary<ItemDefinition, RecipeDefinition>();
            foreach (var r in p.GetAllRecipes())
                foreach (var st in r.Inputs)
                {
                    if (st == null || st.item == null) continue;
                    if (seen.TryGetValue(st.item, out var other) && other != r)
                        Add(MessageType.Warning, "Processor '" + p.name + "': recipes '" + other.name + "' and '" + r.name + "' both use '" + st.item.itemName + "'. The first item to arrive picks one, so it may lock to the wrong recipe.");
                    else seen[st.item] = r;
                }
        }

        foreach (var r in recipes)
        {
            if (!r.IsValid) Add(MessageType.Error, "Recipe '" + r.name + "' needs at least one input and one output item, with no empty slots.");
            else if (r.Inputs.Count == 1 && r.Outputs.Count == 1 && r.Inputs[0].item == r.Outputs[0].item)
                Add(MessageType.Warning, "Recipe '" + r.name + "' produces the same item it consumes.");
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
            if (t.unlocks.Any(b => b == null)) Add(MessageType.Warning, "Tech '" + t.name + "' has an empty unlock slot (e.g. the deleted Seller).");
            if (rm != null && !rm.allTechs.Contains(t)) Add(MessageType.Warning, "Tech '" + t.name + "' is not in ResearchManager.allTechs.");
        }

        if (issues.Count == 0) Add(MessageType.Info, "No problems found.");
    }

    private void Add(MessageType type, string text) { issues.Add((type, text)); }
}
#endif
