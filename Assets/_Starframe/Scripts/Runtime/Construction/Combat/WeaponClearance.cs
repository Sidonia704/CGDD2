// @role Check weapon bores and the countermeasure's single upward cell column against ship geometry.
// @calls CylindricalHitQuery, BlockModules, BlockPlacement

using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
namespace Starframe.Combat
{
    public sealed class WeaponClearance
    {
        readonly CylindricalHitQuery cylinder=new CylindricalHitQuery();
        public bool IsBlockedBy(BlockRecord gun,ModuleDefinition module,BlockRecord obstacle,Mesh mesh)
        {
            if(gun==null||obstacle==null)return false;
            if(gun.shape==BlockShape.FlareLauncher||module&&module.countermeasure)
                return CountermeasurePathBlockedBy(gun,obstacle);
            if(!module||!module.weapon||!mesh)return false;
            var obstacleMatrix=Matrix4x4.TRS(obstacle.position,obstacle.rotation,Vector3.one);
            var offsets=module.weapon.muzzleOffsets;
            if(offsets==null||offsets.Length==0)return Blocked(gun,module,obstacleMatrix,mesh,module.muzzleLocalPosition);
            foreach(var offset in offsets)if(Blocked(gun,module,obstacleMatrix,mesh,module.muzzleLocalPosition+offset))return true;
            return false;
        }
        // This is an occupied-cell column, not a cone or a ray through the decorative mesh.
        // Any own body cell in that column up to the ship boundary blocks, including other weapons.
        public static bool CountermeasurePathBlockedBy(BlockRecord launcher,BlockRecord obstacle)
        {
            if(launcher==null||obstacle==null||ReferenceEquals(launcher,obstacle))return false;
            var origin=BlockPlacement.GridHint(launcher.position);
            var up=Vector3Int.RoundToInt(launcher.rotation*Vector3.up);
            if(up.sqrMagnitude!=1)return true;
            foreach(var cell in BlockModules.BodyCells(obstacle))
            {
                var delta=cell-origin;int distance=delta.x*up.x+delta.y*up.y+delta.z*up.z;
                if(distance>0&&delta==up*distance)return true;
            }
            return false;
        }
        bool Blocked(BlockRecord gun,ModuleDefinition module,Matrix4x4 obstacleMatrix,Mesh mesh,Vector3 local)
        {
            Vector3 muzzle=gun.position+gun.rotation*local,direction=gun.rotation*Vector3.forward;
            return cylinder.TryHitMesh(mesh,obstacleMatrix,muzzle,direction,Mathf.Max(.01f,module.muzzleClearanceDiameter)*.5f,0,module.weapon.range,out _);
        }
    }
}
