using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class SaturnPreview
{
    public static string Capture()
    {
        if(Application.isPlaying)throw new System.InvalidOperationException("Stop Play first.");
        var original=SceneManager.GetActiveScene();
        var preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var previous=RenderTexture.active;
        RenderTexture target=null;Texture2D pixels=null;Camera camera=null;
        try
        {
            SceneManager.SetActiveScene(preview);
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/Materials/Environment/SaturnRangeSky.mat");
            camera=new GameObject("Temporary Saturn material preview").AddComponent<Camera>();
            camera.transform.rotation=Quaternion.Euler(0,-40,0);camera.clearFlags=CameraClearFlags.Skybox;camera.fieldOfView=60;camera.allowHDR=true;
            var data=camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.renderPostProcessing=false;
            target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;camera.Render();
            RenderTexture.active=target;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
            string path=Path.GetFullPath("Documentation/CombatUpdate20261102/Saturn-final.png");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pixels.EncodeToPNG());return path;
        }
        finally
        {
            RenderTexture.active=previous;if(camera){camera.targetTexture=null;Object.DestroyImmediate(camera.gameObject);}if(pixels)Object.DestroyImmediate(pixels);if(target){target.Release();Object.DestroyImmediate(target);}
            if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(preview,true);
        }
    }
}
