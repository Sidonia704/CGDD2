// @role Skip expensive compound-body sweeps when the broad phase finds clear space.

using System;
using Starframe.Combat;
using UnityEngine;

namespace Starframe.Flight
{
    /// <summary>Conservative broad phase before the expensive compound-rigidbody sweep.</summary>
    public sealed class ShipCollisionSweep
    {
        Rigidbody body;
        Bounds localBounds;
        Collider[] candidates;
        bool hasBounds;

        public void Initialize(Rigidbody value)
        {
            body = value; hasBounds = false;
            if (!body) return;
            var colliders = body.GetComponentsInChildren<Collider>(true);
            candidates = new Collider[Mathf.NextPowerOfTwo(Mathf.Max(32, colliders.Length + 16))];
            var inverse = Matrix4x4.TRS(body.position, body.rotation, Vector3.one).inverse;
            foreach (var collider in colliders)
            {
                if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy || collider.attachedRigidbody != body) continue;
                Bounds bounds;
                if (collider is MeshCollider mesh && mesh.sharedMesh)
                    bounds = DebrisWorld.TransformBounds(mesh.sharedMesh.bounds, inverse * CylindricalHitQuery.PhysicsMatrix(collider));
                else if (collider is BoxCollider box)
                    bounds = DebrisWorld.TransformBounds(new Bounds(box.center, box.size), inverse * CylindricalHitQuery.PhysicsMatrix(collider));
                else bounds = DebrisWorld.TransformBounds(collider.bounds, inverse);
                if (hasBounds) localBounds.Encapsulate(bounds); else { localBounds = bounds; hasBounds = true; }
            }
        }

        public bool MayHit(Vector3 displacement)
        {
            if (!body || !hasBounds) return true;
            Vector3 localDisplacement = Quaternion.Inverse(body.rotation) * displacement;
            Vector3 halfTravel = new Vector3(Mathf.Abs(localDisplacement.x), Mathf.Abs(localDisplacement.y), Mathf.Abs(localDisplacement.z)) * .5f;
            Vector3 center = body.position + body.rotation * localBounds.center + displacement * .5f;
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(center, localBounds.extents + halfTravel + Vector3.one * .02f,
                    candidates, body.rotation, ~0, QueryTriggerInteraction.Ignore);
                if (count < candidates.Length) break;
                // An unexpectedly dense scene takes the original safe path rather than overflowing or allocating forever.
                if (candidates.Length >= 16384) return true;
                Array.Resize(ref candidates, candidates.Length * 2);
            }
            for (int i = 0; i < count; i++) if (candidates[i].attachedRigidbody != body) return true;
            return false;
        }
    }
}
