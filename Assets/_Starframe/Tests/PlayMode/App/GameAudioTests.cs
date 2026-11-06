using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Starframe.App;

namespace Starframe.Tests
{
    public sealed class GameAudioTests
    {
        GameSettingsData saved;
        GameAudioRuntime runtime;
        [UnitySetUp]public IEnumerator SetUp()
        {
            saved=GameSettings.Current.Clone();GameAudio.Apply();yield return null;
            runtime=Object.FindFirstObjectByType<GameAudioRuntime>();
            Assert.That(runtime,Is.Not.Null);Assert.That(runtime.Tuning,Is.Not.Null);
            StopGatlingState();
            foreach(var source in runtime.GetComponentsInChildren<AudioSource>())if(source.name.StartsWith("Effect ")||source.name.StartsWith("Gatling ")||source.name=="Incoming missile warning")source.Stop();
            yield return new WaitForSecondsRealtime(.75f);
        }
        void StopGatlingState()=>typeof(GameAudioRuntime).GetMethod("StopGatling",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(runtime,null);
        [TearDown]public void TearDown(){StopGatlingState();GameAudio.Preview(saved);foreach(var source in runtime.GetComponentsInChildren<AudioSource>())if(source.name.StartsWith("Effect ")||source.name.StartsWith("Gatling ")||source.name=="Incoming missile warning")source.Stop();}

        [UnityTest]public IEnumerator BurstLimitDoesNotSilenceWarningsAndVoicesRemainBounded()
        {
            int accepted=0;
            for(int i=0;i<200;i++)if(runtime.Emit(GameSound.RailLarge))accepted++;
            Assert.That(accepted,Is.EqualTo(1),"Synchronized gun batteries must not sum hundreds of identical sounds.");
            foreach(GameSound sound in System.Enum.GetValues(typeof(GameSound)))runtime.Emit(sound);
            Assert.That(runtime.ActiveEffectVoices,Is.LessThanOrEqualTo(runtime.Tuning.effectVoices));
            Assert.That(runtime.Emit(GameSound.HealthShieldWarning,channel:2),Is.True,"A health warning can replace a less important active voice.");
            yield return null;
        }


        [UnityTest]public IEnumerator RailSizesShareCompleteClipAndRejectDuplicateBurst()
        {
            var cue=runtime.Tuning.Get(GameSound.RailSmall);
            Assert.That(cue.alternateClip,Is.Null);Assert.That(cue.clip,Is.SameAs(runtime.Tuning.Get(GameSound.RailLarge).clip));
            Assert.That(cue.clip.length,Is.GreaterThan(.75f));Assert.That(cue.minimumInterval,Is.GreaterThan(0));
            Assert.That(runtime.Emit(GameSound.RailSmall),Is.True);
            var first=runtime.GetComponentsInChildren<AudioSource>().First(s=>s.isPlaying&&(s.clip==cue.clip||s.clip==cue.alternateClip)).clip;
            Assert.That(runtime.Emit(GameSound.RailSmall),Is.False,"A simultaneous battery must not stack identical audio.");
            yield return new WaitForSecondsRealtime(cue.minimumInterval+.03f);
            foreach(var source in runtime.GetComponentsInChildren<AudioSource>())if(source.clip==cue.clip||source.clip==cue.alternateClip)source.Stop();
            Assert.That(runtime.Emit(GameSound.RailSmall),Is.True);
            var second=runtime.GetComponentsInChildren<AudioSource>().First(s=>s.isPlaying&&(s.clip==cue.clip||s.clip==cue.alternateClip)).clip;
            Assert.That(second,Is.SameAs(first));
        }

        [UnityTest]public IEnumerator SustainedFireRefreshesOneVoiceAndExpiresAfterLastShot()
        {
            var cue=runtime.Tuning.Get(GameSound.DirectLaser);Assert.That(cue.loopWhileFiring,Is.True);
            int before=runtime.PlayedCueCount;
            Assert.That(runtime.Emit(GameSound.DirectLaser),Is.True);
            var source=runtime.GetComponentsInChildren<AudioSource>().Single(s=>s.clip==cue.clip&&s.isPlaying);
            Assert.That(source.loop,Is.True);
            for(int i=0;i<200;i++)Assert.That(runtime.Emit(GameSound.DirectLaser),Is.True);
            Assert.That(runtime.PlayedCueCount,Is.EqualTo(before+1),"Refresh does not restart or allocate another voice.");
            Assert.That(runtime.GetComponentsInChildren<AudioSource>().Count(s=>s.clip==cue.clip&&s.isPlaying),Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(cue.releaseSeconds+.1f);
            Assert.That(source.isPlaying,Is.False);Assert.That(source.loop,Is.False);
        }


        [UnityTest]public IEnumerator GatlingStartSustainEndFollowActualFireAndSurviveVoicePressure()
        {
            using(var fixture=new WarningFixture(runtime))
            {
                var cue=runtime.Tuning.Get(GameSound.Gatling);
                Assert.That(cue.startClip,Is.Not.Null);Assert.That(cue.endClip,Is.Not.Null);
                fixture.weapons.FireRequested=true;fixture.weapons.Prepare();fixture.weapons.Execute(.02f,1);
                Assert.That(fixture.weapons.IsSustaining(WeaponFamily.Gatling),Is.True);
                var sources=runtime.GetComponentsInChildren<AudioSource>();
                var intro=sources.Single(v=>v.name=="Gatling 0 start-end");
                var loop=sources.Single(v=>v.name=="Gatling 0 sustain");
                Assert.That(intro.clip,Is.SameAs(cue.startClip));Assert.That(loop.clip,Is.SameAs(cue.clip));
                int shots=fixture.weapons.ShotsFired;
                yield return new WaitForSecondsRealtime(cue.startClip.length+.06f);fixture.StepAudio();
                Assert.That(loop.isPlaying,Is.True);Assert.That(loop.loop,Is.True);
                int played=runtime.PlayedCueCount;
                double until=Time.unscaledTimeAsDouble+cue.clip.length*2+.1;
                while(Time.unscaledTimeAsDouble<until)
                {
                    fixture.StepAudio();
                    foreach(var sound in new[]{GameSound.RailLarge,GameSound.HealthShieldWarning,GameSound.OverheatWarning,GameSound.Select})runtime.Emit(sound);
                    Assert.That(loop.isPlaying,Is.True,"Sustain cannot be stolen or time out between actual shots.");
                    Assert.That(runtime.ActiveEffectVoices,Is.LessThanOrEqualTo(runtime.Tuning.effectVoices));
                    yield return null;
                }
                Assert.That(fixture.weapons.ShotsFired,Is.EqualTo(shots),"No fabricated shot heartbeat is necessary.");
                fixture.weapons.FireRequested=false;fixture.weapons.Prepare();fixture.weapons.Execute(.02f,1);fixture.StepAudio();
                Assert.That(intro.clip,Is.SameAs(cue.endClip));Assert.That(intro.isPlaying,Is.True);
                until=Time.unscaledTimeAsDouble+.06;
                do{yield return null;fixture.StepAudio();}while(Time.unscaledTimeAsDouble<until);
                Assert.That(loop.isPlaying,Is.False);
                yield return new WaitForSecondsRealtime(cue.endClip.length+.03f);fixture.StepAudio();
                Assert.That(intro.isPlaying,Is.False);
                fixture.weapons.FireRequested=true;fixture.weapons.Prepare();fixture.weapons.Execute(.02f,1);fixture.StepAudio();
                Assert.That(intro.clip,Is.SameAs(cue.startClip),"A new actual firing edge restarts only the intro.");
                fixture.weapons.Prepare();fixture.weapons.Execute(.02f,0);fixture.StepAudio();
                Assert.That(intro.clip,Is.SameAs(cue.endClip),"Power loss ends audio independently of the 8/6 thermal cycle.");
            }
        }

        [UnityTest]public IEnumerator FlareVariantsCycleOneUnitPerAcceptedCue()
        {
            var cue=runtime.Tuning.Get(GameSound.FlareDeploy);Assert.That(cue.variants.Length,Is.EqualTo(6));
            var clips=new List<AudioClip>();
            for(int i=0;i<7;i++)
            {
                foreach(var source in runtime.GetComponentsInChildren<AudioSource>())if(cue.variants.Contains(source.clip))source.Stop();
                Assert.That(runtime.Emit(GameSound.FlareDeploy),Is.True);
                clips.Add(runtime.GetComponentsInChildren<AudioSource>().Single(v=>v.isPlaying&&cue.variants.Contains(v.clip)).clip);
                yield return new WaitForSecondsRealtime(cue.minimumInterval+.02f);
            }
            Assert.That(clips.Take(6).Distinct().Count(),Is.EqualTo(6));Assert.That(clips[6],Is.SameAs(clips[0]));
        }

        [Test]public void RealShieldStateAndLowHullShareOneCriticalWarningCooldown()
        {
            using(var fixture=new WarningFixture(runtime))
            {
                int before=runtime.PlayedCueCount;
                fixture.StepAudio();Assert.That(runtime.PlayedCueCount,Is.EqualTo(before));
                fixture.ship.Shields.Fields[0].charge=24;
                fixture.StepAudio();Assert.That(runtime.LastCue,Is.EqualTo(GameSound.HealthShieldWarning));
                Assert.That(runtime.PlayedCueCount,Is.EqualTo(before+1));
                fixture.ship.Performance.currentHp=fixture.ship.Performance.totalHp*.1f;
                fixture.StepAudio();Assert.That(runtime.PlayedCueCount,Is.EqualTo(before+1),"Concurrent low hull and shield share the existing cooldown.");
                fixture.ship.Performance.currentHp=fixture.ship.Performance.totalHp;
                fixture.ship.Shields.Fields[0].definition.shieldCapacity=0;
                fixture.AllowWarningAgain();fixture.StepAudio();
                Assert.That(runtime.PlayedCueCount,Is.EqualTo(before+1),"Zero maximum shield capacity is not low shield.");
            }
        }

        [UnityTest]public IEnumerator RealGatlingHeatWarnsOnceAndAudioRestartsAfterCoolingWithInputHeld()
        {
            using(var fixture=new WarningFixture(runtime))
            {
                var cue=runtime.Tuning.Get(GameSound.Gatling);
                var sources=runtime.GetComponentsInChildren<AudioSource>();
                var phase=sources.Single(v=>v.name=="Gatling 0 start-end");
                var loop=sources.Single(v=>v.name=="Gatling 0 sustain");
                fixture.weapons.FireRequested=true;fixture.weapons.Prepare();fixture.weapons.Execute(.02f,1);fixture.StepAudio();
                yield return new WaitForSecondsRealtime(cue.startClip.length+.04f);fixture.StepAudio();
                Assert.That(loop.isPlaying,Is.True);
                fixture.Heat();Assert.That(fixture.weapons.GatlingHeat.AnyOverheated,Is.True);
                int before=runtime.PlayedCueCount;
                fixture.StepAudio();Assert.That(runtime.LastCue,Is.EqualTo(GameSound.OverheatWarning));
                Assert.That(runtime.PlayedCueCount,Is.EqualTo(before+1));Assert.That(phase.clip,Is.SameAs(cue.endClip));
                double until=Time.unscaledTimeAsDouble+.05;
                do{yield return null;fixture.StepAudio();}while(Time.unscaledTimeAsDouble<until);
                Assert.That(loop.isPlaying,Is.False,"Overheat ends sustain even with the trigger held.");
                Assert.That(runtime.PlayedCueCount,Is.EqualTo(before+1));
                // Heat() intentionally steps past the threshold; that same step already cools its remainder.
                float coolingLeft=fixture.weapons.GatlingHeat.HeatFraction*6f;
                fixture.weapons.Prepare();fixture.weapons.Execute(coolingLeft-.1f,1);fixture.StepAudio();
                Assert.That(fixture.weapons.GatlingHeat.AnyOverheated,Is.True);Assert.That(loop.isPlaying,Is.False);
                fixture.weapons.Prepare();fixture.weapons.Execute(.2f,1);
                Assert.That(fixture.weapons.GatlingHeat.AnyOverheated,Is.False);
                fixture.weapons.Prepare();fixture.weapons.Execute(.02f,1);fixture.StepAudio();
                Assert.That(phase.clip,Is.SameAs(cue.startClip),"Held input restarts the intro after actual firing becomes available.");
                Assert.That(phase.isPlaying,Is.True);
                fixture.Heat();fixture.AllowWarningAgain();before=runtime.PlayedCueCount;
                fixture.StepAudio();Assert.That(runtime.LastCue,Is.EqualTo(GameSound.OverheatWarning));
                Assert.That(runtime.PlayedCueCount,Is.EqualTo(before+1));
            }
        }

        // Real ship, shield and thermal state; only the world binding skips map/bootstrap preparation.
        sealed class WarningFixture:System.IDisposable
        {
            const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
            readonly List<Object> owned=new List<Object>();readonly GameAudioRuntime audio;
            readonly ModuleDatabase previous=BlockModules.ConfiguredDatabase;
            readonly object previousFlightScene;readonly bool previousEnabled;
            public readonly FlightShipRuntime ship;public readonly ShipWeapons weapons;
            public WarningFixture(GameAudioRuntime audio)
            {
                this.audio=audio;previousFlightScene=Field("flightScene").GetValue(audio);previousEnabled=audio.enabled;audio.enabled=false;
                var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
                foreach(var shape in new[]{BlockShape.Core,BlockShape.Laser,BlockShape.Shield})
                {
                    var module=Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                    module.shape=shape;module.maxHp=100;module.massKg=10;module.occupiedCells=new[]{Vector3Int.zero};
                    if(shape==BlockShape.Shield)module.shieldCapacity=100;
                    if(shape==BlockShape.Laser){module.category=ModuleCategory.Weapon;module.muzzleLocalPosition=Vector3.forward*.6f;module.weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());module.weapon.family=WeaponFamily.Gatling;module.weapon.shotsPerSecond=1;module.weapon.firstShotImmediate=true;}
                    db.modules.Add(module);
                }
                var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
                var host=Own(new GameObject("Warning integration ship"));var body=host.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(db);
                var blueprint=new BlueprintData{name="Warning integration"};
                blueprint.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0),rotation=Quaternion.identity});
                blueprint.parts.Add(new BlockRecord{id="gun",shape=BlockShape.Laser,position=new Vector3(0,1.5f,0),rotation=Quaternion.identity});
                blueprint.parts.Add(new BlockRecord{id="shield",shape=BlockShape.Shield,position=new Vector3(1,.5f,0),rotation=Quaternion.identity});
                assembly.Import(blueprint);ship=host.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export(blueprint.name),tuning);Physics.SyncTransforms();
                var flight=host.AddComponent<ShipFlightController>();flight.enabled=false;flight.Initialize(body,ship,tuning,null,1);
                var world=Own(new GameObject("Warning integration world")).AddComponent<RangeCombatWorld>();world.enabled=false;
                var projectiles=new ProjectileWorld();projectiles.Initialize(null);weapons=new ShipWeapons();weapons.Initialize(ship,projectiles);
                var actor=new RangeCombatWorld.Actor{ship=ship,body=body,flight=flight,weapons=weapons};
                typeof(RangeCombatWorld).GetField("player",Private).SetValue(world,actor);
                typeof(RangeCombatWorld).GetField("projectiles",Private).SetValue(world,projectiles);
                ((List<RangeCombatWorld.Actor>)typeof(RangeCombatWorld).GetField("actors",Private).GetValue(world)).Add(actor);
                Field("flightScene").SetValue(audio,false);
                typeof(GameAudioRuntime).GetMethod("Bind",Private).Invoke(audio,new object[]{world});AllowWarningAgain();
            }
            public void Heat(){weapons.FireRequested=true;weapons.Prepare();weapons.Execute(8.1f,1);}
            public void StepAudio()=>typeof(GameAudioRuntime).GetMethod("Update",Private).Invoke(audio,null);
            public void AllowWarningAgain(){Field("nextCritical").SetValue(audio,0d);((Dictionary<int,double>)Field("ready").GetValue(audio)).Clear();}
            static FieldInfo Field(string name)=>typeof(GameAudioRuntime).GetField(name,Private);
            T Own<T>(T value)where T:Object{owned.Add(value);return value;}
            public void Dispose()
            {
                typeof(GameAudioRuntime).GetMethod("Detach",Private).Invoke(audio,null);
                Field("flightScene").SetValue(audio,previousFlightScene);audio.enabled=previousEnabled;
                for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);BlockModules.Configure(previous);
            }
        }

        [Test]public void OnlyExplicitlyDeletedLegacyCuesAreRemoved()
        {
            foreach(var sound in new[]{GameSound.LaserImpact})
                Assert.That(runtime.Emit(sound),Is.False,sound.ToString());
            foreach(var sound in new[]{GameSound.SaveBlueprint,GameSound.ConfirmWarning,GameSound.PlaceHull,GameSound.PlaceModule,
                GameSound.RemovePart,GameSound.BoundaryWarning,GameSound.CoreDestroyed,GameSound.Respawn,
                GameSound.RotatePart,GameSound.InvalidPlacement,GameSound.CorePowerOn})
                Assert.That(runtime.Tuning.Get(sound).clip,Is.Not.Null,sound.ToString());
            Assert.That(runtime.Tuning.Get(GameSound.Select).gain,Is.EqualTo(.42f*.7f).Within(.00001f));
        }

        [UnityTest]public IEnumerator MissilePulseFitsOriginalFastCadenceWithoutOverlapping()
        {
            var tuning=runtime.Tuning;var cue=tuning.Get(GameSound.MissileIncoming);
            Assert.That(tuning.missileWarningNearInterval,Is.EqualTo(.12f).Within(.00001f));
            Assert.That(tuning.missileWarningFarInterval,Is.EqualTo(.9f).Within(.00001f));
            Assert.That(cue.clip.length,Is.LessThan(tuning.missileWarningNearInterval));
            Assert.That(cue.loopWhileFiring,Is.False);
            runtime.StopMissileWarning();Assert.That(runtime.Emit(GameSound.MissileIncoming),Is.True);
            Assert.That(runtime.Emit(GameSound.MissileIncoming),Is.False);
            Assert.That(runtime.GetComponentsInChildren<AudioSource>().Count(v=>v.clip==cue.clip&&v.isPlaying),Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(tuning.missileWarningNearInterval+.02f);
            Assert.That(runtime.Emit(GameSound.MissileIncoming),Is.True,"A short pulse permits the original imminent-threat cadence.");
            Assert.That(runtime.GetComponentsInChildren<AudioSource>().Count(v=>v.clip==cue.clip&&v.isPlaying),Is.EqualTo(1));
        }

        [UnityTest]public IEnumerator PreviewRoutesVolumesAndDoesNotRewritePreferences()
        {
            string before=JsonUtility.ToJson(GameSettings.Current);
            var draft=saved.Clone();draft.masterVolume=.65f;draft.musicVolume=0;draft.sfxVolume=0;draft.uiVolume=.37f;draft.muteWhenUnfocused=false;
            GameAudio.Preview(draft);GameAudio.PlayUiClick();GameAudio.PreviewSfx();yield return null;
            var select=runtime.Tuning.Get(GameSound.Select);var shot=runtime.Tuning.Get(GameSound.LaserRapid);
            var sources=runtime.GetComponentsInChildren<AudioSource>();
            Assert.That(sources.Any(s=>s.clip==select.clip&&Mathf.Abs(s.volume-select.gain*.37f)<.001f),Is.True);
            Assert.That(sources.Any(s=>s.clip==shot.clip&&s.volume==0),Is.True);
            Assert.That(AudioListener.volume,Is.EqualTo(.65f).Within(.001f));
            Assert.That(JsonUtility.ToJson(GameSettings.Current),Is.EqualTo(before));
        }
    }
}
