# DEVLOG

Only append. Do not edit old sections. Two samples stay so students can see the format.

Only changes to the game itself are logged: the verb, win and loss, pressure, and what the player can feel.
Site language, diagram styling, and click-to-read-script do not go into an Interaction or a Stage Reflection.

Each gameplay change should add a section `## Interaction NNN — title` (the Agent can append this when it changes the game).
Add `## Stage Reflection NNN — title` only when you ask for one: Keep / Change / Cut, and which AI suggestions you accepted or rejected. That reflection stays on the game.

## Interaction 000 — EXAMPLE (sample, do not delete)

- Date: —
- Who asked: teacher (sample)
- Request: Tighten one tunable and see if it is still the same game.
- What changed: Sample only. The current code was not changed. A real change would touch only one `@tune` field.
- Result: The player's verb did not change. Win and loss did not change. Pressure changed, not the skeleton.

## Stage Reflection 000 — EXAMPLE (sample, do not delete)

### Keep
- A core verb you can say in one sentence, and a clear win/loss.

### Change
- To make it harder, turn an existing `@tune` first. Do not add a new system yet.

### Cut
- No shop, no deep inventory, no optimization that is off-lesson.

### AI
- Accept: tune feel with existing fields; avoid opening new scripts.
- Reject: GraphView, object pools, long hand-written scene YAML. This class is about script relationships and tunable numbers.

## Interaction 001 — Floating destructible scenery

- Date: 2026-10-24
- Who asked: student
- Request: Build cube-based space scenery that can be damaged and pushed without gravity; an intact asset moves together and separates only when its connections are destroyed.
- What changed: Added shared exterior voxel meshes, merged compound collisions, and independent rigid bodies for disconnected surviving islands. Existing laser penetration can damage scenery cells, and a ship collision sweep can push a scenery body. Only recently hit cells store temporary health; after 2 seconds without another hit the cell is treated as full health immediately. Destroyed cells remain absent, while surviving fragments retain collision and can still be destroyed.
- Result: A standalone developer sandbox allows collision and destruction checks without altering saved scenery or maps. These assets are not loaded by the current practice range or mission flow yet, so the existing player verbs, practice respawns and absence of a terminal practice victory remain unchanged. Mission waves and automatic mission victory/defeat are not implemented by this change.

## Interaction 002 — Keep floating scenery geometry alive across scene changes

- Date: 2026-10-24
- Who asked: student
- Request: Fix missing voxel scenery and rerun the complete regression suite.
- What changed: Shared scenery meshes now have an explicit lifetime, are rebuilt if their native resource has become invalid, and are released when their final owner leaves. Scene teardown no longer tries to delete the same collider twice. Existing voxel shapes, sparse damage, collision, fragmentation and tuning values are retained.
- Result: Scenery can be inspected, loaded into a physics sandbox and released without leaving an invalid shared mesh for the next instance. This is a resource correctness fix; player verbs, challenge pressure, practice respawns and mission outcomes are unchanged.

## Interaction 003 — Shared ship hull appearance for space scenery

- Date: 2026-10-24
- Who asked: student
- Request: Give map-built objects the ship's models and plastic materials, with independently adjustable base colour and the four basic hull shapes; fix missing surfaces inside seams.
- What changed: Scenery now combines the exact cube, wedge, slab and pyramid meshes from the module database, retaining normals, UVs and tangents. Shape, grid rotation and colour slot survive saving and destruction into fragments. Untinted surfaces share the ship materials; tinted copies keep the original shader and plastic properties without changing ships. Visual face culling now checks actual model coverage, so the real gaps between undersized bevelled models retain visible interior surfaces.
- Result: Map objects and ships use the same visual parts. Complete-cell occupancy, compound collision, durability, drifting fragments and existing tuning values are retained. These changes do not alter current practice verbs, challenge pressure, respawns or mission outcomes.

## Interaction 004 — Expanded construction and weapon choices

