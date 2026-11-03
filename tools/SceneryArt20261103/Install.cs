using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;
public static class SceneryArtInstall {
 const string Art="Assets/_Starframe/Art/SceneryArt/";
 const string Docs="Documentation/SceneryArt-2026-11-03/";
 [Serializable] public class Entry { public string path; public float[] color; public List<float[]> slots; public string[] surfaces; public long signature; public int cells; }
 [Serializable] public class Manifest {public List<Entry> entries=new List<Entry>();}
 static Task running;
 public static string Start(){if(File.Exists(Docs+"before-materials.json"))throw new Exception("Already installed; preserve the original manifest. Use Refine or Restore.");running=Run();return "Scenery art installation started";}
 static Material Make(string name,Color color,float smooth,float contrast,float macro,float scale=0.015f) {
  string path=Art+name+".mat";
  var m=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(!m){m=new Material(Shader.Find("Starframe/Environment/Scenery Surface"));AssetDatabase.CreateAsset(m,path);}
  m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smooth);m.SetFloat("_ContrastReduction",contrast);
  m.SetFloat("_MacroVariation",macro);m.SetFloat("_MacroScale",scale);m.enableInstancing=true;
  EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return m;
 }
 static uint Hash(string s){uint h=2166136261;foreach(char c in s){h^=c;h*=16777619;}return h;}
 static async Task Run(){
 try {
 Directory.CreateDirectory(Art);Directory.CreateDirectory(Docs);
 var manifest=new Manifest();
 var rocks=new[]{Make("RockWarmGrey",new Color(.63f,.60f,.55f),.21f,.30f,.10f),
 Make("RockStoneGrey",new Color(.66f,.64f,.60f),.24f,.30f,.09f),
 Make("RockCoolGrey",new Color(.60f,.63f,.65f),.22f,.30f,.08f),
 Make("RockIce",new Color(.78f,.80f,.80f),.31f,.24f,.06f)};
 var shell=Make("StationIvory",new Color(.79f,.77f,.71f),.42f,.10f,0);
 var frame=Make("StationFrame",new Color(.34f,.37f,.39f),.29f,.18f,0);
 var equipment=Make("StationEquipment",new Color(.58f,.60f,.59f),.35f,.16f,0);
 var backdrop=Make("OuterBelt",new Color(.62f,.61f,.59f),.20f,.65f,.07f,1.2f);
 backdrop.SetFloat("_HazeStart",2500);backdrop.SetFloat("_HazeEnd",16000);backdrop.SetFloat("_HazeStrength",.8f);
 var old=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/Materials/Environment/RangeAsteroidBackdrop.mat");
 if(!File.Exists(Docs+"Before/RangeAsteroidBackdrop.mat.txt")) File.Copy(AssetDatabase.GetAssetPath(old),Docs+"Before/RangeAsteroidBackdrop.mat.txt");
 old.shader=backdrop.shader;old.CopyPropertiesFromMaterial(backdrop);old.enableInstancing=true;
 old.SetFloat("_NearFadeStart",80);old.SetFloat("_NearFadeEnd",350);old.SetFloat("_FarFadeStart",17000);old.SetFloat("_FarFadeEnd",25000);
 EditorUtility.SetDirty(old);AssetDatabase.SaveAssetIfDirty(old);
 int count=0;var counts=new Dictionary<string,int>();
 foreach(var guid in AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{"Assets/_Starframe/Maps/ReferenceMiningStation/Assets"})){
 var p=AssetDatabase.GUIDToAssetPath(guid);var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p);
 if(a.category!="Reference rock"&&a.category!="Reference structure")continue;
 manifest.entries.Add(new Entry{path=p,color=new[]{a.baseColor.r,a.baseColor.g,a.baseColor.b,a.baseColor.a},slots=a.surfaceColors.Select(c=>new[]{c.r,c.g,c.b,c.a}).ToList(),surfaces=a.surfaces.Select(x=>AssetDatabase.GetAssetPath(x)).ToArray(),signature=a.initialLodSignature,cells=a.cells.Count});
 Material m;
 if(a.category=="Reference rock"){uint h=Hash(a.name); m=rocks[h%17==0?3:(int)(h%3)];}
 else {string n=a.name.ToLowerInvariant();m=n.Contains("truss")?frame:n.Contains("debris")||n.Contains("scatter")||n.Contains("solar")||n.Contains("panel")||n.Contains("pipe")?equipment:shell;}
 a.baseColor=Color.white;
 for(int i=0;i<a.surfaceColors.Count;i++)a.surfaceColors[i]=Color.white;
 a.surfaces=Enumerable.Repeat(m,a.SurfaceCount).ToArray();
 EditorUtility.SetDirty(a);AssetDatabase.SaveAssetIfDirty(a);
 string key=m.name;if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;
 if(++count%64==0){File.WriteAllText(Docs+"progress.txt","Applied "+count);await Task.Yield();}
 }
 File.WriteAllText(Docs+"before-materials.json",Serialize(manifest));
 AssetDatabase.SaveAssets();
 File.WriteAllText(Docs+"progress.txt","DONE "+count+"\n"+string.Join("\n",counts.Select(x=>x.Key+"="+x.Value)));
 } catch(Exception e){File.WriteAllText(Docs+"progress.txt","FAILED "+e);throw;}
 }
 public static string Restore(){
 var manifest=Deserialize(File.ReadAllText(Docs+"before-materials.json"));
 foreach(var e in manifest.entries){var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(e.path);a.baseColor=new Color(e.color[0],e.color[1],e.color[2],e.color[3]);a.surfaceColors=e.slots.Select(c=>new Color(c[0],c[1],c[2],c[3])).ToList();a.surfaces=e.surfaces.Select(p=>AssetDatabase.LoadAssetAtPath<Material>(p)).ToArray();EditorUtility.SetDirty(a);AssetDatabase.SaveAssetIfDirty(a);}
 File.Copy(Docs+"Before/RangeAsteroidBackdrop.mat.txt","Assets/_Starframe/Art/Materials/Environment/RangeAsteroidBackdrop.mat",true);AssetDatabase.Refresh();return "Restored map colour/surface bindings and backdrop material";
 }

 public static string Audit(){
 var manifest=Deserialize(File.ReadAllText(Docs+"before-materials.json"));
 var signature=typeof(VoxelAssetDefinition).Assembly.GetType("Starframe.Maps.SpaceMapVisuals").GetMethod("ComputeGeometrySignature",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
 if(manifest.entries.Count!=2345)throw new Exception("Incomplete manifest"); int checkedCount=0,bindings=0;var counts=new Dictionary<string,int>();
 foreach(var e in manifest.entries){
 var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(e.path);
 if(a.cells.Count!=e.cells || a.initialLodSignature!=e.signature || (long)signature.Invoke(null,new object[]{a})!=e.signature)throw new Exception("Geometry/LOD changed: "+e.path);
 var owned=new List<Material>();var mats=VoxelHullAppearance.Materials(a,owned);
 if(owned.Count!=0 || mats.Length!=a.initialLodMesh.subMeshCount || mats.Any(m=>m!=a.surfaces[0]))throw new Exception("Palette mismatch: "+e.path);
 if(UnityEditor.ShaderUtil.ShaderHasError(mats[0].shader))throw new Exception("Shader error");
 string key=mats[0].name;if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;
 checkedCount++;bindings+=mats.Length;
 }
 string result="PASS authored geometry and initial LOD signatures and shared exact/coarse material slots: "+checkedCount+" assets / "+bindings+" slots\n"+string.Join("\n",counts.Select(x=>x.Key+"="+x.Value));
 File.WriteAllText(Docs+"validation.txt",result);return result;
 }


 public static string Refine(){running=RefineAsync();return "Refining structural debris";}
 static async Task RefineAsync(){
 var m=AssetDatabase.LoadAssetAtPath<Material>(Art+"StationEquipment.mat"); int n=0;
 foreach(var guid in AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{"Assets/_Starframe/Maps/ReferenceMiningStation/Assets"})){
 var p=AssetDatabase.GUIDToAssetPath(guid);var name=Path.GetFileNameWithoutExtension(p);
 if(!name.StartsWith("debris")&&!name.StartsWith("scatter"))continue;
 var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p);
 if(a.category!="Reference structure")continue;
 a.surfaces=Enumerable.Repeat(m,a.SurfaceCount).ToArray();EditorUtility.SetDirty(a);AssetDatabase.SaveAssetIfDirty(a);
 if(++n%64==0){File.WriteAllText(Docs+"refine.txt","Applied "+n);await Task.Yield();}
 }
 File.WriteAllText(Docs+"refine.txt","DONE "+n);
 }


 public static string ReconstructBaseline(){
 var m=new Manifest();
 foreach(var guid in AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{"Assets/_Starframe/Maps/ReferenceMiningStation/Assets"})){
 var p=AssetDatabase.GUIDToAssetPath(guid);var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p);
 if(a.category!="Reference rock"&&a.category!="Reference structure")continue;
 m.entries.Add(new Entry{path=p,color=new float[]{1,1,1,1},slots=new List<float[]>{new float[]{1,1,1,1}},surfaces=new string[0],signature=a.initialLodSignature,cells=a.cells.Count});
 }
 File.WriteAllText(Docs+"before-materials.json",Serialize(m));
 File.WriteAllText(Docs+"baseline-provenance.txt","Reconstructed white baseline, not a complete pre-edit snapshot. The ephemeral JsonUtility serializer wrote {}. Colour fields are rebuilt from VoxelAssetDefinition defaults, importer conventions, sampled pre-edit headers and existing pre-edit screenshot. All baseColour/slot values are white and surfaces empty in the reconstructed baseline. Geometry signatures/cell counts are from the still-authored assets, checked against their baked LOD signatures; no pre-edit independent full geometry snapshot was captured. Backdrop material and rendering source backups are exact pre-edit copies.");
 return "Reconstructed "+m.entries.Count;
 }


 static Type Json=>Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json",true);
 static string Serialize(object value)=>(string)Json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
 static Manifest Deserialize(string value)=>(Manifest)Json.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{value,typeof(Manifest)});

}