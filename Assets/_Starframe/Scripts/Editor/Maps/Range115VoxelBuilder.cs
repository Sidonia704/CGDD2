using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Starframe.Maps;
using Starframe.Construction;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    // Source meshes are measurement references only. Every output triangle comes from existing hull modules.
    public static class Range115VoxelBuilder
    {
        public const string Root="Assets/_Starframe/Maps/Range115Voxels";
        const string SourceRoot="Assets/_Starframe/Maps/Range115";
        public static string Status{get;private set;}="Idle";
        static readonly Queue<Action> jobs=new Queue<Action>();
        static readonly Dictionary<VoxelAssetDefinition,VoxelAssetDefinition> replacements=new Dictionary<VoxelAssetDefinition,VoxelAssetDefinition>();
        static readonly Dictionary<VoxelAssetDefinition,Vector3> phases=new Dictionary<VoxelAssetDefinition,Vector3>();
        static readonly List<string> report=new List<string>();
        static string output;static bool sample;
        public static void BuildSample()=>Begin(true);
        [MenuItem("Starframe/地图工作室/创建115真实体块地图")]
        public static void BeginBuild()=>Begin(false);
        static void Begin(bool onlySample)
        {
            if(jobs.Count!=0)throw new InvalidOperationException("Range115 voxel build is running");
            sample=onlySample;output=sample?Root+"/Samples":Root;
            if(AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(output+"/Layouts/"+(sample?"SampleGallery":"Range115Voxels")+".asset"))throw new InvalidOperationException("Output exists; preserved without overwrite");
            Folder(output+"/Assets");Folder(output+"/Meshes");Folder(output+"/Layouts");replacements.Clear();phases.Clear();report.Clear();
            var sources=AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{SourceRoot+"/Assets"}).Select(g=>AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(a=>a.name).ToArray();
            if(sample)sources=sources.Where(a=>a.name=="Facility_Cylinder_001"||a.name=="Facility_Cylinder_019"||a.name=="Asteroid_Rock_210").ToArray();
            if(sources.Length!=(sample?3:26))throw new InvalidOperationException("Expected "+(sample?3:26)+" source assemblies, found "+sources.Length);
            foreach(var source in sources){var s=source;jobs.Enqueue(()=>BuildAsset(s));}jobs.Enqueue(Finish);Status="Queued "+sources.Length+" voxel assemblies";EditorApplication.update+=Tick;
        }
        static void Tick(){if(jobs.Count==0){EditorApplication.update-=Tick;return;}try{Status="Building voxel assemblies; "+jobs.Count+" steps remain";jobs.Dequeue()();}catch(Exception e){jobs.Clear();EditorApplication.update-=Tick;Status="FAILED: "+e;Debug.LogException(e);}}
        static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;int i=path.LastIndexOf('/');Folder(path.Substring(0,i));AssetDatabase.CreateFolder(path.Substring(0,i),path.Substring(i+1));}
        static float Max(Vector3 v)=>Mathf.Max(v.x,Mathf.Max(v.y,v.z));
        static bool Cylinder(VoxelAssetDefinition source,Mesh mesh)
        {
            var bounds=mesh.bounds;
            if(!source.category.Contains("Facility")||bounds.size.z<=Mathf.Max(bounds.size.x,bounds.size.y)*1.8f||Mathf.Abs(bounds.size.x-bounds.size.y)>=Mathf.Max(bounds.size.x,bounds.size.y)*.08f)return false;
            // An elongated round envelope is insufficient: open trusses have mostly oblique bracing and no end-cap area.
            // Never revolve the023 authored lattice into detached radial rings.
            if(source.name=="Facility_Cylinder_023")return false;
            var vertices=mesh.vertices;var triangles=mesh.GetTriangles(0);double total=0,inclined=0,endCaps=0;
            for(int i=0;i<triangles.Length;i+=3){var cross=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]);float area=cross.magnitude;if(area<1e-7f)continue;float axis=Mathf.Abs(cross.z)/area;total+=area;if(axis>.05f&&axis<.95f)inclined+=area;if(axis>.95f)endCaps+=area;}
            return total>0&&inclined/total<.45&&endCaps/total>.002;
        }

        static void BuildAsset(VoxelAssetDefinition source)
        {
            string sourceMeshPath=SourceRoot+"/Meshes/"+source.name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(sourceMeshPath);if(!mesh)throw new Exception("Missing canonical measurement mesh "+sourceMeshPath);
            bool rock=source.category.Contains("Asteroid"),cylinder=Cylinder(source,mesh);int cap=Mathf.Min(VoxelAssetDefinition.MaximumCells,Mathf.Max(256,source.cells.Count*4));bool truss=source.name=="Facility_Cylinder_023";if(truss)cap=15000;
            // Rocks get one finer controlled grade; thin manufactured surfaces use metre-scale modules.
            float cell=rock?Mathf.Max(.5f,source.cellSize*.72f):Mathf.Clamp(Mathf.Min(mesh.bounds.size.x,Mathf.Min(mesh.bounds.size.y,mesh.bounds.size.z))/14f,.65f,2.5f);
            if(cylinder)cell=Mathf.Clamp(Mathf.Min(mesh.bounds.size.x,mesh.bounds.size.y)/16f,.75f,3f);
            List<VoxelCell> cells=null;Vector3 phase=Vector3.zero;string method=truss?"reviewed regular open box truss: four chords,16cell frames, exact45degree continuous Wedge braces":rock?"asymmetric low-frequency voxel boulder":cylinder?"source axial sections -> continuous octagonal Cube/Wedge rings":"clipped local surface fitting + aligned planar grid";
            for(int attempt=0;attempt<16;attempt++){
                phase=rock||cylinder||truss?Vector3.zero:PlanarPhase(mesh,cell);
                try{cells=truss?RegularVoxelTruss.Build(mesh.bounds,out cell):rock?BuildFacetedRock(mesh.bounds,cell,cap,source.name):cylinder?CylinderCells(mesh,cell,cap):Range115FittedSampling.Voxelize(mesh.vertices.Select(v=>v-phase).ToArray(),mesh.GetTriangles(0),cell,!truss,cap);break;}
                catch(InvalidOperationException){if(cell>=10f)throw;cell=Mathf.Min(10f,cell*(truss?1.15f:1.18f));}
            }
            if(cells==null||cells.Count==0)throw new Exception("No voxel cells for "+source.name);
            var asset=ScriptableObject.CreateInstance<VoxelAssetDefinition>();asset.name=source.name;asset.category=source.category.Replace("115","115 Voxels");asset.cellSize=cell;asset.hullModules=SceneVoxelModules.GetOrCreate();asset.baseColor=source.baseColor;asset.surfaceColors=new List<Color>(source.surfaceColors);asset.surfaces=source.surfaces;
            if(rock){asset.surfaces=new[]{NaturalRockRebuilder.MatteRockMaterial(source.name)};asset.baseColor=Color.white;asset.surfaceColors=new List<Color>{Color.white};}
            asset.cells=cells;asset.massPerCell=source.massPerCell*source.cells.Count*Mathf.Pow(source.cellSize,3)/(cells.Count*Mathf.Pow(cell,3));
            if(!asset.Validate(out string error))throw new Exception(asset.name+": "+error);
            AssetDatabase.CreateAsset(asset,output+"/Assets/"+asset.name+".asset");var baked=VoxelAssemblyBaker.Bake(asset,output+"/Meshes/"+asset.name+".asset");replacements.Add(source,asset);phases.Add(source,phase);
            var counts=string.Join(", ",cells.GroupBy(c=>c.shape).OrderBy(g=>g.Key).Select(g=>g.Key+"="+g.Count()));
            string line=source.name+" | cells="+cells.Count+" (old="+source.cells.Count+", cap="+cap+") | cellSize="+cell+" | triangles="+VoxelAssemblyBaker.TriangleCount(baked)+" | "+counts+" | phase="+phase+" | method="+method+" | source="+sourceMeshPath+" | originalFBX=Assets/_Starframe/assetsForCodex/115/115reference.fbx or115.fbx; canonical geometry measured, never rendered";report.Add(line);Debug.Log(line);
        }
        static Vector3 PlanarPhase(Mesh mesh,float cell)
        {
            var vertices=mesh.vertices;var triangles=mesh.GetTriangles(0);var planes=new List<KeyValuePair<float,float>>[3];for(int a=0;a<3;a++)planes[a]=new List<KeyValuePair<float,float>>();
            for(int i=0;i<triangles.Length;i+=3){var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];var n=Vector3.Cross(b-a,c-a);float area=n.magnitude;if(area<1e-7f)continue;n/=area;for(int axis=0;axis<3;axis++)if(Mathf.Abs(n[axis])>.995f)planes[axis].Add(new KeyValuePair<float,float>((a[axis]+b[axis]+c[axis])/3,area));}
            var result=Vector3.zero;for(int axis=0;axis<3;axis++){float best=float.MaxValue,bestPhase=0;for(int q=0;q<16;q++){float phase=q*cell/16,score=0;foreach(var plane in planes[axis]){float coordinate=(plane.Key-phase)/cell;float residual=Mathf.Abs(coordinate-(Mathf.Round(coordinate-.5f)+.5f));score+=plane.Value*residual*residual;}if(score<best){best=score;bestPhase=phase;}}result[axis]=bestPhase;}return result;
        }
        struct Ring{public float inner,outer;}
        static List<Ring> Section(Mesh mesh,float z)
        {
            var vs=mesh.vertices;var ts=mesh.GetTriangles(0);var hits=new List<float>();
            // Axis-aligned ray across the actual source section preserves source bores and separate rings.
            for(int i=0;i<ts.Length;i+=3){var a=vs[ts[i]];var b=vs[ts[i+1]];var c=vs[ts[i+2]];var e1=b-a;var e2=c-a;var h=Vector3.Cross(Vector3.right,e2);float det=Vector3.Dot(e1,h);if(Mathf.Abs(det)<1e-8f)continue;var offset=new Vector3(-mesh.bounds.extents.x*3-1,.000137f,z+.000173f)-a;float u=Vector3.Dot(offset,h)/det;if(u<-.00001f||u>1.00001f)continue;var q=Vector3.Cross(offset,e1);float v=q.x/det;if(v<-.00001f||u+v>1.00001f)continue;float t=Vector3.Dot(e2,q)/det;float x=-mesh.bounds.extents.x*3-1+t;if(x>=-.0001f)hits.Add(Mathf.Max(0,x));}
            hits.Sort();var unique=new List<float>();foreach(float h in hits)if(unique.Count==0||Mathf.Abs(h-unique[unique.Count-1])>.002f)unique.Add(h);
            var rings=new List<Ring>();int start=0;if(unique.Count%2==1){rings.Add(new Ring{inner=0,outer=unique[0]});start=1;}for(int i=start;i+1<unique.Count;i+=2)rings.Add(new Ring{inner=unique[i],outer=unique[i+1]});return rings;
        }
        static List<VoxelCell> CylinderCells(Mesh mesh,float cell,int cap)
        {
            var output=new Dictionary<Vector3Int,VoxelCell>();int z0=Mathf.CeilToInt(mesh.bounds.min.z/cell),z1=Mathf.FloorToInt(mesh.bounds.max.z/cell);
            for(int z=z0;z<=z1;z++)foreach(var ring in Section(mesh,z*cell)){
                int outer=Mathf.Max(1,Mathf.RoundToInt(ring.outer/cell-.5f)),outerDiagonal=Mathf.RoundToInt(Mathf.Sqrt(2)*(outer+.5f));
                int inner=ring.inner>cell*.65f?Mathf.Max(0,Mathf.RoundToInt(ring.inner/cell-.5f)):-1,innerDiagonal=inner>=0?Mathf.RoundToInt(Mathf.Sqrt(2)*(inner+.5f)):-1;
                for(int x=-outer;x<=outer;x++)for(int y=-outer;y<=outer;y++){
                    int diagonal=Mathf.Abs(x)+Mathf.Abs(y);if(diagonal>outerDiagonal)continue;
                    bool hole=inner>=0&&Mathf.Abs(x)<=inner&&Mathf.Abs(y)<=inner&&diagonal<innerDiagonal;if(hole)continue;
                    var key=new Vector3Int(x,y,z);var item=new VoxelCell{position=key,shape=BlockShape.Cube};
                    if(diagonal==outerDiagonal&&x!=0&&y!=0){item.shape=BlockShape.Wedge;item.quarterTurns=WedgeTurns(new Vector3(Mathf.Sign(x),Mathf.Sign(y),0));}
                    else if(inner>=0&&Mathf.Abs(x)<=inner&&Mathf.Abs(y)<=inner&&diagonal==innerDiagonal&&x!=0&&y!=0){item.shape=BlockShape.Wedge;item.quarterTurns=WedgeTurns(new Vector3(-Mathf.Sign(x),-Mathf.Sign(y),0));}
                    output[key]=item;if(output.Count>cap)throw new InvalidOperationException("Cylinder cell cap exceeded");
                }
            }
            if(output.Count==0)throw new InvalidOperationException("No closed source cylinder sections");return output.Values.OrderBy(c=>c.position.z).ThenBy(c=>c.position.y).ThenBy(c=>c.position.x).ToList();
        }
        static Vector3Int WedgeTurns(Vector3 normal){normal.Normalize();for(int x=0;x<4;x++)for(int y=0;y<4;y++)for(int z=0;z<4;z++){var turns=new Vector3Int(x,y,z);if(Vector3.Dot(Quaternion.Euler(x*90,y*90,z*90)*new Vector3(0,1,-1).normalized,normal)>.999f)return turns;}throw new Exception("No existing wedge orientation");}
        struct CutPlane{public Vector3 normal;public int limit;}
        public static List<VoxelCell> BuildFacetedRock(Bounds bounds,float cell,int cap,string name)
        {
            return IrregularVoxelRocks.BuildCentered(bounds,cell,cap,name);
        }
        public static void RebuildSampleRock()
        {
            if(jobs.Count!=0)throw new InvalidOperationException("Range115 voxel build is running");
            var source=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(SourceRoot+"/Assets/Asteroid_Rock_210.asset");
            var asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(Root+"/Samples/Assets/Asteroid_Rock_210.asset");
            if(!source||!asset)throw new InvalidOperationException("Build the sample first");
            jobs.Enqueue(()=>{
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(SourceRoot+"/Meshes/"+source.name+".asset");float cell=Mathf.Max(.5f,source.cellSize*.72f);int cap=Mathf.Min(VoxelAssetDefinition.MaximumCells,source.cells.Count*4);List<VoxelCell> cells=null;
                for(int attempt=0;attempt<16;attempt++)try{cells=BuildFacetedRock(mesh.bounds,cell,cap,source.name);break;}catch(InvalidOperationException){if(cell>=10f)throw;cell=Mathf.Min(10,cell*1.18f);}
                if(cells==null)throw new InvalidOperationException("No fitted rock cells");asset.cells=cells;asset.cellSize=cell;asset.hullModules=SceneVoxelModules.GetOrCreate();asset.massPerCell=source.massPerCell*source.cells.Count*Mathf.Pow(source.cellSize,3)/(cells.Count*Mathf.Pow(cell,3));
                asset.surfaces=new[]{NaturalRockRebuilder.MatteRockMaterial(source.name)};asset.baseColor=Color.white;asset.surfaceColors=new List<Color>{Color.white};
                var baked=VoxelAssemblyBaker.Bake(asset,Root+"/Samples/Meshes/"+asset.name+".asset");
                Status="Sample rock rebuilt: "+cells.Count+" cells, "+VoxelAssemblyBaker.TriangleCount(baked)+" triangles; "+string.Join(", ",cells.GroupBy(c=>c.shape).Select(g=>g.Key+"="+g.Count()));
                File.AppendAllText(Root+"/Samples/BuildReport.txt","\n"+Status+"; asymmetric low-frequency voxel boulder, source bounds proportions only; three overlapping eccentric masses; independent coarse facet offsets; no source surface triangles or high-frequency cell noise.\n");Debug.Log(Status);
            });Status="Queued natural sample rock rebuild";EditorApplication.update+=Tick;
        }
        static SpaceMapDefinition Clone(SpaceMapDefinition source)
        {
            var result=UnityEngine.Object.Instantiate(source);result.name=source.name.Replace("Range115","Range115Voxels");
            foreach(var item in result.instances){var original=item.asset;if(!replacements.TryGetValue(original,out var asset))continue;item.asset=asset;item.position+=Quaternion.Euler(item.eulerAngles)*(phases[original]*item.scale);}
            return result;
        }
        static void Finish()
        {
            if(sample){var gallery=ScriptableObject.CreateInstance<SpaceMapDefinition>();gallery.name="Range115 true voxel samples";gallery.enemySpawns.Clear();int i=0;foreach(var a in replacements.Values)gallery.instances.Add(new VoxelMapInstance{id="sample-"+i,label=a.name,asset=a,position=new Vector3((i++-1)*350,0,0)});AssetDatabase.CreateAsset(gallery,output+"/Layouts/SampleGallery.asset");}
            else foreach(var name in new[]{"Range115","ComponentGallery"}){var source=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(SourceRoot+"/Layouts/"+name+".asset");if(!source)throw new Exception("Missing source layout "+name);var clone=Clone(source);if(!clone.Validate(out string error))throw new Exception(error);AssetDatabase.CreateAsset(clone,output+"/Layouts/"+(name=="Range115"?"Range115Voxels":name)+".asset");if(name=="Range115")report.Add("Instances="+clone.instances.Count+"; weightedCells="+clone.instances.Sum(i=>(long)i.asset.cells.Count)+"; source spawn/transforms retained with local grid-phase compensation");}
            AssetDatabase.SaveAssets();File.WriteAllLines(output+"/BuildReport.txt",new[]{"TRUE BASIC SEAMLESS BLOCK ASSEMBLY. Independent scene module database; source ship decorative hull database untouched. Source mesh used only for measurement; baked mesh exclusively VoxelGeometry.Build. Whole reference assemblies and internal islands retained in one asset; no FBX outer shell. Mass recalibrated to original occupied-cell mass."}.Concat(report));Status="Complete: "+replacements.Count+" true voxel assets";Debug.Log(Status);
        }
    }
}



