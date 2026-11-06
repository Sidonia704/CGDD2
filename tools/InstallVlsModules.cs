using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Localization.Tables;
using Starframe.Construction;
using Starframe.Flight;
public static class InstallVlsModules
{
    const string Output="Assets/_Starframe/Settings/Modules/";
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Output+"ModuleDatabase.asset");
        var basis=AssetDatabase.LoadAssetAtPath<ModuleDefinition>(Output+"Missile1.asset");
        var rows=new List<object>();
        foreach(var shape in new[]{BlockShape.VLS1,BlockShape.VLS2})
        {
            bool large=shape==BlockShape.VLS1;string key=shape.ToString();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Starframe/assetsForCodex/116/"+key+".fbx");
            var size=new Vector3Int(large?2:1,2,2);var raw=Combine(source,Matrix4x4.identity,null);
            float scale=Mathf.Min((size.x-.02f)/raw.bounds.size.x,(size.y-.02f)/raw.bounds.size.y,(size.z-.02f)/raw.bounds.size.z);
            var centre=new Vector3((size.x-1)*.5f,(size.y-1)*.5f,(size.z-1)*.5f);
            var normalization=Matrix4x4.Translate(centre)*Matrix4x4.Scale(Vector3.one*scale)*Matrix4x4.Translate(-raw.bounds.center);
            UnityEngine.Object.DestroyImmediate(raw);
            var mesh=Store(Combine(source,normalization,null),Output+key+"Mesh.asset");
            string launcherName=large?"Cube.055":"Cube.075";
            var launcher=Store(Combine(source,normalization,f=>f.name==launcherName),Output+key+"LauncherMesh.asset");
            var filters=source.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.name!=launcherName).ToArray();
            var mouths=new List<Vector3>();Mesh projectile=null;
            foreach(var filter in filters)
            {
                var rocket=Combine(source,normalization,f=>f==filter);var b=rocket.bounds;
                mouths.Add(new Vector3(b.center.x,mesh.bounds.max.y+.04f,b.center.z));
                if(projectile==null)
                {
                    var verts=rocket.vertices;var rotation=Quaternion.FromToRotation(Vector3.up,Vector3.forward);
                    for(int i=0;i<verts.Length;i++)verts[i]=rotation*(verts[i]-b.center);
                    rocket.vertices=verts;rocket.RecalculateNormals();rocket.RecalculateBounds();projectile=rocket;
                }
                else UnityEngine.Object.DestroyImmediate(rocket);
            }
            projectile=Store(projectile,Output+key+"ProjectileMesh.asset");
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Output+key+"Weapon.asset");
            if(!weapon){weapon=ScriptableObject.CreateInstance<WeaponDefinition>();AssetDatabase.CreateAsset(weapon,Output+key+"Weapon.asset");}
            EditorUtility.CopySerialized(basis.weapon,weapon);weapon.name=key+"Weapon";
            weapon.family=WeaponFamily.VerticalLaunchMissile;weapon.delivery=WeaponDelivery.GuidedMissile;
            weapon.magazineSize=large?16:8;weapon.salvoLaunchesPerSecond=16;weapon.shotsPerSecond=16;
            weapon.missileLockSeconds=4;weapon.reloadSeconds=24;weapon.damage=basis.weapon.damage*.3f;
            weapon.explosionRadius=basis.weapon.explosionRadius*.55f;weapon.projectileDiameter=.38f;
            weapon.projectileSpeed=196;weapon.missileLaunchSpeed=24.5f;weapon.missileAcceleration=200;
            weapon.missileTurnDegrees=330;weapon.missileTurnAcceleration=1080;weapon.missileSeekerConeDegrees=110;
            weapon.missileSeekerLossSeconds=.8f;weapon.missileGuidanceSeconds=30;weapon.range=6600;
            weapon.sustainedPowerWatts=large?140:90;weapon.shotAudioGain=large?1:.6f;
            weapon.launcherMesh=launcher;weapon.missileMesh=projectile;weapon.visualWidth=.2f;weapon.visualLength=.8f;weapon.tailLength=5;
            var module=AssetDatabase.LoadAssetAtPath<ModuleDefinition>(Output+key+".asset");
            if(!module){module=ScriptableObject.CreateInstance<ModuleDefinition>();AssetDatabase.CreateAsset(module,Output+key+".asset");}
            EditorUtility.CopySerialized(basis,module);module.name=key;module.shape=shape;
            module.displayName=large?"垂发单元（16发）":"垂发单元（8发）";module.category=ModuleCategory.Weapon;
            module.mesh=mesh;module.outline=null;module.drawOutline=false;module.weapon=weapon;module.countermeasure=null;
            module.occupiedCells=Cells(size);module.cost=large?850:450;module.massKg=large?1800:900;module.maxHp=large?2200:1300;
            module.muzzleLocalPosition=mouths.Aggregate(Vector3.zero,(sum,p)=>sum+p)/mouths.Count;
            module.muzzleClearanceDiameter=.32f;
            weapon.missileLaunchOffsets=mouths.OrderBy(p=>p.z).ThenBy(p=>p.x).Select(p=>p-module.muzzleLocalPosition).ToArray();
            module.icon=RenderIcon(module,Output+key+"Icon.png");module.selectedIcon=module.icon;
            if(!database.modules.Contains(module))database.modules.Add(module);
            EditorUtility.SetDirty(module);EditorUtility.SetDirty(weapon);
            rows.Add(new{name=key,cells=module.occupiedCells.Length,ammo=weapon.magazineSize,tubes=mouths.Count,scale,
                meshSize=new[]{mesh.bounds.size.x,mesh.bounds.size.y,mesh.bounds.size.z},damage=weapon.damage,weapon.missileTurnDegrees});
        }
        var strings=LocalizationEditorSettings.GetStringTableCollection("GameUI");
        foreach(var code in new[]{"en","zh-Hans"})
        {
            var table=(StringTable)strings.GetTable(code);
            foreach(var key in new[]{"VLS1","VLS2"})
            {
                bool large=key=="VLS1";string value=code=="en"?(large?"VLS / 16 MISSILES":"VLS / 8 MISSILES"):(large?"垂发单元（16发）":"垂发单元（8发）");
                var entry=table.GetEntry("module."+key)??table.AddEntry("module."+key,value);entry.Value=value;
            }
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(strings.SharedData);EditorUtility.SetDirty(database);AssetDatabase.SaveAssets();
        var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        string result=(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{new{modules=rows.ToArray()}});
        Directory.CreateDirectory("Documentation/VlsExpansion");File.WriteAllText("Documentation/VlsExpansion/modules.json",result);
        return result;
    }
    static Vector3Int[] Cells(Vector3Int size){var list=new List<Vector3Int>();for(int x=0;x<size.x;x++)for(int y=0;y<size.y;y++)for(int z=0;z<size.z;z++)list.Add(new Vector3Int(x,y,z));return list.ToArray();}
    static Mesh Combine(GameObject source,Matrix4x4 normalize,Func<MeshFilter,bool> include)
    {
        var list=new List<CombineInstance>();
        foreach(var f in source.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh&&(include==null||include(f)))
            for(int i=0;i<f.sharedMesh.subMeshCount;i++)list.Add(new CombineInstance{mesh=f.sharedMesh,subMeshIndex=i,transform=normalize*source.transform.worldToLocalMatrix*f.transform.localToWorldMatrix});
        var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(list.ToArray(),true,true);mesh.RecalculateBounds();return mesh;
    }
    static Mesh Store(Mesh mesh,string path)
    {
        mesh.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static Sprite RenderIcon(ModuleDefinition module,string path)
    {
        var preview=new PreviewRenderUtility();var old=RenderTexture.active;Texture2D texture=null;RenderTexture rt=null;
        try
        {
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(0,0,0,0);preview.camera.orthographic=true;
            preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;preview.camera.aspect=1;
            var bounds=module.mesh.bounds;var direction=new Vector3(1.3f,1.2f,1.8f).normalized;
            preview.camera.transform.SetPositionAndRotation(bounds.center+direction*12,Quaternion.LookRotation(-direction,Vector3.up));
            preview.camera.orthographicSize=bounds.extents.magnitude*1.1f;
            preview.lights[0].intensity=1.6f;preview.lights[0].transform.rotation=Quaternion.Euler(45,-35,0);preview.lights[1].intensity=.7f;preview.ambientColor=new Color(.35f,.38f,.42f);
            preview.BeginPreview(new Rect(0,0,256,256),GUIStyle.none);preview.DrawMesh(module.mesh,Matrix4x4.identity,module.material,0);preview.Render(true);var image=preview.EndPreview();
            rt=RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);Graphics.Blit(image,rt);RenderTexture.active=rt;
            texture=new Texture2D(256,256,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,256,256),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
        }
        finally{RenderTexture.active=old;if(rt)RenderTexture.ReleaseTemporary(rt);if(texture)UnityEngine.Object.DestroyImmediate(texture);preview.Cleanup();}
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
