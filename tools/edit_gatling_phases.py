from pathlib import Path
p=Path('Assets/_Starframe/Scripts/Runtime/Construction/App/GameAudio.cs')
s=p.read_text(encoding='utf-8-sig')
s=s.replace('// @role Route weapon-specific sounds, health/shield and overheat warnings, music and engine loops; reserve one incoming-missile voice.','// @role Route weapon sounds and warnings; reserve DSP-scheduled Gatling start/sustain/end channels and one missile-warning voice.')
s=s.replace('        readonly List<Voice> voices=new List<Voice>();','''        // Two sources per channel keep the intro-to-loop handoff on the audio clock, not the render clock.
        sealed class FiringVoice
        {
            public AudioSource transition,sustain;public GameSoundCue cue;
            public bool firing,ending;public double loopAt,lastTrigger;public float gain,release=1;
        }
        readonly FiringVoice[] gatling=new FiringVoice[2];
        readonly List<Voice> voices=new List<Voice>();''')
s=s.replace('foreach(var voice in voices)if(voice.source.isPlaying)n++;return n;','foreach(var voice in voices)if(voice.source.isPlaying)n++;foreach(var voice in gatling)if(voice!=null){if(voice.transition.isPlaying)n++;if(voice.sustain.isPlaying)n++;}return n;')
s=s.replace('for(int i=0;i<Mathf.Clamp(tuning.effectVoices,4,32)-1;i++)voices.Add(new Voice{source=Source("Effect "+i,false)});','''for(int i=0;i<gatling.Length;i++)gatling[i]=new FiringVoice{
                    transition=Source("Gatling "+i+" start-end",false),sustain=Source("Gatling "+i+" sustain",true)};
                for(int i=0;i<Mathf.Clamp(tuning.effectVoices,8,32)-5;i++)voices.Add(new Voice{source=Source("Effect "+i,false)});''')
s=s.replace('SceneManager.sceneLoaded+=SceneLoaded;Application.focusChanged+=FocusChanged;','SceneManager.sceneLoaded+=SceneLoaded;Application.focusChanged+=FocusChanged;ShipWeapons.ShotWindupStarted+=OnWindup;')
s=s.replace('void FocusChanged(bool focused){if(!focused)StopMissileWarning();ApplyVolumes(settings);}','void FocusChanged(bool focused){if(!focused){StopMissileWarning();StopGatling();}ApplyVolumes(settings);}')
s=s.replace('            if(missileWarning!=null&&missileWarning.source.isPlaying)\n','''            foreach(var voice in gatling)if(voice!=null&&voice.cue!=null)
            {
                float level=voice.gain*Bus(voice.cue.bus)*(voice.cue.bus==GameSoundBus.Effects?duck:1);
                voice.transition.volume=level;voice.sustain.volume=level*voice.release;
            }
            if(missileWarning!=null&&missileWarning.source.isPlaying)
''')
s=s.replace('            double now=Time.unscaledTimeAsDouble;int key=(int)sound*4+channel;','''            if(sound==GameSound.Gatling)return BeginGatling(channel==0?0:1,cue,position,spatial,gain);
            double now=Time.unscaledTimeAsDouble;int key=(int)sound*4+channel;''')
a=s.index('                if(voice.loopKey/4==(int)GameSound.Gatling&&world&&world.Player!=null)')
b=s.index('                bool released=',a)
s=s[:a]+s[b:]
s=s.replace('        void Update()\n        {','        void Update()\n        {\n            UpdateGatling();')
s=s.replace('        void OnShot(FlightShipRuntime owner,Vector3 origin,WeaponDefinition weapon)\n        {','''        void OnWindup(FlightShipRuntime owner,WeaponDefinition weapon,Vector3 origin)
        {
            if(!world||world.Player==null||!weapon||weapon.family!=WeaponFamily.Railgun)return;
            bool own=owner==world.Player.ship;
            Emit(ShotSound(owner,weapon),origin,!own,own?1:.7f,own?0:1);
        }
        void OnShot(FlightShipRuntime owner,Vector3 origin,WeaponDefinition weapon)
        {
            if(weapon&&weapon.family==WeaponFamily.Railgun&&weapon.fireWindupSeconds>0)return;''')
