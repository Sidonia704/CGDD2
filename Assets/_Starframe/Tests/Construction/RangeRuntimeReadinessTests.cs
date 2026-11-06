using System.Reflection;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class RangeRuntimeReadinessTests
    {
        const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject root;
        [SetUp] public void Setup(){root=new GameObject("Range lifecycle readiness fixture");}
        [TearDown] public void Cleanup(){Object.DestroyImmediate(root);}
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Fields).SetValue(target,value);
        static void Invoke(object target,string method)=>target.GetType().GetMethod(method,Fields).Invoke(target,null);
        [Test] public void ReloadedCoreIndexCannotOutliveItsRuntimePartCollection()
        {
            var ship=root.AddComponent<FlightShipRuntime>();root.transform.position=new Vector3(17,23,41);
            ship.Parts.Add(new ShipPartState{view=root,hp=100,present=true});Set(ship,"coreIndex",0);
            Assert.That(ship.IsAlive,Is.True);
            // Reproduce Unity retaining the scalar index while rebuilding a readonly list after reload.
            ship.Parts.Clear();
            Assert.That(ship.IsAlive,Is.False);Assert.That(ship.CorePosition,Is.EqualTo(root.transform.position));Assert.That(ship.CorePhysicsPosition,Is.EqualTo(root.transform.position));
            Set(ship,"coreIndex",int.MaxValue);Assert.That(ship.IsAlive,Is.False);Assert.That(ship.CorePhysicsPosition,Is.EqualTo(root.transform.position));
        }
        [Test] public void MissingRuntimePerformanceAndCombatActorWaitForInitialization()
        {
            var ship=root.AddComponent<FlightShipRuntime>();var flight=root.AddComponent<ShipFlightController>();
            Assert.That(flight.Performance,Is.Null);Set(flight,"runtime",ship);Set(flight,"body",root.AddComponent<Rigidbody>());
            Assert.DoesNotThrow(()=>Invoke(flight,"FixedUpdate"));Assert.That(flight.ThrusterThrottle(null,0),Is.Zero);
            Set(ship,"<Performance>k__BackingField",new ShipPerformance());
            var hud=root.AddComponent<RangeHUD>();Set(hud,"flight",flight);Set(hud,"combat",root.AddComponent<RangeCombatWorld>());
            var canvasObject=new GameObject("Pending HUD canvas",typeof(Canvas));canvasObject.transform.SetParent(root.transform);Set(hud,"canvas",canvasObject.GetComponent<Canvas>());
            var cameraObject=new GameObject("Pending HUD camera",typeof(Camera));cameraObject.transform.SetParent(root.transform);Set(hud,"worldCamera",cameraObject.GetComponent<Camera>());
            var telemetryType=typeof(RangeHUD).GetField("info",Fields).FieldType;var textObject=new GameObject("Pending HUD telemetry",typeof(RectTransform),telemetryType);textObject.transform.SetParent(canvasObject.transform);Set(hud,"info",textObject.GetComponent(telemetryType));
            Assert.DoesNotThrow(()=>Invoke(hud,"Update"));Assert.DoesNotThrow(()=>Invoke(hud,"LateUpdate"));
            Assert.That(typeof(RangeHUD).GetField("sceneTexture",Fields).GetValue(hud),Is.Null,"An absent actor must gate HUD work before Resize allocates a render target.");
        }
    }
}

