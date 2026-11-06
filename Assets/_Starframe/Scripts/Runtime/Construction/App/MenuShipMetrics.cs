// @role Compute read-only menu ship statistics from the real blueprint, propulsion and combat definitions.
// @calls ShipPerformance, BlockModules, CombatDamageTuning, MissionDefinition

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.App
{
    public static class MenuShipMetrics
    {
        public sealed class Snapshot
        {
            public int Parts;public float MassKg,Cost,Health,Mobility,Firepower,Generation,Capacity,Sustained,Peak;
            public ShipPerformance Performance;public float Boost;
        }
        public static Snapshot Calculate(BlueprintData blueprint,FlightTuning tuning,MissionDefinition mission=null)
        {
            var result=new Snapshot{Parts=blueprint?.parts?.Count??0,Performance=new ShipPerformance()};
            if(blueprint?.parts==null||!tuning)return result;
            var cells=new Dictionary<Vector3Int,int>();
            for(int i=0;i<blueprint.parts.Count;i++)foreach(var cell in BlockModules.BodyCells(blueprint.parts[i]))cells[cell]=i;
            var states=new List<ShipPartState>();Quaternion core=Quaternion.identity;float shieldDemand=0;bool propulsion=false;
            foreach(var record in blueprint.parts)
            {
                int mask=0;var exhaust=BlockModules.LocalExhaustDirections(record.shape);
                for(int nozzle=0;nozzle<exhaust.Length&&nozzle<31;nozzle++)
                {
                    bool blocked=false;foreach(var cell in BlockModules.ExhaustCells(record,nozzle))
                        if(cells.TryGetValue(cell,out int owner)&&blueprint.parts[owner].id!=record.id){blocked=true;break;}
                    if(!blocked)mask|=1<<nozzle;
                }
                var stats=tuning.Get(record.shape);states.Add(new ShipPartState{record=record,hp=stats.maxHp,maxHp=stats.maxHp,availableExhaustMask=mask,thrustAvailable=mask!=0});
                if(record.shape==BlockShape.Core)core=record.rotation;
                var module=tuning.database?tuning.database.Get(record.shape):null;if(!module)continue;
                if(BlockModules.IsThruster(record.shape))propulsion=true;
                result.Cost+=mission?mission.CostFor(module):module.cost;result.Generation+=module.generationWatts;result.Capacity+=module.storageJoules;
                shieldDemand+=Mathf.Max(0,module.shieldPowerWatts);
                var weapon=module.weapon;if(!weapon)continue;
                result.Sustained+=Mathf.Max(0,weapon.sustainedPowerWatts);
                float multiplier=tuning.damageTuning?tuning.damageTuning.GetPlayerMultiplier(weapon.delivery):1;
                float cadence=weapon.delivery==WeaponDelivery.GuidedMissile?1/Mathf.Max(.1f,weapon.reloadSeconds):Mathf.Max(0,weapon.shotsPerSecond);
                if(weapon.activationMode==WeaponActivationMode.ChargeRelease)cadence=Mathf.Min(cadence,1/Mathf.Max(.01f,weapon.chargeSeconds));
                result.Firepower+=Mathf.Max(0,weapon.damage)*multiplier*cadence;
            }
            var p=ShipPerformance.Calculate(states,core,tuning);result.Performance=p;result.MassKg=p.totalMass;result.Health=p.totalHp;
            // A transparent handling index: mean six-axis acceleration in g plus mean pitch/yaw/roll rate.
            var a=p.accelerationPositive+p.accelerationNegative;
            var r=Vector3.Min(p.angularRatePositive,p.angularRateNegative);
            result.Mobility=(a.x+a.y+a.z)/(6*9.80665f)*10+(r.x+r.y+r.z)/3;
            result.Peak=result.Sustained+shieldDemand+Mathf.Max(0,tuning.BoostPowerDemandWatts);if(propulsion)result.Sustained+=tuning.PropulsionPowerDemandWatts;result.Boost=tuning.boostMultiplier;return result;
        }
        static string C(string zh,string en)=>L10n.IsEnglish?en:zh;
        static string N(float value,string format="N0")=>float.IsInfinity(value)?"∞":float.IsNaN(value)?"—":value.ToString(format);
        static string V(float value,string format="N0")=>"<color=#B9FF03>"+N(value,format)+"</color>";
        public static string Detail(Snapshot s)
        {
            var p=s.Performance;var a=p.accelerationPositive/9.80665f;var b=p.accelerationNegative/9.80665f;
            var rate=Vector3.Min(p.angularRatePositive,p.angularRateNegative);var angular=Vector3.Min(p.angularAccelerationPositive,p.angularAccelerationNegative);
            return C("质量 ","MASS ")+V(s.MassKg/1000,"0.0")+" t · HP "+V(s.Health)+"\n"+
                C("供电 ","OUTPUT ")+V(s.Generation)+" W · "+C("容量 ","CAPACITY ")+V(s.Capacity)+" J\n\n"+
                C("供能需求","POWER CONSUMPTION")+"\n"+C("闲置 ","IDLE ")+V(0)+" W · "+C("持续 ","SUSTAINED ")+V(s.Sustained)+" W\n"+C("峰值 ","PEAK ")+V(s.Peak)+" W\n\n"+
                C("速度","SPEED")+"\n"+C("限速 ","CAP ")+V(p.maxSpeed)+" m/s · "+C("加力 ×","BOOST ×")+V(s.Boost,"0.##")+"\n\n"+
                C("各向 · 加速 / 制动（g）","DIRECTION · ACCEL / BRAKE (g)")+"\n"+
                C("前 ","FWD ")+V(a.z,"0.00")+"/"+V(b.z,"0.00")+" · "+C("后 ","AFT ")+V(b.z,"0.00")+"/"+V(a.z,"0.00")+"\n"+
                C("左 ","LEFT ")+V(b.x,"0.00")+"/"+V(a.x,"0.00")+" · "+C("右 ","RIGHT ")+V(a.x,"0.00")+"/"+V(b.x,"0.00")+"\n"+
                C("上 ","UP ")+V(a.y,"0.00")+"/"+V(b.y,"0.00")+" · "+C("下 ","DOWN ")+V(b.y,"0.00")+"/"+V(a.y,"0.00")+"\n\n"+
                C("旋转 · 速度 / 角加速度","ROTATION · RATE / ACCELERATION")+"\n"+
                C("俯仰 ","PITCH ")+V(rate.x,"0.0")+" °/s · "+V(angular.x,"0.0")+" °/s²\n"+
                C("偏航 ","YAW ")+V(rate.y,"0.0")+" °/s · "+V(angular.y,"0.0")+" °/s²\n"+
                C("滚转 ","ROLL ")+V(rate.z,"0.0")+" °/s · "+V(angular.z,"0.0")+" °/s²\n"+
                C("满速制动 前/后 ","FULL-SPEED STOP F/A ")+V(p.maxSpeedStoppingDistancePositive.z)+"/"+V(p.maxSpeedStoppingDistanceNegative.z)+" m\n"+
                C("转弯半径 俯仰/偏航 ","TURN RADIUS P/Y ")+V(p.turningRadiusAtMaxSpeed.x)+"/"+V(p.turningRadiusAtMaxSpeed.y)+" m\n\n"+
                "<size=85%>"+C("机动 = 六向平均加速度(g)×10 + 三轴平均角速度。\n火力为额定持续DPS，含玩家伤害倍率、导弹装填。","MOBILITY = mean six-axis g × 10 + mean turn rate.\nFIREPOWER = rated sustained DPS, including player damage scale and missile reload.")+"</size>";
        }
        public static string Compact(float value)
        {
            float magnitude=Mathf.Abs(value);if(magnitude>=1e9f)return (value/1e9f).ToString("0.#")+"b";
            if(magnitude>=1e6f)return (value/1e6f).ToString("0.#")+"m";
            if(magnitude>=1e4f)return (value/1e3f).ToString("0.#")+"k";return N(value);
        }
        public static void Summary(MenuSurface ui,string prefix,Snapshot s,float x,float y,float width,float font=21)
        {
            float gap=font*1.5f,columnGap=20,half=(width-columnGap)*.5f;
            string[] labels={C("部件","PARTS"),C("质量","MASS"),C("造价","COST"),C("耐久","HEALTH"),C("机动","MOBILITY"),C("火力","FIREPOWER")};
            string[] values={s.Parts.ToString(),N(s.MassKg/1000,"0.0")+"t",Compact(s.Cost),Compact(s.Health),Compact(s.Mobility),Compact(s.Firepower)};
            for(int i=0;i<6;i++)
            {
                float col=i<3?0:half+columnGap,row=i%3;
                var text=ui.Label(prefix+i,labels[i]+":",x+col,y+row*gap,half*.61f,gap,font,Color.white);
                text.textWrappingMode=TextWrappingModes.NoWrap;text.enableAutoSizing=true;text.fontSizeMin=font*.72f;text.fontSizeMax=font;
                var number=ui.Label(prefix+i+"Value",values[i],x+col+half*.61f,y+row*gap,half*.36f,gap,font,FigmaVisuals.Accent);
                FigmaVisuals.SetFont(number,true);number.textWrappingMode=TextWrappingModes.NoWrap;number.alignment=TextAlignmentOptions.TopRight;
                number.enableAutoSizing=true;number.fontSizeMin=font*.65f;number.fontSizeMax=font;number.overflowMode=TextOverflowModes.Overflow;
            }
        }
    }
}
