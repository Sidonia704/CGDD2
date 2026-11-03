# Combat HUD and dense-field performance — 2026-11-03

The student confirmed that victory/defeat rules and controls remain unchanged. The working 75-part `11111` blueprint is preserved (SHA256 `E2292024227A090DA08797DA73CB20731DFEAA6EE632B0B2497E8D63FF699E8E`).

## HUD

Only the own/target attitude monitors retain their glass backgrounds during flight. Radar, telemetry, weapon groups and mission readouts float directly over the scene. Pause/settings controls retain normal modal surfaces. Speed uses a graduated tape; shields and energy use segmented meters and symbols, with compact numeric readings. Existing F1 diagnostics remain available.

Blue and then white instruments proved difficult to distinguish from the scene. Three actual-scene trials are saved: [lime](palette-lime.png), [magenta](palette-magenta.png), [amber](palette-amber.png). Current selection: lime instruments/reticle/player radar, neutral text and ship silhouettes, red enemy/attack warnings. Damaged schematic parts stay red and the core is highlighted.

Colour is independently editable in `Assets/_Starframe/Resources/UI/WorkbenchTheme.asset`, **关键仪表与准星 / combatInstrument**. Trial colours: lime `#D6FF2E`, magenta `#FF5CC7`, amber `#FFCC33`. Menus and world colours are unaffected.

## Direct damage

Eleven non-guided WeaponDefinition assets are doubled. Cadence, speed, penetration, energy and missile damage are preserved. These are shared player/AI definitions.

| Family | Installed damage, tiers in order |
| --- | --- |
| Laser | 840 / 1380 / 2280 |
| Gun | 360 / 510 / 720 |
| Railgun | 7200 / 12000 / 20800 |
| Beam | 1500 / 2700 |
| Missile, unchanged | 11200 / 19200 / 32000 |

`tools/CombatHud20261103/Damage.cs` is idempotent against the saved scalar baselines in `before/`; it is not a runtime multiplier.

## Performance

The severe reproduction requires repeated kills **and shooting into dense scenery**, not just ordinary open-space flight. Large first-hit objects previously expanded their coarse exterior into thousands of exact bevelled hull meshes, including hidden interior geometry.

- Budgeted scenery prepares exact collider components in small batches beneath its live bounds proxy; exact voxel shot queries remain authoritative. Selection has distance/ranking hysteresis.
- Large lazy scenery retains the original full-cell exterior LOD after destruction, using local render tiles, original material slots and hidden-interior culling. Source records for all eight shapes, temporary hit HP, permanent removed cells, splitting, mass and collision are unchanged. Ship models and editor exact-model rendering are unchanged.
- Damage exterior meshes/views are prepared during the loading screen. A warm Editor launch measured 25.35 seconds; preparation is moved out of flight, not claimed to be a net startup speedup.
- Wreck instancing caches source descriptors/batches and uses per-island bounds. Every surviving part retains its HP and collision; no wreck cap or timed deletion is added.

Single Editor samples, same 22-second scripted approach and continuous player firing after ten rapid target-kill rounds:

| Metric | Before terrain exterior/prewarm | Final |
| --- | ---: | ---: |
| Wreck parts | 10,760 | 10,880 |
| Frame p50 | 50.01 ms | 25.96 ms |
| Frame p95 | 120.86 ms | 53.30 ms |
| Maximum frame | 2937.58 ms | 126.74 ms |
| Wreck instance draw, mean CPU | 4.689 ms | 0.392 ms |

Sources: `dense-combat-after.txt`, `dense-combat-exterior-final.txt`; the former already contains the initial collider/cache changes. Random drift, damage and fragment counts vary. This stress fixture deliberately overlaps successive wrecks more than normal respawn timing. These are Editor measurements, not a standalone FPS claim. Short high-pressure peaks remain, including an 82.096 ms final map-damage flush. Large fractures and fresh wreck creation remain potential peaks.

`SpaceMapRuntime` on both FlightRange and SaturnRing stores `colliderPreparationPerFrame=24`, `colliderPreparationMilliseconds=1.5`, `lightweightDamageThreshold=128`. The lighter damage path additionally requires `keepCoarseIntactVisuals=true`; ordinary maps that promote to authored fine models retain their original damage visuals. Set the threshold to zero to restore exact damaged-model generation, with its former cost. Exact physical occupied cells do not change with this visual setting.

## Review and rollback

Verification: EditMode **528/528**, then **18/18** map rechecks including the added fine-mode isolation guard. The full PlayMode run passed **47/49**; its two failures were a now-longer staged launch exceeding the old 25-second fixture wait and a shield-only power assertion retaining prior weapon input. The group/HUD check also expected the deliberately removed glass panel. Updating those fixture boundaries gives **3/3** multi-target/shield checks and **1/1** number-key/HUD rechecks, covering all **49** distinct cases. No gameplay assertions were disabled. Post-cleanup wreck mechanics pass **11/11**, and native retired-source rendering/destruction passes **1/1**. Final normal-range smoke verifies75 player parts,2854 map bodies, live attitude glass, transparent instrument roots and260 instrument vertices; Console has zero errors/warnings. See the JSON reports and `final-native-smoke.txt` in this directory.

`before/` retains the pre-change HUD/renderer source, scene copies, theme asset and per-weapon scalar values. `scoped-changes.patch` shows the backed-up UI/renderer edits. Restore only the intended feature, merging later user edits rather than overwriting unrelated work. Helpers live outside Assets and are excluded from the game build.
