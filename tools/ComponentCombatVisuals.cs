using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;

// Isolated still-life of the real runtime rendering pools. It does not simulate a battle.
public static class ComponentCombatVisuals
{
    const string Destination="Documentation/Thruster6-Weapons-2026-10-25/CombatVisuals.png";
    const int Width=2000,Header=106,RowHeight=286,ModelWidth=310,LaneWidth=1640,RenderHeight=226,Footer=52;
    static readonly Color Background=new Color(.045f,.055f,.072f,1),Rule=new Color(.15f,.19f,.24f,1);
    static readonly BlockShape[] Shapes={BlockShape.Gun3,BlockShape.Laser3,BlockShape.Railgun3,BlockShape.Missile2,BlockShape.Sword2};
    static readonly string[] Titles={"GUN 3 / SIX BARRELS","LASER 3 / RAPID CANNON","RAILGUN 3 / HIGH VELOCITY","MISSILE 2 / TWIN GUIDED RACK","SWORD 2 / 40 M BLUE BLADE"};
    static readonly string[] Notes={
        "ORANGE KINETIC TRACERS / ALTERNATING REAL MUZZLES",
        "BLUE ENERGY BOLTS / ACTUAL POOL GEOMETRY",
        "CONTINUOUS GOLD BEAM / WHITE CORE / 420 M TAIL / IMPACT FADE",
        "REAL MISSILE MESHES / POOLED CURVED ORANGE EXHAUST",
        "PERSISTENT TRIANGULAR BODY + INDEPENDENT LIGHT BLADE"
    };
    public static string Render()
    {
        var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        if(!database)throw new InvalidOperationException("The component database is missing.");
        var sheet=new Texture2D(Width,Header+RowHeight*Shapes.Length+Footer,TextureFormat.RGB24,false);
        sheet.SetPixels32(Enumerable.Repeat((Color32)Background,sheet.width*sheet.height).ToArray());
        Text(sheet,"STARFRAME / RUNTIME WEAPON VISUALS",24,sheet.height-32,3,new Color(.93f,.96f,1));
        Text(sheet,"STATIC PREVIEW OF THE REAL MODELS AND RENDERING POOLS / NO SCENE OR SAVE MODIFIED",26,sheet.height-68,1,new Color(.56f,.67f,.79f));
        try
        {
            for(int index=0;index<Shapes.Length;index++)
            {
                var definition=database.Get(Shapes[index]);
                if(!definition||!definition.weapon||!definition.mesh)throw new InvalidOperationException("Missing "+Shapes[index]);
                RenderLane(sheet,definition,index);
            }
            Text(sheet,"POSED SHOTS / FUNCTIONAL COMBAT IS VERIFIED SEPARATELY BY THE UNITY TEST SUITE",26,29,1,new Color(.52f,.63f,.75f));
            sheet.Apply();Directory.CreateDirectory(Path.GetDirectoryName(Destination));File.WriteAllBytes(Destination,sheet.EncodeToPNG());
            return Path.GetFullPath(Destination);
        }
        finally{UnityEngine.Object.DestroyImmediate(sheet);}
    }
    static void RenderLane(Texture2D sheet,ModuleDefinition definition,int index)
    {
        var preview=new PreviewRenderUtility();
        var root=new GameObject("Isolated combat visual lane "+definition.shape);
        root.hideFlags=HideFlags.HideAndDontSave;
        preview.AddSingleGO(root);
        int y=sheet.height-Header-(index+1)*RowHeight;
        try
        {
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=Background;
            preview.camera.orthographic=true;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=1000;preview.camera.allowHDR=true;
            preview.lights[0].color=new Color(1,.94f,.85f);preview.lights[0].intensity=1.8f;preview.lights[0].transform.rotation=Quaternion.Euler(40,-35,0);
            preview.lights[1].color=new Color(.65f,.80f,1);preview.lights[1].intensity=1;preview.lights[1].transform.rotation=Quaternion.Euler(325,150,0);
            preview.ambientColor=new Color(.34f,.38f,.44f);
            // The left tile makes the real component readable alongside the full-length lane.
            var bounds=definition.mesh.bounds;
            var direction=new Vector3(1.35f,1.05f,1.65f).normalized;
            preview.camera.aspect=ModelWidth/(float)RenderHeight;
            preview.camera.orthographicSize=bounds.extents.magnitude*1.14f;
            preview.camera.transform.SetPositionAndRotation(bounds.center+direction*50,Quaternion.LookRotation(-direction,Vector3.up));
            preview.BeginPreview(new Rect(0,0,ModelWidth,RenderHeight),GUIStyle.none);
            preview.DrawMesh(definition.mesh,Matrix4x4.identity,definition.material,0);
            preview.Render(true);Copy(preview.EndPreview(),sheet,20,y+48,ModelWidth,RenderHeight);

            var source=new GameObject(definition.shape+" actual module",typeof(MeshFilter),typeof(MeshRenderer));
            source.transform.SetParent(root.transform,false);source.transform.localRotation=Quaternion.Euler(0,90,0);
            source.GetComponent<MeshFilter>().sharedMesh=definition.shape==BlockShape.Missile2&&definition.weapon.launcherMesh?definition.weapon.launcherMesh:definition.mesh;
            source.GetComponent<MeshRenderer>().sharedMaterial=definition.material;
            ModuleGlowAccents.Attach(source.transform,definition.shape,definition.mesh,null,definition.muzzleLocalPosition);
            var pool=root.AddComponent<LaserVisualPool>();pool.Initialize(Shader.Find("Starframe/Combat/VoxelGlow"));
            var special=root.AddComponent<SpecialWeaponVisualPool>();special.Initialize();
            var flashes=root.AddComponent<CombatVfxPool>();flashes.Initialize(Shader.Find("Starframe/Combat/VoxelGlow"),definition.material);
            var weapon=definition.weapon;Vector3 forward=source.transform.forward;
            Vector3 muzzle=source.transform.TransformPoint(definition.muzzleLocalPosition);
            float laneLength=85;
            if(definition.shape==BlockShape.Gun3||definition.shape==BlockShape.Laser3||definition.shape==BlockShape.Railgun3)
            {
                float[] distances=definition.shape==BlockShape.Gun3?new[]{25f,65f}:definition.shape==BlockShape.Laser3?new[]{28f,72f}:new[]{520f};
                laneLength=definition.shape==BlockShape.Railgun3?560:definition.shape==BlockShape.Laser3?90:85;
                for(int shot=0;shot<distances.Length;shot++)
                {
                    Vector3 offset=weapon.muzzleOffsets!=null&&weapon.muzzleOffsets.Length>0?weapon.muzzleOffsets[shot%weapon.muzzleOffsets.Length]:Vector3.zero;
                    Vector3 local=definition.muzzleLocalPosition+offset,origin=source.transform.TransformPoint(local);
                    // Authored world width is used here: no dependency on the user's Camera.main.
                    int handle=pool.Acquire(origin,forward,weapon.projectileDiameter,weapon.visualLength,weapon.tailLength,source.transform,local,weapon.visualWidth,weapon.visualIntensity,weapon.effectColor,0,weapon.tracerStyle,weapon.tracerLingerSeconds);
                    pool.Move(handle,origin+forward*distances[shot],forward);
                }
                flashes.EmitMuzzle(muzzle,forward,weapon);
            }
            else if(definition.shape==BlockShape.Missile2)
            {
                laneLength=64;
                var offsets=weapon.missileLaunchOffsets;
                int count=offsets!=null&&offsets.Length>0?offsets.Length:1;
                for(int missile=0;missile<count;missile++)
                {
                    Vector3 origin=muzzle+source.transform.TransformVector(offsets!=null&&offsets.Length>0?offsets[missile]:Vector3.zero);
                    int handle=special.AcquireMissile(weapon,origin,forward);
                    Vector3 previous=origin;float travelled=0;
                    for(int frame=1;frame<=72;frame++)
                    {
                        float t=frame/72f,bend=Mathf.SmoothStep(0,1,t);
                        Vector3 position=origin+forward*(43*t)+Vector3.up*((missile%2==0?4:-3)*bend*bend)+Vector3.forward*((missile%2==0?-1.5f:2)*bend*bend);
                        Vector3 velocity=position-previous;travelled+=velocity.magnitude;
                        special.MoveMissile(handle,position,velocity.normalized,travelled);previous=position;
                    }
                    flashes.EmitMuzzle(origin,forward,weapon);
                }
            }
            else
            {
                laneLength=52;
                special.ShowBeam(weapon,muzzle,forward,weapon.range,source.transform,definition.muzzleLocalPosition);
                flashes.EmitMuzzle(muzzle,forward,weapon);
            }
            preview.camera.aspect=LaneWidth/(float)RenderHeight;
            preview.camera.orthographicSize=Mathf.Max(5.5f,laneLength/preview.camera.aspect*.5f);
            Vector3 center=new Vector3(laneLength*.5f-4,0,0),viewDirection=new Vector3(.025f,.22f,-1).normalized;
            preview.camera.transform.SetPositionAndRotation(center+viewDirection*160,Quaternion.LookRotation(-viewDirection,Vector3.up));
            preview.BeginPreview(new Rect(0,0,LaneWidth,RenderHeight),GUIStyle.none);
            preview.Render(true);Copy(preview.EndPreview(),sheet,344,y+48,LaneWidth,RenderHeight);
            Fill(sheet,20,y+40,Width-40,1,Rule);
            Text(sheet,Titles[index],24,y+22,2,new Color(.89f,.94f,1));
            Text(sheet,Notes[index],570,y+18,1,index==1||index==4?new Color(.36f,.73f,1):new Color(1,.70f,.30f));
        }
        finally
        {
            // Runtime pools own their generated materials; detach and release them synchronously in this preview.
            DisposePoolMaterial(root,"sharedMaterial",typeof(LaserVisualPool));
            DisposePoolMaterial(root,"glow",typeof(SpecialWeaponVisualPool));
            DisposePoolMaterial(root,"glow",typeof(CombatVfxPool));
            DisposePoolMaterial(root,"fragmentMaterial",typeof(CombatVfxPool));
            if(root)while(root.transform.childCount>0)UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            preview.Cleanup();
        }
    }
    static void DisposePoolMaterial(GameObject root,string name,Type type)
    {
        var component=root.GetComponent(type);if(!component)return;
        var field=type.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);if(field==null)return;
        var material=field.GetValue(component) as Material;if(material)UnityEngine.Object.DestroyImmediate(material);
        field.SetValue(component,null);
    }
    static void Copy(Texture source,Texture2D destination,int x,int y,int width,int height)
    {
        var render=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var previous=RenderTexture.active;var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        try{Graphics.Blit(source,render);RenderTexture.active=render;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();destination.SetPixels(x,y,width,height,pixels.GetPixels());}
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(render);UnityEngine.Object.DestroyImmediate(pixels);}
    }
    static void Fill(Texture2D texture,int x,int y,int width,int height,Color color)
    {for(int row=0;row<height;row++)for(int col=0;col<width;col++)texture.SetPixel(x+col,y+row,color);}
    static void Text(Texture2D texture,string value,int x,int y,int scale,Color color)
    {
        var type=typeof(Starframe.EditorTools.ComponentCatalogVisuals);
        var draw=type.GetMethod("Text",BindingFlags.NonPublic|BindingFlags.Static);
        draw.Invoke(null,new object[]{texture,value,x,y,scale,color});
    }
}
