// @role Project an aiming direction into screen coordinates.

using UnityEngine;
namespace Starframe.Combat
{
    public static class AimProjection
    {
        // Both the ship reticle and lead markers represent firing directions, independent of camera parallax.
        public static Vector3 ScreenPoint(Camera camera,Vector3 direction)=>camera.WorldToScreenPoint(camera.transform.position+direction.normalized*1500f);
    }
}
