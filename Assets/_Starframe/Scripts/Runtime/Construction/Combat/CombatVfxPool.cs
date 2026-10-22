// @role Render shared weapon-impact and ship-destruction effects.
// @calls VoxelFxGeometry

using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Combat
{
    /// <summary>Bounded, pooled solid voxel flashes and fragments, updated by one manager.</summary>
    public sealed class CombatVfxPool : MonoBehaviour
    {
        [Min(32)] public int maximumVoxels = 384;
        [Range(4,20)] public int hitVoxelCount = 9;
        sealed class Effect
        {
            public Transform transform;
            public MeshRenderer renderer;
            public MaterialPropertyBlock properties;
            public Vector3 velocity, spin;
            public float age, duration, size, intensity;
            public bool active, fragment;
        }
        readonly List<Effect> effects = new List<Effect>();
        readonly Stack<int> free = new Stack<int>();
        Material glow,fragmentMaterial;
        float visualScale=1;
        int recycleCursor;
        static readonly int ColorId = Shader.PropertyToID("_Color"), BaseColorId=Shader.PropertyToID("_BaseColor"), IntensityId = Shader.PropertyToID("_Intensity"), OpacityId = Shader.PropertyToID("_Opacity");

        public void Initialize(Shader shader = null, Material fragments = null,float effectScale=1)
        {
            ReleaseResources();
            glow = VoxelFxGeometry.CreateGlow(shader);
            if(fragments)fragmentMaterial=new Material(fragments){name="Shared plastic destruction fragments"};
            visualScale=Mathf.Max(.1f,effectScale);
            enabled = glow;
        }

        public void EmitPartDestroyed(Vector3 position,Quaternion rotation,Vector3 size,Vector3 inheritedVelocity,Color surface)
        {
            if(!glow)return;
            Emit(position,.3f,.13f,inheritedVelocity,false,new Color(.55f,.9f,1),4);
            int count=Mathf.Clamp(Mathf.RoundToInt(size.x*size.y*size.z*10),8,20);
            for(int i=0;i<count;i++){
                var direction=Random.onUnitSphere;
                var offset=rotation*Vector3.Scale(Random.insideUnitSphere*.35f,size);
                Emit(position+offset,Random.Range(.13f,.24f),Random.Range(.45f,.85f),inheritedVelocity+direction*Random.Range(2,5)*visualScale,true,surface,1);
            }
        }

        public void EmitLaserHit(Vector3 position)
        {
            if (!glow) return;
            Emit(position, .75f, .23f, Vector3.zero, false, new Color(.45f,.9f,1), 7);
            for (int i = 0; i < hitVoxelCount; i++)
            {
                var direction = Random.onUnitSphere;
                Emit(position + direction * (.14f*visualScale), Random.Range(.14f,.32f), Random.Range(.2f,.36f), direction * Random.Range(2f,5f)*visualScale, false, new Color(.08f,.65f,1), 4.5f);
            }
        }

        public void EmitShipDestroyed(Vector3 position, float radius, Vector3 inheritedVelocity)
        {
            if (!glow) return;
            radius = Mathf.Clamp(radius, 1, 14);
            // Brief dense flash first; the longer lived tumbling cubes carry the explosion outward.
            int flashes = Mathf.Clamp(Mathf.RoundToInt(radius * 4), 12, 32);
            for (int i = 0; i < flashes; i++)
            {
                var direction = Random.onUnitSphere;
                Emit(position + Random.insideUnitSphere * radius * (.25f*visualScale), Random.Range(.15f,.4f), Random.Range(.12f,.3f), inheritedVelocity * .15f + direction * Random.Range(3,8)*visualScale, false, new Color(.2f,.8f,1), 5);
            }
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 6), 20, 56);
            for (int i = 0; i < count; i++)
            {
                var direction = Random.onUnitSphere;
                Emit(position + Random.insideUnitSphere * radius * (.18f*visualScale), Random.Range(.12f,.4f), Random.Range(.55f,1.15f), inheritedVelocity * .2f + direction * Random.Range(4,13)*visualScale, true, new Color(.9f,.9f,.9f),1);
            }
        }

        void Emit(Vector3 position, float size, float duration, Vector3 velocity, bool fragment, Color color, float intensity)
        {
            int index = free.Count > 0 ? free.Pop() : effects.Count < Mathf.Max(32,maximumVoxels) ? CreateSlot() : recycleCursor++ % effects.Count;
            var e = effects[index];
            e.active = true; e.fragment = fragment; e.age = 0; e.duration = duration; e.size = size*visualScale;
            e.velocity = velocity; e.spin = Random.onUnitSphere * Random.Range(100,320); e.intensity = intensity;
            e.transform.SetPositionAndRotation(position, fragment ? Random.rotation : Quaternion.identity);
            e.transform.localScale = Vector3.one * e.size;
            e.renderer.sharedMaterial=fragment&&fragmentMaterial?fragmentMaterial:glow;
            e.properties.Clear();e.properties.SetColor(ColorId,color);e.properties.SetColor(BaseColorId,color); e.properties.SetFloat(IntensityId,intensity); e.properties.SetFloat(OpacityId,1);
            e.renderer.SetPropertyBlock(e.properties); e.renderer.enabled = true;
        }

        int CreateSlot()
        {
            var go = new GameObject("CombatVoxel_" + effects.Count, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform,false);
            go.GetComponent<MeshFilter>().sharedMesh = VoxelFxGeometry.Cube;
            var renderer = go.GetComponent<MeshRenderer>();
            VoxelFxGeometry.Configure(renderer,glow);
            effects.Add(new Effect { transform = go.transform, renderer = renderer, properties = new MaterialPropertyBlock() });
            return effects.Count - 1;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (!e.active) continue;
                e.age += dt;
                float t = Mathf.Clamp01(e.age / e.duration);
                e.transform.position += e.velocity * dt;
                e.transform.Rotate(e.spin * dt,Space.Self);
                float shrink = e.fragment ? Mathf.Lerp(1,.05f,Mathf.Clamp01((t-.55f)/.45f)) : Mathf.Lerp(1,.1f,t);
                e.transform.localScale = Vector3.one * (e.size * shrink);
                e.properties.SetFloat(OpacityId,(1-t)*(1-t));
                e.renderer.SetPropertyBlock(e.properties);
                if (t < 1) continue;
                e.active = false; e.renderer.enabled = false; free.Push(i);
            }
        }

        void OnDestroy() => ReleaseResources();
        void ReleaseResources()
        {
            foreach (var e in effects) if (e.transform) Destroy(e.transform.gameObject);
            effects.Clear(); free.Clear(); recycleCursor = 0;
            if (glow) Destroy(glow);
            if(fragmentMaterial)Destroy(fragmentMaterial);
            glow = null;fragmentMaterial=null;
        }
    }
}