- Date: 2026-10-25
- Who asked: student
- Request: Preserve the existing parts and complete the supplied hull, propulsion, power, shield, armor and weapon expansion, with real mounting contact, balanced numbers, materials and combat effects.
- What changed: The library now has 36 parts, including 26 additions. Weapons and passive armor connect through their actual underside; empty slab tops and other unsupported faces no longer carry floating structures. Long armor aligns its real mounting foot to the selected support. Larger engines reserve their full exhaust area. Machine guns alternate their real barrels; railguns fire high-speed penetrating shots. Missiles require two seconds on the same target, launch real missile bodies, track, explode and reload using power; twin racks split one volley's damage between two missiles. Sword housings retain their permanent triangular structure and project a separate blue blade with 40 m reach. Ballistic tracers were enlarged and given a 2.8-pixel minimum display thickness, a bounded visual enlargement and longer orange-yellow trails after the student reported poor visibility. New basic hulls are also available to map authoring with per-cell damage and fragmentation.
- Result: The player can choose between sustained direct fire, penetrating shots, a lock-and-reload missile window and a short-range high-damage blade, while mass and shared power remain tradeoffs. The student specified the missile lock operation and direct fire for other weapons. No new victory or defeat condition was requested; the open practice range and core-destruction respawns remain. Existing blueprints and source art were not rewritten.

## Interaction 005 — Five-way thruster and faster weapon rhythm

- Date: 2026-10-25
- Who asked: student
- Request: Slightly lower the rapid-laser cadence, raise kinetic-gun cadence and muzzle velocity, make the railgun's shot far more visible as a very long, high-speed laser-like pulse, and add a one-cell five-way thruster whose five nozzles each reserve one plume cell.
- What changed: Thruster6 is a 400 kg, 80 HP, single-cell propulsion module with five 45 kN nozzles (+Y, +X, −X, +Z, −Z) and only an underside mount. Each nozzle reserves exactly one adjacent cell; those cells reject other blocks in every rotation, while blocking one nozzle disables only that axis. The shared manoeuvre rating counts the module once, not five times. Laser2/Laser3 fire at 10/s instead of 12/s. Gun1/2/3 fire at 16/20/24/s at 1800/2000/2200 m/s. Railgun1/2/3 now travel at 10/12/14 km/s and render as a continuous gold beam with a white core, a 200/300/420 m tail, a 0.09 s impact linger, authored widths 8/9/10 and a 4.5–5.5-pixel minimum screen thickness; the physical projectile diameter, damage and penetration are unchanged.
- Result: A one-cell module can now nudge the ship along five axes at low thrust while its plumes remain physically clear, and the player can see one nozzle blocked without losing the others. Kinetic guns feel faster and flatter, rapid lasers are slightly calmer, and the railgun reads as a fast straight pulse that sweeps across the target lane instead of an invisible point. No victory/defeat condition, mission flow, player verb, AI or flight tuning was changed. Source assets and existing blueprints were not rewritten.

## Interaction 006 — Half-cell five-way thruster judgement

- Date: 2026-10-25
- Who asked: student
- Request: Keep the Thruster6 plume one cell long, but make its placement judgement half a cell.
- What changed: Thruster6's five exhaust cells no longer hard-reserve placement; a connected block may sit flush in an exhaust cell. The green clearance preview is 0.5 m instead of one full cell. The rendered plume remains 1 m long and 0.3 m wide. Runtime nozzle obstruction still uses the full adjacent cell, so a flush block closes only that nozzle; the other four remain available. Mass, HP, thrust, mounting and all other thrusters are unchanged.
- Result: The small five-way thruster fits into tighter builds while its unobstructed plumes keep their full one-cell read. A block placed flush against a nozzle costs only that one direction. No verb, win/loss, mission flow, AI or flight tuning changed.

## Interaction 007 — Power mounting, wide equipment supports and railgun trigger

- Date: 2026-10-25
- Who asked: student
- Request: Let blocks sit on the large power's top, require both bottom contacts for two-wide weapons and armor, and make the railgun actually fire.
- What changed: Power and Power2 now use complete regular-cell faces in every direction, so their decorative inset/hollow top no longer blocks mounting. Weapons and armor now require support under every bottom width column; a two-wide part needs both columns, while armor is checked only along its real rear-foot row. One-wide weapons/armor and other functional blocks keep the previous rule. Weapon connectivity checks the real support top; armor keeps its actual foot geometry. Railguns fire their first shot immediately on the first press and then wait for their normal slow cadence; two-wide railguns now land on both supports.
- Result: Large power is a normal building block, wide weapons and armor can no longer float on one side, and a railgun responds on the first trigger press instead of appearing dead during its long spin-up. Mass, HP, thrust, projectile damage, penetration and the other functional blocks are unchanged.

