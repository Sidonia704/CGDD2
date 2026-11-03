using System;
using UnityEditor;
using UnityEngine;
using Starframe.Combat;

public static class InstallEnemyFleetLibrary
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Install fleet catalog outside Play mode.");
        const string folder="Assets/_Starframe/Resources/Enemies";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/_Starframe/Resources","Enemies");
        string path=folder+"/FleetTemplates.asset";
        var catalog=AssetDatabase.LoadAssetAtPath<EnemyFleetCatalog>(path);
        if(!catalog){catalog=ScriptableObject.CreateInstance<EnemyFleetCatalog>();AssetDatabase.CreateAsset(catalog,path);}
        var ids=new[]{"needle","viper","wasp","bastion","harrier","halberd","citadel"};
        var templates=new EnemyArchetypeDefinition[ids.Length];
        for(int i=0;i<ids.Length;i++){
            templates[i]=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>("Assets/_Starframe/Data/Enemies/"+ids[i]+".asset");
            if(!templates[i])throw new InvalidOperationException("Missing enemy definition: "+ids[i]);
        }
        catalog.enemies=templates;EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        return "Installed seven enemy templates and their alternate blueprints for manual player-library import.";
    }
}
