// @role Define recovery, target-lock, opponent behavior and respawn defaults.
// @tune outOfCombatDelay:float:0+:Seconds without hull damage before recovery.
// @tune recoveryTick:float:0.02-0.5:Seconds between recovery evaluations.
// @tune unlimitedRangePower:bool:false-true:Practice-only bypass for energy scarcity.
// @tune lockConeDegrees:float:1-80:Target acquisition cone in degrees.
// @tune lockRange:float:1+:Maximum target acquisition distance in metres.
// @tune botCruiseSpeedFraction:float:0.1-1:Cruise speed as a fraction of surviving ship capability.
// @tune botAttackSpeedFraction:float:0.1-1:Attack speed as a fraction of surviving ship capability.
// @tune botPilotDecisionSeconds:float:0.05-0.5:Opponent decision interval in seconds.
// @tune botReactionSeconds:float:0+:Delay before an opponent starts firing.
// @tune botFireBurstSeconds:float:0.05+:Duration of a firing burst.
// @tune botFirePauseSeconds:float:0+:Pause between firing bursts.
// @tune botRespawnSeconds:float:0+:Delay before an opponent respawns.
// @tune playerRespawnSeconds:float:0+:Delay before the player respawns in practice.
// @tune playerSpawnProtectionSeconds:float:0+:Practice respawn invulnerability duration.

using UnityEngine;
namespace Starframe.Combat
{
    [CreateAssetMenu(menuName="Starframe/Battle Tuning")]
    public sealed class BattleTuning:ScriptableObject
    {
        [Header("Damage / restoration")]
        [Min(0)]public float outOfCombatDelay=1;
        [Range(.02f,.5f)]public float recoveryTick=.1f;
        [Header("Temporary range power: disable when adding generators / batteries")]
        public bool unlimitedRangePower=true;
        public Shader laserShader;
        public Shader destructionShader;
        [Header("Shared visual size (does not change hitboxes)")]
        [Min(.1f)]public float particleScale=1.5f;
        [Header("Detached wreckage")]
        [Min(0)]public float debrisDriftSpeed=2.5f,debrisSpinDegreesPerSecond=25;
        [Header("Forward target lock")]
        [Range(1,80)]public float lockConeDegrees=35;
        [Min(1)]public float lockRange=3000;
        [Tooltip("Seconds of physical core-position samples used to estimate target velocity.")]
        [Range(.04f,1)]public float leadVelocitySampleWindow=.16f;
        [Tooltip("Additional velocity smoothing in seconds; smaller values follow manoeuvres faster.")]
        [Range(0,.2f)]public float leadVelocityResponse=.04f;
        [Header("Range opponent")]
        public TextAsset defaultTargetBlueprint;
        public float botSpawnDistance=35,botMovementSpeed=7,botTurnDegreesPerSecond=55;
        public float botAimProbability=.6f,botDecisionSeconds=1.2f,botRespawnSeconds=3,playerRespawnSeconds=3;
        public float playerSpawnProtectionSeconds=4;
        [Header("Range pilot / fractions of the surviving ship's performance")]
        [Range(.1f,1)]public float botCruiseSpeedFraction=.65f,botAttackSpeedFraction=.45f;
        [Range(.05f,.5f)]public float botPilotDecisionSeconds=.1f;
        [Range(.1f,5)]public float botMaxFireAngleDegrees=2.2f;
        [Min(.2f)]public float botAttackSeconds=5,botBreakawaySeconds=1.6f;
        [Header("Combat training: reaction time and short bursts")]
        [Min(0)]public float botReactionSeconds=1.2f;
        [Min(.05f)]public float botFireBurstSeconds=.3f;
        [Min(0)]public float botFirePauseSeconds=1.8f;
        [Tooltip("Fraction of weapon range used for sustained attack passes.")]
        [Range(.1f,.6f)]public float botEngagementRangeFraction=.25f;
        [Tooltip("Attack movement spent strafing while keeping the guns facing the target.")]
        [Range(0,.5f)]public float botStrafeSpeedFraction=.1f;
        [Tooltip("Fire at the hull silhouette instead of waiting for pixel-perfect core alignment. Aim still leads the core.")]
        [Range(.05f,1)]public float botFireTargetRadiusFraction=.28f;
    }
}
