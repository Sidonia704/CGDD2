// @role Define direct weapons, Gatling heat, conventional seekers and independent area-lock VLS magazines with shared visuals and audio gains.
// @tune fireWindupSeconds:float:0+:Delay from accepted rail audio onset to physical discharge; zero preserves immediate shots.
// @tune shotsPerSecond:float:0.01+:Nominal shot cadence before energy allocation.
// @tune damage:float:0.01+:Damage of one complete projectile.
// @tune shotAudioGain:float:0-1:Relative firing loudness by weapon size; small 0.6, medium 0.8, large 1.
// @tune projectileSpeed:float:0.01+:Projectile speed used for travel and interception.
// @tune projectileDiameter:float:0.01+:Physical projectile hit-volume diameter.
// @tune penetrationDepth:float:0.01+:Maximum hull penetration depth.
// @tune secondLayerDamageMultiplier:float:0-1:Damage fraction for the second hit layer.
// @tune smokeTrail:bool:0-1:Visual-only muzzle smoke for solid rounds or post-ignition missile exhaust.
// @tune tracerLingerSeconds:float:0+:Visual-only fade after a shot resolves; zero releases immediately.
// @tune minimumVisualPixels:float:0+:Minimum projected tracer thickness; zero keeps authored world width.
// @tune range:float:1+:Maximum projectile travel distance.
// @tune unboundedBeam:bool:0-1:Use the 100km operational query reach for an effectively unlimited beam.
// @tune sustainedPowerWatts:float:0+:Shared power requested per firing weapon.
// @tune magazineSize:int:1+:Rounds in one VLS magazine; tube offsets describe physical cells independently.
// @tune salvoLaunchesPerSecond:float:0.01+:Independent VLS mount launch cadence.
// @tune missileLockSeconds:float:0+:Seconds holding one valid target before missile launch.
// @tune magazineSize:int:1+:VLS rounds available before a complete magazine reload.
// @tune salvoLaunchesPerSecond:float:0.01+:Independent per-module VLS launch cadence.
// @tune reloadSeconds:float:0.1+:Powered automatic missile reload duration.
// @tune missileTurnDegrees:float:0+:Maximum guided turn rate in degrees per second.
// @tune missileTurnAcceleration:float:0+:Maximum guided angular acceleration in degrees per second squared.
// @tune missileGuidanceSeconds:float:0+:Maximum seeker lifetime after launch; expiry leaves the missile coasting.
// @tune missileSeekerConeDegrees:float:1-179:Seeker half-cone around the missile's current forward direction.
// @tune missileSeekerLossSeconds:float:0+:Continuous time outside the seeker cone before permanent lock loss.
// @tune missileAcceleration:float:0+:Acceleration from launch speed to cruise speed.
// @tune missileLaunchSpeed:float:0.01+:Ejection speed of a guided missile.
// @tune explosionRadius:float:0.1+:Missile blast radius with radial damage falloff.
// @tune beamDuration:float:0.01+:Persistence of one continuous beam tick.
// @tune chargeSeconds:float:0.01+:Charge time for charge-release activation.
// @tune gatlingSpinUpSeconds:float:0.01+:Powered seconds to reach maximum Gatling rate.
// @tune gatlingSpinDownSeconds:float:0.01+:Unpowered seconds to stop fully spun barrels.
// @tune gatlingMinimumRateFraction:float:0-1:Gatling rate at the beginning of powered spin-up.
// @tune gatlingHeatPerSecond:float:0+:Heat gained per fully powered firing second before simultaneous cooling; defaults overheat after 8 seconds.
// @tune gatlingCoolingPerSecond:float:0+:Passive heat removed per simulation second in every state; defaults recover full heat in 6 seconds.

using UnityEngine;
namespace Starframe.Flight
{
    public enum TracerStyle { Voxel=0, Continuous=1 }
    public enum WeaponDelivery { Projectile=0, GuidedMissile=1, Beam=2 }
    public enum WeaponFamily { Generic=0, Gatling=1, RapidLaser=2, Railgun=3, BeamLaser=4, GuidedMissile=5, VerticalLaunchMissile=6 }
    public enum WeaponActivationMode { Hold=0, Press=1, ChargeRelease=2, Toggle=3 }
    [CreateAssetMenu(menuName="Starframe/Weapon Definition")]
    public sealed class WeaponDefinition:ScriptableObject
    {
        [Min(.01f)]public float shotsPerSecond=5,damage=40,projectileSpeed=160,projectileDiameter=1;
        [Range(0,1)]public float shotAudioGain=1;
        [Tooltip("Fire the first shot of a hold cycle immediately, then wait for shotsPerSecond before the next.")]
        public bool firstShotImmediate;
        [Tooltip("Delay accepted projectile discharge to align the shot with its audio peak. Zero fires immediately.")]
        [Min(0)]public float fireWindupSeconds;
        [Min(.01f)]public float penetrationDepth=2;
        [Range(0,1)]public float secondLayerDamageMultiplier=.5f;
        [Min(1)]public float range=800;
        public bool unboundedBeam;
        public float EffectiveRange=>unboundedBeam&&delivery==WeaponDelivery.Beam?100000f:range;
        [Header("White voxel head / separated blue cube tail")]
        public float visualLength=.8f,tailLength=3;
        [Min(.01f)]public float visualWidth=.55f;
        [Min(0)]public float visualIntensity=4.2f;
        [Min(0)]public float minimumVisualPixels=0;
        public TracerStyle tracerStyle;
        [Tooltip("Visual-only world-space muzzle smoke for solid rounds; flight exhaust for missiles.")]
        public bool smokeTrail;
        [Min(0)]public float tracerLingerSeconds=0;
        [Header("Shared power while firing (J/s per weapon)")]
        [Min(0)]public float sustainedPowerWatts=50;
        [HideInInspector,Min(0)]public float energyPerShot=100;
        [Header("Delivery and shared visual colour")]
        public WeaponDelivery delivery;
        [Tooltip("Explicit weapon behaviour family; Generic keeps legacy projectile and beam behaviour.")]
        public WeaponFamily family=WeaponFamily.Generic;
        [ColorUsage(false,true)]public Color effectColor=new Color(.08f,1f,.24f);
        [Header("Guided missile / powered automatic reload")]
        public bool IsVls=>family==WeaponFamily.VerticalLaunchMissile;
        [Min(1)]public int magazineSize=1;
        [Min(.01f)]public float salvoLaunchesPerSecond=16;
        [Min(0)]public float missileLockSeconds=2;
        [Min(.1f)]public float reloadSeconds=4;
        [Min(0)]public float missileTurnDegrees=110,missileAcceleration=60;
        [Min(0)]public float missileTurnAcceleration=180,missileGuidanceSeconds=10;
        [Range(1,179)]public float missileSeekerConeDegrees=65;
        [Min(0)]public float missileSeekerLossSeconds=.35f;
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
        [Header("Gatling powered spin and persistent heat (Gatling family only)")]
        [Min(.01f)]public float gatlingSpinUpSeconds=1.5f,gatlingSpinDownSeconds=.5f;
        [Range(0,1)]public float gatlingMinimumRateFraction=.2f;
        [Min(0)]public float gatlingHeatPerSecond=1f/8f+1f/6f,gatlingCoolingPerSecond=1f/6f;
    }
}
