// @role Display engine output with shared voxel exhaust meshes.
// @calls FlightShipRuntime, ShipFlightController, VoxelFxGeometry

using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Combat;

namespace Starframe.Flight
{
    /// <summary>A solid square nozzle and flowing cyan cube particles, one shared mesh per engine.</summary>
    public sealed class ThrusterExhaust : MonoBehaviour
    {
        sealed class Engine
        {
            public ShipPartState part;
            public Transform nozzle;
            public MeshRenderer renderer;
            public MaterialPropertyBlock properties;
            public float throttle, flowPhase;
        }
        readonly List<Engine> engines = new List<Engine>();
        ShipFlightController flight;
        FlightTuning tuning;
        Material glow;
        float visualScale=1;
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
                var go = new GameObject("VoxelThrusterExhaust",typeof(MeshFilter),typeof(MeshRenderer));
                var nozzle = go.transform;
                nozzle.SetParent(part.view.transform,false);
                nozzle.localPosition = Vector3.up * (BlockModules.BodyLength(part.record.shape) - .5f + .015f);
                go.GetComponent<MeshFilter>().sharedMesh = VoxelFxGeometry.Plume;
                var renderer = go.GetComponent<MeshRenderer>();
                VoxelFxGeometry.Configure(renderer,glow); renderer.enabled = false;
                engines.Add(new Engine { part = part, nozzle = nozzle, renderer = renderer, properties = new MaterialPropertyBlock(), flowPhase = engines.Count * .137f % 1 });
            }
        }

        void LateUpdate()
        {
            if (!flight || !tuning) return;
            float blend = 1 - Mathf.Exp(-Mathf.Max(1,tuning.exhaustResponse) * Time.deltaTime);
            foreach (var e in engines)
            {
                if (!e.part.present || !e.part.view || !e.part.thrustAvailable)
                {
                    e.throttle = 0;
                    if (e.renderer) e.renderer.enabled = false;
                    continue;
                }
                Vector3 direction = flight.transform.InverseTransformDirection(-e.part.view.transform.up);
                float throttle = Mathf.Max(tuning.exhaustIdle,flight.ThrusterThrottle(direction));
                e.throttle = Mathf.Lerp(e.throttle,throttle,blend);
                float force = Mathf.Max(0,tuning.Get(e.part.record.shape).thrustNewtons);
                float scale = Mathf.Sqrt(force / Mathf.Max(1,tuning.exhaustReferenceThrust));
                bool visible = e.throttle > .002f && scale > 0 && flight.Performance.coreAlive;
                e.renderer.enabled = visible;
                if (!visible) continue;
                float length = Mathf.Max(.01f,Mathf.Min(tuning.exhaustMaxLength,tuning.exhaustLength * scale * (.25f + .75f * Mathf.Sqrt(e.throttle))));
                float width = Mathf.Min(length * .45f,Mathf.Max(.01f,tuning.exhaustWidth * (.8f + .2f * scale) * Mathf.Sqrt(e.throttle)));
                e.nozzle.localScale = new Vector3(width,length,width)*visualScale;
                // Keep each trailing voxel cubic while the centres spread with thrust.
                e.properties.SetFloat(AxialShrinkId,width / length);
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

        void OnDestroy() => ReleaseResources();
        void ReleaseResources()
        {
            foreach (var e in engines) if (e.nozzle) Destroy(e.nozzle.gameObject);
            engines.Clear();
            if (glow) Destroy(glow);
            glow = null;
        }
    }
}
