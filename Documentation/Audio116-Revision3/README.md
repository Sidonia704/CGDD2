# Audio 116 revision 3

## Changes
- Gatling uses recorded start (0.27 s), steady loop (0.965 s) and end (0.47 s) clips. The earlier loop mistakenly included the initial transient and decaying end; those no longer recur within sustained fire.
- DSP-scheduled start-to-loop boundary; the steady loop has a 35 ms periodic crossfade and no baked fade-out. Dedicated own/enemy paired sources remain within the bounded voice budget and cannot be stolen by weapons or warnings. State comes from actual powered, enabled, alive, non-overheated firing, independent of the thermal duration.
- Gatling heat remains 12 seconds of full-power held fire to overheat, 6 seconds to cool from full to zero. Release, lack of power and overheat trigger the audio end phase. Holding through cooling restarts audio when the weapon really resumes.
- RailLarge / RailSmallA / RailSmallB primary sample peaks aligned to 80 ms. Native rail profiles use 80 ms accepted-shot windup and fixed pitch 1. Audio starts on windup; projectile and muzzle effect emit after windup. No duplicate sound at discharge. Cancelled prefire sound stops when no same-category pending mount remains; already fired tails survive. Unity simulation/audio device buffering can still impose normal sub-frame/output latency; this is not sample-accurate hardware synchronization.
- Sword1Weapon damage 2250 → 4500; Sword2Weapon 4050 → 8100. Shared profiles apply to both sides; other damage modifiers unchanged.

## Files
`processing.json`: measured clip lengths and peak alignment, loop RMS window analysis.
`installed.json`: native Unity asset readback.
`Before/`: backups of changed native settings and audio.
Editable copies and mapping are refreshed in 音效替换, including 20_机枪启动.wav and 21_机枪停火.wav. 06_机枪.wav is now only the steady loop.

## Verification
37 ExpandedWeaponTests and 7 GatlingThermalTests passed. All 10 GameAudioTests passed in 12.03 seconds after correcting the test's double-counted cooling remainder. Tests cover reserved continuous playback under voice pressure and restart after cooling with input held. Editor stopped; session auto-tick disabled. No subjective headphone audition or standalone build assessment is claimed.

