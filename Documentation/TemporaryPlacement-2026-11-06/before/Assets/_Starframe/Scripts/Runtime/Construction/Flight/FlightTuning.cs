// @role Cache independent balanced flight axes from surviving nozzle layout, mass and inertia.
// @calls ModuleDatabase, BlockModules, ShipAssembly
// @tune maneuverThrustFraction:float:0-0.8:Legacy bot assistance summary fraction; does not set player propulsion allocation.
// @tune maxSpeed:float:0+:Soft flight speed ceiling in metres per second.
// @tune maxLinearAcceleration:float:0+:Soft ceiling applied to each independent translation axis.
// @tune angularAccelerationGain:float:0+:Legacy serialized compatibility setting; unused by the current flight controller.
// @tune angularAuthorityReferenceAcceleration:float:0.01+:Legacy serialized compatibility setting; unused by the current flight controller.
// @tune angularAuthorityExponent:float:0.5-2:Legacy serialized compatibility setting; unused by the current flight controller.
// @tune maxAngularAcceleration:float:0+:Soft angular acceleration ceiling in degrees per second squared.
// @tune angularRateGain:float:0+:Gain for deriving pitch and yaw rate from calculated angular acceleration.
// @tune maxAngularRate:float:0+:Soft pitch and yaw rate ceiling in degrees per second.
// @tune rollRateGain:float:0+:Gain for deriving roll rate from calculated angular acceleration.
// @tune maxRollRate:float:0+:Soft roll rate ceiling in degrees per second.
// @tune rollBlendTime:float:0+:Roll input response duration in seconds.
// @tune minimumTurnAuthority:float:0-1:Legacy serialized compatibility setting; unused by the current flight controller.
// @tune boostPowerWatts:float:0+:Requested power while holding boost.
// @tune boostMultiplier:float:1+:Boost multiplier at full allocated power.
// @tune mouseDegreesPerPixel:float:0+:Virtual-stick sensitivity before the settings multiplier.
// @tune maxAimOffsetDegrees:float:5-45:Maximum persistent virtual-stick offset.
// @tune aimInputDeadZone:float:0-0.4:Dead zone as a fraction of stick radius.
// @tune aimResponseExponent:float:0.5-3:Response curve around the stick centre.
// @tune aimInputSmoothing:float:0-0.15:Steering smoothing duration.
// @tune turnResponseTime:float:0.05-0.5:Turn response duration.
// @tune arenaHalfExtent:float:0+:Half-width of the test range in metres.
// @tune gridWallSpacing:float:1+:Separation between spatial reference planes in metres.
// @tune gridCellSize:float:0.1+:Small reference-grid cell spacing in metres.
// @tune gridOpacity:float:0-1:Reference-grid opacity; boundary warning keeps its own visibility.

