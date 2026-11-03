# Final balance, round 1 — 2026-11-03

Current instruction supersedes the next-round plan below: the user cancelled round2 calibration and disabled gameplay map destruction. See Documentation/PROJECT_CONTEXT.md for the active architecture/art handoff. This report remains historical evidence of round1.

## Installed configuration

- Exact approved player-library t1–t7 snapshots are installed into needle/viper/wasp/bastion/harrier/halberd/citadel. Original player files are read-only. The capital alternate aliases approved t7; the obsolete siege source remains available but unused.
- Standard deployment budget: 30,000. Difficulty budget multipliers still apply. t4 costs9,421; three cost28,263.
- Five waves:7/10/12/11/13 enemies, total53 =40 small/12 large/1 capital. Exact archetype counts19/8/13/5/4/3/1. Concurrent caps10 alive/3 large/1 capital,3 attack opportunities. Cleared fields advance delayed reinforcements after a3-second visible notice; intermissions retain their normal recovery/advance operation.

## Damage adjustment interface

Unity menu: `Starframe → 平衡参数 → 伤害倍率`.

Asset: `Assets/_Starframe/Settings/Combat/CombatDamageTuning.asset`.

| Field | Installed | Meaning |
|---|---:|---|
| nonMissileDamageMultiplier |0.60|Projectiles and beams, relative to authored weapon damage|
| missileDamageMultiplier |0.25|Total missile volley damage, independent of non-missile scale|

The same asset is referenced by BattleTuning and FlightTuning, so combat and assembly weapon descriptions agree. Raw WeaponDefinition.damage values remain intact. Each projectile/missile snapshots scaled damage at launch; beam pulses resolve it when fired. Twin missile racks divide one volley budget between their missiles. Piercing and splash do not reapply the multiplier.

`Assets/_Starframe/Settings/Flight/BattleTuning.asset` holds `enemyWeaponDamageMultiplier=0.50` and `missileBlastBudgetMultiplier=1.50`. All nonzero attacker teams use the enemy factor; player team0 uses1. Thus identical direct weapons deal0.60 of authored damage for players and0.30 for enemies. Missile player volleys2800/4800/8000; enemy1400/2400/4000. Blast aggregate is bounded to1.5 times individual-missile damage across structural contacts.

A charged shield intercepts its entire incoming missile even if that hit empties the field. Its ship receives no residual blast from that missile. Later missiles check live charge and can hit the exposed hull; normal direct-fire overflow is unchanged. Multiple overlapping fields do not all pay for a single intercepted missile.

## Missile counterplay and audio

- Seekers have finite turn, angular acceleration, visibility grace and lifetime. Outside the seeker cone they brake rather than steer toward an invisible target; losing tracking is permanent. M1/2/3 turn110/95/80deg/s, angular acceleration360/300/240deg/s², cone60/55/50deg, loss grace0.3s. Speed240/245/250m/s and existing powered/flight-range limits remain.
- AI forecasts the real installed flare mount, inherited motion, burst lifetime/radius and missile cone. It reacts before a fast incoming missile crosses the deployment window. Real upper clearance and10-second per-launcher cooldowns remain; follow-up bursts are spaced. Actual240/245/250m/s deployment/diversion tests include live projectiles and emitted sparks, not only helper calls. Probability remains0.5 per missile/burst pair.
- Incoming cue is a dry2kHz60ms mono pip with short cosine fades; gain0.48, no random pitch. One of16 total effect voices is reserved so alarms neither stack nor disappear under gunfire. Cadence0.90→0.12s follows distance or time-to-impact urgency; pause/focus/death/scene cleanup stops it. The independent enemy-lock cue remains.
- Fixed an existing mission audio discovery gap: SaturnRing now enters delayed combat-world binding through its FlightRangeBootstrap, like the practice scene.

## Evidence and limits

`three-t4-firepower.json`: three exact t4 ships with player faction scaling and finite independent power, firing through native collision/weapons/power/recovery APIs at600m. First effective hit to core destruction: t4≈2s, t6≈3s, t7≈3s. Perfect stationary aim and continuous fire are an upper-bound baseline, not a human duel or15-minute mission timing result. No physical wreck world is installed in that isolated fixture; actual wreck persistence is checked in the native mission probe.

`incoming-beep-waveform.json`:48kHz mono PCM,60ms,2kHz,0 clipped samples.

Test reports and native lifecycle results are stored beside this file. The initial full EditMode run found stale generated-fleet nozzle/armament assumptions and one disconnected added test fixture. Fleet acceptance now verifies the actual per-nozzle masks and resulting six-axis flight, without altering the approved designs. The flare fixture has real connected supports.

## Original round 2 plan (cancelled by the user)

Calibrate normal-aim play against the fixed3×t4 firepower reference, run unaccelerated five-wave pacing, examine rail/missile burst deaths and recovery windows, then profile repeated destruction plus dense-field flight. Preserve approved blueprint geometry, stable independent flight axes, existing lock input, full-resolution aim projection, sparse map damage and persistent destructible wreckage. Do not rerun old asset installers that restore historical weapon prices/damage or generated enemies.

Before snapshots are in `before/`; approved sources in `approved-blueprints/`; explicit native installation and isolated verification helpers in `tools/FinalBalanceRound1/`. Subsequent student edits to player-library t1–t7 require explicit reimport; installed encounter snapshots do not silently change while editing the library.

## Final verification

- Recompile: completed, no compilation failure.
- Full EditMode561 cases: latest broad run560 passed; the remaining disconnected fixture was corrected and its full8-case CombatBalanceWiringTests group passed. All561 unique cases have passing coverage; the earlier failed runs remain in the evidence.
- Full PlayMode54/54 passed, including new reinforcement notice, mission audio routing and reserved alarm voice tests.
- Actual SaturnRing:30 real seconds /1995 advancing frames,120 AI shots, native enemy automatically deployed a real flare against controlled incoming missiles. Audio and damage asset binding verified.
- Native accelerated five-wave lifecycle: all53 approved ships, correct40/12/1 and19/8/13/5/4/3/1 counts, caps10/3/1,8533 surviving wreck parts at victory. Retired-source wrecks retain collider mesh ownership and take swept projectile damage. Victory/results/reentry/core-loss defeat pass; mission does not use practice respawn. Original user files byte-identical.
- Actual menu `Starframe/平衡参数/伤害倍率` opens the intended asset. Owned Play stopped, auto-tick disabled, original background setting restored. Final Editor Console:0 errors/0 warnings.
- Editor forced-kill diagnostic only: frame median18.29ms/p9534.40ms/max95.26ms, max enemy creation46.60ms. This is neither a standalone Player benchmark nor normal combat pacing. A full unaccelerated human15-minute run was not performed; the user subsequently cancelled round2.
