// @role Bound kinematic ship contacts by finite ship momentum so scenery responds to its real mass.

using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Starframe.Flight
{
    public sealed class ShipSceneryCollision
    {
        readonly struct SceneryMass
        {
            public readonly float mass;
            public readonly Vector3 centerOffset;
            public SceneryMass(Rigidbody body)
            {
                mass = body.mass;
                centerOffset = Quaternion.Inverse(body.rotation) * (body.worldCenterOfMass - body.position);
            }
        }

        static readonly ConcurrentDictionary<int, SceneryMass> scenery = new ConcurrentDictionary<int, SceneryMass>();
        static readonly ConcurrentDictionary<int, ShipSceneryCollision> ships = new ConcurrentDictionary<int, ShipSceneryCollision>();
        static bool subscribed;
        readonly object gate = new object();
        readonly HashSet<int> sweptBodies = new HashSet<int>();
        Rigidbody body;
        int bodyId;
        float shipMass, impulseLimit, impulseSpent;
        Vector3 center;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Physics.ContactModifyEvent -= ModifyContacts;
            Physics.ContactModifyEventCCD -= ModifyContacts;
            scenery.Clear(); ships.Clear(); subscribed = false;
        }
        static void Subscribe()
        {
            if (subscribed) return;
            Physics.ContactModifyEvent += ModifyContacts;
            Physics.ContactModifyEventCCD += ModifyContacts;
            subscribed = true;
        }
        public static void UpdateSceneryMass(Rigidbody value)
        {
            if (value) scenery[value.GetInstanceID()] = new SceneryMass(value);
        }
        public static void RemoveScenery(Rigidbody value)
        {
            if (value) scenery.TryRemove(value.GetInstanceID(), out _);
        }
        public void Initialize(Rigidbody value)
        {
            Dispose(); body = value;
            if (!body) return;
            Subscribe(); bodyId = body.GetInstanceID();
            // Only ship colliders opt into modification, including currently disabled repairable parts.
            foreach (var collider in body.GetComponentsInChildren<Collider>(true))
                if (collider.attachedRigidbody == body) collider.hasModifiableContacts = true;
            BeginStep(); ships[bodyId] = this;
        }
        public void Dispose()
        {
            if (bodyId != 0) ships.TryRemove(bodyId, out _);
            body = null; bodyId = 0;
        }
        public void BeginStep()
        {
            if (!body) return;
            lock (gate)
            {
                shipMass = Mathf.Max(.01f, body.mass); center = body.worldCenterOfMass;
                impulseSpent = impulseLimit = 0; sweptBodies.Clear();
            }
        }
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            if (!body) return;
            var offset = Quaternion.Inverse(body.rotation) * (body.worldCenterOfMass - body.position);
            lock (gate) center = position + rotation * offset;
        }
        // The sweep already blocks ship translation. It and native rotation/overlap contacts share one budget.
        public void ApplySweptImpulse(Rigidbody other, Vector3 velocity, Vector3 point, Vector3 normal)
        {
            if (!body || !other || other.isKinematic || !scenery.TryGetValue(other.GetInstanceID(), out var mass)) return;
            Vector3 relative = velocity - other.GetPointVelocity(point);
            float approach = Mathf.Max(0, -Vector3.Dot(relative, normal));
            float reducedMass = shipMass / (1 + shipMass / mass.mass);
            float impulse = Reserve(approach * reducedMass, relative.magnitude, other.GetInstanceID(), true);
            if (impulse > 0) other.AddForceAtPosition(-normal * impulse, point, ForceMode.Impulse);
        }
        float Reserve(float requested, float relativeSpeed, int otherId, bool swept)
        {
            lock (gate)
            {
                if (!swept && sweptBodies.Contains(otherId)) return 0;
                if (swept) sweptBodies.Add(otherId);
                // One budget covers every collider/manifold and both ordinary/CCD callbacks this physics step.
                impulseLimit = Mathf.Max(impulseLimit, shipMass * relativeSpeed);
                float impulse = Mathf.Min(requested, Mathf.Max(0, impulseLimit - impulseSpent));
                impulseSpent += impulse; return impulse;
            }
        }
        static void ModifyContacts(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            // Unity can invoke this on several worker threads. Only copied values and synchronized state are used.
            foreach (var pair in pairs)
            {
                bool shipFirst = ships.TryGetValue(pair.bodyInstanceID, out var ship);
                int sceneryId = shipFirst ? pair.otherBodyInstanceID : pair.bodyInstanceID;
                if (!shipFirst && !ships.TryGetValue(pair.otherBodyInstanceID, out ship)) continue;
                if (!scenery.TryGetValue(sceneryId, out var mass)) continue;
                Vector3 shipCenter; float finiteShipMass;
                lock (ship.gate) { shipCenter = ship.center; finiteShipMass = ship.shipMass; }
                Vector3 sceneryCenter = shipFirst ? pair.otherPosition + pair.otherRotation * mass.centerOffset
                    : pair.position + pair.rotation * mass.centerOffset;
                float reducedMass = finiteShipMass / (1 + finiteShipMass / mass.mass);
                for (int i = 0; i < pair.contactCount; i++)
                {
                    Vector3 point = pair.GetPoint(i);
                    Vector3 firstCenter = shipFirst ? shipCenter : sceneryCenter;
                    Vector3 secondCenter = shipFirst ? sceneryCenter : shipCenter;
                    Vector3 relative = pair.bodyVelocity + Vector3.Cross(pair.bodyAngularVelocity, point - firstCenter)
                        - pair.otherBodyVelocity - Vector3.Cross(pair.otherBodyAngularVelocity, point - secondCenter);
                    float approach = Mathf.Max(0, -Vector3.Dot(relative, pair.GetNormal(i)));
                    float requested = approach * reducedMass / pair.contactCount;
                    float impulse = ship.Reserve(requested, relative.magnitude, sceneryId, false);
                    pair.SetMaxImpulse(i, Mathf.Min(pair.GetMaxImpulse(i), impulse));
                }
            }
        }
    }
}