using System;
using UnityEngine;
using Starframe.Construction;
namespace Starframe.Flight
{
    [Serializable] public sealed class ModuleStats
    {
        public BlockShape shape;public float massKg,maxHp,thrustNewtons;
        public ModuleStats(BlockShape s,float mass,float hp,float thrust=0){shape=s;massKg=mass;maxHp=hp;thrustNewtons=thrust;}
    }
    [CreateAssetMenu(menuName="Starframe/Flight Tuning")]
    public sealed class FlightTuning : ScriptableObject
    {
        public ModuleDatabase database;
        [Tooltip("Player-facing damage readouts share the combat world's category tuning asset.")]
        public Starframe.Combat.CombatDamageTuning damageTuning;
        [HideInInspector]
        public ModuleStats[] modules={
            new ModuleStats(BlockShape.Cube,300,60),new ModuleStats(BlockShape.Wedge,150,30),
            new ModuleStats(BlockShape.Thruster1,600,100,80000),new ModuleStats(BlockShape.Thruster2,1400,200,200000),
            new ModuleStats(BlockShape.Slab,150,30),new ModuleStats(BlockShape.Pyramid,100,20),new ModuleStats(BlockShape.Core,2000,300)};
        [HideInInspector,Range(0,.8f)]public float maneuverThrustFraction=.2f;
        [Header("Flight / balanced arcade axes, mass and inertia")]
        [Tooltip("Soft ceiling for the ship's speed in metres per second.")]
        public float maxSpeed=250;
        [Tooltip("Soft ceiling for each allocated translation axis, in metres per second squared.")]
        public float maxLinearAcceleration=100;
        [HideInInspector]public float angularAccelerationGain=10;
        [HideInInspector,Min(.01f)]public float angularAuthorityReferenceAcceleration=10;
        [HideInInspector,Range(.5f,2)]public float angularAuthorityExponent=1.1f;
        [Tooltip("Soft ceiling for angular acceleration on each rotation axis, in degrees per second squared.")]
        public float maxAngularAcceleration=360;
        [Tooltip("Gain used to derive pitch and yaw rate from calculated angular acceleration and ship inertia.")]
        public float angularRateGain=4;
        [Tooltip("Soft pitch and yaw rate ceiling in degrees per second.")]
        public float maxAngularRate=150;
        [Tooltip("Gain used to derive roll rate from calculated angular acceleration and ship inertia.")]
        public float rollRateGain=5;
        [Tooltip("Soft roll rate ceiling in degrees per second.")]
        public float maxRollRate=180;
        [Tooltip("Roll response duration in seconds.")]
        public float rollBlendTime=.25f;
        [Header("Hold Shift / shared power")]
        [Min(0)]public float boostPowerWatts=100;
        [Min(1)]public float boostMultiplier=1.5f;
        public float aimSensitivity=1;
        [HideInInspector]public float aimDistance=1500,aimDeadZone=.12f;
        [Header("Mouse virtual stick / persistent turn input")]
        [Tooltip("Degrees per mouse pixel at 1080p, multiplied by Aim Sensitivity.")]
        public float mouseDegreesPerPixel=.085f;
        [Range(5,45)]public float maxAimOffsetDegrees=25;
        [Tooltip("Centre dead zone as a fraction of the virtual stick radius.")]
        [Range(0,.4f)]public float aimInputDeadZone=.035f;
        [Tooltip("1 is proportional response; higher values allow finer turning near the centre.")]
        [Range(.5f,3)]public float aimResponseExponent=1.6f;
        [Tooltip("Seconds to smooth steering input; does not recenter the persistent mouse offset.")]
        [Range(0,.15f)]public float aimInputSmoothing=.035f;
        [Tooltip("Pitch and yaw response duration in seconds.")]
        [Range(.05f,.5f)]public float turnResponseTime=.14f;
        [HideInInspector,Range(0,1)]public float minimumTurnAuthority=.75f;
        public float reticleSharpness=30,reticleMaxSpeed=1800;
        [Header("Third person camera")]
        public float cameraDistance=18,minCameraDistance=6,maxCameraDistance=70;
        [HideInInspector]public float cameraHeight=5,cameraLookAhead=7;
        public float cameraPositionSharpness=10,cameraRotationSharpness=12,cameraZoomSensitivity=.15f,cameraFieldOfView=65;
        [Tooltip("Ship centre in the viewport: 0 is the bottom, .5 is the screen centre.")]
        [Range(.15f,.45f)]public float cameraShipViewportY=.30f;
        [Header("Flowing white / cyan / blue voxel exhaust")]
        public Shader exhaustShader;
        public float exhaustReferenceThrust=80000,exhaustLength=2.1f,exhaustMaxLength=5.5f,exhaustWidth=.42f;
        public float exhaustIntensity=1.6f,exhaustResponse=14;
        [Tooltip("Exhaust particle travel cycles per second at full throttle and reference thrust.")]
        [Min(.05f)]public float exhaustFlowSpeed=1.6f;
        [Range(0,.2f)]public float exhaustIdle=0;
        [Range(.1f,1)]public float exhaustInnerWidth=.38f,exhaustInnerLength=.72f;
        [ColorUsage(false,true)]public Color exhaustOuterColor=new Color(.015f,.3f,.85f);
        [ColorUsage(false,true)]public Color exhaustInnerColor=new Color(.8f,.95f,1.6f);
        [Header("3000 metre range / grid references")]
        public float arenaHalfExtent=1500,boundaryFadeDistance=220,boundaryBrakeAcceleration=80;
        public float gridWallSpacing=600,gridCellSize=12,gridFadeNear=30,gridFadeFar=130,gridPatchHalfExtent=220;
        [Range(0,1)]public float gridOpacity=.42f;
        public int Revision {get;private set;}
        void OnValidate(){Revision++;}
        public ModuleDefinition GetDefinition(BlockShape shape)=>database?database.Get(shape):null;
        public ModuleStats Get(BlockShape shape){var d=GetDefinition(shape);if(d)return d.Stats;foreach(var m in modules)if(m.shape==shape)return m;return new ModuleStats(shape,300,60);}
    }
    [Serializable] public sealed class ShipPartState
    {
        public BlockRecord record;public GameObject view;public float hp,maxHp,rebuildProgress;public bool present=true,thrustAvailable=true,detached;
        public int availableExhaustMask=-1;
        public bool ExhaustAvailable(int nozzle)=>present&&thrustAvailable&&nozzle>=0&&nozzle<31&&(availableExhaustMask&(1<<nozzle))!=0;
    }
    public sealed class ShipPerformance
    {
        public float totalMass,totalHp,currentHp,totalThrust,assistAcceleration,pitchAcceleration,yawAcceleration,rollSpeed,angularRate,maxSpeed;
        public Vector3 accelerationPositive,accelerationNegative;
        public Vector3 centerOfMass,inertiaTensorKgM2;
        public Vector3 angularAccelerationPositive,angularAccelerationNegative;
        public Vector3 angularRatePositive,angularRateNegative;
        public Vector3 maxSpeedStoppingDistancePositive,maxSpeedStoppingDistanceNegative;
        public Vector3 turningRadiusAtMaxSpeed;
        // Angular acceleration values use degrees per second squared throughout this snapshot.
        public sealed class ThrusterNozzle
        {
            public ShipPartState part;public int index;public float forceNewtons,requestedThrottle,allocatedThrottle;
            public Vector3 forceDirection,leverArm;
        }
        // Arcade control authority: the hull layout determines the numbers, while flight
        // applies only the requested axes. Off-centre thrust must never become an unwanted
        // body torque. Each module still shares one budget across simultaneous commands.
        sealed class ModuleAxes
        {
            public Vector3 linearPositive,linearNegative,torquePositive,torqueNegative;
            public readonly int[] linearPositiveNozzle={-1,-1,-1},linearNegativeNozzle={-1,-1,-1};
            public readonly int[] torquePositiveNozzle={-1,-1,-1},torqueNegativeNozzle={-1,-1,-1};
        }
        public readonly System.Collections.Generic.List<ThrusterNozzle> nozzles=new System.Collections.Generic.List<ThrusterNozzle>();
        public Vector3 linearCapacityPositive,linearCapacityNegative,angularCapacityPositive,angularCapacityNegative;
        readonly System.Collections.Generic.List<ModuleAxes> modules=new System.Collections.Generic.List<ModuleAxes>();
        Vector3 angularBalancePositive,angularBalanceNegative;
        public bool coreAlive;

