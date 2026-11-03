# Temporary flight-range map

Open `Assets/_Starframe/Scenes/Testing/FlightRange.unity`, or enter the range through the normal home or assembly buttons. The `Temporary Map Sandbox` root places the complete reference layout at Y=-1100 m, making scenery approximately Y=-1520..-1102. The arena is temporarily 4000 m wide; the spawn is `(0,-600,-1100)`, pitched 25 degrees toward the base, leaving substantial empty space above.

Disable that root **before Play** to restore the original spawn and arena. The original FlightTuning asset is never changed; the optional arena override uses a runtime copy.

Intact scenery keeps baked exterior visuals during approach. This range uses a 3,500 enabled-collider budget, a 120 m detailed-collision distance and a wider exit band. Exact collision partitions remain cached, but precise components are only created when needed. Large-body first destructive hits can still incur visual rebuild cost. Map hits now produce subtle white reticle feedback; ship/shield hits retain red feedback. White strength is adjustable through `UiTheme.sceneryHitFeedbackStrength`.

`RangeLoadingOverlay` covers asynchronous scene loading and actual map building, waits before initializing flight/AI, and restores input after completion. Its progress is phase-based rather than an estimated time remaining. Both English and Chinese are supported. [Live loading capture](loading-progress.png) and [launch check](loading-check.json) verify the normal path.

The measured launch was a **warm Editor reload**, 13.952 seconds, not a cold Player benchmark. All 2,854 objects were loaded; initial actual collider components were 2,854 while logical detailed boxes remained 247,723. Player creation waited for readiness and input was restored. The clean runtime check had no Console errors. Focused deferred-map tests passed 14/14; earlier scenery/projectile hit-feedback checks passed 12/12 and 15/15 respectively. No broad AI or flight tuning changes were made.

Authoring helper: `tools/ReferenceMap20261101/RangeIntegration.cs`. The main reference layout, its standalone scene, and original models remain independent of this temporary range placement.
