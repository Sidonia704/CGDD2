// @role Deploy ready clear surviving launchers with independent cooldowns; forecast real moving burst reach for AI threats.
// @calls GameInput, FlightShipRuntime, FlareWorld, WeaponClearance, CylindricalHitQuery

using System.Collections.Generic;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;

namespace Starframe.Combat
{
    public sealed class ShipCountermeasureController:MonoBehaviour
    {
        readonly Dictionary<ShipPartState,double> readyAt=new Dictionary<ShipPartState,double>();
        FlightShipRuntime ship;
        FlareWorld world;
        RangeHUD hud;
        ShipFlightController flight;
        Rigidbody body;
        int nextLauncher,lifeId;
        public int LauncherCount{get{RefreshLife();int count=0;if(ship)foreach(var part in ship.Parts)if(Definition(part))count++;return count;}}
        public int ReadyCount{get{RefreshLife();int count=0;if(ship)foreach(var part in ship.Parts)if(Definition(part)&&Remaining(part)<=0)count++;return count;}}
        public float NextReadySeconds{get{RefreshLife();float minimum=float.PositiveInfinity;if(ship)foreach(var part in ship.Parts)if(Definition(part))minimum=Mathf.Min(minimum,Remaining(part));return float.IsPositiveInfinity(minimum)?0:minimum;}}

        public void Initialize(FlightShipRuntime owner,FlareWorld flareWorld,RangeHUD display=null)
        {
            ship=owner;world=flareWorld;hud=display;flight=ship?ship.GetComponent<ShipFlightController>():null;body=ship?ship.GetComponent<Rigidbody>():null;
            readyAt.Clear();nextLauncher=0;lifeId=ship?ship.LifeId:0;
        }
        void RefreshLife()
        {
            if(!ship||lifeId==ship.LifeId)return;
            lifeId=ship.LifeId;readyAt.Clear();nextLauncher=0;
        }
        FlareDefinition Definition(ShipPartState part)
        {
            if(!ship||!ship.Tuning||part==null||!part.present||part.hp<=0)return null;
            var definition=ship.Tuning.GetDefinition(part.record.shape);return definition?definition.countermeasure:null;
        }
        float Remaining(ShipPartState part)=>readyAt.TryGetValue(part,out var time)?(float)System.Math.Max(0,time-ship.CombatTime):0;
        public float GetCooldownRemaining(string partId)
        {
            RefreshLife();if(ship)foreach(var part in ship.Parts)if(part.record.id==partId)return Remaining(part);return 0;
        }
        void Update()
        {
            if(!ship||ship.Team!=0||!ship.IsAlive||!Application.isFocused||GameInput.Suspended||Time.timeScale<=0||(hud&&hud.ExitDialogOpen))return;
            if(GameInput.Pressed(GameAction.Countermeasure))TryDeploy();
        }

        public bool TryDeploy()=>TryDeploy(null,Vector3.zero,Vector3.zero,0);

        // Short linear forecast schedules a burst before a fast missile enters its radius.
        // This uses the same muzzle, inherited velocity and mean ejection cone as EmitBurst.
        public bool TryDeployForThreat(Vector3 missilePosition,Vector3 missileVelocity,WeaponDefinition missile,float timeToImpact)
        {
            if(!missile||timeToImpact<=0)return false;
            return TryDeploy(missile,missilePosition,missileVelocity,timeToImpact);
        }
        bool TryDeploy(WeaponDefinition missile,Vector3 missilePosition,Vector3 missileVelocity,float timeToImpact)
        {
            RefreshLife();if(!ship||!ship.IsAlive||!world)return false;
            for(int offset=0;offset<ship.Parts.Count;offset++)
            {
                int index=(nextLauncher+offset)%ship.Parts.Count;var part=ship.Parts[index];
                if(!ReadyLauncher(part,out var definition,out var position,out var up,out var velocity))continue;
                if(missile&&!BurstCanMeetThreat(definition,position,up,velocity,missilePosition,missileVelocity,missile,timeToImpact))continue;
                if(world.EmitBurst(ship,definition,position,up,velocity)==null)continue;
                readyAt[part]=ship.CombatTime+Mathf.Max(0,definition.cooldownSeconds);
                nextLauncher=(index+1)%ship.Parts.Count;return true;
            }
            return false;
        }
        bool ReadyLauncher(ShipPartState part,out FlareDefinition definition,out Vector3 position,out Vector3 up,out Vector3 velocity)
        {
            definition=Definition(part);position=up=velocity=Vector3.zero;
            if(!definition||Remaining(part)>0||!part.view)return false;
            foreach(var obstacle in ship.Parts)
                if(obstacle!=part&&obstacle.present&&obstacle.hp>0&&WeaponClearance.CountermeasurePathBlockedBy(part.record,obstacle.record))return false;
            var collider=part.view.GetComponent<Collider>();
            var matrix=collider?CylindricalHitQuery.PhysicsMatrix(collider):part.view.transform.localToWorldMatrix;
            position=matrix.MultiplyPoint3x4(Vector3.up*.55f);up=matrix.MultiplyVector(Vector3.up).normalized;
            if(flight){
                var rotation=body?body.rotation:ship.transform.rotation;var centre=body?body.position:ship.transform.position;
                velocity=flight.Velocity+Vector3.Cross(rotation*(flight.AngularVelocity*Mathf.Deg2Rad),position-centre);
            }
            else velocity=body?body.GetPointVelocity(position):Vector3.zero;
            return true;
        }
        static bool BurstCanMeetThreat(FlareDefinition flare,Vector3 position,Vector3 up,Vector3 inheritedVelocity,
            Vector3 missilePosition,Vector3 missileVelocity,WeaponDefinition missile,float timeToImpact)
        {
            float radius=Mathf.Max(0,flare.decoyRadius);
            if(radius<=0||flare.decoyChance<=0||missileVelocity.sqrMagnitude<.000001f)return false;
            float horizon=Mathf.Min(.9f,Mathf.Min(flare.lifetime*.5f,timeToImpact));
            if(horizon<=0)return false;
            float coneMean=(1+Mathf.Cos(Mathf.Clamp(flare.spreadHalfAngleDegrees,0,89)*Mathf.Deg2Rad))*.5f;
            var relativePosition=position-missilePosition;
            var relativeVelocity=inheritedVelocity+up*(Mathf.Max(0,flare.emissionSpeed)*coneMean)-missileVelocity;
            float speedSquared=relativeVelocity.sqrMagnitude,closing=Vector3.Dot(relativePosition,relativeVelocity);
            if(speedSquared<.000001f||closing>=0)return false;
            float outside=relativePosition.sqrMagnitude-radius*radius;
            float encounter=0;
            if(outside>0){
                float discriminant=closing*closing-speedSquared*outside;
                if(discriminant<0)return false;
                encounter=(-closing-Mathf.Sqrt(discriminant))/speedSquared;
                if(encounter<0||encounter>horizon)return false;
            }
            var bearing=relativePosition+relativeVelocity*encounter;
            return bearing.sqrMagnitude<.000001f||Vector3.Dot(missileVelocity.normalized,bearing.normalized)>=
                Mathf.Cos(Mathf.Clamp(missile.missileSeekerConeDegrees,1,179)*Mathf.Deg2Rad);
        }
    }
}
