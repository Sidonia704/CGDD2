// @role Check physically supported face connections and reject equipment stacked onto other weapons.
// @calls BlockModules

using System;
using System.Collections.Generic;
using UnityEngine;
namespace Starframe.Construction
{
    public static class BlockConnectivity
    {
        static readonly Vector3Int[] Directions={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
        public static bool Touches(BlockRecord a,BlockRecord b)
        {
            bool aWeapon=BlockModules.IsWeapon(a.shape),bWeapon=BlockModules.IsWeapon(b.shape);
            if(aWeapon||bWeapon)
            {
                if(aWeapon&&bWeapon)return false;
                if(aWeapon&&MountTouches(a,b))return true;
                if(bWeapon&&MountTouches(b,a))return true;
                return false;
            }
            // Occupancy stays integer-sized; only overlapping real planar surfaces carry connections.
            var cells=new HashSet<Vector3Int>(BlockModules.BodyCells(a));
            foreach(var cell in BlockModules.BodyCells(b))foreach(var direction in Directions)
                if(cells.Contains(cell+direction)&&BlockModules.FacesContact(b,cell,direction,a,cell+direction))return true;
            return false;
        }
        // Weapons mount by placing a bottom cell on a support whose top is a real surface.
        static bool MountTouches(BlockRecord weapon,BlockRecord support)
        {
            if(!BlockModules.IsWeapon(weapon.shape))return false;
            var local=BlockModules.LocalCells(weapon.shape);int bottom=int.MaxValue;
            foreach(var c in local)if(c.y<bottom)bottom=c.y;
            var anchor=BlockPlacement.GridHint(weapon.position);
            var down=Vector3Int.RoundToInt(weapon.rotation*Vector3Int.down);
            foreach(var c in local)
            {
                if(c.y!=bottom)continue;
                var cell=anchor+Vector3Int.RoundToInt(weapon.rotation*(Vector3)c);
                var below=cell+down;bool owns=false;
                foreach(var body in BlockModules.BodyCells(support))if(body==below){owns=true;break;}
                if(!owns)continue;
                var localSupport=Vector3Int.RoundToInt(Quaternion.Inverse(support.rotation)*(Vector3)(below-BlockPlacement.GridHint(support.position)));
                var normal=Vector3Int.RoundToInt(Quaternion.Inverse(support.rotation)*(-(Vector3)down));
                if(BlockModules.HasAttachmentSurface(support.shape,localSupport,normal))return true;
            }
            return false;
        }
        // Wide weapons and armor need a support somewhere under every width column of their bottom layer.
        public static bool HasCompleteMount(BlockRecord candidate,IEnumerable<BlockRecord> blocks)
        {
            if(!BlockModules.IsWeapon(candidate.shape)&&!BlockModules.IsArmor(candidate.shape))return true;
            var list=new List<BlockRecord>(blocks);if(list.Count==0)return true;
            var local=BlockModules.LocalCells(candidate.shape);int bottom=int.MaxValue;
            foreach(var c in local)if(c.y<bottom)bottom=c.y;
            bool armor=BlockModules.IsArmor(candidate.shape);
            int row=armor?BlockModules.MountingCell(candidate.shape).z:int.MinValue;
            var columns=new HashSet<int>();foreach(var c in local)if(c.y==bottom&&(!armor||c.z==row))columns.Add(c.x);
            var anchor=BlockPlacement.GridHint(candidate.position);
            var down=Vector3Int.RoundToInt(candidate.rotation*Vector3Int.down);
            foreach(int column in columns)
            {
                bool supported=false;
                foreach(var c in local)
                {
                    if(c.y!=bottom||c.x!=column||(armor&&c.z!=row))continue;
                    var cell=anchor+Vector3Int.RoundToInt(candidate.rotation*(Vector3)c);
                    foreach(var block in list)
                    {
                        var below=cell+down;bool owns=false;
                        foreach(var body in BlockModules.BodyCells(block))if(body==below){owns=true;break;}
                        if(!owns)continue;
                        var localSupport=Vector3Int.RoundToInt(Quaternion.Inverse(block.rotation)*(Vector3)(below-BlockPlacement.GridHint(block.position)));
                        var normal=Vector3Int.RoundToInt(Quaternion.Inverse(block.rotation)*(-(Vector3)down));
                        if(BlockModules.HasAttachmentSurface(block.shape,localSupport,normal)){supported=true;break;}
                    }
                    if(supported)break;
                }
                if(!supported)return false;
            }
            return columns.Count>0;
        }

        public static bool IsConnected(BlueprintData data,string excluded=null)
        {
            var parts=new Dictionary<string,BlockRecord>();
            var cells=new Dictionary<Vector3Int,BlockRecord>();
            foreach(var original in data.parts)
            {
                if(original.id==excluded)continue;
                var p=BlockPlacement.Copy(original);
                if(data.version==1){p.position=new Vector3(p.cell.x,p.cell.y+.5f,p.cell.z);p.rotation=Quaternion.Euler(0,p.yawQuarterTurns*90,0);}
                parts.Add(p.id,p);foreach(var cell in BlockModules.BodyCells(p))cells.Add(cell,p);
            }
            if(parts.Count<2)return true;
            var seen=new HashSet<string>();var queue=new Queue<BlockRecord>();
            foreach(var p in parts.Values){seen.Add(p.id);queue.Enqueue(p);break;}
            while(queue.Count>0)
            {
                var a=queue.Dequeue();
                foreach(var cell in BlockModules.BodyCells(a))foreach(var d in Directions)
                    if(cells.TryGetValue(cell+d,out var b)&&!seen.Contains(b.id)&&Touches(a,b)){seen.Add(b.id);queue.Enqueue(b);}
            }
            return seen.Count==parts.Count;
        }
    }
}
