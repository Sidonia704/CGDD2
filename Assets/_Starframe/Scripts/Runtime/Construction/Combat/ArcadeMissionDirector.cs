// @role Stream authored finite enemy formations through overlapping stages, with continuous flight, fair attack windows and mission outcomes.
// @calls RangeCombatWorld, ArcadeMissionDefinition, ArcadeWaveProgress, MissionResults, MissionHUD

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
        struct Queued {public EnemyArchetypeDefinition enemy;public float at;public int stage;}
        readonly List<Queued> queue=new List<Queued>();
        readonly ArcadeWaveProgress progress=new ArcadeWaveProgress();
        readonly List<RangeCombatWorld.Actor> candidates=new List<RangeCombatWorld.Actor>();
        readonly HashSet<RangeCombatWorld.Actor> permitted=new HashSet<RangeCombatWorld.Actor>();
        readonly Dictionary<int,int> livingStages=new Dictionary<int,int>();
        int scheduledWave;
        int[] stageRemaining;
        float stageStarted;
        ArcadeMissionDefinition definition;RangeCombatWorld world;RangeHUD hud;
        float phaseTime,waveTime,nextSpawn,nextAttack,comboUntil,finishAt;
        float reinforcementAt=-1;
        int wavesCleared,spawnSerial,consecutiveKills,smallKills,largeKills,capitalKills,maxCombo=1;
        bool initialized,returned;float ownedPauseScale=1;bool pauseOwned;
        public ArcadeMissionState State{get;private set;}=ArcadeMissionState.Intro;
        public int WaveIndex{get;private set;}
        public int WaveCount=>definition&&definition.waves!=null?definition.waves.Length:0;
        public int AliveEnemies=>progress.Alive;
        public int PendingEnemies=>progress.Pending;
        public bool ReinforcementsIncoming=>State==ArcadeMissionState.Combat&&reinforcementAt>=0&&progress.Alive==0&&progress.Pending>0;
        public float ReinforcementRemaining=>ReinforcementsIncoming?Mathf.Max(0,reinforcementAt-waveTime):0;
        public int Score{get;private set;}
        public int ComboMultiplier{get;private set;}=1;
        public float ElapsedSeconds{get;private set;}
        public float IntermissionRemaining=>Mathf.Max(0,(State==ArcadeMissionState.Intro?definition.introSeconds:definition.intermissionSeconds)-phaseTime);
        ArcadeWave DisplayWave=>definition&&WaveCount>0?definition.waves[Mathf.Clamp((State==ArcadeMissionState.Intro||State==ArcadeMissionState.Intermission)?WaveIndex:WaveIndex-1,0,WaveCount-1)]:null;
        public string WaveNameZh=>DisplayWave?.nameZh??"";
        public string WaveNameEn=>DisplayWave?.nameEn??"";
        public string WaveIncomingHintZh=>State==ArcadeMissionState.Intro?$"消灭 {WaveCount} 波敌军。核心被毁则出击失败。":ReinforcementsIncoming?"本批敌军已清除，增援正在接近，准备迎战。":"下一批敌军正在接近，可等待修复或提前迎战。";
        public string WaveIncomingHintEn=>State==ArcadeMissionState.Intro?$"Defeat {WaveCount} waves. Loss of your core ends the sortie.":ReinforcementsIncoming?"Formation cleared. Reinforcements approaching; prepare to engage.":"Reinforcements approaching. Recover, or start the next wave early.";
        public ArcadeMissionDefinition Definition=>definition;
        public RangeCombatWorld World=>world;
        public event Action Changed;
        ArcadeWave CurrentWave=>definition&&scheduledWave>0&&scheduledWave<=WaveCount?definition.waves[scheduledWave-1]:null;
        public void Initialize(RangeCombatWorld combat,ArcadeMissionDefinition config,RangeHUD display)
        {
            if(initialized)throw new InvalidOperationException("Mission already initialized.");
            if(!combat||!config||config.waves==null||config.waves.Length==0)throw new ArgumentException("Mission requires a combat world and waves.");
            world=combat;definition=config;hud=display;world.ConfigureMission();
            world.ActorDied+=OnActorDied;initialized=true;stageRemaining=new int[WaveCount];world.ConfigureMissionArea(config.combatAreaFraction);world.Player.flight.MenuCursorMode=false;
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
            if(definition.continuousStages&&scheduledWave<WaveCount&&queue.Count==0
                &&(waveTime-stageStarted>=Mathf.Max(0,definition.stageMinimumSeconds)||progress.Alive==0&&waveTime-stageStarted>=definition.emptyFieldReinforcementSeconds)
                &&(progress.Alive<=definition.stageOverlapEnemies||waveTime-stageStarted>=definition.stageTargetSeconds))StartNextWave();
            TrySpawn();UpdateAttackWindows();
            if(definition.continuousStages){
                if(scheduledWave>=WaveCount&&progress.IsCleared){wavesCleared=WaveCount;Finish(ArcadeMissionState.Victory);}
                return;
            }
            if(progress.IsCleared){wavesCleared=WaveIndex;if(WaveIndex>=WaveCount)Finish(ArcadeMissionState.Victory);else {State=ArcadeMissionState.Intermission;world.Player.flight.MenuCursorMode=true;phaseTime=0;Changed?.Invoke();}}
        }
        public bool StartNextWave()
        {
            if(!initialized||(State!=ArcadeMissionState.Intro&&State!=ArcadeMissionState.Intermission&&!(definition.continuousStages&&State==ArcadeMissionState.Combat&&queue.Count==0)))return false;
            if(scheduledWave>=WaveCount)return false;
            scheduledWave++;world.Player.flight.MenuCursorMode=false;int n=0;
            if(!definition.continuousStages){queue.Clear();waveTime=nextSpawn=nextAttack=0;}
            stageStarted=waveTime;
            var wave=CurrentWave;
            if(wave?.batches!=null)foreach(var batch in wave.batches)if(batch!=null&&batch.enemy)
                for(int i=0;i<Mathf.Max(0,batch.count);i++){queue.Add(new Queued{enemy=batch.enemy,at=waveTime+Mathf.Max(0,batch.delaySeconds),stage=scheduledWave});n++;}
            queue.Sort((a,b)=>a.at.CompareTo(b.at));
            stageRemaining[scheduledWave-1]=n;
            if(definition.continuousStages)progress.Append(n);else progress.Begin(n);
            State=ArcadeMissionState.Combat;phaseTime=0;reinforcementAt=-1;Changed?.Invoke();return true;
        }
        void TrySpawn()
        {
            AdvanceEmptyFieldReinforcements();
            if(ReinforcementsIncoming&&waveTime<reinforcementAt)return;
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
            var actor=world.SpawnMissionEnemy(item.enemy,++spawnSerial,definition.spawnDistance,definition.spawnDistanceVariation,item.stage);
            if(actor==null){nextSpawn=waveTime+1;return;}
            actor.attackReadyAt=world.CombatTime+definition.entryGraceSeconds;
            progress.Spawn(actor.spawnSlot);livingStages[actor.spawnSlot]=item.stage;WaveIndex=Mathf.Max(WaveIndex,item.stage);queue.RemoveAt(index);
            nextSpawn=waveTime+Mathf.Max(.2f,CurrentWave.spawnInterval);Changed?.Invoke();
        }
        void AdvanceEmptyFieldReinforcements()
        {
            if(progress.Alive>0||queue.Count==0){reinforcementAt=-1;return;}
            float notice=Mathf.Max(0,definition.emptyFieldReinforcementSeconds);
            if(reinforcementAt<0){
                if(queue[0].at-waveTime<=notice)return;
                reinforcementAt=waveTime+notice;Changed?.Invoke();
            }
            if(waveTime<reinforcementAt)return;
            // Shift the remaining formation schedule together, preserving its internal spacing.
            // This applies only to combat batches; intermission and manual wave advance keep their own timing.
            float advance=Mathf.Max(0,queue[0].at-waveTime);
            for(int i=0;i<queue.Count;i++){var item=queue[i];item.at-=advance;queue[i]=item;}
            nextSpawn=Mathf.Min(nextSpawn,waveTime);reinforcementAt=-1;Changed?.Invoke();
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
            if(livingStages.TryGetValue(a.spawnSlot,out int stage)){
                livingStages.Remove(a.spawnSlot);stageRemaining[stage-1]=Mathf.Max(0,stageRemaining[stage-1]-1);
                while(wavesCleared<scheduledWave&&stageRemaining[wavesCleared]==0)wavesCleared++;
            }
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
