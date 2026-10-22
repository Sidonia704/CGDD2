// @role Check a weapon muzzle against occupied ship geometry.
// @calls CylindricalHitQuery

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
            if(!module||!module.weapon||!mesh)return false;
            Vector3 muzzle=gun.position+gun.rotation*module.muzzleLocalPosition,direction=gun.rotation*Vector3.forward;
            return cylinder.TryHitMesh(mesh,Matrix4x4.TRS(obstacle.position,obstacle.rotation,Vector3.one),muzzle,direction,Mathf.Max(.01f,module.muzzleClearanceDiameter)*.5f,0,module.weapon.range,out _);
        }
    }
}
