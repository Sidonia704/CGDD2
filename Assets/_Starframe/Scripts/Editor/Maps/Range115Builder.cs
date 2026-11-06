using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.EditorTools
{
    // Independent, source-preserving import. Visuals are continuous; cells only own collision/editing.
    public static class Range115Builder
    {
        public const string Root = "Assets/_Starframe/Maps/Range115";
        public static string Status { get; private set; } = "Idle";
        static readonly Queue<Action> jobs = new Queue<Action>();
        sealed class Part { public string name; public Vector3[] vertices, normals; public int[] triangles; public Vector3 center, size; public Quaternion axes; public VoxelAssetDefinition asset; }
        static readonly List<Part> refs = new List<Part>();
        static SpaceMapDefinition map, gallery;
        static Vector3 origin, verticalOffset;
        static float globalScale;
        static int variants;
        static readonly List<string> report = new List<string>();
        // Legacy reference preparation; the editor menu now builds real blocks via Range115VoxelBuilder.
        public static void BeginBuild()
        {
            if(jobs.Count>0) throw new InvalidOperationException("Range115 build already running");
            if(AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(Root+"/Layouts/Range115.asset")) throw new InvalidOperationException("Range115 already exists; preserved without overwrite");
            refs.Clear(); report.Clear(); variants=0;
            Folder(Root+"/Assets"); Folder(Root+"/Meshes"); Folder(Root+"/Layouts");
            var source = Load("115.fbx"); var reference=Load("115reference.fbx");
            var bounds=BoundsOf(source.SelectMany(p=>p.vertices));
            globalScale=3000/Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z)); origin=bounds.center;
            verticalOffset=new Vector3(0,-bounds.size.y*globalScale*.5f-100,0);
            map=ScriptableObject.CreateInstance<SpaceMapDefinition>(); map.name="Range115";map.size=Vector3.one*3400; map.playerSpawn=new Vector3(0,160,-450);map.playerEulerAngles=new Vector3(25,0,0);map.enemySpawns.Clear();
            gallery=ScriptableObject.CreateInstance<SpaceMapDefinition>();gallery.name="Range115 Component Gallery";gallery.enemySpawns.Clear();gallery.size=Vector3.one*3400;
            foreach(var item in reference){ var part=Canonical(item); refs.Add(part); jobs.Enqueue(()=>Bake(part,"Facility",false)); }
            foreach(var item in source.Where(p=>p.name!="Cube.4509")){var copy=item;jobs.Enqueue(()=>Facility(copy));}
            var scatter=source.FirstOrDefault(p=>p.name=="Cube.4509");
            if(scatter==null)throw new Exception("Expected Cube.4509 asteroid scatter mesh");
            var rocks=Islands(scatter); var prototypes=rocks.OrderByDescending(p=>p.triangles.Length).Take(8).Select(Canonical).ToList();
            foreach(var proto in prototypes){var p=proto;jobs.Enqueue(()=>Bake(p,"Asteroid",true));}
            jobs.Enqueue(()=> {int i=0;foreach(var rock in rocks){var best=prototypes.OrderBy(p=>ShapeError(p.size,rock.size)).First();float scale=Max(rock.size)/Max(best.size);map.instances.Add(new VoxelMapInstance{id="115-rock-"+i++,label="Asteroid "+i,asset=best.asset,position=Position(rock.center),eulerAngles=rock.axes.eulerAngles,scale=Mathf.Clamp(scale,.25f,100)});}report.Add("Asteroid islands: "+rocks.Count+"; shared prototypes: "+prototypes.Count);});
            jobs.Enqueue(Finish); Status="Queued "+jobs.Count+" steps"; EditorApplication.update+=Tick;
        }
        static void Tick(){if(jobs.Count==0){EditorApplication.update-=Tick;return;}try{Status="Building; "+jobs.Count+" steps remaining";jobs.Dequeue()();}catch(Exception e){jobs.Clear();EditorApplication.update-=Tick;Status="FAILED: "+e;Debug.LogException(e);}}
        static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;int i=path.LastIndexOf('/');Folder(path.Substring(0,i));AssetDatabase.CreateFolder(path.Substring(0,i),path.Substring(i+1));}
        static float Max(Vector3 v)=>Mathf.Max(v.x,Mathf.Max(v.y,v.z));
        static Bounds BoundsOf(IEnumerable<Vector3> vertices){var a=vertices.ToArray();var b=new Bounds(a[0],Vector3.zero);foreach(var v in a)b.Encapsulate(v);return b;}
        static List<Part> Load(string file){var obj=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Starframe/assetsForCodex/115/"+file);if(!obj)throw new Exception("Missing "+file);return obj.GetComponentsInChildren<MeshFilter>(true).Select(f=>new Part{name=f.name,vertices=f.sharedMesh.vertices.Select(v=>f.transform.localToWorldMatrix.MultiplyPoint3x4(v)).ToArray(),triangles=f.sharedMesh.triangles,normals=f.sharedMesh.normals.Select(n=>f.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(n).normalized).ToArray()}).ToList();}
        static Vector3 Power(float[,] c,Vector3 v,Vector3 exclude){for(int i=0;i<64;i++){var n=new Vector3(c[0,0]*v.x+c[0,1]*v.y+c[0,2]*v.z,c[1,0]*v.x+c[1,1]*v.y+c[1,2]*v.z,c[2,0]*v.x+c[2,1]*v.y+c[2,2]*v.z);n-=exclude*Vector3.Dot(n,exclude);if(n.sqrMagnitude<1e-15f)break;v=n.normalized;}return v;}
        static Part Canonical(Part input){var vertices=input.vertices;var mean=vertices.Aggregate(Vector3.zero,(a,b)=>a+b)/vertices.Length;var c=new float[3,3];foreach(var v in vertices){var d=v-mean;for(int a=0;a<3;a++)for(int b=0;b<3;b++)c[a,b]+=d[a]*d[b];}var x=Power(c,new Vector3(.91f,.31f,.27f),Vector3.zero);if(x.x<0)x=-x;var y=Power(c,new Vector3(.23f,.93f,.16f),x);var z=Vector3.Cross(x,y).normalized;y=Vector3.Cross(z,x).normalized;var q=Quaternion.LookRotation(z,y);if(!input.name.StartsWith("Rock_"))q=SurfaceAxes(input,q);var inv=Quaternion.Inverse(q);var bnd=BoundsOf(vertices.Select(v=>inv*v));var center=q*bnd.center;return new Part{name=input.name,vertices=vertices.Select(v=>inv*(v-center)*globalScale).ToArray(),normals=input.normals?.Select(n=>inv*n).ToArray(),triangles=input.triangles,center=center,size=bnd.size*globalScale,axes=q};}
        static string Safe(string name)=>new string(name.Select(c=>char.IsLetterOrDigit(c)||c=='_'?c:'_').ToArray());
        static void Bake(Part p,string category,bool fill)
        {
            var asset=ScriptableObject.CreateInstance<VoxelAssetDefinition>();asset.name=category+"_"+Safe(p.name);asset.category="115 · "+category;asset.hullModules=SpaceMapStudioContent.HullDatabase;asset.baseColor=Color.white;asset.surfaceColors=new List<Color>{category=="Asteroid"?new Color(.47f,.49f,.52f):new Color(.74f,.77f,.8f)};
            float cell=Mathf.Clamp(Max(p.size)/48,.5f,10);for(int attempt=0;;attempt++){try{asset.cells=ContinuousMapSampling.Voxelize(p.vertices,p.triangles,cell,fill,20000);break;}catch(InvalidOperationException){if(attempt>=8||cell>=10)throw;cell=Mathf.Min(10,cell*1.4f);}}
            asset.cellSize=cell;
            var mesh=new Mesh{name=asset.name+" continuous surface",indexFormat=IndexFormat.UInt32};mesh.vertices=p.vertices;mesh.subMeshCount=8;mesh.SetTriangles(p.triangles,0);for(int s=1;s<8;s++)mesh.SetTriangles(Array.Empty<int>(),s);if(p.normals!=null&&p.normals.Length==p.vertices.Length)mesh.normals=p.normals;else mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Root+"/Meshes/"+asset.name+".asset");asset.initialLodMesh=mesh;asset.initialLodSignature=SpaceMapRuntime.ComputeGeometrySignature(asset);
            if(!asset.Validate(out string error))throw new Exception(asset.name+": "+error);AssetDatabase.CreateAsset(asset,Root+"/Assets/"+asset.name+".asset");p.asset=asset;
            int index=gallery.instances.Count;gallery.instances.Add(new VoxelMapInstance{id="115-gallery-"+index,label=asset.name,asset=asset,position=new Vector3((index%5-2)*350,0,(index/5)*350)});report.Add(asset.name+": triangles="+p.triangles.Length/3+", cells="+asset.cells.Count+", cellSize="+cell+"; complete mesh-object assembly");
        }
        static float ShapeError(Vector3 a,Vector3 b){a/=Max(a);b/=Max(b);return(a-b).sqrMagnitude;}
        static Vector3 Position(Vector3 p)=>(p-origin)*globalScale+verticalOffset;
        static void Facility(Part input)
        {
            var p=Canonical(input);Part best=null;Quaternion correction=Quaternion.identity;float bestError=float.MaxValue,bestScale=1;
            foreach(var candidate in refs.Where(r=>r.triangles.Length==p.triangles.Length))foreach(var signs in new[]{Quaternion.identity,Quaternion.Euler(180,0,0),Quaternion.Euler(0,180,0),Quaternion.Euler(0,0,180)}){float scale=Max(p.size)/Max(candidate.size);float error=0;int step=Mathf.Max(1,p.vertices.Length/160);for(int i=0;i<p.vertices.Length;i+=step){var v=p.vertices[i];float nearest=float.MaxValue;foreach(var cv in candidate.vertices)nearest=Mathf.Min(nearest,(v-signs*cv*scale).sqrMagnitude);error=Mathf.Max(error,Mathf.Sqrt(nearest));}if(error<bestError){bestError=error;best=candidate;correction=signs;bestScale=scale;}}
            // Strict sampled vertex residual: preserve an explicit whole-object variant if no rigid match is established.
            if(best==null||bestError>Mathf.Max(.15f,Max(p.size)*.001f)||bestScale<.25f||bestScale>100){p.name="Variant_"+p.name;Bake(p,"Facility",false);best=p;correction=Quaternion.identity;bestScale=1;variants++;}
            map.instances.Add(new VoxelMapInstance{id="115-facility-"+map.instances.Count,label=input.name,asset=best.asset,position=Position(p.center),eulerAngles=(p.axes*correction).eulerAngles,scale=bestScale});report.Add(input.name+" -> "+best.asset.name+"; sampled vertex residual="+bestError);
        }
        static List<Part> Islands(Part source)
        {
            var parent=Enumerable.Range(0,source.vertices.Length).ToArray();Func<int,int> find=null;find=i=>{while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;};Action<int,int> join=(a,b)=>{a=find(a);b=find(b);if(a!=b)parent[b]=a;};var weld=new Dictionary<Vector3Int,int>();for(int i=0;i<parent.Length;i++){var key=Vector3Int.RoundToInt(source.vertices[i]*100000);if(weld.TryGetValue(key,out int other))join(i,other);else weld[key]=i;}for(int i=0;i<source.triangles.Length;i+=3){join(source.triangles[i],source.triangles[i+1]);join(source.triangles[i],source.triangles[i+2]);}var groups=new Dictionary<int,List<int>>();for(int i=0;i<source.triangles.Length;i+=3){int k=find(source.triangles[i]);if(!groups.TryGetValue(k,out var g))groups[k]=g=new List<int>();g.AddRange(new[]{source.triangles[i],source.triangles[i+1],source.triangles[i+2]});}var result=new List<Part>();foreach(var group in groups.Values){var indices=group.Distinct().ToArray();var lookup=new Dictionary<int,int>();for(int i=0;i<indices.Length;i++)lookup[indices[i]]=i;var raw=new Part{name="Rock_"+result.Count,vertices=indices.Select(i=>source.vertices[i]).ToArray(),triangles=group.Select(i=>lookup[i]).ToArray(),normals=source.normals?.Length==source.vertices.Length?indices.Select(i=>source.normals[i]).ToArray():null};var p=Canonical(raw); // store world geometry plus canonical metadata for placement
            raw.center=p.center;raw.size=p.size;raw.axes=p.axes;result.Add(raw);}return result;
        }
        static Quaternion SurfaceAxes(Part p, Quaternion fallback)
        {
            var clusters=new Dictionary<Vector3Int,Vector3>();
            for(int i=0;i<p.triangles.Length;i+=3){var cross=Vector3.Cross(p.vertices[p.triangles[i+1]]-p.vertices[p.triangles[i]],p.vertices[p.triangles[i+2]]-p.vertices[p.triangles[i]]);float area=cross.magnitude;if(area<1e-10f)continue;var n=cross/area;int axis=Mathf.Abs(n.x)>Mathf.Abs(n.y)?0:1;if(Mathf.Abs(n.z)>Mathf.Abs(n[axis]))axis=2;if(n[axis]<0)n=-n;var key=Vector3Int.RoundToInt(n*100);clusters.TryGetValue(key,out var sum);clusters[key]=sum+n*area;}
            var normals=clusters.Values.OrderByDescending(v=>v.magnitude).ToArray();if(normals.Length<2)return fallback;var y=normals[0].normalized;var second=normals.FirstOrDefault(v=>Mathf.Abs(Vector3.Dot(v.normalized,y))<.1f);if(second.sqrMagnitude<1e-12f)return fallback;var x=(second-y*Vector3.Dot(second,y)).normalized;var z=Vector3.Cross(x,y).normalized;return Quaternion.LookRotation(z,y);
        }
        public static string Audit()
        {
            var loaded=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(Root+"/Layouts/Range115.asset");if(!loaded)return "Not built: "+Status;
            var errors=new List<string>();if(!loaded.Validate(out string message))errors.Add(message);
            var assets=AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{Root+"/Assets"}).Select(g=>AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            foreach(var a in assets){if(a.cells.Count==0||a.cells.Count>20000)errors.Add(a.name+" invalid collision count");if(!a.initialLodMesh||a.initialLodMesh.subMeshCount!=8||a.initialLodSignature!=SpaceMapRuntime.ComputeGeometrySignature(a))errors.Add(a.name+" invalid continuous mesh/signature");if(a.initialLodMesh&&a.initialLodMesh.normals.Length!=a.initialLodMesh.vertexCount)errors.Add(a.name+" missing normals");}
            int intact=0;foreach(var r in Load("115reference.fbx")){var a=assets.FirstOrDefault(v=>v.name=="Facility_"+Safe(r.name));if(!a||!a.initialLodMesh||a.initialLodMesh.GetTriangles(0).Length!=r.triangles.Length||a.initialLodMesh.vertexCount!=r.vertices.Length)errors.Add(r.name+" whole-object integrity failure");else intact++;}
            int facilities=loaded.instances.Count(i=>i.asset.category.Contains("Facility")),rocks=loaded.instances.Count-facilities;if(intact!=7||facilities!=23||rocks!=393)errors.Add("Unexpected reference or instance counts");
            string result="Reference meshes "+intact+"/7; facility instances "+facilities+"/23; asteroid instances "+rocks+"/393; assets "+assets.Length+"; collision maximum "+assets.Max(a=>a.cells.Count)+"; rotated instances "+loaded.instances.Count(i=>Quaternion.Angle(Quaternion.identity,Quaternion.Euler(i.eulerAngles))>.01f)+"; "+(errors.Count==0?"PASS":string.Join("; ",errors));File.WriteAllText(Root+"/Audit.txt",result);return result;
        }
        // Explicit collision-only optimization; authored continuous visual assets stay byte-for-byte intact.
        public static void RebuildAsteroidCollision()
        {
            if(jobs.Count>0)throw new InvalidOperationException("Range115 operation already running");
            var layout=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(Root+"/Layouts/Range115.asset");if(!layout)throw new InvalidOperationException("Build Range115 first");
            var rocks=layout.instances.Where(i=>i.asset&&i.asset.category.Contains("Asteroid")).Select(i=>i.asset).Distinct().ToArray();
            foreach(var rock in rocks){var asset=rock;jobs.Enqueue(()=>{
                var mesh=asset.initialLodMesh;if(!mesh)throw new InvalidOperationException(asset.name+" has no continuous source mesh");
                var vertices=mesh.vertices;var triangles=mesh.GetTriangles(0);float cell=Mathf.Clamp(Max(mesh.bounds.size)/28f,.5f,10f);List<VoxelCell> cells=null;
                for(int attempt=0;attempt<24;attempt++){
                    try{cells=ContinuousMapSampling.Voxelize(vertices,triangles,cell,true,cell>=10f?6000:2600);break;}
                    catch(InvalidOperationException){if(cell>=10f)throw;cell=Mathf.Min(10f,cell*1.2f);}
                }
                if(cells==null)throw new InvalidOperationException(asset.name+" collision optimization exhausted attempts");
                int previous=asset.cells.Count;asset.cells=cells;asset.cellSize=cell;asset.NotifyChanged();asset.initialLodSignature=SpaceMapRuntime.ComputeGeometrySignature(asset);EditorUtility.SetDirty(asset);
                Debug.Log(asset.name+" collision-only: "+previous+" -> "+cells.Count+" cells at "+cell+"m; continuous mesh retained");
            });}
            jobs.Enqueue(()=>{AssetDatabase.SaveAssets();long weighted=layout.instances.Sum(i=>(long)i.asset.cells.Count);Status="Asteroid collision optimization complete: "+weighted+" weighted cells";File.AppendAllText(Root+"/ImportReport.txt","\n"+Status+"; facilities and visual meshes unchanged.\n");Debug.Log(Status);});
            Status="Queued asteroid collision optimization";EditorApplication.update+=Tick;
        }
        static void Finish(){if(!map.Validate(out string error))throw new Exception(error);AssetDatabase.CreateAsset(map,Root+"/Layouts/Range115.asset");AssetDatabase.CreateAsset(gallery,Root+"/Layouts/ComponentGallery.asset");AssetDatabase.SaveAssets();report.Insert(0,"Range115 source extent scaled to3000m. Whole reference assemblies="+refs.Count+"; explicit unmatched facility variants="+variants+". Structures shifted below flight plane. Source-authored continuous render meshes; conservative collision cells.");File.WriteAllLines(Root+"/ImportReport.txt",report);AssetDatabase.Refresh();Status="Complete: "+map.instances.Count+" instances; "+gallery.instances.Count+" assets";Debug.Log(Status);}
    }
}




