// @role Pool coloured muzzle flashes, weapon impacts, missile explosions and destruction fragments.
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
            public Vector3 stretch=Vector3.one;
            public Color startColor, endColor;
            public bool transitionsColor;
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
            Emit(position, .75f, .23f, Vector3.zero, false, new Color(.06f,1,.18f), 4.5f);
            for (int i = 0; i < hitVoxelCount; i++)
            {
                var direction = Random.onUnitSphere;
                Emit(position + direction * (.14f*visualScale), Random.Range(.14f,.32f), Random.Range(.2f,.36f), direction * Random.Range(2f,5f)*visualScale, false, new Color(.08f,1f,.24f), 4.5f);
            }
        }

        public void EmitMuzzle(Vector3 position,Vector3 direction,Starframe.Flight.WeaponDefinition weapon)
        {
            if(!glow||!weapon)return;
            bool rail=weapon.family==Starframe.Flight.WeaponFamily.Railgun;
            bool missile=weapon.delivery==Starframe.Flight.WeaponDelivery.GuidedMissile;
            bool laser=weapon.family==Starframe.Flight.WeaponFamily.RapidLaser||weapon.family==Starframe.Flight.WeaponFamily.BeamLaser;
            float width=rail?Mathf.Clamp(weapon.projectileDiameter*weapon.visualWidth*.45f,.45f,1.8f):Mathf.Clamp(weapon.projectileDiameter*.9f,.16f,.8f);
            width=LaserVisualPool.MinimumReadableWidth(Camera.main,position,width,rail?3:2.2f);
            float coreDuration=missile?.14f:rail?.09f:laser?.065f:.055f;
            var core=Emit(position+direction*(width*.8f),width*.58f,coreDuration,Vector3.zero,false,laser?Color.Lerp(weapon.effectColor,Color.white,.08f):Color.white,rail?12:laser?4.5f:8,weapon.effectColor);
            if(!missile){
                // One directional flash per real discharge, visible from front and side.
                core.stretch=new Vector3(.72f,.72f,rail?5f:laser?4.5f:3.5f);core.spin=Vector3.zero;
                core.transform.rotation=Quaternion.LookRotation(direction);core.transform.localScale=core.stretch*core.size;
            }
            if(rail)Emit(position+direction*(width*.65f),width*.52f,.055f,direction*8,false,new Color(1,.9f,.55f),12);
            int count=missile?7:rail?12:laser?3:4;
            for(int i=0;i<count;i++){
                float size=rail?Random.Range(.12f,.24f)*Mathf.Clamp(width,.7f,1.8f):Random.Range(.045f,.1f);
                Vector3 velocity=rail?direction*Random.Range(24,50)+Random.insideUnitSphere*5:direction*Random.Range(3,10)+Random.insideUnitSphere*1.5f;
                Emit(position+direction*width,size,rail?Random.Range(.09f,.2f):Random.Range(.07f,.16f),velocity,false,weapon.effectColor,rail?7:3);
            }
        }
        public void EmitWeaponHit(Vector3 position,Starframe.Flight.WeaponDefinition weapon)
        {
            if(!glow||!weapon)return;
            bool rail=weapon.family==Starframe.Flight.WeaponFamily.Railgun;
            bool energy=weapon.family==Starframe.Flight.WeaponFamily.RapidLaser||weapon.family==Starframe.Flight.WeaponFamily.BeamLaser||weapon.delivery==Starframe.Flight.WeaponDelivery.Beam;
            float size=Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0,weapon.damage))*(rail?.11f:.15f),rail?.85f:.6f,rail?4.4f:2.3f);
            Emit(position,size,.18f,Vector3.zero,false,Color.Lerp(weapon.effectColor,Color.white,energy?.08f:rail?.6f:.7f),energy?4.5f:rail?12:9);
            Emit(position,size*.52f,.1f,Vector3.zero,false,energy?weapon.effectColor:rail?new Color(1,.94f,.7f):new Color(1,.9f,.6f),energy?5:rail?15:12);
            int count=Mathf.Clamp(Mathf.RoundToInt(size*(rail?11:16)),rail?18:10,rail?36:26);
            for(int i=0;i<count;i++){
                Vector3 direction=Random.onUnitSphere;
                float particle=rail?Random.Range(.18f,.42f)*Mathf.Clamp(size/1.4f,1,2.2f):Random.Range(.09f,.24f)*Mathf.Clamp(size/1.2f,1,1.6f);
                Emit(position+direction*.1f,particle,Random.Range(.18f,.4f),direction*(rail?Random.Range(8,18):Random.Range(4,11))*visualScale,false,weapon.effectColor,rail?8:6);
            }
        }
        public void EmitMissileExplosion(Vector3 position,Starframe.Flight.WeaponDefinition weapon)
        {
            if(!glow||!weapon)return;
            float radius=Mathf.Clamp(weapon.explosionRadius,.5f,10);
            Emit(position,radius*.5f,.16f,Vector3.zero,false,new Color(1,.85f,.4f),7);
            int count=Mathf.Clamp(Mathf.RoundToInt(radius*7),16,48);
            for(int i=0;i<count;i++){
                Vector3 direction=Random.onUnitSphere;
                Emit(position+direction*Random.Range(.05f,radius*.2f),Random.Range(.12f,.4f),Random.Range(.25f,.6f),direction*Random.Range(radius*1.3f,radius*3.5f),false,Color.Lerp(new Color(1,.68f,.07f),new Color(1,.14f,.015f),Random.value),Random.Range(2.2f,5.5f));
            }
            for(int i=0;i<10;i++)Emit(position+Random.insideUnitSphere*radius*.15f,Random.Range(.12f,.3f),Random.Range(.5f,.9f),Random.onUnitSphere*Random.Range(2,radius*2),true,new Color(.35f,.32f,.28f),1);
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

        Effect Emit(Vector3 position, float size, float duration, Vector3 velocity, bool fragment, Color color, float intensity,Color? endColor=null)
        {
            int index = free.Count > 0 ? free.Pop() : effects.Count < Mathf.Max(32,maximumVoxels) ? CreateSlot() : recycleCursor++ % effects.Count;
            var e = effects[index];
            e.active = true; e.fragment = fragment; e.age = 0; e.duration = duration; e.size = size*visualScale;e.stretch=Vector3.one;
            e.velocity = velocity; e.spin = Random.onUnitSphere * Random.Range(100,320); e.intensity = intensity;
            e.startColor=color;e.endColor=endColor??color;e.transitionsColor=endColor.HasValue;
            e.transform.SetPositionAndRotation(position, fragment ? Random.rotation : Quaternion.identity);
            e.transform.localScale = Vector3.one * e.size;
            e.renderer.sharedMaterial=fragment&&fragmentMaterial?fragmentMaterial:glow;
            e.properties.Clear();e.properties.SetColor(ColorId,color);e.properties.SetColor(BaseColorId,color); e.properties.SetFloat(IntensityId,intensity); e.properties.SetFloat(OpacityId,1);
            e.renderer.SetPropertyBlock(e.properties); e.renderer.enabled = true;return e;
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

        public void StepVisuals(float dt)
        {
            dt=Mathf.Max(0,dt);
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (!e.active) continue;
                e.age += dt;
                float t = Mathf.Clamp01(e.age / e.duration);
                e.transform.position += e.velocity * dt;
                e.transform.Rotate(e.spin * dt,Space.Self);
                float shrink = e.fragment ? Mathf.Lerp(1,.05f,Mathf.Clamp01((t-.55f)/.45f)) : Mathf.Lerp(1,.1f,t);
                e.transform.localScale = e.stretch * (e.size * shrink);
                e.properties.SetFloat(OpacityId,(1-t)*(1-t));
                if(e.transitionsColor)
                {
                    var tint=Color.Lerp(e.startColor,e.endColor,Mathf.SmoothStep(0,1,t));
                    e.properties.SetColor(ColorId,tint);e.properties.SetColor(BaseColorId,tint);
                }
                e.renderer.SetPropertyBlock(e.properties);
                if (t < 1) continue;
                e.active = false; e.renderer.enabled = false; free.Push(i);
            }
        }

        void Update()=>StepVisuals(Time.deltaTime);

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
