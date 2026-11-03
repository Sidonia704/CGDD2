// @role Render bounded dust/haze and align the distant sky sun with the key light, away from the planet.
// @calls RangeAtmosphereTuning, RangeEnvironment, GameSettings

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
namespace Starframe.Flight
{
    public sealed class RangeAtmosphere:MonoBehaviour
    {
        [SerializeField] RangeAtmosphereTuning tuning;
        [SerializeField] Volume volume;
        [SerializeField] Light starLight;
        [SerializeField] Transform beltAnchor;
        static RangeAtmosphere skyOwner;
        static readonly int SkyState=Shader.PropertyToID("_StarframeRangeSkyState"),SkyLight=Shader.PropertyToID("_StarframeRangeSkyLight"),SkyWarm=Shader.PropertyToID("_StarframeRangeSkyWarm"),SkyCool=Shader.PropertyToID("_StarframeRangeSkyCool"),SkyFocus=Shader.PropertyToID("_StarframeRangeSkyFocus");
        Vector3 lightDirection;Color lightColor;float beltProximity;bool lightReady;
        Camera view;ParticleSystem dust,haze;int quality=-1;float nextQualityCheck;
        bool started,fogOwned,oldFog;FogMode oldFogMode;float oldFogDensity;Color oldFogColor;
        public int DustCount=>dust?dust.particleCount:0;
        public int HazeCount=>haze?haze.particleCount:0;
        public int DustBudget=>tuning?Mathf.Clamp(tuning.dustLimit,0,quality==0?64:320):0;
        public int HazeBudget=>tuning&&quality!=0?Mathf.Clamp(tuning.hazeLimit,0,16):0;
        public void Initialize(Transform player,Camera camera)
        {
            if(started||!tuning||!player)return;view=camera?camera:Camera.main;if(!view)return;
            started=true;
            if(starLight&&tuning.starDirection.sqrMagnitude>.001f)
                starLight.transform.rotation=Quaternion.LookRotation(-tuning.starDirection.normalized,Vector3.up);
            // The old nearby sphere appears pasted onto the giant planet and moves with flight parallax.
            // The simulation sky now draws the distant source using the same direction as this light.
            if(RenderSettings.skybox&&RenderSettings.skybox.shader&&
                RenderSettings.skybox.shader.name=="Starframe/Environment/Simulation Sky")
                foreach(var root in gameObject.scene.GetRootGameObjects())
                    if(root.name=="Star Direction Reference"&&root.TryGetComponent<MeshRenderer>(out var renderer))
                        renderer.enabled=false;
            // Keep scenery crisp on every quality tier; depth is used for soft particle intersections only.
            if(volume&&volume.profile.TryGet<DepthOfField>(out var dof)){dof.active=true;dof.mode.Override(DepthOfFieldMode.Off);}
            ApplyQuality();
        }
        void ApplyQuality()
        {
            quality=QualityTier();
            if(dust){dust.gameObject.SetActive(false);Destroy(dust.gameObject);dust=null;}if(haze){haze.gameObject.SetActive(false);Destroy(haze.gameObject);haze=null;}
            if(tuning.dustEnabled&&DustBudget>0&&tuning.dustMaterial) dust=CreateParticles("Local space dust",false,DustBudget);
            if(tuning.hazeEnabled&&HazeBudget>0&&tuning.hazeMaterial) haze=CreateParticles("Thin scattering haze",true,HazeBudget);
            ApplyFog();
        }
        void ApplyFog()
        {
            if(!fogOwned){oldFog=RenderSettings.fog;oldFogMode=RenderSettings.fogMode;oldFogColor=RenderSettings.fogColor;oldFogDensity=RenderSettings.fogDensity;fogOwned=true;}
            RenderSettings.fog=tuning.distanceHaze;
            if(tuning.distanceHaze)
            {
                RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=tuning.fogColor;RenderSettings.fogDensity=tuning.fogDensity;
            }
        }
        void UpdateLighting()
        {
            var direction=starLight?-starLight.transform.forward:Vector3.up;
            var color=starLight?starLight.color*Mathf.Clamp(starLight.intensity*.5f,0,2):Color.gray;
            bool belt=beltAnchor&&beltAnchor.gameObject.activeInHierarchy;
            var focus=belt?beltAnchor.position+Vector3.up*tuning.beltHeightOffset:view.transform.position+Vector3.down*100;
            float proximity=belt?1-Mathf.SmoothStep(0,1,Mathf.Abs(view.transform.position.y-focus.y)/Mathf.Max(100,tuning.beltFalloff)):0;
            float response=lightReady?1-Mathf.Exp(-Time.unscaledDeltaTime*3):1;
            lightDirection=Vector3.Slerp(lightReady?lightDirection:direction,direction,response);lightColor=Color.Lerp(lightReady?lightColor:color,color,response);beltProximity=Mathf.Lerp(beltProximity,proximity,response);lightReady=true;
            var cool=tuning.skyCool*(.38f+.62f*Mathf.Sqrt(Mathf.Max(0,lightColor.maxColorComponent)));
            var warm=tuning.skyWarm*lightColor;
            skyOwner=this;
            Shader.SetGlobalVector(SkyState,new Vector4(1,tuning.skyIntensity,beltProximity,0));
            Shader.SetGlobalVector(SkyLight,new Vector4(lightDirection.x,lightDirection.y,lightDirection.z,0));
            Shader.SetGlobalColor(SkyWarm,warm);Shader.SetGlobalColor(SkyCool,cool);
            var focusDirection=(focus-view.transform.position).normalized;Shader.SetGlobalVector(SkyFocus,new Vector4(focusDirection.x,focusDirection.y,focusDirection.z,0));
            if(tuning.distanceHaze)
            {
                RenderSettings.fogColor=Color.Lerp(tuning.fogColor,cool,.12f);
                RenderSettings.fogDensity=tuning.fogDensity*Mathf.Lerp(.55f,1.35f,beltProximity);
            }
        }
        ParticleSystem CreateParticles(string label,bool cloud,int limit)
        {
            var go=new GameObject(label);go.transform.SetParent(transform,false);
            go.transform.SetPositionAndRotation(view.transform.position+view.transform.forward*(cloud?140:25),Quaternion.identity);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=true;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=limit;main.startLifetime=cloud?18:6;main.startSpeed=cloud?.25f:.12f;
            main.startSize=cloud?new ParticleSystem.MinMaxCurve(45,85):new ParticleSystem.MinMaxCurve(tuning.dustSize.x,tuning.dustSize.y);
            main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            main.startColor=cloud?new Color(.30f,.42f,.56f,tuning.hazeOpacity):new Color(.68f,.78f,.88f,.28f);
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=cloud?110:tuning.dustRadius;
            var emission=ps.emission;emission.rateOverTime=cloud?.45f:tuning.dustRate;emission.rateOverDistance=cloud?.012f:tuning.dustPerMetre;
            var fade=ps.colorOverLifetime;fade.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.7f,.7f),new GradientAlphaKey(0,1)});fade.color=gradient;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.sharedMaterial=cloud?tuning.hazeMaterial:tuning.dustMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            renderer.minParticleSize=0;renderer.maxParticleSize=cloud?.14f:.006f;
            renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
            ps.useAutoRandomSeed=false;ps.randomSeed=cloud?7823u:4177u;ps.Play();ps.Simulate(cloud?10:3,true,false,true);ps.Play();return ps;
        }
        void LateUpdate()
        {
            if(!started||!view||!tuning)return;
            if(Time.unscaledTime>=nextQualityCheck){nextQualityCheck=Time.unscaledTime+.5f;if(quality!=QualityTier())ApplyQuality();}
            if(dust)dust.transform.position=view.transform.position+view.transform.forward*25;
            if(haze)haze.transform.position=view.transform.position+view.transform.forward*140;
            UpdateLighting();
        }
        static int QualityTier()
        {
            // Platform filtering can expose just "PC" at index zero during Play. Use the same mapping for creation and polling.
            var names=QualitySettings.names;
            if(names.Length==1)return names[0].IndexOf("mobile",System.StringComparison.OrdinalIgnoreCase)>=0||names[0].IndexOf("low",System.StringComparison.OrdinalIgnoreCase)>=0?0:1;
            return QualitySettings.GetQualityLevel();
        }
        void OnEnable()
        {
            if(!started)return;
            if(dust){dust.GetComponent<ParticleSystemRenderer>().enabled=true;dust.Play();}
            if(haze){haze.GetComponent<ParticleSystemRenderer>().enabled=true;haze.Play();}
            ApplyFog();
        }
        void OnDisable()
        {
            if(dust)dust.Pause();if(haze)haze.Pause();
            if(dust)dust.GetComponent<ParticleSystemRenderer>().enabled=false;if(haze)haze.GetComponent<ParticleSystemRenderer>().enabled=false;
            RestoreFog();
            if(skyOwner==this){Shader.SetGlobalVector(SkyState,Vector4.zero);skyOwner=null;}
        }
        void RestoreFog()
        {
            if(!fogOwned)return;
            if(SceneManager.GetActiveScene()==gameObject.scene){RenderSettings.fog=oldFog;RenderSettings.fogMode=oldFogMode;RenderSettings.fogColor=oldFogColor;RenderSettings.fogDensity=oldFogDensity;}
            fogOwned=false;
        }
        void OnDestroy(){RestoreFog();}
    }
}
