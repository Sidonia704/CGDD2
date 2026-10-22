// @role Transfer a blueprint snapshot between the editor and test range.
// @calls BlueprintRepository

using UnityEngine;
using Starframe.Construction;
namespace Starframe.Flight
{
    public static class FlightSession
    {
        public const string EditorScene="ShipEditor",RangeScene="FlightRange";
        public static BlueprintData EditorDraft {get;private set;}
        public static bool EditorDirty {get;private set;}
        public static bool ReturningToEditor {get;private set;}
        public static void Begin(BlueprintData blueprint,bool dirty)
        {
            EditorDraft=BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(JsonUtility.ToJson(blueprint)));
            EditorDirty=dirty;ReturningToEditor=false;
        }
        public static void Return(){ReturningToEditor=true;}
        public static void Restored(){ReturningToEditor=false;}
    }
}