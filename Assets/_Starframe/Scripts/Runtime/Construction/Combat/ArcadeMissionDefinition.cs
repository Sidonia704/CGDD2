// @role Configure editable enemy batches, deployment rhythm and attack concurrency for one arcade mission.
// @calls EnemyArchetypeDefinition, ArcadeMissionDirector
// @tune maximumAlive:int:1-32:Maximum simultaneously active enemy ships.
// @tune maximumLarge:int:1-8:Maximum simultaneously active large ships, excluding capital ships.
// @tune maximumAttackers:int:1-8:Ships allowed to commit to the player at one time.
// @tune stageMinimumSeconds:float:0-300:Minimum occupied-stage duration before low-pressure overlap.
// @tune stageTargetSeconds:float:1-600:Advance a deployed stage after this duration despite surviving enemies.
// @tune combatAreaFraction:float:0.2-0.85:Central AI soft return radius relative to the flight boundary.
// @tune minimumArenaHalfExtent:float:0-100000:Mission minimum flight boundary half-width.
// @tune intermissionSeconds:float:0-120:Legacy preparation when continuous stages are disabled.
// @tune emptyFieldReinforcementSeconds:float:0-10:Notice before advancing a later batch when the current field is cleared.

using System;
using UnityEngine;
namespace Starframe.Combat
{
    [Serializable] public sealed class ArcadeEnemyBatch
    {
        public EnemyArchetypeDefinition enemy;
        [Min(1)] public int count=1;
        [Min(0)] public float delaySeconds;
    }
    [Serializable] public sealed class ArcadeWave
    {
        public string nameZh,nameEn;
        public ArcadeEnemyBatch[] batches=Array.Empty<ArcadeEnemyBatch>();
        [Min(.1f)] public float spawnInterval=4;
    }
    [CreateAssetMenu(menuName="Starframe/Arcade Mission")]
    public sealed class ArcadeMissionDefinition:ScriptableObject
    {
        public string id="saturn-ring";
        public Starframe.App.MissionDefinition deployment;
        public TextAsset defaultPlayerBlueprint;
        public ArcadeWave[] waves=Array.Empty<ArcadeWave>();
        [Range(1,32)] public int maximumAlive=10;
        [Range(1,8)] public int maximumLarge=3;
        [Range(1,8)] public int maximumAttackers=3;
        [Header("Continuous stages")]
        public bool continuousStages=true;
        [Min(0)] public float stageMinimumSeconds=35;
        [Min(1)] public float stageTargetSeconds=135;
        [Min(0)] public int stageOverlapEnemies=3;
        [Range(.2f,.85f)] public float combatAreaFraction=.62f;
        [Min(0)] public float minimumArenaHalfExtent=2200;
        [Min(0)] public float introSeconds=8,intermissionSeconds=20;
        [Range(0,10)] public float emptyFieldReinforcementSeconds=3;
        [Min(.1f)] public float attackRotationSeconds=4;
        [Min(10)] public float spawnDistance=700;
        [Min(1)] public float spawnDistanceVariation=240;
        [Min(0)] public float entryGraceSeconds=2;
        [Min(1)] public float comboSeconds=18;
        [Range(1,10)] public int maximumCombo=3;
        [Min(0)] public float resultDelaySeconds=3;
        public int TotalEnemies
        {
            get { int n=0;if(waves!=null)foreach(var w in waves)if(w?.batches!=null)foreach(var b in w.batches)if(b!=null&&b.enemy)n+=Mathf.Max(0,b.count);return n; }
        }
    }
}
