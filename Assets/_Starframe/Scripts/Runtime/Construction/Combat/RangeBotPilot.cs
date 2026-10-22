// @role Steer range opponents, avoid obstacles and plan firing opportunities.
// @calls InterceptSolver, CylindricalHitQuery, ShipWeapons

using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;

namespace Starframe.Combat
{
    public enum RangeBotPhase { Stationary, Cruise, Reposition, Attack, Breakaway }

    // This pilot only supplies the same movement, steering, boost and trigger requests as a player.
    // Damage, acceleration, collision, ammunition timing and electrical allocation stay in their shared systems.
    public sealed class RangeBotPilot
    {
        readonly List<WeaponAimProfile> profiles = new List<WeaponAimProfile>();
        ShipPartState gun;
        ModuleDefinition module;
        WeaponDefinition weapon;
        RangeBotMode lastMode = (RangeBotMode)(-1);
        Quaternion previousRotation;
        Vector3 measuredAngularVelocity, avoidance, waypoint, flank, previousAimDirection;
        bool hasRotation, hasAimDirection;
        int targetLife = -1;
        double nextDecision, phaseUntil, attackStarted,engageStarted;
        public RangeBotPhase Phase { get; private set; }
        public Vector3 DesiredVelocity { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public bool Fire { get; private set; }
        public bool Boost { get; private set; }

        public void Reset()
        {
            gun = null; module = null; weapon = null; targetLife = -1;
            hasRotation = hasAimDirection = false; lastMode = (RangeBotMode)(-1); nextDecision = phaseUntil = 0;
            measuredAngularVelocity = avoidance = DesiredVelocity = Vector3.zero;
            Fire = Boost = false;
        }

        public void Step(RangeCombatWorld.Actor self, RangeCombatWorld.Actor target,
            IReadOnlyList<RangeCombatWorld.Actor> actors, IReadOnlyList<DebrisCluster> wrecks,
            BattleTuning rules, FlightTuning tuning, RangeBotMode mode, Vector3 targetCoreVelocity,
            double time, float dt)
        {
            Fire = Boost = false;
            if (!self.ship.IsAlive)
            {
                DesiredVelocity = Vector3.zero;
                self.fire = false;
                self.aimingAtPlayer = false;
                self.flight.SetExternalInput(Vector3.zero, Vector2.zero);
                hasRotation = hasAimDirection = false;
                return;
            }
            MeasureRotation(self.body.rotation, dt);
            bool targetAlive = target != null && target.ship.IsAlive;
            int life = targetAlive ? target.ship.LifeId : -1;
            bool changed = mode != lastMode || life != targetLife;
            if (changed)
            {
                lastMode = mode; targetLife = life; nextDecision = 0;
                hasAimDirection = false;attackStarted=engageStarted=time;
                Phase = mode == RangeBotMode.FlyOnly ? RangeBotPhase.Cruise : RangeBotPhase.Attack;
                phaseUntil = mode == RangeBotMode.FlyOnly || !targetAlive ? time : time + Mathf.Max(.2f, rules.botAttackSeconds);
            }
            bool stationary = mode == RangeBotMode.Stationary || mode == RangeBotMode.StationaryFire;
            if (time >= nextDecision)
            {
                // The slot offset spreads expensive weapon/obstacle decisions across physics steps.
                float interval = Mathf.Clamp(rules.botPilotDecisionSeconds, .05f, .5f);
                nextDecision = time + interval * (.75f + .5f * ((self.spawnSlot * .61803399f) % 1));
                SelectWeapon(self, targetAlive ? target : null, targetCoreVelocity);
                Plan(self, target, targetAlive, rules, tuning, mode, time);
                avoidance = stationary ? Vector3.zero : AvoidObstacles(self, actors, wrecks);
            }

            Vector3 desiredDirection = self.body.rotation * Vector3.forward;
            Vector3 aimOrigin = self.ship.CorePhysicsPosition;
            Vector3 barrelDirection = desiredDirection;
            bool hasIntercept = false;
            AimPoint = targetAlive ? target.ship.CorePhysicsPosition : self.body.position + desiredDirection * 100;
            if (targetAlive && gun != null && gun.present && module && weapon)
            {
                var matrix = CylindricalHitQuery.PhysicsMatrix(gun.view.GetComponent<Collider>());
                aimOrigin = matrix.MultiplyPoint3x4(module.muzzleLocalPosition);
                barrelDirection = matrix.MultiplyVector(Vector3.forward).normalized;
                hasIntercept = InterceptSolver.TrySolve(aimOrigin, target.ship.CorePhysicsPosition,
                    targetCoreVelocity, weapon.projectileSpeed, weapon.range, out var intercept, out _);
                if (hasIntercept) AimPoint = intercept;
            }

            float cruise = self.ship.Performance.maxSpeed * Mathf.Clamp01(rules.botCruiseSpeedFraction);
            bool boundaryTurn = false;
            if (stationary)
            {
                Phase = RangeBotPhase.Stationary;
                DesiredVelocity = Vector3.zero;
            }
            else
            {
                Vector3 route = waypoint - self.body.position;
                Vector3 requestedVelocity = route.normalized * cruise;
                if (Phase == RangeBotPhase.Attack && targetAlive)
                {
                    route = AimPoint - self.body.position;
                    float separation = Vector3.Distance(self.ship.CorePhysicsPosition, target.ship.CorePhysicsPosition);
                    float preferred = PreferredRange(self, target, weapon,rules);
                    float speed = self.ship.Performance.maxSpeed * Mathf.Clamp01(rules.botAttackSpeedFraction);
                    // Match the target's motion and hold firing distance instead of always charging past it.
                    float closingSpeed=Mathf.Clamp((separation-preferred)*.9f,-speed*.35f,speed);
                    requestedVelocity=target.flight.Velocity+route.normalized*closingSpeed
                        +flank*speed*Mathf.Clamp01(rules.botStrafeSpeedFraction);
                }
                DesiredVelocity = Vector3.ClampMagnitude(requestedVelocity + avoidance, cruise);
                float braking = Mathf.Max(.2f, self.ship.Performance.assistAcceleration / 3);
                Vector3 unconstrained = DesiredVelocity;
                DesiredVelocity = ConstrainToArena(self.body.position, DesiredVelocity, self.flight.Velocity,
                    tuning.arenaHalfExtent, self.ship.HullRadius, braking);
                for(int i=0;i<3;i++)
                    if(DesiredVelocity[i]*unconstrained[i]<-.01f || (Mathf.Abs(unconstrained[i])<.01f&&Mathf.Abs(DesiredVelocity[i])>1))boundaryTurn=true;
                if (DesiredVelocity.sqrMagnitude > .01f) desiredDirection = DesiredVelocity.normalized;
            }

            bool engage = targetAlive && (mode == RangeBotMode.StationaryFire || mode == RangeBotMode.Combat);
            bool aiming = engage && (stationary || (Phase == RangeBotPhase.Attack && !boundaryTurn));
            if (aiming) desiredDirection = (AimPoint - aimOrigin).normalized;
            Vector3 from = aiming && gun != null && gun.present ? barrelDirection : self.body.rotation * Vector3.forward;
            if (mode == RangeBotMode.Stationary) desiredDirection = from;
            float error = Vector3.Angle(from, desiredDirection);
            if (engage && hasIntercept)
            {
                var profile = new WeaponAimProfile { weapon = weapon, origin = aimOrigin, direction = barrelDirection };
                Fire = TryFiringSolution(profile, target.ship.CorePhysicsPosition, targetCoreVelocity,
                    rules.botMaxFireAngleDegrees, out _,Mathf.Clamp(target.ship.HullRadius*rules.botFireTargetRadiusFraction,.4f,8))
                    && ClearFireLane(self, target, actors, wrecks, aimOrigin, AimPoint);
            }
            if(Fire&&mode==RangeBotMode.Combat)
            {
                // Give the player predictable gaps instead of perfect, uninterrupted battery fire.
                double elapsed=time-engageStarted-Mathf.Max(0,rules.botReactionSeconds)-self.spawnSlot%5*.2f;
                float burst=Mathf.Max(.05f,rules.botFireBurstSeconds),pause=Mathf.Max(0,rules.botFirePauseSeconds);
                Fire=elapsed>=0&&elapsed%(burst+pause)<burst;
            }
            float reserve = Mathf.Max(0, tuning.boostPowerWatts) * .5f;
            bool boostPower = rules.unlimitedRangePower || self.ship.Power.Energy > reserve
                || self.ship.Power.Generation >= tuning.boostPowerWatts;
            Boost = !stationary && boostPower && error < 38 && avoidance.sqrMagnitude < cruise * cruise * .16f
                && (Phase == RangeBotPhase.Breakaway || Phase == RangeBotPhase.Reposition || Phase == RangeBotPhase.Cruise
                    || (Phase==RangeBotPhase.Attack&&targetAlive&&Vector3.Distance(self.body.position,target.body.position)>PreferredRange(self,target,weapon,rules)*1.4f))
                && DesiredVelocity.magnitude > self.flight.CurrentSpeed + 3;
            Vector3 aimAngularVelocity = Vector3.zero;
            if (aiming && hasIntercept)
            {
                if (hasAimDirection && dt > .0001f)
                {
                    aimAngularVelocity = Quaternion.Inverse(self.body.rotation)
                        * DirectionRotationVector(previousAimDirection, desiredDirection) / dt;
                }
                previousAimDirection = desiredDirection;
                hasAimDirection = true;
            }
            else hasAimDirection = false;
            var aim = AimInput(self.body.rotation, from, desiredDirection, measuredAngularVelocity,
                self.ship.Performance.angularRate * self.flight.BoostScale, tuning, aimAngularVelocity);
            var move = stationary ? Vector3.zero : MovementInput(self.body.rotation, DesiredVelocity,
                self.flight.Velocity, self.ship.Performance, self.flight.BoostScale);
            self.flight.SetExternalInput(move, aim, 0, Boost);
            // Preserve the old public diagnostics used by existing callers.
            self.fire = Fire; self.aimingAtPlayer = aiming||Fire; self.look = desiredDirection;
            self.waypoint = waypoint; self.nextDecision = (float)nextDecision;
        }

        void MeasureRotation(Quaternion rotation, float dt)
        {
            measuredAngularVelocity = Vector3.zero;
            if (hasRotation && dt > .0001f)
            {
                var delta = Quaternion.Inverse(previousRotation) * rotation;
                measuredAngularVelocity = RotationVector(delta) / dt;
            }
            previousRotation = rotation; hasRotation = true;
        }

        void SelectWeapon(RangeCombatWorld.Actor self, RangeCombatWorld.Actor target, Vector3 targetVelocity)
        {
            self.weapons.GetAimProfiles(profiles);
            gun = null; module = null; weapon = null;
            float best = float.NegativeInfinity;
            foreach (var profile in profiles)
            {
                if (!profile.weapon) continue;
                float score = profile.gunCount;
                if (target != null)
                {
                    bool solution = InterceptSolver.TrySolve(profile.origin, target.ship.CorePhysicsPosition,
                        targetVelocity, profile.weapon.projectileSpeed, profile.weapon.range, out var point, out _);
                    score += solution ? 1000 : 0;
                    score += Vector3.Dot(profile.direction, (point - profile.origin).normalized) * 100;
                }
                if (score <= best) continue;
                foreach (var part in self.ship.Parts)
                    if (part.present && part.record.id == profile.partId)
                    {
                        gun = part; module = self.ship.Tuning.GetDefinition(part.record.shape);
                        weapon = profile.weapon; best = score; break;
                    }
            }
        }

        void Plan(RangeCombatWorld.Actor self, RangeCombatWorld.Actor target, bool targetAlive,
            BattleTuning rules, FlightTuning tuning, RangeBotMode mode, double time)
        {
            float sign = self.spawnSlot % 2 == 0 ? 1 : -1;
            Vector3 centre = targetAlive ? target.ship.CorePhysicsPosition : self.initialPosition;
            Vector3 radial = self.ship.CorePhysicsPosition - centre;
            if (radial.sqrMagnitude < .01f) radial = self.body.rotation * Vector3.back;
            Vector3 up = Mathf.Abs(Vector3.Dot(radial.normalized, Vector3.up)) > .92f ? Vector3.right : Vector3.up;
            flank = Vector3.Cross(radial.normalized, up).normalized * sign;
            if (mode == RangeBotMode.FlyOnly || !targetAlive)
            {
                Phase = RangeBotPhase.Cruise;
                if (time >= phaseUntil || (waypoint - self.body.position).sqrMagnitude < 144)
                {
                    float angle = (float)time * .23f + self.spawnSlot * 2.4f;
                    float radius = Mathf.Min(tuning.arenaHalfExtent * .28f,
                        Mathf.Max(65, self.ship.Performance.maxSpeed * 1.3f));
                    waypoint = centre + new Vector3(Mathf.Sin(angle) * radius, Mathf.Sin(angle * .7f) * radius * .45f,
                        Mathf.Cos(angle) * radius);
                    phaseUntil = time + 2.2f + .2f * (self.spawnSlot % 4);
                }
                waypoint = ClampWaypoint(waypoint, tuning.arenaHalfExtent, self.ship.HullRadius);
                return;
            }
            if (mode == RangeBotMode.Stationary || mode == RangeBotMode.StationaryFire) return;
            float preferred = PreferredRange(self, target, weapon,rules);
            float collisionDistance=self.ship.HullRadius+target.ship.HullRadius+6;
            float close = Mathf.Max(collisionDistance+6, preferred * .15f);
            float distance = radial.magnitude;
            float closing = Vector3.Dot(self.flight.Velocity - target.flight.Velocity, -radial.normalized);
            float stopping = closing > 0 ? closing * closing / (2 * Mathf.Max(.5f, self.ship.Performance.assistAcceleration)) : 0;
            if(Phase==RangeBotPhase.Attack&&distance>preferred*1.4f)phaseUntil=time+Mathf.Max(.2f,rules.botAttackSeconds);
            if (Phase == RangeBotPhase.Attack && (distance<collisionDistance
                || (time-attackStarted>.9f&&distance<close+Mathf.Min(stopping,preferred*.12f)) || time>=phaseUntil))
            {
                Phase = RangeBotPhase.Breakaway;
                phaseUntil = time + Mathf.Max(.2f, rules.botBreakawaySeconds);
                waypoint = centre + (radial.normalized + flank * .8f).normalized * Mathf.Max(preferred, close * 1.5f);
            }
            else if ((Phase == RangeBotPhase.Breakaway||Phase==RangeBotPhase.Reposition) && time>=phaseUntil)
            {
                // A pursuer must not keep the bot stuck in an indefinite escape waiting for separation.
                Phase = RangeBotPhase.Attack;
                attackStarted=time;
                phaseUntil = time + Mathf.Max(.2f, rules.botAttackSeconds) + .2f * (self.spawnSlot % 4);
            }
            waypoint = ClampWaypoint(waypoint, tuning.arenaHalfExtent, self.ship.HullRadius);
        }

        static float PreferredRange(RangeCombatWorld.Actor self, RangeCombatWorld.Actor target, WeaponDefinition weapon,BattleTuning rules)
        {
            float clearance = self.ship.HullRadius + target.ship.HullRadius + 30;
            return weapon ? Mathf.Max(clearance, Mathf.Min(weapon.range * Mathf.Clamp(rules.botEngagementRangeFraction,.1f,.6f), 320)) : Mathf.Max(clearance, 120);
        }

        static Vector3 ClampWaypoint(Vector3 point, float halfExtent, float radius)
        {
            float limit = Mathf.Max(1, halfExtent - radius - 20);
            for (int i = 0; i < 3; i++) point[i] = Mathf.Clamp(point[i], -limit, limit);
            return point;
        }

        static Vector3 AvoidObstacles(RangeCombatWorld.Actor self, IReadOnlyList<RangeCombatWorld.Actor> actors,
            IReadOnlyList<DebrisCluster> wrecks)
        {
            Vector3 result = Vector3.zero;
            float speed = Mathf.Max(12, self.flight.CurrentSpeed), horizon = 1.6f;
            for (int i = 0; i < actors.Count; i++)
            {
                var other = actors[i];
                if (other == self || !other.ship.IsAlive) continue;
                Vector3 offset = self.body.position - other.body.position;
                Vector3 relativeVelocity = self.flight.Velocity - other.flight.Velocity;
                float t = relativeVelocity.sqrMagnitude < .01f ? 0
                    : Mathf.Clamp(-Vector3.Dot(offset, relativeVelocity) / relativeVelocity.sqrMagnitude, 0, horizon);
                Vector3 nearest = offset + relativeVelocity * t;
                float safe = self.ship.HullRadius + other.ship.HullRadius + 6;
                if (nearest.sqrMagnitude > safe * safe * 2.25f || offset.magnitude > speed * horizon + safe * 2) continue;
                Vector3 away = nearest.sqrMagnitude > .01f ? nearest.normalized : offset.normalized;
                if (away.sqrMagnitude < .01f) away = self.spawnSlot % 2 == 0 ? Vector3.right : Vector3.left;
                Vector3 side = Vector3.Cross(away, Vector3.up);
                result += (away + side * .55f) * speed * Mathf.Clamp01(1.5f - nearest.magnitude / safe) * (1 - t / (horizon * 2));
            }
            Vector3 future = self.body.position + self.flight.Velocity * .8f;
            for (int i = 0; i < wrecks.Count; i++)
            {
                var wreck = wrecks[i];
                if (!wreck) continue;
                Bounds bounds = wreck.WorldBounds;
                float safe = self.ship.HullRadius + 7;
                if (bounds.SqrDistance(future) > safe * safe) continue;
                Vector3 away = future - bounds.ClosestPoint(future);
                if (away.sqrMagnitude < .01f) away = self.body.position - bounds.center;
                if (away.sqrMagnitude < .01f) away = Vector3.up;
                result += away.normalized * speed * 1.4f;
            }
            return result;
        }

        static bool ClearFireLane(RangeCombatWorld.Actor self, RangeCombatWorld.Actor target,
            IReadOnlyList<RangeCombatWorld.Actor> actors, IReadOnlyList<DebrisCluster> wrecks,
            Vector3 origin, Vector3 point)
        {
            Vector3 path = point - origin;
            float length = path.magnitude;
            if (length < .001f) return false;
            Vector3 direction = path / length;
            for (int i = 0; i < actors.Count; i++)
            {
                var other = actors[i];
                if (other == self || other == target || !other.ship.IsAlive) continue;
                float along = Vector3.Dot(other.ship.CorePhysicsPosition - origin, direction);
                if (along <= 0 || along >= length) continue;
                float radius = other.ship.HullRadius + .5f;
                if ((origin + direction * along - other.ship.CorePhysicsPosition).sqrMagnitude < radius * radius) return false;
            }
            var ray = new Ray(origin, direction);
            for (int i = 0; i < wrecks.Count; i++)
                if (wrecks[i] && wrecks[i].WorldBounds.IntersectRay(ray, out float distance) && distance < length) return false;
            return true;
        }

        public static bool TryFiringSolution(WeaponAimProfile profile, Vector3 targetCore, Vector3 targetVelocity,
            float maxAngleDegrees, out Vector3 point,float targetRadius=.4f)
        {
            point = targetCore;
            if (!profile.weapon || !InterceptSolver.TrySolve(profile.origin, targetCore, targetVelocity,
                    profile.weapon.projectileSpeed, profile.weapon.range, out point, out _)) return false;
            Vector3 direction = point - profile.origin;
            if (direction.sqrMagnitude < .000001f || profile.direction.sqrMagnitude < .000001f) return false;
            float tolerance = Mathf.Atan2(Mathf.Max(.4f,targetRadius) + profile.weapon.projectileDiameter * .5f, direction.magnitude) * Mathf.Rad2Deg;
            return DirectionRotationVector(profile.direction, direction).magnitude
                <= Mathf.Min(Mathf.Max(0, maxAngleDegrees), tolerance);
        }

        public static Vector3 MovementInput(Quaternion rotation, Vector3 desiredVelocity, Vector3 currentVelocity,
            ShipPerformance performance, float boostScale = 1)
        {
            Vector3 error = Quaternion.Inverse(rotation) * (desiredVelocity - currentVelocity);
            Vector3 input = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                float acceleration = error[i] >= 0 ? performance.accelerationPositive[i] : performance.accelerationNegative[i];
                // Zero input invokes the controller's shared manoeuvre braking on an axis without an engine.
                if (acceleration > .001f && Mathf.Abs(error[i]) > .25f)
                    input[i] = Mathf.Clamp(error[i] / Mathf.Max(.5f, acceleration * Mathf.Max(1, boostScale) * .45f), -1, 1);
            }
            return input;
        }