## Interaction 008 — Rapid rail cadence and readable kinetic hits

- Date: 2026-10-25
- Who asked: student
- Request: Make the railgun fire continuously instead of waiting through a long cooldown, make both kinetic hit effects much more visible, shrink the oversized machine-gun projectile, and investigate one-sided or misplaced rapid-fire visuals.
- What changed: Railgun1/2/3 fire at 3/2.5/2 shots per second (about 0.33/0.4/0.5 s between shots) while keeping the immediate first shot. Gun tracer heads are 4/5/6 m long and 2.2/2.4/2.6 wide with a 2.2-pixel minimum; their 100 m tails stay. Machine-gun and railgun impact flashes now have a larger white core, more particles, larger fragments and a longer life. Real Gun1/2/3 multi-barrel tests verify all 2/4/6 muzzle origins, left/right sides, pooled visuals and tracer heads; the own-muzzle check now ignores adjacent weapons so dense gun batteries cannot mark each other blocked.
- Result: Railguns read as a continuous heavy stream instead of a slow one-shot weapon. Machine-gun bullets are compact again, but their impacts and railgun impacts are much easier to see. Both sides of every multi-barrel gun keep a visible tracer at the correct muzzle; no verb, win/loss, mission flow, AI or flight tuning changed.

## Interaction 009 — Terrain that breaks instead of tracking health

- Date: 2026-11-01
- Who asked: student
- Request: Make combat against map terrain only break it. Do not keep a separate health value on every terrain cell, only compute damage at the moment a cell is hit, forget that temporary damage after a quiet period, and never rebuild the terrain afterwards. A hit that disconnects part of an asset should break it straight into several pieces rather than adding any new disappearance rule. Very small detached pieces may remain as render-only debris.
- What changed: Intact terrain carries no stored HP; a hit writes a temporary per-cell damage record that expires after the quiet timeout (2 s by default) without restoring cells, rebuilding meshes or resetting the asset. Reaching the break point splits the asset into face-connected pieces that drift, collide and can be damaged on their own, and only newly detached pieces are ever considered for the render-only path. Intact assets keep one combined renderer, while 4x4x4 render chunks are prewarmed and appear only on the first hit that destroys or detaches geometry, so the first hit pays renderer activation and later hits rebuild only the damaged chunk and its neighbours. Collision boxes are subtracted and reused with bounded recompaction, and initial connectivity is certified once per template generation instead of being re-walked for every copy. Render-only debris is off by default (`tinyFragmentMaxCells = 0`); if enabled it only applies to newly detached pieces of at most 4 cells and at most 2 world units of extent, and nothing disappears by age, distance or count.
- Result: Terrain now reads as cover that breaks rather than as a second health bar: shooting a cell computes damage only then, a quiet spell leaves the hole exactly where it was, and enough force tears the piece free. In Editor CPU milliseconds the first lethal hit fell from 262.9 to 24.9 ms on the capsule tank and by 86-90% on the other probes; the cost is a roughly 2x slower warm instance build, a roughly 2.5x slower density-patch build, and extra native chunk mesh memory held by prewarming. No ship, weapon, flight, AI, win/loss or mission-flow behaviour changed, and map assembly stays paused.

## Interaction 010 — Third component-library pass: duplicates, sizes and barrel accuracy

- Date: 2026-11-01
- Who asked: student
- Request: Re-check the component library, delete duplicated and leftover test assets, finish whatever was still incomplete, and tighten part sizes to what assembly actually needs.
- What changed: All 37 parts were re-audited against the real asset references. The only genuine duplicates were 27 unreferenced import copies of the expansion models sitting next to their preserved source files; those are deleted, and the installer recreates them from source if it ever runs again. Railgun2 now reserves one column instead of two, so a 0.82-wide rail no longer eats a second build cell, while the model itself is unchanged apart from being re-centred and its muzzle moving with it. Power2 now fills its 2x2x2 box like Block8 and Shield2 instead of sitting 0.13 short. Multi-barrel guns draw one bore glow on each real barrel rather than one glow between them, and placement now checks every barrel exactly like the firing check, so a gun that would be blocked in flight can no longer be placed as valid. Mass, HP, thrust, power, damage, cadence and recovery values are untouched.
- Result: The library no longer carries dead duplicates, the two parts that wasted build volume now match their art, and a multi-barrel gun reads and validates per barrel instead of per averaged muzzle. No player verb, win/loss, mission flow, AI or flight tuning changed, and map assembly stays paused.

