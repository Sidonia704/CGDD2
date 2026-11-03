// @role Pool visible missile bodies, curved orange voxel trails and short continuous beam blades.
// @calls VoxelFxGeometry, WeaponDefinition
using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;

namespace Starframe.Combat
{
    public sealed class SpecialWeaponVisualPool:MonoBehaviour
    {
        const int TrailCount=14,MaximumMissiles=192;
        const float MissileIgnitionDelay=.12f,MissileIgnitionBlend=.09f;
        sealed class MissileVisual
        {
            public Transform body;public MeshFilter filter;public MeshRenderer renderer;
            public readonly Transform[] trail=new Transform[TrailCount];
            public readonly MeshRenderer[] trailRenderers=new MeshRenderer[TrailCount];
            public readonly Vector3[] points=new Vector3[TrailCount];
            public readonly MaterialPropertyBlock[] properties=new MaterialPropertyBlock[TrailCount];
            public WeaponDefinition weapon;public float age;public bool active;
        }
        sealed class BeamVisual
        {
            public Transform shell,core,contact,source;public MeshRenderer shellRenderer,coreRenderer,contactRenderer;public MeshFilter shellFilter,coreFilter;public bool shaped;
            public MaterialPropertyBlock properties=new MaterialPropertyBlock();
            public Vector3 origin,direction,localPosition,localDirection;
            public float length,width,remaining,duration,intensity;public Color color;public bool active;
        }
        readonly List<MissileVisual> missiles=new List<MissileVisual>();
        readonly Stack<int> freeMissiles=new Stack<int>();
        readonly List<BeamVisual> beams=new List<BeamVisual>();
        Material glow;
        static readonly int ColorId=Shader.PropertyToID("_Color"),IntensityId=Shader.PropertyToID("_Intensity"),OpacityId=Shader.PropertyToID("_Opacity");
        public int ActiveMissiles{get;private set;}
        public void Initialize(){if(!glow)glow=VoxelFxGeometry.CreateGlow();}
        MeshRenderer Renderer(string name,out Transform result)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=VoxelFxGeometry.Cube;
            var r=go.GetComponent<MeshRenderer>();VoxelFxGeometry.Configure(r,glow);r.enabled=false;result=go.transform;return r;
        }
        public int AcquireMissile(WeaponDefinition weapon,Vector3 position,Vector3 direction)
        {
            if(!glow)return -1;
            int slot;
            if(freeMissiles.Count>0)slot=freeMissiles.Pop();
            else{
                if(missiles.Count>=MaximumMissiles)return -1;
                slot=missiles.Count;var created=new MissileVisual();
                created.renderer=Renderer("Guided missile "+slot,out created.body);created.filter=created.body.GetComponent<MeshFilter>();
                for(int j=0;j<TrailCount;j++){created.trailRenderers[j]=Renderer("Missile trail "+slot+" "+j,out created.trail[j]);created.properties[j]=new MaterialPropertyBlock();}
                missiles.Add(created);
            }
            var v=missiles[slot];v.weapon=weapon;v.age=0;v.active=true;ActiveMissiles++;
            v.filter.sharedMesh=weapon.missileMesh?weapon.missileMesh:VoxelFxGeometry.Cube;
            v.renderer.sharedMaterial=weapon.projectileMaterial?weapon.projectileMaterial:glow;
            v.body.localScale=weapon.missileMesh?Vector3.one:new Vector3(.16f,.16f,.7f);
            v.body.SetPositionAndRotation(position,Quaternion.LookRotation(direction));v.renderer.enabled=true;
            Vector3 exhaust=position+direction*((weapon.missileMesh?weapon.missileMesh.bounds.min.z:-.35f)-.05f);
            for(int j=0;j<TrailCount;j++){v.points[j]=exhaust;v.properties[j].Clear();v.trailRenderers[j].enabled=false;}
            return slot;
        }
        public void MoveMissile(int slot,Vector3 position,Vector3 direction,float distance)
        {
            if(slot<0||slot>=missiles.Count)return;var v=missiles[slot];if(!v.active)return;
            v.body.SetPositionAndRotation(position,Quaternion.LookRotation(direction));
            float spacing=Mathf.Max(.15f,v.weapon.tailLength/TrailCount);
            Vector3 exhaust=position+direction*((v.weapon.missileMesh?v.weapon.missileMesh.bounds.min.z:-.35f)-.05f);
            float ignition=Mathf.SmoothStep(0,1,(v.age-MissileIgnitionDelay)/MissileIgnitionBlend);
            Vector3 previous=v.points[0];
            // Resample by travelled distance so even fast missiles leave a short coherent trail.
            int samples=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(previous,exhaust)/spacing),1,TrailCount);
            for(int s=1;s<=samples;s++){
                for(int j=TrailCount-1;j>0;j--)v.points[j]=v.points[j-1];
                v.points[0]=Vector3.Lerp(previous,exhaust,(float)s/samples);
            }
            for(int j=0;j<TrailCount;j++){
                float age=(float)j/TrailCount;
                float size=Mathf.Max(.06f,v.weapon.projectileDiameter*.65f)*(1-age*.75f)*Mathf.Lerp(.55f,1,ignition);
                v.trail[j].position=v.points[j];v.trail[j].localScale=Vector3.one*size;
                var p=v.properties[j];p.SetColor(ColorId,Color.Lerp(v.weapon.effectColor,new Color(1,.13f,.01f),age*.65f));
                p.SetFloat(IntensityId,Mathf.Lerp(4.5f,.5f,age)*ignition);p.SetFloat(OpacityId,(1-age)*(1-age)*ignition);
                v.trailRenderers[j].SetPropertyBlock(p);v.trailRenderers[j].enabled=ignition>.001f&&distance>j*spacing;
            }
        }
        public void ReleaseMissile(int slot)
        {
            if(slot<0||slot>=missiles.Count)return;var v=missiles[slot];if(!v.active)return;
            v.active=false;v.renderer.enabled=false;foreach(var r in v.trailRenderers)r.enabled=false;freeMissiles.Push(slot);ActiveMissiles--;
        }
        public void ShowBeam(WeaponDefinition weapon,Vector3 origin,Vector3 direction,float length,Transform source,Vector3 localPosition)
        {
            if(!glow)return;BeamVisual b=null;
            foreach(var beam in beams)if(beam.active&&beam.source==source&&source){b=beam;break;}
            if(b==null)foreach(var beam in beams)if(!beam.active){b=beam;break;}
            if(b==null){b=new BeamVisual();b.shellRenderer=Renderer("Beam sheath",out b.shell);b.coreRenderer=Renderer("Beam white core",out b.core);b.contactRenderer=Renderer("Beam contact glow",out b.contact);b.shellFilter=b.shell.GetComponent<MeshFilter>();b.coreFilter=b.core.GetComponent<MeshFilter>();beams.Add(b);}
            b.shaped=weapon.beamMesh;b.shellFilter.sharedMesh=b.coreFilter.sharedMesh=b.shaped?weapon.beamMesh:VoxelFxGeometry.Cube;
            b.active=true;b.origin=origin;b.direction=direction.normalized;b.source=source;b.localPosition=localPosition;
            b.localDirection=source?source.InverseTransformDirection(b.direction):b.direction;
            b.length=Mathf.Max(.01f,length);b.width=Mathf.Max(.06f,weapon.projectileDiameter*weapon.visualWidth);
            b.duration=b.remaining=Mathf.Max(.02f,weapon.beamDuration);b.color=weapon.effectColor;b.intensity=weapon.visualIntensity;
            b.properties.Clear();
            b.properties.SetColor(ColorId,weapon.effectColor);b.properties.SetFloat(IntensityId,weapon.visualIntensity);b.properties.SetFloat(OpacityId,.55f);
            b.shellRenderer.SetPropertyBlock(b.properties);
            b.properties.SetColor(ColorId,Color.Lerp(weapon.effectColor,Color.white,.85f));b.properties.SetFloat(IntensityId,weapon.visualIntensity*1.5f);b.properties.SetFloat(OpacityId,1);
            b.coreRenderer.SetPropertyBlock(b.properties);
            b.properties.SetColor(ColorId,weapon.effectColor);b.properties.SetFloat(IntensityId,weapon.visualIntensity*1.35f);b.properties.SetFloat(OpacityId,.85f);
            b.contactRenderer.SetPropertyBlock(b.properties);
            UpdateBeam(b);
        }
        static void UpdateBeam(BeamVisual b)
        {
            if(b.source){b.origin=b.source.TransformPoint(b.localPosition);b.direction=b.source.TransformDirection(b.localDirection).normalized;}
            var rotation=Quaternion.LookRotation(b.direction);
            var center=b.shaped?b.origin:b.origin+b.direction*(b.length*.5f);
            b.shell.SetPositionAndRotation(center,rotation);b.core.SetPositionAndRotation(center,rotation);
            b.shell.localScale=new Vector3(b.width,b.width,b.length);b.core.localScale=new Vector3(b.width*.32f,b.width*.32f,b.length);
            b.contact.SetPositionAndRotation(b.origin+b.direction*b.length,rotation);
            float contactSize=Mathf.Max(.12f,b.width*.52f);b.contact.localScale=Vector3.one*contactSize;
            b.shellRenderer.enabled=b.coreRenderer.enabled=b.contactRenderer.enabled=true;
        }
        static float BeamFade(float remainingFraction)
        {
            float fadeStart=.38f;
            return Mathf.SmoothStep(0,1,Mathf.Clamp01(remainingFraction/fadeStart));
        }
        public void StepVisuals(float dt)
        {
            dt=Mathf.Max(0,dt);
            foreach(var missile in missiles)if(missile.active)missile.age+=dt;
            foreach(var b in beams)if(b.active){
                b.remaining-=dt;
                if(b.remaining<=0||(b.source&&!b.source.gameObject.activeInHierarchy)){b.active=false;b.shellRenderer.enabled=b.coreRenderer.enabled=b.contactRenderer.enabled=false;continue;}
                float fade=BeamFade(b.remaining/Mathf.Max(.001f,b.duration));
                b.properties.SetColor(ColorId,b.color);b.properties.SetFloat(IntensityId,b.intensity);b.properties.SetFloat(OpacityId,.55f*fade);
                b.shellRenderer.SetPropertyBlock(b.properties);
                b.properties.SetColor(ColorId,Color.Lerp(b.color,Color.white,.85f));b.properties.SetFloat(IntensityId,b.intensity*1.5f);b.properties.SetFloat(OpacityId,fade);
                b.coreRenderer.SetPropertyBlock(b.properties);
                b.properties.SetColor(ColorId,b.color);b.properties.SetFloat(IntensityId,b.intensity*1.35f);b.properties.SetFloat(OpacityId,.85f*fade);
                b.contactRenderer.SetPropertyBlock(b.properties);
                UpdateBeam(b);
            }
        }
        void LateUpdate()=>StepVisuals(Time.deltaTime);
        public void Clear(){for(int i=0;i<missiles.Count;i++)ReleaseMissile(i);foreach(var b in beams){b.active=false;b.shellRenderer.enabled=b.coreRenderer.enabled=b.contactRenderer.enabled=false;}}
        void OnDestroy(){if(glow){if(Application.isPlaying)Destroy(glow);else DestroyImmediate(glow);}}
    }
}
