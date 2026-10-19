using System;
using UnityEngine;

/// <summary>
/// Player settings (audio, display, camera, gameplay). Static, saved in PlayerPrefs, applied
/// automatically at startup. Read the properties anywhere (GameSettings.PanSpeed ...);
/// assigning one applies and stores it immediately. Call Save() when done editing
/// (SettingsPanel does this when it closes). Subscribe to Changed to react live.
/// </summary>
public static class GameSettings
{
    private const string K = "BoxFactory.Settings.";

    /// <summary>Fired after any setting changes.</summary>
    public static event Action Changed;

    // ---- Defaults ----
    public const float DefaultAutosaveSeconds = 120f;

    private static float masterVolume = 1f;
    private static bool fullscreen = true;
    private static int resWidth, resHeight;   // 0 = leave the current resolution alone
    private static bool vsync = true;
    private static float panSpeed = 1f;
    private static float zoomSpeed = 1f;
    private static bool edgePan;
    private static bool showHints = true;
    private static bool autosaveEnabled = true;
    private static float autosaveInterval = DefaultAutosaveSeconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Changed = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Load();
        AudioListener.volume = masterVolume;
        QualitySettings.vSyncCount = vsync ? 1 : 0;
        if (resWidth > 0) ApplyDisplay(); // only touch the window if the player chose something
    }

    // ---------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------

    public static float MasterVolume
    {
        get { return masterVolume; }
        set
        {
            masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(K + "Volume", masterVolume);
            AudioListener.volume = masterVolume;
            Changed?.Invoke();
        }
    }

    public static bool Fullscreen
    {
        get { return fullscreen; }
        set
        {
            fullscreen = value;
            PlayerPrefs.SetInt(K + "Fullscreen", value ? 1 : 0);
            ApplyDisplay();
            Changed?.Invoke();
        }
    }

    /// <summary>0 = not chosen (keeps whatever the window currently is).</summary>
    public static int ResolutionWidth { get { return resWidth; } }
    public static int ResolutionHeight { get { return resHeight; } }

    public static void SetResolution(int w, int h)
    {
        resWidth = w;
        resHeight = h;
        PlayerPrefs.SetInt(K + "ResW", w);
        PlayerPrefs.SetInt(K + "ResH", h);
        ApplyDisplay();
        Changed?.Invoke();
    }

    public static bool VSync
    {
        get { return vsync; }
        set
        {
            vsync = value;
            PlayerPrefs.SetInt(K + "VSync", value ? 1 : 0);
            QualitySettings.vSyncCount = value ? 1 : 0;
            Changed?.Invoke();
        }
    }

    /// <summary>Multiplier on keyboard / edge pan speed (1 = as set on the camera).</summary>
    public static float PanSpeed
    {
        get { return panSpeed; }
        set { panSpeed = Mathf.Clamp(value, 0.1f, 5f); PlayerPrefs.SetFloat(K + "PanSpeed", panSpeed); Changed?.Invoke(); }
    }

    /// <summary>Multiplier on mouse-wheel zoom speed.</summary>
    public static float ZoomSpeed
    {
        get { return zoomSpeed; }
        set { zoomSpeed = Mathf.Clamp(value, 0.1f, 5f); PlayerPrefs.SetFloat(K + "ZoomSpeed", zoomSpeed); Changed?.Invoke(); }
    }

    public static bool EdgePan
    {
        get { return edgePan; }
        set { edgePan = value; PlayerPrefs.SetInt(K + "EdgePan", value ? 1 : 0); Changed?.Invoke(); }
    }

    /// <summary>Bottom-of-screen controls hint.</summary>
    public static bool ShowHints
    {
        get { return showHints; }
        set { showHints = value; PlayerPrefs.SetInt(K + "Hints", value ? 1 : 0); Changed?.Invoke(); }
    }

    public static bool AutosaveEnabled
    {
        get { return autosaveEnabled; }
        set { autosaveEnabled = value; PlayerPrefs.SetInt(K + "Autosave", value ? 1 : 0); Changed?.Invoke(); }
    }

    public static float AutosaveInterval
    {
        get { return autosaveInterval; }
        set
        {
            autosaveInterval = Mathf.Max(10f, value);
            PlayerPrefs.SetFloat(K + "AutosaveInterval", autosaveInterval);
            Changed?.Invoke();
        }
    }

    // ---------------------------------------------------------------
    // Load / save / reset
    // ---------------------------------------------------------------

    private static void Load()
    {
        masterVolume = PlayerPrefs.GetFloat(K + "Volume", 1f);
        fullscreen = PlayerPrefs.GetInt(K + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        resWidth = PlayerPrefs.GetInt(K + "ResW", 0);
        resHeight = PlayerPrefs.GetInt(K + "ResH", 0);
        vsync = PlayerPrefs.GetInt(K + "VSync", QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
        panSpeed = PlayerPrefs.GetFloat(K + "PanSpeed", 1f);
        zoomSpeed = PlayerPrefs.GetFloat(K + "ZoomSpeed", 1f);
        edgePan = PlayerPrefs.GetInt(K + "EdgePan", 0) == 1;
        showHints = PlayerPrefs.GetInt(K + "Hints", 1) == 1;
        autosaveEnabled = PlayerPrefs.GetInt(K + "Autosave", 1) == 1;
        autosaveInterval = Mathf.Max(10f, PlayerPrefs.GetFloat(K + "AutosaveInterval", DefaultAutosaveSeconds));
    }

    public static void Save() { PlayerPrefs.Save(); }

    public static void ResetToDefaults()
    {
        MasterVolume = 1f;
        PanSpeed = 1f;
        ZoomSpeed = 1f;
        EdgePan = false;
        ShowHints = true;
        AutosaveEnabled = true;
        AutosaveInterval = DefaultAutosaveSeconds;
        VSync = true;

        resWidth = resHeight = 0;
        PlayerPrefs.SetInt(K + "ResW", 0);
        PlayerPrefs.SetInt(K + "ResH", 0);
        Fullscreen = true; // applies the display and fires Changed
        Save();
    }

    private static void ApplyDisplay()
    {
        FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        if (resWidth > 0 && resHeight > 0) Screen.SetResolution(resWidth, resHeight, mode);
        else Screen.fullScreenMode = mode;
    }
}
