# Missile guidance correction

Source: `MissileGuidance.cs`, `ProjectileWorld.cs`; originals and previous guidance test source are in `before/`.

Confirmed issues: missile lead used kinematic Rigidbody velocity instead of the flight controller's authoritative translation, omitted core-point rotational motion, approximated interception as distance/current speed capped at1.2seconds, and demanded bang-bang stopping rates even for tiny residual aim errors.

Correction: cache target body/controller per launched missile; derive core-point motion from translation plus world angular velocity cross core offset. Predict a finite accelerating-speed intercept with two scalar refinements and analytic constant-speed solves. Unreachable targets remain pursuit targets. Small-angle demand converges proportionally; terminal capture tightens as time-to-go shrinks. Existing maximum turn speed/acceleration, ejection delay, seeker cone/loss grace, permanent expiry, flare probability and single-roll behaviour, and swept first-contact collision are preserved. No teleportation, automatic impact or proximity fuse added. Coasting-missile warning uses the same correct core-point velocity.

Six new focused guidance scenarios cover three missile-tier crossing trajectories, near-alignment settling,30/120Hz capture and an outrunning target with finite guidance lifetime. Existing guidance, actual projectile damage/cover/flare tests remain unchanged. Root performs native compilation/tests and writes native tuning; this agent does not modify weapon assets or data values. No new per-tick allocation is introduced by guidance.
