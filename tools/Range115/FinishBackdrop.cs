public static class FinishBackdrop { public static string Run() {
foreach(var name in new[]{"BackdropActualRight","BackdropActualLeft","BackdropActualFar","BackdropDenseForward","BackdropDenseLeft","BackdropDenseRight","BackdropDenseFar"})UnityEditor.AssetDatabase.DeleteAsset("Assets/tmp/"+name+".png");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity");
UnityEditor.Selection.activeObject=UnityEditor.AssetDatabase.LoadAssetAtPath<Starframe.Maps.SpaceMapDefinition>("Assets/_Starframe/Maps/UniformVoxels/SaturnRingVoxels/Layouts/SaturnRingVoxels.asset");
return "Stopped; Hangar restored, updated mission layout selected.";

} }
