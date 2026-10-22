// @role Advance projectiles and resolve shields, hull penetration and wreckage hits.
// @calls CylindricalHitQuery, ShipShieldSystem, FlightShipRuntime, DebrisPartHitbox, LaserVisualPool

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Starframe.Flight;
namespace Starframe.Combat
{
    public sealed class ProjectileWorld
    {
        sealed class Bolt
        {
            public FlightShipRuntime owner;public Vector3 origin,direction;
            public float distance,speed,radius,damage,range,depth,multiplier,firstHit=-1;
            public int visual=-1;public bool hitConfirmed;public readonly HashSet<ShipPartState> hitParts=new HashSet<ShipPartState>();
        }
        struct Contact{public Collider collider;public ShipPartHitbox hitbox;public DebrisPartHitbox debris;public ShipShieldSystem.Field shield;public float distance;}
        struct ShieldSample{public ShipShieldSystem.Field field;public Vector3 center;public float radius;}
        static readonly ProfilerMarker Marker=new ProfilerMarker("Starframe.Projectiles");
        readonly List<Bolt> bolts=new List<Bolt>();readonly Stack<Bolt> free=new Stack<Bolt>();
        readonly List<Contact> contacts=new List<Contact>();readonly CylindricalHitQuery geometry=new CylindricalHitQuery();
        readonly List<FlightShipRuntime> shieldShips=new List<FlightShipRuntime>();
        readonly List<ShieldSample> shieldSamples=new List<ShieldSample>();
        Collider[] candidates=new Collider[64];LaserVisualPool visuals;
        public int ActiveCount=>bolts.Count;
        public int TotalSpawned{get;private set;}
        public bool UseShieldGeometryCache{get;set;}=true;
        public event Action<Vector3> Impact;
        public event Action<FlightShipRuntime,Vector3> ShotFired;
        // One confirmation per projectile, only after a damageable surface actually absorbs damage.
        public event Action<FlightShipRuntime> HitConfirmed;
        public void Initialize(LaserVisualPool pool)=>visuals=pool;
        public void RegisterShields(FlightShipRuntime ship){if(ship&&!shieldShips.Contains(ship))shieldShips.Add(ship);}
        public void Fire(FlightShipRuntime owner,WeaponDefinition weapon,Vector3 position,Vector3 direction,Transform muzzleSource=null,Vector3 muzzleLocalPosition=default)
        {
            if(!owner||!owner.IsAlive||!weapon||direction.sqrMagnitude<.000001f)return;
            var b=free.Count>0?free.Pop():new Bolt();b.owner=owner;b.origin=position;b.direction=direction.normalized;b.distance=0;b.firstHit=-1;b.hitConfirmed=false;b.hitParts.Clear();
            b.speed=Mathf.Max(.01f,weapon.projectileSpeed);b.radius=Mathf.Max(.001f,weapon.projectileDiameter*.5f);b.damage=Mathf.Max(0,weapon.damage);b.range=Mathf.Max(.1f,weapon.range);b.depth=Mathf.Max(.001f,weapon.penetrationDepth);b.multiplier=Mathf.Clamp01(weapon.secondLayerDamageMultiplier);
            b.visual=visuals?visuals.Acquire(position,direction,weapon.projectileDiameter,weapon.visualLength,weapon.tailLength,muzzleSource,muzzleLocalPosition,weapon.visualWidth,weapon.visualIntensity):-1;bolts.Add(b);TotalSpawned++;
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
            if(dt<=0||bolts.Count==0)return;
            using(Marker.Auto()){
            // Physics poses are fixed for this step; shield health is still checked live for every shot.
            shieldSamples.Clear();
            foreach(var ship in shieldShips)if(ship&&ship.IsAlive)
                foreach(var field in ship.Shields.Fields)if(field.Active&&field.charge>0)
                    shieldSamples.Add(new ShieldSample{field=field,center=UseShieldGeometryCache?field.Center:default,radius=field.definition.shieldRadius});
            for(int i=bolts.Count-1;i>=0;i--){
                var b=bolts[i];if(!b.owner||!b.owner.IsAlive){Release(i);continue;}
                float limit=b.firstHit<0?b.range:Mathf.Min(b.range,b.firstHit+b.depth);
                float next=Mathf.Min(limit,b.distance+b.speed*dt);contacts.Clear();
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
                        Impact?.Invoke(point);b.damage=Mathf.Max(0,b.damage-absorbed);
                        if(b.damage<=.00001f){next=Mathf.Min(next,hit.distance);limit=next;break;}
                        continue;
                    }
                    if(!hit.hitbox&&!hit.debris){Impact?.Invoke(hit.collider.ClosestPoint(b.origin+b.direction*hit.distance));next=Mathf.Min(next,hit.distance);limit=next;break;}
                    var target=hit.hitbox;var wreck=hit.debris;var collider=hit.collider;
                    // A core hit can transfer the remaining contacts to wreckage in this very sweep.
                    if(target&&target.Part.detached){wreck=target.DetachedReplacement;if(!wreck)continue;target=null;collider=wreck.GetComponent<Collider>();}
                    var part=target?target.Part:wreck.Part;var identity=target?part:wreck.HitIdentity;
                    if(!part.present||(target&&!target.Ship.IsAlive)||b.hitParts.Contains(identity))continue;
                    if(b.firstHit<0){b.firstHit=hit.distance;limit=Mathf.Min(b.range,b.firstHit+b.depth);}
                    if(hit.distance>=limit-.0001f)continue;
                    float depth=hit.distance-b.firstHit,scale=depth<1-.0001f?1:b.multiplier;
                    b.hitParts.Add(identity);
                    Impact?.Invoke(collider.ClosestPoint(b.origin+b.direction*hit.distance));
                    float dealt=target?target.Ship.ApplyDamage(part.record.id,b.damage*scale,b.owner,true):wreck.ApplyDamage(b.damage*scale);
                    if(dealt>0)ConfirmHit(b);
                }
                b.distance=Mathf.Min(next,limit);if(visuals)visuals.Move(b.visual,b.origin+b.direction*b.distance,b.direction);
                if(b.distance>=limit-.00001f)Release(i);
            }
            }
        }
        void ConfirmHit(Bolt bolt)
        {
            if(bolt.hitConfirmed)return;
            bolt.hitConfirmed=true;HitConfirmed?.Invoke(bolt.owner);
        }
        void Release(int index){var b=bolts[index];if(visuals)visuals.Release(b.visual);b.owner=null;b.hitParts.Clear();bolts[index]=bolts[bolts.Count-1];bolts.RemoveAt(bolts.Count-1);free.Push(b);}
        public void RemoveOwned(FlightShipRuntime owner){for(int i=bolts.Count-1;i>=0;i--)if(bolts[i].owner==owner)Release(i);}
        public void Clear(){for(int i=bolts.Count-1;i>=0;i--)Release(i);shieldSamples.Clear();if(visuals)visuals.Clear();}
    }
}
