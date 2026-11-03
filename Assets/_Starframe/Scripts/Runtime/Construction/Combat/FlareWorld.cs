// @role Pool yellow voxel sparks and expose each timed salvo as one moving missile decoy.
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
            public Vector3 velocity;
            public float size;
            public readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        }
        static readonly int ColorId=Shader.PropertyToID("_Color"),IntensityId=Shader.PropertyToID("_Intensity"),OpacityId=Shader.PropertyToID("_Opacity");
        static readonly Color Yellow=new Color(1,.82f,.03f);
        readonly List<FlareBurst> bursts=new List<FlareBurst>();
        readonly Stack<FlareBurst> freeBursts=new Stack<FlareBurst>();
        readonly Stack<Spark> freeSparks=new Stack<Spark>();
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
            var rotation=Quaternion.FromToRotation(Vector3.up,up.normalized);
            float minimumCos=Mathf.Cos(Mathf.Clamp(definition.spreadHalfAngleDegrees,0,89)*Mathf.Deg2Rad);
            for(int i=0;i<Mathf.Max(1,definition.burstCount);i++)
            {
                var spark=AcquireSpark();
                float cosine=Random.Range(minimumCos,1),sine=Mathf.Sqrt(Mathf.Max(0,1-cosine*cosine)),azimuth=Random.Range(0,Mathf.PI*2);
                var direction=rotation*new Vector3(Mathf.Cos(azimuth)*sine,cosine,Mathf.Sin(azimuth)*sine);
                spark.velocity=inheritedVelocity+direction*Mathf.Max(0,definition.emissionSpeed);
                spark.size=Mathf.Max(.01f,definition.sparkSize)*scale*Random.Range(.8f,1.2f);
                spark.transform.SetPositionAndRotation(position,Random.rotation);
                spark.transform.localScale=Vector3.one*spark.size;
                spark.properties.SetColor(ColorId,Yellow);spark.properties.SetFloat(IntensityId,Mathf.Max(0,definition.glowIntensity));spark.properties.SetFloat(OpacityId,1);
                spark.renderer.SetPropertyBlock(spark.properties);spark.renderer.enabled=true;
                burst.sparks.Add(spark);ActiveSparkCount++;
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
        public void Step(float dt)
        {
            if(dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            for(int i=bursts.Count-1;i>=0;i--)
            {
                var burst=bursts[i];burst.age+=dt;
                if(burst.age>=burst.lifetime){Release(i);continue;}
                float remaining=1-burst.age/burst.lifetime;Vector3 centre=Vector3.zero;
                foreach(var spark in burst.sparks)
                {
                    spark.transform.position+=spark.velocity*dt;centre+=spark.transform.position;
                    spark.transform.localScale=Vector3.one*(spark.size*(.35f+.65f*remaining));
                    spark.properties.SetFloat(OpacityId,remaining);spark.renderer.SetPropertyBlock(spark.properties);
                }
                burst.Position=centre/burst.sparks.Count;
            }
        }

        void Release(int index)
        {
            var burst=bursts[index];burst.Active=false;
            foreach(var spark in burst.sparks){spark.renderer.enabled=false;freeSparks.Push(spark);ActiveSparkCount--;}
            burst.sparks.Clear();bursts.RemoveAt(index);freeBursts.Push(burst);
        }
        public void Clear(){for(int i=bursts.Count-1;i>=0;i--)Release(i);}
        void OnDisable()=>Clear();
        void OnDestroy(){if(glow){if(Application.isPlaying)Destroy(glow);else DestroyImmediate(glow);}}
    }
}
