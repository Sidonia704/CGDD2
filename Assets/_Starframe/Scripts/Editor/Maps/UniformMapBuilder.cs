using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    public static class UniformMapBuilder
    {
        public const string Root="Assets/_Starframe/Maps/UniformVoxels";
        static readonly string[] Sources={"Assets/_Starframe/Maps/Range115Voxels/Layouts/Range115Voxels.asset","Assets/_Starframe/Maps/SaturnRingVoxels/Layouts/SaturnRingVoxels.asset"};
        sealed class Job {public string key,path,map;public VoxelAssetDefinition source;public float scale;}
        [Serializable] public sealed class Entry {public string key,path,source,error,measurementSource;public float scale;public int cells;public long triangles;public Vector3 oldSize,size;}
        [Serializable] sealed class Report {public bool complete,samples;public int expected,errors;public double seconds;public List<Entry> records;}
        [Serializable] sealed class PriorRecord {public string name,operation;}
        [Serializable] sealed class PriorReport {public PriorRecord[] records;}
        static HashSet<string> unaligned;
        static List<Job> jobs,allJobs; static readonly Dictionary<string,VoxelAssetDefinition> results=new Dictionary<string,VoxelAssetDefinition>();
        static readonly List<Entry> records=new List<Entry>();
        static VoxelAssetDefinition lastMeshSource; static Mesh lastMesh;static string lastMeasurement;
        static int next,errors;static bool running,finished,samples;static double started,ended;
        public static string Key(VoxelAssetDefinition a,float s)=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))+"_"+BitConverter.ToUInt32(BitConverter.GetBytes(s),0).ToString("x8");
        public static object Status()=>new {running,finished,samples,completed=next,total=jobs?.Count??0,errors,seconds=(ended>0?ended:EditorApplication.timeSinceStartup)-started,cells=records.Sum(r=>(long)r.cells),latestErrors=records.Where(r=>r.error!=null).Select(r=>r.error).Take(3).ToArray()};
        public static object BeginSamples()=>Begin(true);
        public static object BeginApply()=>Begin(false);
        public static object RetryErrors()
        {
            if(running||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Wait for current work.");
            var keys=new HashSet<string>(records.Where(r=>!string.IsNullOrEmpty(r.error)).Select(r=>r.key));
            if(keys.Count==0)throw new InvalidOperationException("No failed assets to retry.");
            jobs=allJobs.Where(j=>keys.Contains(j.key)).ToList();records.RemoveAll(r=>keys.Contains(r.key));
            next=errors=0;ended=0;finished=false;started=EditorApplication.timeSinceStartup;running=true;EditorApplication.update+=Tick;return Status();
        }
        static object Begin(bool sample)
        {
            if(running||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop active work before rebuilding.");
            jobs=new List<Job>();records.Clear();results.Clear();next=errors=0;ended=0;finished=false;samples=sample;
            var prior=JsonUtility.FromJson<PriorReport>(File.ReadAllText("Assets/_Starframe/Maps/SaturnRingVoxels/BuildReport.json"));
            unaligned=new HashSet<string>(prior.records.Where(r=>r.operation.StartsWith("original occupied",StringComparison.Ordinal)).Select(r=>r.name));
            foreach(string sourceMap in Sources)
            {
                var m=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(sourceMap);string map=m.name;
                foreach(var g in m.instances.GroupBy(i=>Key(i.asset,i.scale)))
                {var i=g.First();jobs.Add(new Job{key=g.Key,source=i.asset,scale=i.scale,map=map,path=Root+"/"+map+"/Assets/"+i.asset.name+"_"+g.Key.Substring(33)+".asset"});}
            }
            if(sample)
            {
                var selected=new List<Job>();
                foreach(string map in jobs.Select(j=>j.map).Distinct())
                {
                    var rocks=jobs.Where(j=>j.map==map&&Rock(j.source)).OrderBy(j=>(j.source.LocalBounds.size*j.scale).sqrMagnitude).ToArray();
                    selected.Add(rocks[rocks.Length/8]);selected.Add(rocks[rocks.Length/2]);selected.Add(rocks.Last());
                }
                selected.Add(jobs.First(j=>j.source.name=="Facility_Cylinder_003"));
                selected.Add(jobs.First(j=>j.source.name=="Facility_Cylinder_023"));
                var old=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(Sources[1]);
                var linked=new HashSet<string>(old.assemblyLinks.SelectMany(l=>new[]{l.instanceIdA,l.instanceIdB}));
                var linkedAssets=new HashSet<VoxelAssetDefinition>(old.instances.Where(i=>linked.Contains(i.id)).Select(i=>i.asset));
                selected.Add(jobs.First(j=>linkedAssets.Contains(j.source)));
                selected.Add(jobs.Where(j=>j.map==old.name&&!Rock(j.source)).OrderBy(j=>(j.source.LocalBounds.size*j.scale).sqrMagnitude).Skip(100).First());
                jobs=selected.Distinct().ToList();foreach(var j in jobs)j.path=Root+"/Samples/Assets/"+j.source.name+"_"+j.key.Substring(33)+".asset";
            }
            foreach(var j in jobs){Folder(Path.GetDirectoryName(j.path).Replace('\\','/'));Folder(Path.GetDirectoryName(j.path.Replace("/Assets/","/Meshes/")).Replace('\\','/'));}
            allJobs=jobs.ToList();
            started=EditorApplication.timeSinceStartup;running=true;EditorApplication.update+=Tick;return Status();
        }
        public static bool Rock(VoxelAssetDefinition a)=>a.name.StartsWith("Asteroid_Rock_",StringComparison.Ordinal)||a.name.StartsWith("field_",StringComparison.Ordinal)||a.name.StartsWith("scatter_",StringComparison.Ordinal);
        static void Tick()
        {
            if(!running||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            // One atomic component per update bounds transient memory; no domain reload while active.
            if(next<jobs.Count)
            {
                var job=jobs[next++];try{Build(job);}catch(Exception e){errors++;records.Add(new Entry{key=job.key,path=job.path,source=AssetDatabase.GetAssetPath(job.source),scale=job.scale,error=e.ToString()});Debug.LogException(e);}
                if(next%10==0)Save(false);
                return;
            }
            try {if(!samples&&errors==0)foreach(var source in Sources)Layout(source);AssetDatabase.SaveAssets();finished=errors==0;}
            catch(Exception e){errors++;records.Add(new Entry{error=e.ToString()});Debug.LogException(e);}
            if(lastMesh)UnityEngine.Object.DestroyImmediate(lastMesh);lastMesh=null;lastMeshSource=null;
            ended=EditorApplication.timeSinceStartup;running=false;EditorApplication.update-=Tick;Save(finished);
        }
        static void Build(Job job)
        {
            var b=job.source.LocalBounds;b.center*=job.scale;b.size*=job.scale;
            List<VoxelCell> cells;
            if(Rock(job.source))cells=PatchworkVoxelRocks.Build(b,1,2000000,job.source.name,out _);
            else
            {
                if(lastMeshSource!=job.source||!lastMesh)
                {
                    if(lastMesh)UnityEngine.Object.DestroyImmediate(lastMesh);
                    var reference=job.map=="SaturnRingVoxels"&&unaligned.Contains(job.source.name)?AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Starframe/Maps/SaturnRingContinuous/Meshes/"+job.source.name+".asset"):null;
                    if(reference)
                    {
                        // Measurement only: the displayed cache below is rebuilt entirely from unit blocks.
                        lastMeasurement=AssetDatabase.GetAssetPath(reference)+" (unaligned local frame; bounds fitted at authoring)";lastMesh=UnityEngine.Object.Instantiate(reference);var oldBounds=job.source.LocalBounds;var refBounds=reference.bounds;
                        var ratio=new Vector3(oldBounds.size.x/refBounds.size.x,oldBounds.size.y/refBounds.size.y,oldBounds.size.z/refBounds.size.z);
                        lastMesh.vertices=reference.vertices.Select(v=>oldBounds.center+Vector3.Scale(v-refBounds.center,ratio)).ToArray();lastMesh.RecalculateBounds();
                    }
                    else
                    {
                        lastMeasurement="native cells exterior, complete coverage";var state=job.source.cells.ToDictionary(c=>c.position,c=>new VoxelCellState{position=c.position,shape=c.shape,surface=c.surface,quarterTurns=c.quarterTurns,modelOffset=c.modelOffset});
                        lastMesh=VoxelGeometry.Build(state,job.source.RenderMaterialCount,job.source.cellSize,job.source.hullModules);
                    }
                    lastMeshSource=job.source;
                }
                cells=UniformSurfaceSampler.Build(lastMesh,job.scale,2000000);
            }
            if(cells.Count==0)throw new InvalidOperationException("Empty uniform asset: "+job.key);
            var target=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(job.path);
            if(!target){target=UnityEngine.Object.Instantiate(job.source);AssetDatabase.CreateAsset(target,job.path);}else EditorUtility.CopySerialized(job.source,target);
            target.name=Path.GetFileNameWithoutExtension(job.path);target.cellSize=1;target.cells=cells;target.hullModules=SceneVoxelModules.GetOrCreate();
            target.authoredPhysics=UniformPhysicsCapture.Capture(job.source,job.scale);
            target.massPerCell=target.authoredPhysics.mass/cells.Count;target.RebuildCellChunks();target.NotifyChanged();
            var mesh=VoxelAssemblyBaker.Bake(target,job.path.Replace("/Assets/","/Meshes/"));
            results[job.key]=target;records.Add(new Entry{key=job.key,path=job.path,source=AssetDatabase.GetAssetPath(job.source),scale=job.scale,measurementSource=Rock(job.source)?"analytic native unit primitives":lastMeasurement,cells=cells.Count,triangles=VoxelAssemblyBaker.TriangleCount(mesh),oldSize=b.size,size=target.LocalBounds.size});
        }
        static void Layout(string sourcePath)
        {
            var source=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(sourcePath);string p=Root+"/"+source.name+"/Layouts/"+source.name+".asset";Folder(Path.GetDirectoryName(p).Replace('\\','/'));
            var target=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(p);if(!target){target=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(target,p);}else EditorUtility.CopySerialized(source,target);
            target.enforceUniformGrid=true;foreach(var i in target.instances){i.asset=results[Key(i.asset,i.scale)];i.scale=1;}
            foreach(var l in target.assemblyLinks)
            {
                var oldA=source.instances.First(i=>i.id==l.instanceIdA);var oldB=source.instances.First(i=>i.id==l.instanceIdB);
                l.hasLegacyAnchorBounds=true;l.legacyAnchorBoundsA=new Bounds((Vector3)l.anchorCellA*(oldA.asset.cellSize*oldA.scale),Vector3.one*(oldA.asset.cellSize*oldA.scale));l.legacyAnchorBoundsB=new Bounds((Vector3)l.anchorCellB*(oldB.asset.cellSize*oldB.scale),Vector3.one*(oldB.asset.cellSize*oldB.scale));
                l.anchorCellA=Nearest(target.instances.First(i=>i.id==l.instanceIdA).asset,l.legacyAnchorBoundsA.center);
                l.anchorCellB=Nearest(target.instances.First(i=>i.id==l.instanceIdB).asset,l.legacyAnchorBoundsB.center);
            }
            if(!target.Validate(out var error))throw new InvalidOperationException(error);EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);
        }
        static Vector3Int Nearest(VoxelAssetDefinition a,Vector3 p){float best=float.MaxValue;Vector3Int result=default;foreach(var c in a.cells){float d=((Vector3)c.position-p).sqrMagnitude;if(d<best){best=d;result=c.position;}}return result;}
        static void Save(bool done){Directory.CreateDirectory("Documentation/UniformVoxels");File.WriteAllText("Documentation/UniformVoxels/"+(samples?"Samples":"Apply")+".json",JsonUtility.ToJson(new Report{complete=done,samples=samples,expected=allJobs.Count,errors=errors,seconds=(ended>0?ended:EditorApplication.timeSinceStartup)-started,records=records},true));}
        public static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;int s=path.LastIndexOf('/');Folder(path.Substring(0,s));AssetDatabase.CreateFolder(path.Substring(0,s),path.Substring(s+1));}
    }
}


