# Lock repair and editable enemy fleet — 2026-11-03

F retains lock/unlock; discovery ranges, offscreen retention, lead calculations and victory/defeat rules are unchanged.

- Fresh presses on the first allowed update after focus/readiness are consumed immediately. Keys held through a blocked/menu state still require release.
- Completed down/up taps are deduplicated by `InputState.updateCount`, not latched as continuously held keys. Distinct input updates remain usable even without a released render frame.
- A real queued F press tries the presented steering cursor first, then the actual ship-forward crosshair, using the existing radius/detection rules. Explicit `ToggleLockAt` calls remain strict single-point queries. Targets outside both acquisition regions are not selected arbitrarily.

Verification:18 sensor/locking EditMode cases pass. Two real FlightRange entries each pass a fresh F on the first allowed frame, held-F suppression,20 completed same-update down/up taps, an offset steering cursor with the target under the forward crosshair, and pause-modal rearming. See `native-input.txt` and `edit-lock-tests.json`. These checks use real keyboard state events and the ordinary Update/LateUpdate path. The Editor also emitted unrelated Unity AI subscription/model-list diagnostics and TMP ellipsis warnings; neither is part of the lock state machine.

Seven primary templates are copied into the real player Blueprint Library with their part poses and weapon-group assignments preserved. Each is reloaded and checked after saving; the current workspace is not selected or rewritten.

| Blueprint | Native archetype | Parts |
| --- | --- | ---: |
| t1 | needle — Needle Interceptor |81|
| t2 | viper — Viper Assault Fighter |105|
| t3 | wasp — Wasp Missile Fighter |95|
| t4 | bastion — Bastion Gunship |311|
| t5 | harrier — Harrier Missile Frigate |331|
| t6 | halberd — Halberd Assault Cruiser |400|
| t7 | citadel — Citadel Capital Ship |917|

Files are under `C:/Users/34808/AppData/LocalLow/DefaultCompany/CGDD26107/Blueprints/t1.json` through `t7.json`, also available in the in-game Blueprint Library. `enemy-blueprints.tsv` records the exact mapping and paths. Only seven primary fits are exported; the existing capital siege variant remains in its source assets.

The new files are editable copies for the student's final designs. When those are ready, apply t1–t7 to the corresponding archetype assets above (and explicitly decide whether t7 also replaces the old wave-five alternate fit). Original encounter assets remain intact during editing. Source backups for this repair are in `before/`; external verification/export helpers are under `tools/LockRepair20261103/`.
