using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Maps;

public static class ReferenceMapImport
{
    public const string Root="Assets/_Starframe/Maps/ReferenceMiningStation";
    public sealed class SourceInfo { public string @object; public int sourceShell; }
    public sealed class AssetEntry { public string assetKey,cellRowsPath;public float cellSize=1;public int cells;public SourceInfo source; }
    public sealed class InstanceEntry { public string id,assetKey,label;public float[] position,eulerAngles;public float scale=1,healthMultiplier=1;public float[] rotationMatrix; }
    public sealed class Catalog { public AssetEntry[] assets;public InstanceEntry[] instances;public float[] globalTranslation; }
    public sealed class Rows { public string assetKey;public int[][] cells; }
    static readonly Type JsonType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Newtonsoft.Json.JsonConvert",false)).First(t=>t!=null);
    static T Read<T>(string path)=>(T)JsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{File.ReadAllText(path),typeof(T)});
    static Vector3 V(float[] v)=>v==null?Vector3.zero:new Vector3(v[0],v[1],v[2]);
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }
    static string Safe(string name)
    {
        if(string.IsNullOrEmpty(name)||name.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-'))throw new ArgumentException("Unsafe generated asset name: "+name);
        return name;
    }
    static Dictionary<string,string> ExistingAssets() => AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{Root+"/Assets"}).Select(AssetDatabase.GUIDToAssetPath).ToDictionary(p=>Path.GetFileNameWithoutExtension(p),p=>p);
    public static object ImportBatch(string catalogPath,string group,int start,int count)
    {
        Safe(group);var catalog=Read<Catalog>(catalogPath);
        var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        Folder(Root+"/Assets/"+group);Folder(Root+"/Meshes/"+group);
        var existingAssets=ExistingAssets();
        var result=new List<object>();int end=Mathf.Min(catalog.assets.Length,start+count);
        for(int i=start;i<end;i++)
        {
            var entry=catalog.assets[i];Safe(entry.assetKey);var rows=Read<Rows>(entry.cellRowsPath);
            if(rows.cells.Length!=entry.cells||rows.cells.Length>VoxelAssetDefinition.MaximumCells)throw new InvalidOperationException("Wrong/oversize voxel count: "+entry.assetKey);
            string path=Root+"/Assets/"+group+"/"+entry.assetKey+".asset";
            if(existingAssets.TryGetValue(entry.assetKey,out var previousPath))path=previousPath;
            var asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);bool created=!asset;
            if(!asset)asset=ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            asset.hullModules=db;asset.category=entry.source?.@object=="Cube.001"?"Reference rock":"Reference structure";
            asset.cellSize=entry.cellSize;asset.baseColor=Color.white;asset.surfaceColors=new List<Color>{Color.white};
            asset.cells=new List<VoxelCell>(rows.cells.Length);
            foreach(var r in rows.cells)
            {
                if(r.Length!=11)throw new InvalidOperationException("Bad cell row");
                asset.cells.Add(new VoxelCell{position=new Vector3Int(r[0],r[1],r[2]),shape=(BlockShape)r[3],quarterTurns=new Vector3Int(r[4],r[5],r[6]),modelOffset=new Vector3Int(r[7],r[8],r[9]),surface=r[10]});
            }
            asset.NotifyChanged();if(!asset.Validate(out var error))throw new InvalidOperationException(entry.assetKey+": "+error);
            var states=asset.cells.ToDictionary(c=>c.position,c=>new VoxelCellState{position=c.position,shape=c.shape,quarterTurns=c.quarterTurns,modelOffset=c.modelOffset,surface=c.surface});
            int islands=VoxelGeometry.Islands(states).Count;if(islands!=1)throw new InvalidOperationException(entry.assetKey+" is disconnected: "+islands);
            if(created)AssetDatabase.CreateAsset(asset,path);
            // Signed, shared exterior-only distance mesh. Exact authored hulls and full physical cells remain the source of truth.
            var coarseCells=asset.cells.ToDictionary(c=>c.position,c=>new VoxelCellState{position=c.position,shape=BlockShape.Cube,surface=c.surface*VoxelHullAppearance.ShapeCount+VoxelHullAppearance.ShapeIndex(c.shape)});
            var coarse=VoxelGeometry.Build(coarseCells,asset.SurfaceCount*VoxelHullAppearance.ShapeCount,asset.cellSize,null);
            coarse.name=entry.assetKey+" exterior LOD";
            string meshPath=Root+"/Meshes/"+group+"/"+entry.assetKey+".asset";
            if(asset.initialLodMesh)meshPath=AssetDatabase.GetAssetPath(asset.initialLodMesh);
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(saved){EditorUtility.CopySerialized(coarse,saved);UnityEngine.Object.DestroyImmediate(coarse);EditorUtility.SetDirty(saved);}else{saved=coarse;AssetDatabase.CreateAsset(saved,meshPath);}
            asset.initialLodMesh=saved;asset.initialLodSignature=SpaceMapRuntime.ComputeGeometrySignature(asset);EditorUtility.SetDirty(asset);
            result.Add(new {path,cells=asset.cells.Count,lodVertices=saved.vertexCount,islands});
        }
        AssetDatabase.SaveAssets();
        return new {group,start,end,total=catalog.assets.Length,assets=result.ToArray()};
    }
    public static object CreateLayout(string catalogPath,string group,string name)
    {
        Safe(group);Safe(name);var catalog=Read<Catalog>(catalogPath);Folder(Root+"/Layouts");
        string path=Root+"/Layouts/"+name+".asset";var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(path);bool created=!map;
        if(!map)map=ScriptableObject.CreateInstance<SpaceMapDefinition>();map.size=Vector3.one*3600;
        var existingAssets=ExistingAssets();
        map.playerSpawn=new Vector3(0,100,-1250);map.enemySpawns=new List<Vector3>{new Vector3(0,100,900)};map.instances.Clear();
        foreach(var item in catalog.instances)
        {
            string assetPath=Root+"/Assets/"+group+"/"+Safe(item.assetKey)+".asset";
            if(existingAssets.TryGetValue(item.assetKey,out var previousPath))assetPath=previousPath;
            var asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(assetPath);if(!asset)throw new InvalidOperationException("Not imported: "+assetPath);
            Quaternion rotation=Quaternion.Euler(V(item.eulerAngles));
            if(item.rotationMatrix!=null&&item.rotationMatrix.Length==9)
                rotation=Quaternion.LookRotation(new Vector3(item.rotationMatrix[2],item.rotationMatrix[5],item.rotationMatrix[8]),new Vector3(item.rotationMatrix[1],item.rotationMatrix[4],item.rotationMatrix[7]));
            int hash=0;foreach(char c in item.id??item.assetKey)hash=unchecked(hash*31+c);
            float side=(hash&1)==0?1:-1;
            map.instances.Add(new VoxelMapInstance{id=string.IsNullOrEmpty(item.id)?Guid.NewGuid().ToString("N"):item.id,label=item.label??item.assetKey,asset=asset,position=V(item.position)+V(catalog.globalTranslation),eulerAngles=rotation.eulerAngles,scale=item.scale,
                healthMultiplier=item.healthMultiplier,driftVelocity=new Vector3(side*.08f,.015f,side*.025f),angularVelocity=Vector3.up*side*.015f});
        }
        if(!map.Validate(out var error))throw new InvalidOperationException(error);
        if(created)AssetDatabase.CreateAsset(map,path);else EditorUtility.SetDirty(map);AssetDatabase.SaveAssets();
        return new {path,instances=map.instances.Count,cells=map.instances.Sum(i=>(long)i.asset.cells.Count)};
    }
}
