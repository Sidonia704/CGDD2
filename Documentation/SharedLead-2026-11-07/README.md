# Shared projectile-speed leads — 2026-11-07

Enabled, present, unblocked projectile mounts are grouped by exact speed across definitions and sizes. The player core is the common reference; range is the maximum range among contributors. Profiles remain independent of which mouse button is held, while disabled groups contribute nothing. HUD cache and shape are keyed by speed; representative-weapon replacement does not move the shared origin. Bots keep the existing per-definition representative real muzzle API.

At actual discharge (including rail windup completion), solve both common-origin and individual-muzzle interception using the target core's estimated velocity. Apply only the difference of those directions to the actual barrel direction. Thus a correctly aimed shared pip converges normal mounts, and an incorrectly aimed pip does not automatically hit. Tiny differences use atan2/AngleAxis rather than Unity FromToRotation's near-parallel shortcut. Correction is player-only, forward-mount-only and capped at4degrees; extreme lateral/close mounting configurations retain residual error. Life changes/clearing target invalidate correction. Own-hull clearance and ordinary projectile/scenery collisions remain unchanged. Projectiles still do not inherit the shooter's velocity.

Native same-family speeds are already uniform: Gun1/2/3 2700m/s, LaserRapidCannon/Laser2/3 6600m/s, Railgun1/2/3 10800m/s. Only stale installer speed assignments were corrected. Native damage/rates/VFX and user balance assets were not edited.

## Verification

- 64 targeted EditMode checks passed: SharedLeadPointTests22, DirectAimAssistTests9, CoreLeadBallisticsTests4, WeaponGroupRuntimeTests16, WeaponBalanceVlsTests13. JSON results are retained here.
- SharedLeadPointTests includes actual three-size launches hitting a moving one-cell core; mathematical cases cover front/rear/side/vertical offsets, exact near-but-different speeds, disabled/blocked/destroyed mounts, maximum range, aim error retention and correction bounds.
- SharedLeadPreview uses temporary clone-only fixtures, the real sensors, TargetLockSystem and TargetAimGraphic. preview-pipeline.json confirms same-speed600/three mounts=>1lead;600+900=>2; disable900=>1. Three PNGs show the actual generated HUD. Temporary geometry is hidden to avoid fixture materials affecting HUD inspection. No game scene or asset was saved.
- Astra accepted source and final screenshots; compilation clean, Editor stopped. No full mission or gameplay Play session.
- Broader legacy AimClearanceTests:17pass/4fail. Three arbitrary orientation placement expectations conflict with the established forward-only non-missile rule; the other assumes Gun1 native offsets.Length==2 although the approved single-chain configuration has0. Source/asset diff confirms this revision did not alter those rules or assets. Astra classified these as existing specification mismatches and approved retaining the raw failure evidence without widening scope.

Initial validation found a missing support cube in the test obstruction fixture and near-parallel quaternion precision loss. Both were fixed before the final passing checks. The later fixture synchronization change was recompiled and all22new cases rerun successfully.
