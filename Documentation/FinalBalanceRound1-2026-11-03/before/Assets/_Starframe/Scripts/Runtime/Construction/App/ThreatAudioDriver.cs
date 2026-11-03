// @role Play merged combat awareness cues for the player's detected contacts, incoming locks, missiles and flares.
// @calls RangeCombatWorld, TargetSensorWorld, ProjectileWorld, FlareWorld, GameAudio

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
        double discoveryDue, nextMissileCue;
        bool discoveryPending, wasLocked, lockWarningPending, hadMissileThreat;
        int lastFlareId;

        public void Bind(RangeCombatWorld value)
        {
            if (sensors != null) sensors.TargetDiscovered -= OnTargetDiscovered;
            world = value;
            sensors = world ? world.Sensors : null;
            flares = world ? world.Flares : null;
            discoveryPending = false;
            nextMissileCue = 0;
            hadMissileThreat = false;
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
            if (!world || world.Player == null || sensors == null || world.Projectiles == null) return;
            var player = world.Player.ship;
            if (!player || !player.IsAlive)
            {
                discoveryPending = false;
                hadMissileThreat = false;
                wasLocked = false;
                lockWarningPending = false;
                return;
            }

            bool audible = Application.isFocused && !GameInput.Suspended && Time.timeScale > 0;
            double now = Time.unscaledTimeAsDouble;
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

            bool incoming = world.Projectiles.TryGetIncomingThreat(player, out float distance, out _);
            if (incoming)
            {
                var tuning = GameAudio.Tuning;
                if (tuning && audible && (!hadMissileThreat || now >= nextMissileCue))
                {
                    GameAudio.Play(GameSound.MissileIncoming);
                    float near = Mathf.Max(1, tuning.missileWarningNearDistance);
                    float far = Mathf.Max(near + 1, tuning.missileWarningFarDistance);
                    float urgency = Mathf.InverseLerp(far, near, distance);
                    float interval = Mathf.Lerp(tuning.missileWarningFarInterval, tuning.missileWarningNearInterval, urgency);
                    nextMissileCue = now + Mathf.Max(.05f, interval);
                }
            }
            hadMissileThreat = incoming;

            if (flares != null)
                foreach (var burst in flares.Bursts)
                {
                    if (burst == null || burst.Id <= lastFlareId) continue;
                    lastFlareId = Mathf.Max(lastFlareId, burst.Id);
                    if (burst.Owner == player && audible) GameAudio.Play(GameSound.FlareDeploy);
                }
        }

        void OnDisable()
        {
            if (sensors != null) sensors.TargetDiscovered -= OnTargetDiscovered;
        }

        void OnDestroy()
        {
            if (sensors != null) sensors.TargetDiscovered -= OnTargetDiscovered;
        }
    }
}
