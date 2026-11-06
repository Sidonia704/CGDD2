using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Starframe.Construction;
using Starframe.App;
using TMPro;

public static class FigmaUIFinish
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before finishing.");
        Application.runInBackground = bool.Parse(File.ReadAllText("Documentation/FigmaUI-2026-11-06/review-original-background.txt"));
        TraceDocsExporter.ExportForAutomation();
        var font = Resources.Load<TMP_FontAsset>("UI/Fonts/ChineseFallback");
        return new { playing = Application.isPlaying, scene = SceneManager.GetActiveScene().path,
            background = Application.runInBackground, chineseGlyphScale = font.faceInfo.scale,
            workspace = ShipWorkspace.WorkspacePath, compileFailed = EditorUtility.scriptCompilationFailed };
    }
}