        public static ShipPerformance CalculateAssembly(ShipAssembly assembly,FlightTuning tuning)
        {
            if(!assembly||!tuning)return new ShipPerformance();
            var blueprint=assembly.Export("flight performance preview");
            var records=blueprint.parts;
            var cells=new System.Collections.Generic.Dictionary<Vector3Int,int>();
            for(int i=0;i<records.Count;i++)
                foreach(var cell in BlockModules.BodyCells(records[i]))cells[cell]=i;
            var parts=new System.Collections.Generic.List<ShipPartState>(records.Count);
            foreach(var record in records)
            {
                int mask=0;var nozzles=BlockModules.LocalExhaustDirections(record.shape);
                for(int nozzle=0;nozzle<nozzles.Length&&nozzle<31;nozzle++)
                {
                    bool blocked=false;
                    foreach(var cell in BlockModules.ExhaustCells(record,nozzle))
                        if(cells.TryGetValue(cell,out int owner)&&records[owner].id!=record.id){blocked=true;break;}
                    if(!blocked)mask|=1<<nozzle;
                }
                var stats=tuning.Get(record.shape);
                parts.Add(new ShipPartState{record=record,hp=stats.maxHp,maxHp=stats.maxHp,availableExhaustMask=mask,thrustAvailable=mask!=0});
            }
            Quaternion coreRotation=Quaternion.identity;
            foreach(var record in records)if(record.shape==BlockShape.Core){coreRotation=record.rotation;break;}
            return Calculate(parts,coreRotation,tuning);
        }

