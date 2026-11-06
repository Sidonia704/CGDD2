# Audio 116 revision 2 — 2026-11-06

## Installed behavior
- Gatling: real powered-fire heartbeat maintains one continuous voice through slow spin-up shot gaps; release, loss of power, destruction and overheat end it. Ordinary weapons/UI cannot steal the own-ship loop; critical warnings retain priority.
- Gatling thermal defaults and all three native profiles: heat 0.25/s, simultaneous cooling 1/6/s. Net full-heat time 12 s; locked until zero after 6 s cooling.
- Large rail: 0.65 s; small rail variants: 0.28 / 0.40 s. Leading silence removed; tails shortened with fades.
- Alerts: 2.8 kHz -5 dB EQ, 4.8 kHz lowpass, 0.85 gain, without normalization undoing attenuation.
- Missile incoming: final short tone from old segment 15 at relative 0.639–0.802 s, tempo-compressed without pitch shift to 0.1089 s. Existing far/near cadence 0.9 / 0.12 s retained.
- Flare: six individual swells, about 0.135–0.143 s, one per admitted deployment cue in deterministic rotation. Source-relative boundaries 0, .143, .282, .424, .564, .699, .841 s.

## New 1162.mp3 cuts
| Use | Source interval (seconds) | Mode |
| --- | --- | --- |
| Rapid laser | .055–.355 | One-shot |
| Direct laser | .724–2.665 | Seam-crossfaded loop |
| Missile launch | 3.285–4.230 | One-shot |
| System UI A | 4.644–4.830 | Rotation feedback |
| System UI B | 5.287–5.360 | Invalid placement / core activation |

All runtime SFX are mono PCM, preloaded DecompressOnLoad; existing streaming music is unchanged. Original MP3s are untouched. Runtime has 23 revision-116 WAVs; editable inventory has 42 entries including retained old sounds. Deleted original 07/08/09/11 are not restored; new UI sounds fill three missing bindings. Original Select gain remains 0.294.

## Validation and rollback
- GatlingThermalTests 7/7; WeaponTelemetryTests 4/4; ExpandedWeaponTests 32/32.
- GameAudioTests 10/10 unique tests, 9.58 s. Test API repeats result rows; summary and distinct test names count ten. First runner attempt stalled before RunStarted, then restart passed.
- Native installed.json records references, durations and exact thermal values; processing.json records signal processing. Before/ contains modified-file backups; original source recordings remain available.
- No subjective headphone audition or standalone build performance claim. Editor left stopped; session auto-tick disabled.
- Tools: tools/revise_audio116.py, tools/InstallAudio116Revision2.cs, tools/update_audio116_revision2_inventory.py. Avoid rerunning older installers because they contain superseded choices.
