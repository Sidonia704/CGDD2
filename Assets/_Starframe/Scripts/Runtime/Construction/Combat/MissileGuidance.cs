// @role Steer finite visible-target missile seekers with bounded turns, braking through cone grace before permanent lock loss.
// @calls WeaponDefinition, InterceptSolver

using UnityEngine;
using Starframe.Flight;

namespace Starframe.Combat
{
    public sealed class MissileGuidance
    {
        public const float EjectionSeconds=.12f;
        public Vector3 Direction { get; private set; }=Vector3.forward;
        public Vector3 AngularVelocityDegrees { get; private set; }
        public bool IsTracking { get; private set; }
        public float OutsideConeSeconds { get; private set; }

        public void Reset(Vector3 direction)
        {
            Direction=direction.sqrMagnitude>.000001f?direction.normalized:Vector3.forward;
            AngularVelocityDegrees=Vector3.zero;OutsideConeSeconds=0;IsTracking=true;
        }

        public void LoseLock()
        {
            IsTracking=false;AngularVelocityDegrees=Vector3.zero;
        }

        public bool InSeekerCone(Vector3 offset,WeaponDefinition weapon)
        {
            return offset.sqrMagnitude<=.000001f||Vector3.Dot(Direction,offset.normalized)>=
                Mathf.Cos(Mathf.Clamp(weapon.missileSeekerConeDegrees,1,179)*Mathf.Deg2Rad);
        }

        public void Step(WeaponDefinition weapon,float age,Vector3 targetOffset,Vector3 targetVelocity,float speed,float dt)
        {
            if(!IsTracking||!weapon||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            if(age>=Mathf.Max(0,weapon.missileGuidanceSeconds)){LoseLock();return;}
            if(age<=EjectionSeconds)return;
            dt=Mathf.Min(dt,age-EjectionSeconds);
            // Cone loss is measured against the target itself, not the velocity lead point.
            OutsideConeSeconds=InSeekerCone(targetOffset,weapon)?0:OutsideConeSeconds+dt;
            if(OutsideConeSeconds>0&&OutsideConeSeconds+.000001f>=Mathf.Max(0,weapon.missileSeekerLossSeconds)){
                LoseLock();return;
            }
            float acceleration=Mathf.Max(0,weapon.missileTurnAcceleration);
            // Outside the field of view the seeker has no fresh bearing or velocity to chase.
            // Brief grace preserves angular momentum, then loss is permanent; it cannot turn
            // toward an unseen ship and repeatedly rescue an overshoot with perfect knowledge.
            if(OutsideConeSeconds>0){Turn(Vector3.zero,acceleration,dt);return;}
            var desired=InterceptOffset(weapon,age,targetOffset,targetVelocity,speed);
            if(desired.sqrMagnitude<=.000001f)return;
            desired.Normalize();float angle=Vector3.Angle(Direction,desired);
            var axis=Vector3.Cross(Direction,desired);
            if(axis.sqrMagnitude<=.000001f&&angle>90)
                axis=Vector3.Cross(Direction,Mathf.Abs(Direction.y)<.9f?Vector3.up:Vector3.right);
            // Start braking before alignment. A changing turn axis must accelerate too.
            float maximumRate=Mathf.Max(0,weapon.missileTurnDegrees);
            // The linear capture region brakes earlier than a pure bang-bang stopping law.
            // Small, changing lead errors therefore converge without repeatedly reversing at full rate.
            float response=Mathf.Clamp(acceleration/Mathf.Max(1,maximumRate)*1.5f,3,8);
            // Tighten capture as physical time-to-go shrinks, while keeping the same hard turn limits.
            float distance=Mathf.Max(1,targetOffset.magnitude);
            float closing=Mathf.Max(0,Vector3.Dot(Direction*Mathf.Max(0,speed)-targetVelocity,targetOffset/distance));
            response=Mathf.Max(response,Mathf.Min(12,2*closing/distance));
            float rate=Mathf.Min(maximumRate,Mathf.Min(Mathf.Sqrt(2*acceleration*angle),angle*response));
            var requested=axis.sqrMagnitude>.000001f?axis.normalized*rate:Vector3.zero;
            Turn(requested,acceleration,dt);
        }
        void Turn(Vector3 requested,float acceleration,float dt)
        {
            var previous=AngularVelocityDegrees;
            AngularVelocityDegrees=Vector3.MoveTowards(previous,requested,acceleration*dt);
            var average=(previous+AngularVelocityDegrees)*.5f;
            float rotation=average.magnitude*dt;
            if(rotation>.000001f)Direction=(Quaternion.AngleAxis(rotation,average.normalized)*Direction).normalized;
        }
        static Vector3 InterceptOffset(WeaponDefinition weapon,float age,Vector3 offset,Vector3 targetVelocity,float speed)
        {
            float horizon=Mathf.Max(0,weapon.missileGuidanceSeconds-age);
            if(horizon<=.0001f||targetVelocity.sqrMagnitude<.000001f)return offset;
            float launchSpeed=Mathf.Max(.01f,speed),cruise=Mathf.Max(.01f,weapon.projectileSpeed);
            float acceleration=Mathf.Max(0,weapon.missileAcceleration);
            float estimate=Mathf.Min(horizon,offset.magnitude/Mathf.Max(1,launchSpeed));
            Vector3 aim=offset;
            // Two scalar refinements account for powered acceleration without iterating the world or allocating.
            for(int i=0;i<2;i++)
            {
                float effective=launchSpeed;
                if(acceleration>.0001f&&estimate>.0001f&&cruise>launchSpeed)
                {
                    float powered=Mathf.Min(estimate,(cruise-launchSpeed)/acceleration);
                    float distance=launchSpeed*estimate+.5f*acceleration*powered*powered+
                        acceleration*powered*(estimate-powered);
                    effective=distance/estimate;
                }
                if(!InterceptSolver.TrySolve(Vector3.zero,offset,targetVelocity,effective,
                    Mathf.Min(Mathf.Max(1,weapon.range),effective*horizon),out var intercept,out float time))
                    return offset; // An outrunning target remains a pursuit, never an impossible lead or auto-hit.
                aim=intercept;estimate=time;
            }
            return aim;
        }
    }
}
