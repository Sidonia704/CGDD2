using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;

public static class Range115Preview
{
    public static string Capture(string assetPath,string output,bool top=false)
    {
        var source=AssetDatabase.LoadMainAssetAtPath(assetPath);
        var map=source as SpaceMapDefinition;var asset=source as VoxelAssetDefinition;
        if(!map&&!asset)throw new ArgumentException(assetPath);
        var items=map?map.instances.ToArray():new[]{new VoxelMapInstance{asset=asset}};
        var bounds=new Bounds();bool first=true;
        foreach(var i in items){if(!i.asset.initialLodMesh)continue;var b=i.asset.initialLodMesh.bounds;for(int k=0;k<8;k++){var p=i.Matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}}
        var preview=new PreviewRenderUtility();Texture2D image=null;RenderTexture target=null;var previous=RenderTexture.active;
        var material=new Material(Shader.Find("Starframe/Tools/VoxelPreview"));
        try
        {
            var camera=preview.camera;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.065f);
            camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=100000;
            Vector3 direction=top?new Vector3(0,1,.001f):new Vector3(1,.75f,1).normalized;
            var rotation=Quaternion.LookRotation(-direction,Vector3.up);var inverse=Quaternion.Inverse(rotation);float extent=0;
            for(int k=0;k<8;k++){var p=inverse*Vector3.Scale(bounds.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1));extent=Mathf.Max(extent,Mathf.Abs(p.y),Mathf.Abs(p.x)/1.5f);}
            camera.orthographicSize=Mathf.Max(1,extent*1.12f);camera.aspect=1.5f;camera.transform.SetPositionAndRotation(bounds.center+direction*Mathf.Max(100,bounds.size.magnitude*2),rotation);
            preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(40,-30,0);preview.lights[1].intensity=.5f;
            preview.BeginPreview(new Rect(0,0,1200,800),GUIStyle.none);
            foreach(var i in items){if(!i.asset.initialLodMesh)continue;material.SetColor("_BaseColor",i.asset.category.Contains("岩")||i.asset.category.Contains("Rock")?new Color(.5f,.52f,.55f):new Color(.8f,.79f,.74f));for(int s=0;s<i.asset.initialLodMesh.subMeshCount;s++)preview.DrawMesh(i.asset.initialLodMesh,i.Matrix,material,s);}
            preview.Render(true);var rendered=preview.EndPreview();target=RenderTexture.GetTemporary(1200,800,0,RenderTextureFormat.ARGB32);Graphics.Blit(rendered,target);RenderTexture.active=target;
            image=new Texture2D(1200,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,image.EncodeToPNG());return output;
        }
        finally{RenderTexture.active=previous;if(target)RenderTexture.ReleaseTemporary(target);if(image)UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);preview.Cleanup();}
    }
}
