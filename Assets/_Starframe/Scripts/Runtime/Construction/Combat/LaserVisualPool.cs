// @role Pool muzzle-clipped tracers with family-specific single kinetic cores or layered green lasers and fixed-endpoint impact fading.
// @calls VoxelFxGeometry

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Flight;

namespace Starframe.Combat
{
    /// <summary>Pooled solid voxel laser segments with a moving muzzle boundary.</summary>
    public sealed class LaserVisualPool : MonoBehaviour
    {
        sealed class Visual
        {
            public Transform transform;
            public MeshRenderer renderer,coreRenderer;
            public MeshFilter filter;
            public Transform core;
            public MaterialPropertyBlock coreProperties;
            public bool continuous,lingering;
            public WeaponFamily family;
            public float lingerSeconds,lingerRemaining,lastLength;
            public MaterialPropertyBlock properties;
            public int handle;
            public float length;
            public float tailLength;
            public float width,minimumScreenPixels;
            public float intensity;
            public Color color;public bool legacyColour;
            public Vector3 direction;
            public Vector3 origin,position,muzzleLocalPosition;
            public Transform muzzleSource;
        }

        readonly List<Visual> visuals = new List<Visual>();
        readonly Stack<int> freeSlots = new Stack<int>();
        readonly Dictionary<int, int> activeHandles = new Dictionary<int, int>();
        Mesh sharedMesh;
        Material sharedMaterial;
        int nextHandle = 1;
        float visualScale=1;
        readonly List<int> expired=new List<int>();
        public int ActiveVisualCount=>activeHandles.Count;

        static readonly int OpacityId=Shader.PropertyToID("_Opacity");
        static readonly int HeadColorId=Shader.PropertyToID("_HeadColor");
        static readonly int TailFractionId = Shader.PropertyToID("_TailFraction");
        static readonly int ProfileId=Shader.PropertyToID("_EffectProfile");
        static readonly int AxialShrinkId = Shader.PropertyToID("_AxialShrink"), ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

        public void Initialize(Shader shader,float effectScale=1)
        {

            if (sharedMaterial) Destroy(sharedMaterial);
            sharedMesh = null;
            sharedMaterial = null;
            activeHandles.Clear();
            freeSlots.Clear();
            for (int i = 0; i < visuals.Count; i++)
                if (visuals[i].transform) Destroy(visuals[i].transform.gameObject);
            visuals.Clear();

            sharedMaterial = VoxelFxGeometry.CreateGlow(shader);
            visualScale=Mathf.Max(.1f,effectScale);
            if (!sharedMaterial)
            {
                enabled = false;
                return;
            }

            enabled = true;

            sharedMesh = VoxelFxGeometry.Beam;
        }

        public int Acquire(Vector3 position, Vector3 direction, float diameter, float visualLength, float tailLength, Transform muzzleSource=null, Vector3 muzzleLocalPosition=default,float widthScale=.55f,float intensity=4.2f,Color? color=null,float minimumScreenPixels=0,TracerStyle style=TracerStyle.Voxel,float lingerSeconds=0,float maximumWorldLength=0,WeaponFamily family=WeaponFamily.Generic)
        {
            if (!sharedMaterial || !sharedMesh) return -1;
            int slot;
            Visual visual;
            if (freeSlots.Count > 0)
            {
                slot = freeSlots.Pop();
                visual = visuals[slot];
            }
            else
            {
                slot = visuals.Count;
                visual = CreateVisual(slot);
                visuals.Add(visual);
            }

            int handle = nextHandle++;
            if (nextHandle <= 0) nextHandle = 1;
            while (activeHandles.ContainsKey(handle))
            {
                handle = nextHandle++;
                if (nextHandle <= 0) nextHandle = 1;
            }
            visual.handle = handle;
            visual.family=family;
            visual.continuous=style==TracerStyle.Continuous;visual.lingering=false;visual.lingerSeconds=Mathf.Max(0,lingerSeconds);visual.lingerRemaining=0;
            visual.filter.sharedMesh=visual.continuous?VoxelFxGeometry.Cube:sharedMesh;
            bool singleCore=visual.continuous&&(family==WeaponFamily.Gatling||family==WeaponFamily.Railgun);
            if(visual.continuous&&!singleCore&&!visual.coreRenderer){
                var core=new GameObject("Continuous tracer white core",typeof(MeshFilter),typeof(MeshRenderer));core.transform.SetParent(visual.transform,false);
                core.GetComponent<MeshFilter>().sharedMesh=VoxelFxGeometry.Cube;visual.core=core.transform;visual.coreRenderer=core.GetComponent<MeshRenderer>();
                VoxelFxGeometry.Configure(visual.coreRenderer,sharedMaterial);visual.coreProperties=new MaterialPropertyBlock();
            }
            if(visual.coreRenderer)visual.coreRenderer.enabled=false;
            // The source projectile is a small simulation point; give its rendered beam a broad, readable body.
            visual.width = Mathf.Max(.001f, diameter * Mathf.Max(.01f,widthScale)*visualScale);
            visual.minimumScreenPixels=Mathf.Max(0,minimumScreenPixels);
            visual.intensity=Mathf.Max(0,intensity);visual.color=color??Color.white;visual.legacyColour=!color.HasValue;
            visual.length = Mathf.Max(.001f, visualLength*visualScale);
            visual.tailLength = Mathf.Max(0f, tailLength*visualScale);
            // Apply after global effect scaling so one Gatling shot leaves a real
            // gap before the next; preheating changes density, never tracer length.
            float totalLength=visual.length+visual.tailLength;
            if(maximumWorldLength>0&&maximumWorldLength<totalLength){float factor=maximumWorldLength/totalLength;visual.length*=factor;visual.tailLength*=factor;}
            visual.origin=position;visual.muzzleSource=muzzleSource;visual.muzzleLocalPosition=muzzleLocalPosition;
            activeHandles.Add(handle, slot);
            MoveVisual(visual, position, direction);
            return handle;
        }

