// @role Apply hold, press, charge and toggle modes using allocated power, external cadence scaling and a persistent immediate-shot cadence.
// @calls WeaponDefinition

using System;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Combat
{
    public struct WeaponInputState
    {
        public bool held,pressed,released;
        public WeaponInputState(bool held,bool pressed=false,bool released=false){this.held=held;this.pressed=pressed;this.released=released;}
    }

    /// <summary>Prepare requests watts; Advance applies the allocated fraction once and returns shot pulses.</summary>
    public sealed class WeaponActivationState
    {
        double progress,charge,cooldown;
        bool previousHeld,toggled,pending,charging,active,releaseReady;
        WeaponActivationMode mode;
        public bool RequestsPower{get;private set;}
        public float ChargeFraction=>(float)charge;
        public bool IsToggled=>toggled;

        public bool Prepare(WeaponDefinition definition,WeaponInputState input,bool canActivate)
        {
            if(!definition||!canActivate){Cancel();return false;}
            if(mode!=definition.activationMode){Cancel();mode=definition.activationMode;}
            // Edge modes require an explicit press. Clearing focus/lock must not
            // switch a toggle back on merely because the physical key is still held.
            bool pressed=input.pressed;
            bool released=input.released||(!input.held&&previousHeld);
            previousHeld=input.held;RequestsPower=false;active=false;
            switch(mode){
                case WeaponActivationMode.Hold:
                    active=input.held;if(!active)progress=0;
                    RequestsPower=active;return active;
                case WeaponActivationMode.Toggle:
                    if(pressed)toggled=!toggled;active=toggled;if(!active)progress=0;RequestsPower=active;return active;
                case WeaponActivationMode.Press:
                    // One powered firing cycle per press; extra presses cannot build a queue.
                    if(pressed&&!pending){pending=true;progress=0;}
                    RequestsPower=pending;return pending;
                case WeaponActivationMode.ChargeRelease:
                    if(input.held){charging=true;RequestsPower=charge<1;}
                    if(released&&charging){releaseReady=charge>=1;charging=false;charge=0;RequestsPower=false;}
                    return charging||releaseReady;
                default:
                    Clear();return false;
            }
        }

        public int Advance(WeaponDefinition definition,float dt,float powerRatio,float rateMultiplier=1)
        {
            if(!definition||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return 0;
            double ratio=float.IsNaN(powerRatio)||float.IsInfinity(powerRatio)?0:Mathf.Clamp01(powerRatio);
            bool immediateHold=mode==WeaponActivationMode.Hold&&definition.firstShotImmediate;
            if(!immediateHold)AdvanceInactive(dt);
            if(mode==WeaponActivationMode.ChargeRelease){
                if(releaseReady){releaseReady=false;return 1;}
                if(charging){float seconds=definition.chargeSeconds;if(float.IsNaN(seconds)||float.IsInfinity(seconds))return 0;charge=Math.Min(1,charge+dt*ratio/Math.Max(.01,seconds));if(charge>=1-1e-7)charge=1;}
                return 0;
            }
            if(!active&&!pending){if(immediateHold)AdvanceInactive(dt);return 0;}
            if(ratio<=0)return 0;
            float rate=definition.shotsPerSecond*rateMultiplier;if(rate<=0||float.IsNaN(rate)||float.IsInfinity(rate)){if(immediateHold)AdvanceInactive(dt);return 0;}
            if(immediateHold){
                // One clock owns the entire cycle. Do not add a second powered
                // progress cycle after cooling, or sustained cadence is halved.
                // The initial shot is retained until power is actually allocated.
                if(cooldown>0)cooldown-=dt*ratio;
                if(cooldown>1e-6)return 0;
                int pulses=(int)Math.Min(int.MaxValue,1+Math.Floor(Math.Max(0,-cooldown)*rate+1e-7));
                cooldown+=pulses/(double)rate;
                return pulses;
            }
            progress+=dt*ratio*rate;
            if(progress<1-1e-7)return 0;
            if(mode==WeaponActivationMode.Press){pending=false;progress=0;RequestsPower=false;return 1;}
            int count=(int)Math.Min(int.MaxValue,Math.Floor(progress+1e-7));progress=Math.Max(0,progress-count);
            return count;
        }

        /// <summary>Cancel input/charge without granting another immediate shot.</summary>
        public void Cancel(){progress=charge=0;previousHeld=toggled=pending=charging=active=releaseReady=RequestsPower=false;}

        /// <summary>Unselected and released weapons cool without firing or banking shot pulses.</summary>
        public void AdvanceInactive(float dt)
        {
            if(dt>0&&!float.IsNaN(dt)&&!float.IsInfinity(dt))cooldown=Math.Max(0,cooldown-dt);
        }

        /// <summary>Offset a newly started automatic cycle without changing its sustained per-gun rate.</summary>
        public void DelayCycle(WeaponDefinition definition,float seconds)
        {
            seconds=Mathf.Max(0,seconds);
            if(definition.activationMode==WeaponActivationMode.Hold&&definition.firstShotImmediate)cooldown+=seconds;
            else progress-=seconds*Mathf.Max(.01f,definition.shotsPerSecond);
        }
        public void SynchronizeCycle(WeaponDefinition definition)
        {
            progress=0;cooldown=1/Math.Max(.01,definition.shotsPerSecond);
        }
        public void Clear(){Cancel();cooldown=0;}
    }
}
