using System.Linq;
using UnityEngine;
using Starframe.Maps;
using Starframe.Flight;
public static class NaturalRockRuntimeCheck
{
    public static object Run()
    {
        var map=Object.FindFirstObjectByType<SpaceMapRuntime>();
        return new {frame=Time.frameCount,time=Time.time,playing=Application.isPlaying,
            map=map?map.definition.name:null,ready=map&&map.BuildIsComplete,
            loaded=map?map.Structures.Count:0,error=map?map.BuildError:null,
            dynamicBodies=map?map.Structures.Count(s=>s.Body&&!s.Body.isKinematic):0,
            voxelCaches=map?map.Structures.Count(s=>s.asset.HasValidInitialLod):0,
            unitGrid=map?map.Structures.Count(s=>s.asset.cellSize==1&&(s.transform.lossyScale-Vector3.one).sqrMagnitude<1e-8f):0,
            cachedCellStates=map?map.Structures.Sum(s=>s.CachedCellStateCount):0,
            activeLinks=map?map.ActiveAssemblyContactCount:0,
            fixedJoints=map?map.Structures.Sum(s=>s.GetComponents<FixedJoint>().Length):0,
            backdrop=string.Join(" | ",Object.FindObjectsByType<RangeAsteroidBackdrop>(FindObjectsSortMode.None).Select(b=>"enabled="+b.backdropEnabled+", drawn="+b.visibleRockCount+", triangles="+b.estimatedTriangles))};
    }
}
