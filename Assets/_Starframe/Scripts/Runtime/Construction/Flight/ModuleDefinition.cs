// @role Define per-part mass, durability, thrust, weapons, countermeasures, power and shields.
// @calls WeaponDefinition, FlareDefinition
// @tune cost:float:0+:Deployment cost; implicit field default is zero.
// @tune massKg:float:0+:Part mass in kilograms.
// @tune maxHp:float:0.01+:Independent part durability.
// @tune recoverySeconds:float:0.01+:Time to reconstruct a missing part or heal a full part after the shared quiet delay.
// @tune thrustNewtons:float:0+:Rated force per nozzle; multi-direction modules contribute only one nozzle rating to shared manoeuvre assistance.
// @tune generationWatts:float:0+:Continuous shared power generation.
// @tune storageJoules:float:0+:Shared stored energy capacity.
// @tune thrusterPowerWatts:float:0+:Power request associated with propulsion.
// @tune shieldPowerWatts:float:0+:Power request associated with shield recovery.
// @tune shieldCapacity:float:0+:Maximum shield strength per generator.
// @tune shieldRadius:float:0.1+:Shield field radius in metres.
// @tune shieldRecoveryDelay:float:0+:Delay before a shield can recover.
// @tune shieldRecoveryPerSecond:float:0+:Shield recovery per second.

using UnityEngine;
using Starframe.Construction;
namespace Starframe.Flight
{
    public enum ModuleCategory { Hull,Propulsion,Core,Weapon,Power,Shield }
    [CreateAssetMenu(menuName="Starframe/Module Definition")]
    public sealed class ModuleDefinition:ScriptableObject
    {
        public BlockShape shape;
        public string displayName;
        public ModuleCategory category;
        [Min(0)]public float cost,massKg=300;
        [Min(.01f)]public float maxHp=60,recoverySeconds=6;
        [Min(0)]public float thrustNewtons;
        public Vector3Int[] occupiedCells={Vector3Int.zero};
        [Tooltip("Local exhaust directions, one per independently controlled nozzle. Empty preserves the legacy single +Y nozzle.")]
        public Vector3[] localExhaustDirections=System.Array.Empty<Vector3>();
        [Tooltip("Nozzle mouth centres in the module's local space, matching localExhaustDirections. Empty uses the mesh boundary.")]
        public Vector3[] localNozzlePositions=System.Array.Empty<Vector3>();
        public Vector3 muzzleLocalPosition=new Vector3(0,.27f,1.52f);
        [Tooltip("Physical muzzle clearance diameter in blocks, independent of the projectile's damage hit volume.")]
        [Min(.01f)]public float muzzleClearanceDiameter=.25f;
        public Mesh mesh,outline;
        public bool drawOutline=true;
        public Material material;
        public Sprite icon,selectedIcon;
        public WeaponDefinition weapon;
        public FlareDefinition countermeasure;
        [Header("Shared power bus (energy / second)")]
        [Min(0)]public float generationWatts,storageJoules,thrusterPowerWatts,shieldPowerWatts;
        [Header("Shield generator (per surviving component)")]
        [Min(0)]public float shieldCapacity;
        [Min(.1f)]public float shieldRadius=8;
        [Min(0)]public float shieldRecoveryDelay=6,shieldRecoveryPerSecond=50;
        public Material shieldSurfaceMaterial;
        [Tooltip("Visual sphere centred at the origin with diameter 1. Does not affect shield collision/radius.")]
        public Mesh shieldSurfaceMesh;
        public int Revision{get;private set;}
        ModuleStats stats;
        public ModuleStats Stats{get{if(stats==null)stats=new ModuleStats(shape,massKg,maxHp,thrustNewtons);stats.shape=shape;stats.massKg=massKg;stats.maxHp=maxHp;stats.thrustNewtons=thrustNewtons;return stats;}}
        void OnValidate(){Revision++;}
    }
}
