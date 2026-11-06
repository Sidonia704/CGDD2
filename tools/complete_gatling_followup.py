from pathlib import Path
p=Path('Assets/_Starframe/Scripts/Runtime/Construction/App/GameAudio.cs');s=p.read_text()
s=s.replace('public int loopKey=-1;public double lastTrigger;','public int loopKey=-1,cueKey=-1;public double lastTrigger;')
s=s.replace('ShipWeapons.ShotWindupStarted+=OnWindup;','ShipWeapons.ShotWindupStarted+=OnWindup;ShipWeapons.ShotWindupCancelled+=OnWindupCancelled;')
s=s.replace('selected.source.Stop();\n            variantNext','selected.source.Stop();selected.cueKey=key;\n            variantNext')
anchor='        void OnShot(FlightShipRuntime owner,Vector3 origin,WeaponDefinition weapon)'
method='''        void OnWindupCancelled(FlightShipRuntime owner,WeaponDefinition weapon)
        {
            if(!world||world.Player==null||!weapon||weapon.family!=WeaponFamily.Railgun)return;
            bool own=owner==world.Player.ship;var sound=ShotSound(owner,weapon);bool large=sound==GameSound.RailLarge;
            foreach(var actor in world.Actors)
                if((actor==world.Player)==own&&actor.weapons!=null&&actor.weapons.HasPendingRailWindup(large))return;
            int key=(int)sound*4+(own?0:1);
            foreach(var voice in voices)
                if(voice.cueKey==key&&voice.source.isPlaying&&voice.source.time<weapon.fireWindupSeconds)
                    voice.source.Stop(); // Before the bang only: never cut the tail of a shot which already fired.
            ready.Remove(key);
        }
'''
s=s.replace(anchor,method+anchor)
s=s.replace('ShipWeapons.ShotWindupStarted-=OnWindup;}','ShipWeapons.ShotWindupStarted-=OnWindup;ShipWeapons.ShotWindupCancelled-=OnWindupCancelled;}')
p.write_text(s)
p=Path('Assets/_Starframe/Tests/PlayMode/App/GameAudioTests.cs');s=p.read_text()
s=s.replace('Assert.That(runtime,Is.Not.Null);Assert.That(runtime.Tuning,Is.Not.Null);','Assert.That(runtime,Is.Not.Null);Assert.That(runtime.Tuning,Is.Not.Null);\n            StopGatlingState();')
s=s.replace('[TearDown]public void TearDown(){GameAudio.Preview(saved);','void StopGatlingState()=>typeof(GameAudioRuntime).GetMethod("StopGatling",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(runtime,null);\n        [TearDown]public void TearDown(){StopGatlingState();GameAudio.Preview(saved);')
a=s.index('        [Test]public void RealGatlingHeatWarnsOncePerOverheatAndRearmsAfterCooling()');b=s.index('        // Real ship, shield and thermal state;',a)
s=s[:a]+'''        [UnityTest]public IEnumerator RealGatlingHeatWarnsOnceAndAudioRestartsAfterCoolingWithInputHeld()
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
                fixture.weapons.Prepare();fixture.weapons.Execute(5.9f,1);fixture.StepAudio();
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

'''+s[b:]
p.write_text(s)
