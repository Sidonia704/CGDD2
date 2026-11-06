// @role Resolve player reticle hull hits and bounded muzzle-parallax convergence without target acquisition.
// @calls ShipPartHitbox, FlightShipRuntime

using System;
using UnityEngine;
using Starframe.Flight;
namespace Starframe.Combat
{
    /// <summary>Correct installation parallax only after the reticle ray actually hits an enemy hull.</summary>
    public sealed class DirectAimAssist
    {
        public const float MaximumCorrectionDegrees=4f;
        RaycastHit[] hits=new RaycastHit[64];
        public bool TryGetPoint(FlightShipRuntime owner,Ray ray,out Vector3 point)
        {
            point=default;
            if(!owner||!owner.IsAlive||owner.Team!=0||ray.direction.sqrMagnitude<.000001f)return false;
            int count;
            do
            {
                count=Physics.RaycastNonAlloc(ray,hits,100000f,~(1<<2|1<<5),QueryTriggerInteraction.Ignore);
                if(count<hits.Length)break;
                Array.Resize(ref hits,hits.Length*2);
            }while(true);
            float nearest=float.PositiveInfinity;ShipPartHitbox target=null;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];if(!hit.collider)continue;
                var part=hit.collider.GetComponent<ShipPartHitbox>();
                if(part&&part.Ship==owner)continue;
                if(hit.distance>=nearest)continue;
                nearest=hit.distance;target=part;point=hit.point;
            }
            return target&&target.Ship&&target.Ship.IsAlive&&target.Ship.Team!=owner.Team&&target.Part!=null&&target.Part.present;
        }
        public static Vector3 Converge(Vector3 muzzle,Vector3 forward,Vector3 point,float range)
        {
            var offset=point-muzzle;
            if(offset.sqrMagnitude<.000001f||offset.sqrMagnitude>range*range||Vector3.Dot(offset,forward)<=0)return forward;
            // Do not bend only part-way toward an off-axis ship: outside the small parallax allowance, fire normally.
            return Vector3.Angle(forward,offset)<=MaximumCorrectionDegrees?offset.normalized:forward;
        }
    }
}
