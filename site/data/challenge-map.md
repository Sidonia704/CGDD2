# How the challenge changes

Look first at whether win and loss change, then at whether the player's verb changes. If neither changes, it is the same game, just tighter or looser.

| Change | Win/loss changed | Verb changed | Data | Script.field |
| --- | --- | --- | --- | --- |
| Baseline: assemble a connected ship, then pilot, aim and fire in the practice range | Baseline: no terminal victory; core destruction triggers a practice respawn | Build, test, diagnose and revise | playerRespawnSeconds=3; botRespawnSeconds=3 | BattleTuning.playerRespawnSeconds; BattleTuning.botRespawnSeconds |
| Baseline: hull damage interrupts recovery; surviving support enables reconstruction | Baseline, unchanged | Keep flying and find a recovery opportunity | outOfCombatDelay=1; recoveryTick=0.1 | BattleTuning.outOfCombatDelay; BattleTuning.recoveryTick |
| Baseline: opponents wait, fire short bursts and pause between bursts | Baseline, unchanged | Manoeuvre and time attacks | botReactionSeconds=1.2; botFireBurstSeconds=0.3; botFirePauseSeconds=1.8 | BattleTuning.botReactionSeconds; BattleTuning.botFireBurstSeconds; BattleTuning.botFirePauseSeconds |
| Baseline: boost requests shared power; practice can bypass scarcity | Baseline, unchanged | Choose when to boost and when to fire | boostPowerWatts=100; boostMultiplier=1.5; unlimitedRangePower=true | FlightTuning.boostPowerWatts; FlightTuning.boostMultiplier; BattleTuning.unlimitedRangePower |

Recorded on 2026-10-22 from the current scripts. These rows document an existing baseline, not a new gameplay change. Numbers are C# field defaults, not inspected ScriptableObject asset overrides. Mission validation and result reporting exist, but a completed wave-based mission is not claimed here.
