// @role Allocate one ship energy budget with shield recovery before boost before weapons, retaining proportional allocation for legacy callers.

using UnityEngine;
namespace Starframe.Combat
{
    public sealed class ShipPowerBus
    {
        public float Capacity{get;private set;}
        public float Generation{get;private set;}
        public float Energy{get;private set;}
        public float Ratio{get;private set;}=1;
        public float RequestedPower{get;private set;}
        public float AllocatedPower{get;private set;}
        public float ShieldRatio{get;private set;}=1;
        public float BoostRatio{get;private set;}=1;
        public float WeaponRatio{get;private set;}=1;
        public void Configure(float capacity,float generation,bool fill=false){Capacity=Mathf.Max(0,capacity);Generation=Mathf.Max(0,generation);Energy=fill?Capacity:Mathf.Min(Energy,Capacity);}
        public void SetEnergy(float value)=>Energy=Mathf.Clamp(value,0,Capacity);
        public float Allocate(float dt,float demand,bool unlimited=false)
        {
            RequestedPower=Demand(demand);
            if(!ValidStep(dt))return Ratio;
            Ratio=unlimited||RequestedPower<=0?1:Mathf.Clamp01((Generation+Energy/dt)/RequestedPower);
            AllocatedPower=RequestedPower*Ratio;
            ShieldRatio=BoostRatio=WeaponRatio=Ratio;
            if(!unlimited)Energy=Mathf.Clamp(Energy+(Generation-AllocatedPower)*dt,0,Capacity);
            return Ratio;
        }
        public void AllocatePrioritized(float dt,float shieldDemand,float boostDemand,float weaponDemand,bool unlimited,
            out float shieldRatio,out float boostRatio,out float weaponRatio)
        {
            shieldRatio=ShieldRatio;boostRatio=BoostRatio;weaponRatio=WeaponRatio;
            if(!ValidStep(dt))return;
            shieldDemand=Demand(shieldDemand);boostDemand=Demand(boostDemand);weaponDemand=Demand(weaponDemand);
            RequestedPower=shieldDemand+boostDemand+weaponDemand;
            float available=Generation+Energy/dt;
            float shield=unlimited?shieldDemand:Serve(shieldDemand,ref available);
            float boost=unlimited?boostDemand:Serve(boostDemand,ref available);
            float weapon=unlimited?weaponDemand:Serve(weaponDemand,ref available);
            ShieldRatio=shieldRatio=shieldDemand>0?Mathf.Clamp01(shield/shieldDemand):1;
            BoostRatio=boostRatio=boostDemand>0?Mathf.Clamp01(boost/boostDemand):1;
            WeaponRatio=weaponRatio=weaponDemand>0?Mathf.Clamp01(weapon/weaponDemand):1;
            AllocatedPower=shield+boost+weapon;
            Ratio=RequestedPower>0?Mathf.Clamp01(AllocatedPower/RequestedPower):1;
            // Generation and battery are integrated once, after all priority classes.
            if(!unlimited)Energy=Mathf.Clamp(Energy+(Generation-AllocatedPower)*dt,0,Capacity);
        }
        static float Serve(float demand,ref float available)
        {
            float served=Mathf.Min(demand,Mathf.Max(0,available));available-=served;return served;
        }
        static float Demand(float value)=>float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Max(0,value);
        static bool ValidStep(float dt)=>dt>0&&!float.IsNaN(dt)&&!float.IsInfinity(dt);
    }
}
