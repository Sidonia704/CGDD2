using System;
using System.IO;
using System.Linq;
using System.Text;
using Starframe.App;
using Starframe.Construction;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;

public static class AuditFleetRevision6Library
{
    public static string Main()
    {
        var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var previous=BlockModules.ConfiguredDatabase;
        var output=new StringBuilder("file\tvalid\tparts\tmassKg\tcost\treason\n");
        string directory=Path.Combine(Application.persistentDataPath,"Blueprints");
        var repository=new BlueprintRepository(directory);
        try {
            BlockModules.Configure(database);
            foreach(string file in repository.List()) {
                try {
                    var blueprint=repository.Load(file);
                    var validation=DeploymentValidator.ValidateForRange(blueprint,database);
                    float mass=0,cost=0;foreach(var part in blueprint.parts){var definition=database.Get(part.shape);if(definition){mass+=definition.massKg;cost+=definition.cost;}}
                    output.AppendLine(file+"\t"+validation.IsValid+"\t"+blueprint.parts.Count+"\t"+mass+"\t"+cost+"\t"+string.Join(",",validation.Errors.Select(e=>e.key)));
                } catch(Exception exception){output.AppendLine(file+"\tfalse\t\t\t\t"+exception.GetType().Name+": "+exception.Message.Replace('\n',' '));}
            }
        } finally {BlockModules.Configure(previous);}
        Directory.CreateDirectory("Documentation/FleetRevision6");
        File.WriteAllText("Documentation/FleetRevision6/library-audit.tsv",output.ToString(),new UTF8Encoding(false));
        return directory+"\n"+output;
    }
}
