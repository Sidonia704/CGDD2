// @role Play merged combat awareness cues; accelerate one dry missile alarm by distance or impact time and clear it when flight is inaudible.
// @calls RangeCombatWorld, TargetSensorWorld, ProjectileWorld, FlareWorld, GameAudio, GameAudioTuning

using UnityEngine;
using Starframe.Combat;

namespace Starframe.App
{
    /// <summary>Turns authoritative combat state changes into restrained, pooled audio cues.</summary>
    public sealed class ThreatAudioDriver : MonoBehaviour
    {
        const double DiscoveryMergeSeconds = .3;
        RangeCombatWorld world;
        TargetSensorWorld sensors;
        FlareWorld flares;
        double discoveryDue, nextMissileCue, lastMissileCue;
        bool discoveryPending, wasLocked, lockWarningPending, hadMissileThreat;
        int lastFlareId, missileLife=-1;

        public void Bind(RangeCombatWorld value)
        {
            if (sensors != null) sensors.TargetDiscovered -= OnTargetDiscovered;
            world = value;
            sensors = world ? world.Sensors : null;
            flares = world ? world.Flares : null;
            discoveryPending = false;
            ResetMissileWarning();
            missileLife = world && world.Player != null && world.Player.ship ? world.Player.ship.LifeId : -1;
            wasLocked = sensors != null && world.Player != null && sensors.CountLocksOn(world.Player.ship) > 0;
            lockWarningPending = wasLocked;
            if (sensors != null && world.Player != null && sensors.GetDetectedTargets(world.Player.ship).Count > 0)
            {
                discoveryPending = true;
                discoveryDue = Time.unscaledTimeAsDouble + DiscoveryMergeSeconds;
            }
            lastFlareId = 0;
            if (flares != null)
                foreach (var burst in flares.Bursts)
                    if (burst != null) lastFlareId = Mathf.Max(lastFlareId, burst.Id);
            if (sensors != null) sensors.TargetDiscovered += OnTargetDiscovered;
        }

        void OnTargetDiscovered(Starframe.Flight.FlightShipRuntime observer, Starframe.Flight.FlightShipRuntime target)
        {
            if (!world || world.Player == null || observer != world.Player.ship || !target) return;
            if (!discoveryPending)
            {
                discoveryPending = true;
                discoveryDue = Time.unscaledTimeAsDouble + DiscoveryMergeSeconds;
            }
        }

        public void Step()
        {
            if (!world || world.Player == null || sensors == null || world.Projectiles == null)
            {
                ResetMissileWarning();
                return;
            }
            var player = world.Player.ship;
            if (!player || !player.IsAlive)
            {
                discoveryPending = false;
                ResetMissileWarning();
                wasLocked = false;
                lockWarningPending = false;
                return;
            }

            bool audible = Application.isFocused && !GameInput.Suspended && Time.timeScale > 0;
            double now = Time.unscaledTimeAsDouble;
            if (missileLife != player.LifeId)
            {
                ResetMissileWarning();
                missileLife = player.LifeId;
            }
            if (discoveryPending && now >= discoveryDue && audible)
            {
                discoveryPending = false;
                GameAudio.Play(GameSound.EnemyDetected);
            }

            bool locked = sensors.CountLocksOn(player) > 0;
            if (locked && !wasLocked) lockWarningPending = true;
            if (!locked) lockWarningPending = false;
            if (locked && lockWarningPending && audible)
            {
                GameAudio.Play(GameSound.EnemyLockWarning);
                lockWarningPending = false;
            }
            wasLocked = locked;

            // TryGetIncomingThreat already filters hostile missiles, current life and a credible intercept path.
            // One nearest threat drives a single pulse train regardless of how many missiles are present.
            if (audible && world.Projectiles.TryGetIncomingThreat(player, out float distance, out float timeToImpact))
            {
                var tuning = GameAudio.Tuning;
                if (tuning)
                {
                    float interval = tuning.MissileWarningInterval(distance, timeToImpact);
                    // A rapidly closing missile can shorten the pending beat instead of waiting out its old slow beat.
                    if (hadMissileThreat && lastMissileCue + interval < nextMissileCue)
                        nextMissileCue = lastMissileCue + interval;
                    if ((!hadMissileThreat || now >= nextMissileCue) && GameAudio.TryPlay(GameSound.MissileIncoming))
                    {
                        lastMissileCue = now;
                        nextMissileCue = now + interval;
                    }
                }
                hadMissileThreat = true;
            }
            else ResetMissileWarning();

            if (flares != null)
                foreach (var burst in flares.Bursts)
                {
                    if (burst == null || burst.Id <= lastFlareId) continue;
                    lastFlareId = Mathf.Max(lastFlareId, burst.Id);
                    if (burst.Owner == player && audible) GameAudio.Play(GameSound.FlareDeploy);
                }
        }

        void ResetMissileWarning()
        {
            GameAudio.StopMissileWarning();
            hadMissileThreat = false;
            nextMissileCue = lastMissileCue = 0;
        }

        void OnDisable()
        {
            if (sensors != null) sensors.TargetDiscovered -= OnTargetDiscovered;
            ResetMissileWarning();
        }

        void OnDestroy()
        {
            if (sensors != null) sensors.TargetDiscovered -= OnTargetDiscovered;
            ResetMissileWarning();
        }
    }
}
