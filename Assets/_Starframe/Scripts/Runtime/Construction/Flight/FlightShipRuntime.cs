// @role Track surviving parts, detachments, recovery, power and current ship performance.
// @calls ShipAssembly, BlockConnectivity, FlightTuning, ShipPowerBus, ShipShieldSystem, ShipInstanceRenderer

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Starframe.Construction;
using Starframe.Combat;
namespace Starframe.Flight
{
    public sealed class FlightShipRuntime:MonoBehaviour
    {
        static readonly Vector3Int[] Directions={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
        static readonly ProfilerMarker ConnectivityMarker=new ProfilerMarker("Starframe.Connectivity"),PerformanceMarker=new ProfilerMarker("Starframe.Performance"),RecoveryMarker=new ProfilerMarker("Starframe.Recovery");
        public readonly List<ShipPartState> Parts=new List<ShipPartState>();
        readonly Dictionary<string,int> byId=new Dictionary<string,int>();
        readonly Dictionary<Vector3Int,int> cellOwners=new Dictionary<Vector3Int,int>();
        readonly HashSet<int> missing=new HashSet<int>(),damaged=new HashSet<int>();
        readonly List<int> ready=new List<int>(),healed=new List<int>();
        readonly List<ShipPartState> detachedParts=new List<ShipPartState>();
        List<int>[] neighbors;bool[] visited;int[] queue,partEpochs;
        FlightTuning tuning;BattleTuning battle;int tuningRevision,definitionRevision,coreIndex=-1,recoveryEpoch;
        bool structureDirty,dead;
        double clock,lastDamage=double.NegativeInfinity;
        public Quaternion CoreRotation{get;private set;}=Quaternion.identity;
        public ShipPerformance Performance{get;private set;}
        public ShipPowerBus Power{get;}=new ShipPowerBus();
        public ShipShieldSystem Shields{get;}=new ShipShieldSystem();
        public bool IsAlive=>!dead&&coreIndex>=0&&Parts[coreIndex].present&&Parts[coreIndex].hp>0;
        public int StructureRevision{get;private set;}
        public int HealthRevision{get;private set;}
        public int LifeId{get;private set;}
        public int Team;
        public Vector3 CorePosition=>coreIndex>=0&&Parts[coreIndex].view?Parts[coreIndex].view.transform.position:transform.position;
        public Vector3 CorePhysicsPosition{get{if(coreIndex>=0&&Parts[coreIndex].view){var collider=Parts[coreIndex].view.GetComponent<Collider>();if(collider)return CylindricalHitQuery.PhysicsMatrix(collider).MultiplyPoint3x4(Vector3.zero);}return CorePosition;}}
        public float DamageTaken{get;private set;}
        public float DamageDealt{get;private set;}
        public float InvulnerableUntil;
        public float HullRadius{get;private set;}=1;
        public double CombatTime=>clock;
        public FlightTuning Tuning=>tuning;
        public BlueprintData BlueprintSnapshot{get;private set;}
        public event Action PerformanceChanged,Died;
        public event Action<ShipPartState> PartChanged;
        public event Action<IReadOnlyList<ShipPartState>> PartsDetached;
        public void Initialize(ShipAssembly assembly,BlueprintData blueprint,FlightTuning values,BattleTuning rules=null)
        {
            BlueprintSnapshot=JsonUtility.FromJson<BlueprintData>(JsonUtility.ToJson(blueprint));
            tuning=values;battle=rules;BlockModules.Configure(values.database);
            var views=new Dictionary<string,BlockView>();foreach(var view in assembly.Blocks)views.Add(view.Record.id,view);
            Parts.Clear();byId.Clear();cellOwners.Clear();missing.Clear();damaged.Clear();coreIndex=-1;
            foreach(var record in blueprint.parts){
                var stats=values.Get(record.shape);int index=Parts.Count;
                var state=new ShipPartState{record=BlockPlacement.Copy(record),view=views[record.id].gameObject,hp=stats.maxHp,maxHp=stats.maxHp};
                Parts.Add(state);byId.Add(record.id,index);
                foreach(var cell in BlockModules.BodyCells(record))cellOwners.Add(cell,index);
                var hitbox=state.view.GetComponent<ShipPartHitbox>();if(!hitbox)hitbox=state.view.AddComponent<ShipPartHitbox>();hitbox.Initialize(this,state);
                if(record.shape==BlockShape.Core){coreIndex=index;CoreRotation=record.rotation;}
            }
            BuildTopology();dead=coreIndex<0;clock=0;lastDamage=double.NegativeInfinity;LifeId++;
            RefreshRadius();Recalculate();RefreshPower(true);Shields.Configure(this,true);
            var instances=GetComponent<ShipInstanceRenderer>();
            if(!instances&&Parts.Count>=ShipInstanceRenderer.MinimumPartCount)instances=gameObject.AddComponent<ShipInstanceRenderer>();
            if(instances)instances.Initialize(this,assembly);
        }
        void BuildTopology()
        {
            neighbors=new List<int>[Parts.Count];visited=new bool[Parts.Count];queue=new int[Parts.Count];partEpochs=new int[Parts.Count];
            for(int i=0;i<neighbors.Length;i++)neighbors[i]=new List<int>();
            var pairs=new HashSet<ulong>();
            foreach(var entry in cellOwners)foreach(var direction in Directions){
                if(!cellOwners.TryGetValue(entry.Key+direction,out int j)||j==entry.Value)continue;
                int a=Mathf.Min(entry.Value,j),b=Mathf.Max(entry.Value,j);ulong pair=((ulong)(uint)a<<32)|(uint)b;
                if(pairs.Add(pair)&&BlockConnectivity.Touches(Parts[a].record,Parts[b].record)){neighbors[a].Add(b);neighbors[b].Add(a);}
            }
        }
        void LateUpdate(){if(tuning&&(tuningRevision!=tuning.Revision||definitionRevision!=(tuning.database?tuning.database.Revision:0)))Recalculate();}
        public void BeginStep(float dt){if(dt>0)clock+=dt;}
        public float ApplyDamage(string id,float amount,FlightShipRuntime source=null,bool deferStructure=false)
        {
            if(!IsAlive||amount<=0||float.IsNaN(amount)||float.IsInfinity(amount)||clock<InvulnerableUntil||!byId.TryGetValue(id,out int index))return 0;
            var p=Parts[index];if(!p.present)return 0;
            float actual=Mathf.Min(p.hp,amount);if(actual<=0)return 0;
            p.hp-=actual;DamageTaken+=actual;HealthRevision++;if(source)source.DamageDealt+=actual;
            lastDamage=clock;recoveryEpoch++;
            if(Performance!=null)Performance.currentHp=Mathf.Max(0,Performance.currentHp-actual);
            if(p.hp<=0){Remove(index);if(index==coreIndex)Die();}else damaged.Add(index);
            if(!deferStructure)FlushChanges();return actual;
        }
        void Remove(int index,bool detached=false)
        {
            var part=Parts[index];if(!part.present)return;
            Shields.PartRemoved(part);
            part.detached=detached;part.present=false;part.hp=0;part.rebuildProgress=0;damaged.Remove(index);missing.Add(index);
            if(part.view)part.view.SetActive(false);structureDirty=true;PartChanged?.Invoke(part);
        }
        void Die()
        {
            if(dead)return;
            DetachSurvivors(true);
            dead=true;FlushChanges();
            Died?.Invoke();
        }
        void DetachSurvivors(bool all)
        {
            detachedParts.Clear();
            for(int i=0;i<Parts.Count;i++)if(Parts[i].present&&Parts[i].hp>0&&(all||!visited[i]))detachedParts.Add(Parts[i]);
            // Transfer surviving health and collision before clearing the original blueprint slots.
            if(detachedParts.Count>0)PartsDetached?.Invoke(detachedParts);
            foreach(var part in detachedParts)Remove(byId[part.record.id],true);
            detachedParts.Clear();
        }
        public void FlushChanges()
        {
            if(!structureDirty)return;
            using(ConnectivityMarker.Auto()){
                if(IsAlive){
                    Array.Clear(visited,0,visited.Length);int head=0,tail=0;queue[tail++]=coreIndex;visited[coreIndex]=true;
                    while(head<tail){int a=queue[head++];foreach(int b in neighbors[a])if(!visited[b]&&Parts[b].present){visited[b]=true;queue[tail++]=b;}}
                    DetachSurvivors(false);
                }
            }
            structureDirty=false;StructureRevision++;Recalculate();
        }
        public void SetPartPresent(string id,bool present)
        {
            if(!byId.TryGetValue(id,out int index)||dead)return;
            if(!present){Remove(index);if(index==coreIndex)Die();}
            else{var p=Parts[index];p.detached=false;p.present=true;p.hp=p.maxHp;p.rebuildProgress=0;if(p.view)p.view.SetActive(true);missing.Remove(index);structureDirty=true;PartChanged?.Invoke(p);}
            FlushChanges();
        }
        public float GetRebuildProgress(string id)=>byId.TryGetValue(id,out int i)&&partEpochs[i]==recoveryEpoch?Parts[i].rebuildProgress:0;
        public void AdvanceRecovery(float dt)
        {
            if(!IsAlive||dt<=0)return;
            double idle=clock-lastDamage-(battle?battle.outOfCombatDelay:1);
            if(idle<=0)return;
            float activeDt=(float)Math.Min(dt,idle);
            using(RecoveryMarker.Auto()){
                healed.Clear();bool healthChanged=false;
                foreach(int i in damaged){var p=Parts[i];if(!p.present){healed.Add(i);continue;}var d=tuning.GetDefinition(p.record.shape);float seconds=d?d.recoverySeconds:3;float increase=Mathf.Min(p.maxHp-p.hp,p.maxHp/Mathf.Max(.01f,seconds)*activeDt);p.hp+=increase;Performance.currentHp+=increase;healthChanged|=increase>0;if(p.hp>=p.maxHp)healed.Add(i);}
                if(healthChanged)HealthRevision++;
                foreach(int i in healed)damaged.Remove(i);
                ready.Clear();
                // Evaluate all support against the same snapshot; submit new blocks only afterwards.
                foreach(int i in missing){var p=Parts[i];if(partEpochs[i]!=recoveryEpoch){p.rebuildProgress=0;partEpochs[i]=recoveryEpoch;}
                    bool supported=false;foreach(int neighbor in neighbors[i])if(Parts[neighbor].present){supported=true;break;}
                    if(!supported){p.rebuildProgress=0;continue;}
                    var d=tuning.GetDefinition(p.record.shape);float seconds=d?d.recoverySeconds:3;
                    p.rebuildProgress+=activeDt;if(p.rebuildProgress+.00001f>=Mathf.Max(.01f,seconds))ready.Add(i);
                }
                foreach(int i in ready){var p=Parts[i];p.detached=false;p.present=true;p.hp=p.maxHp;p.rebuildProgress=0;missing.Remove(i);if(p.view)p.view.SetActive(true);structureDirty=true;PartChanged?.Invoke(p);}
            }
            FlushChanges();
        }
        public void Respawn()
        {
            dead=coreIndex<0;LifeId++;recoveryEpoch++;lastDamage=double.NegativeInfinity;missing.Clear();damaged.Clear();DamageTaken=DamageDealt=0;
            foreach(var p in Parts){p.detached=false;p.present=true;p.hp=p.maxHp;p.rebuildProgress=0;if(p.view)p.view.SetActive(true);PartChanged?.Invoke(p);}
            structureDirty=true;FlushChanges();RefreshPower(true);Shields.Configure(this,true);
        }
        void RefreshRadius(){var b=new Bounds(transform.position,Vector3.zero);foreach(var p in Parts){var r=p.view?p.view.GetComponent<MeshRenderer>():null;if(r)b.Encapsulate(r.bounds);}HullRadius=Mathf.Max(1,b.extents.magnitude);}
        void RefreshPower(bool fill=false){float capacity=0,generation=0;foreach(var p in Parts)if(p.present){var d=tuning.GetDefinition(p.record.shape);if(d){capacity+=d.storageJoules;generation+=d.generationWatts;}}Power.Configure(capacity,generation,fill);}
        void RefreshThrusters()
        {
            foreach(var p in Parts){p.thrustAvailable=p.present;if(!p.present||!BlockModules.IsThruster(p.record.shape))continue;
                var anchor=BlockPlacement.GridHint(p.record.position);var direction=BlockModules.Direction(p.record.rotation);
                for(int i=0;i<BlockModules.Clearance(p.record.shape);i++)if(cellOwners.TryGetValue(anchor+direction*(BlockModules.BodyLength(p.record.shape)+i),out int owner)&&Parts[owner].present){p.thrustAvailable=false;break;}
            }
        }
        public void Recalculate()
        {
            using(PerformanceMarker.Auto()){
                tuningRevision=tuning.Revision;definitionRevision=tuning.database?tuning.database.Revision:0;HealthRevision++;
                foreach(var p in Parts){var s=tuning.Get(p.record.shape);p.maxHp=s.maxHp;p.hp=Mathf.Min(p.hp,p.maxHp);}
                RefreshThrusters();Performance=ShipPerformance.Calculate(Parts,CoreRotation,tuning);RefreshPower();Shields.Configure(this);PerformanceChanged?.Invoke();
            }
        }
        public Vector3 CenterOfMass(){Vector3 sum=Vector3.zero;float mass=0;foreach(var p in Parts)if(p.present){float m=tuning.Get(p.record.shape).massKg;sum+=(p.record.position+p.record.rotation*BlockModules.LocalCenter(p.record.shape))*m;mass+=m;}return mass>0?sum/mass:Vector3.zero;}
    }
}
