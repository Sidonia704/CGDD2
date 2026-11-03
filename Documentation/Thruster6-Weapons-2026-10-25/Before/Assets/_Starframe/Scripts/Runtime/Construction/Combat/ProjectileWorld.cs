// @role Resolve swept direct shots, short beams and guided missile blasts against shields, hulls and scenery.
// @calls CylindricalHitQuery, ShipShieldSystem, FlightShipRuntime, DebrisPartHitbox, LaserVisualPool, SpecialWeaponVisualPool, VoxelBodyHitbox, VoxelStructureRuntime

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Starframe.Flight;
using Starframe.Maps;
namespace Starframe.Combat
{
    public sealed class ProjectileWorld
    {
        sealed class Bolt
        {
            public WeaponDefinition weapon;public FlightShipRuntime owner;public Vector3 origin,direction;
            public float distance,speed,radius,damage,range,depth,multiplier,firstHit=-1;
            public int visual=-1;public bool hitConfirmed;public readonly HashSet<ShipPartState> hitParts=new HashSet<ShipPartState>();
            public readonly HashSet<VoxelCellState> hitVoxels=new HashSet<VoxelCellState>();
        }
        sealed class Missile
        {
            public FlightShipRuntime owner,target;public int targetLife,visual=-1;
            public WeaponDefinition weapon;public Vector3 position,direction;public float speed,distance,age,damageScale;
        }
        readonly List<Missile> missiles=new List<Missile>();readonly Stack<Missile> freeMissiles=new Stack<Missile>();
        readonly Dictionary<FlightShipRuntime,float> blastShieldRatios=new Dictionary<FlightShipRuntime,float>();
        readonly HashSet<ShipPartState> blastParts=new HashSet<ShipPartState>();
        readonly HashSet<VoxelStructureRuntime> blastStructures=new HashSet<VoxelStructureRuntime>();
        struct Contact{public Collider collider;public ShipPartHitbox hitbox;public DebrisPartHitbox debris;public ShipShieldSystem.Field shield;public VoxelStructureRuntime scenery;public VoxelCellState voxel;public float distance;}
        struct ShieldSample{public ShipShieldSystem.Field field;public Vector3 center;public float radius;}
        static readonly ProfilerMarker Marker=new ProfilerMarker("Starframe.Projectiles");
        readonly List<Bolt> bolts=new List<Bolt>();readonly Stack<Bolt> free=new Stack<Bolt>();
        readonly List<Contact> contacts=new List<Contact>();readonly CylindricalHitQuery geometry=new CylindricalHitQuery();
        readonly List<FlightShipRuntime> shieldShips=new List<FlightShipRuntime>();
        readonly List<ShieldSample> shieldSamples=new List<ShieldSample>();
        readonly List<VoxelContact> voxelContacts=new List<VoxelContact>();
        readonly HashSet<VoxelStructureRuntime> queriedScenery=new HashSet<VoxelStructureRuntime>();
        Collider[] candidates=new Collider[64];LaserVisualPool visuals;SpecialWeaponVisualPool specialVisuals;
        public int ActiveCount=>bolts.Count+missiles.Count;
        public int ActiveMissileCount=>missiles.Count;
        public int TotalSpawned{get;private set;}
        public bool UseShieldGeometryCache{get;set;}=true;
        public event Action<Vector3> Impact;
        public event Action<Vector3,WeaponDefinition> WeaponImpact,MissileExplosion;
        public event Action<Vector3,Vector3,WeaponDefinition> MuzzleFlash;
        public event Action<FlightShipRuntime,Vector3> ShotFired;
        // One confirmation per projectile, only after a damageable surface actually absorbs damage.
        public event Action<FlightShipRuntime> HitConfirmed;
        public void Initialize(LaserVisualPool pool)
        {
            visuals=pool;
            if(pool){specialVisuals=pool.GetComponent<SpecialWeaponVisualPool>();if(!specialVisuals)specialVisuals=pool.gameObject.AddComponent<SpecialWeaponVisualPool>();specialVisuals.Initialize();}
        }
        public void RegisterShields(FlightShipRuntime ship){if(ship&&!shieldShips.Contains(ship))shieldShips.Add(ship);}
        public void Fire(FlightShipRuntime owner,WeaponDefinition weapon,Vector3 position,Vector3 direction,Transform muzzleSource=null,Vector3 muzzleLocalPosition=default,FlightShipRuntime lockedTarget=null)
        {
            if(!owner||!owner.IsAlive||!weapon||direction.sqrMagnitude<.000001f)return;
            if(weapon.delivery==WeaponDelivery.GuidedMissile){
                if(!lockedTarget||!lockedTarget.IsAlive||lockedTarget==owner||lockedTarget.Team==owner.Team)return;
                var offsets=weapon.missileLaunchOffsets;int count=offsets!=null&&offsets.Length>0?offsets.Length:1;
                var collider=muzzleSource?muzzleSource.GetComponent<Collider>():null;
                var matrix=collider?CylindricalHitQuery.PhysicsMatrix(collider):muzzleSource?muzzleSource.localToWorldMatrix:Matrix4x4.Rotate(Quaternion.FromToRotation(Vector3.forward,direction));
                for(int i=0;i<count;i++){
                    Vector3 launch=position+matrix.MultiplyVector(offsets!=null&&offsets.Length>0?offsets[i]:Vector3.zero);
                    MuzzleFlash?.Invoke(launch,direction,weapon);FireMissile(owner,weapon,launch,direction,lockedTarget,1f/count);
                }
                return;
            }
            MuzzleFlash?.Invoke(position,direction,weapon);
            if(weapon.delivery==WeaponDelivery.Beam){FireBeam(owner,weapon,position,direction.normalized,muzzleSource,muzzleLocalPosition);return;}
            var b=free.Count>0?free.Pop():new Bolt();b.weapon=weapon;b.owner=owner;b.origin=position;b.direction=direction.normalized;b.distance=0;b.firstHit=-1;b.hitConfirmed=false;b.hitParts.Clear();b.hitVoxels.Clear();
            b.speed=Mathf.Max(.01f,weapon.projectileSpeed);b.radius=Mathf.Max(.001f,weapon.projectileDiameter*.5f);b.damage=Mathf.Max(0,weapon.damage);b.range=Mathf.Max(.1f,weapon.range);b.depth=Mathf.Max(.001f,weapon.penetrationDepth);b.multiplier=Mathf.Clamp01(weapon.secondLayerDamageMultiplier);
            b.visual=visuals?visuals.Acquire(position,direction,weapon.projectileDiameter,weapon.visualLength,weapon.tailLength,muzzleSource,muzzleLocalPosition,weapon.visualWidth,weapon.visualIntensity,weapon.effectColor,weapon.minimumVisualPixels):-1;bolts.Add(b);TotalSpawned++;
            ShotFired?.Invoke(owner,position);
        }
        int Query(Vector3 from,Vector3 to,float radius)
        {
            int count;
            while(true){count=Physics.OverlapCapsuleNonAlloc(from,to,radius,candidates,~(1<<2|1<<5),QueryTriggerInteraction.Ignore);if(count<candidates.Length)return count;Array.Resize(ref candidates,candidates.Length*2);}
        }
        public bool OwnPathBlocked(FlightShipRuntime owner,ShipPartState gun,Vector3 position,Vector3 direction,float diameter)
        {
            float length=owner.HullRadius*2+2,radius=Mathf.Max(.001f,diameter*.5f);int count=Query(position,position+direction*length,radius);
            for(int i=0;i<count;i++){var h=candidates[i].GetComponent<ShipPartHitbox>();if(h&&h.Ship==owner&&h.Part!=gun&&h.Part.present&&geometry.TryHit(candidates[i],position,direction,radius,0,length,out _))return true;}return false;
        }
        public void Step(float dt)
        {
            if(dt<=0||float.IsNaN(dt)||float.IsInfinity(dt)||(bolts.Count==0&&missiles.Count==0))return;
            using(Marker.Auto()){
            // Physics poses are fixed for this step; shield health is still checked live for every shot.
            shieldSamples.Clear();
            foreach(var ship in shieldShips)if(ship&&ship.IsAlive)
                foreach(var field in ship.Shields.Fields)if(field.Active&&field.charge>0)
                    shieldSamples.Add(new ShieldSample{field=field,center=UseShieldGeometryCache?field.Center:default,radius=field.definition.shieldRadius});
            for(int i=bolts.Count-1;i>=0;i--){
                var b=bolts[i];if(!b.owner||!b.owner.IsAlive){Release(i);continue;}
                float limit=b.firstHit<0?b.range:Mathf.Min(b.range,b.firstHit+b.depth);
                float next=Mathf.Min(limit,b.distance+b.speed*dt);contacts.Clear();queriedScenery.Clear();
                foreach(var sample in shieldSamples){var field=sample.field;
                    if(field.ship!=b.owner&&field.Active&&field.charge>0&&
                        ShipShieldSystem.TrySphereEntry(b.origin,b.direction,UseShieldGeometryCache?sample.center:field.Center,sample.radius+b.radius,b.distance,next,out float shieldEntry))
                        contacts.Add(new Contact{shield=field,distance=shieldEntry});
                }
                int count=Query(b.origin+b.direction*b.distance,b.origin+b.direction*next,b.radius);
                for(int j=0;j<count;j++){
                    var c=candidates[j];var h=c.GetComponent<ShipPartHitbox>();
                    if(h&&(h.Ship==b.owner||!h.Ship.IsAlive||!h.Part.present||b.hitParts.Contains(h.Part)))continue;
                    var debris=c.GetComponent<DebrisPartHitbox>();
                    if(debris&&(!debris.Part.present||b.hitParts.Contains(debris.HitIdentity)))continue;
                    var voxelBody=c.GetComponent<VoxelBodyHitbox>();
                    if(voxelBody){
                        if(!queriedScenery.Add(voxelBody.Structure))continue;
                        voxelContacts.Clear();voxelBody.CollectHits(b.origin,b.direction,b.radius,b.distance,next,geometry,voxelContacts);
                        foreach(var contact in voxelContacts)if(!b.hitVoxels.Contains(contact.cell))contacts.Add(new Contact{scenery=contact.structure,voxel=contact.cell,distance=contact.distance});
                        continue;
                    }
                    if(geometry.TryHit(c,b.origin,b.direction,b.radius,b.distance,next,out float entry))contacts.Add(new Contact{collider=c,hitbox=h,debris=debris,distance=entry});
                }
                contacts.Sort((a,c)=>a.distance.CompareTo(c.distance));
                foreach(var hit in contacts){
                    if(hit.shield!=null){
                        var field=hit.shield;if(!field.Active||field.charge<=0||hit.distance>limit)continue;
                        Vector3 point=b.origin+b.direction*hit.distance;
                        float absorbed=field.ship.Shields.Absorb(field,b.damage,point);
                        if(absorbed<=0)continue;
                        ConfirmHit(b);
                        EmitImpact(b,point);b.damage=Mathf.Max(0,b.damage-absorbed);
                        if(b.damage<=.00001f){next=Mathf.Min(next,hit.distance);limit=next;break;}
                        continue;
                    }
                    if(hit.scenery){
                        if(!hit.voxel.Alive||b.hitVoxels.Contains(hit.voxel))continue;
                        if(b.firstHit<0){b.firstHit=hit.distance;limit=Mathf.Min(b.range,b.firstHit+b.depth);}
                        if(hit.distance>=limit-.0001f)continue;
                        b.hitVoxels.Add(hit.voxel);
                        float layer=hit.distance-b.firstHit<1-.0001f?1:b.multiplier;
                        float damage=hit.scenery.ApplyDamage(hit.voxel.position,b.damage*layer);
                        if(damage>0){b.owner.RecordDamageDealt(damage);ConfirmHit(b);EmitImpact(b,b.origin+b.direction*hit.distance);}
                        continue;
                    }
                    if(!hit.hitbox&&!hit.debris){EmitImpact(b,hit.collider.ClosestPoint(b.origin+b.direction*hit.distance));next=Mathf.Min(next,hit.distance);limit=next;break;}
                    var target=hit.hitbox;var wreck=hit.debris;var collider=hit.collider;
                    // A core hit can transfer the remaining contacts to wreckage in this very sweep.
                    if(target&&target.Part.detached){wreck=target.DetachedReplacement;if(!wreck)continue;target=null;collider=wreck.GetComponent<Collider>();}
                    var part=target?target.Part:wreck.Part;var identity=target?part:wreck.HitIdentity;
                    if(!part.present||(target&&!target.Ship.IsAlive)||b.hitParts.Contains(identity))continue;
                    if(b.firstHit<0){b.firstHit=hit.distance;limit=Mathf.Min(b.range,b.firstHit+b.depth);}
                    if(hit.distance>=limit-.0001f)continue;
                    float depth=hit.distance-b.firstHit,scale=depth<1-.0001f?1:b.multiplier;
                    b.hitParts.Add(identity);
                    EmitImpact(b,collider.ClosestPoint(b.origin+b.direction*hit.distance));
                    float dealt=target?target.Ship.ApplyDamage(part.record.id,b.damage*scale,b.owner,true):wreck.ApplyDamage(b.damage*scale);
                    if(dealt>0)ConfirmHit(b);
                }
                b.distance=Mathf.Min(next,limit);if(visuals)visuals.Move(b.visual,b.origin+b.direction*b.distance,b.direction);
                if(b.distance>=limit-.00001f)Release(i);
            }
            StepMissiles(dt);
            }
        }
        bool FirstContact(FlightShipRuntime owner,Vector3 origin,Vector3 direction,float length,float radius,out Contact hit)
        {
            hit=default;float nearest=length+.001f;bool found=false;
            foreach(var ship in shieldShips)if(ship&&ship.IsAlive&&ship!=owner)
                foreach(var field in ship.Shields.Fields)if(field.Active&&field.charge>0&&
                    ShipShieldSystem.TrySphereEntry(origin,direction,field.Center,field.definition.shieldRadius+radius,0,length,out float entry)&&entry<nearest){
                        nearest=entry;hit=new Contact{shield=field,distance=entry};found=true;
                    }
            queriedScenery.Clear();int count=Query(origin,origin+direction*length,radius);
            for(int i=0;i<count;i++){
                var c=candidates[i];if(!c)continue;
                var h=c.GetComponent<ShipPartHitbox>();if(h&&(h.Ship==owner||!h.Ship.IsAlive||!h.Part.present))continue;
                var wreck=c.GetComponent<DebrisPartHitbox>();if(wreck&&!wreck.Part.present)continue;
                var voxel=c.GetComponent<VoxelBodyHitbox>();
                if(voxel){
                    if(!queriedScenery.Add(voxel.Structure))continue;
                    voxelContacts.Clear();voxel.CollectHits(origin,direction,radius,0,length,geometry,voxelContacts);
                    foreach(var v in voxelContacts)if(v.cell.Alive&&v.distance<nearest){nearest=v.distance;hit=new Contact{scenery=v.structure,voxel=v.cell,distance=v.distance};found=true;}
                }else if(geometry.TryHit(c,origin,direction,radius,0,length,out float entry)&&entry<nearest){
                    nearest=entry;hit=new Contact{collider=c,hitbox=h,debris=wreck,distance=entry};found=true;
                }
            }
            return found;
        }
        void FireBeam(FlightShipRuntime owner,WeaponDefinition weapon,Vector3 position,Vector3 direction,Transform source,Vector3 localPosition)
        {
            float length=Mathf.Max(.1f,weapon.range),damage=Mathf.Max(0,weapon.damage);
            if(FirstContact(owner,position,direction,length,Mathf.Max(.001f,weapon.projectileDiameter*.5f),out var hit)){
                length=hit.distance;Vector3 point=position+direction*length;float dealt=0;
                if(hit.shield!=null)dealt=hit.shield.ship.Shields.Absorb(hit.shield,damage,point);
                else if(hit.scenery){dealt=hit.scenery.ApplyDamage(hit.voxel.position,damage);if(dealt>0)owner.RecordDamageDealt(dealt);}
                else if(hit.hitbox)dealt=hit.hitbox.Ship.ApplyDamage(hit.hitbox.Part.record.id,damage,owner,true);
                else if(hit.debris)dealt=hit.debris.ApplyDamage(damage);
                Impact?.Invoke(point);WeaponImpact?.Invoke(point,weapon);if(dealt>0)HitConfirmed?.Invoke(owner);
            }
            if(specialVisuals)specialVisuals.ShowBeam(weapon,position,direction,length,source,localPosition);
            TotalSpawned++;ShotFired?.Invoke(owner,position);
        }
        void FireMissile(FlightShipRuntime owner,WeaponDefinition weapon,Vector3 position,Vector3 direction,FlightShipRuntime target,float damageScale)
        {
            if(!target||!target.IsAlive||target==owner||target.Team==owner.Team)return;
            var missile=freeMissiles.Count>0?freeMissiles.Pop():new Missile();
            missile.owner=owner;missile.weapon=weapon;missile.damageScale=damageScale;missile.target=target;missile.targetLife=target.LifeId;
            missile.position=position;missile.direction=direction.normalized;missile.speed=Mathf.Max(.01f,weapon.missileLaunchSpeed);missile.distance=missile.age=0;
            missile.visual=specialVisuals?specialVisuals.AcquireMissile(weapon,position,missile.direction):-1;
            missiles.Add(missile);TotalSpawned++;ShotFired?.Invoke(owner,position);
        }
        void StepMissiles(float dt)
        {
            for(int i=missiles.Count-1;i>=0;i--){
                var m=missiles[i];if(!m.owner||!m.owner.IsAlive){ReleaseMissile(i);continue;}
                m.age+=dt;
                // The launch ejects along the barrel before the seeker begins turning.
                if(m.age>.12f&&m.target&&m.target.IsAlive&&m.target.LifeId==m.targetLife){
                    Vector3 target=m.target.CorePhysicsPosition;
                    var body=m.target.GetComponent<Rigidbody>();Vector3 velocity=body?body.linearVelocity:Vector3.zero;
                    float lead=Mathf.Min(1.2f,Vector3.Distance(m.position,target)/Mathf.Max(1,m.speed));
                    Vector3 desired=target+velocity*lead-m.position;
                    if(desired.sqrMagnitude>.00001f)m.direction=Vector3.RotateTowards(m.direction,desired.normalized,Mathf.Max(0,m.weapon.missileTurnDegrees)*Mathf.Deg2Rad*dt,0).normalized;
                }
                m.speed=Mathf.MoveTowards(m.speed,Mathf.Max(.01f,m.weapon.projectileSpeed),Mathf.Max(0,m.weapon.missileAcceleration)*dt);
                float travel=Mathf.Min(m.speed*dt,Mathf.Max(0,m.weapon.range-m.distance));
                if(FirstContact(m.owner,m.position,m.direction,travel,Mathf.Max(.025f,m.weapon.projectileDiameter*.5f),out var hit)){
                    Vector3 point=m.position+m.direction*hit.distance;Explode(m,point);ReleaseMissile(i);continue;
                }
                m.position+=m.direction*travel;m.distance+=travel;
                if(specialVisuals)specialVisuals.MoveMissile(m.visual,m.position,m.direction,m.distance);
                if(m.distance>=m.weapon.range-.0001f||m.age>60){ReleaseMissile(i);}
            }
        }
        void Explode(Missile missile,Vector3 point)
        {
            var owner=missile.owner;var weapon=missile.weapon;
            float radius=Mathf.Max(.1f,weapon.explosionRadius),maximum=Mathf.Max(0,weapon.damage)*missile.damageScale,budget=maximum*3;
            bool confirmed=false;Impact?.Invoke(point);MissileExplosion?.Invoke(point,weapon);
            blastShieldRatios.Clear();
            // Charge each enclosing shield once per blast. An intact shield protects its whole ship.
            foreach(var ship in shieldShips)if(ship&&ship.IsAlive&&ship!=owner){
                float remaining=maximum;bool protectedByField=false;
                foreach(var field in ship.Shields.Fields)if(field.Active&&field.charge>0){
                    float surface=Mathf.Max(0,Vector3.Distance(point,field.Center)-field.definition.shieldRadius);
                    if(surface>radius)continue;
                    protectedByField=true;
                    float absorbed=ship.Shields.Absorb(field,remaining,point);remaining-=absorbed;budget-=absorbed;
                    if(absorbed>0)confirmed=true;
                    if(remaining<=.00001f)break;
                }
                if(protectedByField)blastShieldRatios[ship]=maximum>0?Mathf.Clamp01(remaining/maximum):0;
            }
            int count=Query(point,point,radius);
            contacts.Clear();blastParts.Clear();blastStructures.Clear();
            for(int i=0;i<count;i++){
                var c=candidates[i];if(!c)continue;
                var h=c.GetComponent<ShipPartHitbox>();var d=c.GetComponent<DebrisPartHitbox>();
                if(h&&(h.Ship==owner||!h.Ship.IsAlive||!h.Part.present))continue;
                if(d&&!d.Part.present)continue;
                var voxel=c.GetComponent<VoxelBodyHitbox>();
                if(voxel){blastStructures.Add(voxel.Structure);continue;}
                if(!h&&!d)continue;
                var identity=h?h.Part:d.HitIdentity;if(!blastParts.Add(identity))continue;
                float distance=Vector3.Distance(c.ClosestPoint(point),point);
                if(distance<=radius)contacts.Add(new Contact{collider=c,hitbox=h,debris=d,distance=distance});
            }
            contacts.Sort((a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in contacts){
                if(budget<=.00001f)break;
                float falloff=1-Mathf.Clamp01(hit.distance/radius);
                float amount=Mathf.Min(budget,maximum*falloff*falloff);
                var h=hit.hitbox;var d=hit.debris;
                if(h&&blastShieldRatios.TryGetValue(h.Ship,out float ratio))amount*=ratio;
                if(h&&h.Part.detached){d=h.DetachedReplacement;h=null;}
                float dealt=h&&h.Part.present&&h.Ship.IsAlive?h.Ship.ApplyDamage(h.Part.record.id,amount,owner,true):d&&d.Part.present?d.ApplyDamage(amount):0;
                budget-=dealt;if(dealt>0)confirmed=true;
            }
            foreach(var structure in blastStructures){
                if(!structure||budget<=.00001f)continue;
                // A blast is infrequent; copy the local candidates before fragmentation mutates ownership.
                var affected=new List<VoxelCellState>();
                float cell=structure.asset.cellSize;
                foreach(var pair in structure.Cells)if(pair.Value.Alive&&Vector3.Distance(structure.PhysicsMatrix.MultiplyPoint3x4((Vector3)pair.Key*cell),point)<radius+cell*.5f)affected.Add(pair.Value);
                foreach(var state in affected){
                    if(!structure||budget<=.00001f)break;
                    float distance=Mathf.Max(0,Vector3.Distance(structure.PhysicsMatrix.MultiplyPoint3x4((Vector3)state.position*cell),point)-cell*.5f);
                    float falloff=1-Mathf.Clamp01(distance/radius);float dealt=structure.ApplyDamage(state.position,Mathf.Min(budget,maximum*falloff*falloff));
                    budget-=dealt;if(dealt>0){confirmed=true;owner.RecordDamageDealt(dealt);}
                }
            }
            if(confirmed)HitConfirmed?.Invoke(owner);
        }
        void ReleaseMissile(int index)
        {
            var m=missiles[index];if(specialVisuals)specialVisuals.ReleaseMissile(m.visual);
            m.owner=m.target=null;missiles[index]=missiles[missiles.Count-1];missiles.RemoveAt(missiles.Count-1);freeMissiles.Push(m);
        }
        void EmitImpact(Bolt b,Vector3 position){Impact?.Invoke(position);WeaponImpact?.Invoke(position,b.weapon);}
        void ConfirmHit(Bolt bolt)
        {
            if(bolt.hitConfirmed)return;
            bolt.hitConfirmed=true;HitConfirmed?.Invoke(bolt.owner);
        }
        void Release(int index){var b=bolts[index];if(visuals)visuals.Release(b.visual);b.owner=null;b.hitParts.Clear();b.hitVoxels.Clear();bolts[index]=bolts[bolts.Count-1];bolts.RemoveAt(bolts.Count-1);free.Push(b);}
        public void RemoveOwned(FlightShipRuntime owner){for(int i=bolts.Count-1;i>=0;i--)if(bolts[i].owner==owner)Release(i);for(int i=missiles.Count-1;i>=0;i--)if(missiles[i].owner==owner)ReleaseMissile(i);}
        public void Clear(){for(int i=bolts.Count-1;i>=0;i--)Release(i);for(int i=missiles.Count-1;i>=0;i--)ReleaseMissile(i);shieldSamples.Clear();if(visuals)visuals.Clear();if(specialVisuals)specialVisuals.Clear();}
    }
}
