using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

public static class BattleshipAllocationProbe
{
    public static string Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play first");
        var previous=BlockModules.ConfiguredDatabase;
        try {
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            BlockModules.Configure(tuning.database);
            var blueprint=BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(File.ReadAllText("Documentation/Battleship-2026-11-02/Blueprints/Battleship-Heavy.json")));
            var states=blueprint.parts.Select(r=>new ShipPartState{record=r,hp=tuning.Get(r.shape).maxHp}).ToList();
            var core=blueprint.parts.First(r=>r.shape==BlockShape.Core);
            var p=ShipPerformance.Calculate(states,core.rotation,tuning);
            var move=new Vector3(.2f,.1f,1);var turn=new Vector3(.1f,.2f,.1f);
            for(int i=0;i<1000;i++)p.AllocateWrench(move,turn,tuning,out _);
            long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
            const int count=10000;
            for(int i=0;i<count;i++)p.AllocateWrench(move,turn,tuning,out _);
            watch.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            var result="Stopped Editor, warmed cached allocation only; not full-frame/Player benchmark. Parts="+states.Count+", nozzles="+p.nozzles.Count+", iterations="+count+", msPerAllocation="+(watch.Elapsed.TotalMilliseconds/count).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+", allocatedBytesTotal="+bytes+" (Stopwatch creation included).";
            File.WriteAllText("Documentation/Battleship-2026-11-02/allocation-performance.txt",result);return result;
        } finally {BlockModules.Configure(previous);}
    }
}
