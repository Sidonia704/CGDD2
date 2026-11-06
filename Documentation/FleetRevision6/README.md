# Six-wave fleet revision

The source snapshots in `approved-blueprints/` are byte-for-byte copies of the student's saved t0-1–t0-4, t1–t7, t7-2 and t8 designs. `sources.json` records their SHA-256 hashes. The importer checks every blueprint with the native repository and deployment validator before changing encounter assets, and verifies all player blueprint/workspace bytes after import.

`tools/FleetRevision6/AuditLibrary.cs` provides the read-only library audit. `tools/FleetRevision6/InstallFleet.cs` installs native encounter TextAssets, archetypes, behavior profiles, the template catalogue and six-wave configuration. Existing asset bytes and metadata are backed up in `before/` on first installation. The installer does not edit or delete player files.

| Wave | Ships | Role |
| --- | ---: | --- |
| 1 | 10 | Light screening ships with fighter and rail support |
| 2 | 12 | Mixed small ships and the first gunship |
| 3 | 13 | Light escorts, fighters, gunship and laser frigate |
| 4 | 13 | Mixed escorts with two rail cruisers |
| 5 | 14 | Heavy guard, including both t7 and t7-2 |
| 6 | 15 | t8 flagship arriving first, followed by mixed escorts |

The roster totals 77: 38 t0 fillers, 24 other small ships, 14 large ships and one flagship. t7 and t7-2 occupy the Large class; t8 is the sole Capital class. Existing concurrency limits remain 10 live enemies, 3 large ships and 3 active attackers. Existing continuous-stage timing, arena and the 30,000 standard player budget are retained. The current selfuse113 costs 24,266, while the enemy t8 costs 31,425; enemy encounters do not consume the player's assembly budget.

New t0 pilot profiles follow their actual armament: rapid laser at 420 m, rail at 1100 m, direct beam at 350 m, mixed rail/Gatling at 600 m. t7-2 uses a missile-frigate profile and t8 uses the capital profile. These are initial authored encounter choices, not a claim of human playtesting or measured mission duration.

`Assets/_Starframe/Tests/Construction/EnemyFleetTests.cs` checks editable independent copies, catalogue membership, source preservation, native deployment and support/clearance, real nozzle masks and six-axis authority, the complete six-wave composition, final flagship escorts and progress requiring all surviving enemies to be defeated. Run after the installer and gameplay compilation.

The live library audit found only `战舰.json` invalid because its parts no longer form one face-connected assembly. Every other saved blueprint passed. Deletion and its external backup are handled separately from the fleet installer.

## Completed verification

Native installation completed:13approved designs,6waves77enemies.67focusedEditModecases and4BalancedShipControlPlayModecases passed. The latter used current player workspace read-only plus3reference ships and realphysics. Heavy native t7/t8 speed ceilings149.56/88.09m/s. Debug parameters and UI behavior: DebugMultipliers.md. RealUI preview images show Mactive and visible delete minus; temporary UI fixtures intentionally omit normal scene content and telemetry. These previews do not claim a complete interactive playthrough. No full human six-wave difficulty calibration or standalone performance test was performed.

Invalid 战舰.json was removed only after BlueprintRepository validation; byte-verified backup in RemovedBlueprint/. Existing source designs and workspace remain unchanged. The six-wave native installer also verifies all protected user files before/after writes.
