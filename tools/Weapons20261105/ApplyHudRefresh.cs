using UnityEditor;
using UnityEditor.SceneManagement;
using Starframe.Presentation;
public static class ApplyHudRefresh
{
    public static string Run()
    {
        if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
        var theme=AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Starframe/Resources/UI/WorkbenchTheme.asset");
        theme.hitFeedbackMaxRate=12;
        theme.reticleIdleGap=9.9f;theme.reticleHitGap=2.7f;theme.reticleArmLength=9;theme.hitMarkerLength=10.8f;
        EditorUtility.SetDirty(theme);AssetDatabase.SaveAssetIfDirty(theme);
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/_Starframe/Scenes/Frontend/Hangar.unity")
            EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity");
        return "Saved 60Hz HUD, red cost above enemy name, 12Hz hit feedback, 10% smaller reticle and 5% smaller flight HUD. No tests run; Play stopped.";
    }
}
