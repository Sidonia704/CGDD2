// @role Resolve module occupancy, nozzle clearance and attachment geometry.
// @calls ModuleDatabase, BlockPlacement

using System.Collections.Generic;
using UnityEngine;
namespace Starframe.Construction
{
    public static class BlockModules
    {
        static Starframe.Flight.ModuleDatabase database;
        public static Starframe.Flight.ModuleDatabase ConfiguredDatabase=>database;
        static readonly Vector3Int[] Single={Vector3Int.zero},Tall={Vector3Int.zero,Vector3Int.up},Long={Vector3Int.zero,new Vector3Int(0,0,1),new Vector3Int(0,0,2)};
        public static void Configure(Starframe.Flight.ModuleDatabase values)=>database=values;
        public static bool IsThruster(BlockShape shape)=>shape==BlockShape.Thruster1||shape==BlockShape.Thruster2;
        public static Vector3Int[] LocalCells(BlockShape shape){var d=database?database.Get(shape):null;return d&&d.occupiedCells!=null&&d.occupiedCells.Length>0?d.occupiedCells:shape==BlockShape.Thruster2?Tall:shape==BlockShape.Laser?Long:Single;}
        public static int BodyLength(BlockShape shape)=>LocalCells(shape).Length;
        public static bool FullCellConnection(BlockShape shape)=>IsThruster(shape)||shape==BlockShape.Laser||shape==BlockShape.Power||shape==BlockShape.Shield;
        public static Vector3 LocalCenter(BlockShape shape){var cells=LocalCells(shape);Vector3 sum=Vector3.zero;foreach(var cell in cells)sum+=(Vector3)cell;return sum/cells.Length;}
        public static int Clearance(BlockShape shape)=>shape==BlockShape.Thruster1?2:shape==BlockShape.Thruster2?3:0;
        public static Vector3Int Direction(Quaternion rotation)=>Vector3Int.RoundToInt(rotation*Vector3.up);
        public static List<Vector3Int> BodyCells(BlockRecord part)
        {
            var cells=new List<Vector3Int>();var anchor=BlockPlacement.GridHint(part.position);
            foreach(var offset in LocalCells(part.shape))cells.Add(anchor+Vector3Int.RoundToInt(part.rotation*(Vector3)offset));return cells;
        }
        public static List<Vector3Int> AllCells(BlockRecord part)
        {
            var cells=BodyCells(part);var anchor=BlockPlacement.GridHint(part.position);var d=Direction(part.rotation);
            for(int i=0;i<Clearance(part.shape);i++)cells.Add(anchor+d*(BodyLength(part.shape)+i));return cells;
        }
        public static bool CellsOverlap(BlockRecord a,BlockRecord b)
        {
            var first=new HashSet<Vector3Int>(AllCells(a));
            foreach(var cell in AllCells(b))if(first.Contains(cell))return true;return false;
        }
    }
}
