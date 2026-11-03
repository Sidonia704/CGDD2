// @role Define direct shots, guided missile reload and lock, beam reach and coloured effects.
// @tune shotsPerSecond:float:0.01+:Nominal shot cadence before energy allocation.
// @tune damage:float:0.01+:Damage of one complete projectile.
// @tune projectileSpeed:float:0.01+:Projectile speed used for travel and interception.
// @tune projectileDiameter:float:0.01+:Physical projectile hit-volume diameter.
// @tune penetrationDepth:float:0.01+:Maximum hull penetration depth.
// @tune secondLayerDamageMultiplier:float:0-1:Damage fraction for the second hit layer.
// @tune minimumVisualPixels:float:0+:Minimum projected tracer thickness; zero keeps authored world width.
// @tune range:float:1+:Maximum projectile travel distance.
// @tune sustainedPowerWatts:float:0+:Shared power requested per firing weapon.
// @tune missileLockSeconds:float:0+:Seconds holding one valid target before missile launch.
// @tune reloadSeconds:float:0.1+:Powered automatic missile reload duration.
// @tune missileTurnDegrees:float:0+:Maximum guided turn rate in degrees per second.
// @tune missileAcceleration:float:0+:Acceleration from launch speed to cruise speed.
// @tune missileLaunchSpeed:float:0.01+:Ejection speed of a guided missile.
// @tune explosionRadius:float:0.1+:Missile blast radius with radial damage falloff.
// @tune beamDuration:float:0.01+:Persistence of one continuous beam tick.
// @tune chargeSeconds:float:0.01+:Charge time for charge-release activation.

using UnityEngine;
namespace Starframe.Flight
{
    public enum WeaponDelivery { Projectile=0, GuidedMissile=1, Beam=2 }
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
        [Min(0)]public float minimumVisualPixels=0;
        [Header("Shared power while firing (J/s per weapon)")]
        [Min(0)]public float sustainedPowerWatts=50;
        [HideInInspector,Min(0)]public float energyPerShot=100;
        [Header("Delivery and shared visual colour")]
        public WeaponDelivery delivery;
        [ColorUsage(false,true)]public Color effectColor=new Color(.08f,.65f,1);
        [Header("Guided missile / powered automatic reload")]
        [Min(0)]public float missileLockSeconds=2;
        [Min(.1f)]public float reloadSeconds=4;
        [Min(0)]public float missileTurnDegrees=110,missileAcceleration=60;
        [Min(.01f)]public float missileLaunchSpeed=25;
        [Min(.1f)]public float explosionRadius=4;
        public Mesh launcherMesh,missileMesh;
        public Vector3[] missileLaunchOffsets={Vector3.zero};
        public Vector3[] muzzleOffsets=new Vector3[0];
        public Material projectileMaterial;
        [Header("Continuous short range beam")]
        [Min(.01f)]public float beamDuration=.12f;
        public Mesh beamMesh;
        [Header("Activation (shared input / power hooks for future weapon types)")]
        public WeaponActivationMode activationMode=WeaponActivationMode.Hold;
        [Min(.01f)]public float chargeSeconds=1;
        public bool requireTargetLock;
    }
}
