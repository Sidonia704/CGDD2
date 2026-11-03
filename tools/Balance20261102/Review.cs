using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Settings;
using Starframe.App;
using Starframe.Construction;
using TMPro;

public static class BalanceReview
{
    public static string OpenReference()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play required");
        // Isolate every preview/autosave from the player's real current-ship.json.
        ShipWorkspace.UseStorageRoot(Path.GetFullPath("Temp/BalanceVisualWorkspace"));
        var data=JsonUtility.FromJson<BlueprintData>(File.ReadAllText("Documentation/Balance-2026-11-02/Blueprints/Balance-Standard.json"));
        ShipWorkspace.Set(data,false);LocalizationSettings.SelectedLocale=LocalizationSettings.AvailableLocales.GetLocale("en");
        UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>().OpenEditor();return "Opening isolated balanced reference in assembly";
    }
    public static string Capture(string locale)
    {
        LocalizationSettings.SelectedLocale=LocalizationSettings.AvailableLocales.GetLocale(locale);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Documentation/Balance-2026-11-02/assembly-"+locale+".png"));
        return string.Join("\n",UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None).Where(t=>t.name=="AssemblyCost"||t.name=="ModuleStatistics").Select(t=>t.name+": "+t.text));
    }
    public static string RestoreScene()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play first");
        // This project can keep statics across Play stops, so explicitly leave the preview scope.
        ShipWorkspace.UseStorageRoot(Application.persistentDataPath);ShipWorkspace.Reload();
        EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity");return "Hangar restored; real workspace remains intact.";
    }
}
