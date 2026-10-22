# Tunables

Generated from `@tune` headers by the menu `CGDD/Export Trace Docs`. Hand edits are overwritten on the next export.

| Script | Field | Type | Range | Default | Note |
| --- | --- | --- | --- | --- | --- |
| BattleTuning | outOfCombatDelay | float | 0+ | 1 | Seconds without hull damage before recovery. |
| BattleTuning | recoveryTick | float | 0.02-0.5 | 0.1 | Seconds between recovery evaluations. |
| BattleTuning | unlimitedRangePower | bool | false-true | true | Practice-only bypass for energy scarcity. |
| BattleTuning | lockConeDegrees | float | 1-80 | 35 | Target acquisition cone in degrees. |
| BattleTuning | lockRange | float | 1+ | 3000 | Maximum target acquisition distance in metres. |
| BattleTuning | botCruiseSpeedFraction | float | 0.1-1 | 0.65 | Cruise speed as a fraction of surviving ship capability. |
| BattleTuning | botAttackSpeedFraction | float | 0.1-1 | 0.45 | Attack speed as a fraction of surviving ship capability. |
| BattleTuning | botPilotDecisionSeconds | float | 0.05-0.5 | 0.1 | Opponent decision interval in seconds. |
| BattleTuning | botReactionSeconds | float | 0+ | 1.2 | Delay before an opponent starts firing. |
| BattleTuning | botFireBurstSeconds | float | 0.05+ | 0.3 | Duration of a firing burst. |
| BattleTuning | botFirePauseSeconds | float | 0+ | 1.8 | Pause between firing bursts. |
| BattleTuning | botRespawnSeconds | float | 0+ | 3 | Delay before an opponent respawns. |
| BattleTuning | playerRespawnSeconds | float | 0+ | 3 | Delay before the player respawns in practice. |
| BattleTuning | playerSpawnProtectionSeconds | float | 0+ | 4 | Practice respawn invulnerability duration. |
| FlightTuning | maneuverThrustFraction | float | 0-0.8 | 0.2 | Share of total thrust assigned to manoeuvre assistance. |
| FlightTuning | maxSpeed | float | 0+ | 250 | Soft flight speed ceiling in metres per second. |
| FlightTuning | maxLinearAcceleration | float | 0+ | 100 | Soft linear acceleration ceiling. |
| FlightTuning | boostPowerWatts | float | 0+ | 100 | Requested power while holding boost. |
| FlightTuning | boostMultiplier | float | 1+ | 1.5 | Boost multiplier at full allocated power. |
| FlightTuning | mouseDegreesPerPixel | float | 0+ | 0.085 | Virtual-stick sensitivity before the settings multiplier. |
| FlightTuning | maxAimOffsetDegrees | float | 5-45 | 25 | Maximum persistent virtual-stick offset. |
| FlightTuning | aimInputDeadZone | float | 0-0.4 | 0.035 | Dead zone as a fraction of stick radius. |
| FlightTuning | aimResponseExponent | float | 0.5-3 | 1.6 | Response curve around the stick centre. |
| FlightTuning | aimInputSmoothing | float | 0-0.15 | 0.035 | Steering smoothing duration. |
| FlightTuning | turnResponseTime | float | 0.05-0.5 | 0.14 | Turn response duration. |
| FlightTuning | arenaHalfExtent | float | 0+ | 1500 | Half-width of the test range in metres. |
| MissionDefinition | unpricedPartCost | float | 0.01+ | 10 | Optional provisional cost for an otherwise zero-cost part. |
| MissionDefinition | relaxedBudgetMultiplier | float | 0+ | 1.25 | Budget multiplier for relaxed difficulty. |
| MissionDefinition | standardBudgetMultiplier | float | 0+ | 1 | Budget multiplier for standard difficulty. |
| MissionDefinition | expertBudgetMultiplier | float | 0+ | 0.75 | Budget multiplier for expert difficulty. |
| ModuleDefinition | cost | float | 0+ | 0 | Deployment cost; implicit field default is zero. |
| ModuleDefinition | massKg | float | 0+ | 300 | Part mass in kilograms. |
| ModuleDefinition | maxHp | float | 0.01+ | 60 | Independent part durability. |
| ModuleDefinition | recoverySeconds | float | 0.01+ | 3 | Time needed to reconstruct a missing part. |
| ModuleDefinition | thrustNewtons | float | 0+ | 0 | Nominal thrust for a propulsion part. |
| ModuleDefinition | generationWatts | float | 0+ | 0 | Continuous shared power generation. |
| ModuleDefinition | storageJoules | float | 0+ | 0 | Shared stored energy capacity. |
| ModuleDefinition | thrusterPowerWatts | float | 0+ | 0 | Power request associated with propulsion. |
| ModuleDefinition | shieldPowerWatts | float | 0+ | 0 | Power request associated with shield recovery. |
| ModuleDefinition | shieldCapacity | float | 0+ | 0 | Maximum shield strength per generator. |
| ModuleDefinition | shieldRadius | float | 0.1+ | 8 | Shield field radius in metres. |
| ModuleDefinition | shieldRecoveryDelay | float | 0+ | 2 | Delay before a shield can recover. |
| ModuleDefinition | shieldRecoveryPerSecond | float | 0+ | 50 | Shield recovery per second. |
| WeaponDefinition | shotsPerSecond | float | 0.01+ | 5 | Nominal shot cadence before energy allocation. |
| WeaponDefinition | damage | float | 0.01+ | 40 | Damage of one complete projectile. |
| WeaponDefinition | projectileSpeed | float | 0.01+ | 160 | Projectile speed used for travel and interception. |
| WeaponDefinition | projectileDiameter | float | 0.01+ | 1 | Physical projectile hit-volume diameter. |
| WeaponDefinition | penetrationDepth | float | 0.01+ | 2 | Maximum hull penetration depth. |
| WeaponDefinition | secondLayerDamageMultiplier | float | 0-1 | 0.5 | Damage fraction for the second hit layer. |
| WeaponDefinition | range | float | 1+ | 800 | Maximum projectile travel distance. |
| WeaponDefinition | sustainedPowerWatts | float | 0+ | 50 | Shared power requested per firing weapon. |
| WeaponDefinition | chargeSeconds | float | 0.01+ | 1 | Charge time for charge-release activation. |

