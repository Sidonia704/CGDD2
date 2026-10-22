// @role Convert grid poses and copy modular-part records.

using UnityEngine;
namespace Starframe.Construction
{
    public static class BlockPlacement
    {
        public static Quaternion FaceRotation(Vector3 faceNormal,int quarterTurns)
        {
            return Quaternion.AngleAxis(((quarterTurns%4)+4)%4*90,faceNormal)*
                Quaternion.FromToRotation(Vector3.up,faceNormal);
        }
        public static bool IsCellAligned(Vector3 position,Quaternion rotation)
        {
            if((position-(Vector3)GridHint(position)-Vector3.up*.5f).sqrMagnitude>.000001f)return false;
            return IsAxis(rotation*Vector3.right)&&IsAxis(rotation*Vector3.up)&&IsAxis(rotation*Vector3.forward);
        }
        static bool IsAxis(Vector3 n)
        {
            var rounded=Vector3Int.RoundToInt(n);
            return rounded.sqrMagnitude==1&&(n-(Vector3)rounded).sqrMagnitude<.000001f;
        }

        public static bool TryOnFace(BlockShape shape,Transform support,Vector3 worldNormal,int turns,out Pose pose)
            =>TryOnFace(shape,support,support.position,worldNormal,turns,out pose);
        public static bool TryOnFace(BlockShape shape,Transform support,Vector3 hitPoint,Vector3 worldNormal,int turns,out Pose pose)
        {
            pose=default;
            BlockGeometry.FindFace(shape,support.InverseTransformDirection(worldNormal),out _,out var localNormal);
            var n=support.TransformDirection(localNormal).normalized;
            if(!IsAxis(n))return false;
            n=(Vector3)Vector3Int.RoundToInt(n);
            var hitLocal=support.InverseTransformPoint(hitPoint);var nearest=Vector3.zero;float best=float.PositiveInfinity;
            foreach(var cell in BlockModules.LocalCells(shape)){float distance=(hitLocal-(Vector3)cell).sqrMagnitude;if(distance<best){best=distance;nearest=cell;}}
            var position=support.position+support.TransformDirection(nearest)+n;
            var rotation=FaceRotation(n,turns);
            if(!IsCellAligned(position,rotation))return false;
            pose=new Pose(position,rotation);return true;
        }
        public static Pose OnFace(BlockShape shape,Transform support,Vector3 normal,int turns)
        {
            if(!TryOnFace(shape,support,normal,turns,out var pose))
                throw new System.ArgumentException("Only standard grid faces can support a new block.");
            return pose;
        }
        public static Pose OnGround(Vector3 point,int turns)=>new Pose(
            new Vector3(Mathf.Round(point.x),.5f,Mathf.Round(point.z)),FaceRotation(Vector3.up,turns));
        public static BlockRecord Copy(BlockRecord p)=>new BlockRecord{
            id=p.id,shape=p.shape,cell=p.cell,yawQuarterTurns=p.yawQuarterTurns,position=p.position,rotation=p.rotation};
        public static Vector3Int GridHint(Vector3 position)=>Vector3Int.RoundToInt(position-Vector3.up*.5f);
    }
}
