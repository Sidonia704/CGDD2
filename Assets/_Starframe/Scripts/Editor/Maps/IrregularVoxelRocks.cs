using System;
using System.Collections.Generic;
using System.Linq;
using Starframe.Maps;
using Starframe.Construction;
using UnityEngine;

namespace Starframe.EditorTools
{
    // Low-frequency natural silhouettes: three overlapping eccentric faceted prisms on one integer lattice.
    // No source triangle sampling, independent-cell noise, visual shell or non-native geometry.
    public static class IrregularVoxelRocks
    {
        struct Plane{public Vector3 normal;public int limit;}
        struct CellUnion{public VoxelCell cell;public Vector3 cutNormal;}
        struct RandomState
        {
            public uint state;
            public float Next(){state^=state<<13;state^=state>>17;state^=state<<5;return(state&0x00ffffff)/16777216f;}
        }
        static uint Seed(string key){uint h=2166136261;foreach(char c in key??"rock"){h^=c;h*=16777619;}return h==0?1:h;}
        public static List<VoxelCell> Build(Bounds bounds,float cell,int cap,string key,out Vector3 localOffset)
        {
            if(cell<=0||cap<1)throw new ArgumentOutOfRangeException();
            var random=new RandomState{state=Seed(key)};
            int minimumRadius=cap<256?1:3;
            var extent=bounds.extents/cell;
            var envelope=new Vector3Int(Mathf.Max(minimumRadius,Mathf.RoundToInt(extent.x-.5f)),Mathf.Max(minimumRadius,Mathf.RoundToInt(extent.y-.5f)),Mathf.Max(minimumRadius,Mathf.RoundToInt(extent.z-.5f)));
            var flip=new Vector3(random.Next()<.5f?-1:1,random.Next()<.5f?-1:1,random.Next()<.5f?-1:1);
            var merged=new Dictionary<Vector3Int,CellUnion>();
            // Each mass has one coherent slope family; subsidiary masses cycle onto different axes
            // to avoid an extruded capsule silhouette while keeping each mass's broad slopes continuous.
            int mainExtrusionAxis=(int)(Seed(key)%3);
            // Deliberately unequal, offset masses make broad shoulders, shallow saddles and a few large notches.
            // Their overlap is several cells deep; the result remains one connected boulder rather than satellites.
            var centres=new[]{new Vector3(-.16f,.03f,.12f),new Vector3(.38f,-.20f,-.22f),new Vector3(-.40f,.30f,-.34f)};
            var scales=new[]{new Vector3(.77f,.84f,.75f),new Vector3(.61f,.66f,.62f),new Vector3(.46f,.49f,.48f)};
            int masses=cap<256?2:3;
            for(int part=0;part<masses;part++){
                int extrusionAxis=(mainExtrusionAxis+part)%3;int crossA=(extrusionAxis+1)%3,crossB=(extrusionAxis+2)%3;
                // Stable whole-mass variation only: no independent cell displacement or surface noise.
                var centreJitter=new Vector3(random.Next()-.5f,random.Next()-.5f,random.Next()-.5f)*.12f;
                var scaleJitter=new Vector3(random.Next()-.5f,random.Next()-.5f,random.Next()-.5f)*.10f;
                var centre=Vector3Int.RoundToInt(Vector3.Scale(Vector3.Scale(centres[part]+centreJitter,flip),envelope));
                var e=Vector3Int.RoundToInt(Vector3.Scale(scales[part]+scaleJitter,envelope));for(int axis=0;axis<3;axis++)e[axis]=Mathf.Max(cap<256?1:2,e[axis]);
                var planes=new List<Plane>();
                // Four signed cross-section cuts share one extrusion direction. Each offset is independent;
                // cuts are shallower than either half-span, so two diagonal cuts cannot intersect inside a lobe.
                // This removes the omitted double/triple-cut voxel rows that previously produced triangular teeth.
                for(int sa=-1;sa<=1;sa+=2)for(int sb=-1;sb<=1;sb+=2){
                    var n=Vector3.zero;n[crossA]=sa;n[crossB]=sb;
                    int chamfer=Mathf.Max(1,Mathf.RoundToInt(Mathf.Min(e[crossA],e[crossB])*(.36f+random.Next()*.47f)));
                    chamfer=Mathf.Min(chamfer,Mathf.Min(e[crossA],e[crossB])-1);
                    planes.Add(new Plane{normal=n,limit=e[crossA]+e[crossB]-chamfer});
                }
                for(int x=-e.x;x<=e.x;x++)for(int y=-e.y;y<=e.y;y++)for(int z=-e.z;z<=e.z;z++){
                    var local=new Vector3Int(x,y,z);var position=local+centre;
                    if(Mathf.Abs(position.x)>envelope.x||Mathf.Abs(position.y)>envelope.y||Mathf.Abs(position.z)>envelope.z)continue;
                    bool outside=false;Plane boundary=default;int cuts=0;
                    foreach(var plane in planes){float d=Vector3.Dot(plane.normal,local)-plane.limit;if(d>.001f){outside=true;break;}if(d>-.001f){boundary=plane;cuts++;}}
                    if(outside)continue;
                    var voxel=new VoxelCell{position=position,shape=BlockShape.Cube};var cut=Vector3.zero;
                    if(cuts>0){
                        cut=boundary.normal.normalized;voxel.shape=BlockShape.Wedge;voxel.quarterTurns=Turns(cut);bool fits=true;
                        foreach(var vertex in BlockGeometry.Points(BlockShape.Wedge)){
                            var p=(Vector3)local+voxel.Rotation*vertex;
                            foreach(var plane in planes)if(Vector3.Dot(plane.normal,p)>plane.limit+.0001f){fits=false;break;}
                            if(!fits)break;
                        }
                        if(!fits)continue;
                    }
                    if(merged.TryGetValue(position,out var prior)){
                        if(prior.cell.shape==BlockShape.Cube)continue;
                        // A Cube is justified only by an actual full primitive cell or complementary halfspaces.
                        if(voxel.shape==BlockShape.Cube||Vector3.Dot(prior.cutNormal,cut)<-.999f){prior.cell.shape=BlockShape.Cube;prior.cell.quarterTurns=Vector3Int.zero;prior.cutNormal=Vector3.zero;merged[position]=prior;}
                        // Other partial unions have no exact existing module. Retain the larger/main mass's contained
                        // Wedge instead of inventing solid corner voxels; the approximation is inward and at most one cell.
                    }else merged.Add(position,new CellUnion{cell=voxel,cutNormal=cut});
                    if(merged.Count>cap)throw new InvalidOperationException("Natural boulder exceeds cell budget; increase cellSize");
                }
            }
            if(merged.Count==0)throw new InvalidOperationException("Empty natural boulder");
            var result=merged.Values.Select(v=>v.cell).OrderBy(c=>c.position.z).ThenBy(c=>c.position.y).ThenBy(c=>c.position.x).ToList();
            localOffset=bounds.center-Centre(result,cell);return result;
        }
        public static List<VoxelCell> BuildCentered(Bounds bounds,float cell,int cap,string key)
        {
            var result=Build(bounds,cell,cap,key,out _);
            // Existing scenery centres are integer-grid compatible. Trim at most one outer positive layer per axis
            // when parity would otherwise require a half-cell translation; then translate by whole cells only.
            for(int axis=0;axis<3;axis++){
                int min=result.Min(c=>c.position[axis]),max=result.Max(c=>c.position[axis]);
                if(((min+max)&1)!=0){result.RemoveAll(c=>c.position[axis]==max);min=result.Min(c=>c.position[axis]);max=result.Max(c=>c.position[axis]);}
                int shift=Mathf.RoundToInt(bounds.center[axis]/cell)-(min+max)/2;
                foreach(var c in result){var p=c.position;p[axis]+=shift;c.position=p;}
            }
            return result;
        }
        static Vector3 Centre(List<VoxelCell> cells,float cell){var min=cells[0].position;var max=min;foreach(var c in cells){min=Vector3Int.Min(min,c.position);max=Vector3Int.Max(max,c.position);}return((Vector3)min+(Vector3)max)*(cell*.5f);}
        static Vector3Int Turns(Vector3 normal){for(int x=0;x<4;x++)for(int y=0;y<4;y++)for(int z=0;z<4;z++)if(Vector3.Dot(Quaternion.Euler(x*90,y*90,z*90)*new Vector3(0,1,-1).normalized,normal)>.999f)return new Vector3Int(x,y,z);throw new Exception("Native wedge orientation unavailable");}
    }
}
