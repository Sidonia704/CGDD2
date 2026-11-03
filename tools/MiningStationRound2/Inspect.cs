using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        public const string SourceRoot = "Assets/_Starframe/assetsForCodex/scenemodel/";
        public const string EvidenceRoot = "Documentation/MiningStation-Round2-2026-11-01/v04";
        [Serializable] public class SourcePart { public int id, vertexCount, triangleCount; public Vector3 center, size; public int[] indices; }
        [Serializable] public class SourceMesh { public string file, node; public Vector3[] vertices; public int[] triangles; public SourcePart[] parts; public Vector3 center, size; }
        [Serializable] public class SourceReport { public float targetStationLength=500, sourceStationLength, sourceToTargetScale; public Vector3 stationCenter; public List<SourceMesh> meshes=new List<SourceMesh>(); }
        static Bounds PointsBounds(IEnumerable<Vector3> pts) { bool first=true; Bounds b=default; foreach(var p in pts) { if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p); } return b; }
        // Field-only diagnostic serializer: Unity JsonUtility drops nested ephemeral run_script types;
        // Newtonsoft is duplicated by Localization in this project's loaded assemblies.
        public static string Json(object value,bool pretty=true){var b=new System.Text.StringBuilder();WriteJson(b,value);return b.ToString();}
        static void WriteJson(System.Text.StringBuilder b,object value)
        {
            if(value==null){b.Append("null");return;}
            if(value is string s){b.Append('"');foreach(char c in s){switch(c){case '"':b.Append("\\\"");break;case '\\':b.Append("\\\\");break;case '\n':b.Append("\\n");break;case '\r':b.Append("\\r");break;case '\t':b.Append("\\t");break;default:if(c<32)b.Append("\\u"+((int)c).ToString("x4"));else b.Append(c);break;}}b.Append('"');return;}
            if(value is bool flag){b.Append(flag?"true":"false");return;}
            var type=value.GetType();if(type.IsEnum){WriteJson(b,value.ToString());return;}
            if(type.IsPrimitive||value is decimal){b.Append(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture));return;}
            if(value is UnityEngine.Object obj){WriteJson(b,AssetDatabase.GetAssetPath(obj));return;}
            if(value is System.Collections.IDictionary dict){b.Append('{');bool comma=false;foreach(System.Collections.DictionaryEntry e in dict){if(comma)b.Append(',');comma=true;WriteJson(b,e.Key.ToString());b.Append(':');WriteJson(b,e.Value);}b.Append('}');return;}
            if(value is System.Collections.IEnumerable list){b.Append('[');bool comma=false;foreach(var e in list){if(comma)b.Append(',');comma=true;WriteJson(b,e);}b.Append(']');return;}
            b.Append('{');bool has=false;foreach(var f in type.GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance)){if(has)b.Append(',');has=true;WriteJson(b,f.Name);b.Append(':');WriteJson(b,f.GetValue(value));}b.Append('}');
        }
        public static SourceReport LoadSources()
        {
            var report=new SourceReport();
            var station=AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot+"mainstru.fbx");
            if(!station) throw new Exception("Missing station source");
            var filters=station.GetComponentsInChildren<MeshFilter>(true);
            var bounds=PointsBounds(filters.SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.localToWorldMatrix.MultiplyPoint3x4(v))));
            report.sourceStationLength=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z); report.sourceToTargetScale=500/report.sourceStationLength; report.stationCenter=bounds.center;
            foreach(var file in new[]{"mainstru.fbx","scatter.fbx","debri.fbx"})
            {
                var obj=AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot+file);
                foreach(var f in obj.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m=f.sharedMesh; var matrix=f.transform.localToWorldMatrix;
                    var verts=m.vertices.Select(v=>(matrix.MultiplyPoint3x4(v)-report.stationCenter)*report.sourceToTargetScale).ToArray();
                    var tri=m.triangles;var bb=PointsBounds(verts);
                    report.meshes.Add(new SourceMesh { file=file,node=f.name,vertices=verts,triangles=tri,center=bb.center,size=bb.size,parts=SplitSource(verts,tri) });
                }
            }
            return report;
        }
        static SourcePart[] SplitSource(Vector3[] verts,int[] tris)
        {
            var p=Enumerable.Range(0,verts.Length).ToArray();
            Func<int,int> find=null;find=i=>{while(p[i]!=i){p[i]=p[p[i]];i=p[i];}return i;};
            Action<int,int> join=(a,b)=>{a=find(a);b=find(b);if(a!=b)p[b]=a;};
            var welded=new Dictionary<Vector3Int,int>();
            for(int i=0;i<verts.Length;i++){var key=Vector3Int.RoundToInt(verts[i]*1000);if(welded.TryGetValue(key,out int other))join(i,other);else welded[key]=i;}
            for(int i=0;i<tris.Length;i+=3){join(tris[i],tris[i+1]);join(tris[i],tris[i+2]);}
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<tris.Length;i+=3){int k=find(tris[i]);if(!groups.TryGetValue(k,out var g))groups[k]=g=new List<int>();g.Add(tris[i]);g.Add(tris[i+1]);g.Add(tris[i+2]);}
            var parts=new List<SourcePart>();
            foreach(var group in groups.Values){var unique=group.Distinct().ToArray();var b=PointsBounds(unique.Select(i=>verts[i]));parts.Add(new SourcePart {vertexCount=unique.Length,triangleCount=group.Count/3,center=b.center,size=b.size,indices=group.ToArray()});}
            parts=parts.OrderByDescending(a=>a.triangleCount).ToList();for(int i=0;i<parts.Count;i++)parts[i].id=i;
            return parts.ToArray();
        }
        public static string Inspect()
        {
            var r=LoadSources();Directory.CreateDirectory("Temp/MiningRound2");File.WriteAllText("Temp/MiningRound2/Sources.json",Json(r,false));
            var summary=new List<string>{"sourceStationLength="+r.sourceStationLength+", targetScale="+r.sourceToTargetScale};
            foreach(var m in r.meshes){summary.Add(m.file+" / "+m.node+" / size="+m.size+" / parts="+m.parts.Length);foreach(var part in m.parts.Take(m.file=="mainstru.fbx"?35:2))summary.Add("  #"+part.id+" tri="+part.triangleCount+" center="+part.center+" size="+part.size);}
            File.WriteAllLines("Temp/MiningRound2/SourceSummary.txt",summary);return string.Join("\n",summary);
        }
    }
}
