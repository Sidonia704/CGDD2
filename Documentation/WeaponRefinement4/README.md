# Weapon refinement 4 — 2026-11-06

- Gatling full-power continuous fire now overheats at 8 seconds; full cooling remains6 seconds. Gross heat is `1/8 + 1/6` per second, preserving simultaneous passive cooling. Three-phase audio follows actual firing and has no duration-based coupling.
- Native weapon identities remain recognized during module-database initialization gaps; normalization/upgrade can no longer silently discard Gun/Rail/Sword/Missile group assignments during saving or scene handoff. Previously lost intent cannot be inferred retroactively.
- Direct weapon convergence uses the actual forward firing reticle's first hostile hull hit, not the steering cursor or nearest target. Each player muzzle may converge up to4 degrees; no prediction, lock requirement or enemy buff. Scenery/own-hull blockage and normal projectile collision remain. At extreme close range/wide mount offsets,4 degrees may be insufficient; this is deliberately light parallax assistance rather than guaranteed hits at every distance.
- All native rail sizes share RailShared.wav, decoded from the complete new116/电磁炮.mp3 (actual source folder assetsForCodex/116). All33408 samples at44100Hz retained,0.757551s; no crop, fades, stretch or tail removal. Constant gain only. Absolute PCM main peak0.202494s sets windup; fixed pitch1 preserves alignment. Original MP3 unchanged; replaced old runtime rail clips backed up under Before/.
- Editable audio folder now has one03_电磁炮_大小共用.wav instead of the former large/small variants. Mapping updated.

Evidence: audio.json, installed.json and focused test result JSON files in this folder. No subjective headphone audition or standalone build assessment.

Validation complete:39 EditMode checks (7thermal+9aim+15database+8group data),10 GameAudio PlayMode tests passed (12.04s). Astra scope review approved with the4-degree limitation. Compilation clean, native settings read back, Editor stopped and session auto-tick disabled.
