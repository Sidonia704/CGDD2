using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Starframe.Maps;
using Starframe.Flight;

public static class InstallVoxelMaps
{
    public static string RestoreUniformStudio()
    {
        EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity");
        var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(Starframe.EditorTools.UniformMapBuilder.Root+"/Range115Voxels/Layouts/Range115Voxels.asset");
        EditorWindow.GetWindow<Starframe.EditorTools.SpaceMapStudioWindow>().OpenMap(map);
        Selection.activeObject=map;
        return "Hangar restored; map studio selected the uniform Range115 layout.";
    }
    public static string InstallUniform()
    {
        var results=new System.Collections.Generic.List<string>();
        foreach(var row in new[]{new[]{"Assets/_Starframe/Scenes/Testing/FlightRange.unity","Range115Voxels"},new[]{"Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity","SaturnRingVoxels"}})
        {
            results.Add(Install(row[0],Starframe.EditorTools.UniformMapBuilder.Root+"/"+row[1]+"/Layouts/"+row[1]+".asset"));
            Starframe.EditorTools.UniformBackdropBuilder.InstallToScene(row[0]);
        }
        return string.Join("\n",results);
    }
    public static string Install(string scenePath,string mapPath,string backdropRoot="")
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before installing.");
        var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(mapPath);
        if(!map||!map.Validate(out var error))throw new InvalidOperationException("Missing/invalid voxel map: "+mapPath);
        if(map.instances.Any(i=>!i.asset.HasValidInitialLod||i.asset.initialLodMesh.name!=i.asset.name+" Voxel Assembly"))throw new InvalidOperationException("Map contains an unverified voxel cache.");
        var previous=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(scenePath);bool opened=!scene.isLoaded;
        if(!opened&&scene.isDirty)throw new InvalidOperationException("Scene has unsaved changes: "+scenePath);
        Directory.CreateDirectory("tmp/VoxelMaps/before");string backup="tmp/VoxelMaps/before/"+Path.GetFileName(scenePath);
        if(!File.Exists(backup))File.Copy(scenePath,backup);
        if(opened)scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
        try
        {
            var roots=scene.GetRootGameObjects();var runtime=roots.SelectMany(r=>r.GetComponentsInChildren<SpaceMapRuntime>(true)).Single();
            if(map.enforceUniformGrid && (runtime.transform.lossyScale-Vector3.one).sqrMagnitude>1e-10f)throw new InvalidOperationException("Map root/ancestor scale must be one before binding uniform data.");
            if(map.enforceUniformGrid)for(var ancestor=runtime.transform;ancestor;ancestor=ancestor.parent)if((ancestor.localScale-Vector3.one).sqrMagnitude>1e-10f)throw new InvalidOperationException("Non-unit map ancestor: "+ancestor.name);
            runtime.definition=map;runtime.lazyFineVisuals=true;runtime.keepCoarseIntactVisuals=true;
            if(!string.IsNullOrEmpty(backdropRoot))foreach(var backdrop in roots.SelectMany(r=>r.GetComponentsInChildren<RangeAsteroidBackdrop>(true)))
            {
                var so=new SerializedObject(backdrop);var fields=new[]{"detailedRockA","detailedRockB","distantRockA","distantRockB"};
                for(int i=0;i<fields.Length;i++){var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(backdropRoot+"/VoxelBackdrop"+i+".asset");if(!mesh)throw new InvalidOperationException("Missing voxel backdrop "+i);so.FindProperty(fields[i]).objectReferenceValue=mesh;}
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return scenePath+" -> "+mapPath+" ("+map.instances.Count+" voxel assemblies)";
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }
}
