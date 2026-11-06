// @role Present frame-interpolated yellow flares and continuous burst flashes, preserving fixed-step decoy movement.
// @calls FlareDefinition, FlightShipRuntime, VoxelFxGeometry

using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;

namespace Starframe.Combat
{
    public sealed class FlareBurst
    {
        public int Id;
        public FlightShipRuntime Owner;
        public int OwnerLifeId;
        public Vector3 Position;
        public bool Active;
        public FlareDefinition Definition;
        internal float age,lifetime;
        internal readonly List<FlareWorld.Spark> sparks=new List<FlareWorld.Spark>();
    }

    public sealed class FlareWorld:MonoBehaviour
    {
        internal sealed class Spark
        {
            public Transform transform;
            public MeshRenderer renderer;
            public Vector3 velocity,simulationPosition,visualOrigin,inherited,visualForward;
            
            public float size,burstAt,glow;
            public readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        }
        static readonly int ColorId=Shader.PropertyToID("_Color"),IntensityId=Shader.PropertyToID("_Intensity"),OpacityId=Shader.PropertyToID("_Opacity");
        static readonly Color Yellow=new Color(1,.75f,0);
        readonly List<FlareBurst> bursts=new List<FlareBurst>();
        readonly Stack<FlareBurst> freeBursts=new Stack<FlareBurst>();
        readonly Stack<Spark> freeSparks=new Stack<Spark>();
        sealed class VisualSalvo { public float age,previousAge,lifetime;public readonly List<Spark> sparks=new List<Spark>(); }
        readonly List<VisualSalvo> visuals=new List<VisualSalvo>();
        readonly Stack<VisualSalvo> freeVisuals=new Stack<VisualSalvo>();
        Material glow;
        float scale=1;
        int nextId;
        public IReadOnlyList<FlareBurst> Bursts=>bursts;
        public int ActiveSparkCount{get;private set;}
        public int PooledSparkCount{get;private set;}

        public void Initialize(float particleScale=1)
        {
            scale=Mathf.Max(.01f,particleScale);
            if(!glow){glow=VoxelFxGeometry.CreateGlow();if(glow)glow.name="Shared yellow flare glow";}
        }

        public FlareBurst EmitBurst(FlightShipRuntime owner,FlareDefinition definition,Vector3 position,Vector3 up,Vector3 inheritedVelocity)
        {
            if(!owner||!definition||up.sqrMagnitude<.000001f)return null;
            if(!glow)Initialize(scale);
            var burst=freeBursts.Count>0?freeBursts.Pop():new FlareBurst();
            // Missiles retain the ID, never the pool slot, so a recycled salvo cannot be tested twice.
            burst.Id=++nextId;burst.Owner=owner;burst.OwnerLifeId=owner.LifeId;burst.Definition=definition;
            burst.Position=position;burst.Active=true;burst.age=0;burst.lifetime=Mathf.Max(.01f,definition.lifetime);
            var visual=freeVisuals.Count>0?freeVisuals.Pop():new VisualSalvo();visual.age=visual.previousAge=0;visual.lifetime=burst.lifetime;visuals.Add(visual);
            var rotation=Quaternion.FromToRotation(Vector3.up,up.normalized);
            float minimumCos=Mathf.Cos(Mathf.Clamp(definition.spreadHalfAngleDegrees,0,89)*Mathf.Deg2Rad);
            for(int i=0;i<Mathf.Max(1,definition.burstCount);i++)
            {
                var spark=AcquireSpark();
                float cosine=Random.Range(minimumCos,1),sine=Mathf.Sqrt(Mathf.Max(0,1-cosine*cosine)),azimuth=Random.Range(0,Mathf.PI*2);
                var direction=rotation*new Vector3(Mathf.Cos(azimuth)*sine,cosine,Mathf.Sin(azimuth)*sine);
                spark.velocity=inheritedVelocity+direction*Mathf.Max(0,definition.emissionSpeed);
                spark.simulationPosition=position;spark.visualOrigin=position;spark.inherited=inheritedVelocity;
                // Reuse sampled variation without consuming additional gameplay random values.
                float visualSpeed=Mathf.Max(0,definition.emissionSpeed);
                spark.visualForward=direction*visualSpeed;
                float distanceVariation=Mathf.Repeat(Mathf.Sin(azimuth*12.9898f+cosine*78.233f)*43758.5453f,1);
                spark.burstAt=Mathf.Min(visual.lifetime*.65f,Mathf.Lerp(20,45,distanceVariation)/Mathf.Max(.01f,visualSpeed));
                spark.glow=Mathf.Clamp(definition.glowIntensity,0,1.6f);
                spark.size=Mathf.Max(.01f,definition.sparkSize)*scale*Random.Range(.8f,1.2f);
                spark.transform.SetPositionAndRotation(position,Random.rotation);
                spark.transform.localScale=Vector3.one*spark.size;
                spark.properties.SetColor(ColorId,Yellow);spark.properties.SetFloat(IntensityId,spark.glow);spark.properties.SetFloat(OpacityId,1);
                spark.renderer.SetPropertyBlock(spark.properties);spark.renderer.enabled=true;
                visual.sparks.Add(spark);burst.sparks.Add(spark);ActiveSparkCount++;
            }
            bursts.Add(burst);return burst;
        }

