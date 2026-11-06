using UnityEditor;
using UnityEditor.SceneManagement;

public static class FinishBeltVolume
{
    public static string Run()
    {
        string[] names = { "BeltVolumeForward", "BeltVolumeForwardFinal", "BeltVolumeLeftFinal", "BeltVolumeRightFinal" };
        foreach (string name in names)
            AssetDatabase.DeleteAsset("Assets/tmp/" + name + ".png");
        EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity");
        return "Belt previews archived; temporary image assets removed; Hangar restored.";
    }
}