        public void Move(int handle, Vector3 position, Vector3 direction)
        {
            if (activeHandles.TryGetValue(handle, out int slot)&&!visuals[slot].lingering)
                MoveVisual(visuals[slot], position, direction);
        }

        public void Release(int handle)
        {
            if(!activeHandles.TryGetValue(handle,out int slot))return;
            var visual=visuals[slot];if(visual.lingering)return;
            if(visual.lingerSeconds>0&&visual.renderer.enabled&&visual.lastLength>.0001f){
                visual.lingering=true;visual.lingerRemaining=visual.lingerSeconds;
                // Freeze the last physical sweep segment. No later muzzle movement or destruction may stretch it.
                visual.origin=visual.position-visual.direction*visual.lastLength;visual.muzzleSource=null;
                return;
            }
            Recycle(handle,slot);
        }
        void Recycle(int handle,int slot)
        {
            activeHandles.Remove(handle);var visual=visuals[slot];visual.renderer.enabled=false;
            if(visual.coreRenderer)visual.coreRenderer.enabled=false;
            visual.handle=0;visual.lingering=false;freeSlots.Push(slot);
        }

        public void Clear()
        {
            activeHandles.Clear();
            freeSlots.Clear();
            for (int i = 0; i < visuals.Count; i++)
            {
                Visual visual = visuals[i];
                if (visual.renderer) visual.renderer.enabled = false;
                if(visual.coreRenderer)visual.coreRenderer.enabled=false;
                visual.lingering=false;visual.lingerRemaining=0;
                visual.handle = 0;
                freeSlots.Push(i);
            }
        }

        Visual CreateVisual(int slot)
        {
            var go = new GameObject("LaserVisual_" + slot, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = sharedMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return new Visual { transform = go.transform, renderer = renderer, filter=go.GetComponent<MeshFilter>(), properties = new MaterialPropertyBlock(), direction = Vector3.forward };
        }

        /// <summary>Only the visible cross section changes; hit volume and axial muzzle clipping remain authoritative.</summary>
        public static float MinimumReadableWidth(Camera camera,Vector3 position,float baseWidth,float minimumPixels)
        {
            baseWidth=Mathf.Max(.001f,baseWidth);
            if(!camera||minimumPixels<=0||camera.pixelHeight<=0)return baseWidth;
            float depth=Vector3.Dot(position-camera.transform.position,camera.transform.forward);
            if(depth<=0)return baseWidth;
            float worldHeight=camera.orthographic?2*camera.orthographicSize:2*depth*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad);
            float required=minimumPixels*worldHeight/camera.pixelHeight;
            return Mathf.Clamp(required,baseWidth,baseWidth*10);
        }

