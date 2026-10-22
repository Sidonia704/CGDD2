// @role Define shot cadence, damage, hit volume, range, power and activation mode.
// @tune shotsPerSecond:float:0.01+:Nominal shot cadence before energy allocation.
// @tune damage:float:0.01+:Damage of one complete projectile.
// @tune projectileSpeed:float:0.01+:Projectile speed used for travel and interception.
// @tune projectileDiameter:float:0.01+:Physical projectile hit-volume diameter.
// @tune penetrationDepth:float:0.01+:Maximum hull penetration depth.
// @tune secondLayerDamageMultiplier:float:0-1:Damage fraction for the second hit layer.
// @tune range:float:1+:Maximum projectile travel distance.
// @tune sustainedPowerWatts:float:0+:Shared power requested per firing weapon.
// @tune chargeSeconds:float:0.01+:Charge time for charge-release activation.

using UnityEngine;
namespace Starframe.Flight
{
    public enum WeaponActivationMode { Hold=0, Press=1, ChargeRelease=2, Toggle=3 }
    [CreateAssetMenu(menuName="Starframe/Weapon Definition")]
    public sealed class WeaponDefinition:ScriptableObject
    {
        [Min(.01f)]public float shotsPerSecond=5,damage=40,projectileSpeed=160,projectileDiameter=1;
        [Min(.01f)]public float penetrationDepth=2;
        [Range(0,1)]public float secondLayerDamageMultiplier=.5f;
        [Min(1)]public float range=800;
        [Header("White voxel head / separated blue cube tail")]
        public float visualLength=.8f,tailLength=3;
        [Min(.01f)]public float visualWidth=.55f;
        [Min(0)]public float visualIntensity=4.2f;
        [Header("Shared power while firing (J/s per weapon)")]
        [Min(0)]public float sustainedPowerWatts=50;
        [HideInInspector,Min(0)]public float energyPerShot=100;
        [Header("Activation (shared input / power hooks for future weapon types)")]
        public WeaponActivationMode activationMode=WeaponActivationMode.Hold;
        [Min(.01f)]public float chargeSeconds=1;
        public bool requireTargetLock;
    }
}
