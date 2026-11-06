# Weapon balance and VLS expansion — 2026-11-06

Native rates/speeds/gains and unchanged theoretical DPS are recorded in balance-installed.json. Full module/weapon/audio readback is runtime-audit.json. VLS source mesh provenance and occupied-cell counts are in ../VlsExpansion/modules.json.

| Weapon family | Small / medium / large rate | Shared speed |
| --- | --- | --- |
| Rail | 1.5 / 1 / 0.75 per second | 10800 |
| Rapid laser | 10 / 8 / 6 per second | 6600 |
| Gatling | 12 / 24 / 32 per second | 2700 |

Each profile retains previous damage×rate. Native damage values: Gatling720/637.5/810; rapid laser1008/2328.75/5130; rail10800/22500/41600. Gatling8/6 thermal cycle retained. Firing gain by size0.6/0.8/1; missile cue0.301 (30% decrease); radar entry cue0.24 (60% of earlier0.4) with pitch0.8.

VLS1 is16 rounds/8cells (2×2×2); VLS2 is8rounds/4cells (1×2×2). Both use4-second radar-visible area acquisition,16launches/s per component,24powered seconds to refill after the last physical launch. Small empties in0.5s, large in1s, independently of conventional stagger and other modules. Group round-robin assigns total available rounds with at most one-round allocation difference. Disabling during salvo retains unused rounds. Dead/life-changed recipients never consume ammunition. Completed locks and live VLS seekers drive secondary target frames; lost/diverted/expired seekers remove that mark. Targets must remain radar-visible for drawing.

Per VLS round damage3360 (30% of Missile1); explosion radius1.375; speed280; launch35; acceleration200; turn330deg/s, angularacceleration1080; seekercone110deg, loss0.8s, guidance30s; range6600 covers sensor exit hysteresis. Local-up launch for every grid orientation. Bottom/sides mount; top cells and launch columns cannot be blocked by own hull/weapons. Native models use one uniform scale0.50713 fitting the shared grid. Catalogue follows conventional missiles, small then large.

Conventional missile acquisition now restarts after reload and only ready/unobstructed units contribute lock progress; first eligible completed unit is sufficient. Sensor target selection and direct-fire lead remain. New guided missiles default to second/right-mouse group; existing explicit assignments are retained.

Validation:98 unique EditMode checks plus10 GameAudio PlayMode checks passed. ExpandedWeaponTests initial36/37 plus focused corrected thermal fixture1/1 covers37 unique cases; test result files retain the original failure and passing rerun transparently. Stateful lock fixture IDs were updated to use returned group IDs. Native icons and vls-hud.png inspected. HUD preview injects only20s cooldown/ammo0 states into cloned native fixture to show real rendering; it does not claim interactive full-scene performance. No standalone build or subjective headphone audition performed. Editor stopped/auto-tick disabled. Astra final acceptance approved.
