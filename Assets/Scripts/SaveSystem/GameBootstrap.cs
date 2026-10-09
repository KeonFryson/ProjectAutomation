using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the save/menu systems automatically so no manual scene wiring is needed:
///  - in the MainMenu scene: creates the MainMenu UI
///  - in any scene containing a GridManager: creates SaveManager (+ PauseMenu)
/// </summary>
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SaveSystem.MenuScene)
        {
            if (Object.FindFirstObjectByType<MainMenu>() == null)
                new GameObject("MainMenu").AddComponent<MainMenu>();
            return;
        }

        if (Object.FindFirstObjectByType<GridManager>() != null &&
            Object.FindFirstObjectByType<SaveManager>() == null)
        {
            new GameObject("SaveManager").AddComponent<SaveManager>();
        }
    }
}
