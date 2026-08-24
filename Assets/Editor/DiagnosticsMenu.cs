using MechaChameleon;
using UnityEditor;
using UnityEngine;

namespace MechaChameleon.Editor
{
    public static class DiagnosticsMenu
    {
        [MenuItem("Mecha Chameleon/Open Diagnostics Folder")]
        static void OpenFolder()
        {
            EditorUtility.RevealInFinder(GameDiagnostics.LogDirectory);
        }

        [MenuItem("Mecha Chameleon/Print Diagnostics Path")]
        static void PrintPath()
        {
            var path = string.IsNullOrEmpty(GameDiagnostics.CurrentLogPath)
                ? $"No active Play session. Folder: {GameDiagnostics.LogDirectory}"
                : GameDiagnostics.CurrentLogPath;
            Debug.Log($"[Diagnostics] {path}");
        }
    }
}