        public static ShipPerformance Calculate(System.Collections.Generic.IEnumerable<ShipPartState> parts,Quaternion coreRotation,FlightTuning tuning)
        {
            var p=new ShipPerformance{maxSpeed=tuning.maxSpeed};var source=new System.Collections.Generic.List<ShipPartState>();
            foreach(var part in parts)if(part.present)source.Add(part);
            var inverse=Quaternion.Inverse(coreRotation);float totalMass=0;Vector3 weightedCenter=Vector3.zero;
            foreach(var part in source){var m=tuning.Get(part.record.shape);float mass=Mathf.Max(0,m.massKg);totalMass+=mass;weightedCenter+=(part.record.position+part.record.rotation*BlockModules.LocalCenter(part.record.shape))*mass;}
            p.totalMass=totalMass;float safeMass=Mathf.Max(1,totalMass);p.centerOfMass=totalMass>0?weightedCenter/totalMass:Vector3.zero;
            Vector3 centerInCore=inverse*p.centerOfMass;
            // Distribute each module's mass over its occupied unit cells. Unit cubes contribute
            // their own 1/6 m r^2 inertia in addition to the parallel-axis term.
            foreach(var part in source)
            {
                var m=tuning.Get(part.record.shape);p.totalHp+=m.maxHp;p.currentHp+=part.hp;
                if(part.record.shape==BlockShape.Core)p.coreAlive=true;
                var cells=BlockModules.LocalCells(part.record.shape);if(cells.Length==0)continue;
                float cellMass=Mathf.Max(0,m.massKg)/cells.Length;
                foreach(var cell in cells)
                {
                    var position=inverse*(part.record.position+part.record.rotation*(Vector3)cell)-centerInCore;
                    p.inertiaTensorKgM2.x+=cellMass*(position.y*position.y+position.z*position.z+1f/6f);
                    p.inertiaTensorKgM2.y+=cellMass*(position.x*position.x+position.z*position.z+1f/6f);
                    p.inertiaTensorKgM2.z+=cellMass*(position.x*position.x+position.y*position.y+1f/6f);
                }
            }
            Vector3 torquePositive=Vector3.zero,torqueNegative=Vector3.zero;
            foreach(var part in source)
            {
                float force=Mathf.Max(0,tuning.Get(part.record.shape).thrustNewtons);if(force<=0||!part.thrustAvailable)continue;
                var definition=tuning.GetDefinition(part.record.shape);
                var nozzles=definition&&definition.localExhaustDirections!=null&&definition.localExhaustDirections.Length>0?definition.localExhaustDirections:BlockModules.LocalExhaustDirections(part.record.shape);
                Vector3 position=inverse*(part.record.position+part.record.rotation*BlockModules.LocalCenter(part.record.shape))-centerInCore;
                Vector3 bestTranslationPositive=Vector3.zero,bestTranslationNegative=Vector3.zero,bestTorquePositive=Vector3.zero,bestTorqueNegative=Vector3.zero;
                var axes=new ModuleAxes();
                bool available=false;
                for(int nozzle=0;nozzle<nozzles.Length&&nozzle<31;nozzle++)
                {
                    if(!part.ExhaustAvailable(nozzle))continue;available=true;
                    var direction=(inverse*(part.record.rotation*(-nozzles[nozzle].normalized))).normalized;
                    var torque=Vector3.Cross(position,direction);
                    p.nozzles.Add(new ThrusterNozzle{part=part,index=nozzle,forceNewtons=force,forceDirection=direction,leverArm=position});
                    for(int axis=0;axis<3;axis++)
                    {
                        int index=p.nozzles.Count-1;
                        if(direction[axis]*force>bestTranslationPositive[axis])axes.linearPositiveNozzle[axis]=index;
                        if(-direction[axis]*force>bestTranslationNegative[axis])axes.linearNegativeNozzle[axis]=index;
                        if(torque[axis]*force>bestTorquePositive[axis])axes.torquePositiveNozzle[axis]=index;
                        if(-torque[axis]*force>bestTorqueNegative[axis])axes.torqueNegativeNozzle[axis]=index;
                        bestTranslationPositive[axis]=Mathf.Max(bestTranslationPositive[axis],Mathf.Max(0,direction[axis])*force);
                        bestTranslationNegative[axis]=Mathf.Max(bestTranslationNegative[axis],Mathf.Max(0,-direction[axis])*force);
                        bestTorquePositive[axis]=Mathf.Max(bestTorquePositive[axis],Mathf.Max(0,torque[axis])*force);
                        bestTorqueNegative[axis]=Mathf.Max(bestTorqueNegative[axis],Mathf.Max(0,-torque[axis])*force);
                    }
                }
                if(!available)continue;
                axes.linearPositive=bestTranslationPositive;axes.linearNegative=bestTranslationNegative;
                axes.torquePositive=bestTorquePositive;axes.torqueNegative=bestTorqueNegative;p.modules.Add(axes);
                p.totalThrust+=force;p.accelerationPositive+=bestTranslationPositive;p.accelerationNegative+=bestTranslationNegative;
                torquePositive+=bestTorquePositive;torqueNegative+=bestTorqueNegative;
            }
            // Keep this summary for older bot-navigation heuristics; player flight uses the
            // per-module axis allocation below and no longer reserves a fixed assist share.
            p.assistAcceleration=p.totalThrust*Mathf.Clamp01(tuning.maneuverThrustFraction)/safeMass;
            p.linearCapacityPositive=p.accelerationPositive/safeMass;p.linearCapacityNegative=p.accelerationNegative/safeMass;
            p.angularCapacityPositive=new Vector3(torquePositive.x/Mathf.Max(.001f,p.inertiaTensorKgM2.x),torquePositive.y/Mathf.Max(.001f,p.inertiaTensorKgM2.y),torquePositive.z/Mathf.Max(.001f,p.inertiaTensorKgM2.z))*Mathf.Rad2Deg;
            p.angularCapacityNegative=new Vector3(torqueNegative.x/Mathf.Max(.001f,p.inertiaTensorKgM2.x),torqueNegative.y/Mathf.Max(.001f,p.inertiaTensorKgM2.y),torqueNegative.z/Mathf.Max(.001f,p.inertiaTensorKgM2.z))*Mathf.Rad2Deg;
            // Use the weaker direction for both acceleration and braking on each rotation
            // axis. This is a stable performance model, not a free-running nozzle simulation.
            for(int axis=0;axis<3;axis++){
                float balanced=Mathf.Min(p.angularCapacityPositive[axis],p.angularCapacityNegative[axis]);
                p.angularBalancePositive[axis]=p.angularCapacityPositive[axis]>.000001f?balanced/p.angularCapacityPositive[axis]:0;
                p.angularBalanceNegative[axis]=p.angularCapacityNegative[axis]>.000001f?balanced/p.angularCapacityNegative[axis]:0;
                p.angularCapacityPositive[axis]=p.angularCapacityNegative[axis]=balanced;
            }
            p.accelerationPositive=p.linearCapacityPositive;p.accelerationNegative=p.linearCapacityNegative;
            for(int i=0;i<3;i++){p.accelerationPositive[i]=SoftLimit(p.accelerationPositive[i],tuning.maxLinearAcceleration);p.accelerationNegative[i]=SoftLimit(p.accelerationNegative[i],tuning.maxLinearAcceleration);}
            for(int axis=0;axis<3;axis++)
            {
                float inertia=Mathf.Max(.001f,p.inertiaTensorKgM2[axis]);
                p.angularAccelerationPositive[axis]=SoftLimit(p.angularCapacityPositive[axis],tuning.maxAngularAcceleration);
                p.angularAccelerationNegative[axis]=SoftLimit(p.angularCapacityNegative[axis],tuning.maxAngularAcceleration);
                p.angularRatePositive[axis]=RateFromAcceleration(p.angularAccelerationPositive[axis],tuning,axis);
                p.angularRateNegative[axis]=RateFromAcceleration(p.angularAccelerationNegative[axis],tuning,axis);
                p.maxSpeedStoppingDistancePositive[axis]=StoppingDistance(tuning.maxSpeed,p.accelerationNegative[axis]);
                p.maxSpeedStoppingDistanceNegative[axis]=StoppingDistance(tuning.maxSpeed,p.accelerationPositive[axis]);
            }
            // Pitch turns use vertical authority; yaw turns use lateral authority. Both the
            // rotational rate and the available centripetal acceleration bound the radius.
            for(int turn=0;turn<2;turn++)
            {
                int lateralAxis=turn==0?1:0,angularAxis=turn;
                float rate=Mathf.Min(p.angularRatePositive[angularAxis],p.angularRateNegative[angularAxis]);
                float lateralAcceleration=Mathf.Min(p.accelerationPositive[lateralAxis],p.accelerationNegative[lateralAxis]);
                float rateRadius=rate>.001f?tuning.maxSpeed/(rate*Mathf.Deg2Rad):float.PositiveInfinity;
                float accelerationRadius=lateralAcceleration>.001f?tuning.maxSpeed*tuning.maxSpeed/lateralAcceleration:float.PositiveInfinity;
                p.turningRadiusAtMaxSpeed[turn]=Mathf.Max(rateRadius,accelerationRadius);
            }
            p.pitchAcceleration=Mathf.Min(p.angularAccelerationPositive.x,p.angularAccelerationNegative.x);
            p.yawAcceleration=Mathf.Min(p.angularAccelerationPositive.y,p.angularAccelerationNegative.y);
            p.rollSpeed=Mathf.Min(p.angularRatePositive.z,p.angularRateNegative.z);
            p.angularRate=Mathf.Min(Mathf.Min(p.angularRatePositive.x,p.angularRateNegative.x),Mathf.Min(p.angularRatePositive.y,p.angularRateNegative.y));
            return p;
        }

