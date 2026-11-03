Continuous mission / 2026-11-03

Source backup: before/*.cs.txt. Native config backup written by installer before first save.
Installer: tools/SessionNov03/InstallContinuousMission.cs, InstallContinuousMissionNov03.Run.

Five finite authored rosters retained (53 enemies). continuousStages=true streams the next stage while old contacts survive. No intermission or input capture in the active native mode. All current-stage queued ships deploy before the next roster is queued; existing live/large/capital caps remain. Occupied stage advances after35s if <=3 contacts, or135s regardless of survivors. A fully empty stage can pull the next roster forward after3s rather than waiting35s. Current stage label advances on the first real spawn, not enqueue. Victory requires all stages queued and all53 actors defeated. Per-stage remaining counters retain correct summary when survivors overlap.

HUD: only left-side3/5, no enemy count/countdown/background/buttons. Existing terminal outcome text retained. Menu pause still works; no Enter-wave action.

Mission arena minimum half extent2200 overrides old SaturnRing sandbox2000 through an instantiated tuning copy before boundary mesh/player systems initialize. No shared FlightTuning asset or map placement changed. Spawn anchors are bounded near center and y>=-850; pilots return gradually from .65 to1.0 of central combat radius (.62*arena), with per-slot destinations. Practice AI remains unrestricted by the mission soft return.

Focused checks for root: ArcadeWaveProgressTests (includes append persistence/navigation tests), MissionHUDTests bilingual layout/no extra UI, ArcadeMissionLifecycleTests (legacy explicit false plus new ContinuousStagesOverlapWithoutResettingSurvivingContactsOrLockingPlayerControl). No Unity compile/tests invoked by this worker.
