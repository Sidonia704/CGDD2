using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public static class RingTopCapture
{
    static Camera camera;
    static RenderTexture target, previous;
    static int startFrame;
    public static string Begin()
    {
        camera=Camera.main;previous=camera.targetTexture;
        target=new RenderTexture(1200,1800,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){name="Ring top verification"};
        target.Create();camera.targetTexture=target;camera.aspect=1200f/1800;
        camera.fieldOfView=80;startFrame=Time.frameCount;
        RenderPipelineManager.endCameraRendering+=Capture;
        return "Capturing next regular camera render including instanced world rocks.";
    }
    static void Capture(ScriptableRenderContext context,Camera rendered)
    {
        if(rendered!=camera||Time.frameCount<=startFrame)return;
        RenderPipelineManager.endCameraRendering-=Capture;
        var old=RenderTexture.active;RenderTexture.active=target;
        var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
        Directory.CreateDirectory("tmp/VoxelMaps");
        File.WriteAllBytes("tmp/VoxelMaps/RingPolishTopFinal.png",pixels.EncodeToPNG());
        RenderTexture.active=old;camera.targetTexture=previous;camera.ResetAspect();
        Object.Destroy(pixels);target.Release();Object.Destroy(target);
    }
}
