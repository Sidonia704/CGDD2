// @role Place and remove parts, validate occupancy and rebuild the assembled ship.
// @calls BlockModules, BlockConnectivity, BlockPlacement, BlockGeometry, ModuleDatabase, WeaponClearance, WeaponGroups

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;
namespace Starframe.Construction
{
    public sealed class ShipAssembly : MonoBehaviour
    {
        [SerializeField] Material bodyMaterial;
        [SerializeField] Material edgeMaterial;
        [SerializeField] Material coreMaterial,forwardRayMaterial;
        [SerializeField] Mesh thruster1Mesh,thruster2Mesh,thruster1Edges,thruster2Edges;
        ModuleDatabase moduleDatabase;
        BlueprintData weaponGroupData=new BlueprintData();
        readonly Starframe.Combat.WeaponClearance weaponClearance=new Starframe.Combat.WeaponClearance();
        public string PlacementFailure{get;private set;}
        readonly Dictionary<string,BlockView> blocks=new Dictionary<string,BlockView>();
        Mesh[] solids,outlines,collisions;
        readonly List<Mesh> generatedMeshes=new List<Mesh>();
        MeshCollider probe,secondProbe;
        public int Count=>blocks.Count;
        public IEnumerable<BlockView> Blocks=>blocks.Values;
        public event Action Changed;
        public Material BodyMaterial=>bodyMaterial;
        public Material DirectionMaterial=>forwardRayMaterial;
        public Material MaterialFor(BlockShape shape)
        {
            var definition=moduleDatabase?moduleDatabase.Get(shape):null;
            return definition&&definition.material?definition.material:shape==BlockShape.Core?coreMaterial:bodyMaterial;
        }
        public Vector3 ShipForward {get{foreach(var b in blocks.Values)if(b.Record.shape==BlockShape.Core)return (b.transform.localRotation*Vector3.forward).normalized;return Vector3.forward;}}
        void Awake()
        {
            if(solids!=null)return;
            int shapeCount=System.Enum.GetValues(typeof(BlockShape)).Length;
            solids=new Mesh[shapeCount];outlines=new Mesh[shapeCount];
            collisions=new Mesh[shapeCount];
            foreach(BlockShape shape in System.Enum.GetValues(typeof(BlockShape)))
            {
                int i=(int)shape;
                solids[i]=BlockGeometry.CreateSolid(shape);generatedMeshes.Add(solids[i]);
                collisions[i]=BlockGeometry.CreateCellCollision(shape);generatedMeshes.Add(collisions[i]);
                if(shape!=BlockShape.Core){outlines[i]=BlockGeometry.CreateOutline(shape);generatedMeshes.Add(outlines[i]);}
            }
            if(thruster1Mesh)solids[(int)BlockShape.Thruster1]=thruster1Mesh;if(thruster2Mesh)solids[(int)BlockShape.Thruster2]=thruster2Mesh;
            if(thruster1Edges)outlines[(int)BlockShape.Thruster1]=thruster1Edges;if(thruster2Edges)outlines[(int)BlockShape.Thruster2]=thruster2Edges;
            probe=MakeProbe("PlacementCollisionProbe");secondProbe=MakeProbe("ImportCollisionProbe");
        }
        public void Configure(ModuleDatabase database)
        {
            if(solids==null)Awake();
            moduleDatabase=database;
            BlockModules.Configure(database);
            if(!moduleDatabase)return;
            foreach(BlockShape shape in System.Enum.GetValues(typeof(BlockShape)))
            {
                var definition=moduleDatabase.Get(shape);if(!definition)continue;
                int index=(int)shape;
                if(definition.mesh)solids[index]=definition.mesh;
                if(definition.outline)outlines[index]=definition.outline;
                generatedMeshes.Remove(collisions[index]);Release(collisions[index]);collisions[index]=BlockGeometry.CreateCellCollision(shape);generatedMeshes.Add(collisions[index]);
            }
        }
        MeshCollider MakeProbe(string name)
        {
            var go=new GameObject(name);go.layer=2;go.transform.SetParent(transform,false);
            var collider=go.AddComponent<MeshCollider>();collider.convex=true;collider.isTrigger=true;collider.enabled=true;return collider;
        }
        public Mesh Solid(BlockShape shape){if(solids==null)Awake();return solids[(int)shape];}
        public Mesh Collision(BlockShape shape){if(solids==null)Awake();return collisions[(int)shape];}
        public Mesh Outline(BlockShape shape){if(outlines==null)Awake();return outlines[(int)shape];}
        public bool Occupied(Vector3Int cell)
        {
            var position=new Vector3(cell.x,cell.y+.5f,cell.z);
            foreach(var b in blocks.Values)if((b.transform.localPosition-position).sqrMagnitude<.000001f)return true;
            return false;
        }
        public bool CanPlace(BlockShape shape,Vector3 position,Quaternion rotation)
        {
            PlacementFailure=null;
            if(!BlockPlacement.IsCellAligned(position,rotation))return Reject("必须对齐标准格子。");
            if(shape==BlockShape.Core)foreach(var b in blocks.Values)if(b.Record.shape==BlockShape.Core)return Reject("每艘船只能有一个核心。");

            var candidate=new BlockRecord{shape=shape,position=position,rotation=rotation};
            foreach(var b in blocks.Values)if(BlockModules.CellsOverlap(candidate,b.Record))return Reject("占用格或推进器净空已被占用。");
            if(blocks.Count>0)
            {
                bool attached=false;foreach(var b in blocks.Values)if(BlockConnectivity.Touches(candidate,b.Record)){attached=true;break;}
                if(!attached)return Reject("必须与现有船体通过面连接。");
            }
            probe.sharedMesh=Collision(shape);
            var world=transform.TransformPoint(position);var q=transform.rotation*rotation;
            float radius=Solid(shape).bounds.extents.magnitude*MaxScale();
            foreach(var b in blocks.Values)
            {
                float limit=radius+b.Solid.bounds.extents.magnitude;
                if((world-b.transform.position).sqrMagnitude>limit*limit)continue;
                if(Physics.ComputePenetration(probe,world,q,b.GetComponent<MeshCollider>(),
                    b.transform.position,b.transform.rotation,out _,out float distance)&&distance>.001f)return Reject("实体模型发生重叠。");
            }
            var newDefinition=moduleDatabase?moduleDatabase.Get(shape):null;
            foreach(var b in blocks.Values){
                if(weaponClearance.IsBlockedBy(candidate,newDefinition,b.Record,Collision(b.Record.shape)))return Reject("武器炮口前方被现有部件遮挡。");
                var definition=moduleDatabase?moduleDatabase.Get(b.Record.shape):null;
                if(weaponClearance.IsBlockedBy(b.Record,definition,candidate,Collision(shape)))return Reject("该部件会遮挡已有武器的开火路径。");
            }
            return true;
        }
        bool Reject(string message){PlacementFailure=message;return false;}
        float MaxScale(){var s=transform.lossyScale;return Mathf.Max(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));}
        public bool Place(BlockShape shape,Vector3Int cell,int rotation=0)
        {
            return Place(shape,new Vector3(cell.x,cell.y+.5f,cell.z),Quaternion.Euler(0,rotation*90,0),rotation);
        }
        public bool Place(BlockShape shape,Vector3 position,Quaternion rotation,int quarterTurns=0)
        {
            if(!CanPlace(shape,position,rotation))return false;
            var record=new BlockRecord{id=Guid.NewGuid().ToString("N"),shape=shape,position=position,rotation=rotation,
                cell=BlockPlacement.GridHint(position),yawQuarterTurns=((quarterTurns%4)+4)%4};
            Create(record);Physics.SyncTransforms();Changed?.Invoke();return true;
        }
        BlockView Create(BlockRecord record)
        {
            var go=new GameObject(record.shape+"_"+record.id.Substring(0,Math.Min(8,record.id.Length)));
            go.transform.SetParent(transform,false);var view=go.AddComponent<BlockView>();
            var definition=moduleDatabase?moduleDatabase.Get(record.shape):null;
            view.Initialize(record,Solid(record.shape),definition&&!definition.drawOutline?null:Outline(record.shape),MaterialFor(record.shape),edgeMaterial,true,Collision(record.shape));
            ModuleGlowAccents.Attach(go.transform,record.shape,Solid(record.shape),null,definition?definition.muzzleLocalPosition:(Vector3?)null);
            if(record.shape==BlockShape.Core){
                var rayObject=new GameObject("ShipForwardRay");rayObject.transform.SetParent(go.transform,false);
                var ray=rayObject.AddComponent<LineRenderer>();ray.useWorldSpace=false;ray.positionCount=2;
                ray.SetPositions(new[]{Vector3.forward*.51f,Vector3.forward*45});ray.startWidth=ray.endWidth=.012f;
                ray.sharedMaterial=forwardRayMaterial;ray.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;ray.receiveShadows=false;
            }
            blocks.Add(record.id,view);return view;
        }
        public bool RotateCore(string id)
        {
            if(!blocks.TryGetValue(id,out var core)||core.Record.shape!=BlockShape.Core)return false;
            var q=core.transform.localRotation;
            q=Quaternion.AngleAxis(90,q*Vector3.up)*q;
            core.transform.localRotation=q;core.Record.rotation=q;core.Record.yawQuarterTurns=(core.Record.yawQuarterTurns+1)%4;
            Changed?.Invoke();return true;
        }
        public bool Remove(string id)
        {
            if(!blocks.TryGetValue(id,out var block))return false;
            if(!BlockConnectivity.IsConnected(Export("ConnectivityCheck"),id))return false;
            blocks.Remove(id);block.gameObject.SetActive(false);Release(block.gameObject);Changed?.Invoke();return true;
        }
        public bool Remove(Vector3Int cell)
        {
            foreach(var b in blocks.Values)if(b.Record.cell==cell)return Remove(b.Record.id);
            return false;
        }
        public BlueprintData Export(string name)
        {
            var data=new BlueprintData{name=name};
            foreach(var block in blocks.Values)
            {
                var p=BlockPlacement.Copy(block.Record);
                p.position=block.transform.localPosition;p.rotation=block.transform.localRotation;
                p.cell=BlockPlacement.GridHint(p.position);data.parts.Add(p);
            }
            foreach(var p in data.parts)if(p.shape==BlockShape.Core){data.hasCore=true;data.corePartId=p.id;data.shipForward=(p.rotation*Vector3.forward).normalized;}
            WeaponGroups.CopyTo(weaponGroupData,data);WeaponGroups.Normalize(data,moduleDatabase);
            data.parts.Sort((a,b)=>StringComparer.Ordinal.Compare(a.id,b.id));return data;
        }
        public void UpdateWeaponGroups(BlueprintData data){WeaponGroups.CopyTo(data,weaponGroupData);Changed?.Invoke();}
        public void Import(BlueprintData data)
        {
            data=BlueprintRepository.Upgrade(data);
            ValidateGeometry(data);
            WeaponGroups.CopyTo(data,weaponGroupData);
            foreach(var block in blocks.Values){block.gameObject.SetActive(false);Release(block.gameObject);}
            blocks.Clear();foreach(var p in data.parts)Create(BlockPlacement.Copy(p));
            Physics.SyncTransforms();Changed?.Invoke();
        }
        void ValidateGeometry(BlueprintData data)
        {
            float radius=Solid(BlockShape.Cube).bounds.extents.magnitude*MaxScale();
            for(int i=0;i<data.parts.Count;i++)
            {
                var a=data.parts[i];probe.sharedMesh=Collision(a.shape);
                var aPosition=transform.TransformPoint(a.position);var aRotation=transform.rotation*a.rotation;
                for(int j=0;j<i;j++)
                {
                    var b=data.parts[j];var bPosition=transform.TransformPoint(b.position);
                    if((aPosition-bPosition).sqrMagnitude>radius*radius*4)continue;
                    secondProbe.sharedMesh=Collision(b.shape);
                    if(Physics.ComputePenetration(probe,aPosition,aRotation,secondProbe,bPosition,transform.rotation*b.rotation,
                        out _,out float depth)&&depth>.001f)throw new System.IO.InvalidDataException("Blueprint parts overlap.");
                }
            }
        }
        public Bounds ContentBounds()
        {
            var bounds=new Bounds(Vector3.zero,Vector3.zero);bool first=true;
            foreach(var b in blocks.Values){if(first){bounds=b.Solid.bounds;first=false;}else bounds.Encapsulate(b.Solid.bounds);}
            return first?new Bounds(Vector3.zero,Vector3.one):bounds;
        }
        static void Release(UnityEngine.Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        void OnDestroy(){foreach(var mesh in generatedMeshes)if(mesh)Release(mesh);}
    }
}
