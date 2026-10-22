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
            // Structural faces follow complete occupied cells, just like collision and damage.
            // Bevels, half-height visuals and the smaller core must never remove a grid connection.
            var cells=new HashSet<Vector3Int>(BlockModules.BodyCells(a));
            foreach(var cell in BlockModules.BodyCells(b))foreach(var direction in Directions)
                if(cells.Contains(cell+direction))return true;
            return false;
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
