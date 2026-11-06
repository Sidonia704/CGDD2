// @role Price blueprint thumbnail cards using the same mission/module cost rules as deployment.
// @calls MissionDefinition, ModuleDatabase, L10n
using System.Globalization;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Presentation;
namespace Starframe.App
{
    public static class BlueprintCostDisplay
    {
        public static float Calculate(BlueprintData blueprint,ModuleDatabase database,MissionDefinition mission=null)
        {
            if(blueprint?.parts==null||!database)return 0;
            float cost=0;
            foreach(var part in blueprint.parts){if(part==null)continue;var definition=database.Get(part.shape);if(definition)cost+=mission?mission.CostFor(definition):definition.cost;}
            return cost;
        }
        public static string Format(float cost,bool english)=>(english?"COST ":"造价 ")+cost.ToString("N0",CultureInfo.InvariantCulture);
        public static string Label(BlueprintData blueprint,ModuleDatabase database,MissionDefinition mission=null)=>Format(Calculate(blueprint,database,mission),L10n.IsEnglish);
    }
}
