using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    public static class ComponentCatalogVisuals
    {
        const string Destination="Documentation/ComponentLibrary-2026-10-25/CatalogVisuals.png";
        [MenuItem("Starframe/Components/Render component contact sheet")]
        public static void Render()=>RenderTo(Destination);
        public static void RenderTo(string destination)
        {
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            if(!database)throw new InvalidOperationException("Install the component database first.");
            var entries=database.modules.Where(d=>d&&d.mesh&&d.material).OrderBy(d=>(int)d.shape).ToArray();
            const int columns=6,width=400,height=360,renderHeight=304,header=68;
            int rows=Mathf.CeilToInt(entries.Length/(float)columns);
            var sheet=new Texture2D(columns*width,rows*height+header,TextureFormat.RGB24,false);
            var background=new Color(.055f,.063f,.075f,1);
            var pixels=Enumerable.Repeat((Color32)background,sheet.width*sheet.height).ToArray();
            sheet.SetPixels32(pixels);
            Text(sheet,"STARFRAME COMPONENT LIBRARY",24,sheet.height-28,3,new Color(.94f,.95f,.96f));
            Text(sheet,entries.Length+" COMPONENTS / ORIGINAL + EXPANSION / ORTHOGRAPHIC MATERIAL REVIEW",24,sheet.height-54,1,new Color(.56f,.63f,.70f));
            var preview=new PreviewRenderUtility();
            var reflection=AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/_Starframe/Art/Materials/Ships/PlasticSoftboxReflection.asset");
            try
            {
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=background;
                preview.camera.orthographic=true;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=1000;preview.camera.allowHDR=true;
                preview.lights[0].color=new Color(1,.93f,.84f);preview.lights[0].intensity=1.8f;preview.lights[0].transform.rotation=Quaternion.Euler(40,-35,0);
                preview.lights[1].color=new Color(.65f,.79f,1);preview.lights[1].intensity=.9f;preview.lights[1].transform.rotation=Quaternion.Euler(330,150,0);
                preview.ambientColor=new Color(.34f,.37f,.42f);
                var direction=new Vector3(1.35f,1.05f,1.65f).normalized;
                var rotation=Quaternion.LookRotation(-direction,Vector3.up);
                for(int i=0;i<entries.Length;i++)
                {
                    var entry=entries[i];var bounds=entry.mesh.bounds;
                    float halfWidth=0,halfHeight=0;
                    for(int corner=0;corner<8;corner++)
                    {
                        var delta=Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                        var p=Quaternion.Inverse(rotation)*delta;halfWidth=Mathf.Max(halfWidth,Mathf.Abs(p.x));halfHeight=Mathf.Max(halfHeight,Mathf.Abs(p.y));
                    }
                    preview.camera.aspect=width/(float)renderHeight;
                    preview.camera.orthographicSize=Mathf.Max(halfHeight,halfWidth/preview.camera.aspect)*1.22f;
                    preview.camera.transform.SetPositionAndRotation(bounds.center+direction*Mathf.Max(8,bounds.extents.magnitude*4),rotation);
                    preview.BeginPreview(new Rect(0,0,width,renderHeight),GUIStyle.none);
                    preview.DrawMesh(entry.mesh,Matrix4x4.identity,entry.material,0);
                    preview.Render(true);
                    var rendered=preview.EndPreview();
                    var rt=RenderTexture.GetTemporary(width,renderHeight,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                    var previous=RenderTexture.active;
                    var tile=new Texture2D(width,renderHeight,TextureFormat.RGB24,false);
                    try
                    {
                        Graphics.Blit(rendered,rt);RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,width,renderHeight),0,0);tile.Apply();
                        int col=i%columns,row=i/columns;
                        int x=col*width,y=sheet.height-header-(row+1)*height;
                        sheet.SetPixels(x,y+height-renderHeight,width,renderHeight,tile.GetPixels());
                        Fill(sheet,x+12,y+50,width-24,1,new Color(.17f,.20f,.24f));
                        Text(sheet,((int)entry.shape).ToString("00")+"  "+entry.shape.ToString().ToUpperInvariant(),x+18,y+30,2,new Color(.9f,.93f,.95f));
                        var size=CellSize(entry);
                        Text(sheet,size+" / "+entry.massKg+" KG / "+entry.maxHp+" HP",x+18,y+10,1,new Color(.56f,.63f,.70f));
                    }
                    finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tile);}
                }
                sheet.Apply();Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllBytes(destination,sheet.EncodeToPNG());
                Debug.Log("Component contact sheet: "+Path.GetFullPath(destination));
            }
            finally{preview.Cleanup();UnityEngine.Object.DestroyImmediate(sheet);}
        }
        static string CellSize(ModuleDefinition d)
        {
            var min=d.occupiedCells[0];var max=min;
            foreach(var cell in d.occupiedCells){min=Vector3Int.Min(min,cell);max=Vector3Int.Max(max,cell);}
            var size=max-min+Vector3Int.one;return size.x+"X"+size.y+"X"+size.z;
        }
        static void Fill(Texture2D texture,int x,int y,int w,int h,Color color)
        {for(int j=0;j<h;j++)for(int i=0;i<w;i++)if(x+i>=0&&y+j>=0&&x+i<texture.width&&y+j<texture.height)texture.SetPixel(x+i,y+j,color);}
        internal static void Text(Texture2D texture,string value,int x,int y,int scale,Color color)
        {
            foreach(char ch in value.ToUpperInvariant())
            {
                if(Font.TryGetValue(ch,out string glyph))
                    for(int row=0;row<7;row++)for(int col=0;col<5;col++)
                        if(glyph[row*5+col]=='1')Fill(texture,x+col*scale,y-row*scale,scale,scale,color);
                x+=6*scale;
            }
        }
        static readonly Dictionary<char,string> Font=new Dictionary<char,string>
        {
            ['A']="01110100011000111111100011000110001",['B']="11110100011000111110100011000111110",
            ['C']="01111100001000010000100001000001111",['D']="11110100011000110001100011000111110",
            ['E']="11111100001000011110100001000011111",['F']="11111100001000011110100001000010000",
            ['G']="01111100001000010111100011000101111",['H']="10001100011000111111100011000110001",
            ['I']="11111001000010000100001000010011111",['J']="00111000100001000010000101001001100",
            ['K']="10001100101010011000101001001010001",['L']="10000100001000010000100001000011111",
            ['M']="10001110111010110101100011000110001",['N']="10001110011010110011100011000110001",
            ['O']="01110100011000110001100011000101110",['P']="11110100011000111110100001000010000",
            ['Q']="01110100011000110001101011001001101",['R']="11110100011000111110101001001010001",
            ['S']="01111100001000001110000010000111110",['T']="11111001000010000100001000010000100",
            ['U']="10001100011000110001100011000101110",['V']="10001100011000110001100010101000100",
            ['W']="10001100011000110101101011101110001",['X']="10001100010101000100010101000110001",
            ['Y']="10001100010101000100001000010000100",['Z']="11111000010001000100010001000011111",
            ['0']="01110100011001110101110011000101110",['1']="00100011000010000100001000010001110",
            ['2']="01110100010000100010001000100011111",['3']="11110000010000101110000010000111110",
            ['4']="00010001100101010010111110001000010",['5']="11111100001000011110000010000111110",
            ['6']="01110100001000011110100011000101110",['7']="11111000010001000100010000100001000",
            ['8']="01110100011000101110100011000101110",['9']="01110100011000101111000010000101110",
            ['/']="00001000100001000100010000100010000",['+']="00000001000010011111001000010000000",
            ['-']="00000000000000011111000000000000000",['.']="00000000000000000000000000011000110"
        };
    }
}