## Interaction 011 — Preserve loaded terrain through source-generation changes

- Date: 2026-11-01
- Who asked: student
- Request: Audit recent map-generation changes and correct erroneous behavior before planning the reference-scene reconstruction.
- What changed: A loaded terrain body now gives its fragments the retained geometry-template generation even when the authored source has changed or shrunk below the chunk threshold. Fragments keep independently owned copies of the donor's runtime tint materials instead of reading a newer source palette, so removing the donor does not invalidate their appearance.
- Result: Editing a source asset no longer prevents an already loaded body from splitting into surviving collidable, destructible pieces. Sparse damage expiry, permanent holes, initial-island splitting, mass and health defaults remain. No player verb, victory/defeat, mission flow, ship, AI or flight tuning changed; the larger map reconstruction is still planned work.

## Interaction 012 — Source-derived mining station sandbox

- Date: 2026-11-01
- Who asked: student
- Request: Reconstruct the supplied mining station and asteroid field at approximately 3500 metres in the map workshop. Preserve appearance first; simplify collision and computation where necessary.
- What changed: Added a separate editable layout with 2,854 placed scenery bodies and 3,123,469 occupied cells, using all eight hull shapes. Fixed per-asset grids retain one-metre mechanical detail and use larger cells for large rocks. Loading proceeds in batches, distant bodies share exterior meshes, and the largest intact solids retain their cube exterior instead of promoting to costly fitted hull geometry. Frozen native hull data is shared across template generations. The new sandbox opts into one moving bounds collider per body, with nearby detailed collisions admitted under an 8,000-collider budget. Budgeted scenery does not collide with itself, avoiding false contacts between overlapping proxy bounds, while external ship collisions, pushes and exact voxel projectile tests remain available. Source-confirmed station contacts retain physical links until their attachment cells are destroyed.
- Result: The scene remains separate from mission deployment. Individual bodies and severed islands drift without gravity; only recently hit cells carry temporary damage records, expired damage does not repair holes, and surviving cells remain after destruction. Approximate proxy contacts can block empty space around irregular scenery; precise collision is restored for admitted nearby bodies. Existing ship balance, AI, flight controls, player verbs and victory/defeat rules are unchanged. Original reference files and the previous map draft are retained.

## Interaction 013 — Temporary mining-station flight practice

- Date: 2026-11-02
- Who asked: student
- Request: Put the reference scenery below the flight range with substantial empty space above, reduce approach stutters, and distinguish terrain hits with subtle white reticle feedback.
- What changed: The range now optionally loads the reference layout at Y=-1100 m, with an elevated approach spawn and a temporary 4000 m arena. Disabling the single sandbox root restores the prior spawn and arena; the flight tuning asset is unchanged. Intact scenery retains its baked exterior instead of generating exact meshes on approach. Detailed collision has a smaller budget and a wider exit band to avoid repeated switches. Scenery physics matrices normalize slight quaternion drift before conversion. Successful terrain hits produce shorter, lower-intensity white hit marks; ship and shield damage retains red feedback and takes priority over simultaneous terrain hits. Existing hit-rate limiting is preserved.
- Result: The generated scenery is available through the ordinary practice-range entry. Terrain still floats and can be damaged, with the previously approved approximate physical contacts. Sparse damage and permanent destruction rules, ship balance, AI settings, controls, respawning and victory/defeat rules are unchanged. First destructive hits can still incur mesh-rebuild cost.

## Interaction 014 — Visible loading and deferred scenery collisions

