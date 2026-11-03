// @role Deploy one ready surviving countermeasure launcher per press, rotating independent cooldowns.
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

        public bool TryDeploy()
        {
            RefreshLife();if(!ship||!ship.IsAlive||!world)return false;
            for(int offset=0;offset<ship.Parts.Count;offset++)
            {
                int index=(nextLauncher+offset)%ship.Parts.Count;var part=ship.Parts[index];var definition=Definition(part);
                if(!definition||Remaining(part)>0||!part.view)continue;
                bool blocked=false;
                foreach(var obstacle in ship.Parts)
                    if(obstacle!=part&&obstacle.present&&obstacle.hp>0&&WeaponClearance.CountermeasurePathBlockedBy(part.record,obstacle.record)){blocked=true;break;}
                if(blocked)continue;
                var collider=part.view.GetComponent<Collider>();
                var matrix=collider?CylindricalHitQuery.PhysicsMatrix(collider):part.view.transform.localToWorldMatrix;
                Vector3 position=matrix.MultiplyPoint3x4(Vector3.up*.55f),up=matrix.MultiplyVector(Vector3.up).normalized;
                Vector3 velocity=flight?flight.Velocity:body?body.linearVelocity:Vector3.zero;
                if(world.EmitBurst(ship,definition,position,up,velocity)==null)continue;
                readyAt[part]=ship.CombatTime+Mathf.Max(0,definition.cooldownSeconds);
                nextLauncher=(index+1)%ship.Parts.Count;return true;
            }
            return false;
        }
    }
}
