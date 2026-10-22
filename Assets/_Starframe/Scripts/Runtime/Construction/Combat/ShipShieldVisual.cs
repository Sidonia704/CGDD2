// @role Render shield fields and hit feedback from runtime shield state.
// @calls FlightShipRuntime

using System.Collections.Generic;
using Starframe.Flight;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.Combat
{
    public sealed class ShipShieldVisual:MonoBehaviour
    {
        FlightShipRuntime ship;
        readonly List<MeshRenderer> shells=new List<MeshRenderer>();
        MaterialPropertyBlock properties;
        void Awake()=>properties=new MaterialPropertyBlock();
        public void Initialize(FlightShipRuntime runtime)=>ship=runtime;
        public static MeshRenderer CreateSurface(Transform parent,string name="EnergyShield",Mesh surfaceMesh=null)
        {
            GameObject go;
            if(surfaceMesh)
            {
                go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh=surfaceMesh;
            }
            else
            {
                go=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var collider=go.GetComponent<Collider>();collider.enabled=false;
                if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
            }
            go.name=name;go.layer=2;
            go.transform.SetParent(parent,false);
            var renderer=go.GetComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            go.AddComponent<ShieldSurfaceVisibility>();
            return renderer;
        }
        void LateUpdate()
        {
            if(!ship)return;
            var fields=ship.Shields.Fields;
            while(shells.Count<fields.Count)
            {
                shells.Add(CreateSurface(transform,"EnergyShield",fields[shells.Count].definition.shieldSurfaceMesh));
            }
            for(int i=0;i<shells.Count;i++)
            {
                var renderer=shells[i];
                if(i>=fields.Count){renderer.enabled=false;continue;}
                var f=fields[i];renderer.enabled=f.Active&&f.charge>.001f&&f.definition.shieldSurfaceMaterial;
                if(!renderer.enabled)continue;
                renderer.sharedMaterial=f.definition.shieldSurfaceMaterial;
                renderer.transform.position=f.part.view?f.part.view.transform.position:f.Center;
                renderer.transform.localScale=Vector3.one*(2*f.definition.shieldRadius);
                properties.Clear();properties.SetFloat("_ChargeRatio",f.charge/Mathf.Max(1,f.definition.shieldCapacity));
                properties.SetFloat("_HitPulse",Mathf.Clamp01(1-(float)(ship.CombatTime-f.lastHit)/.45f));
                properties.SetVector("_HitPosition",f.lastHitPosition);renderer.SetPropertyBlock(properties);
            }
        }
    }
}