- Date: 2026-11-02
- Who asked: student
- Request: After confirming approach stutters were fixed, add a loading bar for the roughly forty-second startup and reduce loading work where practical.
- What changed: Range entry now presents a bilingual loading overlay before asynchronous scene reading, followed by real map-object preparation progress. Flight and AI start after the map is ready; completion removes the overlay and restores input. A failed map build offers a return to assembly. Budgeted scenery stores its exact collision partition without allocating every precise BoxCollider at startup, materializing detailed components only when selected nearby. Damage and physical fragments retain the deferred partition policy.
- Result: The full range initially creates 2,854 collider components rather than eagerly creating 247,723 precise boxes. Deferred collision checks pass 14/14; a warm Editor re-entry completed in 13.952 seconds with visible progress and correct input restoration, without new runtime errors. First-start timing can differ, and a first destructive hit may still rebuild visuals. Flight controls, combat balance, damage rules, respawning and victory/defeat rules are unchanged.

## Interaction 015 — Countermeasures and readable space combat

- Date: 2026-11-02
- Who asked: student
- Request: Add a one-cell flare launcher, finite missile guidance and evasive countermeasures, size-based discovery, clearer red target feedback, separated awareness sounds and a quiet Saturn-ring setting.
- What changed: R deploys one ready surviving launcher per press, rotating launchers with independent cooldowns; the whole twelve-spark burst is one lure opportunity per missile, and the rotated upward cell column must remain clear. Missiles have bounded turn acceleration, guidance time and seeker-cone loss, permanently coasting after losing guidance; existing swept terrain collision remains. Both sides discover enemies from cached surviving hull size. F selects a red marker near the virtual cursor; locks retain valid targets offscreen and display red direction markers and a through-cover exterior silhouette. Discovery, an actual enemy lock and an already incoming missile have distinct merged cues, with distance controlling incoming cadence. The range uses a dark Saturn surface and the supplied quiet starfield; weapon timing gains a short muzzle front, delayed missile ignition and beam-contact fade.
- Result: The confirmed new player operation is deploying countermeasures; core death, destructible surviving wreckage, respawning and mission outcomes are unchanged. Existing weapon damage, projectile speeds, reload, flight and map-loading optimizations remain. All 459 EditMode checks pass. Native range checks verify independent 12/24-spark deployments, cooldown rejection, target selection, retained offscreen locks and an exterior outline visible behind opaque cover. Source models and user blueprints are preserved.
- Follow-up validation: The actual lifecycle scenario passes 1/1 and multi-target, shield and tactical-HUD scenarios pass 3/3. Rapid replacement of a closing loading overlay exposed stale UI/input ownership; the replacement now owns its own view and inherits the original input-restoration state. The dedicated reentry scenario passes 1/1 for both initially suspended and active input. Map construction and collision budgets are unchanged.

## Interaction 016 — Sparse references and light-responsive space atmosphere

- Date: 2026-11-02
- Who asked: student
- Request: Make spatial references three times sparser and less visible, add immersive glow, dust, haze and background softness while controlling cost, and give the backdrop environmental color that follows scene lighting.
- What changed: Reference walls now use 600-metre spacing and 12-metre cells, reduced opacity, and CPU rejection outside their fade distance. A separate range post-processing profile adds far Gaussian softness and wider quarter-resolution bloom. Bounded world-space dust and depth-softened haze use two small density textures, with no particle physics, shadows or lights. Broad warm/cool sky gradients and particle lighting respond to the scene star; proximity to the mining belt controls haze strength. Existing two-light count, map and collision budgets remain. A platform-filtered single-PC-quality edge case was corrected to prevent periodic particle recreation.
- Result: Nearby ship silhouettes and the HUD remain readable while the environment gains softer depth, colored illumination and moving dust. Native checks confirm grid spacing/opacity, stable particle instances, light/color and altitude response, and no runtime Console errors. At 2560x1440 on an RTX 5080, the fixed-view Editor stress comparison with 160 dust particles and 12 haze layers measured +0.439 ms mean main-thread time and +0.073 ms mean GPU time across 120 frames per mode; it is not a Player performance guarantee. Flight, aiming, targeting, damage, victory/defeat, respawn and player operations are unchanged.

## Interaction 017 — Extended exterior ring and distant planet scale

