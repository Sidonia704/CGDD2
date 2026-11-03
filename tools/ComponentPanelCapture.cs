using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
public static class ComponentPanelCapture
{
    public static string Main()
    {
        var scroll=GameObject.Find("ModuleLibraryScroll").GetComponent<ScrollRect>();
        Canvas.ForceUpdateCanvases();
        float contentHeight=scroll.content.rect.height;
        const int logicalWidth=356;
        int logicalHeight=Mathf.CeilToInt(contentHeight)+32;
        var cameraObject=new GameObject("Temporary component panel capture camera");
        var canvasObject=new GameObject("Temporary component panel capture",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        RenderTexture texture=null;Texture2D pixels=null;var previous=RenderTexture.active;
        try
        {
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.20f,.22f,1);
            camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=logicalHeight*.5f;camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.allowHDR=false;
            texture=new RenderTexture(logicalWidth*2,logicalHeight*2,24,RenderTextureFormat.ARGB32);texture.Create();camera.targetTexture=texture;
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.pixelPerfect=false;
            var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.scaleFactor=2;
            var clone=UnityEngine.Object.Instantiate(scroll.content.gameObject,canvasObject.transform,false);
            var rect=clone.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(16,-16);rect.sizeDelta=new Vector2(logicalWidth-32,contentHeight);
            foreach(var transform in canvasObject.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=30;
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=texture;
            pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();
            string path="Documentation/Thruster6-Weapons-2026-10-25/ComponentPanel.png";File.WriteAllBytes(path,pixels.EncodeToPNG());return path;
        }
        finally
        {
            RenderTexture.active=previous;if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);if(canvasObject)UnityEngine.Object.DestroyImmediate(canvasObject);
            foreach(var font in UnityEngine.Object.FindObjectsByType<Starframe.Presentation.LocalizedUiFont>(UnityEngine.FindObjectsSortMode.None))font.ApplyForContent();
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels);if(texture){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
