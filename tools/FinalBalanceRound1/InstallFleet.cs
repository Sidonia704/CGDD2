// Prepared separately from Assets; execute InstallFinalBalanceRound1Fleet.Main through the live Unity Editor.
// Native ScriptableObjects are changed through Unity APIs. Player files are read only.
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

public static class InstallFinalBalanceRound1Fleet
{
    const string Root = "Assets/_Starframe/Data/Enemies/";
    const string Evidence = "Documentation/FinalBalanceRound1-2026-11-03";
    const string WavePath = "Assets/_Starframe/Settings/Game/SaturnRingWaves.asset";
    const string MissionPath = "Assets/_Starframe/Settings/Game/ice.asset";
    static readonly string[] Ids = { "needle", "viper", "wasp", "bastion", "harrier", "halberd", "citadel" };
    static readonly int[] PartCounts = { 111, 172, 75, 301, 293, 344, 419 };

    public static string Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before installing the final fleet.");
        var database = Require<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var config = Require<ArcadeMissionDefinition>(WavePath);
        var mission = Require<MissionDefinition>(MissionPath);
        var enemies = Ids.Select(id => Require<EnemyArchetypeDefinition>(Root + id + ".asset")).ToArray();
        var behaviors = Ids.Select(id => Require<BotBehaviorProfile>(Root + id + "-behavior.asset")).ToArray();
        var sourcePaths = Enumerable.Range(1, 7).Select(i => Path.Combine(Application.persistentDataPath, "Blueprints", "t" + i + ".json")).ToArray();
        var sourceBytes = sourcePaths.Select(File.ReadAllBytes).ToArray();
        string workspace = Path.Combine(Application.persistentDataPath, "Workspace", "current-ship.json");
        var protectedFiles = sourcePaths.Concat(new[] { workspace, workspace + ".bak" })
            .ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
        var previous = BlockModules.ConfiguredDatabase;
        try
        {
            BlockModules.Configure(database);
            // Validate every approved source before overwriting the first encounter TextAsset.
            for (int i = 0; i < Ids.Length; i++)
            {
                var data = BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(Encoding.UTF8.GetString(sourceBytes[i])));
                if (data.name != "t" + (i + 1) || data.parts.Count != PartCounts[i])
                    throw new InvalidDataException("Final source changed: " + sourcePaths[i] + ". Review the approved design before importing.");
                var report = DeploymentValidator.ValidateForRange(data, database);
                if (!report.IsValid) throw new InvalidDataException(sourcePaths[i] + ": " + string.Join(", ", report.Errors.Select(e => e.key)));
                Require<TextAsset>(Root + Ids[i] + ".json");
                string snapshot = Evidence + "/approved-blueprints/t" + (i + 1) + ".json";
                if (File.Exists(snapshot) && !File.ReadAllBytes(snapshot).SequenceEqual(sourceBytes[i]))
                    throw new InvalidDataException("Approved snapshot differs: " + snapshot);
            }

