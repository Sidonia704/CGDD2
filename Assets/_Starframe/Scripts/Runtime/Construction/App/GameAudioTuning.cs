// @role Define weapon and warning cues, Gatling start/end phases, alternating clips, engine response, music levels and playback limits.
// @tune criticalHullFraction:float:0-1:Hull fraction triggering the shared health/shield warning
// @tune criticalRepeatSeconds:float:1+:Seconds between shared health/shield warnings
// @tune missileWarningFarDistance:float:1-5000:Distance for the slowest incoming missile pulse
// @tune missileWarningNearDistance:float:1-500:Distance for the fastest incoming missile pulse
// @tune missileWarningFarTimeToImpact:float:0.1-30:Impact estimate for the slowest incoming missile pulse
// @tune missileWarningNearTimeToImpact:float:0-5:Impact estimate for the fastest incoming missile pulse
// @tune missileWarningFarInterval:float:0.1-5:Seconds between distant incoming missile pulses
// @tune missileWarningNearInterval:float:0.1-5:Minimum seconds between imminent incoming missile pulses

using System;
using UnityEngine;

namespace Starframe.App
{
    public enum GameSound
    {
        Select, SaveBlueprint, ConfirmWarning, PlaceHull, PlaceModule, RemovePart,
        RotatePart, InvalidPlacement, CorePowerOn, LaserShot, LaserImpact,
        LockAcquired, TargetLost, ManeuverJet, BoundaryWarning, CoreDestroyed, Respawn,
        EnemyDetected, EnemyLockWarning, MissileIncoming, FlareDeploy,
        LaserRapid, DirectLaser, RailLarge, RailSmall, Gatling, MissileShot,
        HealthShieldWarning, OverheatWarning
    }
    public enum GameSoundBus { Effects, Interface }

    [Serializable]
    public sealed class GameSoundCue
    {
        public GameSound sound;
        public AudioClip clip;
        [Tooltip("Gatling intro and release; clip is the seamless sustain loop. State, not clip length, controls firing.")]
        public AudioClip startClip,endClip;
        [Tooltip("Optional second clip, alternated deterministically after each admitted playback.")]
        public AudioClip alternateClip;
        [Tooltip("Optional deterministic round-robin variants; when populated these replace clip/alternateClip selection.")]
        public AudioClip[] variants=Array.Empty<AudioClip>();
        [Tooltip("Keep one bounded voice running while actual shots refresh this cue/channel.")]
        public bool loopWhileFiring;
        [Min(.01f)] public float releaseSeconds=.15f;
        public GameSoundBus bus;
        [Range(0,1)] public float gain=.6f;
        [Range(.5f,2)] public float pitch=1;
        [Range(0,.2f)] public float pitchVariation=.025f;
        [Min(0)] public float minimumInterval=.07f;
        [Range(0,256)] public int priority=128;
    }

    [CreateAssetMenu(menuName="Starframe/Audio tuning")]
    public sealed class GameAudioTuning:ScriptableObject
    {
        [Header("Music (leave a clip empty to disable it)")]
        public AudioClip workbenchMusic,flightMusic;
        [Range(0,1)] public float musicGain=.6f;
        [Min(.1f)] public float musicCrossfadeSeconds=1.5f;
        [Range(0,1)] public float menuEffectsMultiplier=.4f;
        [Header("Own ship propulsion")]
        public AudioClip thrusterLoop;
        [Range(0,1)] public float thrusterGain=.52f;
        [Range(1,3)] public float boostVolumeMultiplier=1.75f;
        [Range(.5f,2)] public float idlePitch=.86f,fullThrustPitch=1.08f,boostPitch=1.18f;
        [Min(.02f)] public float thrustAttack=.12f,thrustRelease=.4f;
        [Header("Voice limits and distance")]
        [Range(8,32)] public int effectVoices=16;
        [Min(1)] public float nearDistance=12,farDistance=500;
        [Min(0)] public float boundaryWarningDistance=90;
        [Min(.5f)] public float boundaryRepeatSeconds=3;
        [Range(0,1)] public float criticalHullFraction=.25f;
        [Min(1)] public float criticalRepeatSeconds=10;
        [Header("Incoming missile warning cadence")]
        [Min(1)] public float missileWarningFarDistance=600,missileWarningNearDistance=40;
        [Min(.1f)] public float missileWarningFarTimeToImpact=4;
        [Min(0)] public float missileWarningNearTimeToImpact=.35f;
        [Min(.1f)] public float missileWarningFarInterval=.9f,missileWarningNearInterval=.12f;
        [Header("Per event clips, level, pitch, cooldown and bus")]
        public GameSoundCue[] cues=Array.Empty<GameSoundCue>();
        public GameSoundCue Get(GameSound sound)
        {
            foreach(var cue in cues)if(cue!=null&&cue.sound==sound)return cue;
            return null;
        }
        /// <summary>One merged alarm uses the more urgent of distance and the missile world's impact estimate.</summary>
        public float MissileWarningInterval(float distance,float timeToImpact)
        {
            float near=Mathf.Max(1,missileWarningNearDistance),far=Mathf.Max(near+1,missileWarningFarDistance);
            float nearTime=Mathf.Max(0,missileWarningNearTimeToImpact),farTime=Mathf.Max(nearTime+.1f,missileWarningFarTimeToImpact);
            float distanceUrgency=float.IsNaN(distance)?0:Mathf.InverseLerp(far,near,distance);
            float impactUrgency=float.IsNaN(timeToImpact)?0:Mathf.InverseLerp(farTime,nearTime,timeToImpact);
            float fastest=Mathf.Max(.1f,missileWarningNearInterval),slowest=Mathf.Max(fastest,missileWarningFarInterval);
            return Mathf.Lerp(slowest,fastest,Mathf.Max(distanceUrgency,impactUrgency));
        }
    }
}