        public static Vector3 ConstrainToArena(Vector3 position, Vector3 desiredVelocity, Vector3 currentVelocity,
            float halfExtent, float radius, float brakingAcceleration)
        {
            float limit = Mathf.Max(1, halfExtent - radius - 4), brake = Mathf.Max(.2f, brakingAcceleration);
            float speed = Mathf.Max(12, desiredVelocity.magnitude);
            for (int i = 0; i < 3; i++)
            {
                float sign = Mathf.Sign(position[i]), remaining = limit - Mathf.Abs(position[i]);
                float outward = Mathf.Max(0, currentVelocity[i] * sign);
                float stopping = outward * outward / (2 * brake);
                if (remaining < 16 + stopping)
                    desiredVelocity[i] = -sign * Mathf.Min(speed, Mathf.Max(8, outward));
                else if (desiredVelocity[i] * sign > 0)
                    desiredVelocity[i] = sign * Mathf.Min(Mathf.Abs(desiredVelocity[i]), Mathf.Sqrt(2 * brake * Mathf.Max(0, remaining - 16)));
            }
            // At a corner, independently reversing each axis must not increase the requested speed.
            return Vector3.ClampMagnitude(desiredVelocity, speed);
        }

        static Vector3 RotationVector(Quaternion rotation)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            float sine = vector.magnitude;
            if (sine < 1e-8f) return Vector3.zero;
            float degrees = 2 * Mathf.Atan2(sine, rotation.w) * Mathf.Rad2Deg;
            if (degrees > 180) degrees -= 360;
            return vector * (degrees / sine);
        }

