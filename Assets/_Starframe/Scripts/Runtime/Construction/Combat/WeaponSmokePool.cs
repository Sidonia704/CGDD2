// @role Pool visible world-space muzzle, solid-round path and missile smoke with reserved budgets for both factions.
// @calls WeaponDefinition
// @tune missileLifetime:float:0.1-10:Seconds missile smoke remains after the missile passes.
// @tune ballisticLifetime:float:0.1-10:Seconds solid-round flight smoke stays in world space.
// @tune ballisticOpacity:float:0-0.5:Peak opacity of each solid-round path wisp.
// @tune ballisticWidth:float:0.5-8:Base diameter of stretched solid-round path smoke.
// @tune ballisticSpacing:float:1-30:Metres between solid-round path samples before budgets.
// @tune muzzleLifetime:float:0.1-10:Seconds gun smoke stays near its firing location as the ship moves away.
// @tune missileOpacity:float:0-0.5:Peak opacity of each missile smoke puff.
// @tune muzzleOpacity:float:0-0.5:Peak opacity of each gun muzzle smoke puff.
// @tune missileSpacing:float:0.5-10:World metres between flight smoke samples before per-frame budgets.
// @tune missileWidth:float:0.5-8:Base missile smoke diameter in metres before expansion.
// @tune muzzleWidth:float:0.5-8:Base muzzle smoke diameter in metres before expansion.
// @tune muzzlePlumeLength:float:0-12:Short forward muzzle plume; never traces the full solid-round path.
// @tune maximumDistance:float:100-10000:Reject distant smoke emissions to protect combat visibility and fill rate.
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Flight;
namespace Starframe.Combat
{
    [DisallowMultipleComponent]
    public sealed class WeaponSmokePool : MonoBehaviour
    {
        public const int MissileCapacity=8192,BallisticCapacity=8192,MuzzleCapacity=4096,MaximumEmissionsPerFrame=384;
        [Range(.1f,10)] public float missileLifetime=6f,muzzleLifetime=6.5f,ballisticLifetime=4.5f;
        [Range(0,.5f)] public float missileOpacity=.36f,muzzleOpacity=.30f,ballisticOpacity=.32f;
        [Range(.5f,10)] public float missileSpacing=3.2f;
        [Range(1,30)] public float ballisticSpacing=10;
        [Range(.5f,8)] public float ballisticWidth=1.5f;
        [Range(.5f,8)] public float missileWidth=3f,muzzleWidth=2.1f;
        [Range(0,12)] public float muzzlePlumeLength=5;
        [Range(100,10000)] public float maximumDistance=5500;
        readonly ParticleSystem[] missileSmoke=new ParticleSystem[2],muzzleSmoke=new ParticleSystem[2],ballisticSmoke=new ParticleSystem[2];
        readonly int[] sideEmissions=new int[2],channelEmissions=new int[6];
        Material material;
        Camera view;
        int emissionFrame=-1,emittedThisFrame;
        uint serial;
        public int MissileParticleCount=>MissileParticlesForTeam(0)+MissileParticlesForTeam(1);
        public int MissileParticlesForTeam(int team)=>missileSmoke[team==0?0:1]?missileSmoke[team==0?0:1].particleCount:0;
        public int BallisticParticleCount=>BallisticParticlesForTeam(0)+BallisticParticlesForTeam(1);
        public int BallisticParticlesForTeam(int team)=>ballisticSmoke[team==0?0:1]?ballisticSmoke[team==0?0:1].particleCount:0;
        public int MuzzleParticleCount=>MuzzleParticlesForTeam(0)+MuzzleParticlesForTeam(1);
        public int MuzzleParticlesForTeam(int team)=>muzzleSmoke[team==0?0:1]?muzzleSmoke[team==0?0:1].particleCount:0;
        public int EmittedThisFrame=>emissionFrame==Time.frameCount?emittedThisFrame:0;
        public void Initialize()
        {
            if(missileSmoke[0])return;
            material=Resources.Load<Material>("Combat/WeaponSmoke");
            if(!material)return;
            view=Camera.main;
            for(int side=0;side<2;side++){
                missileSmoke[side]=Create("Missile smoke "+side+" (world)",MissileCapacity/2);
                muzzleSmoke[side]=Create("Gun muzzle smoke "+side+" (world)",MuzzleCapacity/2);
                ballisticSmoke[side]=Create("Solid path smoke "+side+" (world)",BallisticCapacity/2);
                var pathRenderer=ballisticSmoke[side].GetComponent<ParticleSystemRenderer>();
                pathRenderer.renderMode=ParticleSystemRenderMode.Stretch;pathRenderer.lengthScale=6;pathRenderer.velocityScale=0;pathRenderer.cameraVelocityScale=0;
            }
        }
        ParticleSystem Create(string label,int capacity)
        {
            var go=new GameObject(label);go.transform.SetParent(transform,false);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=true;main.duration=6;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=capacity;
            main.startSpeed=0;main.startLifetime=3;main.startSize=1;main.useUnscaledTime=false;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            ps.useAutoRandomSeed=false;ps.randomSeed=261103;
            var emission=ps.emission;emission.enabled=false;
            var shape=ps.shape;shape.enabled=false;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.85f,.89f,.94f),1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.03f),new GradientAlphaKey(.82f,.35f),new GradientAlphaKey(.48f,.72f),new GradientAlphaKey(0,1)});
            color.color=gradient;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(.5f,1.75f),new Keyframe(1,2.6f)));
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.sortMode=ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            renderer.minParticleSize=.0012f;renderer.maxParticleSize=.06f;
            ps.Play(false);return ps;
        }
        bool Admit(Vector3 position,int side,int kind)
        {
            if(emissionFrame!=Time.frameCount){emissionFrame=Time.frameCount;emittedThisFrame=0;sideEmissions[0]=sideEmissions[1]=0;for(int i=0;i<channelEmissions.Length;i++)channelEmissions[i]=0;}
            if(emittedThisFrame>=MaximumEmissionsPerFrame||sideEmissions[side]>=MaximumEmissionsPerFrame/2)return false;
            if(channelEmissions[side*3+kind]>=(kind==0?32:kind==1?64:96))return false;
            if(!view)view=Camera.main;
            if(view&&(position-view.transform.position).sqrMagnitude>maximumDistance*maximumDistance)return false;
            emittedThisFrame++;sideEmissions[side]++;channelEmissions[side*3+kind]++;return true;
        }
        public void EmitMuzzle(Vector3 position,Vector3 direction,WeaponDefinition weapon,int team=0)
        {
            int side=team==0?0:1;
            if(!weapon||!weapon.smokeTrail||weapon.delivery!=WeaponDelivery.Projectile||!muzzleSmoke[side])return;
            direction=direction.normalized;
            float width=muzzleWidth*Mathf.Lerp(.9f,1.25f,Mathf.Clamp01(weapon.projectileDiameter/1.3f));
            // Short launch plume only: deposited positions remain in the world as the firing ship moves.
            for(int i=0;i<3;i++){
                var point=position+direction*(.5f+muzzlePlumeLength*i*.5f);
                if(!Admit(point,side,0))continue;
                Emit(muzzleSmoke[side],point,direction*.8f+Vector3.up*.20f,
                    Mathf.Clamp(muzzleLifetime,.1f,10),width*(1-i*.12f),Mathf.Clamp01(muzzleOpacity)*(1-i*.20f));
            }
        }
        public void EmitMissileSegment(Vector3 from,Vector3 to,WeaponDefinition weapon,int team=0)
        {
            int side=team==0?0:1;
            if(!weapon||!weapon.smokeTrail||weapon.delivery!=WeaponDelivery.GuidedMissile||!missileSmoke[side])return;
            float distance=Vector3.Distance(from,to);if(distance<.001f||float.IsNaN(distance)||float.IsInfinity(distance))return;
            int samples=Mathf.Clamp(Mathf.CeilToInt(distance/Mathf.Max(.5f,missileSpacing)),1,12);
            var direction=(to-from)/distance;
            for(int i=0;i<samples;i++){
                var position=Vector3.Lerp(from,to,(i+.5f)/samples)-direction*.4f;
                if(!Admit(position,side,1))continue;
                Emit(missileSmoke[side],position,Vector3.up*.10f,Mathf.Clamp(missileLifetime,.1f,10),
                    missileWidth*Mathf.Clamp(weapon.projectileDiameter*.7f,1,1.35f),Mathf.Clamp01(missileOpacity));
            }
        }
        public void EmitBallisticSegment(Vector3 from,Vector3 to,WeaponDefinition weapon,int team=0)
        {
            int side=team==0?0:1;
            if(!weapon||!weapon.smokeTrail||weapon.delivery!=WeaponDelivery.Projectile||!ballisticSmoke[side])return;
            float distance=Vector3.Distance(from,to);if(distance<.001f||float.IsNaN(distance)||float.IsInfinity(distance))return;
            var direction=(to-from)/distance;
            int samples=Mathf.Clamp(Mathf.CeilToInt(distance/Mathf.Max(1,ballisticSpacing)),1,12);
            for(int i=0;i<samples;i++){
                var point=Vector3.Lerp(from,to,(i+.5f)/samples);
                if(!Admit(point,side,2))continue;
                // Tiny real velocity supplies stretched-billboard direction while the smoke stays near the path.
                Emit(ballisticSmoke[side],point,direction*.05f,Mathf.Clamp(ballisticLifetime,.1f,10),
                    ballisticWidth,Mathf.Clamp01(ballisticOpacity),true);
            }
        }
        void Emit(ParticleSystem ps,Vector3 position,Vector3 velocity,float life,float size,float opacity,bool path=false)
        {
            // Own deterministic sequence: cosmetics never consume combat's UnityEngine.Random state.
            serial++;float phase=serial*2.39996323f;
            var p=new ParticleSystem.EmitParams{position=position,velocity=velocity+(path?Vector3.zero:new Vector3(Mathf.Sin(phase),Mathf.Cos(phase),Mathf.Sin(phase*.7f))*.10f),
                startLifetime=life,startSize=size,startColor=new Color(.84f,.86f,.88f,opacity),rotation=phase*Mathf.Rad2Deg,randomSeed=serial};
            ps.Emit(p,1);
        }
        public void Clear()
        {
            for(int side=0;side<2;side++){
                if(missileSmoke[side])missileSmoke[side].Clear(false);
                if(muzzleSmoke[side])muzzleSmoke[side].Clear(false);
                if(ballisticSmoke[side])ballisticSmoke[side].Clear(false);
                sideEmissions[side]=0;
            }
            emittedThisFrame=0;emissionFrame=-1;
            for(int i=0;i<channelEmissions.Length;i++)channelEmissions[i]=0;
        }
    }
}
