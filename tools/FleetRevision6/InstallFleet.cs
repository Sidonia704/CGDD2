// Execute through the live Editor after gameplay compilation. Player files are read only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;

public static class InstallFleetRevision6
{
    const string Root="Assets/_Starframe/Data/Enemies/", Evidence="Documentation/FleetRevision6";
    const string WavePath="Assets/_Starframe/Settings/Game/SaturnRingWaves.asset";
    const string CatalogPath="Assets/_Starframe/Resources/Enemies/FleetTemplates.asset";
    static readonly string[] Sources={"t0-1","t0-2","t0-3","t0-4","t1","t2","t3","t4","t5","t6","t7","t7-2","t8"};
    static readonly string[] Ids={"scout-pulse","scout-rail","scout-beam","scout-mixed","needle","viper","wasp","bastion","harrier","halberd","citadel","citadel-missile","sovereign"};
    static readonly int[] Counts={52,44,44,45,111,172,75,301,293,344,419,415,852};

    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before fleet installation.");
        var database=Require<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var config=Require<ArcadeMissionDefinition>(WavePath);
        var catalog=Require<EnemyFleetCatalog>(CatalogPath);
        var previous=BlockModules.ConfiguredDatabase;
        var sourceBytes=Sources.Select(name=>File.ReadAllBytes(Evidence+"/approved-blueprints/"+name+".json")).ToArray();
        var protectedFiles=Directory.GetFiles(Path.Combine(Application.persistentDataPath,"Blueprints"),"*.json").Concat(new[]{ShipWorkspace.WorkspacePath,ShipWorkspace.WorkspacePath+".bak"}).Where(File.Exists).ToDictionary(path=>path,File.ReadAllBytes);
        try {
            BlockModules.Configure(database);
            // Validate the complete fleet and confirm all snapshots still match the student's originals before writes.
            for(int i=0;i<Sources.Length;i++){
                var data=BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(Encoding.UTF8.GetString(sourceBytes[i])));
                if(data.name!=Sources[i]||data.parts.Count!=Counts[i])throw new InvalidDataException("Unexpected source: "+Sources[i]);
                var report=DeploymentValidator.ValidateForRange(data,database);
                if(!report.IsValid)throw new InvalidDataException(Sources[i]+": "+string.Join(",",report.Errors.Select(error=>error.key)));
                if(!sourceBytes[i].SequenceEqual(File.ReadAllBytes(Path.Combine(Application.persistentDataPath,"Blueprints",Sources[i]+".json"))))throw new InvalidDataException("Student source changed after snapshot: "+Sources[i]);
            }
            Backup(WavePath);Backup(CatalogPath);
            var fleet=new EnemyArchetypeDefinition[Ids.Length];
            var mapping=new List<string>{"source\tarchetype\tparts\tsha256\tsize"};
            for(int i=0;i<Ids.Length;i++){
                string jsonPath=Root+Ids[i]+".json",assetPath=Root+Ids[i]+".asset";
                Backup(jsonPath);Backup(assetPath);
                File.WriteAllBytes(jsonPath,sourceBytes[i]);AssetDatabase.ImportAsset(jsonPath,ImportAssetOptions.ForceSynchronousImport);
                var enemy=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(assetPath);
                if(!enemy){enemy=ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();AssetDatabase.CreateAsset(enemy,assetPath);}
                enemy.id=Ids[i];enemy.blueprint=Require<TextAsset>(jsonPath);enemy.alternateBlueprint=null;
                enemy.size=i<7?EnemySize.Small:i==12?EnemySize.Capital:EnemySize.Large;
                if(i<4){
                    enemy.nameZh="轻型僚机 "+Sources[i];enemy.nameEn="Light Escort "+Sources[i];enemy.score=60;
                    enemy.behavior=CreateBehavior(Ids[i],i==1||i==3?BotCombatRole.Cruiser:BotCombatRole.Assault);
                    enemy.behavior.preferredRange=i==1?1100:i==3?600:i==2?350:420;
                    EditorUtility.SetDirty(enemy.behavior);
                }
                else if(i==10){enemy.nameZh="壁垒重巡洋舰";enemy.nameEn="Citadel Heavy Cruiser";enemy.score=2500;}
                else if(i==11){enemy.nameZh="壁垒导弹巡洋舰";enemy.nameEn="Citadel Missile Cruiser";enemy.score=2500;enemy.behavior=CreateBehavior(Ids[i],BotCombatRole.Frigate);}
                else if(i==12){enemy.nameZh="君主旗舰";enemy.nameEn="Sovereign Flagship";enemy.score=8000;enemy.behavior=CreateBehavior(Ids[i],BotCombatRole.Capital);}
                if(!enemy.behavior)throw new InvalidDataException("Missing behavior: "+enemy.id);
                fleet[i]=enemy;EditorUtility.SetDirty(enemy);
                mapping.Add(Sources[i]+"\t"+Ids[i]+"\t"+Counts[i]+"\t"+Hash(sourceBytes[i])+"\t"+enemy.size);
            }
            // Preserve both t7 fittings as explicit heavy-escort entries; only t8 consumes the capital slot.
            var t01=fleet[0];var t02=fleet[1];var t03=fleet[2];var t04=fleet[3];
            var t1=fleet[4];var t2=fleet[5];var t3=fleet[6];var t4=fleet[7];var t5=fleet[8];var t6=fleet[9];var t7=fleet[10];var t72=fleet[11];var t8=fleet[12];
            ArcadeEnemyBatch B(EnemyArchetypeDefinition enemy,int count,float at=0)=>new ArcadeEnemyBatch{enemy=enemy,count=count,delaySeconds=at};
            config.waves=new[]{
                new ArcadeWave{nameZh="第一波 · 前哨",nameEn="WAVE 1 · SCREEN",spawnInterval=5,batches=new[]{B(t01,3),B(t1,2,10),B(t02,2,22),B(t3,1,34),B(t03,2,44)}},
                new ArcadeWave{nameZh="第二波 · 拦截",nameEn="WAVE 2 · INTERCEPTION",spawnInterval=5,batches=new[]{B(t01,2),B(t1,2,10),B(t04,2,20),B(t2,1,32),B(t02,2,42),B(t4,1,54),B(t3,2,64)}},
                new ArcadeWave{nameZh="第三波 · 交叉火力",nameEn="WAVE 3 · CROSSFIRE",spawnInterval=5,batches=new[]{B(t03,3),B(t4,1,12),B(t1,2,24),B(t04,3,36),B(t2,2,48),B(t5,1,60),B(t3,1,72)}},
                new ArcadeWave{nameZh="第四波 · 重装编队",nameEn="WAVE 4 · HEAVY FORMATION",spawnInterval=6,batches=new[]{B(t01,2),B(t4,1,12),B(t02,2,24),B(t1,2,36),B(t5,1,48),B(t03,2,60),B(t6,1,72),B(t2,1,84),B(t6,1,96)}},
                new ArcadeWave{nameZh="第五波 · 旗舰卫队",nameEn="WAVE 5 · FLAGSHIP GUARD",spawnInterval=6,batches=new[]{B(t01,2),B(t7,1,12),B(t02,2,24),B(t1,2,36),B(t4,1,48),B(t04,2,60),B(t2,1,72),B(t6,1,84),B(t3,1,96),B(t72,1,108)}},
                new ArcadeWave{nameZh="第六波 · 君主旗舰",nameEn="WAVE 6 · SOVEREIGN",spawnInterval=6,batches=new[]{B(t8,1),B(t03,2,6),B(t1,2,18),B(t5,1,30),B(t04,3,42),B(t2,1,54),B(t6,1,66),B(t03,2,78),B(t3,1,90),B(t72,1,102)}}
            };
            config.maximumAlive=10;config.maximumLarge=3;config.maximumAttackers=3;
            // Existing continuous-wave timing, arena and deployment budget remain authoritative.
            catalog.enemies=fleet;EditorUtility.SetDirty(config);EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            if(config.TotalEnemies!=77)throw new InvalidDataException("Six-wave roster must total 77.");
            foreach(var pair in protectedFiles)if(!File.Exists(pair.Key)||!pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)))throw new IOException("Protected player file changed: "+pair.Key);
            File.WriteAllLines(Evidence+"/fleet-map.tsv",mapping,new UTF8Encoding(false));
            var plan=new List<string>{"wave\tarchetype\tcount\treleaseSeconds\tspawnInterval"};
            for(int w=0;w<config.waves.Length;w++)foreach(var batch in config.waves[w].batches)plan.Add((w+1)+"\t"+batch.enemy.id+"\t"+batch.count+"\t"+batch.delaySeconds+"\t"+config.waves[w].spawnInterval);
            File.WriteAllLines(Evidence+"/wave-plan.tsv",plan,new UTF8Encoding(false));
            return "Imported 13 exact source designs; six mixed waves 10/12/13/13/14/15=77 (38 light fillers,24 other small,14 large,1 final flagship). Caps10/3/3. Player originals/workspace unchanged. Live pacing requires play verification.";
        } finally {BlockModules.Configure(previous);}
    }
    static BotBehaviorProfile CreateBehavior(string id,BotCombatRole role)
    {
        string path=Root+id+"-behavior.asset";Backup(path);var profile=AssetDatabase.LoadAssetAtPath<BotBehaviorProfile>(path);
        if(!profile){profile=ScriptableObject.CreateInstance<BotBehaviorProfile>();AssetDatabase.CreateAsset(profile,path);}
        profile.ConfigureRole(role);EditorUtility.SetDirty(profile);return profile;
    }
    static T Require<T>(string path)where T:UnityEngine.Object{var value=AssetDatabase.LoadAssetAtPath<T>(path);if(!value)throw new FileNotFoundException(path);return value;}
    static void Backup(string path){foreach(var original in new[]{path,path+".meta"}){if(!File.Exists(original))continue;string target=Evidence+"/before/"+original;Directory.CreateDirectory(Path.GetDirectoryName(target));if(!File.Exists(target))File.Copy(original,target);}}
    static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
}
