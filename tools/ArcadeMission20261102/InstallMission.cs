using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Starframe.Combat;
using Starframe.Flight;
using Starframe.App;
public static class InstallArcadeMission
{
    const string Root="Assets/_Starframe/Data/Enemies";
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play before installing mission.");
        var fleet=AssetDatabase.FindAssets("t:EnemyArchetypeDefinition",new[]{Root})
            .Select(g=>AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        if(fleet.Length!=7)throw new Exception("Expected seven enemy archetypes, found "+fleet.Length);
        var small=fleet.Where(x=>x.size==EnemySize.Small).OrderBy(x=>x.id).ToArray();
        var large=fleet.Where(x=>x.size==EnemySize.Large).OrderBy(x=>x.id).ToArray();
        var boss=fleet.Single(x=>x.size==EnemySize.Capital);
        string path="Assets/_Starframe/Settings/Game/SaturnRingWaves.asset";
        var config=AssetDatabase.LoadAssetAtPath<ArcadeMissionDefinition>(path);
        if(!config){config=ScriptableObject.CreateInstance<ArcadeMissionDefinition>();AssetDatabase.CreateAsset(config,path);}
        ArcadeEnemyBatch B(EnemyArchetypeDefinition e,int count,float delay=0)=>new ArcadeEnemyBatch{enemy=e,count=count,delaySeconds=delay};
        config.waves=new[]{
            new ArcadeWave{nameZh="第一波 · 接触",nameEn="WAVE 1 · FIRST CONTACT",spawnInterval=5,batches=new[]{B(small[0],4),B(small[1],4,32)}},
            new ArcadeWave{nameZh="第二波 · 拦截",nameEn="WAVE 2 · INTERCEPTION",spawnInterval=5,batches=new[]{B(small[0],4),B(small[1],3,24),B(small[2],3,52),B(large[0],2,65)}},
            new ArcadeWave{nameZh="第三波 · 交叉火力",nameEn="WAVE 3 · CROSSFIRE",spawnInterval=5,batches=new[]{B(small[1],5),B(small[2],5,40),B(large[0],1,25),B(large[1],2,70)}},
            new ArcadeWave{nameZh="第四波 · 重装编队",nameEn="WAVE 4 · HEAVY FORMATION",spawnInterval=6,batches=new[]{B(small[0],5),B(small[1],5,45),B(large[0],2,20),B(large[2],2,65),B(boss,1,90)}},
            new ArcadeWave{nameZh="第五波 · 旗舰",nameEn="WAVE 5 · THE FLAGSHIP",spawnInterval=6,batches=new[]{B(small[0],4),B(small[1],4,30),B(small[2],4,65),B(large[0],2,15),B(large[1],2,45),B(large[2],2,85),B(boss,1,105)}}
        };
        config.deployment=AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/_Starframe/Settings/Game/ice.asset");config.defaultPlayerBlueprint=boss.blueprint;
        EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();
        var mission=AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/_Starframe/Settings/Game/ice.asset");
        Directory.CreateDirectory("Documentation/ArcadeMission-2026-11-02/before/native");
        string baseline="Documentation/ArcadeMission-2026-11-02/before/native/ice.json";
        if(!File.Exists(baseline))File.WriteAllText(baseline,EditorJsonUtility.ToJson(mission,true));
        var setup=EditorSceneManager.GetSceneManagerSetup();
        foreach(var scene in setup)if(scene.isLoaded&&SceneManager.GetSceneByPath(scene.path).isDirty)throw new Exception("Unsaved scene: "+scene.path);
        string scenePath="Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity";
        try{
            var scene=EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Testing/FlightRange.unity",OpenSceneMode.Single);
            var bootstrap=UnityEngine.Object.FindFirstObjectByType<FlightRangeBootstrap>();
            var serialized=new SerializedObject(bootstrap);var originalMission=serialized.FindProperty("arcadeMission").objectReferenceValue;serialized.FindProperty("arcadeMission").objectReferenceValue=AssetDatabase.LoadAssetAtPath<ArcadeMissionDefinition>(path);serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene,scenePath,true);
            AssetDatabase.ImportAsset(scenePath,ImportAssetOptions.ForceSynchronousImport);
            serialized.FindProperty("arcadeMission").objectReferenceValue=originalMission;serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Testing/FlightRange.unity",OpenSceneMode.Single);
        }finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
        mission=AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/_Starframe/Settings/Game/ice.asset");
        config=AssetDatabase.LoadAssetAtPath<ArcadeMissionDefinition>(path);
        mission.sceneName="SaturnRing";mission.baseBudget=10000;EditorUtility.SetDirty(mission);
        var scenes=EditorBuildSettings.scenes.ToList();
        if(!scenes.Any(x=>x.path==scenePath))scenes.Add(new EditorBuildSettingsScene(scenePath,true));
        else foreach(var s in scenes)if(s.path==scenePath)s.enabled=true;
        EditorBuildSettings.scenes=scenes.ToArray();AssetDatabase.SaveAssets();
        return $"Installed {config.waves.Length} waves / {config.TotalEnemies} finite enemies, 10000-cost mission, native SaturnRing scene; original range restored.";
    }
}
