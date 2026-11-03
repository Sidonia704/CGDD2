using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Starframe.App;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Starframe.Tests
{
    public sealed class FlightWrenchClosedLoopTests
    {
        readonly List<Object> owned=new List<Object>();
        GameObject host;Scene fixtureScene;ModuleDatabase previousDatabase;float previousScale;bool previousSuspended,previousCursorVisible;CursorLockMode previousCursorLock;UnityEngine.Random.State previousRandom;
        [UnitySetUp]public IEnumerator SetUp()
        {
            previousDatabase=BlockModules.ConfiguredDatabase;previousScale=Time.timeScale;previousSuspended=GameInput.Suspended;previousCursorLock=Cursor.lockState;previousCursorVisible=Cursor.visible;previousRandom=UnityEngine.Random.state;
            fixtureScene=SceneManager.CreateScene("ClosedLoopFlightFixture_"+System.Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(fixtureScene);
            var scenes=new List<Scene>();for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene!=fixtureScene&&scene.path.StartsWith("Assets/_Starframe/Scenes/",System.StringComparison.OrdinalIgnoreCase))scenes.Add(scene);}
            foreach(var scene in scenes)if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;Time.timeScale=1;GameInput.Suspended=false;if(!Object.FindFirstObjectByType<AudioListener>())Own(new GameObject("Closed-loop fixture listener",typeof(AudioListener)));
        }

        [UnityTearDown]public IEnumerator Cleanup()
        {
            if(host)Object.Destroy(host);
            yield return null;
            foreach(var item in owned)if(item&&item!=host)Object.Destroy(item);
            owned.Clear();yield return null;
            if(fixtureScene.IsValid()&&fixtureScene.isLoaded){var empty=SceneManager.CreateScene("ClosedLoopFlightCleanup_"+System.Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(empty);yield return SceneManager.UnloadSceneAsync(fixtureScene);}
            BlockModules.Configure(previousDatabase);Time.timeScale=previousScale;GameInput.Suspended=previousSuspended;Cursor.lockState=previousCursorLock;Cursor.visible=previousCursorVisible;UnityEngine.Random.state=previousRandom;
        }

        [UnityTest]public IEnumerator CombinedForwardAndYawInputRemainsFiniteAndSettlesAfterRelease()
        {
            var database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            database.modules=new List<ModuleDefinition>{
                Definition(database,BlockShape.Core,1200,1200,0,ModuleCategory.Core),
                Definition(database,BlockShape.Cube,200,200,0,ModuleCategory.Hull),
                Definition(database,BlockShape.Thruster1,200,200,30000,ModuleCategory.Propulsion)
            };
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;tuning.maxSpeed=50;tuning.maxAngularAcceleration=180;tuning.maxAngularRate=75;
            host=Own(new GameObject("Flight wrench closed-loop fixture"));
            var body=host.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
            var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            var blueprint=new BlueprintData{name="Closed-loop wrench fixture",hasCore=true,corePartId="core"};
            blueprint.parts.Add(Part("core",BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity));
            blueprint.parts.Add(Part("forward",BlockShape.Thruster1,new Vector3(0,.5f,-1),BlockPlacement.FaceRotation(Vector3.back,0)));
            blueprint.parts.Add(Part("reverse",BlockShape.Thruster1,new Vector3(0,.5f,1),BlockPlacement.FaceRotation(Vector3.forward,0)));
            blueprint.parts.Add(Part("yaw-left",BlockShape.Thruster1,new Vector3(-1,.5f,0),BlockPlacement.FaceRotation(Vector3.back,0)));
            blueprint.parts.Add(Part("yaw-right",BlockShape.Thruster1,new Vector3(1,.5f,0),BlockPlacement.FaceRotation(Vector3.back,0)));
            assembly.Import(blueprint);
            var runtime=host.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);
            var centering=Quaternion.Inverse(runtime.CoreRotation);assembly.transform.localRotation=centering;assembly.transform.localPosition=-(centering*runtime.CenterOfMass());
            var flight=host.AddComponent<ShipFlightController>();flight.Initialize(body,runtime,tuning,null,4);

            flight.SetExternalInput(Vector3.forward,new Vector2(.2f,0));
            for(int i=0;i<80;i++)yield return new WaitForFixedUpdate();
            Assert.That(flight.CurrentSpeed,Is.GreaterThan(.5f));
            Assert.That(runtime.Performance.angularAccelerationPositive.y+runtime.Performance.angularAccelerationNegative.y,Is.GreaterThan(0));
            Assert.That(IsFinite(body.rotation),Is.True);

            flight.SetExternalInput(Vector3.zero,Vector2.zero);
            for(int i=0;i<120;i++)yield return new WaitForFixedUpdate();
            var angularField=typeof(ShipFlightController).GetField("angularVelocity",BindingFlags.Instance|BindingFlags.NonPublic);
            var finalRate=(Vector3)angularField.GetValue(flight);
            Assert.That(IsFinite(finalRate),Is.True);
            Assert.That(finalRate.magnitude,Is.LessThan(2f),"The allocator must use opposing torque to settle after the steering target returns to zero.");
            var settled=body.rotation;
            for(int i=0;i<20;i++)yield return new WaitForFixedUpdate();
            Assert.That(Angle(settled,body.rotation),Is.LessThan(.1f),"No residual turn jitter should remain after the angular rate settles.");
        }

#if UNITY_EDITOR
        [UnityTest]public IEnumerator LargeManeuverBattleshipSettlesWithoutLargeCrossAxisReversals()=>RunLargeBattleship("Battleship-Maneuver");
        [UnityTest]public IEnumerator LargeHeavyBattleshipSettlesWithoutLargeCrossAxisReversals()=>RunLargeBattleship("Battleship-Heavy");

        IEnumerator RunLargeBattleship(string blueprintName)
        {
            var source=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            Assert.That(source,Is.Not.Null,"Use the real project FlightTuning asset for this hull test.");
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Documentation","Battleship-2026-11-02","Blueprints",blueprintName+".json"));
            Assert.That(File.Exists(path),Is.True,"The generated large-hull fixture must be present.");
            var blueprint=JsonUtility.FromJson<BlueprintData>(File.ReadAllText(path));
            Assert.That(blueprint.parts.Count,Is.GreaterThanOrEqualTo(300));
            var tuning=Own(Object.Instantiate(source));tuning.name=source.name+" (closed-loop test copy)";
            host=Own(new GameObject(blueprintName+" closed-loop fixture"));
            var body=host.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
            var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);assembly.Import(blueprint);
            var runtime=host.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);
            var centering=Quaternion.Inverse(runtime.CoreRotation);assembly.transform.localRotation=centering;assembly.transform.localPosition=-(centering*runtime.CenterOfMass());
            body.mass=Mathf.Max(1,runtime.Performance.totalMass);body.centerOfMass=Vector3.zero;
            var flight=host.AddComponent<ShipFlightController>();flight.Initialize(body,runtime,tuning,null,Mathf.Max(4,runtime.HullRadius));
            var angularField=typeof(ShipFlightController).GetField("angularVelocity",BindingFlags.Instance|BindingFlags.NonPublic);

            flight.SetExternalInput(Vector3.forward,new Vector2(.18f,.12f));
            for(int i=0;i<50;i++)yield return new WaitForFixedUpdate();
            Assert.That(IsFinite(body.rotation),Is.True);
            Assert.That(flight.CurrentSpeed,Is.GreaterThan(0));
            flight.SetExternalInput(Vector3.forward,Vector2.zero);
            for(int i=0;i<50;i++)yield return new WaitForFixedUpdate();
            Assert.That(flight.CurrentSpeed,Is.GreaterThan(.1f),"Releasing the turn input must preserve the forward command.");

            flight.SetExternalInput(Vector3.zero,Vector2.zero);
            Vector3 previous=(Vector3)angularField.GetValue(flight);float peakPitch=0,peakYaw=0;int largePitchReversals=0,largeYawReversals=0;
            for(int i=0;i<150;i++)
            {
                yield return new WaitForFixedUpdate();var current=(Vector3)angularField.GetValue(flight);
                Assert.That(IsFinite(current),Is.True,blueprintName+" angular velocity must stay finite.");
                peakPitch=Mathf.Max(peakPitch,Mathf.Abs(current.x));peakYaw=Mathf.Max(peakYaw,Mathf.Abs(current.y));
                if(previous.x*current.x<0&&Mathf.Abs(previous.x)>2&&Mathf.Abs(current.x)>2)largePitchReversals++;
                if(previous.y*current.y<0&&Mathf.Abs(previous.y)>2&&Mathf.Abs(current.y)>2)largeYawReversals++;
                previous=current;
            }
            var finalRate=(Vector3)angularField.GetValue(flight);
            Assert.That(Mathf.Abs(finalRate.x),Is.LessThan(.5f),blueprintName+" pitch must settle.");
            Assert.That(Mathf.Abs(finalRate.y),Is.LessThan(.5f),blueprintName+" yaw must settle.");
            Assert.That(peakPitch,Is.LessThan(20),blueprintName+" pitch cross-axis peak must remain bounded.");
            Assert.That(peakYaw,Is.LessThan(20),blueprintName+" yaw cross-axis peak must remain bounded.");
            Assert.That(largePitchReversals,Is.LessThanOrEqualTo(1),blueprintName+" pitch must not oscillate through repeated large reversals.");
            Assert.That(largeYawReversals,Is.LessThanOrEqualTo(1),blueprintName+" yaw must not oscillate through repeated large reversals.");
        }
#endif

        T Own<T>(T item)where T:Object{owned.Add(item);return item;}
        ModuleDefinition Definition(ModuleDatabase ignored,BlockShape shape,float mass,float hp,float thrust,ModuleCategory category)
        {
            var definition=Own(ScriptableObject.CreateInstance<ModuleDefinition>());definition.shape=shape;definition.massKg=mass;definition.maxHp=hp;definition.thrustNewtons=thrust;
            definition.category=category;definition.occupiedCells=new[]{Vector3Int.zero};definition.localExhaustDirections=new[]{Vector3.up};return definition;
        }
        static BlockRecord Part(string id,BlockShape shape,Vector3 position,Quaternion rotation)=>new BlockRecord{id=id,shape=shape,position=position,rotation=rotation,cell=BlockPlacement.GridHint(position)};
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static bool IsFinite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        static bool IsFinite(Quaternion q)=>Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w);
        static float Angle(Quaternion a,Quaternion b)=>Quaternion.Angle(a,b);
    }
}