- Date: 2026-11-02
- Who asked: student
- Request: Extend the playable asteroid strip into the background, add separated parallel strips on both sides, remove strong blur in favour of distant fading, and make the planet feel immense and far away.
- What changed: Added 4,285 deterministic exterior rock instances across a 50-kilometre strip and two parallel side strips. Shared low-poly meshes, spatial batches, camera culling, two LODs and distance-dither fading keep these rocks purely visual, with no individual objects, collision, health or loading during flight. The camera sees 28 kilometres; range depth of field is Off in both native data and runtime initialization. The sky sphere uses a 20,000-kilometre world radius and a nearest surface roughly320 kilometres away, keeping an approximately80-degree apparent radius and small flight parallax without extra sphere geometry. Neutral, non-emissive shading preserves crisp remote silhouettes.
- Result: Flight and overhead views show separated outer strips around the original destructible map. A 2560x1440 RTX5080 Editor comparison submitted 2,112 visible rocks in73 batches and76,800 triangles; final GPU mean was2.626→2.632ms across120 frames per mode, while CPU mean varied14.148→13.785ms and is not treated as a speedup. Console and shader checks are clean. Existing flight, damage, target locking, collision budgets, practice outcomes and player operations remain unchanged; decorative exterior rocks are outside the authored map content.

## Interaction 018 — Readable assembly data and consistent weapon contact

- Date: 2026-11-02
- Who asked: student
- Request: Show live handling and power totals during assembly, hide the stretched home mission image, improve projectile contact and missile guidance, slow structural recovery, and set the demo deployment ceiling to 10000 cost.
- What changed: The assembly readout now exposes the same cached capabilities used by flight, including six-axis acceleration/braking, rotation, power output/storage and concurrent demand. Projectile speeds and swept-cylinder diameters were raised. Missile prediction now uses authoritative core motion and bounded acceleration-aware interception with terminal damping. Hull or shield damage restarts the 20-second structural quiet period; module recovery/rebuild durations are 2.5 times the prior installed values. Range bot/player respawn delays are 6/5 seconds. The standard demo budget is 10000, and its home card no longer displays an image.
- Result: The student explicitly confirmed that victory/defeat conditions and player operations stay unchanged. Existing destructible surviving wreckage, missile evasion, flares and terrain collision remain. This round does not add mission waves or new enemy blueprints.
- Validation: EditMode494/494. The initial PlayMode run passed42/44; large-text bounds and a stale respawn wait were corrected, followed by2/2 assembly and3/3 multi-target passing checks. The real current-ship workspace hash was unchanged.

## Interaction 019 — Consistent repeated target locking

- Date: 2026-11-02
- Who asked: student
- Request: Fix F locking becoming unresponsive after repeated use while a visible enemy marker remains under the cursor.
- What changed: F presses are consumed once and resolved after chase-camera and reticle presentation, using the actual smoothed onscreen cursor instead of a different physical-heading/raw-input projection. Lock initialization and clearing also reset stored life identifiers.
- Result: The existing F lock/unlock operation, size-based discovery, retained offscreen locks, battle values and victory/defeat rules are preserved. Fifteen focused editor checks pass, including repeated acquisition, missed attempts and both sides respawning. Twenty actual F press/hold/release cycles in the range correctly alternate lock and unlock.
## Interaction 020 — Four-second saturation and quick out-of-combat restoration

- Date: 2026-11-02
- Who asked: student
- Request: Make ordinary piloting able to inflict meaningful damage, fix the recurrent lock failure after leaving/reentering the range, then explicitly target roughly four seconds of saturated incoming fire and restoration beginning after six seconds without damage.
- What changed: All14 weapon damage profiles were raised substantially, with guided acquisition1.0/1.2/1.4s. Existing cadence, energy, HP and costs are retained. Hull and all shield fields now share the last effective damage time and a6s quiet gate; empty shields cannot recharge during continuing hull hits. Full per-part restoration takes4s for basic hull,6s for ordinary components/armor and8s for core/shield modules; powered shields recharge25% capacity per second. Flight readiness explicitly releases inherited menu input suspension, while F samples held/tap edges with fresh state after entry, focus or modal transitions.
- Result: Existing controls, core-death/respawn conditions and destructible surviving wreckage are retained; the earlier long-duel timing target is superseded by the student's new cadence. Three same-design one-way saturation fixtures at600m die3.30/4.14/4.18s after their first effective hit. These stationary native-system checks are not a human-dogfight guarantee. EditMode498/498 and PlayMode45/45 pass.
- Reentry validation: Three actual range entries, including inherited menu suspension and holding F across transitions, pass18 real key cycles through Home and assembly paths without manually unblocking gameplay input. The user75-part11111 workspace is preserved.


