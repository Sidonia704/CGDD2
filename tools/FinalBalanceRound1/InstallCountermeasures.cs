using System;
using System.IO;
using System.Text;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;

public static class InstallCountermeasures
{
    // Run explicitly through the live Editor after the coordinated source recompile.
    // Speeds, launch acceleration, guidance duration, damage and burst probability remain
    // their existing asset values; this pass only tunes finite turn and visibility limits.
    public static string Run()
    {
        var summary=new StringBuilder();
        Apply("Missile1Weapon",110,360,60,summary);
        Apply("Missile2Weapon",95,300,55,summary);
        Apply("Missile3Weapon",80,240,50,summary);
        return summary.ToString();
    }
    static void Apply(string name,float turn,float acceleration,float cone,StringBuilder summary)
    {
        string asset="Assets/_Starframe/Settings/Modules/"+name+".asset";
        var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>(asset);
        if(!weapon)throw new InvalidOperationException("Missing missile asset: "+asset);
        string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        string original=Path.GetFullPath(Path.Combine(project,asset));
        string backup=Path.Combine(project,"Documentation/FinalBalanceRound1-2026-11-03/before",asset);
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        if(!File.Exists(backup))File.Copy(original,backup);
        Undo.RecordObject(weapon,"First-round finite missile seeker tuning");
        weapon.missileTurnDegrees=turn;
        weapon.missileTurnAcceleration=acceleration;
        weapon.missileSeekerConeDegrees=cone;
        weapon.missileSeekerLossSeconds=.3f;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssetIfDirty(weapon);
        summary.AppendLine(name+": speed="+weapon.projectileSpeed+", turn="+weapon.missileTurnDegrees+
            ", angularAcceleration="+weapon.missileTurnAcceleration+", cone="+weapon.missileSeekerConeDegrees+
            ", loss="+weapon.missileSeekerLossSeconds+", guidance="+weapon.missileGuidanceSeconds);
    }
}
