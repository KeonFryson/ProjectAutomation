#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Menu: Box Factory > Create Main Menu Scene. Creates Assets/Scenes/MainMenu.unity and fixes Build Settings.</summary>
public static class MainMenuSceneSetup
{
    [MenuItem("Box Factory/Create Main Menu Scene")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        const string menuPath = "Assets/Scenes/MainMenu.unity";
        const string gamePath = "Assets/Scenes/" + SaveSystem.GameScene + ".unity";

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("MainMenu").AddComponent<MainMenu>();
        EditorSceneManager.SaveScene(scene, menuPath);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(menuPath, true),
            new EditorBuildSettingsScene(gamePath, true)
        };

        EditorUtility.DisplayDialog("Main Menu",
            "Created " + menuPath + " and set Build Settings to:\n0: MainMenu\n1: " + SaveSystem.GameScene +
            "\n\nPress Play from the MainMenu scene.", "OK");
    }
}
#endif
