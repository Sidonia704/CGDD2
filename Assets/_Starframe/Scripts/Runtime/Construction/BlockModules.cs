// @role Resolve full-cell occupancy, equipment mounting faces and independent nozzle clearances.
// @calls ModuleDatabase, BlockPlacement, BlockGeometry

using System.Collections.Generic;
using UnityEngine;
namespace Starframe.Construction
{
    public static class BlockModules
    {
        static Starframe.Flight.ModuleDatabase database;
        public static Starframe.Flight.ModuleDatabase ConfiguredDatabase=>database;
        static readonly Vector3Int[] Single={Vector3Int.zero};
        static readonly Dictionary<BlockShape,Vector3Int[]> fallbackCells=new Dictionary<BlockShape,Vector3Int[]>();
        public static void Configure(Starframe.Flight.ModuleDatabase values){if(database!=values)surfaces.Clear();database=values;}
        public static void InvalidateSurfaces()=>surfaces.Clear();
        public static Vector3Int MountingCell(BlockShape shape)
        {
            var part=new BlockRecord{shape=shape,position=Vector3.up*.5f,rotation=Quaternion.identity};
            Vector3Int best=Vector3Int.zero;float distance=float.PositiveInfinity;
            foreach(var cell in LocalCells(shape))
            {
                if(cell.sqrMagnitude>=distance)continue;
                var support=new BlockRecord{shape=BlockShape.Cube,position=(Vector3)(cell+Vector3Int.down)+Vector3.up*.5f,rotation=Quaternion.identity};
                if(FacesContact(part,cell,Vector3Int.down,support,cell+Vector3Int.down)){best=cell;distance=cell.sqrMagnitude;}
            }
            return best;
        }
        public static bool IsThruster(BlockShape shape)=>shape==BlockShape.Thruster1||shape==BlockShape.Thruster2||shape==BlockShape.Thruster3||shape==BlockShape.Thruster4||shape==BlockShape.Thruster5||shape==BlockShape.Thruster6;
        public static bool IsArmor(BlockShape shape)=>shape>=BlockShape.Armor1&&shape<=BlockShape.Armor4;
        public static bool IsVerticalLaunch(BlockShape shape)=>shape==BlockShape.VLS1||shape==BlockShape.VLS2;
        public static bool IsCountermeasure(BlockShape shape)
        {
            var definition=database?database.Get(shape):null;
            return shape==BlockShape.FlareLauncher||definition&&definition.countermeasure;
        }
        public static bool IsWeapon(BlockShape shape)
        {
            var definition=database?database.Get(shape):null;
            return definition&&(definition.weapon||definition.countermeasure) || shape==BlockShape.FlareLauncher || shape==BlockShape.Laser || shape>=BlockShape.Gun1&&shape<=BlockShape.Sword2||IsVerticalLaunch(shape);
        }
        public static Vector3Int[] LocalCells(BlockShape shape)
        {
            // The countermeasure reserves one complete cell even when its launcher mesh is inset.
            if(shape==BlockShape.FlareLauncher)return Single;
            var d=database?database.Get(shape):null;
            if(d&&d.occupiedCells!=null&&d.occupiedCells.Length>0)return d.occupiedCells;
            if(fallbackCells.TryGetValue(shape,out var cells))return cells;
            Vector3Int size=Vector3Int.one;
            switch(shape)
            {
                case BlockShape.Thruster2:size=new Vector3Int(1,2,1);break;
                case BlockShape.Laser:case BlockShape.Block7:case BlockShape.Armor2:size=new Vector3Int(1,1,shape==BlockShape.Armor2||shape==BlockShape.Block7?2:3);break;
                case BlockShape.Block8:case BlockShape.Power2:case BlockShape.Shield2:size=new Vector3Int(2,2,2);break;
                case BlockShape.Thruster3:size=new Vector3Int(1,2,2);break;
                case BlockShape.Thruster4:size=new Vector3Int(1,2,4);break;
                case BlockShape.Thruster5:size=new Vector3Int(2,3,2);break;
                case BlockShape.Armor3:case BlockShape.Gun1:case BlockShape.Missile1:case BlockShape.Sword1:size=new Vector3Int(1,1,3);break;
                case BlockShape.Armor4:size=new Vector3Int(2,1,3);break;
                case BlockShape.Gun2:case BlockShape.Laser2:case BlockShape.Sword2:size=new Vector3Int(2,1,4);break;
                case BlockShape.Gun3:case BlockShape.Laser3:size=new Vector3Int(2,2,4);break;
                case BlockShape.Railgun1:size=new Vector3Int(1,1,4);break;
                case BlockShape.Railgun2:size=new Vector3Int(1,1,5);break;
                case BlockShape.Railgun3:size=new Vector3Int(2,2,6);break;
                case BlockShape.Missile2:size=new Vector3Int(2,1,3);break;
                case BlockShape.Missile3:size=new Vector3Int(1,1,5);break;
                case BlockShape.VLS1:size=new Vector3Int(2,2,2);break;
                case BlockShape.VLS2:size=new Vector3Int(1,2,2);break;
            }
            if(size==Vector3Int.one)return Single;
            var result=new List<Vector3Int>();for(int x=0;x<size.x;x++)for(int y=0;y<size.y;y++)for(int z=0;z<size.z;z++)result.Add(new Vector3Int(x,y,z));
            cells=result.ToArray();fallbackCells[shape]=cells;return cells;
        }
        public static int BodyLength(BlockShape shape)=>LocalCells(shape).Length;
        public static bool FullCellConnection(BlockShape shape)=>shape==BlockShape.Cube||shape==BlockShape.Core||shape==BlockShape.Power||shape==BlockShape.Shield||shape==BlockShape.Block8||shape==BlockShape.Power2||shape==BlockShape.Shield2;
        public static Vector3 LocalCenter(BlockShape shape){var cells=LocalCells(shape);Vector3 sum=Vector3.zero;foreach(var cell in cells)sum+=(Vector3)cell;return sum/cells.Length;}
        public static int Clearance(BlockShape shape)=>shape==BlockShape.Thruster1?2:shape==BlockShape.Thruster2?3:shape==BlockShape.Thruster3?3:shape==BlockShape.Thruster4?4:shape==BlockShape.Thruster5?4:shape==BlockShape.Thruster6||IsVerticalLaunch(shape)?1:0;
        public static Vector3Int Direction(Quaternion rotation)=>Vector3Int.RoundToInt(rotation*Vector3.up);
        public static List<Vector3Int> BodyCells(BlockRecord part)
        {
            var cells=new List<Vector3Int>();var anchor=BlockPlacement.GridHint(part.position);
            foreach(var offset in LocalCells(part.shape))cells.Add(anchor+Vector3Int.RoundToInt(part.rotation*(Vector3)offset));return cells;
        }
        static readonly Vector3[] SingleExhaust={Vector3.up};
        static readonly Vector3[] FiveExhausts={Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};
        public static Vector3[] LocalExhaustDirections(BlockShape shape)
        {
            var definition=database?database.Get(shape):null;
            return definition&&definition.localExhaustDirections!=null&&definition.localExhaustDirections.Length>0
                ?definition.localExhaustDirections:shape==BlockShape.Thruster6?FiveExhausts:SingleExhaust;
        }
        public static Vector3 LocalNozzlePosition(BlockShape shape,int nozzle,Mesh mesh=null)
        {
            var definition=database?database.Get(shape):null;var directions=LocalExhaustDirections(shape);
            var direction=directions[Mathf.Clamp(nozzle,0,directions.Length-1)].normalized;
            if(definition&&definition.localNozzlePositions!=null&&nozzle>=0&&nozzle<definition.localNozzlePositions.Length)
                return definition.localNozzlePositions[nozzle]+direction*.015f;
            if(!mesh&&definition)mesh=definition.mesh;
            var bounds=mesh?mesh.bounds:new Bounds(LocalCenter(shape),Vector3.one);
            var position=bounds.center;for(int axis=0;axis<3;axis++)if(direction[axis]>.5f)position[axis]=bounds.max[axis]+.015f;else if(direction[axis]<-.5f)position[axis]=bounds.min[axis]-.015f;
            return position;
        }
        public static Vector2 LocalNozzleAperture(BlockShape shape,int nozzle,Mesh mesh=null)
        {
            if(shape==BlockShape.Thruster6)return new Vector2(.48f,.48f);
            var definition=database?database.Get(shape):null;if(!mesh&&definition)mesh=definition.mesh;
            var size=mesh?mesh.bounds.size:Vector3.one;var directions=LocalExhaustDirections(shape);
            var rotation=Quaternion.FromToRotation(Vector3.up,directions[Mathf.Clamp(nozzle,0,directions.Length-1)].normalized);
            var x=rotation*Vector3.right;var z=rotation*Vector3.forward;
            return new Vector2(Mathf.Abs(x.x)*size.x+Mathf.Abs(x.y)*size.y+Mathf.Abs(x.z)*size.z,Mathf.Abs(z.x)*size.x+Mathf.Abs(z.y)*size.y+Mathf.Abs(z.z)*size.z);
        }
        public static List<Vector3Int> AllCells(BlockRecord part)
        {
            var occupied=new HashSet<Vector3Int>(BodyCells(part));foreach(var cell in ExhaustCells(part))occupied.Add(cell);return new List<Vector3Int>(occupied);
        }
        // Thruster6 trades the full-cell placement reservation for a half-cell judgement;
        // its runtime nozzle blocking still uses the complete adjacent cell.
        public static float PlacementClearance(BlockShape shape)=>shape==BlockShape.Thruster6?.5f:1f;
        public static List<Vector3Int> PlacementCells(BlockRecord part)
        {
            var occupied=new HashSet<Vector3Int>(BodyCells(part));
            if(part.shape!=BlockShape.Thruster6)foreach(var cell in ExhaustCells(part))occupied.Add(cell);
            return new List<Vector3Int>(occupied);
        }
        // A nozzle reserves only its own outward columns. Blocking one never disables its siblings.
        public static List<Vector3Int> ExhaustCells(BlockRecord part,int nozzle=-1)
        {
            int clearance=Clearance(part.shape);var reserved=new HashSet<Vector3Int>();if(clearance==0)return new List<Vector3Int>();
            var local=LocalCells(part.shape);var occupied=new HashSet<Vector3Int>(local);var directions=LocalExhaustDirections(part.shape);
            var anchor=BlockPlacement.GridHint(part.position);
            for(int index=0;index<directions.Length;index++)
            {
                if(nozzle>=0&&index!=nozzle)continue;var direction=Vector3Int.RoundToInt(directions[index].normalized);if(direction.sqrMagnitude!=1)continue;
                foreach(var cell in local)if(!occupied.Contains(cell+direction))
                    for(int i=1;i<=clearance;i++)reserved.Add(anchor+Vector3Int.RoundToInt(part.rotation*(Vector3)(cell+direction*i)));
            }
            return new List<Vector3Int>(reserved);
        }
        public static bool CellsOverlap(BlockRecord a,BlockRecord b)
        {
            var first=new HashSet<Vector3Int>(PlacementCells(a));
            foreach(var cell in PlacementCells(b))if(first.Contains(cell))return true;return false;
        }

