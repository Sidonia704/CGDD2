// @role Allocate generated and stored power proportionally across consumers.

using UnityEngine;
namespace Starframe.Combat
{
    // All consumers request full power before a single proportional allocation.
    public sealed class ShipPowerBus
    {
        public float Capacity{get;private set;}
        public float Generation{get;private set;}
        public float Energy{get;private set;}
        public float Ratio{get;private set;}=1;
        public float RequestedPower{get;private set;}
        public void Configure(float capacity,float generation,bool fill=false){Capacity=Mathf.Max(0,capacity);Generation=Mathf.Max(0,generation);Energy=fill?Capacity:Mathf.Min(Energy,Capacity);}
        public void SetEnergy(float value)=>Energy=Mathf.Clamp(value,0,Capacity);
        public float Allocate(float dt,float demand,bool unlimited=false)
        {
            RequestedPower=Mathf.Max(0,demand);
            if(dt<=0)return Ratio;
            Ratio=unlimited||RequestedPower<=0?1:Mathf.Clamp01((Generation+Energy/dt)/RequestedPower);
            if(!unlimited)Energy=Mathf.Clamp(Energy+(Generation-RequestedPower*Ratio)*dt,0,Capacity);
            return Ratio;
        }
    }
}
