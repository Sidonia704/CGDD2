# Combat threat cue sources

These four 48 kHz mono PCM WAVs are generated locally by `../generate_audio.py` from sine tones, smooth amplitude envelopes, and brief silences. They contain no recorded or third-party material. `AudioThreatInstall.Install` copies them into the Unity audio clips folder and appends their cues to `GameAudioTuning` without changing existing cue entries.

- `Enemy_Detected.wav`: two short ascending pips.
- `Enemy_Lock_Warning.wav`: one smooth rising chirp.
- `Missile_Incoming.wav`: two quick descending warning pips.
- `Flare_Deploy.wav`: one short descending tick.
