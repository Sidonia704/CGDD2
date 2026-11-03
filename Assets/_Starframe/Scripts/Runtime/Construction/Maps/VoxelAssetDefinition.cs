// @role Store reusable hull-shaped scenery, colour channels, voxel durability and an optional borrowed coarse visual mesh.
// @calls VoxelStructureRuntime, ModuleDatabase
// @tune cellSize:float:0.25-10:World metres per voxel before instance scaling.
// @tune blockHealth:float:1-100000:Default health of each scenery cell.
// @tune damageResetSeconds:float:0.1-60:Forget temporary cell damage after this many seconds without a hit.
// @tune massPerCell:float:0.01-1000000:Kilograms per cubic metre of solid scenery cells.
// @tune fragmentSeparationSpeed:float:0-10:Small outward drift added when an island separates.
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

    [CreateAssetMenu(menuName = "Starframe/Maps/Voxel scenery asset")]
    public sealed class VoxelAssetDefinition : ScriptableObject
    {
        public const int MaximumCells = 131072;
        public string category = "岩石";
        [Range(.25f, 10)] public float cellSize = 1;
        [Min(1)] public float blockHealth = 100;
        [Min(.1f)] public float damageResetSeconds = 2;
        [Min(.01f)] public float massPerCell = 100;
        [Range(0, 10)] public float fragmentSeparationSpeed = .5f;
        public Material[] surfaces = Array.Empty<Material>();
        public ModuleDatabase hullModules;
        // Optional borrowed render asset for maps that opt into lazy fine visuals. Never owns damage or collision.
        public Mesh initialLodMesh;
        public long initialLodSignature;
        public Color baseColor = Color.white;
        public List<Color> surfaceColors = new List<Color> { Color.white };
        public List<VoxelCell> cells = new List<VoxelCell>();
        [NonSerialized] int revision;
        public int Revision => revision;
        public int GeometryRevision => unchecked(revision * 397 + (hullModules ? hullModules.Revision : 0));
        public bool UsesHullModels => hullModules || cells.Exists(cell => cell != null && cell.shape != BlockShape.Cube);
        public int SurfaceCount => hullModules ? Mathf.Max(1, surfaceColors?.Count ?? 0) : Mathf.Max(1, surfaces?.Length ?? 0);
        public static bool IsHullShape(BlockShape shape) => shape == BlockShape.Cube || shape == BlockShape.Wedge || shape == BlockShape.Slab || shape == BlockShape.Pyramid || shape == BlockShape.Block5 || shape == BlockShape.Block6 || shape == BlockShape.Block7 || shape == BlockShape.Block8;
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
            if (!Finite(cellSize) || cellSize < .25f || cellSize > 10 || !Finite(blockHealth) || blockHealth <= 0 || !Finite(damageResetSeconds) || damageResetSeconds < .1f || !Finite(massPerCell) || massPerCell <= 0 || !Finite(fragmentSeparationSpeed) || fragmentSeparationSpeed < 0)
            { error = "格子尺寸或血量无效。"; return false; }
            if (cells == null || cells.Count > MaximumCells) { error = "体块数量超出上限。"; return false; }
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
