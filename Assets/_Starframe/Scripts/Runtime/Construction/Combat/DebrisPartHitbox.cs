// @role Apply projectile damage to detached wreckage parts.
// @calls DebrisCluster

using UnityEngine;
using Starframe.Flight;
namespace Starframe.Combat
{
    public sealed class DebrisPartHitbox:MonoBehaviour
    {
        public DebrisCluster Cluster{get;internal set;}
        public ShipPartState Part{get;internal set;}
        public ShipPartState HitIdentity{get;internal set;}
        public float MassKg{get;internal set;}
        public float ApplyDamage(float amount)
        {
            if(!Cluster||Part==null||!Part.present||amount<=0||float.IsNaN(amount)||float.IsInfinity(amount))return 0;
            float damage=Mathf.Min(amount,Part.hp);Part.hp-=damage;
            if(Part.hp<=0){Part.present=false;Cluster.RemovePart(this);}
            return damage;
        }
    }
}
