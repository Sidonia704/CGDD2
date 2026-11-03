# Continuous combat / recovery / weapons / sound — 2026-11-03

Implemented approved plan plus V cadence, railgun rate and staggered missiles. Main handoff: ../PROJECT_CONTEXT.md.

## Player-visible result
- Continuous overlapping five-stage mission. Left HUD only1/5…5/5, no panel, ETA, contact count or wave controls. In-stage gaps are ordinary free flight. Enemy roster53 unchanged; caps10/3/1 and3 attacking persist.
- Central AI combat region and soft return; effective mission half extent2200 (full width4400), independent of3500m authored scenery. Map geometry stays at its established bottom offset.
- Shield absorption never resets hull repair. Each damaged/missing part independently waits its own6s actual damage/detachment delay. Connected support grows one layer at a time. External solids block completed reconstruction without deleting progress; probes shrink full integer cells, ignore ship/trigger colliders and use assembly/physics transforms.
- Projectile diameter unchanged. Existing per-shot hit identity prevents the same penetrating bolt from damaging a reconstructed slot twice.
- V switches projectile salvo/alternating, including press and charge release; see WEAPONS.md. HUD lightweight mode text, Settings rebindable.
- Missile staggering6 actual tubes/s per ship. No post-stall burst catch-up. Railgun1/2/3 rates1.50/1.25/1.00.
- Shared category multipliers remain0.60/0.25. New player-only1.10/0.85 yield0.66/0.2125 relative to raw weapon damage; enemy actual scaling remains unchanged. Module details use final player product.
- Eleven chirpy currently active cues replaced with short mechanical impulses; flare is low triple casing thump. Existing user engine/laser/block clips and crisp incoming alarm kept.
- Distant sun uses the actual key-light direction, separate from planet; no new expensive volumetrics, full-resolution world retained.

## Tune / rollback
- Settings/Game/SaturnRingWaves.asset: continuousStages, stageMinimumSeconds35, stageTargetSeconds135, stageOverlapEnemies3, combatAreaFraction.62, minimumArenaHalfExtent2200. Native backup and mission source before copies: ../ContinuousMission-2026-11-03/before/.
- Settings/Combat/CombatDamageTuning.asset: two global category knobs plus playerDirectDamageMultiplier/playerMissileDamageMultiplier.
- Settings/Modules/Railgun[1..3]Weapon.asset: shotsPerSecond. ShipWeapons.MissileLaunchesPerSecond controls launch flow6/s.
- Audio/environment details and before WAV/source/import snapshots: audio-environment/README.md.
- Repair before copies: before/FlightShipRuntime.cs.txt, before/ShipShieldSystem.cs.txt. Weapon/input/UI source and native backups: weapon-before/.
- Installers under tools/SessionNov03 were executed through Unity. Do not rerun old historical balance installers. Native assets were changed through Unity APIs, no YAML rewriting.
- Changes can be reverted independently by subsystem; do not blanket restore the project or overwrite player blueprints.

## Verification
- Initial broad EditMode573/573 pass126.14s, evidence edit-tests-initial.json.
- Final coordinate correction then ShieldMechanicsTests13/13 pass, including transformed assembly occupancy, repair under shield hits, independent timers, moving shields and overflow. repair-tests-final.json.
- First PlayMode start was interrupted by an erroneous duplicate tool invocation; that run has no verdict. It was cancelled and restarted once with persistent auto-tick. Record final run below.
- No15-minute human difficulty calibration or standalone benchmark is claimed.

- Broad PlayMode55:53pass/2fail. The shield-power fixture used pre-existing unscaled player recharge assumptions; updated to the installed player recovery modifier, focused rerun1/1 pass (shield-power-tests-final.json). The input fixture was consuming Editor/implicit updates and lacked focused-game routing in an unfocused Editor; isolated explicit Dynamic update and restored focus settings afterward.
- Actual V check additionally exposed GameInput.FindControl choosing an older keyboard while group number toggles used Keyboard.current. GameInput now resolves Keyboard/Mouse bindings against current devices. Actual synthetic V/group press, hold, menu exclusion and repeat-press integration rerun1/1 pass (input-tests-final.json). All55 unique runtime cases have passing coverage, not one final broad55/55 report.
- Native SaturnRing loaded and advanced471frames; actual stage1/5 and one enemy/pending6, free flight,2200 arena extent, old finite star renderer hidden, new key light aligned with sky direction. native-check.txt and native-hud.png. No new native runtime errors were reported in the checked Console interval. Play stopped, temporary auto-tick/background overrides restored.
- Latest source compiled without errors. Earlier interrupted/failed test reports are retained transparently as initial evidence. No long-form difficulty calibration or standalone performance result claimed.
