// @role Create the range player, camera, flight systems and combat world.
// @calls FlightSession, ShipAssembly, FlightShipRuntime, ShipFlightController, RangeCombatWorld, RangeHUD

using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Construction;
using Starframe.Combat;
namespace Starframe.Flight
{
    public sealed class FlightRangeBootstrap:MonoBehaviour
    {
        [SerializeField] FlightTuning tuning;
        [SerializeField] ShipAssembly assemblyPrefab;
        [SerializeField] Camera flightCamera;
        [SerializeField] RangeEnvironment environment;
        [SerializeField] BattleTuning battleTuning;
        void Start()
        {
            var blueprint=FlightSession.EditorDraft??DefaultShip();
            if(!battleTuning)battleTuning=ScriptableObject.CreateInstance<BattleTuning>();
            var bodyObject=new GameObject("PlayerShip");var body=bodyObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
            var assembly=Instantiate(assemblyPrefab,bodyObject.transform);assembly.name="FlightShip";
            assembly.Configure(tuning.database);
            assembly.Import(blueprint);
            var runtime=bodyObject.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning,battleTuning);
            var rotation=Quaternion.Inverse(runtime.CoreRotation);assembly.transform.localRotation=rotation;assembly.transform.localPosition=-(rotation*runtime.CenterOfMass());
            foreach(var t in assembly.GetComponentsInChildren<Transform>(true))
                if(t.name=="ShipForwardRay"||t.name=="PlacementCollisionProbe"||t.name=="ImportCollisionProbe")t.gameObject.SetActive(false);
            body.mass=Mathf.Max(1,runtime.Performance.totalMass);body.centerOfMass=Vector3.zero;
            runtime.PerformanceChanged+=()=>{if(body)body.mass=Mathf.Max(1,runtime.Performance.totalMass);};
            var hullBounds=new Bounds(bodyObject.transform.position,Vector3.zero);
            foreach(var part in runtime.Parts){var renderer=part.view.GetComponent<MeshRenderer>();if(renderer)hullBounds.Encapsulate(renderer.bounds);}
            float radius=Mathf.Max(1,hullBounds.extents.magnitude);
            var flight=bodyObject.AddComponent<ShipFlightController>();flight.Initialize(body,runtime,tuning,flightCamera,radius);
            var exhaust=bodyObject.AddComponent<ThrusterExhaust>();exhaust.Initialize(runtime,flight,tuning,flightCamera,battleTuning.particleScale);
            var chase=flightCamera.gameObject.AddComponent<FlightChaseCamera>();chase.Initialize(bodyObject.transform,flight,tuning);
            var hud=gameObject.AddComponent<RangeHUD>();hud.Initialize(flightCamera,flight,tuning);flight.BindHUD(hud);chase.BindHUD(hud);
            hud.ExitConfirmed+=()=>{if(Starframe.App.AppNavigation.HasAppContext&&!Starframe.App.AppNavigation.RangeReturnToEditor){Starframe.App.AppNavigation.OpenHome(Starframe.App.AppPage.RangeSetup);return;}FlightSession.Return();Starframe.App.GameInput.Suspended=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;SceneManager.LoadSceneAsync(FlightSession.EditorScene);};
            environment.Initialize(bodyObject.transform,tuning);
            var combat=gameObject.AddComponent<RangeCombatWorld>();combat.Initialize(runtime,flight,assemblyPrefab,tuning,battleTuning,flightCamera,hud);
            gameObject.AddComponent<Starframe.App.RangeAppNavigation>().Initialize(hud,combat);
        }
        public static BlueprintData DefaultShip()
        {
            var d=new BlueprintData{name="标准靶场试飞船"};
            d.parts.Add(new BlockRecord{id="range-core",shape=BlockShape.Core,position=new Vector3(0,.5f,0),rotation=Quaternion.identity});
            var dirs=new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            for(int i=0;i<dirs.Length;i++)d.parts.Add(new BlockRecord{id="range-engine-"+i,shape=BlockShape.Thruster1,position=dirs[i]+Vector3.up*.5f,rotation=BlockPlacement.FaceRotation(dirs[i],0)});
            d.parts.Add(new BlockRecord{id="range-mount",shape=BlockShape.Cube,position=new Vector3(1,1.5f,0),rotation=Quaternion.identity});
            d.parts.Add(new BlockRecord{id="range-mount-front",shape=BlockShape.Power,position=new Vector3(1,1.5f,1),rotation=Quaternion.identity});
            d.parts.Add(new BlockRecord{id="range-gun",shape=BlockShape.Laser,position=new Vector3(1,2.5f,0),rotation=Quaternion.identity});
            return BlueprintRepository.Upgrade(d);
        }
    }
}
