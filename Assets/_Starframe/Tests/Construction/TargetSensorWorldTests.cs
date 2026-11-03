using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class TargetSensorWorldTests
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<RangeCombatWorld.Actor> actors = new List<RangeCombatWorld.Actor>();
        ModuleDatabase database;
        FlightTuning flight;
        BattleTuning battle;
        TargetSensorWorld sensors;

        [SetUp] public void SetUp()
        {
            database = AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            Assert.That(database, Is.Not.Null);
            flight = Own(ScriptableObject.CreateInstance<FlightTuning>());
            flight.database = database;
            battle = Own(ScriptableObject.CreateInstance<BattleTuning>());
            sensors = new TargetSensorWorld(battle);
            BlockModules.Configure(database);
        }

        [TearDown] public void TearDown()
        {
            sensors.Dispose();
            actors.Clear();
            BlockModules.Configure(null);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(0, 1000)]
        [TestCase(1, 1000)]
        [TestCase(10, 3000)]
        [TestCase(40, 6000)]
        [TestCase(160, 6000)]
        public void SignatureRangeUsesSquareRootAndClamps(float radius, float expected)
        {
            Assert.That(TargetSensorWorld.RangeForRadius(radius, battle), Is.EqualTo(expected).Within(.001f));
        }

        [Test] public void MaximumRangeUsesTheSameLiveSanitizedBoundsAsDiscovery()
        {
            Assert.That(sensors.MaximumRange, Is.EqualTo(6000));
            battle.sensorMaximumRange = 7500;
            Assert.That(sensors.MaximumRange, Is.EqualTo(7500));
            Assert.That(TargetSensorWorld.RangeForRadius(1000, battle), Is.EqualTo(sensors.MaximumRange));
            battle.sensorMinimumRange = 2000; battle.sensorMaximumRange = 1000;
            Assert.That(sensors.MaximumRange, Is.EqualTo(2000));
            Assert.That(TargetSensorWorld.RangeForRadius(1000, battle), Is.EqualTo(sensors.MaximumRange));
        }

        [Test] public void SignatureTracksAttachedSurvivorsDespiteStaleHullRadiusAndDisabledRenderers()
        {
            var target = Actor(1, Vector3.zero, 20);
            float full = sensors.SignatureRadius(target.ship);
            float initialHullRadius = target.ship.HullRadius;
            Assert.That(full, Is.GreaterThan(10));
            foreach (var part in target.ship.Parts)
                if (part.view.TryGetComponent<MeshRenderer>(out var renderer)) renderer.enabled = false;
            target.ship.Recalculate();
            Assert.That(sensors.SignatureRadius(target.ship), Is.EqualTo(full).Within(.001f),
                "Instanced/disabled source renderers still describe attached parts.");

            target.ship.SetPartPresent("arm-1", false);
            Assert.That(target.ship.Parts.Find(p => p.record.id == "arm-20").detached, Is.True);
            Assert.That(target.ship.HullRadius, Is.EqualTo(initialHullRadius), "The old runtime radius remains an initial-size cache.");
            Assert.That(sensors.SignatureRadius(target.ship), Is.LessThan(1),
                "Detached views and their old renderer bounds must no longer enlarge the sensor signature.");
            Assert.That(sensors.DetectionRange(target.ship), Is.EqualTo(battle.sensorMinimumRange));
        }

        [Test] public void GeometryIsCachedUntilARealPartOrPerformanceRevision()
        {
            var target = Actor(1, Vector3.zero);
            float original = sensors.SignatureRadius(target.ship);
            target.ship.Parts[0].view.transform.localScale = Vector3.one * 4;
            Assert.That(sensors.SignatureRadius(target.ship), Is.EqualTo(original), "Reading contacts must not rescan part meshes.");
            target.ship.Recalculate();
            Assert.That(sensors.SignatureRadius(target.ship), Is.EqualTo(original * 4).Within(.001f));
        }

        [Test] public void BothTeamsUseTheTargetsSizeAndDiscoveryDoesNotRequireFacing()
        {
            var small = Actor(0, Vector3.zero);
            var large = Actor(1, Vector3.forward * 1800, 20);
            small.body.rotation = Quaternion.Euler(0, 180, 0);
            large.body.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            sensors.Update(actors, .02f);
            Assert.That(sensors.IsDetected(small.ship, large.ship), Is.True);
            Assert.That(sensors.IsDetected(large.ship, small.ship), Is.False,
                "Both teams inspect the target signature; a large observer gets no bonus for seeing a small ship.");

            small.ship.Team = 1; large.ship.Team = 0;
            sensors.Update(actors, .02f);
            Assert.That(sensors.IsDetected(small.ship, large.ship), Is.True);
            Assert.That(sensors.IsDetected(large.ship, small.ship), Is.False);
            Move(large, Vector3.forward * 900);
            sensors.Update(actors, .02f);
            Assert.That(sensors.IsDetected(large.ship, small.ship), Is.True);
        }

        [Test] public void EntryExitBandAndContinuousLossDelayAreSharedAndDiscoveryEventsOnlyReportTransitions()
        {
            var first = Actor(0, Vector3.zero);
            var second = Actor(1, Vector3.forward * 1001);
            int discoveries = 0;
            sensors.TargetDiscovered += (_, __) => discoveries++;
            sensors.Update(actors, .02f);
            Assert.That(sensors.IsDetected(first.ship, second.ship), Is.False);
            Move(second, Vector3.forward * 1000);
            sensors.Update(actors, .02f);
            Assert.That(discoveries, Is.EqualTo(2));
            Assert.That(sensors.GetDetectedTargets(first.ship), Does.Contain(second.ship));
            Move(second, Vector3.forward * 1090);
            sensors.Update(actors, 2);
            Assert.That(sensors.IsDetected(first.ship, second.ship), Is.True);
            Move(second, Vector3.forward * 1110);
            sensors.Update(actors, .49f);
            Assert.That(sensors.IsDetected(first.ship, second.ship), Is.True);
            Move(second, Vector3.forward * 1090);
            sensors.Update(actors, .02f);
            Move(second, Vector3.forward * 1110);
            sensors.Update(actors, .49f);
            Assert.That(sensors.IsDetected(first.ship, second.ship), Is.True, "Returning to the band resets the loss timer.");
            sensors.Update(actors, .02f);
            Assert.That(sensors.IsDetected(first.ship, second.ship), Is.False);
            Assert.That(sensors.IsDetected(second.ship, first.ship), Is.False);
            Assert.That(discoveries, Is.EqualTo(2));
            Move(second, Vector3.forward * 1050);
            sensors.Update(actors, .02f);
            Assert.That(sensors.IsDetected(first.ship, second.ship), Is.False, "Lost contacts must re-enter the entry radius.");
            Move(second, Vector3.forward * 990);
            sensors.Update(actors, .02f);
            Assert.That(discoveries, Is.EqualTo(4));
        }

        [Test] public void EnemyLockNeedsDiscoveryFacingAndContinuousAcquisitionThenSurvivesLookingAway()
        {
            var observer = Actor(1, Vector3.zero);
            var target = Actor(0, Vector3.forward * 900);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, 1), Is.False,
                "A caller reporting aim alignment cannot invent an undiscovered lock.");
            sensors.Update(actors, .02f);
            observer.body.rotation = Quaternion.Euler(0, 180, 0);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, 1), Is.False);
            observer.body.rotation = Quaternion.identity;
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, .2f), Is.False);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, false, .02f), Is.False);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, .2f), Is.False);
            Assert.That(sensors.CountLocksOn(target.ship), Is.Zero);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, .1f), Is.True);
            Assert.That(sensors.CountLocksOn(target.ship), Is.EqualTo(1));
            Assert.That(sensors.EnemyLocks[0].Observer, Is.SameAs(observer.ship));
            observer.body.rotation = Quaternion.Euler(0, 180, 0);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, false, .02f), Is.True);
            Move(target, Vector3.forward * 1110);
            sensors.Update(actors, .49f);
            Assert.That(sensors.CountLocksOn(target.ship), Is.EqualTo(1));
            sensors.Update(actors, .02f);
            Assert.That(sensors.CountLocksOn(target.ship), Is.Zero);
        }

        [Test] public void DeathRespawnAndExplicitCancellationCannotInheritEnemyLocks()
        {
            var observer = Actor(1, Vector3.zero);
            var target = Actor(0, Vector3.forward * 900);
            sensors.Update(actors, .02f);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, .3f), Is.True);
            sensors.ClearEnemyLock(observer.ship);
            Assert.That(sensors.CountLocksOn(target.ship), Is.Zero);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, .3f), Is.True);
            target.ship.SetPartPresent("core", false);
            Assert.That(sensors.IsEnemyLocked(observer.ship, target.ship), Is.False);
            Assert.That(sensors.CountLocksOn(target.ship), Is.Zero);
            target.ship.Respawn();
            sensors.Update(actors, .02f);
            Assert.That(sensors.CountLocksOn(target.ship), Is.Zero);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, .2f), Is.False);
            Assert.That(sensors.UpdateEnemyLock(observer.ship, target.ship, true, .1f), Is.True);
            observer.ship.Respawn();
            Assert.That(sensors.CountLocksOn(target.ship), Is.Zero);
            sensors.Update(actors, .02f);
            Assert.That(sensors.CountLocksOn(target.ship), Is.Zero);
        }

        [Test] public void SelectionUsesVisibleMarkerNearestTheVirtualCursorInsteadOfFirstOrMostForwardShip()
        {
            var centered = Actor(1, Vector3.forward * 100);
            var nearCursor = Actor(1, new Vector3(8, 0, 100));
            var hidden = Actor(1, Vector3.back * 100);
            var camera = Own(new GameObject("Sensor selection test camera")).AddComponent<Camera>();
            camera.pixelRect = new Rect(0, 0, 1920, 1080);
            camera.fieldOfView = 60;
            camera.transform.position = Vector3.up * .5f;
            var corePosition = nearCursor.ship.CorePosition;
            var screen = camera.WorldToScreenPoint(corePosition);
            Assert.That(nearCursor.ship.IsAlive, Is.True);
            Assert.That(Vector3.Distance(corePosition, nearCursor.ship.CorePhysicsPosition), Is.LessThan(.001f),
                "The EditMode fixture must explicitly synchronize its rendered and physical poses before screen selection.");
            Assert.That(screen.z, Is.GreaterThan(0));
            Assert.That(camera.pixelRect.Contains((Vector2)screen), Is.True);
            Vector2 cursor = screen;
            var candidates = new[] { centered.ship, hidden.ship, nearCursor.ship };
            Assert.That(TargetLockSystem.SelectNearestVisibleTarget(candidates, camera, cursor, 80), Is.SameAs(nearCursor.ship));
            Assert.That(TargetLockSystem.SelectNearestVisibleTarget(candidates, camera, new Vector2(200, 200), 10), Is.Null);
            Move(nearCursor, new Vector3(1000, 0, 100));
            cursor = camera.WorldToScreenPoint(nearCursor.ship.CorePosition);
            Assert.That(TargetLockSystem.SelectNearestVisibleTarget(new[] { nearCursor.ship, hidden.ship }, camera, cursor, 10000), Is.Null,
                "Being closest to a virtual cursor cannot acquire a marker outside the view or behind the camera.");
        }

        [Test] public void QueuedTogglesUseTheVisibleSmoothedCursorAndRemainReusable()
        {
            var targeting=LockFixture(out var world,out var camera,out var player,out var enemy);
            var canvasObject=Own(new GameObject("Lock cursor canvas",typeof(RectTransform),typeof(Canvas)));
            canvasObject.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var graphicObject=new GameObject("Lock cursor",typeof(RectTransform));graphicObject.transform.SetParent(canvasObject.transform,false);
            var rect=graphicObject.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            // Use the real graphic via reflection to keep this editor fixture independent of the uGUI assembly.
            var graphicType=typeof(RangeHUD).Assembly.GetType("Starframe.Flight.FlightAimGraphic");
            var graphic=graphicObject.AddComponent(graphicType);
            graphicType.GetMethod("Initialize").Invoke(graphic,new object[]{camera,player.flight});
            Canvas.ForceUpdateCanvases();
            SetPrivate(graphic,"positioned",true);SetPrivate(graphic,"desiredPoint",rect.rect.center);
            var hud=Own(new GameObject("Lock cursor HUD")).AddComponent<RangeHUD>();SetPrivate(hud,"flightAim",graphic);
            targeting.Initialize(world,camera,hud,battle);
            player.flight.SetExternalInput(Vector3.zero,new Vector2(.9f,.3f));
            Assert.That(hud.TryGetAimCursorScreenPoint(out var shown),Is.True);
            var marker=(Vector2)camera.WorldToScreenPoint(enemy.ship.CorePosition);
            Assert.That(Vector2.Distance(shown,marker),Is.LessThan(1),"The rendered cursor is still over the marker while its commanded offset has moved on.");
            Assert.That(player.flight.AimOffsetPixels.magnitude,Is.GreaterThan(battle.sensorAcquireRadiusPixels+1));
            targeting.ToggleLock();Assert.That(targeting.HasTarget,Is.False,"The old raw-offset selection would miss this visible marker.");
            bool suspended=Starframe.App.GameInput.Suspended;Starframe.App.GameInput.Suspended=false;
            try {
                for(int i=0;i<20;i++){
                    SetPrivate(targeting,"toggleRequested",true);typeof(TargetLockSystem).GetMethod("LateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(targeting,null);
                    Assert.That(targeting.HasTarget,Is.EqualTo(i%2==0),"A queued press must toggle exactly once at the visible cursor, including repeated reacquisition.");
                    typeof(TargetLockSystem).GetMethod("LateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(targeting,null);
                    Assert.That(targeting.HasTarget,Is.EqualTo(i%2==0),"A consumed press must never run again in a later presentation pass.");
                }
            } finally {Starframe.App.GameInput.Suspended=suspended;}
        }

        [Test] public void MissedAcquisitionAndEitherShipsRespawnCannotPoisonLaterLockAttempts()
        {
            var targeting=LockFixture(out var world,out var camera,out var player,out var enemy);
            Move(enemy,new Vector3(500,0,100));sensors.Update(actors,0);
            targeting.ToggleLock();Assert.That(targeting.HasTarget,Is.False);
            Move(enemy,Vector3.forward*100);sensors.Update(actors,0);
            targeting.ToggleLock();Assert.That(targeting.LockedTarget,Is.SameAs(enemy.ship));Assert.That(targeting.HasTarget,Is.True);
            enemy.ship.Respawn();Assert.That(targeting.HasTarget,Is.False);sensors.Update(actors,0);
            targeting.ToggleLock();Assert.That(targeting.HasTarget,Is.True);
            player.ship.Respawn();Assert.That(targeting.HasTarget,Is.False);sensors.Update(actors,0);
            targeting.ToggleLock();Assert.That(targeting.HasTarget,Is.True);
            Move(enemy,Vector3.forward*7000);sensors.Update(actors,battle.sensorLossDelay+.1f);targeting.RefreshLeadPoints();
            Assert.That(targeting.HasTarget,Is.False);Assert.That(targeting.LockedTarget,Is.Null);
            Move(enemy,Vector3.forward*100);sensors.Update(actors,0);
            targeting.ToggleLock();Assert.That(targeting.HasTarget,Is.True);
        }

        [Test] public void HeldEdgesRearmAcrossEntryAndModalsWithoutDependingOnTransientPressFlags()
        {
            var targeting=LockFixture(out var world,out var camera,out var player,out var enemy);
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var sample=typeof(TargetLockSystem).GetMethod("ProcessLockInput",flags);var present=typeof(TargetLockSystem).GetMethod("LateUpdate",flags);
            bool suspended=Starframe.App.GameInput.Suspended;Starframe.App.GameInput.Suspended=false;
            uint step=0;
            void Input(bool held,bool tapped,bool allowed=true,bool sameUpdate=false){if(!sameUpdate)step++;sample.Invoke(targeting,new object[]{held,tapped,allowed,step});present.Invoke(targeting,null);}
            try {
                Input(false,false);Input(true,false);Assert.That(targeting.HasTarget,Is.True,"A physical rising edge must work even if wasPressedThisFrame has expired.");
                Input(true,false);Assert.That(targeting.HasTarget,Is.True,"Held input must not retrigger.");
                Input(false,false);Input(true,false);Assert.That(targeting.HasTarget,Is.False);
                targeting.Initialize(world,camera,null,battle);
                Input(true,false);Assert.That(targeting.HasTarget,Is.False,"A key held while entering must not auto-lock.");
                Input(false,false);Input(true,false);Assert.That(targeting.HasTarget,Is.True,"Reentry must rearm a fresh release/press cycle.");
                Input(false,false,false);Input(true,true,false);Input(true,false);Assert.That(targeting.HasTarget,Is.True,"A modal/focus transition must not consume a held menu key as a new action.");
                Input(false,false);Input(true,false);Assert.That(targeting.HasTarget,Is.False);
                Input(false,false);Input(false,true);Assert.That(targeting.HasTarget,Is.True,"A short down/up tap within one render frame must still register.");
                Input(false,true,true,true);Assert.That(targeting.HasTarget,Is.True,"The same pulse can only be consumed once.");
            } finally {Starframe.App.GameInput.Suspended=suspended;}
        }

        [Test] public void FirstFreshPressAndBackToBackCompletedTapsRemainResponsive()
        {
            var targeting=LockFixture(out _,out _,out _,out _);
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var sample=typeof(TargetLockSystem).GetMethod("ProcessLockInput",flags);var present=typeof(TargetLockSystem).GetMethod("LateUpdate",flags);
            bool suspended=Starframe.App.GameInput.Suspended;Starframe.App.GameInput.Suspended=false;
            try{
                for(uint step=1;step<=20;step++){
                    sample.Invoke(targeting,new object[]{false,true,true,step});present.Invoke(targeting,null);
                    Assert.That(targeting.HasTarget,Is.EqualTo(step%2==1),"Each completed physical tap must register without requiring an idle render frame.");
                    sample.Invoke(targeting,new object[]{false,true,true,step});present.Invoke(targeting,null);
                    Assert.That(targeting.HasTarget,Is.EqualTo(step%2==1),"Re-reading one InputSystem update must not repeat that press.");
                }
                sample.Invoke(targeting,new object[]{true,true,false,21u});present.Invoke(targeting,null);
                sample.Invoke(targeting,new object[]{true,false,true,22u});present.Invoke(targeting,null);
                Assert.That(targeting.HasTarget,Is.False,"A held menu key is not a new in-flight press.");
                sample.Invoke(targeting,new object[]{false,false,false,23u});present.Invoke(targeting,null);
                sample.Invoke(targeting,new object[]{true,true,true,24u});present.Invoke(targeting,null);
                Assert.That(targeting.HasTarget,Is.True,"A genuinely new key press on the first allowed frame must not be swallowed.");
            }finally{Starframe.App.GameInput.Suspended=suspended;}
        }

        [Test] public void QueuedPressCanAcquireAtTheShipReticleWhileTheSteeringCursorIsOffset()
        {
            var targeting=LockFixture(out _,out var camera,out var player,out var enemy);
            player.flight.SetExternalInput(Vector3.zero,new Vector2(.9f,.3f));
            targeting.ToggleLock();Assert.That(targeting.HasTarget,Is.False,"The steering cursor is outside the marker's acquisition radius.");
            bool suspended=Starframe.App.GameInput.Suspended;Starframe.App.GameInput.Suspended=false;
            try{
                SetPrivate(targeting,"toggleRequested",true);
                typeof(TargetLockSystem).GetMethod("LateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(targeting,null);
                Assert.That(targeting.LockedTarget,Is.SameAs(enemy.ship));Assert.That(targeting.HasTarget,Is.True);
                targeting.Clear();Move(enemy,new Vector3(500,0,100));sensors.Update(actors,0);
                SetPrivate(targeting,"toggleRequested",true);
                typeof(TargetLockSystem).GetMethod("LateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(targeting,null);
                Assert.That(targeting.HasTarget,Is.False,"The fallback must not acquire an offscreen contact or ignore the existing radius.");
            }finally{Starframe.App.GameInput.Suspended=suspended;}
        }

        TargetLockSystem LockFixture(out RangeCombatWorld world,out Camera camera,out RangeCombatWorld.Actor player,out RangeCombatWorld.Actor enemy)
        {
            camera=Own(new GameObject("Lock fixture camera")).AddComponent<Camera>();camera.fieldOfView=60;
            camera.pixelRect=new Rect(0,0,Screen.width,Screen.height);camera.transform.position=Vector3.up*.5f;
            player=Actor(0,Vector3.zero);enemy=Actor(1,Vector3.forward*100);
            foreach(var actor in actors){
                actor.flight=actor.ship.gameObject.AddComponent<ShipFlightController>();actor.flight.Initialize(actor.body,actor.ship,flight,camera,1);actor.flight.enabled=false;
                actor.weapons=new ShipWeapons();actor.weapons.Initialize(actor.ship,new ProjectileWorld());
            }
            world=Own(new GameObject("Lock fixture world")).AddComponent<RangeCombatWorld>();world.enabled=false;
            SetPrivate(world,"player",player);((List<RangeCombatWorld.Actor>)world.Actors).AddRange(actors);
            typeof(RangeCombatWorld).GetProperty("Sensors").SetValue(world,sensors);sensors.Update(actors,0);
            var targeting=world.gameObject.AddComponent<TargetLockSystem>();targeting.Initialize(world,camera,null,battle);return targeting;
        }
        static void SetPrivate(object instance,string name,object value)=>instance.GetType().GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(instance,value);

        RangeCombatWorld.Actor Actor(int team, Vector3 position, int armLength = 0)
        {
            var blueprint = new BlueprintData { name = "sensor test" };
            blueprint.parts.Add(new BlockRecord { id = "core", shape = BlockShape.Core, position = Vector3.up * .5f, rotation = Quaternion.identity });
            for (int x = 1; x <= armLength; x++) blueprint.parts.Add(new BlockRecord
            { id = "arm-" + x, shape = BlockShape.Cube, position = new Vector3(x, .5f, 0), rotation = Quaternion.identity });
            var root = Own(new GameObject("Sensor test ship"));
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var child = new GameObject("Assembly"); child.transform.SetParent(root.transform, false);
            var assembly = child.AddComponent<ShipAssembly>(); assembly.Configure(database); assembly.Import(blueprint);
            var runtime = root.AddComponent<FlightShipRuntime>(); runtime.Initialize(assembly, blueprint, flight, battle); runtime.Team = team;
            var actor = new RangeCombatWorld.Actor { ship = runtime, body = body };
            actors.Add(actor); Move(actor, position); return actor;
        }

        static void Move(RangeCombatWorld.Actor actor, Vector3 position)
        {
            // EditMode has no physics tick to copy Rigidbody.position back to the rendered Transform.
            actor.ship.transform.position = position;
            actor.body.position = position;
            Physics.SyncTransforms();
        }
        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    }
}
