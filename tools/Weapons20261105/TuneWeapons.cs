using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Starframe.Flight;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine;

/// <summary>Idempotent native asset authoring for the approved 2026-11-05 weapon tuning.</summary>
public static class TuneWeapons
{
    const string Settings = "Assets/_Starframe/Settings/Modules/";
    const string Evidence = "Documentation/Weapons-2026-11-05";
    static readonly string[] Names = { "Gun1Weapon", "Gun2Weapon", "Gun3Weapon", "LaserRapidCannon", "Laser2Weapon", "Laser3Weapon",
        "Railgun1Weapon", "Railgun2Weapon", "Railgun3Weapon", "Missile1Weapon", "Missile2Weapon", "Missile3Weapon", "Sword1Weapon", "Sword2Weapon" };
    // Exact installed baseline; assigning constants prevents repeated execution from compounding a multiplier.
    static readonly float[] Damage = { 360, 510, 720, 840, 1380, 2280, 7200, 12000, 20800, 11200, 19200, 32000, 1500, 2700 };
    static readonly float[] Power = { 18, 32, 55, 55, 100, 165, 100, 180, 280, 70, 120, 180, 120, 220 };
    static readonly WeaponFamily[] Families = { WeaponFamily.Gatling, WeaponFamily.Gatling, WeaponFamily.Gatling,
        WeaponFamily.RapidLaser, WeaponFamily.RapidLaser, WeaponFamily.RapidLaser,
        WeaponFamily.Railgun, WeaponFamily.Railgun, WeaponFamily.Railgun,
        WeaponFamily.GuidedMissile, WeaponFamily.GuidedMissile, WeaponFamily.GuidedMissile,
        WeaponFamily.BeamLaser, WeaponFamily.BeamLaser };

