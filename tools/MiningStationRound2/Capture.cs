using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        struct DrawItem { public Mesh mesh;public Matrix4x4 matrix;public Color color; }
        static Mesh PreviewMesh(VoxelAssetDefinition asset)
        {
            var states=new Dictionary<Vector3Int,VoxelCellState>();
            foreach(var c in asset.cells)states[c.position]=new VoxelCellState{position=c.position,surface=c.surface,shape=c.shape,quarterTurns=c.quarterTurns,modelOffset=c.modelOffset};
            return VoxelGeometry.Build(states,asset.UsesHullModels?asset.SurfaceCount*VoxelHullAppearance.ShapeCount:asset.SurfaceCount,asset.cellSize,asset.hullModules);
        }
        static void RenderImage(List<DrawItem> items,Bounds bounds,string path,Vector3 direction,int width,int height)
        {
            var preview=new PreviewRenderUtility();var mats=new Dictionary<Color,Material>();Texture2D image=null;RenderTexture resized=null;RenderTexture previous=RenderTexture.active;
            try
            {
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.035f,.045f,.06f);preview.camera.orthographic=true;
                preview.camera.nearClipPlane=.1f;preview.camera.farClipPlane=100000;
                direction=direction.normalized;var rotation=Quaternion.LookRotation(-direction,Mathf.Abs(direction.y)>.99f?Vector3.forward:Vector3.up);
                float aspect=(float)width/height,extent=0;var inverse=Quaternion.Inverse(rotation);
                for(int i=0;i<8;i++){var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));var p=inverse*(corner-bounds.center);extent=Mathf.Max(extent,Mathf.Abs(p.y),Mathf.Abs(p.x)/aspect);}
                preview.camera.orthographicSize=Mathf.Max(.1f,extent*1.12f);preview.camera.transform.SetPositionAndRotation(bounds.center+direction*Mathf.Max(100,bounds.size.magnitude*2),rotation);
                preview.camera.aspect=aspect;preview.BeginPreview(new Rect(0,0,width,height),GUIStyle.none);
                foreach(var item in items)
                {
                    if(!mats.TryGetValue(item.color,out var material)){var shader=Shader.Find("Starframe/Tools/VoxelPreview");if(!shader)throw new Exception("Preview shader missing");material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};material.SetColor("_BaseColor",item.color);mats[item.color]=material;}
                    for(int sub=0;sub<item.mesh.subMeshCount;sub++)preview.DrawMesh(item.mesh,item.matrix,material,sub);
                }
                preview.Render(true);var rendered=preview.EndPreview();
                // BeginPreview allocates in physical pixels on high-DPI displays; read the WHOLE frame.
                resized=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);Graphics.Blit(rendered,resized);
                RenderTexture.active=resized;image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {RenderTexture.active=previous;if(resized)RenderTexture.ReleaseTemporary(resized);if(image)UnityEngine.Object.DestroyImmediate(image);preview.Cleanup();foreach(var m in mats.Values)UnityEngine.Object.DestroyImmediate(m);}
        }
        public static string Recapture()
        {
            var src=LoadSources();var report=new BuildReport();
            Action<SourceMesh,int[],string,bool,float> prepare=(source,parts,name,align,extra)=>
            {
                var indices=parts==null?source.triangles:parts.SelectMany(i=>source.parts[i].indices).ToArray();var unique=indices.Distinct().ToArray();var selected=unique.Select(i=>source.vertices[i]*extra).ToArray();
                var rotation=ComponentAxes(selected,align);var inverse=Quaternion.Inverse(rotation);var b=PointsBounds(selected.Select(p=>inverse*p));var center=rotation*b.center;
                var remap=new Dictionary<int,int>();for(int i=0;i<unique.Length;i++)remap[unique[i]]=i;
                var local=selected.Select(p=>inverse*(p-center)).ToArray();var triangles=indices.Select(i=>remap[i]).ToArray();if(name=="Station_OpenTruss")MiddleThird(ref local,ref triangles);
                var mesh=new Mesh{name=name+" source reference only",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.vertices=local;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();ComparisonSourceMeshes[name]=mesh;
                report.prototypes.Add(new PrototypeRecord{name=name,assetPath=OutputRoot+"/Assets/"+name+".asset"});
            };
            try
            {
                var main=src.meshes.First(m=>m.file=="mainstru.fbx");
                prepare(main,new[]{2},"Station_SphericalTank",false,1);prepare(main,new[]{12},"Station_CapsuleTank",true,1);prepare(main,new[]{31},"Station_OpenTruss",true,1);prepare(main,new[]{28},"Station_EquipmentCage",true,1);prepare(main,new[]{179},"Station_NotchedSlab",true,1);
                prepare(src.meshes.First(m=>m.node=="Breccia Rocks_Low.022"),null,"Rock_Ridged",true,1.8f);prepare(src.meshes.First(m=>m.node=="Procedural Brown Rocks.001"),null,"Rock_Notched",true,2);prepare(src.meshes.First(m=>m.node.StartsWith("asteroid 01.007")),null,"Rock_Faceted",true,2.2f);
                var debris=src.meshes.First(m=>m.file=="debri.fbx");var choices=debris.parts.Where(p=>Mathf.Max(p.size.x,p.size.y,p.size.z)>8).OrderByDescending(p=>p.triangleCount).ThenByDescending(p=>p.size.sqrMagnitude).Take(2).ToArray();
                for(int i=0;i<choices.Length;i++)prepare(debris,new[]{choices[i].id},"Debris_"+(i==0?"FracturedChunk":"AngledShard"),true,1);
                CaptureComparisons(report);CaptureSolidSections(report);
                var patch=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(OutputRoot+"/Layouts/LocalDensityPatch.asset");CaptureLayout(patch,EvidenceRoot+"/density-oblique.png",new Vector3(.8f,.7f,1),1300,850);CaptureLayout(patch,EvidenceRoot+"/density-top.png",new Vector3(.01f,1,.01f),1300,850);CaptureLayout(patch,EvidenceRoot+"/density-context.png",new Vector3(.8f,.7f,1),1500,900,true);
                var study=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(OutputRoot+"/Layouts/PartialStationStudy.asset");CaptureLayout(study,EvidenceRoot+"/partial-station-study.png",new Vector3(1,.8f,1),1300,850);
                return "Recaptured all ten prototypes from three views; source meshes remain temporary. No asset modifications.";
            }
            finally{foreach(var mesh in ComparisonSourceMeshes.Values)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);ComparisonSourceMeshes.Clear();}
        }
        static void CaptureComparisons(BuildReport report)
        {
            var directions=new[]{new Vector3(1,.7f,1),new Vector3(0,.05f,1),new Vector3(.01f,1,.01f)};
            foreach(var record in report.prototypes)
            {
                var asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(record.assetPath);var blockMesh=PreviewMesh(asset);
                try
                {
                    var sourceMesh=ComparisonSourceMeshes[record.name];var b=sourceMesh.bounds;b.Encapsulate(blockMesh.bounds);
                    for(int i=0;i<directions.Length;i++)
                    {
                        RenderImage(new List<DrawItem>{new DrawItem{mesh=sourceMesh,matrix=Matrix4x4.identity,color=new Color(.72f,.76f,.81f)}},b,EvidenceRoot+"/raw/"+record.name+"-source-"+i+".png",directions[i],650,450);
                        RenderImage(new List<DrawItem>{new DrawItem{mesh=blockMesh,matrix=Matrix4x4.identity,color=new Color(.72f,.76f,.81f)}},b,EvidenceRoot+"/raw/"+record.name+"-blocks-"+i+".png",directions[i],650,450);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(blockMesh);}
            }
        }
        static void CaptureSolidSections(BuildReport report)
        {
            foreach(var record in report.prototypes.Where(p=>p.name=="Station_SphericalTank"||p.name=="Station_CapsuleTank"||p.name=="Station_NotchedSlab"||p.name=="Rock_Notched"))
            {
                var source=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(record.assetPath);var section=ScriptableObject.CreateInstance<VoxelAssetDefinition>();Mesh mesh=null;
                try{section.hullModules=source.hullModules;section.cellSize=source.cellSize;section.cells=source.cells.Where(c=>c.position.x>=0).Select(c=>c.Copy()).ToList();mesh=PreviewMesh(section);RenderImage(new List<DrawItem>{new DrawItem{mesh=mesh,matrix=Matrix4x4.identity,color=new Color(.72f,.76f,.81f)}},source.LocalBounds,EvidenceRoot+"/raw/"+record.name+"-section.png",new Vector3(-1,.45f,.65f),800,600);}
                finally{if(mesh)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(section);}
            }
        }
        static void CaptureLayout(SpaceMapDefinition map,string path,Vector3 direction,int width,int height,bool stationEnvelope=false)
        {
            var meshes=new Dictionary<VoxelAssetDefinition,Mesh>();var items=new List<DrawItem>();bool first=true;Bounds bounds=default;Mesh wire=null;
            try
            {
                foreach(var instance in map.instances)
                {
                    if(!meshes.TryGetValue(instance.asset,out var mesh))meshes[instance.asset]=mesh=PreviewMesh(instance.asset);
                    var matrix=instance.Matrix;var b=instance.asset.LocalBounds;
                    for(int i=0;i<8;i++){var c=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(first){bounds=new Bounds(c,Vector3.zero);first=false;}else bounds.Encapsulate(c);}
                    items.Add(new DrawItem{mesh=mesh,matrix=matrix,color=new Color(.72f,.76f,.81f)});
                }
                if(stationEnvelope)
                {
                    var envelope=new Bounds(Vector3.zero,new Vector3(351.64f,202.69f,500));var vertices=new Vector3[8];var indices=new List<int>();
                    for(int i=0;i<8;i++){vertices[i]=Vector3.Scale(envelope.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));for(int bit=1;bit<=4;bit*=2)if((i&bit)==0){indices.Add(i);indices.Add(i|bit);}}
                    wire=new Mesh{name="Scale reference only - not station geometry"};wire.vertices=vertices;wire.normals=Enumerable.Repeat(Vector3.up,8).ToArray();wire.SetIndices(indices.ToArray(),MeshTopology.Lines,0);bounds.Encapsulate(envelope);items.Add(new DrawItem{mesh=wire,matrix=Matrix4x4.identity,color=new Color(.9f,.45f,.12f)});
                }
                RenderImage(items,bounds,path,direction,width,height);
            }
            finally{foreach(var m in meshes.Values)UnityEngine.Object.DestroyImmediate(m);if(wire)UnityEngine.Object.DestroyImmediate(wire);}
        }
    }
}
