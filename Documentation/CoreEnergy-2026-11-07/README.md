# Core emission and propulsion energy — 2026-11-07

Normal propulsion now requests up to70W (one eighth of560W boost) for the whole ship, scaled by requested movement/steering/braking demand. Boost uses560W total propulsion demand, not560+70. The shared power allocator prioritizes shield recovery, powered propulsion, then weapons. No new per-module authored propulsion charge is added; each thruster detail explicitly labels the whole-ship scope.

FlightTuning.boostPowerCostMultiplier=4, PropulsionPowerDemandWatts=BoostPowerDemandWatts/8. Actual normal and boost force/exhaust follow their allocated power fraction. Existing temporary200%speed cap/overspeed recovery remain.

Core nativegeneration50W/storage700J equal half the small Power100W/1400J. CoreGreen emission keyword/GI flags enabled, HDR emission(0.12,2.4,0.04). Bloom profiles unchanged (.65,threshold1.2). Authoring tools/FigmaUIAssets.cs matches native asset installation.

Verification limited to compilation and UI/visual rendering as requested. No gameplay or numeric tests, no Play session. thruster-details.png and core-details.png show all rows completely. core-after-assembly.png and core-after-flight.png use actual core mesh/material and native post FX; final diagnostics in core-after-capture.json. Earlier black/flat captures resulted from offscreen initialization/volume-stack setup and were replaced after fixing the external helper; do not count first before capture as valid comparison. Final saturated green glow/halos accepted by Astra. UI fixtures omit normal scene content and telemetry; no complete live-game screenshot claim.
