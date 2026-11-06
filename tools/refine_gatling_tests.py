from pathlib import Path
p=Path('Assets/_Starframe/Scripts/Runtime/Construction/App/GameAudio.cs');s=p.read_text()
s=s.replace('if(firing&&cue!=null&&cue.clip&&cue.gain>0)BeginGatling(channel,cue,position,channel!=0,channel==0?1:.7f);','if(firing&&cue!=null&&cue.clip&&cue.gain>0){if(world||!voice.firing)BeginGatling(channel,cue,position,channel!=0,channel==0?1:.7f);}')
p.write_text(s)
p=Path('Assets/_Starframe/Tests/PlayMode/App/GameAudioTests.cs');s=p.read_text()
s=s.replace('source.name.StartsWith("Effect ")||source.name=="Incoming missile warning"','source.name.StartsWith("Effect ")||source.name.StartsWith("Gatling ")||source.name=="Incoming missile warning"')
a=s.index('        [UnityTest]public IEnumerator GatlingHeartbeatBridgesSlowShotGapsButReleaseStopsItsVoice()');b=s.index('        [UnityTest]public IEnumerator FlareVariantsCycleOneUnitPerAcceptedCue()',a)
s=s[:a]+'''        [UnityTest]public IEnumerator GatlingStartSustainEndFollowActualFireAndSurviveVoicePressure()
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
                Assert.That(intro.clip,Is.SameAs(cue.endClip),"Power loss ends audio independently of the 12/6 thermal cycle.");
            }
        }

'''+s[b:]
p.write_text(s)
