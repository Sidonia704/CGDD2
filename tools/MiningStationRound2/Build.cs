using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;
using Starframe.Construction;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        public const string OutputRoot="Assets/_Starframe/Maps/MiningStationRound2/Generated_v04";
        [Serializable] public class PrototypeRecord
        {
            public string name, category, assetPath, sourceFile, sourceNode;
            public int[] sourceIslands; public float cellSize, sourceExtraScale=1;
            public Vector3 assemblyPosition, assemblyEuler, localSize;
            public int cells, sourceTriangles, sourceConnectedIslands, voxelConnectedIslands, discardedMinorCells, largeBlockGroups, enclosedAirCells;
            public bool solidRequired, fillInterior, block8FootprintsValidated;public string selectionNote;
            public int sourceBoundaryEdges, surfaceOnlyCells, interiorFilledCells;
            public double samplingMs; public Dictionary<string,int> shapeCounts;
        }
        [Serializable] public class BuildReport
        {
            public string scope="Round 2 prototypes + local density only; not the complete station/map";
            public float mapUnits=2500, mainStationLength=500, sourceStationLength, sourceToTargetScale;
            public List<PrototypeRecord> prototypes=new List<PrototypeRecord>();
            public int patchBodies, patchCells; public float patchMinimumGap;
            public string patchPath, galleryPath, assemblyStudyPath, scatterReport;
        }
        static readonly Dictionary<string,Mesh> ComparisonSourceMeshes=new Dictionary<string,Mesh>();
        static readonly Dictionary<string,VoxelAssetDefinition> PreparedAssets=new Dictionary<string,VoxelAssetDefinition>();
        static int SourceBoundaryEdges(Vector3[] vertices,int[] triangles)
        {
            var keys=new Dictionary<Vector3Int,int>();var ids=new int[vertices.Length];for(int i=0;i<vertices.Length;i++){var key=Vector3Int.RoundToInt(vertices[i]*1000);if(!keys.TryGetValue(key,out int id)){id=keys.Count;keys.Add(key,id);}ids[i]=id;}
            var edges=new Dictionary<string,int>();for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++){int a=ids[triangles[i+j]],b=ids[triangles[i+(j+1)%3]];if(a==b)continue;string key=Math.Min(a,b)+"/"+Math.Max(a,b);edges.TryGetValue(key,out int count);edges[key]=count+1;}return edges.Values.Count(n=>n!=2);
        }
        static List<Vector3> ClipPolygonX(List<Vector3> input,float plane,bool keepGreater)
        {
            var output=new List<Vector3>();if(input.Count==0)return output;
            var a=input[input.Count-1];bool ai=keepGreater?a.x>=plane:a.x<=plane;
            foreach(var b in input){bool bi=keepGreater?b.x>=plane:b.x<=plane;if(ai!=bi){float t=(plane-a.x)/(b.x-a.x);output.Add(Vector3.LerpUnclamped(a,b,t));}if(bi)output.Add(b);a=b;ai=bi;}return output;
        }
        static void MiddleThird(ref Vector3[] vertices,ref int[] triangles)
        {
            var bounds=PointsBounds(vertices);float half=bounds.size.x/6;var vs=new List<Vector3>();var ts=new List<int>();
            for(int i=0;i<triangles.Length;i+=3){var poly=new List<Vector3>{vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]]};poly=ClipPolygonX(poly,-half,true);poly=ClipPolygonX(poly,half,false);for(int j=1;j+1<poly.Count;j++){int start=vs.Count;vs.Add(poly[0]);vs.Add(poly[j]);vs.Add(poly[j+1]);ts.Add(start);ts.Add(start+1);ts.Add(start+2);}}
            vertices=vs.ToArray();triangles=ts.ToArray();
        }
        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            int split=path.LastIndexOf('/');EnsureFolder(path.Substring(0,split));AssetDatabase.CreateFolder(path.Substring(0,split),path.Substring(split+1));
        }
        static Vector3 PowerAxis(float[,] cov,Vector3 initial,Vector3 exclude)
        {
            var v=initial;
            for(int i=0;i<40;i++) { var next=new Vector3(cov[0,0]*v.x+cov[0,1]*v.y+cov[0,2]*v.z,cov[1,0]*v.x+cov[1,1]*v.y+cov[1,2]*v.z,cov[2,0]*v.x+cov[2,1]*v.y+cov[2,2]*v.z);next-=exclude*Vector3.Dot(next,exclude);if(next.sqrMagnitude<1e-10f)break;v=next.normalized; }
            return v.normalized;
        }
        static Quaternion ComponentAxes(Vector3[] points,bool align)
        {
            if(!align)return Quaternion.identity;
            Vector3 mean=Vector3.zero;foreach(var p in points)mean+=p;mean/=points.Length;
            var cov=new float[3,3];foreach(var p in points){var d=p-mean;for(int a=0;a<3;a++)for(int b=0;b<3;b++)cov[a,b]+=d[a]*d[b];}
            var x=PowerAxis(cov,new Vector3(.91f,.31f,.27f),Vector3.zero);if(x.x<0)x=-x;
            var y=PowerAxis(cov,Vector3.up-x*Vector3.Dot(Vector3.up,x),x);
            if(y.sqrMagnitude<.1f)y=Vector3.Cross(x,Vector3.forward).normalized;if(Vector3.Dot(y,Vector3.up)<0)y=-y;
            var z=Vector3.Cross(x,y).normalized;y=Vector3.Cross(z,x).normalized;
            return Quaternion.LookRotation(z,y);
        }
        static PrototypeRecord CreatePrototype(BuildReport report,SourceMesh source,int[] partIds,string name,string category,float cellSize,bool fillInterior,bool align=true,float extraScale=1,bool manufactured=false)
        {
            var sourceIndices=partIds==null?source.triangles:partIds.SelectMany(id=>source.parts[id].indices).ToArray();
            var unique=sourceIndices.Distinct().ToArray();var selected=unique.Select(i=>source.vertices[i]*extraScale).ToArray();
            var rotation=ComponentAxes(selected,align);var inverse=Quaternion.Inverse(rotation);
            var b=PointsBounds(selected.Select(p=>inverse*p));var center=rotation*b.center;
            var local=selected.Select(p=>inverse*(p-center)).ToArray();var indexMap=new Dictionary<int,int>();for(int i=0;i<unique.Length;i++)indexMap[unique[i]]=i;
            var tris=sourceIndices.Select(i=>indexMap[i]).ToArray();
            bool trussModule=name=="Station_OpenTruss";if(trussModule)MiddleThird(ref local,ref tris);
            int openEdges=SourceBoundaryEdges(local,tris);if(fillInterior&&openEdges!=0)throw new Exception(name+": non-watertight source ("+openEdges+" open/non-manifold edges); must cap semantically, not accept a leaking shell");
            var watch=System.Diagnostics.Stopwatch.StartNew();var allCells=Voxelize(local,tris,cellSize,fillInterior,20000);int surfaceCells=fillInterior?Voxelize(local,tris,cellSize,false,20000).Count:allCells.Count;watch.Stop();
            int addedInterior=allCells.Count-surfaceCells;if(fillInterior&&addedInterior<=0&&Mathf.Min(b.size.x,b.size.y,b.size.z)>cellSize*3)throw new Exception(name+": no verified interior fill");
            var groups=ConnectedGroups(allCells).OrderByDescending(g=>g.Count).ToList();if(groups.Count==0)throw new Exception(name+": no occupied cells");
            int minor=groups.Skip(1).Sum(g=>g.Count);
            if(minor>Mathf.Max(12,allCells.Count*.015f))throw new Exception(name+": needs semantic split; "+groups.Count+" voxel islands, "+minor+" cells outside primary island");
            var asset=ScriptableObject.CreateInstance<VoxelAssetDefinition>();asset.name=name;asset.category=category;
            asset.cellSize=cellSize;asset.hullModules=Starframe.EditorTools.SpaceMapStudioContent.HullDatabase;asset.cells=groups[0];
            if(manufactured)foreach(var cell in asset.cells){cell.shape=BlockShape.Cube;cell.quarterTurns=Vector3Int.zero;cell.modelOffset=Vector3Int.zero;}
            int largeBlocks=PackLargeBlocks(asset.cells);var packedGroups=asset.cells.Where(c=>c.shape==BlockShape.Block8).GroupBy(c=>c.position-c.modelOffset).ToArray();
            if(packedGroups.Length!=largeBlocks||packedGroups.Any(g=>g.Count()!=8||g.Any(c=>c.quarterTurns!=Vector3Int.zero||c.modelOffset.x<0||c.modelOffset.x>1||c.modelOffset.y<0||c.modelOffset.y>1||c.modelOffset.z<0||c.modelOffset.z>1)))throw new Exception(name+": incomplete or invalid Block8 footprint");
            int air=EnclosedAirCells(asset.cells);if(fillInterior&&air!=0)throw new Exception(name+": solid acceptance failed, "+air+" enclosed air cells");
            asset.baseColor=Color.white;asset.surfaceColors=new List<Color>{Color.white};
            if(!asset.hullModules)throw new Exception("Hull module database missing");
            if(!asset.Validate(out string error))throw new Exception(name+": "+error);
            string path=OutputRoot+"/Assets/"+name+".asset";if(AssetDatabase.LoadMainAssetAtPath(path))throw new Exception("Refusing to overwrite "+path);
            PreparedAssets.Add(path,asset); // Preflight every component before any asset is persisted.
            var record=new PrototypeRecord {name=name,category=category,assetPath=path,sourceFile=source.file,sourceNode=source.node,sourceIslands=partIds??Enumerable.Range(0,source.parts.Length).ToArray(),cellSize=cellSize,sourceExtraScale=extraScale,assemblyPosition=center/extraScale,assemblyEuler=rotation.eulerAngles,localSize=asset.LocalBounds.size,cells=asset.cells.Count,sourceTriangles=tris.Length/3,sourceConnectedIslands=partIds?.Length??source.parts.Length,voxelConnectedIslands=1,discardedMinorCells=minor,largeBlockGroups=largeBlocks,block8FootprintsValidated=true,enclosedAirCells=air,solidRequired=fillInterior,fillInterior=fillInterior,sourceBoundaryEdges=openEdges,surfaceOnlyCells=surfaceCells,interiorFilledCells=addedInterior,selectionNote=trussModule?"Central third of source island 31, a reusable ~40-unit bay module; not the full ~115-unit truss":"Complete selected source island(s); rocks deliberately normalized for this density study",samplingMs=watch.Elapsed.TotalMilliseconds,shapeCounts=asset.cells.GroupBy(c=>c.shape.ToString()).ToDictionary(g=>g.Key,g=>g.Count())};
            report.prototypes.Add(record);
            var mesh=new Mesh {name=name+" source reference only",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.vertices=local;mesh.triangles=tris;mesh.RecalculateNormals();mesh.RecalculateBounds();ComparisonSourceMeshes[name]=mesh;
            return record;
        }
        public static string Build()
        {
            if(AssetDatabase.IsValidFolder(OutputRoot))throw new Exception("No-overwrite: output already exists; change OutputRoot to a new version for a new study.");
            var src=LoadSources();var report=new BuildReport {sourceStationLength=src.sourceStationLength,sourceToTargetScale=src.sourceToTargetScale};
            Directory.CreateDirectory(EvidenceRoot);
            try
            {
                var main=src.meshes.First(m=>m.file=="mainstru.fbx");
                CreatePrototype(report,main,new[]{2},"Station_SphericalTank","第二轮·站体",2f,true,false);
                CreatePrototype(report,main,new[]{12},"Station_CapsuleTank","第二轮·站体",1.5f,true);
                CreatePrototype(report,main,new[]{31},"Station_OpenTruss","第二轮·站体",1f,false,true,1,true);
                CreatePrototype(report,main,new[]{28},"Station_EquipmentCage","第二轮·站体",1.25f,false,true,1,true);
                CreatePrototype(report,main,new[]{179},"Station_NotchedSlab","第二轮·站体",2f,true,true,1,true);
                var scatter=src.meshes.Where(m=>m.file=="scatter.fbx").ToList();
                CreatePrototype(report,scatter.First(m=>m.node=="Breccia Rocks_Low.022"),null,"Rock_Ridged","第二轮·岩石",3f,true,true,1.8f);
                CreatePrototype(report,scatter.First(m=>m.node=="Procedural Brown Rocks.001"),null,"Rock_Notched","第二轮·岩石",2.5f,true,true,2f);
                CreatePrototype(report,scatter.First(m=>m.node.StartsWith("asteroid 01.007")),null,"Rock_Faceted","第二轮·岩石",1.75f,true,true,2.2f);
                var debris=src.meshes.First(m=>m.file=="debri.fbx");
                // Use actual authored wreckage, not a noise stamp; select structurally distinct larger islands.
                var debrisChoices=debris.parts.Where(p=>Mathf.Max(p.size.x,p.size.y,p.size.z)>8).OrderByDescending(p=>p.triangleCount).ThenByDescending(p=>p.size.sqrMagnitude).Take(2).ToArray();
                for(int i=0;i<debrisChoices.Length;i++)CreatePrototype(report,debris,new[]{debrisChoices[i].id},"Debris_"+(i==0?"FracturedChunk":"AngledShard"),"第二轮·残骸",1f,true);
                EnsureFolder(OutputRoot+"/Assets");EnsureFolder(OutputRoot+"/Layouts");foreach(var prepared in PreparedAssets)AssetDatabase.CreateAsset(prepared.Value,prepared.Key);
                var rocks=report.prototypes.Where(p=>p.category.Contains("岩石")).Select(p=>AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p.assetPath)).ToList();
                var wrecks=report.prototypes.Where(p=>p.category.Contains("残骸")).Select(p=>AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p.assetPath)).ToList();
                var patch=CreateDensityPatch(rocks,wrecks);report.patchPath=OutputRoot+"/Layouts/LocalDensityPatch.asset";AssetDatabase.CreateAsset(patch,report.patchPath);
                report.patchBodies=patch.instances.Count;report.patchCells=patch.instances.Sum(i=>i.asset.cells.Count);report.patchMinimumGap=PatchMinimumGap(patch);report.scatterReport=ScatterLastReport;
                var gallery=ScriptableObject.CreateInstance<SpaceMapDefinition>();gallery.size=new Vector3(2500,2500,2500);gallery.name="Round 2 component gallery";gallery.enemySpawns.Clear();
                for(int i=0;i<report.prototypes.Count;i++){var p=report.prototypes[i];gallery.instances.Add(new VoxelMapInstance{id="r2-gallery-"+i,label=p.name,asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p.assetPath),position=new Vector3((i%5-2)*150,0,(i/5)*160),eulerAngles=Vector3.zero,scale=1});}
                report.galleryPath=OutputRoot+"/Layouts/ComponentGallery.asset";AssetDatabase.CreateAsset(gallery,report.galleryPath);
                var assembly=ScriptableObject.CreateInstance<SpaceMapDefinition>();assembly.name="Partial station assembly STUDY - NOT full station";assembly.size=Vector3.one*2500;assembly.enemySpawns.Clear();
                foreach(var p in report.prototypes.Where(p=>p.category.Contains("站体")))assembly.instances.Add(new VoxelMapInstance {id="r2-study-"+p.name,label=p.name,asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p.assetPath),position=p.assemblyPosition,eulerAngles=p.assemblyEuler});
                report.assemblyStudyPath=OutputRoot+"/Layouts/PartialStationStudy.asset";AssetDatabase.CreateAsset(assembly,report.assemblyStudyPath);
                foreach(var layout in new[]{patch,gallery,assembly})if(!layout.Validate(out string e))throw new Exception("Invalid layout "+layout.name+": "+e);
                AssetDatabase.SaveAssets();File.WriteAllText(EvidenceRoot+"/build-report.json",Json(report));
                CaptureComparisons(report);CaptureSolidSections(report);CaptureLayout(patch,EvidenceRoot+"/density-context.png",new Vector3(.8f,.7f,1),1500,900,true);CaptureLayout(patch,EvidenceRoot+"/density-oblique.png",new Vector3(.8f,.7f,1),1300,850);CaptureLayout(patch,EvidenceRoot+"/density-top.png",new Vector3(.01f,1,.01f),1300,850);
                CaptureLayout(assembly,EvidenceRoot+"/partial-station-study.png",new Vector3(1,.8f,1),1300,850);
                return Json(report);
            }
            finally { foreach(var mesh in ComparisonSourceMeshes.Values)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);ComparisonSourceMeshes.Clear();foreach(var asset in PreparedAssets.Values)if(asset&&!AssetDatabase.Contains(asset))UnityEngine.Object.DestroyImmediate(asset);PreparedAssets.Clear(); }
        }
    }
}
