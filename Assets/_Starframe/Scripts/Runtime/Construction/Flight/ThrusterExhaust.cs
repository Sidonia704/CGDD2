// @role Display each available engine nozzle independently with shared voxel exhaust meshes.
// @calls FlightShipRuntime, ShipFlightController, BlockModules, VoxelFxGeometry

using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Combat;

namespace Starframe.Flight
{
    /// <summary>A solid square nozzle and flowing cyan cube particles, one shared mesh for all nozzle instances.</summary>
    public sealed class ThrusterExhaust : MonoBehaviour
    {
        sealed class Engine
        {
            public ShipPartState part;
            public int index;
            public Vector3 localDirection;
            public Transform nozzle;
            public MeshRenderer renderer;
            public MaterialPropertyBlock properties;
            public float throttle, flowPhase;
            public Vector2 aperture;
        }
        readonly List<Engine> engines = new List<Engine>();
        ShipFlightController flight;
        FlightTuning tuning;
        Material glow;
        float visualScale=1;
        // The five-way module renders a full cell of flame; its placement judgement is half a cell.
        const float ManeuverPlumeRange=1f;
        static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity"), AxialShrinkId = Shader.PropertyToID("_AxialShrink");
        static readonly int FlowEnabledId = Shader.PropertyToID("_FlowEnabled"), FlowPhaseId = Shader.PropertyToID("_FlowPhase");
        static readonly int ProfileId=Shader.PropertyToID("_EffectProfile"),HeadColorId=Shader.PropertyToID("_HeadColor");

        public void Initialize(FlightShipRuntime runtime, ShipFlightController controller, FlightTuning values, Camera camera,float effectScale=1)
        {
            ReleaseResources();
            flight = controller; tuning = values;
            visualScale=Mathf.Max(.1f,effectScale);
            glow = VoxelFxGeometry.CreateGlow(values.exhaustShader);
            if (!glow) { enabled = false; return; }
            enabled = true;
            foreach (var part in runtime.Parts)
            {
                if (!BlockModules.IsThruster(part.record.shape)) continue;
                var definition=values.GetDefinition(part.record.shape);var mesh=definition?definition.mesh:null;
                var directions=BlockModules.LocalExhaustDirections(part.record.shape);
                for(int index=0;index<directions.Length;index++)
                {
                    var go=new GameObject("VoxelThrusterExhaust",typeof(MeshFilter),typeof(MeshRenderer));var nozzle=go.transform;
                    nozzle.SetParent(part.view.transform,false);nozzle.localPosition=BlockModules.LocalNozzlePosition(part.record.shape,index,mesh);
                    nozzle.localRotation=Quaternion.FromToRotation(Vector3.up,directions[index].normalized);
                    go.GetComponent<MeshFilter>().sharedMesh=VoxelFxGeometry.Plume;
                    var renderer=go.GetComponent<MeshRenderer>();VoxelFxGeometry.Configure(renderer,glow);renderer.enabled=false;
                    engines.Add(new Engine{part=part,index=index,localDirection=directions[index].normalized,nozzle=nozzle,renderer=renderer,properties=new MaterialPropertyBlock(),
                        aperture=BlockModules.LocalNozzleAperture(part.record.shape,index,mesh),flowPhase=engines.Count*.137f%1});
                }
            }
        }

        void LateUpdate()
        {
            if (!flight || !tuning) return;
            float blend = 1 - Mathf.Exp(-Mathf.Max(1,tuning.exhaustResponse) * Time.deltaTime);
            foreach (var e in engines)
            {
                if (!e.part.present || !e.part.view || !e.part.ExhaustAvailable(e.index))
                {
                    e.throttle = 0;
                    if (e.renderer) e.renderer.enabled = false;
                    continue;
                }
                float throttle = Mathf.Max(tuning.exhaustIdle,flight.ThrusterThrottle(e.part,e.index));
                e.throttle = Mathf.Lerp(e.throttle,throttle,blend);
                float force = Mathf.Max(0,tuning.Get(e.part.record.shape).thrustNewtons);
                float scale = Mathf.Sqrt(force / Mathf.Max(1,tuning.exhaustReferenceThrust));
                bool visible = e.throttle > .002f && scale > 0 && flight.Performance.coreAlive;
                e.renderer.enabled = visible;
                if (!visible) continue;
                float length = Mathf.Max(.01f,Mathf.Min(tuning.exhaustMaxLength,tuning.exhaustLength * scale * (.25f + .75f * Mathf.Sqrt(e.throttle))));
                if(e.part.record.shape==BlockShape.Thruster6)length=Mathf.Min(ManeuverPlumeRange,length);
                float width = Mathf.Min(length * .45f,Mathf.Max(.01f,tuning.exhaustWidth * (.8f + .2f * scale) * Mathf.Sqrt(e.throttle)));
                float crossScale=Mathf.Max(1,Mathf.Sqrt(e.aperture.x*e.aperture.y));
                float widthX=width*Mathf.Clamp(e.aperture.x,.5f,4),widthZ=width*Mathf.Clamp(e.aperture.y,.5f,4);
                if(e.part.record.shape==BlockShape.Thruster6){widthX=Mathf.Min(widthX,.3f);widthZ=Mathf.Min(widthZ,.3f);}
                var nozzleScale=new Vector3(widthX,length*Mathf.Sqrt(crossScale),widthZ)*visualScale;
                if(e.part.record.shape==BlockShape.Thruster6){nozzleScale.x=Mathf.Min(.3f,nozzleScale.x);nozzleScale.z=Mathf.Min(.3f,nozzleScale.z);nozzleScale.y=Mathf.Min(ManeuverPlumeRange,nozzleScale.y);}
                e.nozzle.localScale=nozzleScale;
                // Keep each trailing voxel cubic while the centres spread with thrust.
                e.properties.SetFloat(AxialShrinkId,e.part.record.shape==BlockShape.Thruster6?Mathf.Max(nozzleScale.x,nozzleScale.z)/Mathf.Max(.001f,nozzleScale.y):width / length);
                float flowSpeed = Mathf.Max(.05f,tuning.exhaustFlowSpeed) * (.35f + .65f * Mathf.Sqrt(e.throttle)) * Mathf.Sqrt(scale);
                // Integrate the phase once per nozzle so changing throttle cannot jump particles backwards.
                e.flowPhase = (e.flowPhase + Time.deltaTime * flowSpeed) % 1;
                e.properties.SetFloat(FlowEnabledId,1);
                e.properties.SetFloat(FlowPhaseId,e.flowPhase);
                e.properties.SetFloat(ProfileId,2);
                e.properties.SetColor(HeadColorId,tuning.exhaustInnerColor);
                e.properties.SetColor(ColorId,tuning.exhaustOuterColor);
                e.properties.SetFloat(IntensityId,Mathf.Max(0,tuning.exhaustIntensity) * Mathf.Sqrt(scale) * e.throttle);
                e.renderer.SetPropertyBlock(e.properties);
            }
        }

        static void Release(Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        void OnDestroy() => ReleaseResources();
        void ReleaseResources()
        {
            foreach (var e in engines) if (e.nozzle) Release(e.nozzle.gameObject);
            engines.Clear();
            if (glow) Release(glow);
            glow = null;
        }
    }
}
