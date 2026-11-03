# How the challenge changes

Look first at whether win and loss change, then at whether the player's verb changes. If neither changes, it is the same game, just tighter or looser.

| Change | Win/loss changed | Verb changed | Data | Script.field |
| --- | --- | --- | --- | --- |
| Baseline: assemble a connected ship, then pilot, aim and fire in the practice range | Baseline: no terminal victory; core destruction triggers a practice respawn | Build, test, diagnose and revise | playerRespawnSeconds=3; botRespawnSeconds=3 | BattleTuning.playerRespawnSeconds; BattleTuning.botRespawnSeconds |
| Baseline: hull damage interrupts recovery; surviving support enables reconstruction | Baseline, unchanged | Keep flying and find a recovery opportunity | outOfCombatDelay=1; recoveryTick=0.1 | BattleTuning.outOfCombatDelay; BattleTuning.recoveryTick |
| Baseline: opponents wait, fire short bursts and pause between bursts | Baseline, unchanged | Manoeuvre and time attacks | botReactionSeconds=1.2; botFireBurstSeconds=0.3; botFirePauseSeconds=1.8 | BattleTuning.botReactionSeconds; BattleTuning.botFireBurstSeconds; BattleTuning.botFirePauseSeconds |
| Baseline: boost requests shared power; practice can bypass scarcity | Baseline, unchanged | Choose when to boost and when to fire | boostPowerWatts=100; boostMultiplier=1.5; unlimitedRangePower=true | FlightTuning.boostPowerWatts; FlightTuning.boostMultiplier; BattleTuning.unlimitedRangePower |

Recorded on 2026-10-22 from the current scripts. These rows document an existing baseline, not a new gameplay change. Numbers are C# field defaults, not inspected ScriptableObject asset overrides. Mission validation and result reporting exist, but a completed wave-based mission is not claimed here.


## 2026-10-25 — Expanded component library

The student clarified the changed operation: missiles require a continuous two-second lock before launch; other weapons fire directly with the existing controls. No new victory or defeat rule was requested. The practice range remains open-ended with core-destruction respawns; mission wave outcomes are still outside this change.

| Change | Win/loss changed | Verb changed | Data | Script.field |
| --- | --- | --- | --- | --- |
| Guided missiles acquire the same live target before firing, then automatically reload using shared power | Existing practice outcomes retained | Acquire and hold a target lock, choose a launch window, wait for reload | lock=2 s; default reload=4 s; three production tiers have 4/5.5/7 s reload | WeaponDefinition.missileLockSeconds; WeaponDefinition.reloadSeconds |
| Ballistic guns, rapid lasers, penetrating railguns and short-range beam swords broaden weapon choice | Existing practice outcomes retained | Direct aim/fire retained; short beam reach changes approach distance | Delivery is Projectile / GuidedMissile / Beam; production numbers are recorded in Documentation/ComponentLibrary-2026-10-25/Catalog.md | WeaponDefinition.delivery; WeaponDefinition.damage; WeaponDefinition.range |
| Real planar contact replaces support from empty occupied faces; weapons mount through their underside and cannot support other weapons; armor connects only underneath | Existing practice outcomes retained | Assemble, rotate and revise retained; unsupported floating stacks are rejected | Integer occupancy is retained; real mesh patches carry structural connections | BlockModules.FacesContact; BlockPlacement.TryOnFace |
| Passive armor adds durability and larger power/shield/propulsion components change mass and energy tradeoffs | Existing practice outcomes retained | Build, test, diagnose and revise retained | Four armor sizes and larger component tiers; original ten component values retained | ModuleDefinition.maxHp; ModuleDefinition.massKg; ModuleDefinition.storageJoules; ModuleDefinition.shieldCapacity |

The two-second lock and default fields above follow the script defaults. Production asset balance is listed separately in the catalog so field defaults are not confused with configured tier values.

## 2026-11-02 — Countermeasures, signatures and finite missile seekers

The student confirmed the proposed combat changes, including R for countermeasures and F for target lock. Victory, core death, surviving wreckage and practice respawn rules stay unchanged. The new player operation is choosing when to deploy one ready launcher; discovery and missile evasion change the available engagement windows.

| Change | Win/loss changed | Verb changed | Data | Script.field |
| --- | --- | --- | --- | --- |
| One clear, ready launcher emits a shared decoy burst per R press; launchers rotate independently | No | Deploy countermeasures and time repeated bursts | salvo=12; cooldown=10 s; decoy chance=0.45 per missile/burst | FlareDefinition.burstCount; FlareDefinition.cooldownSeconds; FlareDefinition.decoyChance |
| Surviving hull size determines symmetric discovery distance; F selects a visible red marker and the lock survives leaving the view | No | Aim at a detected contact before locking; steer toward the offscreen indicator | reference radius=10 m; reference range=3000 m; clamp=1000..6000 m; exit=1.1x for 0.5 s | BattleTuning.sensorReferenceRadius; BattleTuning.lockRange; BattleTuning.sensorMinimumRange; BattleTuning.sensorMaximumRange; BattleTuning.sensorExitRangeMultiplier; BattleTuning.sensorLossDelay |
| Missiles have bounded turn acceleration and lose guidance after timeout, sustained cone loss or an expired decoy; scenery still intercepts them | No | Manoeuvre, use cover or deploy flares to evade | angular acceleration=180 deg/s²; guidance=10 s; half-cone=65 deg; cone-loss delay=0.35 s | WeaponDefinition.missileTurnAcceleration; WeaponDefinition.missileGuidanceSeconds; WeaponDefinition.missileSeekerConeDegrees; WeaponDefinition.missileSeekerLossSeconds |

