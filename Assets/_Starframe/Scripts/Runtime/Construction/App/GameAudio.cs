// @role Route gameplay sounds, music and engine loops through shared audio settings.
// @calls GameSettings, GameAudioTuning

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
        public static void Preview(GameSettingsData settings)=>Ensure().ApplyVolumes(settings);
        public static void Play(GameSound sound)=>Ensure().Emit(sound);
        public static void PlayUiClick()=>Play(GameSound.Select);
        public static void Click()=>PlayUiClick();
        public static void PreviewSfx()=>Play(GameSound.LaserShot);
    }

    /// <summary>Persistent audio buses. Sound balancing lives in GameAudioTuning.</summary>
    public sealed class GameAudioRuntime:MonoBehaviour
    {
        sealed class Voice
        {
            public AudioSource source;public GameSoundBus bus;public float gain;public double started;
        }
        readonly List<Voice> voices=new List<Voice>();
        readonly Dictionary<int,double> ready=new Dictionary<int,double>();
        readonly Dictionary<FlightShipRuntime,bool> alive=new Dictionary<FlightShipRuntime,bool>();
        readonly List<FlightShipRuntime> removedActors=new List<FlightShipRuntime>();
        readonly List<ShipPartState> thrusters=new List<ShipPartState>();
        GameAudioTuning tuning;AudioSource musicA,musicB,engine;
        RangeCombatWorld world;ProjectileWorld projectiles;RangeHUD hud;
        GameSettingsData settings;
        AudioClip musicTarget;float blendA,blendB,targetA,targetB,engineLevel,engineBoost=1;
        double nextWorldCheck,nextBoundary,nextCritical;bool rangeScene,boosting;
        FlightShipRuntime locked;int lockLife,playerLife=-1,thrustRevision=-1;

        public GameAudioTuning Tuning=>tuning;
        public int ActiveEffectVoices {get{int n=0;foreach(var voice in voices)if(voice.source.isPlaying)n++;return n;}}
        public float EngineLevel=>engineLevel;
        public int PlayedCueCount {get;private set;}
        public GameSound LastCue {get;private set;}

        void Awake()
        {
            tuning=Resources.Load<GameAudioTuning>("Audio/GameAudioTuning");
            settings=GameSettings.Current.Clone();
            musicA=Source("Music A",true);musicB=Source("Music B",true);engine=Source("Own ship propulsion",true);
            musicA.priority=musicB.priority=220;engine.priority=160;
            if(tuning)
            {
                engine.clip=tuning.thrusterLoop;
                for(int i=0;i<Mathf.Clamp(tuning.effectVoices,4,32);i++)voices.Add(new Voice{source=Source("Effect "+i,false)});
            }
            SceneManager.sceneLoaded+=SceneLoaded;Application.focusChanged+=FocusChanged;
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
        void FocusChanged(bool focused)=>ApplyVolumes(settings);
        float Bus(GameSoundBus bus)=>bus==GameSoundBus.Interface?settings.uiVolume:settings.sfxVolume;
        bool FlightMenu=>rangeScene&&((hud&&hud.ExitDialogOpen)||GameInput.Suspended);
        void RefreshLevels()
        {
            if(!tuning)return;
            musicA.volume=blendA*tuning.musicGain*settings.musicVolume;
            musicB.volume=blendB*tuning.musicGain*settings.musicVolume;
            float duck=FlightMenu?tuning.menuEffectsMultiplier:1;
            engine.volume=Mathf.Clamp01(engineLevel*tuning.thrusterGain*engineBoost*settings.sfxVolume*duck);
            foreach(var voice in voices)if(voice.source.isPlaying)
                voice.source.volume=voice.gain*Bus(voice.bus)*(voice.bus==GameSoundBus.Effects?duck:1);
        }
        public bool Emit(GameSound sound,Vector3 position=default,bool spatial=false,float gain=1,int channel=0)
        {
            if(!tuning)return false;
            var cue=tuning.Get(sound);if(cue==null||!cue.clip||cue.gain<=0||gain<=0)return false;
            if(spatial&&world&&world.Player!=null&&world.Player.body&&Vector3.Distance(position,world.Player.body.position)>tuning.farDistance)return false;
            double now=Time.unscaledTimeAsDouble;int key=(int)sound*4+channel;
            if(ready.TryGetValue(key,out double time)&&now<time)return false;
            Voice selected=null;
            foreach(var voice in voices)
            {
                if(!voice.source.isPlaying){selected=voice;break;}
                if(voice.source.priority<cue.priority)continue;
                if(selected==null||voice.source.priority>selected.source.priority||
                    (voice.source.priority==selected.source.priority&&voice.started<selected.started))selected=voice;
            }
            if(selected==null)return false;
            ready[key]=now+Mathf.Max(0,cue.minimumInterval);
            selected.source.Stop();selected.source.clip=cue.clip;selected.source.priority=cue.priority;
            selected.source.spatialBlend=spatial?.85f:0;selected.source.transform.position=spatial?position:transform.position;
            selected.source.pitch=Mathf.Clamp(cue.pitch+Random.Range(-cue.pitchVariation,cue.pitchVariation),.25f,3);
            selected.bus=cue.bus;selected.gain=cue.gain*Mathf.Clamp01(gain);selected.started=now;
            selected.source.volume=selected.gain*Bus(cue.bus)*(cue.bus==GameSoundBus.Effects&&FlightMenu?tuning.menuEffectsMultiplier:1);
            selected.source.Play();PlayedCueCount++;LastCue=sound;return true;
        }
        void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            if(mode==LoadSceneMode.Additive)return;
            Detach();foreach(var voice in voices)voice.source.Stop();ready.Clear();
            engine.Stop();engineLevel=0;engineBoost=1;boosting=false;rangeScene=scene.name==FlightSession.RangeScene;
            SelectMusic(tuning?(rangeScene?tuning.flightMusic:tuning.workbenchMusic):null);nextWorldCheck=0;
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
            if(!tuning)return;
            float dt=Mathf.Min(.1f,Time.unscaledDeltaTime),musicStep=dt/Mathf.Max(.1f,tuning.musicCrossfadeSeconds);
            blendA=Mathf.MoveTowards(blendA,targetA,musicStep);blendB=Mathf.MoveTowards(blendB,targetB,musicStep);
            if(blendA<=0&&targetA==0)musicA.Stop();if(blendB<=0&&targetB==0)musicB.Stop();
            if(rangeScene&&!world&&Time.unscaledTimeAsDouble>=nextWorldCheck)
            {
                nextWorldCheck=Time.unscaledTimeAsDouble+.25;
                var candidate=FindFirstObjectByType<RangeCombatWorld>();
                if(candidate&&candidate.Projectiles!=null&&candidate.Player!=null)Bind(candidate);
            }
            float throttle=0;
            if(world&&world.Player!=null)
            {
                UpdateActors();var player=world.Player;var ship=player.ship;var flight=player.flight;
                if(ship.IsAlive)
                {
                    if(playerLife!=ship.LifeId){if(playerLife>=0)Emit(GameSound.Respawn);playerLife=ship.LifeId;thrustRevision=-1;}
                    if(thrustRevision!=ship.StructureRevision){thrusters.Clear();foreach(var part in ship.Parts)if(BlockModules.IsThruster(part.record.shape))thrusters.Add(part);thrustRevision=ship.StructureRevision;}
                    foreach(var part in thrusters)if(part.present&&part.thrustAvailable&&part.view)
                        throttle=Mathf.Max(throttle,flight.ThrusterThrottle(flight.transform.InverseTransformDirection(-part.view.transform.up)));
                    if(!FlightMenu)
                    {
                        if(flight.IsBoosting&&!boosting)Emit(GameSound.ManeuverJet);boosting=flight.IsBoosting;
                        var p=player.body.position;
                        float edge=flight.Tuning.arenaHalfExtent-flight.HullRadius-Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y),Mathf.Abs(p.z));
                        if(edge<tuning.boundaryWarningDistance&&Time.unscaledTimeAsDouble>=nextBoundary){Emit(GameSound.BoundaryWarning);nextBoundary=Time.unscaledTimeAsDouble+tuning.boundaryRepeatSeconds;}
                        var performance=ship.Performance;
                        if(performance.currentHp/Mathf.Max(1,performance.totalHp)<tuning.criticalHullFraction&&Time.unscaledTimeAsDouble>=nextCritical)
                        {Emit(GameSound.ConfirmWarning);nextCritical=Time.unscaledTimeAsDouble+tuning.criticalRepeatSeconds;}
                    }
                }
                else boosting=false;
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
            projectiles.ShotFired+=OnShot;
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
        void OnShot(FlightShipRuntime owner,Vector3 origin)
        {
            if(!world||world.Player==null)return;
            bool own=owner==world.Player.ship;Emit(GameSound.LaserShot,origin,!own,own?1:.7f,own?0:1);
        }
        void Detach()
        {
            if(projectiles!=null)projectiles.ShotFired-=OnShot;
            projectiles=null;world=null;hud=null;locked=null;lockLife=0;playerLife=thrustRevision=-1;alive.Clear();thrusters.Clear();
        }
        void OnDestroy(){Detach();SceneManager.sceneLoaded-=SceneLoaded;Application.focusChanged-=FocusChanged;}
    }
}
