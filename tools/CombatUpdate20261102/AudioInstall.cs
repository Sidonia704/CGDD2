using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;

namespace Starframe.Editor
{
    /// <summary>Copies the prepared threat cues and appends only the four new tuning entries.</summary>
    public static class AudioThreatInstall
    {
        const string TuningPath = "Assets/_Starframe/Resources/Audio/GameAudioTuning.asset";
        const string ClipFolder = "Assets/_Starframe/Audio/Clips/";
        static readonly CueSetup[] Setups =
        {
            new CueSetup(GameSound.EnemyDetected, "Enemy_Detected.wav", .24f, .3f, 48),
            new CueSetup(GameSound.EnemyLockWarning, "Enemy_Lock_Warning.wav", .3f, .08f, 28),
            new CueSetup(GameSound.MissileIncoming, "Missile_Incoming.wav", .3f, .05f, 20),
            new CueSetup(GameSound.FlareDeploy, "Flare_Deploy.wav", .18f, .05f, 112)
        };

        readonly struct CueSetup
        {
            public readonly GameSound Sound;
            public readonly string FileName;
            public readonly float Gain, MinimumInterval;
            public readonly int Priority;

            public CueSetup(GameSound sound, string fileName, float gain, float minimumInterval, int priority)
            { Sound = sound; FileName = fileName; Gain = gain; MinimumInterval = minimumInterval; Priority = priority; }
        }

        [MenuItem("Starframe/Audio/Install combat threat cues")]
        public static void Install()
        {
            string sourceFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tools", "CombatUpdate20261102", "AudioSource"));
            if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException("Prepared cue folder not found: " + sourceFolder);
            if (!AssetDatabase.IsValidFolder("Assets/_Starframe/Audio/Clips"))
                throw new DirectoryNotFoundException("Audio clip folder is missing: " + ClipFolder);

            foreach (var setup in Setups)
            {
                string source = Path.Combine(sourceFolder, setup.FileName);
                if (!File.Exists(source)) throw new FileNotFoundException("Prepared cue is missing.", source);
                string destination = ClipFolder + setup.FileName;
                string destinationFile = Path.Combine(Application.dataPath, "..", destination);
                if (!File.Exists(destinationFile)) FileUtil.CopyFileOrDirectory(source, destination);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var setup in Setups) ConfigureImporter(ClipFolder + setup.FileName);

            var tuning = AssetDatabase.LoadAssetAtPath<GameAudioTuning>(TuningPath);
            if (!tuning) throw new InvalidOperationException("GameAudioTuning asset not found: " + TuningPath);
            Undo.RecordObject(tuning, "Install combat threat audio cues");
            var cues = tuning.cues == null ? new System.Collections.Generic.List<GameSoundCue>() : tuning.cues.ToList();
            foreach (var setup in Setups)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipFolder + setup.FileName);
                if (!clip) throw new InvalidOperationException("Imported audio clip not found: " + ClipFolder + setup.FileName);
                var cue = cues.FirstOrDefault(existing => existing != null && existing.sound == setup.Sound);
                if (cue == null)
                {
                    cue = new GameSoundCue
                    {
                        sound = setup.Sound,
                        bus = GameSoundBus.Effects,
                        gain = setup.Gain,
                        pitch = 1,
                        pitchVariation = 0,
                        minimumInterval = setup.MinimumInterval,
                        priority = setup.Priority
                    };
                    cues.Add(cue);
                }
                cue.clip = clip;
            }
            tuning.cues = cues.ToArray();
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Audio importer not found: " + assetPath);
            var sample = importer.defaultSampleSettings;
            sample.loadType = AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat = AudioCompressionFormat.PCM;
            sample.quality = .8f;
            sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            sample.preloadAudioData = true;
            importer.defaultSampleSettings = sample;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }
    }
}