Numbers above are script defaults. Existing production damage, speed, reload and flight balance are preserved; per-weapon and module assets remain authoritative.

## 2026-11-02 — Projectile contact and structural recovery

The student confirmed “保持现状”: victory/defeat conditions and player operations remain unchanged. The practice range still uses core death and delayed respawn, with surviving parts retained as physical, destructible wreckage.

| Change | Win/loss changed | Verb changed | Data | Script.field |
| --- | --- | --- | --- | --- |
| Hull and shield damage both interrupt structural healing/reconstruction | No | No | quiet interval 20 s; module recovery defaults to 20 s, installed per-module values 25–67.5 s | BattleTuning.outOfCombatDelay; ModuleDefinition.recoverySeconds; FlightShipRuntime.NotifyIncomingDamage |
| Slower practice respawn | No | No | bot 6 s; player 5 s | BattleTuning.botRespawnSeconds; BattleTuning.playerRespawnSeconds |
| Faster swept shots and damped finite missile pursuit | No | No | speeds and physical diameters are per-weapon asset parameters; hard turn limits, seeker expiry, flare and cover remain | WeaponDefinition.projectileSpeed; WeaponDefinition.projectileDiameter; MissileGuidance.Step |
## 2026-11-02 — Revised arcade damage and recovery cadence

The student explicitly superseded the earlier long-duel pacing: saturated incoming fire should down a comparable ship in roughly4s; after6s without effective damage it should recover quickly. Core destruction, practice respawn outcomes and player actions remain as previously confirmed. F retains lock/unlock; no new player verb or mission outcome is added.

| Change | Win/loss changed | Verb changed | Data | Script.field |
| --- | --- | --- | --- | --- |
| Much stronger shared weapon profiles | No | No | Installed WeaponDefinition damage values are listed in Documentation/CombatDamageReentry-2026-11-02/README.md; saturation reference windows3.30/4.14/4.18s | WeaponDefinition.damage; WeaponDefinition.missileLockSeconds |
| Shared hull/shield quiet gate | No | No |6s since the last hull or shield damage, including empty shield fields | BattleTuning.outOfCombatDelay; ModuleDefinition.shieldRecoveryDelay; FlightShipRuntime.LastDamageTime |
| Faster reconstruction and recharge | No | No | Full part restoration4/6/8s by type; native shields25% capacity/s with sufficient shared power | ModuleDefinition.recoverySeconds; ModuleDefinition.shieldRecoveryPerSecond |
| Reentry input rearming | No | No | Flight readiness releases menu suspension; held keys prime before the next press | RangeLoadingOverlay.Complete; TargetLockSystem.SampleLockKey |

## 2026-11-03 — Approved five-wave arcade mission

The student confirmed the proposed mission framework before implementation. Formal mission victory is clearing all five finite waves; player core destruction is defeat, with a return to the operation report and redeployment. Practice keeps automatic respawning. Player flight, firing, group toggles and countermeasures keep their controls; the added preparation operation starts the next wave early by button or Enter. The deployment ceiling is10000 cost. Enemy composition is50 small/15 large/2 capital, seven archetypes plus a capital fit variant. Three concurrent attack opportunities, finite batch scheduling, recovery breaks and role-specific flight shape the challenge. Fifteen minutes is the first-playtest pacing target, not a forced timer. Surviving detached parts remain destructible wrecks after deaths and no longer depend on the retired ship's meshes. Parameter and verification entry points: Documentation/ArcadeMission-2026-11-02/README.md.

## 2026-11-03 — Direct damage and graphical flight instruments

The student explicitly confirmed “保持现有胜负条件和操作”. Practice core death/respawn, mission outcomes and all player controls stay unchanged.

| Change | Win/loss changed | Verb changed | Data | Script.field |
| --- | --- | --- | --- | --- |
| Direct-fire damage doubled again | No | No | Eleven installed non-guided profiles; missiles unchanged | WeaponDefinition.damage |
| Floating graphical flight telemetry | No | No | Speed tape, segmented shield/energy meters, own/target glass monitors and independent critical-instrument colour | RangeHUD; FlightInstrumentGraphic; UiTheme.combatInstrument |
| Bounded collision preparation and lighter damaged scenery exteriors | No | No | 24 cold boxes/frame, 1.5ms soft budget; exterior damage threshold128 cells for coarse budgeted scenery | SpaceMapRuntime.colliderPreparationPerFrame; SpaceMapRuntime.colliderPreparationMilliseconds; SpaceMapRuntime.lightweightDamageThreshold |
| Cached wreck rendering | No | No | All surviving parts retain health/collision and remain until destroyed | DebrisInstanceRenderer; DebrisWorld |

The same-script extreme Editor approach still has short frame peaks; it is not a standalone performance guarantee. Evidence and native tuning paths: Documentation/CombatHud-2026-11-03/README.md.

## 2026-11-03 — Lock input reliability and editable enemy copies

F remains lock/unlock. Victory/defeat rules and player verbs are unchanged. Input pulses are deduplicated by InputSystem update; a fresh post-focus press is accepted, while held blocked-state input stays suppressed. A queued F tries the steering cursor, then the actual forward crosshair, using the existing discovery and acquisition radius. This repairs input/acquisition reliability rather than changing sensor balance. Seven primary enemy blueprints are exported as t1–t7 for student editing; encounter assets remain the current source until the final revisions are applied. Verification:18 focused cases plus two actual range entries with20 complete taps each. Documentation/LockRepair-2026-11-03/README.md.
