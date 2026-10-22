using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.Presentation.Editor
{
    /// <summary>Renders replaceable operation-card artwork from real geometry, without game UI.</summary>
    public static class VisualPolishArtwork
    {
        const string Folder="Assets/_Starframe/Art/UI/Operations";
        [MenuItem("Starframe/Visuals/Render operation artwork")]
        public static void Render()
        {
            Directory.CreateDirectory(Folder);
            RenderCard("IceGiant",false);
            RenderCard("Frontier",true);
            AssetDatabase.Refresh();
        }
        static void RenderCard(string name,bool rocky)
        {
            var owned=new List<UnityEngine.Object>();
            var masks=new Dictionary<Light,int>();
            var ambient=RenderSettings.ambientMode;var ambientLight=RenderSettings.ambientLight;
            var active=RenderTexture.active;
            try
            {
                foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {masks.Add(light,light.cullingMask);light.cullingMask&=~(1<<30);}
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.016f,.02f,.025f);
                var root=new GameObject("Operation artwork"){hideFlags=HideFlags.HideAndDontSave};owned.Add(root);
                var texture=new Texture2D(1024,512,TextureFormat.RGBA32,true){wrapMode=TextureWrapMode.Repeat};owned.Add(texture);
                var pixels=new Color[1024*512];
                for(int y=0;y<512;y++)for(int x=0;x<1024;x++)
                {
                    float u=x/1024f,v=y/512f;
                    float broad=Mathf.PerlinNoise(u*9+11,v*12+6),fine=Mathf.PerlinNoise(u*83+31,v*89+13);
                    float bands=.5f+.5f*Mathf.Sin((v*27+broad*.7f)*Mathf.PI);
                    float level=rocky?Mathf.Clamp01(broad*.72f+fine*.28f):Mathf.Clamp01(.28f+bands*.22f+broad*.35f+fine*.15f);
                    var dark=rocky?new Color(.105f,.11f,.115f):new Color(.13f,.19f,.22f);
                    var bright=rocky?new Color(.50f,.50f,.48f):new Color(.58f,.66f,.67f);
                    pixels[y*1024+x]=Color.Lerp(dark,bright,level);
                }
                texture.SetPixels(pixels);texture.Apply();
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));owned.Add(material);
                material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
                material.SetFloat("_Smoothness",rocky?.08f:.27f);material.SetFloat("_Metallic",0);
                var planet=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));planet.layer=30;
                var sphere=Sphere();owned.Add(sphere);planet.GetComponent<MeshFilter>().sharedMesh=sphere;
                planet.transform.SetParent(root.transform,false);planet.transform.localPosition=rocky?new Vector3(3.2f,1.1f,15):new Vector3(2.5f,-.25f,15);
                planet.transform.localScale=Vector3.one*(rocky?8.8f:9.4f);planet.transform.rotation=Quaternion.Euler(7,10,-14);
                planet.GetComponent<Renderer>().sharedMaterial=material;
                var key=new GameObject("Artwork key");key.transform.SetParent(root.transform,false);key.transform.rotation=Quaternion.Euler(25,rocky?58:-48,0);
                var sun=key.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2.6f;sun.color=rocky?new Color(1,.98f,.94f):new Color(.87f,.94f,1);sun.cullingMask=1<<30;sun.shadows=LightShadows.None;
                var cameraObject=new GameObject("Artwork camera");cameraObject.transform.SetParent(root.transform,false);
                var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=36;camera.aspect=640f/580;camera.clearFlags=CameraClearFlags.Skybox;
                camera.cullingMask=1<<30;camera.nearClipPlane=.1f;camera.farClipPlane=1000;camera.allowHDR=false;
                var target=new RenderTexture(640,580,24,RenderTextureFormat.ARGB32);owned.Add(target);camera.targetTexture=target;
                camera.Render();RenderTexture.active=target;
                var output=new Texture2D(640,580,TextureFormat.RGB24,false);owned.Add(output);
                output.ReadPixels(new Rect(0,0,640,580),0,0);output.Apply();
                File.WriteAllBytes(Folder+"/"+name+".png",output.EncodeToPNG());camera.targetTexture=null;
            }
            finally
            {
                RenderTexture.active=active;
                foreach(var pair in masks)if(pair.Key)pair.Key.cullingMask=pair.Value;
                RenderSettings.ambientMode=ambient;RenderSettings.ambientLight=ambientLight;
                for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);
            }
        }
        static Mesh Sphere()
        {
            const int segments=96,rings=64;
            var vertices=new Vector3[(segments+1)*(rings+1)];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
            var triangles=new int[segments*rings*6];int t=0;
            for(int y=0;y<=rings;y++)for(int x=0;x<=segments;x++)
            {
                float theta=Mathf.PI*y/rings,phi=Mathf.PI*2*x/segments;int i=y*(segments+1)+x;
                var n=new Vector3(Mathf.Sin(theta)*Mathf.Cos(phi),Mathf.Cos(theta),Mathf.Sin(theta)*Mathf.Sin(phi));
                normals[i]=n;vertices[i]=n*.5f;uv[i]=new Vector2((float)x/segments,1-(float)y/rings);
                if(x==segments||y==rings)continue;
                int b=i+segments+1;
                triangles[t++]=i;triangles[t++]=i+1;triangles[t++]=b;
                triangles[t++]=i+1;triangles[t++]=b+1;triangles[t++]=b;
            }
            var mesh=new Mesh{name="Smooth operation globe",vertices=vertices,normals=normals,uv=uv,triangles=triangles};mesh.RecalculateBounds();return mesh;
        }
    }
}
