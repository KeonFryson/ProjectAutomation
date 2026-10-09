#if UNITY_EDITOR
using UnityEditor;

/// <summary>Shows the ID as a read-only field at the top of item/building assets.</summary>
[CustomEditor(typeof(GameDefinition), true)]
public class GameDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var def = (GameDefinition)target;
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.TextField("ID", string.IsNullOrEmpty(def.Id) ? "(assigned automatically)" : def.Id);
        DrawDefaultInspector();
    }
}
#endif