        public Vector3 AllocateWrench(Vector3 linearDemand,Vector3 angularDemand,FlightTuning tuning,out Vector3 angularAcceleration)
        {
            for(int i=0;i<nozzles.Count;i++)nozzles[i].requestedThrottle=nozzles[i].allocatedThrottle=0;
            Vector3 linearAcceleration=Vector3.zero;angularAcceleration=Vector3.zero;
            float safeMass=Mathf.Max(1,totalMass);
            for(int i=0;i<modules.Count;i++){
                var module=modules[i];float load=0;
                for(int axis=0;axis<3;axis++){
                    float move=Mathf.Clamp(linearDemand[axis],-1,1),turn=Mathf.Clamp(angularDemand[axis],-1,1);
                    int linearIndex=(move>=0?module.linearPositiveNozzle:module.linearNegativeNozzle)[axis];
                    int angularIndex=(turn>=0?module.torquePositiveNozzle:module.torqueNegativeNozzle)[axis];
                    if(Available(linearIndex))load+=Mathf.Abs(move);
                    if(Available(angularIndex)&&angularCapacityPositive[axis]>0)load+=Mathf.Abs(turn);
                }
                float share=1/Mathf.Max(1,load);
                for(int axis=0;axis<3;axis++){
                    float move=Mathf.Clamp(linearDemand[axis],-1,1)*share,turn=Mathf.Clamp(angularDemand[axis],-1,1)*share;
                    int linearIndex=(move>=0?module.linearPositiveNozzle:module.linearNegativeNozzle)[axis];
                    int angularIndex=(turn>=0?module.torquePositiveNozzle:module.torqueNegativeNozzle)[axis];
                    if(Available(linearIndex)){
                        linearAcceleration[axis]+=move*(move>=0?module.linearPositive[axis]:module.linearNegative[axis])/safeMass;
                        nozzles[linearIndex].allocatedThrottle+=Mathf.Abs(move);
                    }
                    if(Available(angularIndex)&&angularCapacityPositive[axis]>0){
                        float balance=(turn>=0?angularBalancePositive:angularBalanceNegative)[axis];
                        angularAcceleration[axis]+=turn*(turn>=0?module.torquePositive[axis]:module.torqueNegative[axis])*balance/Mathf.Max(.001f,inertiaTensorKgM2[axis]);
                        nozzles[angularIndex].allocatedThrottle+=Mathf.Abs(turn);
                    }
                }
            }
            for(int axis=0;axis<3;axis++)
            {
                linearAcceleration[axis]=SoftLimitSigned(linearAcceleration[axis],tuning.maxLinearAcceleration);
                angularAcceleration[axis]=SoftLimitSigned(angularAcceleration[axis]*Mathf.Rad2Deg,tuning.maxAngularAcceleration);
            }
            return linearAcceleration;
        }

