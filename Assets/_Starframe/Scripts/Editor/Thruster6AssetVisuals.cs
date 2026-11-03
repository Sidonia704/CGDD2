using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    public static class Thruster6AssetVisuals
    {
        [MenuItem("Starframe/Components/Render Thruster6 six faces")]
        public static void Render()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var module=db.Get(BlockShape.Thruster6);if(!module||!module.mesh)throw new InvalidOperationException("Install Thruster6 first.");
            const int width=384,viewHeight=320,tileHeight=372;
            var sheet=new Texture2D(width*3,tileHeight*2+64,TextureFormat.RGB24,false);
            var background=new Color(.055f,.063f,.075f,1);
            var pixels=new Color[sheet.width*sheet.height];for(int i=0;i<pixels.Length;i++)pixels[i]=background;sheet.SetPixels(pixels);
            ComponentCatalogVisuals.Text(sheet,"THRUSTER6 / FIVE MEASURED EXHAUST FACES",18,sheet.height-22,2,new Color(.93f,.95f,.97f));
            ComponentCatalogVisuals.Text(sheet,"45000 N PER NOZZLE / UNDERSIDE MOUNT / ONE CELL CLEARANCE",18,sheet.height-45,1,new Color(.6f,.67f,.74f));
            var preview=new PreviewRenderUtility();
            try
            {
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=background;
                preview.camera.orthographic=true;preview.camera.orthographicSize=.64f;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=50;preview.camera.aspect=width/(float)viewHeight;
                preview.lights[0].intensity=1.8f;preview.lights[0].transform.rotation=Quaternion.Euler(40,-35,0);preview.lights[1].intensity=.9f;preview.lights[1].transform.rotation=Quaternion.Euler(330,150,0);preview.ambientColor=new Color(.34f,.37f,.42f);
                var directions=new[]{Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back,Vector3.down};
                var names=new[]{"EXHAUST +Y","EXHAUST +X","EXHAUST -X","EXHAUST +Z","EXHAUST -Z","MOUNT -Y / NO EXHAUST"};
                for(int i=0;i<directions.Length;i++)
                {
                    var direction=directions[i];var bounds=module.mesh.bounds;
                    preview.camera.transform.SetPositionAndRotation(bounds.center+direction*4,Quaternion.LookRotation(-direction,Mathf.Abs(direction.y)>.9f?Vector3.forward:Vector3.up));
                    preview.BeginPreview(new Rect(0,0,width,viewHeight),GUIStyle.none);preview.DrawMesh(module.mesh,Matrix4x4.identity,module.material,0);preview.Render(true);
                    var rendered=preview.EndPreview();var rt=RenderTexture.GetTemporary(width,viewHeight,0);var previous=RenderTexture.active;var tile=new Texture2D(width,viewHeight,TextureFormat.RGB24,false);
                    try
                    {
                        Graphics.Blit(rendered,rt);RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,width,viewHeight),0,0);tile.Apply();
                        int x=(i%3)*width,y=sheet.height-64-(i/3+1)*tileHeight;
                        sheet.SetPixels(x,y+tileHeight-viewHeight,width,viewHeight,tile.GetPixels());
                        ComponentCatalogVisuals.Text(sheet,names[i],x+14,y+30,2,new Color(.88f,.93f,.96f));
                        if(i<5){var p=module.localNozzlePositions[i];ComponentCatalogVisuals.Text(sheet,"MOUTH "+p.x.ToString("F3")+" / "+p.y.ToString("F3")+" / "+p.z.ToString("F3"),x+14,y+9,1,new Color(.57f,.67f,.76f));}
                    }
                    finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tile);}
                }
                sheet.Apply();Directory.CreateDirectory(Thruster6Installer.Evidence);File.WriteAllBytes(Thruster6Installer.Evidence+"Thruster6-SixFaces.png",sheet.EncodeToPNG());
            }
            finally{preview.Cleanup();UnityEngine.Object.DestroyImmediate(sheet);}
        }
    }
}
