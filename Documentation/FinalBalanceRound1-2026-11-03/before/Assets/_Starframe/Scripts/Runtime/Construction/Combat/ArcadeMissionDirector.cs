// @role Run five-wave arcade combat, finite reinforcements, scoring, fair attack windows and mission outcomes.
// @calls RangeCombatWorld, ArcadeMissionDefinition, ArcadeWaveProgress, MissionResults, MissionHUD
// @tune State:ArcadeMissionState:Intro-Aborted:Current mission phase, read only.

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;
namespace Starframe.Combat
{
    public enum ArcadeMissionState {Intro,Combat,Intermission,Victory,Defeat,Aborted}
    [DefaultExecutionOrder(200)]
    public sealed class ArcadeMissionDirector:MonoBehaviour
    {
        struct Queued {public EnemyArchetypeDefinition enemy;public float at;}
        readonly List<Queued> queue=new List<Queued>();
        readonly ArcadeWaveProgress progress=new ArcadeWaveProgress();
        readonly List<RangeCombatWorld.Actor> candidates=new List<RangeCombatWorld.Actor>();
        readonly HashSet<RangeCombatWorld.Actor> permitted=new HashSet<RangeCombatWorld.Actor>();
        ArcadeMissionDefinition definition;RangeCombatWorld world;RangeHUD hud;
        float phaseTime,waveTime,nextSpawn,nextAttack,comboUntil,finishAt;
        int wavesCleared,spawnSerial,consecutiveKills,smallKills,largeKills,capitalKills,maxCombo=1;
        bool initialized,returned;float ownedPauseScale=1;bool pauseOwned;
        public ArcadeMissionState State{get;private set;}=ArcadeMissionState.Intro;
        public int WaveIndex{get;private set;}
        public int WaveCount=>definition&&definition.waves!=null?definition.waves.Length:0;
        public int AliveEnemies=>progress.Alive;
        public int PendingEnemies=>progress.Pending;
        public int Score{get;private set;}
        public int ComboMultiplier{get;private set;}=1;
        public float ElapsedSeconds{get;private set;}
        public float IntermissionRemaining=>Mathf.Max(0,(State==ArcadeMissionState.Intro?definition.introSeconds:definition.intermissionSeconds)-phaseTime);
        ArcadeWave DisplayWave=>definition&&WaveCount>0?definition.waves[Mathf.Clamp((State==ArcadeMissionState.Intro||State==ArcadeMissionState.Intermission)?WaveIndex:WaveIndex-1,0,WaveCount-1)]:null;
        public string WaveNameZh=>DisplayWave?.nameZh??"";
        public string WaveNameEn=>DisplayWave?.nameEn??"";
        public string WaveIncomingHintZh=>State==ArcadeMissionState.Intro?"消灭五波敌军。核心被毁则出击失败。":"下一批敌军正在接近，可等待修复或提前迎战。";
        public string WaveIncomingHintEn=>State==ArcadeMissionState.Intro?"Defeat five waves. Loss of your core ends the sortie.":"Reinforcements approaching. Recover, or start the next wave early.";
        public ArcadeMissionDefinition Definition=>definition;
        public RangeCombatWorld World=>world;
        public event Action Changed;
        ArcadeWave CurrentWave=>definition&&WaveIndex>0&&WaveIndex<=WaveCount?definition.waves[WaveIndex-1]:null;
        public void Initialize(RangeCombatWorld combat,ArcadeMissionDefinition config,RangeHUD display)
        {
            if(initialized)throw new InvalidOperationException("Mission already initialized.");
            if(!combat||!config||config.waves==null||config.waves.Length==0)throw new ArgumentException("Mission requires a combat world and waves.");
            world=combat;definition=config;hud=display;world.ConfigureMission();
            world.ActorDied+=OnActorDied;initialized=true;world.Player.flight.MenuCursorMode=true;
            gameObject.AddComponent<MissionHUD>().Initialize(this);Changed?.Invoke();
        }
        void Update()
        {
            if(!initialized||returned)return;
            bool terminal=State==ArcadeMissionState.Victory||State==ArcadeMissionState.Defeat||State==ArcadeMissionState.Aborted;
            bool menu=hud&&hud.ExitDialogOpen;
            if(!terminal&&menu&&!pauseOwned){ownedPauseScale=Time.timeScale;Time.timeScale=0;pauseOwned=true;}
            else if(pauseOwned&&(!menu||terminal)){Time.timeScale=ownedPauseScale;pauseOwned=false;}
            if(terminal){if(Time.unscaledTime>=finishAt)ReturnResult();return;}
            if(menu)return;
            float dt=Time.deltaTime;ElapsedSeconds+=dt;phaseTime+=dt;
            if(State==ArcadeMissionState.Intro){if(phaseTime>=definition.introSeconds)StartNextWave();return;}
            if(State==ArcadeMissionState.Intermission){comboUntil+=dt;if(phaseTime>=definition.intermissionSeconds)StartNextWave();return;}
            waveTime+=dt;
            if(ComboMultiplier>1&&ElapsedSeconds>comboUntil){consecutiveKills=0;ComboMultiplier=1;}
            TrySpawn();UpdateAttackWindows();
            if(progress.IsCleared){wavesCleared=WaveIndex;if(WaveIndex>=WaveCount)Finish(ArcadeMissionState.Victory);else {State=ArcadeMissionState.Intermission;world.Player.flight.MenuCursorMode=true;phaseTime=0;Changed?.Invoke();}}
        }
        public bool StartNextWave()
        {
            if(!initialized||(State!=ArcadeMissionState.Intro&&State!=ArcadeMissionState.Intermission))return false;
            if(WaveIndex>=WaveCount)return false;
            WaveIndex++;world.Player.flight.MenuCursorMode=false;queue.Clear();int n=0;
            var wave=CurrentWave;
            if(wave?.batches!=null)foreach(var batch in wave.batches)if(batch!=null&&batch.enemy)
                for(int i=0;i<Mathf.Max(0,batch.count);i++){queue.Add(new Queued{enemy=batch.enemy,at=Mathf.Max(0,batch.delaySeconds)});n++;}
            queue.Sort((a,b)=>a.at.CompareTo(b.at));progress.Begin(n);
            State=ArcadeMissionState.Combat;phaseTime=waveTime=nextSpawn=nextAttack=0;Changed?.Invoke();return true;
        }
        void TrySpawn()
        {
            if(queue.Count==0||waveTime<nextSpawn||progress.Alive>=definition.maximumAlive)return;
            int large=0,capital=0;foreach(var a in world.Bots)if(a.ship.IsAlive&&a.archetype){if(a.archetype.size==EnemySize.Large)large++;if(a.archetype.size==EnemySize.Capital)capital++;}
            int index=-1;
            for(int i=0;i<queue.Count;i++){
                var q=queue[i];if(q.at>waveTime)continue;
                if(q.enemy.size==EnemySize.Large&&large>=definition.maximumLarge)continue;
                if(q.enemy.size==EnemySize.Capital&&capital>=1)continue;
                index=i;break;
            }
            if(index<0)return;
            var item=queue[index];
            var actor=world.SpawnMissionEnemy(item.enemy,++spawnSerial,definition.spawnDistance,definition.spawnDistanceVariation,WaveIndex);
            if(actor==null){nextSpawn=waveTime+1;return;}
            actor.attackReadyAt=world.CombatTime+definition.entryGraceSeconds;
            progress.Spawn(actor.spawnSlot);queue.RemoveAt(index);
            nextSpawn=waveTime+Mathf.Max(.2f,CurrentWave.spawnInterval);Changed?.Invoke();
        }
        void UpdateAttackWindows()
        {
            if(waveTime>=nextAttack){
                candidates.Clear();foreach(var a in world.Bots)if(a.ship.IsAlive&&world.CombatTime>=a.attackReadyAt)candidates.Add(a);
                // Keep the firing arc and engagement distance relevant; rotate a small priority bonus between slots.
                int turn=(int)(waveTime/Mathf.Max(.5f,definition.attackRotationSeconds));
                candidates.Sort((a,b)=>AttackPriority(b,turn).CompareTo(AttackPriority(a,turn)));
                permitted.Clear();for(int i=0;i<Mathf.Min(definition.maximumAttackers,candidates.Count);i++)permitted.Add(candidates[i]);
                nextAttack=waveTime+Mathf.Max(.5f,definition.attackRotationSeconds);
            }
            foreach(var a in world.Bots)a.pilot?.SetAttackPermission(a.ship.IsAlive&&permitted.Contains(a)&&world.CombatTime>=a.attackReadyAt);
        }
        float AttackPriority(RangeCombatWorld.Actor a,int turn)
        {
            var offset=world.Player.body.position-a.body.position;float angle=Vector3.Dot(a.body.rotation*Vector3.forward,offset.normalized);
            return angle*3-Mathf.Min(offset.magnitude,2500)/1000+((a.spawnSlot+turn)%4==0?1.5f:0);
        }
        void OnActorDied(RangeCombatWorld.Actor a)
        {
            if(State!=ArcadeMissionState.Combat&&State!=ArcadeMissionState.Intro&&State!=ArcadeMissionState.Intermission)return;
            if(a==world.Player){Finish(ArcadeMissionState.Defeat);return;}
            if(!progress.Defeat(a.spawnSlot))return;
            if(a.archetype){switch(a.archetype.size){case EnemySize.Small:smallKills++;break;case EnemySize.Large:largeKills++;break;default:capitalKills++;break;}}
            if(ElapsedSeconds>comboUntil)consecutiveKills=0;
            consecutiveKills++;ComboMultiplier=Mathf.Min(definition.maximumCombo,1+(consecutiveKills-1)/3);maxCombo=Mathf.Max(maxCombo,ComboMultiplier);
            Score+=(a.archetype?Mathf.Max(0,a.archetype.score):100)*ComboMultiplier;comboUntil=ElapsedSeconds+definition.comboSeconds;
            nextAttack=0;Changed?.Invoke();
        }
        void Finish(ArcadeMissionState state)
        {
            if(State==ArcadeMissionState.Victory||State==ArcadeMissionState.Defeat||State==ArcadeMissionState.Aborted)return;
            State=state;world.Player.flight.MenuCursorMode=true;world.CombatEnabled=false;Time.timeScale=pauseOwned?ownedPauseScale:Time.timeScale;pauseOwned=false;
            finishAt=Time.unscaledTime+definition.resultDelaySeconds;Changed?.Invoke();
        }
        public void AbortAndReturn(){if(!initialized)return;Finish(ArcadeMissionState.Aborted);ReturnResult();}
        public MissionRunSummary Summary()
        {
            int remaining=0;foreach(var p in world.Player.ship.Parts)if(p.present)remaining++;
            return new MissionRunSummary{durationSeconds=ElapsedSeconds,enemiesDestroyed=smallKills+largeKills+capitalKills,
                survivingParts=remaining,damageDealt=world.Player.ship.DamageDealt,damageTaken=world.Player.ship.DamageTaken,
                score=Score,wavesCompleted=wavesCleared,totalWaves=WaveCount,
                smallDestroyed=smallKills,largeDestroyed=largeKills,capitalDestroyed=capitalKills,maxCombo=maxCombo};
        }
        void ReturnResult()
        {
            if(returned)return;returned=true;
            if(pauseOwned){Time.timeScale=ownedPauseScale;pauseOwned=false;}
            var outcome=State==ArcadeMissionState.Victory?MissionOutcome.Victory:State==ArcadeMissionState.Defeat?MissionOutcome.Defeat:MissionOutcome.Aborted;
            MissionResults.FinishAndReturn(outcome,Summary());
        }
        void OnDestroy(){if(world){world.ActorDied-=OnActorDied;if(world.Player?.flight)world.Player.flight.MenuCursorMode=false;}if(pauseOwned)Time.timeScale=ownedPauseScale;}
    }
}
