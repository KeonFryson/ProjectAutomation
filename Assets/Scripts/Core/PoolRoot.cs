using UnityEngine;

/// <summary>
/// Shared parent for all pooled objects. Persists across scene loads so
/// pooled instances are never silently destroyed out from under the pools.
/// </summary>
public static class PoolRoot
{
    private static Transform root;

    public static Transform Get()
    {
        if (root == null)
        {
            var go = new GameObject("PooledObjects");
            Object.DontDestroyOnLoad(go);
            root = go.transform;
        }
        return root;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { root = null; }
}