    public static object Main() => Apply();
    public static object Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before tuning native weapon assets.");
        var weapons = new WeaponDefinition[Names.Length];
        for (int i = 0; i < Names.Length; i++)
        {
            weapons[i] = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Settings + Names[i] + ".asset");
            if (!weapons[i]) throw new InvalidOperationException("Missing native weapon: " + Names[i]);
        }
        Directory.CreateDirectory(Evidence + "/before"); Directory.CreateDirectory(Evidence + "/after");
        var result = new List<object>();
        for (int i = 0; i < weapons.Length; i++)
        {
            var w = weapons[i]; string backup = Evidence + "/before/" + Names[i] + ".json";
            if (!File.Exists(backup)) File.WriteAllText(backup, EditorJsonUtility.ToJson(w, true));
            w.family = Families[i];
            bool missile = w.family == WeaponFamily.GuidedMissile;
            bool laser = w.family == WeaponFamily.RapidLaser || w.family == WeaponFamily.BeamLaser;
            w.damage = Damage[i] * (missile ? 1f : 1.5f);
            w.sustainedPowerWatts = Power[i] * (laser ? 2.5f : 1.4f);
            w.unboundedBeam = w.family == WeaponFamily.BeamLaser;
            if (w.family == WeaponFamily.Gatling)
            {
                int tier = i;
                w.shotsPerSecond = new[] { 16f, 20f, 24f }[tier];
                w.gatlingSpinUpSeconds = 1.5f; w.gatlingSpinDownSeconds = .5f; w.gatlingMinimumRateFraction = .2f;
                w.gatlingHeatPerSecond = .25f; w.gatlingCoolingPerSecond = 1f / 12f;
                w.tracerStyle = TracerStyle.Continuous;
                w.visualLength = new[] { 6f, 8f, 10f }[tier]; w.tailLength = new[] { 120f, 140f, 160f }[tier];
                w.minimumVisualPixels = 2.2f; w.effectColor = new Color(1f, .62f, .055f, 1);
                w.muzzleOffsets = Array.Empty<Vector3>();
            }
            else if (w.family == WeaponFamily.RapidLaser)
            {
                int tier = i - 3;
                w.shotsPerSecond = new[] { 8f, 9f, 9f }[tier];
                w.projectileSpeed = new[] { 5400f, 6000f, 6600f }[tier];
                w.visualLength = new[] { 140f, 160f, 180f }[tier]; w.tailLength = new[] { 80f, 100f, 120f }[tier];
                w.tracerStyle = TracerStyle.Continuous; w.minimumVisualPixels = 2;
                // Keep each original blue; only the continuous tracer shape changes.
                w.effectColor = tier == 0 ? new Color(.08f, .65f, 1f, 1f) : new Color(.10f, .57f, 1f, 1f);
            }
            else if (w.family == WeaponFamily.Railgun)
                w.tailLength = new[] { 400f, 600f, 840f }[i - 6];
            else if (missile)
            {
                w.reloadSeconds = new[] { 10f, 12f, 15f }[i - 9];
                w.shotsPerSecond = 1f / w.reloadSeconds;
            }
            else if (w.family == WeaponFamily.BeamLaser)
            {
                w.range = 100000; w.shotsPerSecond = 12;
                w.visualWidth = i == 12 ? 1f : 1.5f;
            }
            EditorUtility.SetDirty(w); AssetDatabase.SaveAssetIfDirty(w);
            File.WriteAllText(Evidence + "/after/" + Names[i] + ".json", EditorJsonUtility.ToJson(w, true));
            result.Add(new { name = Names[i], family = w.family.ToString(), w.damage, w.sustainedPowerWatts, w.shotsPerSecond,
                w.reloadSeconds, w.projectileSpeed, w.range, effectiveRange = w.EffectiveRange,
                w.visualLength, w.tailLength, w.visualWidth, w.unboundedBeam });
        }
        RenameBeamCannons();
        return new { updated = weapons.Length, shipWideMissileLaunchesPerSecond = 8, weapons = result };
    }

    static void RenameBeamCannons()
    {
        const string csvPath = "Assets/_Starframe/Localization/GameUI.csv";
        var keys = new[] { "module.Sword1", "module.Sword2", "module.short.Sword1", "module.short.Sword2" };
        var chinese = new[] { "直射激光炮 I", "直射激光炮 II", "光束炮1", "光束炮2" };
        var english = new[] { "Laser Beam Cannon I", "Laser Beam Cannon II", "Beam I", "Beam II" };
        var collection = LocalizationEditorSettings.GetStringTableCollection("GameUI");
        if (collection == null) throw new InvalidOperationException("GameUI localization collection is missing.");
        var zh = collection.GetTable("zh-Hans") as StringTable;
        var en = collection.GetTable("en") as StringTable;
        if (!zh || !en) throw new InvalidOperationException("Both native GameUI language tables are required.");
        byte[] originalBytes = File.ReadAllBytes(csvPath);
        string originalCsv = File.ReadAllText(csvPath), csv = originalCsv;
        for (int i = 0; i < keys.Length; i++)
        {
            var row = new Regex("^(\"" + Regex.Escape(keys[i]) + "\",)\"(?:[^\"]|\"\")*\",\"(?:[^\"]|\"\")*\"(,.*)$", RegexOptions.Multiline);
            if (row.Matches(csv).Count != 1) throw new InvalidOperationException("Expected exactly one CSV row for " + keys[i]);
            int index = i;
            csv = row.Replace(csv, match => match.Groups[1].Value + "\"" + chinese[index] + "\",\"" + english[index] + "\"" + match.Groups[2].Value);
        }
        string csvBackup = Evidence + "/before/GameUI.csv";
        if (!File.Exists(csvBackup)) File.WriteAllBytes(csvBackup, originalBytes);
        for (int tier = 0; tier < 2; tier++)
        {
            string moduleName = "Sword" + (tier + 1);
            var module = AssetDatabase.LoadAssetAtPath<ModuleDefinition>(Settings + moduleName + ".asset");
            if (!module) throw new InvalidOperationException("Missing native beam cannon module: " + moduleName);
            string backup = Evidence + "/before/" + moduleName + ".module.json";
            if (!File.Exists(backup)) File.WriteAllText(backup, EditorJsonUtility.ToJson(module, true));
            module.displayName = chinese[tier];
            EditorUtility.SetDirty(module); AssetDatabase.SaveAssetIfDirty(module);
        }
        foreach (var table in new[] { zh, en })
        {
            string backup = Evidence + "/before/GameUI_" + table.LocaleIdentifier.Code + ".json";
            if (!File.Exists(backup)) File.WriteAllText(backup, EditorJsonUtility.ToJson(table, true));
            for (int i = 0; i < keys.Length; i++)
            {
                string value = table == zh ? chinese[i] : english[i];
                var entry = table.GetEntry(keys[i]) ?? table.AddEntry(keys[i], value);
                entry.Value = value; entry.IsSmart = false;
            }
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
        }
        if (csv != originalCsv)
        {
            bool hadBom = originalBytes.Length >= 3 && originalBytes[0] == 0xEF && originalBytes[1] == 0xBB && originalBytes[2] == 0xBF;
            File.WriteAllText(csvPath, csv, new UTF8Encoding(hadBom));
            AssetDatabase.ImportAsset(csvPath);
        }
        EditorUtility.SetDirty(collection); EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssetIfDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(TuneWeapons), collection);
    }
}
