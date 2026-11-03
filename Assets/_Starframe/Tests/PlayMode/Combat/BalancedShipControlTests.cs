using System;
using System.Collections;
using System.IO;
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
    public sealed class BalancedShipControlTests
    {
        GameObject host;FlightTuning tuning;ModuleDatabase previousDatabase;float previousScale;
        Scene fixtureScene;bool previousSuspended,previousCursorVisible;CursorLockMode previousCursorLock;UnityEngine.Random.State previousRandom;
        [Serializable]sealed class Workspace{public BlueprintData ship=null;}
        [UnitySetUp]public IEnumerator SetUp()
        {
            previousDatabase=BlockModules.ConfiguredDatabase;previousScale=Time.timeScale;previousSuspended=GameInput.Suspended;
            previousCursorLock=Cursor.lockState;previousCursorVisible=Cursor.visible;previousRandom=UnityEngine.Random.state;
            // These free-flight assertions use the normal PhysX scene and real colliders. Remove
            // prior UI/range fixtures so their hulls at the origin cannot obstruct this ship.
            fixtureScene=SceneManager.CreateScene("BalancedFlightFixture_"+Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(fixtureScene);
            var oldScenes=new System.Collections.Generic.List<Scene>();for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene!=fixtureScene&&scene.path.StartsWith("Assets/_Starframe/Scenes/",StringComparison.OrdinalIgnoreCase))oldScenes.Add(scene);}
            foreach(var scene in oldScenes)if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;Time.timeScale=1;GameInput.Suspended=false;
        }
        [UnityTearDown]public IEnumerator CleanUp()
        {
            if(host)UnityEngine.Object.Destroy(host);yield return null;if(tuning)UnityEngine.Object.Destroy(tuning);yield return null;
            if(fixtureScene.IsValid()&&fixtureScene.isLoaded){var empty=SceneManager.CreateScene("BalancedFlightCleanup_"+Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(empty);yield return SceneManager.UnloadSceneAsync(fixtureScene);}
            BlockModules.Configure(previousDatabase);Time.timeScale=previousScale;GameInput.Suspended=previousSuspended;Cursor.lockState=previousCursorLock;Cursor.visible=previousCursorVisible;UnityEngine.Random.state=previousRandom;
        }
#if UNITY_EDITOR
        [UnityTest]public IEnumerator CurrentPlayerShipFliesStraightOnAllSixAxes(){
            string path=Path.Combine(Application.persistentDataPath,"Workspace","current-ship.json");
            Assert.That(File.Exists(path),Is.True,"Exercise the real current player design read-only.");
            string before=File.ReadAllText(path);var data=JsonUtility.FromJson<Workspace>(before).ship;
            yield return CheckShip(data);
            Assert.That(File.ReadAllText(path),Is.EqualTo(before),"Flight verification must not rewrite the player workspace.");
        }
        [UnityTest]public IEnumerator HeavyReferenceFliesStraightOnAllSixAxes()=>CheckReference("Heavy");
        [UnityTest]public IEnumerator ManeuverReferenceFliesStraightOnAllSixAxes()=>CheckReference("Maneuver");
        [UnityTest]public IEnumerator AssaultReferenceFliesStraightOnAllSixAxes()=>CheckReference("Assault");
        IEnumerator CheckReference(string label){
            string path=Path.Combine(Application.dataPath,"..","Documentation","Battleship-2026-11-02","Blueprints","Battleship-"+label+".json");
            yield return CheckShip(JsonUtility.FromJson<BlueprintData>(File.ReadAllText(path)));
        }
        IEnumerator CheckShip(BlueprintData data){
            Assert.That(data,Is.Not.Null);
            var source=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            tuning=UnityEngine.Object.Instantiate(source);
            host=new GameObject("Balanced flight verification: "+data.name);
            if(!UnityEngine.Object.FindFirstObjectByType<AudioListener>())host.AddComponent<AudioListener>();
            var body=host.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
            var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);assembly.Import(data);
            var runtime=host.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,data,tuning);
            var inverse=Quaternion.Inverse(runtime.CoreRotation);assembly.transform.localRotation=inverse;assembly.transform.localPosition=-(inverse*runtime.CenterOfMass());
            var flight=host.AddComponent<ShipFlightController>();flight.Initialize(body,runtime,tuning,null,runtime.HullRadius);
            var axes=new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            foreach(var direction in axes){
                flight.ResetMotion(Vector3.zero,Quaternion.identity);flight.SetExternalInput(direction,Vector2.zero);
                for(int i=0;i<32;i++)yield return new WaitForFixedUpdate();
                int axis=Mathf.Abs(direction.x)>.5f?0:Mathf.Abs(direction.y)>.5f?1:2;
                float capacity=(direction[axis]>0?runtime.Performance.accelerationPositive:runtime.Performance.accelerationNegative)[axis];
                if(capacity>.1f)Assert.That(Vector3.Dot(flight.Velocity,direction),Is.GreaterThan(.01f),data.name+" must actually move in the requested direction.");
                Assert.That(Quaternion.Angle(Quaternion.identity,body.rotation),Is.LessThan(.001f),data.name+" changed attitude during "+direction);
                Assert.That(flight.AngularVelocity.sqrMagnitude,Is.LessThan(.000001f));
                Assert.That(Vector3.Cross(flight.Velocity,direction).sqrMagnitude,Is.LessThan(.000001f),data.name+" drifted sideways.");
                flight.SetExternalInput(Vector3.zero,Vector2.zero);
                for(int i=0;i<16;i++)yield return new WaitForFixedUpdate();
                Assert.That(flight.AngularVelocity.sqrMagnitude,Is.LessThan(.000001f),data.name+" braking created rotation.");
            }
            for(int axis=0;axis<3;axis++){
                flight.ResetMotion(Vector3.zero,Quaternion.identity);
                flight.SetExternalInput(Vector3.zero,axis==0?new Vector2(0,-.4f):axis==1?new Vector2(.4f,0):Vector2.zero,axis==2?.5f:0);
                for(int i=0;i<30;i++)yield return new WaitForFixedUpdate();
                if(runtime.Performance.angularAccelerationPositive[axis]>.1f)Assert.That(Mathf.Abs(flight.AngularVelocity[axis]),Is.GreaterThan(.05f),data.name+" must actually turn on the requested axis.");
                Assert.That(flight.Velocity.sqrMagnitude,Is.LessThan(.000001f),data.name+" rotation injected translation.");
                for(int other=0;other<3;other++)if(other!=axis)Assert.That(Mathf.Abs(flight.AngularVelocity[other]),Is.LessThan(.001f),data.name+" unwanted cross-axis rotation.");
                flight.SetExternalInput(Vector3.zero,Vector2.zero);
                for(int i=0;i<90;i++)yield return new WaitForFixedUpdate();
                Assert.That(flight.AngularVelocity.magnitude,Is.LessThan(.1f),data.name+" did not stop rotating after release.");
            }
            foreach(var part in runtime.Parts)if(BlockModules.IsThruster(part.record.shape)){
                runtime.SetPartPresent(part.record.id,false);break;
            }
            flight.ResetMotion(Vector3.zero,Quaternion.identity);flight.SetExternalInput(Vector3.forward,Vector2.zero,boost:true);flight.SetBoostPowerRatio(1);
            for(int i=0;i<40;i++)yield return new WaitForFixedUpdate();
            Assert.That(flight.AngularVelocity.sqrMagnitude,Is.LessThan(.000001f),data.name+" damaged boost flight started spinning.");
        }
#endif
    }
}