        // Decorative bevels are intentionally inset. This tolerance bridges their shallow seams,
        // while a half-height top, a sloping face and empty weapon cells remain unsupported.
        const float SeamTolerance=.105f;
        sealed class SurfaceModel
        {
            public Mesh mesh;public int revision;
            public readonly Dictionary<Vector3Int,Dictionary<Vector3Int,List<Vector3[]>>> cells=new Dictionary<Vector3Int,Dictionary<Vector3Int,List<Vector3[]>>>();
        }
        static readonly Dictionary<BlockShape,SurfaceModel> surfaces=new Dictionary<BlockShape,SurfaceModel>();
        static readonly Vector3Int[] directions={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,Vector3Int.forward,Vector3Int.back};
        static List<Vector3[]> Patches(BlockShape shape,Vector3Int cell,Vector3Int normal)
        {
            // VLS cell housing supports all exterior sides and its bottom; the launch lid is never a mounting face.
            if(IsVerticalLaunch(shape))return normal==Vector3Int.up?null:FullFace(cell,normal);
            if((IsWeapon(shape)||IsArmor(shape)||shape==BlockShape.Thruster6)&&normal!=Vector3Int.down)return null;
            // Power modules keep their regular full-cell faces even when the decorative mesh
            // leaves an inset top or a hollow centre.
            if(shape==BlockShape.Power||shape==BlockShape.Power2)return FullFace(cell,normal);
            var definition=database?database.Get(shape):null;Mesh mesh=definition?definition.mesh:null;int revision=definition?definition.Revision:0;
            if(!surfaces.TryGetValue(shape,out var model)||model.mesh!=mesh||model.revision!=revision)
            {
                model=new SurfaceModel{mesh=mesh,revision=revision};surfaces[shape]=model;
                var source=mesh?mesh:BlockGeometry.CreateSolid(shape);
                var vertices=source.vertices;var triangles=source.triangles;
                foreach(var c in LocalCells(shape))
                {
                    var faceMap=new Dictionary<Vector3Int,List<Vector3[]>>();model.cells[c]=faceMap;
                    foreach(var n in directions)
                    {
                        var patches=new List<Vector3[]>();faceMap[n]=patches;float plane=Vector3.Dot(c,n)+.5f;
                        for(int t=0;t<triangles.Length;t+=3)
                        {
                            var a=vertices[triangles[t]];var b=vertices[triangles[t+1]];var v=vertices[triangles[t+2]];
                            if(Mathf.Abs(Vector3.Dot(a,n)-plane)>SeamTolerance||Mathf.Abs(Vector3.Dot(b,n)-plane)>SeamTolerance||Mathf.Abs(Vector3.Dot(v,n)-plane)>SeamTolerance)continue;
                            var cross=Vector3.Cross(b-a,v-a);
                            if(cross.sqrMagnitude<.00000001f||Vector3.Dot(cross.normalized,n)<.999f)continue;
                            int axis=n.x!=0?0:n.y!=0?1:2;var center=Project(c,axis);
                            var square=new[]{center+new Vector2(-.5f,-.5f),center+new Vector2(.5f,-.5f),center+new Vector2(.5f,.5f),center+new Vector2(-.5f,.5f)};
                            if(IntersectionArea(new List<Vector2>{Project(a,axis),Project(b,axis),Project(v,axis)},square)>.001f)patches.Add(new[]{a,b,v});
                        }
                    }
                }
                if(!mesh){if(Application.isPlaying)Object.Destroy(source);else Object.DestroyImmediate(source);}
            }
            return model.cells.TryGetValue(cell,out var faces)&&faces.TryGetValue(normal,out var result)?result:null;
        }
        public static bool HasAttachmentSurface(BlockShape shape,Vector3Int localCell,Vector3Int localNormal)
        {
            var patches=Patches(shape,localCell,localNormal);return patches!=null&&patches.Count>0;
        }
        static List<Vector3[]> FullFace(Vector3Int cell,Vector3Int normal)
        {
            int axis=normal.x!=0?0:normal.y!=0?1:2;
            int u=(axis+1)%3,v=(axis+2)%3;
            var centre=(Vector3)cell;
            centre[axis]=Vector3.Dot(cell,normal)+.5f*(normal.x+normal.y+normal.z);
            var a=Vector3.zero;var b=Vector3.zero;a[u]=.5f;b[v]=.5f;
            var p0=centre-a-b;var p1=centre+a-b;var p2=centre+a+b;var p3=centre-a+b;
            return new List<Vector3[]>{new[]{p0,p1,p2},new[]{p0,p2,p3}};
        }
        public static bool FacesContact(BlockRecord a,Vector3Int aCell,Vector3Int normal,BlockRecord b,Vector3Int bCell)
        {
            var localA=Vector3Int.RoundToInt(Quaternion.Inverse(a.rotation)*(Vector3)(aCell-BlockPlacement.GridHint(a.position)));
            var localB=Vector3Int.RoundToInt(Quaternion.Inverse(b.rotation)*(Vector3)(bCell-BlockPlacement.GridHint(b.position)));
            var normalA=Vector3Int.RoundToInt(Quaternion.Inverse(a.rotation)*(Vector3)normal);
            var normalB=Vector3Int.RoundToInt(Quaternion.Inverse(b.rotation)*(Vector3)(-normal));
            var patchesA=Patches(a.shape,localA,normalA);var patchesB=Patches(b.shape,localB,normalB);
            if(patchesA==null||patchesB==null)return false;
            int axis=normal.x!=0?0:normal.y!=0?1:2;
            foreach(var pa in patchesA)foreach(var pb in patchesB)
            {
                var polygon=new List<Vector2>(3);var clip=new Vector2[3];
                for(int i=0;i<3;i++){polygon.Add(Project(a.position+a.rotation*pa[i],axis));clip[i]=Project(b.position+b.rotation*pb[i],axis);}
                if(IntersectionArea(polygon,clip)>.001f)return true;
            }
            return false;
        }
        static Vector2 Project(Vector3 p,int axis)=>axis==0?new Vector2(p.y,p.z):axis==1?new Vector2(p.x,p.z):new Vector2(p.x,p.y);
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static float IntersectionArea(List<Vector2> polygon,Vector2[] clip)
        {
            float sign=Cross(clip[1]-clip[0],clip[2]-clip[0])>=0?1:-1;
            for(int side=0;side<clip.Length&&polygon.Count>0;side++)
            {
                var output=new List<Vector2>();var a=clip[side];var edge=clip[(side+1)%clip.Length]-a;var previous=polygon[polygon.Count-1];float previousDistance=Cross(edge,previous-a)*sign;
                foreach(var current in polygon)
                {
                    float distance=Cross(edge,current-a)*sign;
                    if((distance>=0)!=(previousDistance>=0))output.Add(Vector2.LerpUnclamped(previous,current,previousDistance/(previousDistance-distance)));
                    if(distance>=0)output.Add(current);
                    previous=current;previousDistance=distance;
                }
                polygon=output;
            }
            float area=0;for(int i=0;i<polygon.Count;i++)area+=Cross(polygon[i],polygon[(i+1)%polygon.Count]);return Mathf.Abs(area)*.5f;
        }
    }
}
