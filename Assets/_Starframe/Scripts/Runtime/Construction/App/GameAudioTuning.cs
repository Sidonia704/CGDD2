// @role Define sound cues, engine response, music levels and playback limits.

using System;
using UnityEngine;

namespace Starframe.App
{
    public enum GameSound
    {
        Select, SaveBlueprint, ConfirmWarning, PlaceHull, PlaceModule, RemovePart,
        RotatePart, InvalidPlacement, CorePowerOn, LaserShot, LaserImpact,
        LockAcquired, TargetLost, ManeuverJet, BoundaryWarning, CoreDestroyed, Respawn
    }
    public enum GameSoundBus { Effects, Interface }

    [Serializable]
    public sealed class GameSoundCue
    {
        public GameSound sound;
        public AudioClip clip;
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
        [Range(4,32)] public int effectVoices=16;
        [Min(1)] public float nearDistance=12,farDistance=500;
        [Min(0)] public float boundaryWarningDistance=90;
        [Min(.5f)] public float boundaryRepeatSeconds=3;
        [Range(0,1)] public float criticalHullFraction=.25f;
        [Min(1)] public float criticalRepeatSeconds=10;
        [Header("Per event clips, level, pitch, cooldown and bus")]
        public GameSoundCue[] cues=Array.Empty<GameSoundCue>();
        public GameSoundCue Get(GameSound sound)
        {
            foreach(var cue in cues)if(cue!=null&&cue.sound==sound)return cue;
            return null;
        }
    }
}
