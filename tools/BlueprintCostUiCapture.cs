using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using Starframe.App;
public static class BlueprintCostUiCapture
{
    static GameAppBootstrap App=>UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();
    public static object TargetPicker()
    {
        if(!Application.isPlaying||!App||!App.IsReady)throw new InvalidOperationException("Ready frontend Play session required.");
        App.ShowRangeSetup();typeof(GameAppBootstrap).GetMethod("ShowRangeTargetPicker",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(App,null);
        return Capture("target-picker");
    }
    public static object Library()
    {
        if(!Application.isPlaying||!App||!App.IsReady)throw new InvalidOperationException("Ready frontend Play session required.");
        App.ShowPage(AppPage.BlueprintLibrary);return Capture("blueprint-library");
    }
    static object Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        var labels=UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)
            .Where(t=>t.gameObject.activeInHierarchy&&(t.name.StartsWith("TargetCost_")||t.name.StartsWith("LibraryCost_")||t.name=="RangeTargetCost")).ToArray();
        foreach(var text in labels)text.ForceMeshUpdate();
        string directory="Documentation/BlueprintCostReview";Directory.CreateDirectory(directory);string path=Path.GetFullPath(directory+"/"+name+".png");
        ScreenCapture.CaptureScreenshot(path);
        return new{path,costLabels=labels.Select(t=>new{t.name,t.text,t.fontSize,t.isTextTruncated}).ToArray(),note="Screenshot is written at end of frame; read it after the next advancing frame."};
    }
}
