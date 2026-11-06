using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;
// External read-only asset audit; only writes its JSON report outside Assets.
public static class UniformInventory
{
 [Serializable] public class Variant {
 public string map,guid,asset,category; public int instances,sourceCells;
 public float scale,cellSize,effectiveCellSize; public Vector3 finalBoundsSize;
 public double area,volume,sourceFullCellVolumeUpperEstimate;
 public long boxSolidCells,boxShellCells;public bool solidOver131072,shellOver131072;
 }
 [Serializable] public class MapReport {
 public string path;public int instances,links,sourceAssets,variants,solidOver131072,shellOver131072;
 public long solidCells,shellCells,weightedSolidCells,weightedShellCells;public Variant[] largest10;
 }
 [Serializable] public class SceneReport { public string path;public ScaleEntry[] nonUnitTransforms; }
 [Serializable] public class ScaleEntry { public string transformId,gameObjectId,parentId;public Vector3 scale; }
 [Serializable] public class Report {
 public string assumptions;public int variants,instances;public MapReport[] maps;public SceneReport[] scenes;
 public Variant[] largest10,solidOver131072,shellOver131072,allVariants;
 }
 static readonly CultureInfo C=CultureInfo.InvariantCulture;
 public static string Run() {
 var rows=new List<Variant>();var maps=new List<MapReport>();
 foreach(var path in new[]{"Assets/_Starframe/Maps/Range115Voxels/Layouts/Range115Voxels.asset","Assets/_Starframe/Maps/SaturnRingVoxels/Layouts/SaturnRingVoxels.asset"}) {
 var m=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(path);if(!m)throw new Exception("Missing "+path);
 var own=new List<Variant>();var bounds=new Dictionary<VoxelAssetDefinition,Bounds>();
 foreach(var g in m.instances.GroupBy(i=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(i.asset))+"@"+i.scale.ToString("R",C))) {
 var i=g.First();var a=i.asset;Bounds b;if(!bounds.TryGetValue(a,out b)){b=a.LocalBounds;bounds[a]=b;}
 var d=b.size*Math.Abs(i.scale);long x=Math.Max(1,(long)Math.Ceiling(d.x-0.00001)),y=Math.Max(1,(long)Math.Ceiling(d.y-0.00001)),z=Math.Max(1,(long)Math.Ceiling(d.z-0.00001));
 long solid=checked(x*y*z),shell=solid-Math.Max(0,x-2)*Math.Max(0,y-2)*Math.Max(0,z-2);double cell=a.cellSize*Math.Abs(i.scale);
 own.Add(new Variant{map=path,guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a)),asset=AssetDatabase.GetAssetPath(a),category=a.category,instances=g.Count(),sourceCells=a.cells.Count,scale=i.scale,cellSize=a.cellSize,effectiveCellSize=(float)cell,finalBoundsSize=d,area=2*((double)d.x*d.y+(double)d.x*d.z+(double)d.y*d.z),volume=(double)d.x*d.y*d.z,sourceFullCellVolumeUpperEstimate=a.cells.Count*cell*cell*cell,boxSolidCells=solid,boxShellCells=shell,solidOver131072=solid>131072,shellOver131072=shell>131072});
 }
 rows.AddRange(own);maps.Add(new MapReport{path=path,instances=m.instances.Count,links=m.assemblyLinks.Count,sourceAssets=bounds.Count,variants=own.Count,solidOver131072=own.Count(r=>r.solidOver131072),shellOver131072=own.Count(r=>r.shellOver131072),solidCells=own.Sum(r=>r.boxSolidCells),shellCells=own.Sum(r=>r.boxShellCells),weightedSolidCells=own.Sum(r=>r.boxSolidCells*r.instances),weightedShellCells=own.Sum(r=>r.boxShellCells*r.instances),largest10=own.OrderByDescending(r=>r.boxSolidCells).Take(10).ToArray()});
 }
 var scenes=new List<SceneReport>();foreach(var p in new[]{"Assets/_Starframe/Scenes/Testing/FlightRange.unity","Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity"}) {
 var entries=new List<ScaleEntry>();foreach(Match b in Regex.Matches(File.ReadAllText(p),@"(?ms)^--- !u!4 &(-?\d+)[^\r\n]*\r?\n(.*?)(?=^--- !u!|\z)")) {
 string body=b.Groups[2].Value;var s=Regex.Match(body,@"m_LocalScale: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}");if(!s.Success)continue;
 var v=new Vector3(float.Parse(s.Groups[1].Value,C),float.Parse(s.Groups[2].Value,C),float.Parse(s.Groups[3].Value,C));if((v-Vector3.one).sqrMagnitude<1e-10f)continue;
 entries.Add(new ScaleEntry{transformId=b.Groups[1].Value,gameObjectId=Regex.Match(body,@"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value,parentId=Regex.Match(body,@"m_Father: \{fileID: (\d+)\}").Groups[1].Value,scale=v});
 }scenes.Add(new SceneReport{path=p,nonUnitTransforms=entries.ToArray()});
 }
 var report=new Report{assumptions="Variant=(map,source GUID,exact scale). Bounds are occupancy bounds times instance scale, before rotation. Scene nonunit transform list is a serialized-text audit, not hierarchy-filtered. Bounds estimates exclude parent scales; resolve map parent transforms separately. 1-unit bounding-box solid/shell are conservative estimates, not actual topology. Source full-cell volume assumes cubes and overestimates slopes. No scene opened; no Assets modified.",variants=rows.Count,instances=maps.Sum(m=>m.instances),maps=maps.ToArray(),scenes=scenes.ToArray(),largest10=rows.OrderByDescending(r=>r.boxSolidCells).Take(10).ToArray(),solidOver131072=rows.Where(r=>r.solidOver131072).OrderByDescending(r=>r.boxSolidCells).ToArray(),shellOver131072=rows.Where(r=>r.shellOver131072).OrderByDescending(r=>r.boxShellCells).ToArray(),allVariants=rows.ToArray()};
 var jsonType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Newtonsoft.Json.JsonConvert")).First(t=>t!=null); string json=(string)jsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{new { report.variants, report.instances, maps=maps.Select(m=>new {m.path,m.instances,m.variants,m.shellCells,m.weightedShellCells,m.shellOver131072}), largest10=report.largest10.Select(v=>new {v.asset,v.scale,size=new[]{v.finalBoundsSize.x,v.finalBoundsSize.y,v.finalBoundsSize.z},v.boxShellCells})}});Directory.CreateDirectory("tools/Range115");File.WriteAllText("tools/Range115/uniform-inventory.json",json);return json;
 }
}