s=s.replace('        void Detach()\n        {','        void Detach()\n        {\n            StopGatling();')
s=s.replace('void OnDestroy(){Detach();SceneManager.sceneLoaded-=SceneLoaded;Application.focusChanged-=FocusChanged;}','void OnDestroy(){Detach();SceneManager.sceneLoaded-=SceneLoaded;Application.focusChanged-=FocusChanged;ShipWeapons.ShotWindupStarted-=OnWindup;}')
anchor='        public void StopMissileWarning()'
methods='''        bool BeginGatling(int channel,GameSoundCue cue,Vector3 position,bool spatial,float gain)
        {
            var voice=gatling[channel];if(voice==null||!cue.clip)return false;
            voice.lastTrigger=Time.unscaledTimeAsDouble;voice.gain=cue.gain*Mathf.Clamp01(gain);
            voice.transition.transform.position=voice.sustain.transform.position=spatial?position:transform.position;
            if(voice.firing)return true;
            voice.transition.Stop();voice.sustain.Stop();voice.cue=cue;voice.firing=true;voice.ending=false;voice.release=1;
            float pitch=Mathf.Clamp(cue.pitch,.25f,3); // One pitch keeps scheduled phase boundaries sample-aligned.
            foreach(var source in new[]{voice.transition,voice.sustain})
            {source.pitch=pitch;source.priority=32;source.spatialBlend=spatial?.85f:0;}
            voice.sustain.clip=cue.clip;voice.sustain.loop=true;
            double start=AudioSettings.dspTime+.01;
            voice.loopAt=start+(cue.startClip?(double)cue.startClip.samples/cue.startClip.frequency/pitch:0);
            if(cue.startClip){voice.transition.clip=cue.startClip;voice.transition.loop=false;voice.transition.PlayScheduled(start);}
            voice.sustain.PlayScheduled(voice.loopAt);
            RefreshLevels();PlayedCueCount++;LastCue=GameSound.Gatling;return true;
        }
        void EndGatling(FiringVoice voice)
        {
            if(!voice.firing)return;
            voice.firing=false;voice.ending=true;voice.transition.Stop();
            // Cancel a future loop immediately; otherwise crossfade it into the recorded release.
            if(AudioSettings.dspTime<voice.loopAt){voice.sustain.Stop();voice.release=0;}
            if(voice.cue.endClip){voice.transition.clip=voice.cue.endClip;voice.transition.loop=false;voice.transition.Play();}
        }
        void StopGatling()
        {
            foreach(var voice in gatling)if(voice!=null)
            {voice.transition.Stop();voice.sustain.Stop();voice.firing=voice.ending=false;voice.release=0;}
        }
        void UpdateGatling()
        {
            if(!tuning)return;
            var cue=tuning.Get(GameSound.Gatling);
            for(int channel=0;channel<gatling.Length;channel++)
            {
                var voice=gatling[channel];if(voice==null)continue;
                bool firing=false;Vector3 position=transform.position;
                if(world&&world.Player!=null)
                {
                    if(!FlightMenu&&Time.timeScale>0)foreach(var actor in world.Actors)
                    {
                        bool own=actor==world.Player;
                        if((channel==0)!=own||actor.weapons==null||!actor.weapons.IsSustaining(WeaponFamily.Gatling))continue;
                        if(!own&&(!actor.body||!world.Player.body||Vector3.Distance(actor.body.position,world.Player.body.position)>tuning.farDistance))continue;
                        firing=true;if(actor.body)position=actor.body.position;break;
                    }
                }
                else firing=voice.firing&&Time.unscaledTimeAsDouble-voice.lastTrigger<Mathf.Max(.01f,voice.cue.releaseSeconds);
                if(firing&&cue!=null&&cue.clip&&cue.gain>0)BeginGatling(channel,cue,position,channel!=0,channel==0?1:.7f);
                else EndGatling(voice);
                if(voice.ending)
                {
                    voice.release=Mathf.MoveTowards(voice.release,0,Time.unscaledDeltaTime/.02f);
                    if(voice.release<=0){voice.sustain.Stop();if(!voice.transition.isPlaying)voice.ending=false;}
                }
            }
        }
'''
s=s.replace(anchor,methods+anchor)
p.write_text(s,encoding='utf-8')
p=Path('Assets/_Starframe/Scripts/Runtime/Construction/App/GameAudioTuning.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('// @role Define weapon and warning cues, optional alternating clips, engine response, music levels and playback limits.','// @role Define weapon and warning cues, Gatling start/end phases, alternating clips, engine response, music levels and playback limits.')
s=s.replace('        public AudioClip clip;','''        public AudioClip clip;
        [Tooltip("Gatling intro and release; clip is the seamless sustain loop. State, not clip length, controls firing.")]
        public AudioClip startClip,endClip;''')
s=s.replace('[Range(4,32)] public int effectVoices=16;','[Range(8,32)] public int effectVoices=16;')
p.write_text(s,encoding='utf-8')
