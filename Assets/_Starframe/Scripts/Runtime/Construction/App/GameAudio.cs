// @role Route weapon sounds and warnings; reserve DSP-scheduled Gatling start/sustain/end channels and one missile-warning voice.
// @calls GameSettings, GameAudioTuning, ThreatAudioDriver, FlightRangeBootstrap, ProjectileWorld, ShipWeapons, ShipShieldSystem

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.App
{
    public static class GameAudio
    {
        static GameAudioRuntime runtime;
        static GameAudioRuntime Ensure()
        {
            if(runtime)return runtime;
            var go=new GameObject("Game Audio");Object.DontDestroyOnLoad(go);
            runtime=go.AddComponent<GameAudioRuntime>();return runtime;
        }
        public static void Apply()=>Ensure().ApplyVolumes(GameSettings.Current);
        public static GameAudioTuning Tuning=>Ensure().Tuning;
        public static void Preview(GameSettingsData settings)=>Ensure().ApplyVolumes(settings);
        public static void Play(GameSound sound)=>Ensure().Emit(sound);
        public static bool TryPlay(GameSound sound)=>Ensure().Emit(sound);
        public static void StopMissileWarning(){if(runtime)runtime.StopMissileWarning();}
        public static void PlayUiClick()=>Play(GameSound.Select);
        public static void Click()=>PlayUiClick();
        public static void PreviewSfx()=>Play(GameSound.LaserRapid);
    }

    /// <summary>Persistent audio buses. Sound balancing lives in GameAudioTuning.</summary>
    public sealed class GameAudioRuntime:MonoBehaviour
    {
        sealed class Voice
        {
            public AudioSource source;public GameSoundBus bus;public float gain;public double started;
            public int loopKey=-1,cueKey=-1;public double lastTrigger;public float releaseSeconds,envelope=1;
        }
        // Two sources per channel keep the intro-to-loop handoff on the audio clock, not the render clock.
        sealed class FiringVoice
        {
            public AudioSource transition,sustain;public GameSoundCue cue;
            public bool firing,ending;public double loopAt,lastTrigger;public float gain,release=1;
        }
        readonly FiringVoice[] gatling=new FiringVoice[2];
        readonly List<Voice> voices=new List<Voice>();
        readonly Dictionary<int,double> ready=new Dictionary<int,double>();
        readonly Dictionary<int,int> variantNext=new Dictionary<int,int>();
        readonly Dictionary<FlightShipRuntime,bool> alive=new Dictionary<FlightShipRuntime,bool>();
        readonly List<FlightShipRuntime> removedActors=new List<FlightShipRuntime>();
        readonly List<ShipPartState> thrusters=new List<ShipPartState>();
        GameAudioTuning tuning;AudioSource musicA,musicB,engine;
        Voice missileWarning;
        ThreatAudioDriver threatAudio;
        RangeCombatWorld world;ProjectileWorld projectiles;RangeHUD hud;
        GameSettingsData settings;
        AudioClip musicTarget;float blendA,blendB,targetA,targetB,engineLevel,engineBoost=1;
        double nextWorldCheck,nextBoundary,nextCritical;bool flightScene,boosting,overheated;
        FlightShipRuntime locked;int lockLife,playerLife=-1,thrustRevision=-1;

        public GameAudioTuning Tuning=>tuning;
        public int ActiveEffectVoices {get{int n=missileWarning!=null&&missileWarning.source.isPlaying?1:0;foreach(var voice in voices)if(voice.source.isPlaying)n++;foreach(var voice in gatling)if(voice!=null){if(voice.transition.isPlaying)n++;if(voice.sustain.isPlaying)n++;}return n;}}
        public float EngineLevel=>engineLevel;
        public int PlayedCueCount {get;private set;}
        public GameSound LastCue {get;private set;}

        void Awake()
        {
            tuning=Resources.Load<GameAudioTuning>("Audio/GameAudioTuning");
            settings=GameSettings.Current.Clone();
            musicA=Source("Music A",true);musicB=Source("Music B",true);engine=Source("Own ship propulsion",true);
            threatAudio=gameObject.AddComponent<ThreatAudioDriver>();
            musicA.priority=musicB.priority=220;engine.priority=160;
            if(tuning)
            {
                engine.clip=tuning.thrusterLoop;
                // Reserve one existing voice: gunfire cannot swallow the alarm, and missiles never stack copies.
                missileWarning=new Voice{source=Source("Incoming missile warning",false)};
                for(int i=0;i<gatling.Length;i++)gatling[i]=new FiringVoice{
                    transition=Source("Gatling "+i+" start-end",false),sustain=Source("Gatling "+i+" sustain",true)};
                for(int i=0;i<Mathf.Clamp(tuning.effectVoices,8,32)-5;i++)voices.Add(new Voice{source=Source("Effect "+i,false)});
            }
            SceneManager.sceneLoaded+=SceneLoaded;Application.focusChanged+=FocusChanged;ShipWeapons.ShotWindupStarted+=OnWindup;ShipWeapons.ShotWindupCancelled+=OnWindupCancelled;
            SceneLoaded(SceneManager.GetActiveScene(),LoadSceneMode.Single);ApplyVolumes(settings);
        }
        AudioSource Source(string label,bool loop)
        {
            var go=new GameObject(label);go.transform.SetParent(transform,false);
            var s=go.AddComponent<AudioSource>();s.playOnAwake=false;s.loop=loop;s.volume=0;
            s.dopplerLevel=0;s.reverbZoneMix=0;s.rolloffMode=AudioRolloffMode.Linear;
            if(tuning){s.minDistance=tuning.nearDistance;s.maxDistance=Mathf.Max(s.minDistance+1,tuning.farDistance);}
            return s;
        }
        public void ApplyVolumes(GameSettingsData value)
        {
            settings=value.Clone();
            AudioListener.volume=settings.muteWhenUnfocused&&!Application.isFocused?0:settings.masterVolume;
            RefreshLevels();
        }
        void FocusChanged(bool focused){if(!focused){StopMissileWarning();StopGatling();}ApplyVolumes(settings);}
        float Bus(GameSoundBus bus)=>bus==GameSoundBus.Interface?settings.uiVolume:settings.sfxVolume;
        bool FlightMenu=>flightScene&&((hud&&hud.ExitDialogOpen)||GameInput.Suspended);
        void RefreshLevels()
        {
            if(!tuning)return;
            musicA.volume=blendA*tuning.musicGain*settings.musicVolume;
            musicB.volume=blendB*tuning.musicGain*settings.musicVolume;
            float duck=FlightMenu?tuning.menuEffectsMultiplier:1;
            engine.volume=Mathf.Clamp01(engineLevel*tuning.thrusterGain*engineBoost*settings.sfxVolume*duck);
            foreach(var voice in voices)if(voice.source.isPlaying)
                voice.source.volume=voice.gain*voice.envelope*Bus(voice.bus)*(voice.bus==GameSoundBus.Effects?duck:1);
            foreach(var voice in gatling)if(voice!=null&&voice.cue!=null)
            {
                float level=voice.gain*Bus(voice.cue.bus)*(voice.cue.bus==GameSoundBus.Effects?duck:1);
                voice.transition.volume=level;voice.sustain.volume=level*voice.release;
            }
            if(missileWarning!=null&&missileWarning.source.isPlaying)
                missileWarning.source.volume=missileWarning.gain*Bus(missileWarning.bus)*(missileWarning.bus==GameSoundBus.Effects?duck:1);
        }
        public bool Emit(GameSound sound,Vector3 position=default,bool spatial=false,float gain=1,int channel=0,float pitchScale=1)
        {
            if(!tuning)return false;
            var cue=tuning.Get(sound);if(cue==null||!cue.clip||cue.gain<=0||gain<=0)return false;
            if(spatial&&world&&world.Player!=null&&world.Player.body&&Vector3.Distance(position,world.Player.body.position)>tuning.farDistance)return false;
            if(sound==GameSound.Gatling)return BeginGatling(channel==0?0:1,cue,position,spatial,gain);
            double now=Time.unscaledTimeAsDouble;int key=(int)sound*4+channel;
            if(cue.loopWhileFiring)foreach(var voice in voices)
                if(voice.loopKey==key&&voice.source.isPlaying)
                {
                    voice.lastTrigger=now;voice.releaseSeconds=Mathf.Max(.01f,cue.releaseSeconds);
                    voice.source.transform.position=spatial?position:transform.position;
                    voice.gain=cue.gain*Mathf.Clamp01(gain);return true;
                }
            if(ready.TryGetValue(key,out double time)&&now<time)return false;
            Voice selected=sound==GameSound.MissileIncoming?missileWarning:null;
            if(sound==GameSound.MissileIncoming&&selected!=null&&selected.source.isPlaying)return false;
            if(sound!=GameSound.MissileIncoming)foreach(var voice in voices)
            {
                if(!voice.source.isPlaying){selected=voice;break;}
                if(voice.source.priority<cue.priority)continue;
                // Other guns and routine UI must not interrupt an active own-ship loop; critical warnings may still preempt it.
                if(voice.loopKey>=0&&voice.loopKey%4==0&&cue.priority>=48)continue;
                if(selected==null||voice.source.priority>selected.source.priority||
                    (voice.source.priority==selected.source.priority&&voice.started<selected.started))selected=voice;
            }
            if(selected==null)return false;
            ready[key]=now+Mathf.Max(0,cue.minimumInterval);
            selected.source.Stop();selected.cueKey=key;
            variantNext.TryGetValue(key,out int variant);
            int variantCount=cue.variants!=null&&cue.variants.Length>0?cue.variants.Length:cue.alternateClip?2:1;
            variant%=variantCount;
            var clip=cue.variants!=null&&cue.variants.Length>0?cue.variants[variant]:variant==1?cue.alternateClip:cue.clip;
            selected.source.clip=clip?clip:cue.clip;
            variantNext[key]=(variant+1)%variantCount;
            selected.source.priority=cue.priority;
            selected.source.loop=cue.loopWhileFiring;selected.loopKey=cue.loopWhileFiring?key:-1;
            selected.envelope=cue.loopWhileFiring?0:1;
            selected.lastTrigger=now;selected.releaseSeconds=Mathf.Max(.01f,cue.releaseSeconds);
            selected.source.spatialBlend=spatial?.85f:0;selected.source.transform.position=spatial?position:transform.position;
            selected.source.pitch=Mathf.Clamp((cue.pitch+Random.Range(-cue.pitchVariation,cue.pitchVariation))*pitchScale,.25f,3);
            selected.bus=cue.bus;selected.gain=cue.gain*Mathf.Clamp01(gain);selected.started=now;
            selected.source.volume=selected.gain*selected.envelope*Bus(cue.bus)*(cue.bus==GameSoundBus.Effects&&FlightMenu?tuning.menuEffectsMultiplier:1);
            selected.source.Play();PlayedCueCount++;LastCue=sound;return true;
        }
        bool BeginGatling(int channel,GameSoundCue cue,Vector3 position,bool spatial,float gain)
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
                bool firing=false;float firingGain=0;Vector3 position=transform.position;
                if(world&&world.Player!=null)
                {
                    if(!FlightMenu&&Time.timeScale>0)foreach(var actor in world.Actors)
                    {
                        bool own=actor==world.Player;
                        if((channel==0)!=own||actor.weapons==null||!actor.weapons.IsSustaining(WeaponFamily.Gatling))continue;
                        if(!own&&(!actor.body||!world.Player.body||Vector3.Distance(actor.body.position,world.Player.body.position)>tuning.farDistance))continue;
                        float actorGain=actor.weapons.SustainingAudioGain(WeaponFamily.Gatling);
                        if(!firing||actorGain>firingGain){firingGain=actorGain;if(actor.body)position=actor.body.position;}
                        firing=true;
                    }
                }
                else firing=voice.firing&&Time.unscaledTimeAsDouble-voice.lastTrigger<Mathf.Max(.01f,voice.cue.releaseSeconds);
                if(firing&&cue!=null&&cue.clip&&cue.gain>0){if(world||!voice.firing)BeginGatling(channel,cue,position,channel!=0,(channel==0?1:.7f)*firingGain);}
                else EndGatling(voice);
                if(voice.ending)
                {
                    voice.release=Mathf.MoveTowards(voice.release,0,Time.unscaledDeltaTime/.02f);
                    if(voice.release<=0){voice.sustain.Stop();if(!voice.transition.isPlaying)voice.ending=false;}
                }
            }
        }
        public void StopMissileWarning()
        {
            if(missileWarning!=null)missileWarning.source.Stop();
            ready.Remove((int)GameSound.MissileIncoming*4);
        }
        void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            if(mode==LoadSceneMode.Additive)return;
            Detach();foreach(var voice in voices)voice.source.Stop();ready.Clear();variantNext.Clear();
            engine.Stop();engineLevel=0;engineBoost=1;boosting=false;flightScene=IsFlightScene(scene);
            SelectMusic(tuning?(flightScene?tuning.flightMusic:tuning.workbenchMusic):null);nextWorldCheck=0;
        }
        static bool IsFlightScene(Scene scene)
        {
            if(scene.name==FlightSession.RangeScene)return true;
            if(!scene.IsValid()||!scene.isLoaded)return false;
            // Missions share the flight bootstrap but can have their own scene names and delayed map preparation.
            foreach(var root in scene.GetRootGameObjects())
                if(root.GetComponentInChildren<FlightRangeBootstrap>(true))return true;
            return false;
        }
        void SelectMusic(AudioClip clip)
        {
            if(clip==musicTarget)return;musicTarget=clip;
            if(!clip){targetA=targetB=0;return;}
            if(musicA.isPlaying&&musicA.clip==clip){targetA=1;targetB=0;return;}
            if(musicB.isPlaying&&musicB.clip==clip){targetA=0;targetB=1;return;}
            if(blendA<=blendB){musicA.clip=clip;musicA.Play();targetA=1;targetB=0;}
            else{musicB.clip=clip;musicB.Play();targetA=0;targetB=1;}
        }
        void Update()
        {
            UpdateGatling();
            foreach(var voice in voices)if(voice.loopKey>=0)
            {
                bool released=Time.unscaledTimeAsDouble-voice.lastTrigger>=voice.releaseSeconds;
                voice.envelope=Mathf.MoveTowards(voice.envelope,released?0:1,Time.unscaledDeltaTime/(released?.025f:.01f));
                if(released&&voice.envelope<=0){voice.source.Stop();voice.source.loop=false;voice.loopKey=-1;}
            }
            if(!tuning)return;
            float dt=Mathf.Min(.1f,Time.unscaledDeltaTime),musicStep=dt/Mathf.Max(.1f,tuning.musicCrossfadeSeconds);
            blendA=Mathf.MoveTowards(blendA,targetA,musicStep);blendB=Mathf.MoveTowards(blendB,targetB,musicStep);
            if(blendA<=0&&targetA==0)musicA.Stop();if(blendB<=0&&targetB==0)musicB.Stop();
            if(flightScene&&!world&&Time.unscaledTimeAsDouble>=nextWorldCheck)
            {
                nextWorldCheck=Time.unscaledTimeAsDouble+.25;
                var candidate=FindFirstObjectByType<RangeCombatWorld>();
                if(candidate&&candidate.Projectiles!=null&&candidate.Player!=null)Bind(candidate);
            }
            float throttle=0;
            threatAudio?.Step();
            if(world&&world.Player!=null)
            {
                UpdateActors();var player=world.Player;var ship=player.ship;var flight=player.flight;
                if(ship.IsAlive)
                {
                    if(playerLife!=ship.LifeId){playerLife=ship.LifeId;thrustRevision=-1;overheated=false;nextCritical=0;}
                    if(thrustRevision!=ship.StructureRevision){thrusters.Clear();foreach(var part in ship.Parts)if(BlockModules.IsThruster(part.record.shape))thrusters.Add(part);thrustRevision=ship.StructureRevision;}
                    foreach(var part in thrusters)if(part.present&&part.thrustAvailable&&part.view)
                    {
                        var nozzles=BlockModules.LocalExhaustDirections(part.record.shape);
                        for(int nozzle=0;nozzle<nozzles.Length;nozzle++)
                            if(part.ExhaustAvailable(nozzle))throttle=Mathf.Max(throttle,flight.ThrusterThrottle(part,nozzle));
                    }
                    if(!FlightMenu)
                    {
                        if(flight.IsBoosting&&!boosting)Emit(GameSound.ManeuverJet);boosting=flight.IsBoosting;
                        var p=player.body.position;
                        float edge=flight.Tuning.arenaHalfExtent-flight.HullRadius-Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y),Mathf.Abs(p.z));
                        if(edge<tuning.boundaryWarningDistance&&Time.unscaledTimeAsDouble>=nextBoundary){Emit(GameSound.BoundaryWarning);nextBoundary=Time.unscaledTimeAsDouble+tuning.boundaryRepeatSeconds;}
                        var performance=ship.Performance;
                        bool lowHull=performance.currentHp/Mathf.Max(1,performance.totalHp)<tuning.criticalHullFraction;
                        float shieldCapacity=ship.Shields.Capacity;
                        bool lowShield=shieldCapacity>0&&ship.Shields.Charge/shieldCapacity<.25f;
                        if((lowHull||lowShield)&&Time.unscaledTimeAsDouble>=nextCritical)
                        {Emit(GameSound.HealthShieldWarning);nextCritical=Time.unscaledTimeAsDouble+tuning.criticalRepeatSeconds;}
                        bool hot=player.weapons!=null&&player.weapons.GatlingHeat.AnyOverheated;
                        if(hot&&!overheated)Emit(GameSound.OverheatWarning);
                        overheated=hot;
                    }
                }
                else {boosting=false;overheated=false;}
                var target=world.Targeting&&world.Targeting.HasTarget?world.Targeting.LockedTarget:null;
                if(target!=locked||(target&&target.LifeId!=lockLife))
                {
                    if(target)Emit(GameSound.LockAcquired);else if(locked&&ship.IsAlive)Emit(GameSound.TargetLost);
                    locked=target;lockLife=target?target.LifeId:0;
                }
                float pitch=Mathf.Lerp(tuning.idlePitch,flight.IsBoosting?tuning.boostPitch:tuning.fullThrustPitch,Mathf.Sqrt(engineLevel));
                engine.pitch=Mathf.Lerp(engine.pitch,pitch,1-Mathf.Exp(-dt/.15f));
            }
            if(FlightMenu)throttle=0;
            engineBoost=Mathf.Lerp(engineBoost,boosting?Mathf.Max(1,tuning.boostVolumeMultiplier):1,1-Mathf.Exp(-dt/.12f));
            engineLevel=Mathf.Lerp(engineLevel,throttle,1-Mathf.Exp(-dt/Mathf.Max(.02f,throttle>engineLevel?tuning.thrustAttack:tuning.thrustRelease)));
            if(engine.clip&&engineLevel>.002f){if(!engine.isPlaying)engine.Play();}
            else if(engine.isPlaying){engine.Stop();engineLevel=0;}
            RefreshLevels();
        }
        void Bind(RangeCombatWorld value)
        {
            Detach();world=value;hud=FindFirstObjectByType<RangeHUD>();projectiles=world.Projectiles;
            threatAudio.Bind(world);
            projectiles.WeaponShotFired+=OnShot;
            playerLife=world.Player.ship.LifeId;nextBoundary=nextCritical=0;
            foreach(var actor in world.Actors)alive[actor.ship]=actor.ship.IsAlive;
        }
        void UpdateActors()
        {
            removedActors.Clear();foreach(var ship in alive.Keys)if(!ship)removedActors.Add(ship);
            foreach(var ship in removedActors)alive.Remove(ship);
            foreach(var actor in world.Actors)
            {
                var ship=actor.ship;if(!ship)continue;
                if(actor==world.Player&&alive.TryGetValue(ship,out bool wasAlive)&&wasAlive&&!ship.IsAlive)
                    Emit(GameSound.CoreDestroyed);
                alive[ship]=ship.IsAlive;
            }
        }
        void OnWindup(FlightShipRuntime owner,WeaponDefinition weapon,Vector3 origin)
        {
            if(!world||world.Player==null||!weapon||weapon.family!=WeaponFamily.Railgun)return;
            bool own=owner==world.Player.ship;
            Emit(ShotSound(owner,weapon),origin,!own,(own?1:.7f)*ShotGain(weapon),own?0:1);
        }
        void OnWindupCancelled(FlightShipRuntime owner,WeaponDefinition weapon)
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
        void OnShot(FlightShipRuntime owner,Vector3 origin,WeaponDefinition weapon)
        {
            if(weapon&&weapon.family==WeaponFamily.Railgun&&weapon.fireWindupSeconds>0)return;
            if(!world||world.Player==null)return;
            bool own=owner==world.Player.ship;
            Emit(ShotSound(owner,weapon),origin,!own,(own?1:.7f)*ShotGain(weapon),own?0:1);
        }
        public static float ShotGain(WeaponDefinition weapon)=>weapon?Mathf.Clamp01(weapon.shotAudioGain):1;
        public static GameSound ShotSound(FlightShipRuntime owner,WeaponDefinition weapon)
        {
            if(!weapon)return GameSound.LaserShot;
            if(weapon.delivery==WeaponDelivery.GuidedMissile||weapon.family==WeaponFamily.GuidedMissile)return GameSound.MissileShot;
            if(weapon.delivery==WeaponDelivery.Beam||weapon.family==WeaponFamily.BeamLaser)return GameSound.DirectLaser;
            switch(weapon.family)
            {
                case WeaponFamily.Gatling:return GameSound.Gatling;
                case WeaponFamily.RapidLaser:return GameSound.LaserRapid;
                case WeaponFamily.Railgun:
                    var large=owner&&owner.Tuning?owner.Tuning.GetDefinition(BlockShape.Railgun3):null;
                    return large&&large.weapon==weapon?GameSound.RailLarge:GameSound.RailSmall;
                default:return GameSound.LaserShot;
            }
        }
        void Detach()
        {
            StopGatling();
            foreach(var voice in voices)if(voice.loopKey>=0)
            {voice.source.Stop();voice.source.loop=false;voice.loopKey=-1;}
            if(projectiles!=null)projectiles.WeaponShotFired-=OnShot;
            threatAudio?.Bind(null);
            projectiles=null;world=null;hud=null;locked=null;lockLife=0;playerLife=thrustRevision=-1;alive.Clear();thrusters.Clear();overheated=false;
        }
        void OnDestroy(){Detach();SceneManager.sceneLoaded-=SceneLoaded;Application.focusChanged-=FocusChanged;ShipWeapons.ShotWindupStarted-=OnWindup;ShipWeapons.ShotWindupCancelled-=OnWindupCancelled;}
    }
}