        bool Available(int index)=>index>=0&&index<nozzles.Count&&nozzles[index].part.ExhaustAvailable(nozzles[index].index);

        public float AllocatedThrottle(ShipPartState part,int nozzleIndex)
        {for(int i=0;i<nozzles.Count;i++)if(nozzles[i].part==part&&nozzles[i].index==nozzleIndex)return nozzles[i].allocatedThrottle;return 0;}

        static float SoftLimitSigned(float value,float ceiling)=>Mathf.Sign(value)*SoftLimit(Mathf.Abs(value),ceiling);
        // A smooth ceiling keeps an extra thruster or lost hull mass useful beyond the old hard cap.
        static float SoftLimit(float value,float ceiling){value=Mathf.Max(0,value);ceiling=Mathf.Max(0,ceiling);return ceiling>0?ceiling*(value/(ceiling+value)):0;}
        static float RateFromAcceleration(float acceleration,FlightTuning tuning,int axis)
        {
            float gain=axis==2?tuning.rollRateGain:tuning.angularRateGain;
            float ceiling=axis==2?tuning.maxRollRate:tuning.maxAngularRate;
            return SoftLimit(Mathf.Sqrt(Mathf.Max(0,acceleration))*Mathf.Max(0,gain)*1.6f,ceiling);
        }
        static float StoppingDistance(float speed,float deceleration)=>deceleration>.001f?speed*speed/(2*deceleration):float.PositiveInfinity;
    }
}
