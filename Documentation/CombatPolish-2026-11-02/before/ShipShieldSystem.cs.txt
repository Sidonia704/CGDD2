// @role Maintain component-owned shield fields and power-dependent recovery.
// @calls FlightShipRuntime

using System;
using System.Collections.Generic;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Combat
{
    // Shield health belongs to its generator, never to the ship's structural health.
    public sealed class ShipShieldSystem
    {
        public sealed class Field
        {
            public FlightShipRuntime ship;
            public ShipPartState part;
            public ModuleDefinition definition;
            public float charge, preparedRecovery;
            public double lastHit=double.NegativeInfinity;
            public Vector3 lastHitPosition;
            public bool Active=>ship&&ship.IsAlive&&part.present&&part.hp>0&&definition&&definition.shieldCapacity>0;
            public Vector3 Center
            {
                get
                {
                    if(!part.view)return ship.transform.position;
                    var collider=part.view.GetComponent<Collider>();
                    return collider?CylindricalHitQuery.PhysicsMatrix(collider).MultiplyPoint3x4(Vector3.zero):part.view.transform.position;
                }
            }
        }
        readonly List<Field> fields=new List<Field>();
        public IReadOnlyList<Field> Fields=>fields;
        public float Capacity {get{float v=0;foreach(var f in fields)if(f.Active)v+=f.definition.shieldCapacity;return v;}}
        public float Charge {get{float v=0;foreach(var f in fields)if(f.Active)v+=f.charge;return v;}}

        public void Configure(FlightShipRuntime ship,bool fill=false)
        {
            if(fields.Count>0&&fields[0].ship!=ship)fields.Clear();
            foreach(var part in ship.Parts)
            {
                var d=ship.Tuning.GetDefinition(part.record.shape);
                if(!d||d.shieldCapacity<=0)continue;
                var f=fields.Find(x=>ReferenceEquals(x.part,part));
                if(f==null){f=new Field{ship=ship,part=part,definition=d,charge=d.shieldCapacity};fields.Add(f);}
                f.definition=d;f.charge=fill?d.shieldCapacity:Mathf.Clamp(f.charge,0,d.shieldCapacity);
                if(fill){f.lastHit=double.NegativeInfinity;f.preparedRecovery=0;}
            }
            fields.RemoveAll(f=>!ship.Parts.Contains(f.part)||!f.definition||f.definition.shieldCapacity<=0);
        }
        public void PartRemoved(ShipPartState part)
        {
            foreach(var f in fields)if(ReferenceEquals(f.part,part))
            {f.charge=0;f.preparedRecovery=0;f.lastHit=f.ship.CombatTime;}
        }
        public float PrepareRecovery(float dt)
        {
            float demand=0;
            foreach(var f in fields)
            {
                f.preparedRecovery=0;if(!f.Active||dt<=0)continue;
                float activeDt=(float)Math.Min(dt,Math.Max(0,f.ship.CombatTime-f.lastHit-f.definition.shieldRecoveryDelay));
                float rate=Mathf.Max(0,f.definition.shieldRecoveryPerSecond);
                f.preparedRecovery=Mathf.Min(Mathf.Max(0,f.definition.shieldCapacity-f.charge),rate*activeDt);
                if(rate>0)demand+=Mathf.Max(0,f.definition.shieldPowerWatts)*f.preparedRecovery/(rate*dt);
            }
            return demand;
        }
        public void ExecuteRecovery(float powerRatio)
        {
            foreach(var f in fields){if(f.Active)f.charge=Mathf.Min(f.definition.shieldCapacity,f.charge+f.preparedRecovery*Mathf.Clamp01(powerRatio));f.preparedRecovery=0;}
        }
        public float Absorb(Field field,float damage,Vector3 point)
        {
            if(!field.Active||damage<=0||field.charge<=0)return 0;
            float absorbed=Mathf.Min(damage,field.charge);field.charge-=absorbed;
            field.lastHit=field.ship.CombatTime;field.lastHitPosition=point;
            return absorbed;
        }
        // Test the swept projectile cylinder against a spherical field analytically.
        // There are no physical shield colliders to interfere with building, ships or outgoing shots.
        public static bool TrySphereEntry(Vector3 origin,Vector3 direction,Vector3 center,float radius,float min,float max,out float distance)
        {
            distance=0;if(max<min||radius<=0||direction.sqrMagnitude<.000001f)return false;
            Vector3 delta=origin-center,atStart=delta+direction*min;
            if(atStart.sqrMagnitude<=radius*radius){distance=min;return true;}
            float a=Vector3.Dot(direction,direction),b=Vector3.Dot(delta,direction),c=delta.sqrMagnitude-radius*radius;
            float discriminant=b*b-a*c;if(discriminant<0)return false;
            float entry=(-b-Mathf.Sqrt(discriminant))/a;
            if(entry<min-.00001f||entry>max+.00001f)return false;
            distance=Mathf.Clamp(entry,min,max);return true;
        }
    }
}
