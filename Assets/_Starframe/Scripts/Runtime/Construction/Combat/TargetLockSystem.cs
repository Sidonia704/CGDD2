// @role Select valid range targets and update velocity-based lead points.
// @calls RangeCombatWorld, InterceptSolver, CoreVelocityEstimator, ShipWeapons

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Starframe.Flight;
namespace Starframe.Combat
{
    public struct WeaponAimProfile{public WeaponDefinition weapon;public Vector3 origin,direction;public string partId;public int gunCount;}
    public struct LeadPoint{public WeaponDefinition weapon;public Vector3 point,aimDirection;public float flightTime;}
    [DefaultExecutionOrder(75)]
    public sealed class TargetLockSystem:MonoBehaviour
    {
        RangeCombatWorld world;Camera viewCamera;RangeHUD hud;BattleTuning tuning;RangeCombatWorld.Actor target;int life;
        readonly List<WeaponAimProfile> profiles=new List<WeaponAimProfile>();readonly List<LeadPoint> points=new List<LeadPoint>();
        readonly CoreVelocityEstimator coreMotion=new CoreVelocityEstimator();
        public FlightShipRuntime LockedTarget=>target?.ship;
        public bool HasTarget=>target!=null&&target.ship&&target.ship.IsAlive&&target.ship.LifeId==life;
        public IReadOnlyList<LeadPoint> LeadPoints=>points;
        public Vector3 EstimatedCoreVelocity=>coreMotion.Velocity;
        public float LockRange=>tuning?Mathf.Max(1,tuning.lockRange):3000;
        public float TargetDistance=>HasTarget&&world?Vector3.Distance(world.Player.ship.CorePhysicsPosition,target.ship.CorePhysicsPosition):0;
        public void Initialize(RangeCombatWorld combat,Camera view,RangeHUD display,BattleTuning rules){world=combat;viewCamera=view;hud=display;tuning=rules;}
        void Update(){ValidateTarget();if(world&&world.Player.ship.IsAlive&&Application.isFocused&&(!hud||!hud.ExitDialogOpen)&&Starframe.App.GameInput.Pressed(Starframe.App.GameAction.TargetLock))ToggleLock();}
        public void Clear(){target=null;points.Clear();coreMotion.Clear();}
        void ValidateTarget(){if(!world||!world.Player.ship.IsAlive||!HasTarget||TargetDistance>LockRange)Clear();}
        public void ToggleLock()
        {
            ValidateTarget();
            if(!world||!world.Player.ship.IsAlive)return;
            if(HasTarget){Clear();return;}
            var owner=world.Player;Vector3 position=owner.ship.CorePhysicsPosition,forward=owner.body.rotation*Vector3.forward;
            float best=-2,bestDistance=float.PositiveInfinity,cone=Mathf.Cos(tuning.lockConeDegrees*Mathf.Deg2Rad);
            foreach(var actor in world.Actors){
                if(actor==owner||!actor.ship.IsAlive||actor.ship.Team==owner.ship.Team)continue;
                var offset=actor.ship.CorePhysicsPosition-position;float distance=offset.magnitude;if(distance<.001f||distance>LockRange)continue;
                float facing=Vector3.Dot(forward,offset/distance);var screen=viewCamera.WorldToViewportPoint(actor.ship.CorePosition);
                if(facing<cone||screen.z<=0||screen.x<0||screen.x>1||screen.y<0||screen.y>1)continue;
                if(facing>best+.0001f||(Mathf.Abs(facing-best)<.0001f&&distance<bestDistance)){target=actor;best=facing;bestDistance=distance;}
            }
            if(target!=null){life=target.ship.LifeId;coreMotion.Reset(target.ship.CorePhysicsPosition,Time.fixedTimeAsDouble,target.flight.Velocity);}RefreshLeadPoints();
        }
        void LateUpdate()=>RefreshLeadPoints();
        public void RefreshLeadPoints()
        {
            ValidateTarget();points.Clear();if(!HasTarget)return;
            var core=target.ship.CorePhysicsPosition;
            coreMotion.Sample(core,Time.fixedTimeAsDouble,tuning.leadVelocitySampleWindow,tuning.leadVelocityResponse,target.flight.Velocity);
            world.Player.weapons.GetAimProfiles(profiles);
            foreach(var profile in profiles)if(InterceptSolver.TrySolve(profile.origin,core,coreMotion.Velocity,profile.weapon.projectileSpeed,profile.weapon.range,out var point,out float time))points.Add(new LeadPoint{weapon=profile.weapon,point=point,aimDirection=(point-profile.origin).normalized,flightTime=time});
        }
    }
}
