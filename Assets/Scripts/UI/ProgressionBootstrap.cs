using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Starts the tutorial automatically, no scene wiring needed.
/// Runs from sceneLoaded, i.e. before SaveManager.Start consumes PendingLoadSlot,
/// so we can tell a fresh game (NoSlot) from a loaded save.
/// </summary>
public static class ProgressionBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SaveSystem.MenuScene) return;
        if (UnityEngine.Object.FindFirstObjectByType<GridManager>() == null) return;

        bool freshGame = SaveSystem.PendingLoadSlot == SaveSystem.NoSlot;
        if (!freshGame || TutorialManager.IsDone) return;
        if (UnityEngine.Object.FindFirstObjectByType<TutorialManager>() != null) return;

        new GameObject("Tutorial").AddComponent<TutorialManager>();
    }
}
