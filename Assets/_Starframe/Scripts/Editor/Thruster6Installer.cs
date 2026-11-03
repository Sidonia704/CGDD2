using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    public static class Thruster6Installer
    {
        const string Root="Assets/_Starframe/";
        const string Source=Root+"assetsForCodex/thruster6.fbx";
        const string Model=Root+"Art/Models/Modules/Expansion/Thruster6.fbx";
        const string MeshPath=Root+"Art/Models/Construction/Expansion/Thruster6Mesh.asset";
        const string DefinitionPath=Root+"Settings/Modules/Thruster6.asset";
        const string Icons=Root+"Art/UI/Construction/Icons/Expansion/";
        public const string Evidence="Documentation/Thruster6-Weapons-2026-10-25/";
        static readonly Vector3[] Directions={Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};

        [MenuItem("Starframe/Components/Install five-way Thruster6")]
        public static void Install()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before importing components.");
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            if(!database)throw new InvalidOperationException("Module database is missing.");
            Directory.CreateDirectory(Path.GetDirectoryName(Model));Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));Directory.CreateDirectory(Icons);Directory.CreateDirectory(Evidence+"Before");
            Backup(database);
            File.Copy(Source,Model,true);AssetDatabase.ImportAsset(Model,ImportAssetOptions.ForceSynchronousImport);
            var sourceImporter=(ModelImporter)AssetImporter.GetAtPath(Source);
            var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
            importer.globalScale=sourceImporter.globalScale;importer.useFileScale=sourceImporter.useFileScale;importer.bakeAxisConversion=sourceImporter.bakeAxisConversion;
            importer.importNormals=ModelImporterNormals.Import;importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.isReadable=true;importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var mesh=Bake(model);
            var nozzles=FindNozzles(mesh);
            var definition=database.modules.FirstOrDefault(d=>d&&d.shape==BlockShape.Thruster6);
            if(!definition){definition=ScriptableObject.CreateInstance<ModuleDefinition>();AssetDatabase.CreateAsset(definition,DefinitionPath);database.modules.Add(definition);}
            definition.name="Thruster6";definition.shape=BlockShape.Thruster6;definition.displayName="五向微调推进器";definition.category=ModuleCategory.Propulsion;
            definition.cost=0;definition.massKg=400;definition.maxHp=80;definition.recoverySeconds=8;
            definition.thrustNewtons=45000;definition.thrusterPowerWatts=0;
            definition.occupiedCells=new[]{Vector3Int.zero};definition.localExhaustDirections=(Vector3[])Directions.Clone();definition.localNozzlePositions=nozzles;
            definition.mesh=Store(mesh,MeshPath);definition.outline=null;definition.drawOutline=false;
            definition.material=database.Get(BlockShape.Thruster1).material;definition.weapon=null;
            definition.icon=Icon("thruster6.png","Thruster6.png");definition.selectedIcon=Icon("thruster6s.png","Thruster6Selected.png");
            definition.generationWatts=definition.storageJoules=definition.shieldCapacity=definition.shieldPowerWatts=0;
            definition.shieldSurfaceMaterial=null;definition.shieldSurfaceMesh=null;
            database.modules=database.modules.Where(d=>d).GroupBy(d=>d.shape).Select(g=>g.First()).OrderBy(d=>(int)d.shape).ToList();
            EditorUtility.SetDirty(definition);EditorUtility.SetDirty(database);BlockModules.Configure(database);BlockModules.InvalidateSurfaces();
            AssetDatabase.SaveAssets();
            var text=new StringBuilder("# Thruster6 asset configuration\n\n");
            text.AppendLine("Single-cell five-way maneuvering thruster. The supplied source model has an authored nonuniform root scale and root rotation; those transforms are preserved before one final uniform scale. The mounting plate was identified on the source +X face, then rotated -90 degrees about Z to become local -Y.");
            text.AppendLine("\n- Mass: 400 kg; durability: 80 HP; recovery: 8 s.\n- Nominal force: 45,000 N per nozzle (Thruster1 is unchanged at 320,000 N).\n- Exhaust directions: +Y, +X, -X, +Z, -Z. Only -Y mounts to the ship.\n- Each exhaust reserves one adjacent cell; no extra clearance is reserved below the mounting plate.\n- Source files and their importer settings are untouched. Output FBX, mesh and icons are separate assets.\n");
            for(int i=0;i<Directions.Length;i++)text.AppendLine("- "+Directions[i].ToString("F0")+": nozzle "+nozzles[i].ToString("F5"));
            File.WriteAllText(Evidence+"Thruster6-Assets.md",text.ToString());
            File.WriteAllText(Evidence+"Thruster6-Module.json",JsonUtility.ToJson(definition,true));
            Debug.Log("Thruster6 installed: "+database.modules.Count+" modules, five measured nozzles at 45 kN each.");
        }
        static Mesh Bake(GameObject source)
        {
            // The FBX root scale is part of the authoring: removing it would stretch the cube.
            var authored=Matrix4x4.Rotate(Quaternion.Euler(0,0,-90))*
                Matrix4x4.TRS(Vector3.zero,source.transform.localRotation,source.transform.localScale);
            var combines=new List<CombineInstance>();
            foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true))
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                    combines.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=authored*source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
            var mesh=new Mesh{name="Thruster6FiveNozzleVisual",indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(combines.ToArray(),true,true);mesh.RecalculateBounds();
            var bounds=mesh.bounds;float uniform=.98f/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            var vertices=mesh.vertices;var centre=new Vector3(0,-.5f+bounds.extents.y*uniform,0);
            for(int i=0;i<vertices.Length;i++)vertices[i]=(vertices[i]-bounds.center)*uniform+centre;
            mesh.vertices=vertices;mesh.RecalculateBounds();if(mesh.uv.Length==mesh.vertexCount)mesh.RecalculateTangents();
            return mesh;
        }
        static Vector3[] FindNozzles(Mesh mesh)
        {
            var islands=Islands(mesh);
            try
            {
                var throats=islands.Where(m=>m.vertexCount>=600).ToArray();
                if(throats.Length!=5)throw new InvalidOperationException("Expected five disconnected detailed nozzle throats; found "+throats.Length+". Inspect the supplied mesh before changing the mapping.");
                var result=new Vector3[5];var chosen=new HashSet<Mesh>();
                for(int i=0;i<Directions.Length;i++)
                {
                    var direction=Directions[i];
                    var nozzle=throats.OrderByDescending(m=>Vector3.Dot(m.bounds.center,direction)).First();
                    if(!chosen.Add(nozzle))throw new InvalidOperationException("Nozzle mapping is ambiguous for "+direction);
                    var bounds=nozzle.bounds;
                    // Keep the measured mouth just inside the frame; rendering adds its own outward offset.
                    result[i]=bounds.center+Vector3.Scale(bounds.extents,direction)-direction*.012f;
                }
                return result;
            }
            finally{foreach(var island in islands)UnityEngine.Object.DestroyImmediate(island);}
        }
        static Mesh[] Islands(Mesh source)
        {
            var vertices=source.vertices;var triangles=source.triangles;var parent=Enumerable.Range(0,vertices.Length).ToArray();
            Func<int,int> find=null;find=i=>parent[i]==i?i:parent[i]=find(parent[i]);
            Action<int,int> union=(a,b)=>{a=find(a);b=find(b);if(a!=b)parent[a]=b;};
            var welded=new Dictionary<Vector3Int,int>();
            for(int i=0;i<vertices.Length;i++){var key=Vector3Int.RoundToInt(vertices[i]*10000);if(welded.TryGetValue(key,out int other))union(i,other);else welded.Add(key,i);}
            for(int i=0;i<triangles.Length;i+=3){union(triangles[i],triangles[i+1]);union(triangles[i],triangles[i+2]);}
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<triangles.Length;i+=3){int id=find(triangles[i]);if(!groups.TryGetValue(id,out var list)){list=new List<int>();groups.Add(id,list);}list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);}
            var result=new List<Mesh>();
            foreach(var list in groups.Values)
            {
                var indices=list.Distinct().ToArray();var remap=new Dictionary<int,int>();for(int i=0;i<indices.Length;i++)remap.Add(indices[i],i);
                var mesh=new Mesh();mesh.vertices=indices.Select(i=>vertices[i]).ToArray();mesh.triangles=list.Select(i=>remap[i]).ToArray();mesh.RecalculateBounds();result.Add(mesh);
            }
            return result.ToArray();
        }
        static Mesh Store(Mesh mesh,string path)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Sprite Icon(string source,string name)
        {
            var destination=Icons+name;File.Copy(Root+"assetsForCodex/"+source,destination,true);AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(destination);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=256;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(destination);
        }
        static void Backup(ModuleDatabase database)
        {
            var files=Directory.GetFiles(Root+"assetsForCodex","thruster6*").Where(p=>p.EndsWith(".fbx")||p.EndsWith(".png")).OrderBy(p=>p).ToArray();
            var hashes=new List<string>();
            foreach(var path in files){using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())hashes.Add(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","")+"  "+path);}
            string manifest=Evidence+"Before/SourceAssetHashes.sha256";if(!File.Exists(manifest))File.WriteAllLines(manifest,hashes);
            foreach(var d in database.modules.Where(d=>d&&d.shape!=BlockShape.Thruster6))
            {
                string path=AssetDatabase.GetAssetPath(d),output=Evidence+"Before/"+Path.GetFileName(path);
                if(!File.Exists(output))File.Copy(path,output);
            }
        }
    }
}
