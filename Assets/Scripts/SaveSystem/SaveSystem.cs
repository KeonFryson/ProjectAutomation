using System;
using System.IO;
using UnityEngine;

/// <summary>File IO for save slots + shared constants. Slot 0 = autosave, 1..3 = manual slots.</summary>
public static class SaveSystem
{
    public const string MenuScene = "MainMenu";
    public const string GameScene = "SampleScene";

    public const int ManualSlots = 3;
    public const int AutosaveSlot = 0;
    public const int NoSlot = -1;

    /// <summary>Set by the main menu; the game scene loads this slot on start (NoSlot = new game).</summary>
    public static int PendingLoadSlot = NoSlot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { PendingLoadSlot = NoSlot; }

    private static string Dir
    {
        get
        {
            string d = Path.Combine(Application.persistentDataPath, "saves");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string PathFor(int slot)
    {
        return Path.Combine(Dir, slot == AutosaveSlot ? "autosave.json" : "slot" + slot + ".json");
    }

    public static string SlotName(int slot)
    {
        return slot == AutosaveSlot ? "Autosave" : "Slot " + slot;
    }

    public static bool Exists(int slot) { return File.Exists(PathFor(slot)); }

    /// <summary>Returns null if the slot is empty or unreadable.</summary>
    public static SaveData Read(int slot)
    {
        try
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning("SaveSystem: could not read " + SlotName(slot) + ": " + e.Message);
            return null;
        }
    }

    public static bool Write(int slot, SaveData data)
    {
        try
        {
            string path = PathFor(slot);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("SaveSystem: could not write " + SlotName(slot) + ": " + e.Message);
            return false;
        }
    }

    public static void Delete(int slot)
    {
        try { if (File.Exists(PathFor(slot))) File.Delete(PathFor(slot)); }
        catch (Exception e) { Debug.LogWarning("SaveSystem: could not delete " + SlotName(slot) + ": " + e.Message); }
    }

    /// <summary>Slot (including autosave) with the newest save, or NoSlot.</summary>
    public static int MostRecentSlot()
    {
        int best = NoSlot;
        long bestTicks = long.MinValue;
        for (int slot = 0; slot <= ManualSlots; slot++)
        {
            SaveData d = Read(slot);
            if (d == null) continue;
            if (d.savedAtTicks > bestTicks) { bestTicks = d.savedAtTicks; best = slot; }
        }
        return best;
    }

    public static string FormatPlayTime(float seconds)
    {
        int t = Mathf.Max(0, (int)seconds);
        return (t / 3600) + ":" + ((t / 60) % 60).ToString("00") + ":" + (t % 60).ToString("00");
    }

    public static string FormatSavedAt(long ticks)
    {
        if (ticks <= 0) return "unknown date";
        return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
