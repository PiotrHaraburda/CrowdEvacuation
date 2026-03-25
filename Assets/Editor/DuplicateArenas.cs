using UnityEditor;
using UnityEngine;

public static class DuplicateArenas
{
    private const int Columns = 4;
    private const int Rows = 4;
    private const float Spacing = 30f;

    [MenuItem("Tools/Duplicate Training Arenas (4x4)")]
    public static void Execute()
    {
        var original = Selection.activeGameObject;
        if (original == null)
        {
            return;
        }

        if (!EditorUtility.DisplayDialog("Duplicate Arenas",
                $"This will create 15 copies of '{original.name}' in a 4×4 grid with {Spacing}m spacing.\n\n" +
                "The original will be moved to position (0, 0, 0).\n\nContinue?",
                "Yes", "Cancel"))
        {
            return;
        }

        Undo.SetCurrentGroupName("Duplicate Training Arenas");
        var undoGroup = Undo.GetCurrentGroup();

        Undo.RecordObject(original.transform, "Move original arena");
        original.transform.position = Vector3.zero;
        original.name = "TrainingArena_0";

        var count = 1;
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                if (row == 0 && col == 0) continue;

                var pos = new Vector3(col * Spacing, 0f, row * Spacing);

                var copy = Object.Instantiate(original, pos, Quaternion.identity);
                copy.name = $"TrainingArena_{count}";
                Undo.RegisterCreatedObjectUndo(copy, "Create arena copy");

                count++;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.DisplayDialog("Done", $"Created {count} arenas total.", "OK");
    }
}