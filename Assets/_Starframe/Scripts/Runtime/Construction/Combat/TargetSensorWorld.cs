// @role Cache surviving ship signatures, share discovery hysteresis and maintain real opponent target locks.
// @calls FlightShipRuntime, RangeCombatWorld, BattleTuning, BlockModules

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Combat
{
    /// <summary>Discovery is omnidirectional. View/aim restrictions apply only when acquiring a lock.</summary>
    public sealed class TargetSensorWorld : IDisposable
    {
        public readonly struct EnemyLock
        {
            public readonly FlightShipRuntime Observer, Target;
            public EnemyLock(FlightShipRuntime observer, FlightShipRuntime target) { Observer = observer; Target = target; }
        }

        sealed class Contact
        {
            public int life;
            public bool detected;
            public float outsideSeconds;
        }

        sealed class ObserverState
        {
            public FlightShipRuntime ship;
            public RangeCombatWorld.Actor actor;
            public int life = -1, signatureRevision = -1;
            public bool signatureDirty = true;
            public Vector3 position, scale;
            public float radius, range;
            public Action changed;
            public Action<ShipPartState> partChanged;
            public readonly Dictionary<FlightShipRuntime, Contact> contacts = new Dictionary<FlightShipRuntime, Contact>();
            public readonly List<FlightShipRuntime> detected = new List<FlightShipRuntime>();
            public FlightShipRuntime lockTarget;
            public int lockLife;
            public float alignedSeconds;
            public bool locked;
        }

        static readonly FlightShipRuntime[] NoTargets = Array.Empty<FlightShipRuntime>();
        readonly BattleTuning tuning;
        readonly Dictionary<FlightShipRuntime, ObserverState> states = new Dictionary<FlightShipRuntime, ObserverState>();
        readonly List<ObserverState> ordered = new List<ObserverState>();
        readonly HashSet<FlightShipRuntime> active = new HashSet<FlightShipRuntime>();
        readonly List<FlightShipRuntime> removed = new List<FlightShipRuntime>();
        readonly List<EnemyLock> enemyLocks = new List<EnemyLock>();
        public IReadOnlyList<EnemyLock> EnemyLocks => enemyLocks;
        public float MaximumRange => Mathf.Max(Mathf.Max(1, tuning ? tuning.sensorMinimumRange : 1000), tuning ? tuning.sensorMaximumRange : 6000);
        public float ExitRangeMultiplier => Mathf.Max(1, tuning ? tuning.sensorExitRangeMultiplier : 1.1f);
        public float LossDelay => Mathf.Max(0, tuning ? tuning.sensorLossDelay : .5f);
        public event Action<FlightShipRuntime, FlightShipRuntime> TargetDiscovered;

        public TargetSensorWorld(BattleTuning rules) { tuning = rules; }

        public static float RangeForRadius(float radius, BattleTuning rules)
        {
            float minimum = Mathf.Max(1, rules ? rules.sensorMinimumRange : 1000);
            float maximum = Mathf.Max(minimum, rules ? rules.sensorMaximumRange : 6000);
            float reference = Mathf.Max(.01f, rules ? rules.sensorReferenceRadius : 10);
            float baseline = Mathf.Max(1, rules ? rules.lockRange : 3000);
            return Mathf.Clamp(baseline * Mathf.Sqrt(Mathf.Max(0, radius) / reference), minimum, maximum);
        }

        ObserverState StateFor(FlightShipRuntime ship)
        {
            if (!states.TryGetValue(ship, out var state))
            {
                state = new ObserverState { ship = ship };
                state.changed = () => state.signatureDirty = true;
                state.partChanged = _ => state.signatureDirty = true;
                ship.PerformanceChanged += state.changed;
                ship.PartChanged += state.partChanged;
                states.Add(ship, state);
            }
            return state;
        }

        void RefreshSignature(ObserverState state)
        {
            var ship = state.ship;
            var scale = ship.transform.lossyScale;
            if (state.signatureDirty || state.signatureRevision != ship.StructureRevision || state.scale != scale)
            {
                state.radius = CalculateSignatureRadius(ship);
                state.signatureRevision = ship.StructureRevision;
                state.scale = scale;
                state.signatureDirty = false;
            }
            // Tuning can change independently of the geometry cache.
            state.range = RangeForRadius(state.radius, tuning);
        }

        public float SignatureRadius(FlightShipRuntime target)
        {
            if (!target) return 0;
            var state = StateFor(target);
            RefreshSignature(state);
            return state.radius;
        }

        public float DetectionRange(FlightShipRuntime target)
        {
            if (!target) return 0;
            var state = StateFor(target);
            RefreshSignature(state);
            return state.range;
        }

        /// <summary>Only live attached part meshes contribute; disabled instancing source renderers still count.</summary>
        public static float CalculateSignatureRadius(FlightShipRuntime ship)
        {
            if (!ship) return 0;
            bool hasBounds = false;
            var bounds = new Bounds();
            var inverse = ship.transform.worldToLocalMatrix;
            foreach (var part in ship.Parts)
            {
                if (!part.present || part.detached || part.hp <= 0) continue;
                var filter = part.view ? part.view.GetComponent<MeshFilter>() : null;
                var mesh = filter ? filter.sharedMesh : null;
                if (mesh)
                {
                    // Use the part's own mesh, excluding child forward rays, plumes and transient weapon effects.
                    Encapsulate(ref bounds, ref hasBounds, inverse * filter.transform.localToWorldMatrix, mesh.bounds);
                }
                else
                {
                    // Occupancy is a fallback only at signature rebuild time, never an actor-pair/frame scan.
                    var definition = ship.Tuning ? ship.Tuning.GetDefinition(part.record.shape) : null;
                    var cells = definition && definition.occupiedCells != null && definition.occupiedCells.Length > 0
                        ? definition.occupiedCells : BlockModules.LocalCells(part.record.shape);
                    var matrix = Matrix4x4.TRS(part.record.position, part.record.rotation, Vector3.one);
                    foreach (var cell in cells)
                        Encapsulate(ref bounds, ref hasBounds, matrix, new Bounds((Vector3)cell, Vector3.one));
                }
            }
            if (!hasBounds) return 0;
            var scale = ship.transform.lossyScale;
            return Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))).magnitude;
        }

        static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Matrix4x4 matrix, Bounds local)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                else bounds.Encapsulate(point);
            }
        }

        public void Update(IReadOnlyList<RangeCombatWorld.Actor> actors, float dt)
        {
            dt = Mathf.Max(0, dt);
            active.Clear(); ordered.Clear();
            if (actors != null) foreach (var actor in actors)
            {
                if (actor == null || !actor.ship || !active.Add(actor.ship)) continue;
                var state = StateFor(actor.ship);
                state.actor = actor;
                if (state.life != actor.ship.LifeId)
                {
                    state.life = actor.ship.LifeId;
                    state.contacts.Clear();
                    ResetLock(state);
                    state.signatureDirty = true;
                }
                state.position = actor.ship.CorePhysicsPosition;
                RefreshSignature(state);
                ordered.Add(state);
            }
            removed.Clear();
            foreach (var pair in states) if (!active.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var ship in removed) { Unsubscribe(states[ship]); states.Remove(ship); }

            float exitMultiplier = ExitRangeMultiplier;
            float delay = LossDelay;
            foreach (var observer in ordered)
            {
                observer.detected.Clear();
                if (!observer.ship.IsAlive) { observer.contacts.Clear(); ResetLock(observer); continue; }
                foreach (var target in ordered)
                {
                    if (target == observer || !target.ship.IsAlive || target.ship.Team == observer.ship.Team) continue;
                    if (!observer.contacts.TryGetValue(target.ship, out var contact))
                        observer.contacts.Add(target.ship, contact = new Contact { life = target.life });
                    if (contact.life != target.life)
                    {
                        contact.life = target.life; contact.detected = false; contact.outsideSeconds = 0;
                    }
                    float distanceSquared = (target.position - observer.position).sqrMagnitude;
                    bool wasDetected = contact.detected;
                    if (!contact.detected)
                    {
                        if (distanceSquared <= target.range * target.range)
                        { contact.detected = true; contact.outsideSeconds = 0; }
                    }
                    else if (distanceSquared > Mathf.Pow(target.range * exitMultiplier, 2))
                    {
                        contact.outsideSeconds += dt;
                        if (contact.outsideSeconds >= delay) { contact.detected = false; contact.outsideSeconds = 0; }
                    }
                    else contact.outsideSeconds = 0;
                    if (contact.detected) observer.detected.Add(target.ship);
                    if (!wasDetected && contact.detected) TargetDiscovered?.Invoke(observer.ship, target.ship);
                }
                removed.Clear();
                foreach (var pair in observer.contacts)
                    if (!active.Contains(pair.Key) || !pair.Key || !pair.Key.IsAlive || pair.Key.Team == observer.ship.Team) removed.Add(pair.Key);
                foreach (var ship in removed) observer.contacts.Remove(ship);
            }
            // All contacts are updated before validating locks, so actor order cannot affect retention.
            foreach (var observer in ordered)
                if (observer.lockTarget && !IsDetected(observer.ship, observer.lockTarget)) ResetLock(observer);
            RefreshEnemyLocks();
        }

        public IReadOnlyList<FlightShipRuntime> GetDetectedTargets(FlightShipRuntime observer)
            => observer && states.TryGetValue(observer, out var state) && observer.IsAlive && state.life == observer.LifeId
                ? state.detected : NoTargets;

        public bool IsDetected(FlightShipRuntime observer, FlightShipRuntime target)
            => observer && target && observer != target && observer.IsAlive && target.IsAlive && observer.Team != target.Team
                && states.TryGetValue(observer, out var state) && state.life == observer.LifeId
                && state.contacts.TryGetValue(target, out var contact) && contact.detected && contact.life == target.LifeId;

        /// <summary>Alignment must hold continuously to acquire. An acquired lock survives leaving the aim cone.</summary>
        public bool UpdateEnemyLock(FlightShipRuntime observer, FlightShipRuntime target, bool aimAligned, float dt)
        {
            if (!observer || !states.TryGetValue(observer, out var state)) return false;
            if (!IsDetected(observer, target)) { ClearEnemyLock(observer); return false; }
            if (state.lockTarget != target || state.lockLife != target.LifeId)
            {
                ResetLock(state); state.lockTarget = target; state.lockLife = target.LifeId;
            }
            if (!state.locked)
            {
                var offset = target.CorePhysicsPosition - observer.CorePhysicsPosition;
                var forward = state.actor != null && state.actor.body ? state.actor.body.rotation * Vector3.forward : observer.transform.forward;
                float cone = Mathf.Cos(Mathf.Clamp(tuning ? tuning.lockConeDegrees : 35, 1, 80) * Mathf.Deg2Rad);
                bool inCone = offset.sqrMagnitude > .000001f && Vector3.Dot(forward, offset.normalized) >= cone;
                state.alignedSeconds = aimAligned && inCone ? state.alignedSeconds + Mathf.Max(0, dt) : 0;
                state.locked = aimAligned && inCone && state.alignedSeconds + .000001f >= Mathf.Max(0, tuning ? tuning.enemyLockAcquireSeconds : .3f);
            }
            RefreshEnemyLocks();
            return state.locked;
        }

        public bool IsEnemyLocked(FlightShipRuntime observer, FlightShipRuntime target)
            => IsDetected(observer, target) && states.TryGetValue(observer, out var state)
                && state.locked && state.lockTarget == target && state.lockLife == target.LifeId;

        public int CountLocksOn(FlightShipRuntime target)
        {
            int count = 0;
            foreach (var pair in enemyLocks) if (pair.Target == target && IsEnemyLocked(pair.Observer, target)) count++;
            return count;
        }

        public void ClearEnemyLock(FlightShipRuntime observer)
        {
            if (observer && states.TryGetValue(observer, out var state)) ResetLock(state);
            RefreshEnemyLocks();
        }

        public void ClearEnemyLocks()
        {
            foreach (var state in states.Values) ResetLock(state);
            enemyLocks.Clear();
        }

        static void ResetLock(ObserverState state)
        { state.lockTarget = null; state.alignedSeconds = 0; state.locked = false; state.lockLife = -1; }

        void RefreshEnemyLocks()
        {
            enemyLocks.Clear();
            foreach (var state in states.Values)
                if (state.locked && IsEnemyLocked(state.ship, state.lockTarget)) enemyLocks.Add(new EnemyLock(state.ship, state.lockTarget));
        }

        static void Unsubscribe(ObserverState state)
        {
            if (!state.ship) return;
            state.ship.PerformanceChanged -= state.changed;
            state.ship.PartChanged -= state.partChanged;
        }

        public void Dispose()
        {
            foreach (var state in states.Values) Unsubscribe(state);
            states.Clear(); ordered.Clear(); active.Clear(); removed.Clear(); enemyLocks.Clear();
        }
    }
}