## Interaction 021 — Five-wave Saturn-ring arcade mission

- Date: 2026-11-03
- Who asked: student
- Request: Implement the confirmed five-wave, roughly fifteen-minute arcade framework, generate seven editable enemy types, and make AI combat more natural.
- What changed: Added the native SaturnRing mission behind the existing 10000-cost deployment screen. Fifty small, fifteen large and two capital enemies arrive in finite batches; up to twelve coexist, while three hold main attack opportunities. Seven ordinary enemy blueprints and a capital variant can be imported as separately editable library copies. Role-specific pilots use continuous navigation, stable weapon selection, terrain checks and physical moving-target interception. Wave preparation, mouse/Enter early start, scoring, best scores and result/retry navigation are integrated. Opaque wreckage is instanced, distant whole islands use cheaper collision/interpolation, and surviving wrecks own their mesh copies after the original ship is retired. Import validation caches world poses without changing its penetration rules.
- Result: The student confirmed the framework: clearing wave five wins the mission and losing the player core ends it; the preparation operation can start a wave early. Existing flight, firing, weapon-group and countermeasure controls and practice respawning remain. Only destroyed blocks disappear; surviving pieces stay as collidable, destructible wrecks. Final525 EditMode checks pass, along with49 full PlayMode checks earlier in the round and27 subsequent combat checks. Final actual-scene verification reaches12 simultaneous enemies, completes67 defeats, preserves11590 wreck parts, verifies ordinary projectiles still destroy retired-source wrecks, and passes victory, failure and reentry with a clean Console. The original75-part workspace is unchanged. Fifteen-minute manual-player pacing is a playtest target; extreme late wreckage and capital creation retain measured frame peaks.


## Interaction 022 — Blue simulation visual trial

- Date: 2026-11-03
- Who asked: student
- Request: Try a unified blue simulation setting with an orange emissive planet, then explicitly keep the background blue and remove pixel filtering.
- What changed: Added a dedicated procedural calibration sky for the hangar, assembly, practice and mission; removed photographic star/surface sampling from the active skies. Added separate blue atmosphere configuration and retained native scene backups. The initially attempted display pixel filter was removed following the student's correction, restoring the original viewports.
- Result: Real assembly and mission previews show clear blue backgrounds and an orange monochrome planet at native viewport resolution. Shader and Console checks are clean. Game outcomes, player operations, flight, AI, mission waves and balance remain as before.

## Interaction 023 — Legible flight instruments and dense-field combat performance

- Date: 2026-11-03
- Who asked: student
- Request: Double direct-fire weapon damage, reduce practice stutters after repeated kills and fast travel into dense asteroids, and replace combat background panels with minimal graphical instruments. Subsequent feedback asked for colours distinguishable from both blue space and pale asteroids.
- What changed: Eleven direct-fire profiles are doubled; missile profiles are retained. Speed, shields and energy use graphical instruments, while only own/target attitude monitors keep flight glass panels. Critical instruments have an independently adjustable colour; lime is selected with neutral text and red enemy/damage indications. Scenery exact-collider creation is staged beneath a live proxy. Large budgeted scenery keeps a prewarmed, locally rebuilt exterior LOD after damage instead of expanding its hidden bevelled interior. Wreck source descriptors, instance batches and island bounds are cached, and retired cache references are released.
- Result: The student confirmed existing victory/defeat conditions and player operations remain unchanged. Sparse terrain hit HP, removed cells, mass, splitting and exact shot data are preserved. Every surviving wreck part keeps health and collision until destroyed. The 75-part 11111 workspace remains unchanged.
- Validation: EditMode528/528. Full PlayMode47/49, followed by3/3 multi-target/shield and1/1 group/HUD rechecks after correcting loading-time and isolated-input fixture assumptions. Post-cleanup wreck mechanics11/11. In one same-script Editor stress approach with roughly10880 wreck parts and sustained fire, the maximum frame falls from2937.58ms to126.74ms, p95 from120.86ms to53.30ms, and wreck-instance mean CPU from4.689ms to0.392ms. Counts/drift vary, and this is not a standalone FPS guarantee; short pressure peaks remain. Loading-screen preparation measured25.35s in the final warm Editor sample and is not claimed as a net startup speedup. Evidence: Documentation/CombatHud-2026-11-03/README.md.

