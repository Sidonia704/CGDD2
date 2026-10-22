// @role Solve a finite-speed projectile interception against a moving target.

using System;
using UnityEngine;
namespace Starframe.Combat
{
    public static class InterceptSolver
    {
        // Projectiles use world velocity and do not inherit the firing ship's velocity.
        public static bool TrySolve(Vector3 origin,Vector3 target,Vector3 velocity,float speed,float range,out Vector3 point,out float time)
        {
            point=target;time=0;if(speed<=0||range<=0||!Finite(origin)||!Finite(target)||!Finite(velocity)||float.IsNaN(speed)||float.IsInfinity(speed))return false;
            Vector3 relative=target-origin;double c=relative.sqrMagnitude;if(c<1e-8)return true;
            double a=(double)velocity.sqrMagnitude-(double)speed*speed,b=2*(double)Vector3.Dot(relative,velocity),t=double.PositiveInfinity;
            if(Math.Abs(a)<Math.Max(1,speed*speed)*1e-8){if(b<0)t=-c/b;}
            else{double discriminant=b*b-4*a*c;if(discriminant>=0){double root=Math.Sqrt(discriminant),u=(-b-root)/(2*a),v=(-b+root)/(2*a);if(u>0)t=u;if(v>0)t=Math.Min(t,v);}}
            if(double.IsInfinity(t)||double.IsNaN(t)||t<0||t>(double)range/speed)return false;
            time=(float)t;point=target+velocity*time;return Finite(point);
        }
        static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
    }
}
