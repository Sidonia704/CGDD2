// @role Preserve each Gatling mount's powered spin-up, simultaneous heating/cooling and overheat lockout.
// @calls WeaponDefinition

using System;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Combat
{
    /// <summary>Advanced exactly once per simulation step by ShipWeapons, independently of input cancellation.</summary>
    public sealed class GatlingThermalState
    {
        double spin, heat;
        public float SpinFraction => (float)spin;
        public float HeatFraction => (float)heat;
        public bool IsOverheated { get; private set; }

        /// <returns>Step-averaged cadence fraction, including a partial step before overheating.</returns>
        public float Advance(WeaponDefinition definition, float dt, bool requested, float powerRatio)
        {
            if (!definition || definition.family != WeaponFamily.Gatling) return 1;
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return 0;
            double ratio = float.IsNaN(powerRatio) || float.IsInfinity(powerRatio) ? 0 : Mathf.Clamp01(powerRatio);
            double cooling = Positive(definition.gatlingCoolingPerSecond);
            double spinDown = Math.Max(.0001, Positive(definition.gatlingSpinDownSeconds));
            if (IsOverheated || !requested || ratio <= 0)
            {
                heat = Math.Max(0, heat - cooling * dt);
                spin = Math.Max(0, spin - dt / spinDown);
                if (heat <= 1e-7) { heat = 0; IsOverheated = false; }
                return 0;
            }
            double netHeat = Positive(definition.gatlingHeatPerSecond) * ratio - cooling;
            double firingSeconds = dt;
            if (netHeat > 0) firingSeconds = Math.Min(dt, Math.Max(0, (1 - heat) / netHeat));
            double spinRate = ratio / Math.Max(.0001, Positive(definition.gatlingSpinUpSeconds));
            double rampSeconds = Math.Min(firingSeconds, Math.Max(0, (1 - spin) / spinRate));
            double spinIntegral = spin * rampSeconds + .5 * spinRate * rampSeconds * rampSeconds + firingSeconds - rampSeconds;
            spin = Math.Min(1, spin + firingSeconds * spinRate);
            double minimumRate = Mathf.Clamp01(definition.gatlingMinimumRateFraction);
            double cadenceIntegral = minimumRate * firingSeconds + (1 - minimumRate) * spinIntegral;
            heat = Math.Max(0, Math.Min(1, heat + netHeat * firingSeconds));
            if (netHeat > 0 && heat >= 1 - 1e-7)
            {
                heat = 1; IsOverheated = true;
                double remaining = dt - firingSeconds;
                heat = Math.Max(0, heat - cooling * remaining);
                spin = Math.Max(0, spin - remaining / spinDown);
                if (heat <= 1e-7) { heat = 0; IsOverheated = false; }
            }
            return (float)(cadenceIntegral / dt);
        }

        public void Clear() { spin = heat = 0; IsOverheated = false; }
        static double Positive(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, value);
    }
}