        static void MoveVisual(Visual visual, Vector3 position, Vector3 direction)
        {
            visual.position=position;
            if (direction.sqrMagnitude > 1e-8f) visual.direction = direction.normalized;
            // The visible column grows with travel; neither its body nor its tail may extend behind the muzzle.
            float available=Mathf.Max(0,Vector3.Dot(position-visual.origin,visual.direction));
            if(visual.muzzleSource)available=Mathf.Min(available,Mathf.Max(0,Vector3.Dot(position-visual.muzzleSource.TransformPoint(visual.muzzleLocalPosition),visual.direction)));
            float fullLength = Mathf.Min(visual.length + visual.tailLength,available);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, visual.direction);
            visual.lastLength=fullLength;
            visual.transform.SetPositionAndRotation(position - visual.direction * fullLength*(visual.continuous?.5f:1), rotation);
            float baselineDisplayWidth=visual.minimumScreenPixels>0?MinimumReadableWidth(Camera.main,position,visual.width,visual.minimumScreenPixels):visual.width;
            bool singleCore=visual.continuous&&(visual.family==WeaponFamily.Gatling||visual.family==WeaponFamily.Railgun);
            bool gatling=singleCore&&visual.family==WeaponFamily.Gatling;
            bool rapidLaser=visual.continuous&&visual.family==WeaponFamily.RapidLaser;
            bool energy=visual.family==WeaponFamily.RapidLaser||visual.family==WeaponFamily.BeamLaser;
            float displayWidth=baselineDisplayWidth*(singleCore?.24f*.8f:1);
            float fade=visual.lingering?Mathf.Clamp01(visual.lingerRemaining/Mathf.Max(.001f,visual.lingerSeconds)):1;
            visual.transform.localScale = new Vector3(displayWidth, fullLength, displayWidth);
            visual.properties.SetFloat(AxialShrinkId,fullLength>.0001f?displayWidth/(.06f*fullLength):1);
            visual.properties.SetFloat(ProfileId,visual.continuous?4:visual.legacyColour?1:3);
            visual.properties.SetFloat(OpacityId,fade*(rapidLaser?.65f:1));
            visual.properties.SetColor(ColorId,singleCore&&!gatling?Color.Lerp(visual.color,Color.white,.82f):visual.color);
            visual.properties.SetColor(HeadColorId,gatling?visual.color:singleCore?Color.white:Color.Lerp(visual.color,Color.white,energy?.08f:.85f));
            visual.properties.SetFloat(IntensityId,visual.intensity*(singleCore&&!gatling?1.25f:energy?.8f:1));
            visual.properties.SetFloat(TailFractionId,!visual.continuous&&fullLength>.0001f?Mathf.Max(0,fullLength-visual.length)/fullLength:0);
            visual.renderer.SetPropertyBlock(visual.properties);visual.renderer.enabled=fullLength>.0001f;
            if(visual.coreRenderer){
                visual.coreRenderer.enabled=visual.continuous&&!singleCore&&visual.renderer.enabled;
                if(visual.continuous&&!singleCore){visual.core.localPosition=Vector3.zero;visual.core.localRotation=Quaternion.identity;
                    float coreWidth=rapidLaser?.36f:.24f;visual.core.localScale=new Vector3(coreWidth,1,coreWidth);
                    visual.coreProperties.SetFloat(ProfileId,4);visual.coreProperties.SetColor(ColorId,Color.Lerp(visual.color,Color.white,energy?.08f:.82f));
                    visual.coreProperties.SetColor(HeadColorId,energy?Color.Lerp(visual.color,Color.white,.12f):Color.white);
                    visual.coreProperties.SetFloat(IntensityId,visual.intensity*(energy?.9f:1.25f));visual.coreProperties.SetFloat(OpacityId,fade);
                    visual.coreRenderer.SetPropertyBlock(visual.coreProperties);
                }
            }
        }

        public void StepVisuals(float dt)
        {
            expired.Clear();
            foreach(var pair in activeHandles){
                var visual=visuals[pair.Value];
                if(visual.lingering){
                    visual.lingerRemaining-=Mathf.Max(0,dt);
                    if(visual.lingerRemaining<=0){expired.Add(pair.Key);continue;}
                }
                if(visual.lingering||visual.muzzleSource||visual.minimumScreenPixels>0)MoveVisual(visual,visual.position,visual.direction);
            }
            foreach(int handle in expired)if(activeHandles.TryGetValue(handle,out int slot))Recycle(handle,slot);
        }
        void LateUpdate()=>StepVisuals(Time.deltaTime);

        void OnDestroy()
        {

            if (sharedMaterial) Destroy(sharedMaterial);
        }
    }
}

