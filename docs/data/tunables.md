# Tunables

Generated from `@tune` headers by the menu `CGDD/Export Trace Docs`. Hand edits are overwritten on the next export.

| Script | Field | Type | Range | Default | Note |
| --- | --- | --- | --- | --- | --- |
| ArcadeMissionDefinition | maximumAlive | int | 1-32 | 10 | Maximum simultaneously active enemy ships. |
| ArcadeMissionDefinition | maximumLarge | int | 1-8 | 3 | Maximum simultaneously active large ships, excluding capital ships. |
| ArcadeMissionDefinition | maximumAttackers | int | 1-8 | 3 | Ships allowed to commit to the player at one time. |
| ArcadeMissionDefinition | stageMinimumSeconds | float | 0-300 | 35 | Minimum occupied-stage duration before low-pressure overlap. |
| ArcadeMissionDefinition | stageTargetSeconds | float | 1-600 | 135 | Advance a deployed stage after this duration despite surviving enemies. |
| ArcadeMissionDefinition | combatAreaFraction | float | 0.2-0.85 | 0.62 | Central AI soft return radius relative to the flight boundary. |
| ArcadeMissionDefinition | minimumArenaHalfExtent | float | 0-100000 | 2200 | Mission minimum flight boundary half-width. |
| ArcadeMissionDefinition | intermissionSeconds | float | 0-120 | 20 | Legacy preparation when continuous stages are disabled. |
| ArcadeMissionDefinition | emptyFieldReinforcementSeconds | float | 0-10 | 3 | Notice before advancing a later batch when the current field is cleared. |
| BattleTuning | outOfCombatDelay | float | 0+ | 6 | Seconds without hull or shield damage before structural recovery. |
| BattleTuning | recoveryTick | float | 0.02-0.5 | 0.1 | Seconds between recovery evaluations. |
| BattleTuning | unlimitedRangePower | bool | false-true | true | Practice-only bypass for energy scarcity. |
| BattleTuning | lockConeDegrees | float | 1-80 | 35 | AI initial lock acquisition half-cone in degrees. |
| BattleTuning | lockRange | float | 1+ | 3000 | Discovery distance for a target at the reference signature radius, in metres. |
| BattleTuning | sensorReferenceRadius | float | 0.01+ | 10 | Surviving hull radius corresponding to lockRange, in metres. |
| BattleTuning | sensorMinimumRange | float | 1+ | 1000 | Minimum signature-based discovery range, in metres. |
| BattleTuning | sensorMaximumRange | float | 1+ | 6000 | Maximum signature-based discovery range, in metres. |
| BattleTuning | sensorExitRangeMultiplier | float | 1+ | 1.1 | Discovery exit radius as a multiple of the entry radius. |
| BattleTuning | sensorLossDelay | float | 0+ | 0.5 | Continuous seconds beyond the exit radius before losing contact. |
| BattleTuning | sensorAcquireRadiusPixels | float | 1+ | 80 | Maximum initial lock marker distance from the virtual aim cursor at 1080p. |
| BattleTuning | enemyLockAcquireSeconds | float | 0+ | 0.3 | Continuous initial AI aim alignment required for a target lock. |
| BattleTuning | botCruiseSpeedFraction | float | 0.1-1 | 0.65 | Cruise speed as a fraction of surviving ship capability. |
| BattleTuning | botAttackSpeedFraction | float | 0.1-1 | 0.45 | Attack speed as a fraction of surviving ship capability. |
| BattleTuning | botPilotDecisionSeconds | float | 0.05-0.5 | 0.1 | Opponent decision interval in seconds. |
| BattleTuning | botReactionSeconds | float | 0+ | 1.2 | Delay before an opponent fires or reacts to an incoming missile with countermeasures. |
| BattleTuning | botFireBurstSeconds | float | 0.05+ | 0.3 | Duration of a firing burst. |
| BattleTuning | botFirePauseSeconds | float | 0+ | 1.8 | Pause between firing bursts. |
| BattleTuning | botRespawnSeconds | float | 0+ | 6 | Delay before an opponent respawns. |
| BattleTuning | playerRespawnSeconds | float | 0+ | 5 | Delay before the player respawns in practice. |
| BattleTuning | playerSpawnProtectionSeconds | float | 0+ | 4 | Practice respawn invulnerability duration. |
| BotBehaviorProfile | preferredRange | float | 40-3000 | 450 | Desired shooting distance in metres, limited by the selected weapon and ship clearance. |
| BotBehaviorProfile | velocityResponseSeconds | float | 0.05-2 | 0.45 | Continuous desired-velocity response; actual acceleration remains engine-limited. |
| BotBehaviorProfile | steeringResponseSeconds | float | 0.02-1 | 0.16 | Continuous commanded sightline response, in seconds. |
| BotBehaviorProfile | weaponHoldSeconds | float | 0.1-10 | 1.4 | Minimum duration to retain a surviving aim weapon before reconsidering the battery. |
| BotBehaviorProfile | terrainProbeSeconds | float | 0.05-1 | 0.18 | Interval between bounded non-allocating terrain probes. |
| CombatDamageTuning | nonMissileDamageMultiplier | float | 0+ | 0.6 | Global damage scale for projectile and beam weapons. |
| CombatDamageTuning | missileDamageMultiplier | float | 0+ | 0.25 | Global missile volley damage scale before faction scaling. |
| CombatDamageTuning | globalDamageMultiplier | float | 0+ | 1 | Debug multiplier for every weapon and faction, composed once with category scaling. |
| DebrisWorld | physicsBudgetInterval | float | 0.05-2 | 0.5 | Seconds between whole-island distance evaluations against living ships. |
| DebrisWorld | farPhysicsDistance | float | 350+ | 800 | Distance from every living ship before disabling speculative CCD and render interpolation. |
| DebrisWorld | nearPhysicsDistance | float | 1+ | 350 | Distance to any living ship that restores speculative CCD and interpolation, expanded for closing speed. |
| DebrisWorld | lifetimeSeconds | float | 1+ | 90 | Wreck lifetime after detachment; splitting never extends the deadline. |
| FlareDefinition | burstCount | int | 1+ | 12 | Sparks in one shared decoy burst; each burst is one lure opportunity per missile. |
| FlareDefinition | cooldownSeconds | float | 0+ | 10 | Independent cooldown per surviving launcher. |
| FlareDefinition | spreadHalfAngleDegrees | float | 0-89 | 22 | Half angle about the launcher's rotated local up direction. |
| FlareDefinition | emissionSpeed | float | 0+ | 24 | Outward speed added to the launching ship's velocity in metres per second. |
| FlareDefinition | lifetime | float | 0.01+ | 3 | Burst lifetime in seconds. |
| FlareDefinition | decoyChance | float | 0-1 | 0.45 | One decoy roll per eligible missile and burst. |
| FlareDefinition | decoyRadius | float | 0+ | 150 | Maximum missile distance from an eligible decoy burst in metres. |
| FlareDefinition | sparkSize | float | 0.01+ | 0.22 | Initial yellow voxel side length in metres before particle scaling. |
| FlareDefinition | glowIntensity | float | 0+ | 5 | Yellow voxel glow intensity. |
| FlightRangeBootstrap | sandboxArenaHalfExtent | float | 0-100000 | 0 | Optional half-width used only while the referenced sandbox spawn is active; zero keeps the existing flight tuning. |
| FlightTuning | maneuverThrustFraction | float | 0-0.8 | 0.2 | Legacy bot assistance summary fraction; does not set player propulsion allocation. |
| FlightTuning | maxSpeed | float | 0+ | 250 | Soft flight speed ceiling in metres per second. |
| FlightTuning | thrustMultiplier | float | 0+ | 1 | Global multiplier of all module thrust without changing native module stats. |
| FlightTuning | blockMassMultiplier | float | 0.01+ | 1 | Global multiplier of all module mass, including center of mass and inertia. |
| FlightTuning | heavyMassThresholdKg | float | 1+ | 100000 | Mass above which extra speed and mobility penalties start. |
| FlightTuning | heavySpeedExponent | float | 0+ | 0.65 | Exponent of the inverse-mass speed limit curve. |
| FlightTuning | heavyManeuverExponent | float | 0+ | 0.35 | Exponent of the inverse-mass acceleration and turning curve. |
| FlightTuning | maxLinearAcceleration | float | 0+ | 100 | Soft ceiling applied to each independent translation axis. |
| FlightTuning | angularAccelerationGain | float | 0+ | 10 | Legacy serialized compatibility setting; unused by the current flight controller. |
| FlightTuning | angularAuthorityReferenceAcceleration | float | 0.01+ | 10 | Legacy serialized compatibility setting; unused by the current flight controller. |
| FlightTuning | angularAuthorityExponent | float | 0.5-2 | 1.1 | Legacy serialized compatibility setting; unused by the current flight controller. |
| FlightTuning | maxAngularAcceleration | float | 0+ | 360 | Soft angular acceleration ceiling in degrees per second squared. |
| FlightTuning | angularRateGain | float | 0+ | 4 | Gain for deriving pitch and yaw rate from calculated angular acceleration. |
| FlightTuning | maxAngularRate | float | 0+ | 150 | Soft pitch and yaw rate ceiling in degrees per second. |
| FlightTuning | rollRateGain | float | 0+ | 5 | Gain for deriving roll rate from calculated angular acceleration. |
| FlightTuning | maxRollRate | float | 0+ | 180 | Soft roll rate ceiling in degrees per second. |
| FlightTuning | rollBlendTime | float | 0+ | 0.25 | Roll input response duration in seconds. |
| FlightTuning | minimumTurnAuthority | float | 0-1 | 0.75 | Legacy serialized compatibility setting; unused by the current flight controller. |
| FlightTuning | boostPowerWatts | float | 0+ | 100 | Requested power while holding boost. |
| FlightTuning | boostPowerCostMultiplier | float | 1+ | 4 | Multiplier of authored boost power demand; normal propulsion requests one eighth of this boost demand. |
| FlightTuning | boostSpeedLimitMultiplier | float | 1+ | 2 | Speed ceiling during fully powered boost, relative to the ship's mass-adjusted normal ceiling. |
| FlightTuning | overspeedRecoveryDeceleration | float | 0.01+ | 40 | Speed removed per second above the current ceiling, even while continuing to thrust. |
| FlightTuning | boostMultiplier | float | 1+ | 1.5 | Boost multiplier at full allocated power. |
| FlightTuning | mouseDegreesPerPixel | float | 0+ | 0.085 | Virtual-stick sensitivity before the settings multiplier. |
| FlightTuning | maxAimOffsetDegrees | float | 5-45 | 25 | Maximum persistent virtual-stick offset. |
| FlightTuning | aimInputDeadZone | float | 0-0.4 | 0.035 | Dead zone as a fraction of stick radius. |
| FlightTuning | aimResponseExponent | float | 0.5-3 | 1.6 | Response curve around the stick centre. |
| FlightTuning | aimInputSmoothing | float | 0-0.15 | 0.035 | Steering smoothing duration. |
| FlightTuning | turnResponseTime | float | 0.05-0.5 | 0.14 | Turn response duration. |
| FlightTuning | arenaHalfExtent | float | 0+ | 1500 | Half-width of the test range in metres. |
| FlightTuning | gridWallSpacing | float | 1+ | 600 | Separation between spatial reference planes in metres. |
| FlightTuning | gridCellSize | float | 0.1+ | 12 | Small reference-grid cell spacing in metres. |
| FlightTuning | gridOpacity | float | 0-1 | 0.42 | Reference-grid opacity; boundary warning keeps its own visibility. |
| GameAudioTuning | criticalHullFraction | float | 0-1 | 0.25 | Hull fraction triggering the shared health/shield warning |
| GameAudioTuning | criticalRepeatSeconds | float | 1+ | 10 | Seconds between shared health/shield warnings |
| GameAudioTuning | missileWarningFarDistance | float | 1-5000 | 600 | Distance for the slowest incoming missile pulse |
| GameAudioTuning | missileWarningNearDistance | float | 1-500 | 40 | Distance for the fastest incoming missile pulse |
| GameAudioTuning | missileWarningFarTimeToImpact | float | 0.1-30 | 4 | Impact estimate for the slowest incoming missile pulse |
| GameAudioTuning | missileWarningNearTimeToImpact | float | 0-5 | 0.35 | Impact estimate for the fastest incoming missile pulse |
| GameAudioTuning | missileWarningFarInterval | float | 0.1-5 | 0.9 | Seconds between distant incoming missile pulses |
| GameAudioTuning | missileWarningNearInterval | float | 0.1-5 | 0.12 | Minimum seconds between imminent incoming missile pulses |
| MapSandboxCamera | flySpeed | float | 1-500 | 60 | Sandbox camera speed in metres per second. |
| MapSandboxCamera | lookSensitivity | float | 0.01-1 | 0.12 | Degrees of camera rotation per mouse pixel. |
| MapSandboxCamera | pushSpeed | float | 0.1-20 | 3 | Velocity impulse applied by the sandbox's middle-click collision probe. |
| MissionDefinition | unpricedPartCost | float | 0.01+ | 10 | Optional provisional cost for an otherwise zero-cost functional part; ordinary hull remains free. |
| MissionDefinition | relaxedBudgetMultiplier | float | 0+ | 1.25 | Budget multiplier for relaxed difficulty. |
| MissionDefinition | standardBudgetMultiplier | float | 0+ | 1 | Budget multiplier for standard difficulty. |
| MissionDefinition | expertBudgetMultiplier | float | 0+ | 0.75 | Budget multiplier for expert difficulty. |
| ModuleDefinition | cost | float | 0+ | 0 | Deployment cost; implicit field default is zero. |
| ModuleDefinition | massKg | float | 0+ | 300 | Part mass in kilograms. |
| ModuleDefinition | maxHp | float | 0.01+ | 60 | Independent part durability. |
| ModuleDefinition | recoverySeconds | float | 0.01+ | 6 | Time to reconstruct a missing part or heal a full part after the shared quiet delay. |
| ModuleDefinition | thrustNewtons | float | 0+ | 0 | Rated force per nozzle; multi-direction modules contribute only one nozzle rating to shared manoeuvre assistance. |
| ModuleDefinition | generationWatts | float | 0+ | 0 | Continuous shared power generation. |
| ModuleDefinition | storageJoules | float | 0+ | 0 | Shared stored energy capacity. |
| ModuleDefinition | thrusterPowerWatts | float | 0+ | 0 | Power request associated with propulsion. |
| ModuleDefinition | shieldPowerWatts | float | 0+ | 0 | Power request associated with shield recovery. |
| ModuleDefinition | shieldCapacity | float | 0+ | 0 | Maximum shield strength per generator. |
| ModuleDefinition | shieldRadius | float | 0.1+ | 8 | Shield field radius in metres. |
| ModuleDefinition | shieldRecoveryDelay | float | 0+ | 6 | Delay before a shield can recover. |
| ModuleDefinition | shieldRecoveryPerSecond | float | 0+ | 50 | Shield recovery per second. |
| RangeAsteroidBackdrop | backdropEnabled | bool | 0-1 | true | Show or hide the decorative belts without changing their deterministic positions. |
| RangeAsteroidBackdrop | seed | int | 0-2147483647 | 261107 | Stable placement seed for all three world-anchored belts. |
| RangeAsteroidBackdrop | farFadeStart | float | 0-50000 | 17000 | Camera distance where horizon rocks begin blending toward the distant atmosphere. |
| RangeAsteroidBackdrop | farFadeEnd | float | 0-50000 | 25000 | Camera distance where horizon rocks reach their distant atmosphere colour. |
| RangeAsteroidBackdrop | maximumVisibleTriangles | int | 1-20000000 | 4000000 | Diagnostic triangle target; reaching it never removes permanent decorative rocks. |
| RangeAsteroidBackdrop | densityMultiplier | int | 1-8 | 1 | Legacy procedural-rock density only; native uniform belts use fixed stratified world slots. |
| RangeAtmosphereTuning | dustLimit | int | 0-320 | 160 | Hard dust-particle cap; low quality caps this at 64. |
| RangeAtmosphereTuning | hazeLimit | int | 0-16 | 12 | Hard haze-billboard cap; low quality disables haze. |
| RangeAtmosphereTuning | dustRadius | float | 10-150 | 65 | World-space dust emission radius around the camera. |
| RangeAtmosphereTuning | dustRate | float | 0-40 | 10 | Dust particles emitted per second. |
| RangeAtmosphereTuning | dustPerMetre | float | 0-0.5 | 0.12 | Additional native particle emission per metre travelled. |
| RangeAtmosphereTuning | hazeOpacity | float | 0-0.1 | 0.028 | Maximum haze opacity before lifetime and texture fading. |
| RangeAtmosphereTuning | fogDensity | float | 0-0.001 | 0.00016 | Lightweight exponential-squared haze on opaque scene geometry. |
| RangeAtmosphereTuning | skyIntensity | float | 0-0.15 | 0.09 | Directional warm/cool background radiance, driven by the scene star light. |
| RangeAtmosphereTuning | beltFalloff | float | 100-3000 | 1200 | Height range over which scenery-belt haze fades into open space. |
| RangeAtmosphereTuning | starDirection | Vector3 | -1-1 | new Vector3(.62f,.52f,.58f) | Direction toward the distant sun, shared by sky disk and scene key light. |
| ReferenceMapProbe | captureFrames | int | 60-2000 | 120 | Rendered frames sampled per overview/near-station measurement phase. |
| SpaceMapDefinition | size | Vector3 | 1-100000 | Vector3.one * 3000 | Full map dimensions in metres. |
| SpaceMapRuntime | stageBuildOnStart | bool | 0-1 | false | Opt into staged startup when the caller waits for BuildIsComplete before gameplay. |
| SpaceMapRuntime | instancesPerFrame | int | 1-1000 | 2 | Maximum scenery bodies created in a staged startup frame. |
| SpaceMapRuntime | buildMillisecondsPerFrame | float | 0-100 | 5 | Soft CPU budget for staged startup; one body always finishes atomically. |
| SpaceMapRuntime | lazyFineVisuals | bool | 0-1 | false | Use signed authored meshes at startup and generate exact meshes near the camera when enabled. |
| SpaceMapRuntime | visualLodDistance | float | 0-100000 | 800 | Camera distance where an authored coarse visual replaces an intact body's exact renderer. |
| SpaceMapRuntime | visualCullDistance | float | 0-100000 | 0 | Camera distance beyond which only scenery renderers are hidden; zero disables this cull. |
| SpaceMapRuntime | visualRefreshFrames | int | 1-120 | 4 | Frames between map-level visibility checks. |
| SpaceMapRuntime | maximumFineVisualCells | int | 0-131072 | 0 | Keep larger intact bodies on their exterior cube mesh; zero permits all exact hull upgrades. |
| SpaceMapRuntime | keepCoarseIntactVisuals | bool | 0-1 | false | With lazyFineVisuals, retain authored coarse meshes on intact scenery at every distance. |
| SpaceMapRuntime | assemblyInitialMaxDepenetrationVelocity | float | 0-100 | 0 | Optional temporary cap for the first physics step after linked map loading; zero preserves old behaviour. |
| SpaceMapRuntime | useBudgetedPhysics | bool | 0-1 | false | Use one moving bounds collider per scenery body and add exact colliders near the camera within a global budget. |
| SpaceMapRuntime | detailedCollisionDistance | float | 0-100000 | 300 | Maximum camera distance from an asset's bounds for detailed physics selection. |
| SpaceMapRuntime | activeColliderBudget | int | 1-1000000 | 8000 | Maximum enabled scenery colliders, with one mandatory bounds proxy per live body. |
| SpaceMapRuntime | physicsRefreshFrames | int | 1-120 | 12 | Frames between map physics distance and budget checks. |
| SpaceMapRuntime | colliderPreparationPerFrame | int | 1-256 | 24 | Maximum cold exact collider components prepared per frame beneath a live proxy. |
| SpaceMapRuntime | colliderPreparationMilliseconds | float | 0-10 | 1.5 | Soft collider preparation CPU budget; zero disables the time limit. |
| TargetSilhouetteOutline | resolutionScale | float | 0.25-1 | 0.5 | Selected-target mask resolution relative to the existing world view. |
| TargetSilhouetteOutline | outlinePixels | float | 1-6 | 2 | Exterior silhouette thickness in world-view pixels. |
| UiTheme | sceneryHitFeedbackStrength | float | 0-1 | 0.3 | Relative brightness and contraction of the white terrain-hit confirmation. |
| UiTheme | combatInstrument | Color | 0-1 | new Color32(185,255,3,255) | Independent high-contrast flight instrument, reticle and player-radar colour. |
| VoxelAssetDefinition | cellSize | float | 0.25-10 | 1 | World metres per voxel before instance scaling. |
| VoxelAssetDefinition | massPerCell | float | 0.01-1000000 | 100 | Kilograms per cubic metre of solid scenery cells. |
| VoxelAssetDefinition | baseColor | Color | 0-1 | Color.white | Scenery-only tint multiplied with the original ship hull material and each colour slot; white preserves the source appearance. |
| VoxelStructureRuntime | driftVelocity | Vector3 | -100-100 | 0 | Initial world-space velocity; collisions subsequently control movement. |
| VoxelStructureRuntime | angularVelocity | Vector3 | -180-180 | 0 | Initial local angular velocity in degrees per second. |
| WeaponDefinition | fireWindupSeconds | float | 0+ | 0 | Delay from accepted rail audio onset to physical discharge; zero preserves immediate shots. |
| WeaponDefinition | shotsPerSecond | float | 0.01+ | 5 | Nominal shot cadence before energy allocation. |
| WeaponDefinition | damage | float | 0.01+ | 40 | Damage of one complete projectile. |
| WeaponDefinition | shotAudioGain | float | 0-1 | 1 | Relative firing loudness by weapon size; small 0.6, medium 0.8, large 1. |
| WeaponDefinition | projectileSpeed | float | 0.01+ | 160 | Projectile speed used for travel and interception. |
| WeaponDefinition | projectileDiameter | float | 0.01+ | 1 | Physical projectile hit-volume diameter. |
| WeaponDefinition | penetrationDepth | float | 0.01+ | 2 | Maximum hull penetration depth. |
| WeaponDefinition | secondLayerDamageMultiplier | float | 0-1 | 0.5 | Damage fraction for the second hit layer. |
| WeaponDefinition | smokeTrail | bool | 0-1 | false | Visual-only muzzle smoke for solid rounds or post-ignition missile exhaust. |
| WeaponDefinition | tracerLingerSeconds | float | 0+ | 0 | Visual-only fade after a shot resolves; zero releases immediately. |
| WeaponDefinition | minimumVisualPixels | float | 0+ | 0 | Minimum projected tracer thickness; zero keeps authored world width. |
| WeaponDefinition | range | float | 1+ | 800 | Maximum projectile travel distance. |
| WeaponDefinition | unboundedBeam | bool | 0-1 | false | Use the 100km operational query reach for an effectively unlimited beam. |
| WeaponDefinition | sustainedPowerWatts | float | 0+ | 50 | Shared power requested per firing weapon. |
| WeaponDefinition | magazineSize | int | 1+ | 1 | Rounds in one VLS magazine; tube offsets describe physical cells independently. |
| WeaponDefinition | salvoLaunchesPerSecond | float | 0.01+ | 16 | Independent VLS mount launch cadence. |
| WeaponDefinition | missileLockSeconds | float | 0+ | 2 | Seconds holding one valid target before missile launch. |
| WeaponDefinition | magazineSize | int | 1+ | 1 | VLS rounds available before a complete magazine reload. |
| WeaponDefinition | salvoLaunchesPerSecond | float | 0.01+ | 16 | Independent per-module VLS launch cadence. |
| WeaponDefinition | reloadSeconds | float | 0.1+ | 4 | Powered automatic missile reload duration. |
| WeaponDefinition | missileTurnDegrees | float | 0+ | 110 | Maximum guided turn rate in degrees per second. |
| WeaponDefinition | missileTurnAcceleration | float | 0+ | 180 | Maximum guided angular acceleration in degrees per second squared. |
| WeaponDefinition | missileGuidanceSeconds | float | 0+ | 10 | Maximum seeker lifetime after launch; expiry leaves the missile coasting. |
| WeaponDefinition | missileSeekerConeDegrees | float | 1-179 | 65 | Seeker half-cone around the missile's current forward direction. |
| WeaponDefinition | missileSeekerLossSeconds | float | 0+ | 0.35 | Continuous time outside the seeker cone before permanent lock loss. |
| WeaponDefinition | missileAcceleration | float | 0+ | 60 | Acceleration from launch speed to cruise speed. |
| WeaponDefinition | missileLaunchSpeed | float | 0.01+ | 25 | Ejection speed of a guided missile. |
| WeaponDefinition | explosionRadius | float | 0.1+ | 4 | Missile blast radius with radial damage falloff. |
| WeaponDefinition | beamDuration | float | 0.01+ | 0.12 | Persistence of one continuous beam tick. |
| WeaponDefinition | chargeSeconds | float | 0.01+ | 1 | Charge time for charge-release activation. |
| WeaponDefinition | gatlingSpinUpSeconds | float | 0.01+ | 1.5 | Powered seconds to reach maximum Gatling rate. |
| WeaponDefinition | gatlingSpinDownSeconds | float | 0.01+ | 0.5 | Unpowered seconds to stop fully spun barrels. |
| WeaponDefinition | gatlingMinimumRateFraction | float | 0-1 | 0.2 | Gatling rate at the beginning of powered spin-up. |
| WeaponDefinition | gatlingHeatPerSecond | float | 0+ | 1f/8f+1f/6f | Heat gained per fully powered firing second before simultaneous cooling; defaults overheat after 8 seconds. |
| WeaponDefinition | gatlingCoolingPerSecond | float | 0+ | 1f/6f | Passive heat removed per simulation second in every state; defaults recover full heat in 6 seconds. |
| WeaponSmokePool | missileLifetime | float | 0.1-10 | 6 | Seconds missile smoke remains after the missile passes. |
| WeaponSmokePool | ballisticLifetime | float | 0.1-10 | 4.5 | Seconds solid-round flight smoke stays in world space. |
| WeaponSmokePool | ballisticOpacity | float | 0-0.5 | 0.32 | Peak opacity of each solid-round path wisp. |
| WeaponSmokePool | ballisticWidth | float | 0.5-8 | 1.5 | Base diameter of stretched solid-round path smoke. |
| WeaponSmokePool | ballisticSpacing | float | 1-30 | 10 | Metres between solid-round path samples before budgets. |
| WeaponSmokePool | muzzleLifetime | float | 0.1-10 | 6.5 | Seconds gun smoke stays near its firing location as the ship moves away. |
| WeaponSmokePool | missileOpacity | float | 0-0.5 | 0.36 | Peak opacity of each missile smoke puff. |
| WeaponSmokePool | muzzleOpacity | float | 0-0.5 | 0.3 | Peak opacity of each gun muzzle smoke puff. |
| WeaponSmokePool | missileSpacing | float | 0.5-10 | 3.2 | World metres between flight smoke samples before per-frame budgets. |
| WeaponSmokePool | missileWidth | float | 0.5-8 | 3 | Base missile smoke diameter in metres before expansion. |
| WeaponSmokePool | muzzleWidth | float | 0.5-8 | 2.1 | Base muzzle smoke diameter in metres before expansion. |
| WeaponSmokePool | muzzlePlumeLength | float | 0-12 | 5 | Short forward muzzle plume; never traces the full solid-round path. |
| WeaponSmokePool | maximumDistance | float | 100-10000 | 5500 | Reject distant smoke emissions to protect combat visibility and fill rate. |

