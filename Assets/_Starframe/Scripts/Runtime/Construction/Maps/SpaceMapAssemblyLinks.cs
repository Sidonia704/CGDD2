// @role Hold authored station contacts through staged loading, physical damage and fragment ownership changes.
// @calls SpaceMapDefinition, VoxelStructureRuntime

using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Maps
{
    internal sealed class SpaceMapAssemblyLinks
    {
        sealed class BodyState
        {
            public Rigidbody body;
            public bool wasKinematic;
            public Vector3 velocity, spin;
        }
        sealed class Contact
        {
            public VoxelAssemblyLink source;
            public VoxelCellState cellA, cellB;
            public VoxelStructureRuntime ownerA, ownerB;
            public Vector3 localAnchorA, localAnchorB;
            public Joint joint;
            public bool released;
        }

        readonly Dictionary<Rigidbody, BodyState> frozen = new Dictionary<Rigidbody, BodyState>();
        readonly List<Contact> contacts = new List<Contact>();
        readonly HashSet<VoxelCellState> anchors = new HashSet<VoxelCellState>();
        readonly HashSet<VoxelStructureRuntime> observed = new HashSet<VoxelStructureRuntime>();
        readonly Dictionary<Rigidbody, float> depenetrationRestore = new Dictionary<Rigidbody, float>();
        int cappedFixedSteps;
        public int ActiveContactCount { get { int count = 0; foreach (var link in contacts) if (!link.released) count++; return count; } }

        public void Freeze(VoxelStructureRuntime structure)
        {
            var body = structure ? structure.Body : null;
            if (!body || frozen.ContainsKey(body)) return;
            frozen.Add(body, new BodyState { body = body, wasKinematic = body.isKinematic,
                velocity = body.linearVelocity, spin = body.angularVelocity });
            body.isKinematic = true;
        }

        public void Install(SpaceMapDefinition definition, IReadOnlyList<VoxelStructureRuntime> structures, Transform mapRoot)
        {
            var byId = new Dictionary<string, List<VoxelStructureRuntime>>();
            foreach (var body in structures)
            {
                if (!body) continue;
                Observe(body);
                if (!byId.TryGetValue(body.AuthoringId ?? "", out var list))
                    byId.Add(body.AuthoringId ?? "", list = new List<VoxelStructureRuntime>());
                list.Add(body);
            }
            if (definition.assemblyLinks == null) return;
            foreach (var source in definition.assemblyLinks)
            {
                var cellA = FindAnchor(byId, source.instanceIdA, source.anchorCellA);
                var cellB = FindAnchor(byId, source.instanceIdB, source.anchorCellB);
                if (cellA == null || cellB == null) continue; // An anchor was destroyed while a staged map loaded.
                Vector3 world = mapRoot.TransformPoint(source.worldAnchor);
                var link = new Contact { source = source, cellA = cellA, cellB = cellB,
                    localAnchorA = cellA.owner.transform.InverseTransformPoint(world),
                    localAnchorB = cellB.owner.transform.InverseTransformPoint(world) };
                contacts.Add(link); anchors.Add(cellA); anchors.Add(cellB);
                Attach(link);
            }
        }
        static VoxelCellState FindAnchor(Dictionary<string, List<VoxelStructureRuntime>> byId, string id, Vector3Int position)
        {
            if (!byId.TryGetValue(id ?? "", out var bodies)) return null;
            foreach (var body in bodies)
                if (body.Cells.TryGetValue(position, out var cell) && cell.Alive) return cell;
            return null;
        }
        public void Observe(VoxelStructureRuntime structure)
        {
            if (!structure || !observed.Add(structure)) return;
        }
        public void Refresh()
        {
            foreach (var link in contacts)
            {
                if (link.released) continue;
                if (!link.cellA.Alive || !link.cellB.Alive || !link.cellA.owner || !link.cellB.owner ||
                    !link.cellA.owner.Body || !link.cellB.owner.Body)
                { Release(link); continue; }
                if (link.ownerA != link.cellA.owner || link.ownerB != link.cellB.owner || !link.joint)
                { RemoveJoint(link); Attach(link); }
            }
        }
        public void Tick()
        {
            foreach (var link in contacts)
                if (!link.released && !link.source.createFixedJoint && !AnchorBoxesOverlap(link)) Release(link);
        }
        static bool AnchorBoxesOverlap(Contact link)
        {
            var a = link.source.hasLegacyAnchorBounds ? TransformBounds(link.cellA.owner.PhysicsMatrix, link.source.legacyAnchorBoundsA) : CellBounds(link.cellA.owner, link.cellA.position);
            var b = link.source.hasLegacyAnchorBounds ? TransformBounds(link.cellB.owner.PhysicsMatrix, link.source.legacyAnchorBoundsB) : CellBounds(link.cellB.owner, link.cellB.position);
            a.Expand(.05f); b.Expand(.05f);
            return a.Intersects(b);
        }
        static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            var x = matrix.MultiplyVector(Vector3.right * local.extents.x);
            var y = matrix.MultiplyVector(Vector3.up * local.extents.y);
            var z = matrix.MultiplyVector(Vector3.forward * local.extents.z);
            return new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2);
        }
        static Bounds CellBounds(VoxelStructureRuntime body, Vector3Int position)
        {
            var matrix = body.CellMatrix(position);
            var center = matrix.MultiplyPoint3x4(Vector3.zero);
            var x = matrix.MultiplyVector(Vector3.right * .5f);
            var y = matrix.MultiplyVector(Vector3.up * .5f);
            var z = matrix.MultiplyVector(Vector3.forward * .5f);
            return new Bounds(center, new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2);
        }
        static void Attach(Contact link)
        {
            link.ownerA = link.cellA.owner; link.ownerB = link.cellB.owner;
            if (!link.ownerA || !link.ownerB || !link.ownerA.Body || !link.ownerB.Body) return;
            Joint joint;
            if (link.source.createFixedJoint) joint = link.ownerA.gameObject.AddComponent<FixedJoint>();
            else
            {
                var free = link.ownerA.gameObject.AddComponent<ConfigurableJoint>();
                free.xMotion = free.yMotion = free.zMotion = ConfigurableJointMotion.Free;
                free.angularXMotion = free.angularYMotion = free.angularZMotion = ConfigurableJointMotion.Free;
                joint = free;
            }
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = link.localAnchorA;
            joint.connectedAnchor = link.localAnchorB;
            joint.connectedBody = link.ownerB.Body;
            joint.enableCollision = !link.source.ignoreMutualCollision;
            link.joint = joint;
        }
        static void RemoveJoint(Contact link)
        {
            if (!link.joint) return;
            link.joint.enableCollision = true;
            if (Application.isPlaying) Object.Destroy(link.joint); else Object.DestroyImmediate(link.joint);
            link.joint = null;
        }
        static void Release(Contact link) { RemoveJoint(link); link.released = true; }

        // All source bodies are frozen until every contact exists. Welded groups restart with one rigid velocity field.
        public void ReleaseFreeze(float initialMaxDepenetrationVelocity = 0)
        {
            var parent = new Dictionary<Rigidbody, Rigidbody>();
            foreach (var pair in frozen) if (pair.Key) parent.Add(pair.Key, pair.Key);
            foreach (var link in contacts)
                if (!link.released && link.source.createFixedJoint && link.ownerA && link.ownerB &&
                    parent.ContainsKey(link.ownerA.Body) && parent.ContainsKey(link.ownerB.Body))
                    parent[Root(parent, link.ownerB.Body)] = Root(parent, link.ownerA.Body);
            var groups = new Dictionary<Rigidbody, List<BodyState>>();
            foreach (var pair in frozen)
            {
                if (!pair.Key) continue;
                var root = Root(parent, pair.Key);
                if (!groups.TryGetValue(root, out var list)) groups.Add(root, list = new List<BodyState>());
                list.Add(pair.Value);
            }
            foreach (var group in groups.Values)
            {
                if (group.Count > 1)
                {
                    float mass = 0; Vector3 center = Vector3.zero, velocity = Vector3.zero, spin = Vector3.zero;
                    foreach (var state in group)
                    {
                        float weight = Mathf.Max(.01f, state.body.mass);
                        mass += weight; center += state.body.worldCenterOfMass * weight;
                        velocity += state.velocity * weight; spin += state.spin * weight;
                    }
                    center /= mass; velocity /= mass; spin /= mass;
                    foreach (var state in group)
                    {
                        state.velocity = velocity + Vector3.Cross(spin, state.body.worldCenterOfMass - center);
                        state.spin = spin;
                    }
                }
            }
            foreach (var state in frozen.Values) if (state.body)
            {
                state.body.isKinematic = state.wasKinematic;
                if (!state.wasKinematic) { state.body.linearVelocity = state.velocity; state.body.angularVelocity = state.spin; }
                if (initialMaxDepenetrationVelocity > 0 && !state.wasKinematic)
                {
                    depenetrationRestore[state.body] = state.body.maxDepenetrationVelocity;
                    state.body.maxDepenetrationVelocity = initialMaxDepenetrationVelocity;
                }
            }
            cappedFixedSteps = depenetrationRestore.Count > 0 ? 2 : 0;
            frozen.Clear();
        }
        public void AdvanceFixedStep()
        {
            if (cappedFixedSteps == 0 || --cappedFixedSteps > 0) return;
            RestoreDepenetration();
        }
        void RestoreDepenetration()
        {
            foreach (var pair in depenetrationRestore) if (pair.Key) pair.Key.maxDepenetrationVelocity = pair.Value;
            depenetrationRestore.Clear(); cappedFixedSteps = 0;
        }
        static Rigidbody Root(Dictionary<Rigidbody, Rigidbody> parent, Rigidbody body)
        {
            while (parent[body] != body) { parent[body] = parent[parent[body]]; body = parent[body]; }
            return body;
        }
        public void Clear()
        {
            RestoreDepenetration();
            foreach (var link in contacts) RemoveJoint(link);
            contacts.Clear(); anchors.Clear(); observed.Clear(); frozen.Clear();
        }
    }
}
