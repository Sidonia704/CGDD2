// @role Store reusable hull-shaped scenery, colour channels, immutable occupancy and an optional signed authored surface mesh.
// @calls VoxelStructureRuntime, ModuleDatabase, SpaceMapVisuals, VoxelHullAppearance
// @tune cellSize:float:0.25-10:World metres per voxel before instance scaling.
// @tune massPerCell:float:0.01-1000000:Kilograms per cubic metre of solid scenery cells.
// @tune baseColor:Color:0-1:Scenery-only tint multiplied with the original ship hull material and each colour slot; white preserves the source appearance.

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Maps
{
    [Serializable]
    public sealed class VoxelCell
    {
        public Vector3Int position;
        public int surface;
        public BlockShape shape;
        public Vector3Int quarterTurns;
        public Vector3Int modelOffset; // Source-cell offset for a partition of a multi-cell hull model.
        public Quaternion Rotation => Quaternion.Euler(quarterTurns.x * 90f, quarterTurns.y * 90f, quarterTurns.z * 90f);
        public VoxelCell Copy() => new VoxelCell { position = position, surface = surface, shape = shape, quarterTurns = quarterTurns, modelOffset = modelOffset };
    }

    [Serializable]
    public sealed class VoxelCellChunk
    {
        public int start, count;
    }

    [Serializable]
    public sealed class VoxelPhysicalMetadata
    {
        // Metres/kilograms at the authored asset's scale one, independent of its sparse shell.
        public bool valid;
        public float mass;
        public Vector3 centerOfMass, inertiaTensor;
        public Quaternion inertiaTensorRotation = Quaternion.identity;
        public bool IsValid => valid && mass > 0 && Finite(mass) && Finite(centerOfMass) &&
            Finite(inertiaTensor) && inertiaTensor.x > 0 && inertiaTensor.y > 0 && inertiaTensor.z > 0 &&
            Finite(inertiaTensorRotation.x) && Finite(inertiaTensorRotation.y) &&
            Finite(inertiaTensorRotation.z) && Finite(inertiaTensorRotation.w) &&
            (inertiaTensorRotation.x * inertiaTensorRotation.x + inertiaTensorRotation.y * inertiaTensorRotation.y +
             inertiaTensorRotation.z * inertiaTensorRotation.z + inertiaTensorRotation.w * inertiaTensorRotation.w) > .01f;
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }

    [CreateAssetMenu(menuName = "Starframe/Maps/Voxel scenery asset")]
    public sealed class VoxelAssetDefinition : ScriptableObject
    {
        public const int MaximumCells = 131072;
        public string category = "岩石";
        [Range(.25f, 10)] public float cellSize = 1;
        [Min(.01f)] public float massPerCell = 100;
        public Material[] surfaces = Array.Empty<Material>();
        public ModuleDatabase hullModules;
        // Optional signed render surface for maps that opt into lazy fine visuals. Never owns damage or collision.
        public Mesh initialLodMesh;
        public long initialLodSignature;
        public Color baseColor = Color.white;
        public List<Color> surfaceColors = new List<Color> { Color.white };
        public List<VoxelCell> cells = new List<VoxelCell>();
        // Internal bounded work ranges; these never split the authored component or physics body.
        public List<VoxelCellChunk> cellChunks = new List<VoxelCellChunk>();
        public VoxelPhysicalMetadata authoredPhysics;
        public void RebuildCellChunks()
        {
            if (cellChunks == null) cellChunks = new List<VoxelCellChunk>();
            cellChunks.Clear();
            for (int start = 0; start < (cells?.Count ?? 0); start += MaximumCells)
                cellChunks.Add(new VoxelCellChunk { start = start, count = Math.Min(MaximumCells, cells.Count - start) });
        }
        [NonSerialized] int revision;
        public int Revision => revision;
        public int GeometryRevision => unchecked(revision * 397 + (hullModules ? hullModules.Revision : 0));
        public bool UsesHullModels => hullModules || cells.Exists(cell => cell != null && cell.shape != BlockShape.Cube);
        public int SurfaceCount => hullModules ? Mathf.Max(1, surfaceColors?.Count ?? 0) : Mathf.Max(1, surfaces?.Length ?? 0);
        public static bool IsHullShape(BlockShape shape) => shape == BlockShape.Cube || shape == BlockShape.Wedge || shape == BlockShape.Slab || shape == BlockShape.Pyramid || shape == BlockShape.Block5 || shape == BlockShape.Block6 || shape == BlockShape.Block7 || shape == BlockShape.Block8;
        public int RenderMaterialCount => SurfaceCount * (UsesHullModels ? VoxelHullAppearance.ShapeCount : 1);
        public bool HasValidInitialLod => initialLodMesh && initialLodMesh.vertexCount > 0 &&
            initialLodMesh.subMeshCount == RenderMaterialCount &&
            initialLodSignature == SpaceMapVisuals.ComputeGeometrySignature(this);
        public void NotifyChanged() => revision++;
        void OnValidate() => NotifyChanged();
        public Bounds LocalBounds
        {
            get
            {
                if (cells == null || cells.Count == 0) return new Bounds(Vector3.zero, Vector3.one * cellSize);
                var bounds = new Bounds((Vector3)cells[0].position * cellSize, Vector3.one * cellSize);
                for (int i = 1; i < cells.Count; i++) bounds.Encapsulate(new Bounds((Vector3)cells[i].position * cellSize, Vector3.one * cellSize));
                return bounds;
            }
        }
        public bool Validate(out string error)
        {
            if (!Finite(cellSize) || cellSize < .25f || cellSize > 10 || !Finite(massPerCell) || massPerCell <= 0)
            { error = "格子尺寸或血量无效。"; return false; }
            if (cells == null) { error = "体块数据缺失。"; return false; }
            if (cells.Count > MaximumCells)
            {
                int end = 0;
                if (cellChunks == null) { error = "大型体块缺少内部分区。"; return false; }
                foreach (var chunk in cellChunks)
                {
                    if (chunk == null || chunk.start != end || chunk.count <= 0 || chunk.count > MaximumCells ||
                        chunk.count > cells.Count - end) { error = "大型体块内部分区无效。"; return false; }
                    end += chunk.count;
                }
                if (end != cells.Count) { error = "大型体块内部分区不完整。"; return false; }
            }
            if (authoredPhysics != null && authoredPhysics.valid && !authoredPhysics.IsValid)
            { error = "物理质量元数据无效。"; return false; }
            var unique = new HashSet<Vector3Int>();
            foreach (var cell in cells)
                if (cell == null || !IsHullShape(cell.shape) || !ValidOffset(cell) || !unique.Add(cell.position) ||
                    Math.Abs((long)cell.position.x) > 100000 || Math.Abs((long)cell.position.y) > 100000 || Math.Abs((long)cell.position.z) > 100000)
                { error = "存在重复格子或无效体块数据。"; return false; }
            error = null; return true;
        }
        static bool ValidOffset(VoxelCell cell)
        {
            foreach (var offset in BlockModules.LocalCells(cell.shape)) if (offset == cell.modelOffset) return true;
            return false;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
