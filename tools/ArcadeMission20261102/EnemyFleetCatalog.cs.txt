// @role Make authored enemy blueprints available as safe, editable player-library copies.
// @calls EnemyArchetypeDefinition, BlueprintRepository

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Starframe.Construction;

namespace Starframe.Combat
{
    [CreateAssetMenu(menuName="Starframe/Enemy Fleet Catalog")]
    public sealed class EnemyFleetCatalog : ScriptableObject
    {
        public EnemyArchetypeDefinition[] enemies=Array.Empty<EnemyArchetypeDefinition>();
    }

    public static class EnemyFleetLibrary
    {
        public static EnemyArchetypeDefinition[] LoadTemplates()
        {
            var catalog=Resources.Load<EnemyFleetCatalog>("Enemies/FleetTemplates");
            return catalog&&catalog.enemies!=null?catalog.enemies.Where(enemy=>enemy).ToArray():Array.Empty<EnemyArchetypeDefinition>();
        }

        public static string[] ImportInto(BlueprintRepository repository,IEnumerable<EnemyArchetypeDefinition> templates)
        {
            if(repository==null)throw new ArgumentNullException(nameof(repository));
            if(templates==null)throw new ArgumentNullException(nameof(templates));
            // Validate the entire pack before writing its first copy. Source assets are never renamed.
            var copies=new List<BlueprintData>();
            foreach(var enemy in templates.Where(enemy=>enemy).OrderBy(enemy=>enemy.id,StringComparer.Ordinal)){
                copies.Add(enemy.CreateBlueprint());
                if(enemy.alternateBlueprint&&enemy.alternateBlueprint!=enemy.blueprint)copies.Add(enemy.CreateBlueprint(true));
            }
            var files=new List<string>(copies.Count);
            foreach(var data in copies){
                string original=data.name,name=original;int suffix=2;
                while(repository.NameExists(name))name=original+" ("+(suffix++)+")";
                data.name=name;files.Add(repository.Save(data));
            }
            return files.ToArray();
        }
    }
}