## Interaction 024 — Reliable F presses and seven editable enemy blueprints

- Date: 2026-11-03
- Who asked: student
- Request: Repair locking when red contacts remain visible but F appears unresponsive, then provide the seven enemies as editable t1–t7 blueprints for final redesign.
- What changed: F input now separates physical held state from completed taps and deduplicates press pulses by InputSystem update. Fresh presses immediately after readiness/focus are accepted while held menu keys remain suppressed. Queued F acquisition checks the displayed steering cursor and then the actual forward crosshair, within existing detected-target/radius rules. Seven primary enemy templates are saved as t1–t7 in the normal Blueprint Library with their positions and weapon groups preserved; native encounter sources and the working ship remain intact.
- Result: Existing F lock/unlock, sensor ranges, lock retention, lead calculation, player actions and game outcomes remain. The editable copies are ready for the student's revisions before application to final encounter assets.
- Validation:18 focused sensor/lock EditMode cases pass. Two actual range entries each pass first-frame F, held-key suppression,20 complete rapid taps, forward-crosshair acquisition with an offset steering cursor and pause-menu rearming. All seven saved blueprints reload with matching part/weapon-assignment counts and the working-ship snapshot is unchanged. Evidence: Documentation/LockRepair-2026-11-03/README.md.

## Interaction 025 — Lingering weapon smoke

- Date: 2026-11-03
- Who asked: student
- Request: Add restrained missile and solid-weapon smoke, then clarify that solid-round smoke belongs mainly at firing locations and both types should linger.
- What changed: Solid guns and railguns leave faint world-space muzzle clouds; moving sustained fire draws a smoke band through those firing locations. Guided missiles leave thin grey-white smoke along actual post-ignition swept flight paths. Smoke expands gradually for2.4/2.6seconds and remains after projectile retirement. Shared capped particle systems, emission budgets and distant-emission rejection limit visual load. Energy weapons do not emit smoke.
- Result: Weapon damage, speed, cadence, guidance, energy, controls and outcomes are unchanged. Smoke does not consume combat random state. Existing particle glow and missile ejection timing remain.
- Validation: Smoke residency, segment bounds, energy-weapon exclusion, budgets, random-state isolation and reuse pass. ProjectileMechanics15/15 and final VFX timing3/3 pass. Controlled native-range verification launches24 solid shots and6 missiles through actual mounted muzzle transforms over279frames, with captured persistent smoke. No standalone performance claim. Evidence: Documentation/WeaponSmoke-2026-11-03/README.md.

## Interaction 026 — Visible bidirectional ballistic and missile smoke

- Date: 2026-11-03
- Who asked: student
- Request: Make own and hostile missile and gun smoke fill the combat view with readable crossing trails; explicitly make solid-round flight-path smoke visible.
- What changed: Added solid-round swept-path smoke alongside the existing short muzzle plume, with wider and denser smoke and longer lifetimes: ballistic4.5s, missile6s, muzzle6.5s. Both factions have reserved missile/ballistic/muzzle pools and per-frame quotas, so one source cannot consume the other source's smoke allocation. Distant projected-size support helps smoke remain visible.
- Result: Real gun and missile paths now leave conspicuous lingering smoke bands; energy weapons are excluded. Weapon speed, cadence, damage, guidance, input and outcomes are unchanged.
- Validation: Smoke sampling, residency, independent faction/type budgets, random-state isolation and expiry/reuse checks pass; projectile15/15 pass. Controlled native4ship review fires256 solid shots and48 missiles across496frames with all six smoke categories populated. No standalone performance claim. Evidence: Documentation/WeaponSmokeDense-2026-11-03/README.md.
