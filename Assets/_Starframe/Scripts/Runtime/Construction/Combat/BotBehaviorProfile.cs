// @role Define editable enemy piloting, engagement distance and firing cadence without overriding ship performance.
// @calls RangeBotPilot
// @tune preferredRange:float:40-3000:Desired shooting distance in metres, limited by the selected weapon and ship clearance.
// @tune velocityResponseSeconds:float:0.05-2:Continuous desired-velocity response; actual acceleration remains engine-limited.
// @tune steeringResponseSeconds:float:0.02-1:Continuous commanded sightline response, in seconds.
// @tune weaponHoldSeconds:float:0.1-10:Minimum duration to retain a surviving aim weapon before reconsidering the battery.
// @tune terrainProbeSeconds:float:0.05-1:Interval between bounded non-allocating terrain probes.

using UnityEngine;

namespace Starframe.Combat
{
    public enum BotCombatRole { Interceptor, Assault, Missile, Gunship, Frigate, Cruiser, Capital }

    [CreateAssetMenu(menuName = "Starframe/AI Behavior Profile")]
    public sealed class BotBehaviorProfile : ScriptableObject
    {
        public BotCombatRole role = BotCombatRole.Assault;
        [Min(40)] public float preferredRange = 450;
        [Range(.1f, 1)] public float cruiseSpeedFraction = .65f, attackSpeedFraction = .45f;
        [Range(0, .5f)] public float strafeFraction = .12f;
        public bool attackPass;
        [Min(.2f)] public float minimumAttackSeconds = 2, attackSeconds = 6, breakawaySeconds = 2;
        [Min(0)] public float reactionSeconds = 1;
        [Min(.05f)] public float fireBurstSeconds = .65f;
        [Min(0)] public float firePauseSeconds = 1.3f;
        [Range(0, 3)] public float aimErrorDegrees = .3f;
        [Range(.05f, 2)] public float velocityResponseSeconds = .45f;
        [Range(.02f, 1)] public float steeringResponseSeconds = .16f;
        [Range(.1f, 10)] public float weaponHoldSeconds = 1.4f;
        [Range(.5f, 5)] public float terrainLookaheadSeconds = 2;
        [Range(.05f, 1)] public float terrainProbeSeconds = .18f;

        public void ConfigureRole(BotCombatRole value)
        {
            role = value;
            attackPass = value == BotCombatRole.Interceptor || value == BotCombatRole.Assault;
            minimumAttackSeconds = 2; attackSeconds = 6; breakawaySeconds = 2;
            reactionSeconds = 1; fireBurstSeconds = .65f; firePauseSeconds = 1.3f;
            velocityResponseSeconds = .45f; steeringResponseSeconds = .16f; weaponHoldSeconds = 1.4f;
            terrainLookaheadSeconds = 2; terrainProbeSeconds = .18f; aimErrorDegrees = .3f;
            switch (value)
            {
                case BotCombatRole.Interceptor:
                    preferredRange = 300; cruiseSpeedFraction = .82f; attackSpeedFraction = .7f; strafeFraction = .08f;
                    attackSeconds = 5; breakawaySeconds = 1.7f; fireBurstSeconds = .5f; aimErrorDegrees = .45f;
                    break;
                case BotCombatRole.Assault:
                    preferredRange = 420; cruiseSpeedFraction = .7f; attackSpeedFraction = .55f; strafeFraction = .14f;
                    break;
                case BotCombatRole.Missile:
                    preferredRange = 850; cruiseSpeedFraction = .68f; attackSpeedFraction = .4f; strafeFraction = .15f;
                    fireBurstSeconds = 1.8f; firePauseSeconds = 2.4f; aimErrorDegrees = .2f;
                    break;
                case BotCombatRole.Gunship:
                    preferredRange = 550; cruiseSpeedFraction = .56f; attackSpeedFraction = .3f; strafeFraction = .16f;
                    fireBurstSeconds = .8f; firePauseSeconds = 1.5f; velocityResponseSeconds = .7f;
                    break;
                case BotCombatRole.Frigate:
                    preferredRange = 1000; cruiseSpeedFraction = .5f; attackSpeedFraction = .26f; strafeFraction = .08f;
                    fireBurstSeconds = 2; firePauseSeconds = 2.8f; velocityResponseSeconds = .8f;
                    break;
                case BotCombatRole.Cruiser:
                    preferredRange = 750; cruiseSpeedFraction = .48f; attackSpeedFraction = .3f; strafeFraction = .05f;
                    fireBurstSeconds = 1.2f; firePauseSeconds = 1.8f; velocityResponseSeconds = .85f;
                    break;
                case BotCombatRole.Capital:
                    preferredRange = 1100; cruiseSpeedFraction = .35f; attackSpeedFraction = .2f; strafeFraction = .025f;
                    fireBurstSeconds = 1.5f; firePauseSeconds = 2.5f; velocityResponseSeconds = 1;
                    steeringResponseSeconds = .24f; terrainLookaheadSeconds = 3; aimErrorDegrees = .2f;
                    break;
            }
        }
    }
}