        static Vector3 DirectionRotationVector(Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude < 1e-12f || to.sqrMagnitude < 1e-12f) return Vector3.zero;
            from.Normalize(); to.Normalize();
            var cross = Vector3.Cross(from, to);
            float sine = cross.magnitude, cosine = Vector3.Dot(from, to);
            // atan2 preserves the small angles that acos/quaternion ToAngleAxis round to zero.
            if (sine > 1e-7f) return cross * (Mathf.Atan2(sine, cosine) * Mathf.Rad2Deg / sine);
            return cosine >= 0 ? cross * Mathf.Rad2Deg : RotationVector(Quaternion.FromToRotation(from, to));
        }

        public static Vector2 AimInput(Quaternion rotation, Vector3 from, Vector3 desired, Vector3 angularVelocity,
            float angularRate, FlightTuning tuning, Vector3 desiredAngularVelocity = default)
        {
            if (desired.sqrMagnitude < .0001f || angularRate <= .001f) return Vector2.zero;
            var local = Quaternion.Inverse(rotation) * DirectionRotationVector(from, desired);
            // A moving sightline needs its own angular velocity even at zero heading error.
            // Damping absolute ship velocity would leave a permanent error behind a crossing core.
            Vector3 requestedRate = local / .3f + desiredAngularVelocity
                - (angularVelocity - desiredAngularVelocity) * .8f;
            Vector2 rate = new Vector2(requestedRate.y, -requestedRate.x) / angularRate;
            float strength = Mathf.Clamp01(rate.magnitude);
            if (strength < .0001f) return Vector2.zero;
            float deadZone = Mathf.Clamp(tuning.aimInputDeadZone, 0, .4f);
            float stick = deadZone + (1 - deadZone)
                * Mathf.Pow(strength, 1 / Mathf.Max(.1f, tuning.aimResponseExponent));
            return rate.normalized * stick;
        }
    }
}
