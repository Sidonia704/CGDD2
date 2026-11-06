# Temporary component placement bypass — restored

Status: **restored on 2026-11-06**. All six modified source files match their recorded pre-change hashes. The temporary `PlacementTestMode.cs` and its `.meta` were removed. No Unity invocation or tests were performed.

The following is an archived restoration record; no further restoration is pending. Restoration used the `before/` snapshots rather than Git HEAD because these files already contained earlier work. `restore-manifest.json` retains both the pre-change and temporary version hashes. Saved player blueprints were not modified.

Restore these existing files:

- `Assets/_Starframe/Scripts/Runtime/Construction/ShipAssembly.cs`
- `Assets/_Starframe/Scripts/Runtime/Construction/BlockPlacement.cs`
- `Assets/_Starframe/Scripts/Runtime/Construction/BlueprintRepository.cs`
- `Assets/_Starframe/Scripts/Runtime/Construction/App/Missions/DeploymentValidator.cs`
- `Assets/_Starframe/Scripts/Runtime/Construction/Flight/FlightShipRuntime.cs`
- `Assets/_Starframe/Scripts/Runtime/Construction/Flight/FlightTuning.cs`

Remove this round's new `PlacementTestMode.cs` and its `.meta` from the same Construction directory after restoring the callers. This round does not modify saved player blueprints; restoration must not delete or silently repair testing blueprints.

The enabled mode relaxes placement geometry, facing, support, overlap, connectivity and core-count authoring checks across preview, assembly, saving/importing and deployment. Data validity remains checked. Editing/saving without a core is allowed; flight still requires a core. Combat obstruction, damage and detachment rules are unchanged. Multiple cores use one consistent primary core.

No Unity session, compilation or tests were invoked for this round. Only source changes and static review were performed. The `BlockPlacement.cs` baseline was reconstructed from its full pre-change text after its edit; its content is preserved, but the original line-ending bytes were not recorded in advance. Other baseline files were copied before editing.
