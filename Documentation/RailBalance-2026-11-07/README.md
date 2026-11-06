# Rail penetration balance — 2026-11-07

Per the final user instruction, railgun base damage decreases by 20%, while penetration distance and retained internal damage fraction increase to target approximately 150% of original aggregate damage against a thick hull. Fire rate, windup, projectile speed/diameter, range, energy, shields and faction multipliers remain unchanged. Both player and enemy mounts use these definitions.

| Profile | Old → new base damage | Raw DPS after | Old → new depth | Old → new retained fraction | Old → new thick-hull budget | Relative budget |
|---|---:|---:|---:|---:|---:|---:|
| Railgun1 | 10800 → 8640 | 12960 | 2 → 4 m | 35 → 50% | 14580 → 21600 | 148.15% |
| Railgun2 | 22500 → 18000 | 18000 | 3 → 6 m | 35 → 45% | 38250 → 58500 | 152.94% |
| Railgun3 | 41600 → 33280 | 24960 | 4 → 8 m | 40 → 45% | 91520 → 138112 | 150.91% |

## Calibration and limits

The thick-hull reference is an aligned contiguous row of one-metre hull cubes with enough HP that no damage is lost to overkill. For a depth of N metres, the first cube receives full base damage and the next N−1 cubes receive the retained fraction. Thus the aggregate reference budget is `baseDamage * (1 + (N - 1) * retainedFraction)`. Rounded retained fractions of 0.50/0.45/0.45 keep each tier within three percentage points of the 150% target. These are theoretical per-shot raw damage budgets, not measured time-to-kill or a guarantee for arbitrary ship designs.

The larger retained fraction describes the proportion of the reduced base shot. It does not mean every already-reached interior part receives more absolute damage: small-rail interior damage rises from3780 to4320, medium from7875 to8100, large falls from16640 to14976. The extra reach supplies the large-target aggregate increase. First-layer damage is20% lower for every tier. Thin targets can therefore take less damage; thick targets with important modules inside the new reach are the intended beneficiaries.

Actual HP removed depends on shields, angle, empty space, module thickness, HP caps and topology; a part already destroyed by the old shot cannot absorb additional damage. No human-playthrough or guaranteed150% lethality claim follows from this calibration.

## Diagnosis

`ProjectileWorld.Step` uses a swept cylinder, so high projectile speed does not by itself skip hulls. Penetration starts at the first actual hull contact and ends strictly before first contact plus the configured distance. Empty space and thick modules consume that travel distance. All contacts less than one metre after the first receive full damage. Later contacts receive the retained fraction once, without exponential loss per successive layer. Hull HP removed is not subtracted from the continuing bolt.

Shields are processed in distance order and subtract absorbed damage before hull. They do not consume hull penetration distance. Unabsorbed damage continues; a fully absorbing shield stops the bolt. Unbreakable scenery stops the bolt. The actual projectile diameter is only0.30/0.34/0.38m, independent of the larger visible tracer.

The former2/3/4m penetration and35/35/40% retention can explain limited damage deeper inside large ships. Inspection found no evidence of a swept-collision bug and does not prove shallow penetration was the sole cause of the user's observation. This change adjusts authored rail balance, with the hit algorithm unchanged.

## Live multiplier snapshot

The native CombatDamageTuning asset reads global1.2, non-missile0.8, player direct3. The player factor is2.88. BattleTuning enemyWeaponDamageMultiplier is0.5, giving enemies a factor of0.48. These existing settings differ from older documented defaults and remain untouched.

## Apply and verify

Run `tools/InstallRailBalance20261107.cs`, class `InstallRailBalance20261107.Run`, through the project's Unity code execution mechanism. The installer validates all three original/already-installed profiles before changing anything, sets exact new base damage/depth/retention values, preserves other fields, and writes `installed.json`. Original full rail assets are retained in `Before/`. Repeating installation does not compound any multiplier.

Existing `WeaponBalanceVlsTests` native-DPS expectations now reflect20% lower base damage at unchanged cadence. Six `ProjectileMechanicsTests.NativeRailTradesSurfaceDamageForAboutFiftyPercentMoreThickHullDamage` cases exercise all three native profiles with one large sweep and many short sweeps. Each fires the original and revised profile through nine high-HP unit cubes, measures every layer, checks20% lower surface damage, increased reach, unchanged strict endpoint exclusion, expected aggregate damage and proximity to150% of the original total. Existing swept-collision and shield-absorption suites should run alongside them. The parent task records the actual Unity results; preparing checks is not a passing result.

Rollback: in stopped Unity, restore the three original asset files in `Before/` to their corresponding Modules paths and refresh assets, and restore the associated balance-test expectations. Historical installers represent historical configurations and can supersede this balance if deliberately rerun.
