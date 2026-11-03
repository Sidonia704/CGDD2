// @role Define flight limits, steering response, range bounds and performance aggregation.
// @calls ModuleDatabase
// @tune maneuverThrustFraction:float:0-0.8:Share of total thrust assigned to manoeuvre assistance.
// @tune maxSpeed:float:0+:Soft flight speed ceiling in metres per second.
// @tune maxLinearAcceleration:float:0+:Soft linear acceleration ceiling.
// @tune boostPowerWatts:float:0+:Requested power while holding boost.
// @tune boostMultiplier:float:1+:Boost multiplier at full allocated power.
// @tune mouseDegreesPerPixel:float:0+:Virtual-stick sensitivity before the settings multiplier.
// @tune maxAimOffsetDegrees:float:5-45:Maximum persistent virtual-stick offset.
// @tune aimInputDeadZone:float:0-0.4:Dead zone as a fraction of stick radius.
// @tune aimResponseExponent:float:0.5-3:Response curve around the stick centre.
// @tune aimInputSmoothing:float:0-0.15:Steering smoothing duration.
// @tune turnResponseTime:float:0.05-0.5:Turn response duration.
// @tune arenaHalfExtent:float:0+:Half-width of the test range in metres.

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
        [HideInInspector]
        public ModuleStats[] modules={
            new ModuleStats(BlockShape.Cube,300,60),new ModuleStats(BlockShape.Wedge,150,30),
            new ModuleStats(BlockShape.Thruster1,600,100,80000),new ModuleStats(BlockShape.Thruster2,1400,200,200000),
            new ModuleStats(BlockShape.Slab,150,30),new ModuleStats(BlockShape.Pyramid,100,20),new ModuleStats(BlockShape.Core,2000,300)};
        [Header("Flight / simple shared manoeuvre authority")]
        [Range(0,.8f)]public float maneuverThrustFraction=.2f;
        public float maxSpeed=250,maxLinearAcceleration=100;
        [Tooltip("Angular acceleration / rate per metre per second squared of manoeuvre thrust. Limits are gradual soft ceilings.")]
        public float angularAccelerationGain=10,maxAngularAcceleration=360,angularRateGain=4,maxAngularRate=150;
        public float rollRateGain=5,maxRollRate=180,rollBlendTime=.25f;
        [Tooltip("Acceleration at which angular authority uses the configured gains directly.")]
        [Min(.01f)]public float angularAuthorityReferenceAcceleration=10;
        [Tooltip("Above 1 makes thrust-to-mass changes more pronounced for angular manoeuvring.")]
        [Range(.5f,2)]public float angularAuthorityExponent=1.1f;
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
        [Range(.05f,.5f)]public float turnResponseTime=.14f;
        [Range(0,1)]public float minimumTurnAuthority=.75f;
        public float reticleSharpness=30,reticleMaxSpeed=1800;
        [Header("Third person camera")]
        public float cameraDistance=18,minCameraDistance=6,maxCameraDistance=70;
        [HideInInspector]public float cameraHeight=5,cameraLookAhead=7;
        public float cameraPositionSharpness=10,cameraRotationSharpness=12,cameraZoomSensitivity=.15f,cameraFieldOfView=65;
        [Tooltip("Ship centre in the viewport: 0 is the bottom, .5 is the screen centre.")]
        [Range(.15f,.45f)]public float cameraShipViewportY=.30f;
        [Header("Flowing white / cyan / blue voxel exhaust")]
        public Shader exhaustShader;
        public float exhaustReferenceThrust=80000,exhaustLength=3.2f,exhaustMaxLength=9,exhaustWidth=.7f;
        public float exhaustIntensity=3,exhaustResponse=14;
        [Tooltip("Exhaust particle travel cycles per second at full throttle and reference thrust.")]
        [Min(.05f)]public float exhaustFlowSpeed=1.6f;
        [Range(0,.2f)]public float exhaustIdle=0;
        [Range(.1f,1)]public float exhaustInnerWidth=.38f,exhaustInnerLength=.72f;
        [ColorUsage(false,true)]public Color exhaustOuterColor=new Color(.015f,.3f,.85f);
        [ColorUsage(false,true)]public Color exhaustInnerColor=new Color(.8f,.95f,1.6f);
        [Header("3000 metre range / grid references")]
        public float arenaHalfExtent=1500,boundaryFadeDistance=220,boundaryBrakeAcceleration=80;
        public float gridWallSpacing=200,gridCellSize=4,gridFadeNear=30,gridFadeFar=130,gridPatchHalfExtent=220;
        public int Revision {get;private set;}
        void OnValidate(){Revision++;}
        public ModuleDefinition GetDefinition(BlockShape shape)=>database?database.Get(shape):null;
        public ModuleStats Get(BlockShape shape){var d=GetDefinition(shape);if(d)return d.Stats;foreach(var m in modules)if(m.shape==shape)return m;return new ModuleStats(shape,300,60);}
    }
    [Serializable] public sealed class ShipPartState
    {
        public BlockRecord record;public GameObject view;public float hp,maxHp,rebuildProgress;public bool present=true,thrustAvailable=true,detached;
    }
    public sealed class ShipPerformance
    {
        public float totalMass,totalHp,currentHp,totalThrust,assistAcceleration,pitchAcceleration,yawAcceleration,rollSpeed,angularRate,maxSpeed;
        public Vector3 accelerationPositive,accelerationNegative;
        public bool coreAlive;
        public static ShipPerformance Calculate(System.Collections.Generic.IEnumerable<ShipPartState> parts,Quaternion coreRotation,FlightTuning tuning)
        {
            var p=new ShipPerformance{maxSpeed=tuning.maxSpeed};var inverse=Quaternion.Inverse(coreRotation);
            foreach(var part in parts)
            {
                if(!part.present)continue;var m=tuning.Get(part.record.shape);
                p.totalMass+=m.massKg;p.totalHp+=m.maxHp;p.currentHp+=part.hp;
                if(part.record.shape==BlockShape.Core)p.coreAlive=true;
                float force=part.thrustAvailable?Mathf.Max(0,m.thrustNewtons):0;p.totalThrust+=force;
                var direction=inverse*(part.record.rotation*Vector3.down);
                p.accelerationPositive+=new Vector3(Mathf.Max(0,direction.x),Mathf.Max(0,direction.y),Mathf.Max(0,direction.z))*force;
                p.accelerationNegative+=new Vector3(Mathf.Max(0,-direction.x),Mathf.Max(0,-direction.y),Mathf.Max(0,-direction.z))*force;
            }
            float mass=Mathf.Max(1,p.totalMass);
            float mainShare=1-Mathf.Clamp01(tuning.maneuverThrustFraction);
            p.accelerationPositive*=mainShare/mass;p.accelerationNegative*=mainShare/mass;
            for(int i=0;i<3;i++){p.accelerationPositive[i]=SoftLimit(p.accelerationPositive[i],tuning.maxLinearAcceleration);p.accelerationNegative[i]=SoftLimit(p.accelerationNegative[i],tuning.maxLinearAcceleration);}
            p.assistAcceleration=p.totalThrust*Mathf.Clamp01(tuning.maneuverThrustFraction)/mass;
            float reference=Mathf.Max(.01f,tuning.angularAuthorityReferenceAcceleration);
            float authority=reference*Mathf.Pow(p.assistAcceleration/reference,Mathf.Max(.1f,tuning.angularAuthorityExponent));
            p.pitchAcceleration=p.yawAcceleration=SoftLimit(authority*tuning.angularAccelerationGain,tuning.maxAngularAcceleration);
            p.angularRate=SoftLimit(authority*tuning.angularRateGain,tuning.maxAngularRate);
            p.rollSpeed=SoftLimit(authority*tuning.rollRateGain,tuning.maxRollRate);
            return p;
        }
        // A smooth ceiling keeps an extra thruster or lost hull mass useful beyond the old hard cap.
        static float SoftLimit(float value,float ceiling){value=Mathf.Max(0,value);ceiling=Mathf.Max(0,ceiling);return ceiling>0?ceiling*(value/(ceiling+value)):0;}
    }
}
