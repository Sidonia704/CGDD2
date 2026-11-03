// @role Own persistent render-only detached scenery meshes and analytically drift/spin them with one shared renderer, without cell simulation or physics.
// @calls VoxelStructureRuntime, SpaceMapRuntime

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Starframe.Maps
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class VoxelDetachedVisualFragments : MonoBehaviour
    {
        struct Draw
        {
            public int submesh;
            public RenderParams parameters;
        }

        sealed class Fragment
        {
            public Mesh mesh;
            public Bounds localBounds;
            public Draw[] draws;
            public Matrix4x4 ownerInverseAtBirth, meshFromWorldPivot;
            public Vector3 worldPivot, worldVelocity, spinAxis;
            public double birthTime, spinRadiansPerSecond;
        }

        static readonly Dictionary<int, VoxelDetachedVisualFragments> sceneOwners = new Dictionary<int, VoxelDetachedVisualFragments>();
        readonly List<Fragment> fragments = new List<Fragment>();
        readonly HashSet<Mesh> ownedMeshes = new HashSet<Mesh>();
        readonly Dictionary<Material, Material> ownedMaterials = new Dictionary<Material, Material>();
        int sceneOwnerHandle = -1;
        static double Clock => Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;

        public int FragmentCount => fragments.Count;
        public int CellCount { get; private set; }
        public int OwnedMeshCount => ownedMeshes.Count;
        public int OwnedMaterialCount => ownedMaterials.Count;
        public int SubmittedDrawCount { get; private set; }

        public static VoxelDetachedVisualFragments GetOrCreate(Transform lifetimeOwner)
        {
            if (!lifetimeOwner) throw new ArgumentNullException(nameof(lifetimeOwner));
            var renderer = lifetimeOwner.GetComponent<VoxelDetachedVisualFragments>();
            return renderer ? renderer : lifetimeOwner.gameObject.AddComponent<VoxelDetachedVisualFragments>();
        }

        // Keep visuals outside the source body's lifetime. A map or existing non-body parent
        // is the natural owner; a root-level standalone body uses one owner in its own scene.
        public static VoxelDetachedVisualFragments GetOrCreateFor(VoxelStructureRuntime source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            var map = source.GetComponentInParent<SpaceMapRuntime>();
            if (map) return GetOrCreate(map.transform);
            var parent = source.transform.parent;
            while (parent && parent.GetComponentInParent<VoxelStructureRuntime>()) parent = parent.parent;
            if (parent) return GetOrCreate(parent);

            var scene = source.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Detached scenery needs a loaded owner scene.", nameof(source));
            if (sceneOwners.TryGetValue(scene.handle, out var existing) && existing) return existing;
            var root = new GameObject("Detached scenery visuals");
            root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            SceneManager.MoveGameObjectToScene(root, scene);
            var renderer = root.AddComponent<VoxelDetachedVisualFragments>();
            renderer.sceneOwnerHandle = scene.handle;
            sceneOwners[scene.handle] = renderer;
            return renderer;
        }

        /// <summary>
        /// Take ownership of a newly baked mesh, never a template or asset mesh. Materials are
        /// independently cloned/cached, so destroying the source body cannot invalidate them.
        /// The pivot is in mesh coordinates (including cellSize); velocity and angular velocity
        /// are world-space, with angular velocity in radians/second. No source or cell is retained.
        /// </summary>
        public void AddOwnedMesh(Mesh ownedMesh, Material[] sourceMaterials, Matrix4x4 worldPose,
            Vector3 localPivot, Vector3 worldVelocity, Vector3 worldAngularVelocityRadians, int cellCount, int layer)
        {
            if (!ownedMesh) throw new ArgumentNullException(nameof(ownedMesh));
            if (sourceMaterials == null || sourceMaterials.Length < ownedMesh.subMeshCount)
                throw new ArgumentException("Every fragment submesh needs a source material.", nameof(sourceMaterials));
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer));
            if (!Finite(worldPose) || !Finite(localPivot) || !Finite(worldVelocity) || !Finite(worldAngularVelocityRadians))
                throw new ArgumentException("Fragment pose and motion must be finite.");
            var ownerPose = transform.localToWorldMatrix;
            if (!Finite(ownerPose) || Math.Abs(ownerPose.determinant) < 1e-12)
                throw new ArgumentException("Fragment lifetime owner must have an invertible transform.");
            int totalCells = checked(CellCount + cellCount);
            var draws = new List<Draw>();
            for (int submesh = 0; submesh < ownedMesh.subMeshCount; submesh++)
                if (ownedMesh.GetIndexCount(submesh) != 0 && !sourceMaterials[submesh])
                    throw new ArgumentException("A visible fragment submesh has no material.", nameof(sourceMaterials));
            for (int submesh = 0; submesh < ownedMesh.subMeshCount; submesh++)
            {
                if (ownedMesh.GetIndexCount(submesh) == 0) continue;
                var source = sourceMaterials[submesh];
                if (!ownedMaterials.TryGetValue(source, out var material))
                {
                    material = new Material(source) { name = "Detached scenery " + source.name, hideFlags = HideFlags.HideAndDontSave };
                    ownedMaterials.Add(source, material);
                }
                draws.Add(new Draw
                {
                    submesh = submesh,
                    parameters = new RenderParams(material)
                    {
                        layer = layer,
                        shadowCastingMode = ShadowCastingMode.On,
                        receiveShadows = true,
                        lightProbeUsage = LightProbeUsage.BlendProbes,
                        reflectionProbeUsage = ReflectionProbeUsage.BlendProbes
                    }
                });
            }
            var pivot = worldPose.MultiplyPoint3x4(localPivot);
            double spin = Math.Sqrt((double)worldAngularVelocityRadians.x * worldAngularVelocityRadians.x +
                (double)worldAngularVelocityRadians.y * worldAngularVelocityRadians.y +
                (double)worldAngularVelocityRadians.z * worldAngularVelocityRadians.z);
            var axis = spin > 0 ? new Vector3((float)(worldAngularVelocityRadians.x / spin),
                (float)(worldAngularVelocityRadians.y / spin), (float)(worldAngularVelocityRadians.z / spin)) : Vector3.up;
            fragments.Add(new Fragment
            {
                mesh = ownedMesh,
                localBounds = ownedMesh.bounds,
                draws = draws.ToArray(),
                ownerInverseAtBirth = ownerPose.inverse,
                meshFromWorldPivot = Matrix4x4.Translate(-pivot) * worldPose,
                worldPivot = pivot,
                worldVelocity = worldVelocity,
                spinAxis = axis,
                spinRadiansPerSecond = spin,
                birthTime = Clock
            });
            ownedMeshes.Add(ownedMesh);
            CellCount = totalCells;
        }

        // Elapsed time is explicit for deterministic tests: frame subdivision never changes motion.
        public Matrix4x4 GetWorldMatrix(int fragmentIndex, double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            return WorldMatrix(fragments[fragmentIndex], transform.localToWorldMatrix, elapsedSeconds);
        }

        public Bounds GetWorldBounds(int fragmentIndex, double elapsedSeconds)
        {
            return TransformBounds(GetWorldMatrix(fragmentIndex, elapsedSeconds), fragments[fragmentIndex].localBounds);
        }

        static Matrix4x4 WorldMatrix(Fragment fragment, Matrix4x4 ownerPose, double elapsed)
        {
            var displacement = new Vector3((float)(fragment.worldVelocity.x * elapsed),
                (float)(fragment.worldVelocity.y * elapsed), (float)(fragment.worldVelocity.z * elapsed));
            float degrees = (float)((fragment.spinRadiansPerSecond * elapsed * (180.0 / Math.PI)) % 360.0);
            var rotation = fragment.spinRadiansPerSecond > 0 ? Quaternion.AngleAxis(degrees, fragment.spinAxis) : Quaternion.identity;
            // Apply inherited world-space motion about the fragment's own pivot, then follow the
            // owner's transform delta. This preserves parent translation, rotation and scale.
            return ownerPose * fragment.ownerInverseAtBirth *
                Matrix4x4.TRS(fragment.worldPivot + displacement, rotation, Vector3.one) * fragment.meshFromWorldPivot;
        }

        void LateUpdate()
        {
            SubmittedDrawCount = 0;
            double now = Clock;
            var ownerPose = transform.localToWorldMatrix;
            foreach (var fragment in fragments)
            {
                if (!fragment.mesh) continue;
                var matrix = WorldMatrix(fragment, ownerPose, Math.Max(0, now - fragment.birthTime));
                var bounds = TransformBounds(matrix, fragment.localBounds);
                foreach (var draw in fragment.draws)
                {
                    var parameters = draw.parameters;
                    parameters.worldBounds = bounds;
                    Graphics.RenderMesh(parameters, fragment.mesh, draw.submesh, matrix);
                    SubmittedDrawCount++;
                }
            }
        }

        static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            var x = matrix.MultiplyVector(new Vector3(bounds.extents.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, bounds.extents.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, bounds.extents.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2);
        }

        public void Clear()
        {
            fragments.Clear();
            CellCount = SubmittedDrawCount = 0;
            foreach (var mesh in ownedMeshes) Release(mesh);
            ownedMeshes.Clear();
            foreach (var material in ownedMaterials.Values) Release(material);
            ownedMaterials.Clear();
        }

        void OnDisable() => SubmittedDrawCount = 0;
        void OnDestroy()
        {
            Clear();
            if (sceneOwnerHandle != -1 && sceneOwners.TryGetValue(sceneOwnerHandle, out var owner) && ReferenceEquals(owner, this))
                sceneOwners.Remove(sceneOwnerHandle);
        }
        static void Release(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++) if (!Finite(value[i])) return false;
            return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSceneOwners() => sceneOwners.Clear();
    }
}
