// @role Instance persistent wreckage with cached source descriptors and island bounds while preserving each destructible part and collider.
// @calls DebrisWorld, DebrisCluster, DebrisPartHitbox

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.Combat
{
    [DisallowMultipleComponent]
    public sealed class DebrisInstanceRenderer:MonoBehaviour
    {
        const int Capacity=256;
        static readonly Unity.Profiling.ProfilerMarker DrawMarker=new Unity.Profiling.ProfilerMarker("Starframe.WreckInstances"),RebuildMarker=new Unity.Profiling.ProfilerMarker("Starframe.WreckInstanceRebuild");
        sealed class Entry
        {
            public DebrisPartHitbox part;public DebrisCluster cluster;public MeshRenderer source;public Matrix4x4 local;
            public (Mesh,Material,int,int,ShadowCastingMode,bool,uint) key;
        }
        sealed class Batch
        {
            public Mesh mesh;public int submesh;public RenderParams parameters;
            public readonly List<Entry> entries=new List<Entry>(Capacity);
            public readonly Matrix4x4[] matrices=new Matrix4x4[Capacity];
        }
        readonly List<Batch> batches=new List<Batch>();
        readonly List<MeshRenderer> hidden=new List<MeshRenderer>();
        readonly Dictionary<Material,Material> materials=new Dictionary<Material,Material>();
        readonly Dictionary<DebrisCluster,Matrix4x4> roots=new Dictionary<DebrisCluster,Matrix4x4>();
        readonly HashSet<DebrisCluster> visible=new HashSet<DebrisCluster>();
        readonly Plane[] planes=new Plane[6];
        readonly Dictionary<DebrisPartHitbox,List<Entry>> cachedParts=new Dictionary<DebrisPartHitbox,List<Entry>>();
        readonly HashSet<DebrisPartHitbox> visited=new HashSet<DebrisPartHitbox>();
        readonly List<DebrisPartHitbox> retired=new List<DebrisPartHitbox>();
        readonly Dictionary<(Mesh,Material,int,int,ShadowCastingMode,bool,uint),List<Entry>> groups=new Dictionary<(Mesh,Material,int,int,ShadowCastingMode,bool,uint),List<Entry>>();
        readonly HashSet<MeshRenderer> sources=new HashSet<MeshRenderer>();
        readonly List<Batch> allocatedBatches=new List<Batch>();
        readonly Dictionary<DebrisCluster,Bounds> clusterBounds=new Dictionary<DebrisCluster,Bounds>();
        DebrisWorld world;int revision=-1;bool dirty=true;
        public int BatchedRendererCount=>hidden.Count;
        public int SubmittedBatchCount{get;private set;}
        public int VisibleInstanceCount{get;private set;}
        public void Initialize(DebrisWorld value){Restore();cachedParts.Clear();world=value;dirty=true;revision=-1;}
        public void MarkDirty()=>dirty=true;
        void LateUpdate()
        {
            using var sample=DrawMarker.Auto();
            SubmittedBatchCount=VisibleInstanceCount=0;if(!world)return;
            if(dirty||revision!=world.TopologyRevision)Rebuild();
            roots.Clear();visible.Clear();clusterBounds.Clear();var camera=Camera.main;
            bool cull=camera&&camera.isActiveAndEnabled;if(cull)GeometryUtility.CalculateFrustumPlanes(camera,planes);
            foreach(var cluster in world.Clusters)
            {
                if(!cluster||!cluster.gameObject.activeInHierarchy)continue;
                var matrix=cluster.transform.localToWorldMatrix;roots[cluster]=matrix;
                var bounds=DebrisWorld.TransformBounds(cluster.localBounds,matrix);bounds.Expand(2);
                clusterBounds[cluster]=bounds;
                if(!cull||GeometryUtility.TestPlanesAABB(planes,bounds))visible.Add(cluster);
            }
            foreach(var batch in batches)
            {
                int count=0;Bounds bounds=default;DebrisCluster previousCluster=null;
                foreach(var entry in batch.entries)
                {
                    // Destruction/splitting increments TopologyRevision before LateUpdate. Rebuild already
                    // removed dead entries and updated island-local poses, so avoid native per-part queries here.
                    if(!visible.Contains(entry.cluster)||!roots.TryGetValue(entry.cluster,out var root))continue;
                    var matrix=root*entry.local;batch.matrices[count]=matrix;
                    if(entry.cluster!=previousCluster){var box=clusterBounds[entry.cluster];if(count==0)bounds=box;else bounds.Encapsulate(box);previousCluster=entry.cluster;}count++;
                }
                if(count==0)continue;
                batch.parameters.worldBounds=bounds;
                Graphics.RenderMeshInstanced(batch.parameters,batch.mesh,batch.submesh,batch.matrices,count);
                SubmittedBatchCount++;VisibleInstanceCount+=count;
            }
        }
        void Rebuild()
        {
            using var sample=RebuildMarker.Auto();
            batches.Clear();dirty=false;revision=world.TopologyRevision;
            if(!SystemInfo.supportsInstancing){Restore();return;}
            foreach(var entries in groups.Values)entries.Clear();
            visited.Clear();sources.Clear();int batchIndex=0;
            foreach(var cluster in world.Clusters)
            {
                if(!cluster)continue;var inverse=cluster.transform.worldToLocalMatrix;
                foreach(var part in cluster.Parts)
                {
                    if(!part||part.Part==null||!part.Part.present||!part.gameObject.activeInHierarchy)continue;
                    visited.Add(part);
                    if(!cachedParts.TryGetValue(part,out var descriptors))
                    {
                      descriptors=new List<Entry>();cachedParts.Add(part,descriptors);
                      foreach(var source in part.GetComponentsInChildren<MeshRenderer>(true))
                      {
                        if(!source.enabled||source.HasPropertyBlock())continue;
                        var filter=source.GetComponent<MeshFilter>();var mesh=filter?filter.sharedMesh:null;
                        var shared=source.sharedMaterials;if(!mesh||shared.Length!=mesh.subMeshCount)continue;
                        bool eligible=true;
                        foreach(var m in shared)
                            if(!m||!m.shader||!m.shader.name.StartsWith("Universal Render Pipeline/")||m.renderQueue>2500||m.IsKeywordEnabled("_EMISSION")||m.HasProperty("_EmissionColor")&&m.GetColor("_EmissionColor").maxColorComponent>.001f)eligible=false;
                        if(!eligible)continue;
                        for(int sub=0;sub<shared.Length;sub++)
                        {
                            var key=(mesh,shared[sub],sub,source.gameObject.layer,source.shadowCastingMode,source.receiveShadows,source.renderingLayerMask);
                            descriptors.Add(new Entry{part=part,cluster=cluster,source=source,local=inverse*source.transform.localToWorldMatrix,key=key});
                        }
                      }
                    }
                    foreach(var entry in descriptors){if(!entry.source)continue;if(entry.cluster!=cluster){entry.cluster=cluster;entry.local=inverse*entry.source.transform.localToWorldMatrix;}if(!groups.TryGetValue(entry.key,out var entries))groups.Add(entry.key,entries=new List<Entry>());entries.Add(entry);}
                }
            }
            retired.Clear();foreach(var part in cachedParts.Keys)if(!visited.Contains(part))retired.Add(part);foreach(var part in retired)cachedParts.Remove(part);retired.Clear();
            foreach(var group in groups)
            {
                if(group.Value.Count<2&&group.Key.Item1.subMeshCount==1)continue;
                var original=group.Key.Item2;
                if(!materials.TryGetValue(original,out var material))
                {material=new Material(original){name=original.name+" (wreck instances)",enableInstancing=true};materials.Add(original,material);}
                Batch batch=null;
                foreach(var entry in group.Value)
                {
                    if(batch==null||batch.entries.Count==Capacity)
                    {
                        var source=entry.source;
                        if(batchIndex==allocatedBatches.Count)allocatedBatches.Add(new Batch());batch=allocatedBatches[batchIndex++];batch.entries.Clear();batch.mesh=group.Key.Item1;batch.submesh=group.Key.Item3;batch.parameters=new RenderParams(material){layer=source.gameObject.layer,renderingLayerMask=source.renderingLayerMask,shadowCastingMode=source.shadowCastingMode,receiveShadows=source.receiveShadows,lightProbeUsage=source.lightProbeUsage,reflectionProbeUsage=source.reflectionProbeUsage,motionVectorMode=source.motionVectorGenerationMode};
                        batches.Add(batch);
                    }
                    batch.entries.Add(entry);sources.Add(entry.source);
                }
            }
            for(int i=batchIndex;i<allocatedBatches.Count;i++)allocatedBatches[i].entries.Clear();
            foreach(var source in hidden)if(source&&!sources.Contains(source))source.enabled=true;
            hidden.Clear();hidden.AddRange(sources);foreach(var source in hidden)if(source)source.enabled=false;
        }
        void Restore(){foreach(var source in hidden)if(source)source.enabled=true;hidden.Clear();}
        void OnEnable()=>dirty=true;
        void OnDisable(){Restore();SubmittedBatchCount=VisibleInstanceCount=0;dirty=true;}
        void OnDestroy()
        {
            Restore();foreach(var material in materials.Values)if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}materials.Clear();
        }
    }
}
