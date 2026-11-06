// @role Compensate bounded player muzzle parallax for shared projectile leads and direct reticle hull hits without removing aim error.
// @calls ShipPartHitbox, FlightShipRuntime, InterceptSolver

using System;
using UnityEngine;
using Starframe.Flight;
namespace Starframe.Combat
{
    /// <summary>Correct installation parallax using shared projectile leads or a direct reticle hull hit.</summary>
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
        public static bool TryCompensateLead(Vector3 referenceOrigin,Vector3 muzzle,Vector3 forward,Vector3 target,Vector3 targetVelocity,float speed,float range,out Vector3 corrected)
        {
            corrected=forward;
            float magnitude=forward.sqrMagnitude;
            if(magnitude<.000001f||float.IsNaN(magnitude)||float.IsInfinity(magnitude)||
                !InterceptSolver.TrySolve(referenceOrigin,target,targetVelocity,speed,range,out var referencePoint,out _)||
                !InterceptSolver.TrySolve(muzzle,target,targetVelocity,speed,range,out var muzzlePoint,out _))return false;
            Vector3 referenceDirection=referencePoint-referenceOrigin,muzzleDirection=muzzlePoint-muzzle;
            if(referenceDirection.sqrMagnitude<.000001f||muzzleDirection.sqrMagnitude<.000001f||Vector3.Dot(forward,referenceDirection)<=0||Vector3.Dot(forward,muzzleDirection)<=0)return false;
            // Apply only the installation error, not the player's error relative to the lead.
            // Re-solve each real muzzle's travel time; fore/aft offsets matter against moving targets too.
            referenceDirection.Normalize();muzzleDirection.Normalize();
            var axis=Vector3.Cross(referenceDirection,muzzleDirection);float sine=axis.magnitude;
            // FromToRotation may collapse sub-tenth-degree differences to identity at long ranges.
            // atan2 retains small fore/aft time-of-flight corrections instead of silently dropping them.
            float degrees=Mathf.Atan2(sine,Mathf.Clamp(Vector3.Dot(referenceDirection,muzzleDirection),-1,1))*Mathf.Rad2Deg;
            corrected=sine>1e-10f?(Quaternion.AngleAxis(Mathf.Min(MaximumCorrectionDegrees,degrees),axis/sine)*forward.normalized).normalized:forward.normalized;
            return true;
        }
    }
}
