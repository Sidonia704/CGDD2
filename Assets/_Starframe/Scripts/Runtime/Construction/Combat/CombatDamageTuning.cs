// @role Expose independent live non-missile and missile damage multipliers without rewriting weapon definitions.
// @calls WeaponDefinition, ProjectileWorld
// @tune nonMissileDamageMultiplier:float:0+:Global damage scale for projectile and beam weapons.
// @tune missileDamageMultiplier:float:0+:Global missile volley damage scale before faction scaling.

using UnityEngine;
using Starframe.Flight;

namespace Starframe.Combat
{
    [CreateAssetMenu(menuName="Starframe/Combat Damage Tuning")]
    public sealed class CombatDamageTuning:ScriptableObject
    {
        [Tooltip("Projectile and beam damage; 0.6 reduces original damage by 40%.")]
        [Min(0)]public float nonMissileDamageMultiplier=.6f;
        [Tooltip("Missile damage, tuned independently from all other weapons.")]
        [Min(0)]public float missileDamageMultiplier=.25f;
        [Header("Player-only fine balance (enemy output is unchanged)")]
        [Min(0)]public float playerDirectDamageMultiplier=1.1f;
        [Min(0)]public float playerMissileDamageMultiplier=.85f;
        public float GetPlayerMultiplier(WeaponDelivery delivery)=>GetMultiplier(delivery)*NonNegative(delivery==WeaponDelivery.GuidedMissile?playerMissileDamageMultiplier:playerDirectDamageMultiplier);
        public float GetMultiplier(WeaponDelivery delivery)=>NonNegative(delivery==WeaponDelivery.GuidedMissile?missileDamageMultiplier:nonMissileDamageMultiplier);
        public static float NonNegative(float value)=>float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Max(0,value);
    }
}
