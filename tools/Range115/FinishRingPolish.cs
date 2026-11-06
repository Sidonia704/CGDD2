using UnityEditor;
using UnityEditor.SceneManagement;
public static class FinishRingPolish
{
    public static string Run()
    {
        foreach(string name in new[]{"RingPolishForward","RingPolishMotion","RingPolishForwardFinal","RingPolishTopFinal"})
            AssetDatabase.DeleteAsset("Assets/tmp/"+name+".png");
        EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity");
        return "Mission saved; verification images archived; Hangar restored.";
    }
}
