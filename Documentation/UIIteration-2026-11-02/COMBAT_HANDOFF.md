# Combat UI iteration

Owned source files: `RangeHUD.cs`, `SphereRadarGraphic.cs`, `TargetAimGraphic.cs`, `RangeLoadingOverlay.cs`, `RangeAppNavigation.cs`. Exact pre-edit copies are under `before/combat/`.

- Compact top-left speed/ship summary; detailed six-axis and damage telemetry remains behind F1. Real sensor range replaces the stale hardcoded 3 km display.
- Bottom-center single-row weapon-group strip supports 1–8 groups with explicit ON/OFF plus weapon count. No key, group activation, firing or energy rules changed.
- Missile readiness, flare availability and incoming/lock warnings have dedicated areas; enemy-lock warning is amber and incoming missiles red. Self/target status monitors and left shield/energy remain in their established corners.
- Enemy markers retain red, own reticle retains accent, and scenery hit feedback stays white. Target tether leaves empty space around both endpoints. Target detection triangles no longer obscure an already-locked core.
- Radar reduces background rings/ticks and keeps the complete altitude stem within its graphic rectangle. Contact projection and detection rules are unchanged.
- Input cue is less bulky; hit contraction and extension respect the new HUD motion preference without modifying flight/aim positions. Hit feedback remains rate-limited.
- Shared UI typography/scaler/control colours are applied, all non-interactive elements remain non-raycast, and ESC menu selects Resume for keyboard navigation.
- Loading shows stage 01/02 and 02/02 instead of a seemingly precise percentage for unequally weighted tasks; real async progress, object counts, input ownership and completion behaviour remain.
- Live practice modes use numbered selectable cards and an explicit ACTIVE state.

Root owns native compilation, screenshots and tests. Suggested verification: AppFlowTests (number-key HUD state and live practice), RangeLoadingOverlayTests, MultiTargetRangeTests, TargetMarkerGraphicTests, FlightAimStabilityTests. No native assets or blueprints were modified by this agent.

Integration follow-up: shield, energy, missile and loading fills are normalized children of their tracks (no fixed Canvas-unit widths). Compact rich-text sizes are relative to the user's text size. The two RangeMenuFlow fixtures now wait until the loading blocker has actually closed; RangeLoadingOverlayTests unloads authored scene callbacks before exercising pure overlay reentry. Assertions remain unchanged. These fixture-only changes address asynchronous-loading readiness and prior-scene callback contamination, without altering runtime input ownership. Test originals are in `before/tests/`.