        Spark AcquireSpark()
        {
            if(freeSparks.Count>0)return freeSparks.Pop();
            var go=new GameObject("Pooled flare voxel "+PooledSparkCount,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=VoxelFxGeometry.Cube;
            var renderer=go.GetComponent<MeshRenderer>();VoxelFxGeometry.Configure(renderer,glow);
            PooledSparkCount++;return new Spark{transform=go.transform,renderer=renderer};
        }

        void FixedUpdate()=>Step(Time.fixedDeltaTime);
        void LateUpdate()
        {
            // Match the ship/camera's Rigidbody interpolation instead of showing 50 Hz position jumps.
            // Rendering never advances decoy age or modifies its physics position.
            float alpha=Time.fixedDeltaTime>0?Mathf.Clamp01((float)((Time.timeAsDouble-Time.fixedTimeAsDouble)/Time.fixedDeltaTime)):1;
            Present(alpha);
        }
        void Present(float alpha)
        {
            foreach(var visual in visuals)PresentSalvo(visual,Mathf.Lerp(visual.previousAge,visual.age,alpha));
        }
        public void Step(float dt)
        {
            if(dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            for(int i=bursts.Count-1;i>=0;i--)
            {
                var burst=bursts[i];burst.age+=dt;
                if(burst.age>=burst.lifetime){Release(i);continue;}
                Vector3 centre=Vector3.zero;
                foreach(var spark in burst.sparks){spark.simulationPosition+=spark.velocity*dt;centre+=spark.simulationPosition;}
                burst.Position=centre/burst.sparks.Count;
            }
            for(int i=visuals.Count-1;i>=0;i--)
            {
                var visual=visuals[i];visual.previousAge=visual.age;visual.age+=dt;
                if(visual.age>=visual.lifetime){ReleaseVisual(i);continue;}
                // Explicit Step callers retain deterministic presentation; LateUpdate samples between ticks at runtime.
                PresentSalvo(visual,visual.age);
            }
        }
        static void PresentSalvo(VisualSalvo visual,float age)
        {
                float remaining=1-age/visual.lifetime;
                foreach(var spark in visual.sparks)
                {
                    float travelAge=Mathf.Min(age,spark.burstAt);
                    spark.transform.position=spark.visualOrigin+spark.inherited*age+spark.visualForward*travelAge;
                    float burstAge=Mathf.Max(0,age-spark.burstAt);
                    bool exploded=age>=spark.burstAt;
                    float attack=Mathf.SmoothStep(0,1,burstAge/.045f);
                    float flash=exploded?attack*Mathf.Exp(-Mathf.Max(0,burstAge-.045f)*9):0;
                    float restingSize=Mathf.Lerp(.35f+.65f*remaining,.7f,attack);
                    spark.transform.localScale=Vector3.one*(spark.size*(restingSize+2.6f*flash));
                    spark.properties.SetFloat(IntensityId,spark.glow*(1+.2f*flash));
                    spark.properties.SetColor(ColorId,Yellow);
                    spark.properties.SetFloat(OpacityId,remaining);spark.renderer.SetPropertyBlock(spark.properties);
                }
        }
        void Release(int index)
        {
            var burst=bursts[index];burst.Active=false;
            
            burst.sparks.Clear();bursts.RemoveAt(index);freeBursts.Push(burst);
        }
        void ReleaseVisual(int index)
        {
            foreach(var spark in visuals[index].sparks){spark.renderer.enabled=false;freeSparks.Push(spark);ActiveSparkCount--;}
            var visual=visuals[index];visual.sparks.Clear();visuals.RemoveAt(index);freeVisuals.Push(visual);
        }
        public void Clear(){for(int i=bursts.Count-1;i>=0;i--)Release(i);for(int i=visuals.Count-1;i>=0;i--)ReleaseVisual(i);}
        void OnDisable()=>Clear();
        void OnDestroy(){if(glow){if(Application.isPlaying)Destroy(glow);else DestroyImmediate(glow);}}
    }
}