            Directory.CreateDirectory(Evidence);
            foreach (var asset in enemies.Cast<UnityEngine.Object>().Concat(behaviors).Concat(new UnityEngine.Object[] { config, mission })) BackupAsset(asset);
            foreach (var id in Ids) BackupFile(Root + id + ".json");
            BackupFile(Root + "citadel-siege.json");
            var mapping = new List<string> { "source\tarchetype\tparts\tsha256\trole\tpreferredRange" };
            for (int i = 0; i < Ids.Length; i++)
            {
                string nativePath = Root + Ids[i] + ".json";
                string snapshot = Evidence + "/approved-blueprints/t" + (i + 1) + ".json";
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot));
                File.WriteAllBytes(snapshot, sourceBytes[i]);
                File.WriteAllBytes(nativePath, sourceBytes[i]);
                AssetDatabase.ImportAsset(nativePath, ImportAssetOptions.ForceSynchronousImport);
                enemies[i].blueprint = Require<TextAsset>(nativePath);
                enemies[i].behavior = behaviors[i];
                ConfigureBehavior(i, behaviors[i]);
                EditorUtility.SetDirty(behaviors[i]);
                EditorUtility.SetDirty(enemies[i]);
                mapping.Add("t" + (i + 1) + ".json\t" + Ids[i] + "\t" + PartCounts[i] + "\t" + Hash(sourceBytes[i]) + "\t" + behaviors[i].role + "\t" + behaviors[i].preferredRange);
            }
            // No alternate was approved this round. Alias the later-wave fit to the final t7;
            // retain the old siege JSON on disk for rollback and avoid importing it into the library.
            enemies[6].alternateBlueprint = enemies[6].blueprint;
            enemies[1].nameZh = "蝰蛇导弹机"; enemies[1].nameEn = "Viper Missile Fighter";
            enemies[2].nameZh = "黄蜂轨道炮战机"; enemies[2].nameEn = "Wasp Rail Fighter";
            enemies[4].nameZh = "鹞式激光护卫舰"; enemies[4].nameEn = "Harrier Laser Frigate";
            enemies[5].nameZh = "长戟轨道炮巡洋舰"; enemies[5].nameEn = "Halberd Rail Cruiser";
            foreach (var enemy in enemies) EditorUtility.SetDirty(enemy);

            var t1 = enemies[0]; var t2 = enemies[1]; var t3 = enemies[2]; var t4 = enemies[3];
            var t5 = enemies[4]; var t6 = enemies[5]; var t7 = enemies[6];
            ArcadeEnemyBatch B(EnemyArchetypeDefinition enemy, int count, float at = 0) =>
                new ArcadeEnemyBatch { enemy = enemy, count = count, delaySeconds = at };
            config.waves = new[] {
                new ArcadeWave { nameZh="第一波 · 接触", nameEn="WAVE 1 · FIRST CONTACT", spawnInterval=5, batches=new[] {
                    B(t1,3), B(t3,1,8), B(t1,2,20), B(t3,1,30) } },
                new ArcadeWave { nameZh="第二波 · 拦截", nameEn="WAVE 2 · INTERCEPTION", spawnInterval=5, batches=new[] {
                    B(t1,2), B(t2,1,10), B(t3,2,20), B(t4,1,30), B(t1,2,40), B(t2,1,48), B(t3,1,55) } },
                new ArcadeWave { nameZh="第三波 · 交叉火力", nameEn="WAVE 3 · CROSSFIRE", spawnInterval=5, batches=new[] {
                    B(t1,2), B(t3,1,8), B(t4,1,16), B(t2,1,24), B(t3,2,32), B(t5,1,42), B(t1,2,52), B(t2,1,62), B(t4,1,70) } },
                new ArcadeWave { nameZh="第四波 · 重装编队", nameEn="WAVE 4 · HEAVY FORMATION", spawnInterval=6, batches=new[] {
                    B(t1,3), B(t2,1,12), B(t3,2,24), B(t4,1,36), B(t5,1,48), B(t2,1,60), B(t5,1,72), B(t6,1,85) } },
                new ArcadeWave { nameZh="第五波 · 旗舰", nameEn="WAVE 5 · THE FLAGSHIP", spawnInterval=6, batches=new[] {
                    B(t1,3), B(t3,2,13), B(t4,1,26), B(t2,2,39), B(t5,1,52), B(t6,1,65), B(t3,1,78), B(t6,1,91), B(t7,1,105) } }
            };
            config.maximumAlive = 10; config.maximumLarge = 3; config.maximumAttackers = 3;
            config.emptyFieldReinforcementSeconds = 3;
            config.deployment = mission;
            mission.baseBudget = 30000;
            EditorUtility.SetDirty(config); EditorUtility.SetDirty(mission);
            AssetDatabase.SaveAssets();
            if (config.TotalEnemies != 53) throw new InvalidDataException("The approved five-wave roster must contain exactly 53 enemies.");
            foreach (var pair in protectedFiles)
            {
                bool unchanged = pair.Value == null ? !File.Exists(pair.Key) : File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value);
                if (!unchanged) throw new IOException("Protected player file changed during installation: " + pair.Key);
            }
            File.WriteAllLines(Evidence + "/fleet-map.tsv", mapping, new UTF8Encoding(false));
            var wavePlan = new List<string> { "wave\tarchetype\tcount\treleaseSeconds\tspawnInterval" };
            for (int w = 0; w < config.waves.Length; w++) foreach (var batch in config.waves[w].batches)
                wavePlan.Add((w + 1) + "\t" + batch.enemy.id + "\t" + batch.count + "\t" + batch.delaySeconds + "\t" + config.waves[w].spawnInterval);
            File.WriteAllLines(Evidence + "/wave-plan.tsv", wavePlan, new UTF8Encoding(false));
            return "Imported exact t1–t7 final designs (111/172/75/301/293/344/419 parts); five staged waves 7/10/12/11/13 = 53 enemies; concurrency 10/3/3; standard mission budget 30000; player files unchanged. Pacing requires actual play calibration.";
        }
        finally { BlockModules.Configure(previous); }
    }

    static void ConfigureBehavior(int index, BotBehaviorProfile profile)
    {
        switch (index)
        {
            case 0: profile.ConfigureRole(BotCombatRole.Interceptor); break;
            case 1: profile.ConfigureRole(BotCombatRole.Missile); break;
            case 2:
                profile.ConfigureRole(BotCombatRole.Cruiser); profile.preferredRange = 1100;
                profile.cruiseSpeedFraction = .72f; profile.attackSpeedFraction = .42f; profile.strafeFraction = .12f;
                profile.fireBurstSeconds = .7f; profile.firePauseSeconds = 1.7f; profile.aimErrorDegrees = .2f;
                break;
            case 3: profile.ConfigureRole(BotCombatRole.Gunship); break;
            case 4:
                profile.ConfigureRole(BotCombatRole.Cruiser); profile.preferredRange = 650;
                profile.strafeFraction = .1f; profile.fireBurstSeconds = 1.5f;
                break;
            case 5:
                // Rails provide the approach battery; the approved short-range beams remain defensive.
                profile.ConfigureRole(BotCombatRole.Cruiser); profile.preferredRange = 700;
                break;
            case 6: profile.ConfigureRole(BotCombatRole.Capital); profile.preferredRange = 800; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
    static T Require<T>(string path) where T : UnityEngine.Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) throw new FileNotFoundException("Required native " + typeof(T).Name + " is missing: " + path);
        return asset;
    }
    static void BackupAsset(UnityEngine.Object value)
    {
        string path = AssetDatabase.GetAssetPath(value);
        BackupFile(path);
        string json = Evidence + "/before/native-json/" + Path.GetFileName(path) + ".json";
        Directory.CreateDirectory(Path.GetDirectoryName(json));
        if (!File.Exists(json)) File.WriteAllText(json, EditorJsonUtility.ToJson(value, true), new UTF8Encoding(false));
    }
    static void BackupFile(string path)
    {
        foreach (string original in new[] { path, path + ".meta" })
        {
            if (!File.Exists(original)) continue;
            string backup = Evidence + "/before/" + original;
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.Copy(original, backup);
        }
    }
    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
