// @role Create and simulate persistent, destructible detached ship wreckage.
// @calls CombatVfxPool, FlightTuning, DebrisCluster, DebrisPartHitbox

using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
namespace Starframe.Combat
{
    // A drifting island uses one rigidbody and complete-cell colliders, with no flight or weapon components.
    public sealed class DebrisWorld:MonoBehaviour
    {
        static readonly Vector3Int[] Directions={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,Vector3Int.forward,Vector3Int.back};
        readonly List<DebrisCluster> clusters=new List<DebrisCluster>();
        readonly HashSet<DebrisCluster> changed=new HashSet<DebrisCluster>();
        BattleTuning tuning;CombatVfxPool effects;
        public IReadOnlyList<DebrisCluster> Clusters=>clusters;
        public int PartCount{get{int count=0;foreach(var cluster in clusters)if(cluster)count+=cluster.PartCount;return count;}}
        public void Initialize(BattleTuning rules,CombatVfxPool visuals){tuning=rules;effects=visuals;}

        public void Spawn(IReadOnlyList<ShipPartState> detached,FlightTuning definitions,Vector3 velocity,Vector3 sourceCenter,FlightShipRuntime sourceShip=null)
        {
            var parts=new List<DebrisPartHitbox>(detached.Count);
            foreach(var source in detached){
                if(!source.view||source.hp<=0)continue;
                var filter=source.view.GetComponent<MeshFilter>();var renderer=source.view.GetComponent<MeshRenderer>();var collider=source.view.GetComponent<MeshCollider>();
                if(!filter||!renderer||!collider)continue;
                var go=new GameObject("Wreck_"+source.record.id,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider),typeof(DebrisPartHitbox));
                var pose=CylindricalHitQuery.PhysicsMatrix(collider);
                go.SetActive(false);go.layer=0;go.transform.SetParent(transform,false);CopyMatrix(pose,go.transform);
                go.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;CopyRenderer(renderer,go.GetComponent<MeshRenderer>());
                var hit=go.GetComponent<DebrisPartHitbox>();hit.Part=new ShipPartState{record=BlockPlacement.Copy(source.record),view=go,hp=source.hp,maxHp=source.maxHp,detached=true,thrustAvailable=false};
                hit.HitIdentity=source;hit.MassKg=Mathf.Max(1,definitions.Get(source.record.shape).massKg);
                var originalHitbox=source.view.GetComponent<ShipPartHitbox>();if(originalHitbox)originalHitbox.DetachedReplacement=hit;
                var physics=go.GetComponent<MeshCollider>();physics.sharedMesh=collider.sharedMesh;physics.convex=true;physics.sharedMaterial=collider.sharedMaterial;
                foreach(var child in source.view.GetComponentsInChildren<MeshFilter>(true)){
                    if(child==filter||child.name=="VoxelThrusterExhaust"||child.name=="ModuleGlowAccent")continue;
                    var visual=child.GetComponent<MeshRenderer>();if(!visual)continue;
                    var copy=new GameObject(child.name,typeof(MeshFilter),typeof(MeshRenderer));copy.transform.SetParent(go.transform,false);CopyMatrix(pose*source.view.transform.worldToLocalMatrix*child.transform.localToWorldMatrix,copy.transform);
                    copy.GetComponent<MeshFilter>().sharedMesh=child.sharedMesh;CopyRenderer(visual,copy.GetComponent<MeshRenderer>());
                }
                parts.Add(hit);
            }
            var sourceColliders=new List<Collider>();
            if(sourceShip){var excluded=new HashSet<ShipPartState>(detached);foreach(var part in sourceShip.Parts)if(part.present&&!excluded.Contains(part)){var collider=part.view?part.view.GetComponent<Collider>():null;if(collider)sourceColliders.Add(collider);}}
            foreach(var island in Islands(parts)){
                Vector3 center=Center(island);var outward=center-sourceCenter;if(outward.sqrMagnitude<.001f)outward=Random.onUnitSphere;
                var drift=(outward.normalized+Random.onUnitSphere*.65f).normalized*(tuning?tuning.debrisDriftSpeed:2.5f);
                CreateCluster(island,velocity+drift,Random.onUnitSphere*(tuning?tuning.debrisSpinDegreesPerSecond:25)*Mathf.Deg2Rad,sourceShip,sourceColliders);
            }
        }
        internal void MarkChanged(DebrisCluster cluster)=>changed.Add(cluster);
        internal void EmitDestroyedPart(DebrisPartHitbox part,Vector3 velocity)
        {
            if(!effects)return;var mesh=part.GetComponent<MeshFilter>().sharedMesh;var material=part.GetComponent<MeshRenderer>().sharedMaterial;
            var color=material&&material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):Color.white;
            effects.EmitPartDestroyed(part.transform.TransformPoint(mesh.bounds.center),part.transform.rotation,Vector3.Scale(mesh.bounds.size,part.transform.lossyScale),velocity,color);
        }
        public void Step(float dt)
        {
            if(changed.Count>0){var pending=new List<DebrisCluster>(changed);changed.Clear();foreach(var cluster in pending)if(cluster)Split(cluster);}
            // Healthy parts persist until destroyed or the scene is unloaded.
            for(int i=clusters.Count-1;i>=0;i--){var cluster=clusters[i];if(!cluster){clusters.RemoveAt(i);continue;}UpdateSeparation(cluster);}
        }
        void Split(DebrisCluster cluster)
        {
            var groups=Islands(cluster.parts);if(groups.Count==0){Remove(cluster);return;}
            if(groups.Count==1){cluster.parts.RemoveAll(p=>!p||!p.Part.present);Recenter(cluster);return;}
            var body=cluster.Body;
            foreach(var group in groups){var center=Center(group);CreateCluster(group,body.GetPointVelocity(center),body.angularVelocity,cluster.SuppressSourceCollisions?cluster.SourceShip:null,cluster.ignoredSourceColliders);}
            cluster.parts.Clear();Remove(cluster);
        }
        DebrisCluster CreateCluster(List<DebrisPartHitbox> parts,Vector3 velocity,Vector3 spin,FlightShipRuntime sourceShip=null,List<Collider> sourceColliders=null)
        {
            var go=new GameObject("DriftingWreckage",typeof(Rigidbody),typeof(DebrisCluster));go.transform.SetParent(transform,false);go.transform.position=Center(parts);
            var cluster=go.GetComponent<DebrisCluster>();cluster.world=this;cluster.Body=go.GetComponent<Rigidbody>();cluster.Body.isKinematic=true;cluster.Body.useGravity=false;
            // New rigidbodies otherwise keep their original physics pose until the next transform sync.
            cluster.Body.position=go.transform.position;cluster.Body.rotation=go.transform.rotation;
            foreach(var part in parts){part.transform.SetParent(go.transform,true);part.Cluster=cluster;cluster.parts.Add(part);part.gameObject.SetActive(true);}
            cluster.SourceShip=sourceShip;cluster.sourceRevision=sourceShip?sourceShip.StructureRevision:0;
            if(sourceShip&&sourceColliders!=null&&sourceColliders.Count>0){cluster.SuppressSourceCollisions=true;foreach(var collider in sourceColliders)if(collider){cluster.ignoredSourceColliders.Add(collider);SetIgnored(cluster,collider,true);}}
            Recenter(cluster);var body=cluster.Body;body.linearDamping=0;body.angularDamping=0;body.sleepThreshold=0;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;body.interpolation=RigidbodyInterpolation.Interpolate;
            float radius=1;foreach(var part in parts){var mesh=part.GetComponent<MeshCollider>().sharedMesh;radius=Mathf.Max(radius,Vector3.Distance(go.transform.position,part.transform.TransformPoint(mesh.bounds.center))+mesh.bounds.extents.magnitude);}
            // Large islands must separate before their outer edge tumbles back into the remaining core.
            float maxSpin=Mathf.Max(.02f,(tuning?tuning.debrisDriftSpeed:2.5f)*.5f/radius);
            body.isKinematic=false;body.linearVelocity=velocity;body.angularVelocity=Vector3.ClampMagnitude(spin,maxSpin);clusters.Add(cluster);return cluster;
        }
        static void Recenter(DebrisCluster cluster)
        {
            float mass=0;Vector3 local=Vector3.zero;bool first=true;var bounds=new Bounds();
            foreach(var part in cluster.parts)if(part&&part.Part.present){mass+=part.MassKg;local+=cluster.transform.InverseTransformPoint(part.transform.TransformPoint(BlockModules.LocalCenter(part.Part.record.shape)))*part.MassKg;
                var shape=part.GetComponent<MeshCollider>();var box=TransformBounds(shape.sharedMesh.bounds,cluster.transform.worldToLocalMatrix*part.transform.localToWorldMatrix);if(first){bounds=box;first=false;}else bounds.Encapsulate(box);
            }
            cluster.localBounds=bounds;
            cluster.Body.mass=Mathf.Max(1,mass);cluster.Body.centerOfMass=mass>0?local/mass:Vector3.zero;cluster.Body.ResetInertiaTensor();
        }
        static Vector3 Center(List<DebrisPartHitbox> parts)
        {
            Vector3 sum=Vector3.zero;float mass=0;foreach(var part in parts)if(part&&part.Part.present){sum+=part.transform.TransformPoint(BlockModules.LocalCenter(part.Part.record.shape))*part.MassKg;mass+=part.MassKg;}return mass>0?sum/mass:Vector3.zero;
        }
        static List<List<DebrisPartHitbox>> Islands(List<DebrisPartHitbox> parts)
        {
            var cells=new Dictionary<Vector3Int,int>();var active=new HashSet<int>();
            for(int i=0;i<parts.Count;i++)if(parts[i]&&parts[i].Part.present){active.Add(i);foreach(var cell in BlockModules.BodyCells(parts[i].Part.record))cells[cell]=i;}
            var result=new List<List<DebrisPartHitbox>>();var queue=new Queue<int>();
            for(int i=0;i<parts.Count;i++){if(!active.Remove(i))continue;var island=new List<DebrisPartHitbox>();queue.Enqueue(i);
                while(queue.Count>0){int current=queue.Dequeue();island.Add(parts[current]);foreach(var cell in BlockModules.BodyCells(parts[current].Part.record))foreach(var side in Directions)
                    if(cells.TryGetValue(cell+side,out int next)&&active.Remove(next))queue.Enqueue(next);}
                result.Add(island);
            }return result;
        }
        void UpdateSeparation(DebrisCluster cluster)
        {
            if(!cluster.SuppressSourceCollisions)return;
            var source=cluster.SourceShip;if(!source||!source.IsAlive){RestoreSourceCollisions(cluster);return;}
            if(cluster.sourceRevision!=source.StructureRevision){
                for(int i=cluster.ignoredSourceColliders.Count-1;i>=0;i--){var c=cluster.ignoredSourceColliders[i];if(!c||!c.gameObject.activeInHierarchy){if(c)SetIgnored(cluster,c,false);cluster.ignoredSourceColliders.RemoveAt(i);}}
                foreach(var part in source.Parts)if(part.present&&part.view){var c=part.view.GetComponent<Collider>();if(c&&!cluster.ignoredSourceColliders.Contains(c)){cluster.ignoredSourceColliders.Add(c);SetIgnored(cluster,c,true);}}
                cluster.sourceRevision=source.StructureRevision;
            }
            var bounds=cluster.WorldBounds;bounds.Expand(.5f);
            foreach(var collider in cluster.ignoredSourceColliders)if(collider&&collider.gameObject.activeInHierarchy){
                var mesh=collider as MeshCollider;var box=mesh&&mesh.sharedMesh?TransformBounds(mesh.sharedMesh.bounds,CylindricalHitQuery.PhysicsMatrix(collider)):collider.bounds;
                if(bounds.Intersects(box))return;
            }
            RestoreSourceCollisions(cluster);
        }
        static void SetIgnored(DebrisCluster cluster,Collider source,bool ignored){foreach(var part in cluster.parts)if(part&&part.Part.present){var collider=part.GetComponent<Collider>();if(collider)Physics.IgnoreCollision(source,collider,ignored);}}
        static void RestoreSourceCollisions(DebrisCluster cluster){foreach(var source in cluster.ignoredSourceColliders)if(source)SetIgnored(cluster,source,false);cluster.ignoredSourceColliders.Clear();cluster.SuppressSourceCollisions=false;}
        internal static Bounds TransformBounds(Bounds bounds,Matrix4x4 matrix){var x=matrix.MultiplyVector(new Vector3(bounds.extents.x,0,0));var y=matrix.MultiplyVector(new Vector3(0,bounds.extents.y,0));var z=matrix.MultiplyVector(new Vector3(0,0,bounds.extents.z));return new Bounds(matrix.MultiplyPoint3x4(bounds.center),new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z))*2);}
        void Remove(DebrisCluster cluster){clusters.Remove(cluster);changed.Remove(cluster);if(cluster){RestoreSourceCollisions(cluster);cluster.gameObject.SetActive(false);Release(cluster.gameObject);}}
        static void CopyMatrix(Matrix4x4 pose,Transform target){target.SetPositionAndRotation(pose.MultiplyPoint3x4(Vector3.zero),pose.rotation);var parent=target.parent?target.parent.lossyScale:Vector3.one;var scale=pose.lossyScale;target.localScale=new Vector3(scale.x/parent.x,scale.y/parent.y,scale.z/parent.z);}
        static void CopyRenderer(MeshRenderer source,MeshRenderer target){target.sharedMaterials=source.sharedMaterials;target.shadowCastingMode=source.shadowCastingMode;target.receiveShadows=source.receiveShadows;var properties=new MaterialPropertyBlock();source.GetPropertyBlock(properties);target.SetPropertyBlock(properties);}
        static void Release(Object target){if(Application.isPlaying)Destroy(target);else DestroyImmediate(target);}
    }
}
