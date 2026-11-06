using System.Linq;
using UnityEngine;
using UnityEditor;
public static class InspectVlsModels
{
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    public static object Run(){var rows=new[]{"VLS1","VLS2"}.Select(name=>{
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Starframe/assetsForCodex/116/"+name+".fbx");
        return new{name,root=V(asset.transform.localScale),parts=asset.GetComponentsInChildren<MeshFilter>(true).Select(f=>new{f.name,size=V(f.sharedMesh.bounds.size),center=V(f.sharedMesh.bounds.center),scale=V(f.transform.lossyScale),position=V(f.transform.position),rotation=V(f.transform.rotation.eulerAngles)}).ToArray()};}).ToArray();
        var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        var str=(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{rows});
        System.IO.File.WriteAllText("Temp/vls-models.json",str);return str;}
}
