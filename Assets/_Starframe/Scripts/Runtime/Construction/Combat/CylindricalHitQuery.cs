// @role Intersect cylindrical projectile volumes with transformed collider meshes.

using UnityEngine;
using System.Collections.Generic;
namespace Starframe.Combat
{
    // Exact flat-ended cylinder versus mesh triangles; capsules are only the broad phase.
    public sealed class CylindricalHitQuery
    {
        Vector3[] beamVertices=new Vector3[64];
        readonly Vector3[] a=new Vector3[8],b=new Vector3[8];
        sealed class MeshData{public Vector3[] vertices;public int[] triangles;}
        readonly Dictionary<Mesh,MeshData> cache=new Dictionary<Mesh,MeshData>();
        readonly List<float> crossings=new List<float>();
        MeshData Data(Mesh mesh){if(!cache.TryGetValue(mesh,out var d)){d=new MeshData{vertices=mesh.vertices,triangles=mesh.triangles};cache.Add(mesh,d);}return d;}
        static readonly int[] BoxTriangles={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,1,2,6,1,6,5,0,4,7,0,7,3};
        public static Matrix4x4 PhysicsMatrix(Collider collider)
        {
            var body=collider.attachedRigidbody;
            return body?Matrix4x4.TRS(body.position,body.rotation,body.transform.lossyScale)*body.transform.worldToLocalMatrix*collider.transform.localToWorldMatrix:collider.transform.localToWorldMatrix;
        }
        public bool TryHit(Collider collider,Vector3 origin,Vector3 direction,float radius,float from,float to,out float entry)
        {
            entry=float.PositiveInfinity;if(!collider||!collider.enabled||to<from||direction.sqrMagnitude<.000001f)return false;
            direction.Normalize();
            Vector3 start=origin+direction*from;
            if(!(collider is MeshCollider)&&(collider.ClosestPoint(start)-start).sqrMagnitude<.00000001f){entry=from;return true;}
            var hitbox=collider.GetComponent<ShipPartHitbox>();Vector3[] vertices=hitbox?hitbox.Vertices:null;int[] triangles=hitbox?hitbox.Triangles:null;
            Matrix4x4 matrix=PhysicsMatrix(collider);
            if(vertices==null&&collider is MeshCollider mc&&mc.sharedMesh){var data=Data(mc.sharedMesh);vertices=data.vertices;triangles=data.triangles;}
            if(vertices==null||triangles==null){
                var box=collider as BoxCollider;var bounds=box?new Bounds(box.center,box.size):collider.bounds;
                vertices=a;Vector3 lo=bounds.min,hi=bounds.max;
                a[0]=new Vector3(lo.x,lo.y,lo.z);a[1]=new Vector3(hi.x,lo.y,lo.z);a[2]=new Vector3(hi.x,lo.y,hi.z);a[3]=new Vector3(lo.x,lo.y,hi.z);
                a[4]=new Vector3(lo.x,hi.y,lo.z);a[5]=new Vector3(hi.x,hi.y,lo.z);a[6]=new Vector3(hi.x,hi.y,hi.z);a[7]=new Vector3(lo.x,hi.y,hi.z);
                if(!box)matrix=Matrix4x4.identity;triangles=BoxTriangles;
            }
            return TryGeometry(vertices,triangles,matrix,origin,direction,radius,from,to,out entry);
        }
        public bool TryHitMesh(Mesh mesh,Matrix4x4 matrix,Vector3 origin,Vector3 direction,float radius,float from,float to,out float entry)
        {
            entry=float.PositiveInfinity;if(!mesh||to<from||direction.sqrMagnitude<.000001f)return false;var d=Data(mesh);
            return TryGeometry(d.vertices,d.triangles,matrix,origin,direction.normalized,radius,from,to,out entry);
        }
        bool TryGeometry(Vector3[] vertices,int[] triangles,Matrix4x4 matrix,Vector3 origin,Vector3 direction,float radius,float from,float to,out float entry)
        {
            entry=float.PositiveInfinity;radius=Mathf.Max(.0001f,radius-.0001f);
            if(beamVertices.Length<vertices.Length)beamVertices=new Vector3[Mathf.NextPowerOfTwo(vertices.Length)];
            var right=Vector3.Cross(direction,Mathf.Abs(direction.y)<.9f?Vector3.up:Vector3.right).normalized;var up=Vector3.Cross(right,direction);
            Vector3 minimum=Vector3.one*float.PositiveInfinity,maximum=Vector3.one*float.NegativeInfinity;
            for(int i=0;i<vertices.Length;i++){var delta=matrix.MultiplyPoint3x4(vertices[i])-origin;var v=new Vector3(Vector3.Dot(delta,right),Vector3.Dot(delta,up),Vector3.Dot(delta,direction));beamVertices[i]=v;minimum=Vector3.Min(minimum,v);maximum=Vector3.Max(maximum,v);}
            if(maximum.z<from||minimum.z>to||minimum.x>radius||maximum.x<-radius||minimum.y>radius||maximum.y<-radius)return false;
            if(minimum.x<0&&maximum.x>0&&minimum.y<0&&maximum.y>0&&minimum.z<from&&maximum.z>from&&InsideMesh(new Vector3(0,0,from),triangles)){entry=from;return true;}
            float r2=radius*radius;
            for(int i=0;i<triangles.Length;i+=3){
                Vector3 p=beamVertices[triangles[i]],q=beamVertices[triangles[i+1]],r=beamVertices[triangles[i+2]];
                a[0]=p;a[1]=q;a[2]=r;int count=Clip(a,3,b,from,true);count=Clip(b,count,a,to,false);if(count<3)continue;
                for(int j=0;j<count;j++){
                    var v=a[j];if(v.x*v.x+v.y*v.y<=r2)entry=Mathf.Min(entry,v.z);
                    var w=a[(j+1)%count];var d=w-v;float aa=d.x*d.x+d.y*d.y;
                    if(aa<.000000001f)continue;
                    float bb=2*(v.x*d.x+v.y*d.y),cc=v.x*v.x+v.y*v.y-r2,discriminant=bb*bb-4*aa*cc;
                    if(discriminant<0)continue;float root=Mathf.Sqrt(Mathf.Max(0,discriminant));
                    Candidate((-bb-root)/(2*aa),v,d,ref entry);Candidate((-bb+root)/(2*aa),v,d,ref entry);
                }
                var normal=Vector3.Cross(q-p,r-p);
                if(Mathf.Abs(normal.z)>.000001f){
                    float constant=Vector3.Dot(normal,p)/normal.z;var gradient=new Vector2(-normal.x/normal.z,-normal.y/normal.z);
                    if(Inside(Vector2.zero,a,count))entry=Mathf.Min(entry,constant);
                    var tangent=gradient.sqrMagnitude>.00000001f?-gradient.normalized*radius:Vector2.zero;
                    if(Inside(tangent,a,count))entry=Mathf.Min(entry,constant+Vector2.Dot(gradient,tangent));
                }
            }
            if(float.IsInfinity(entry))return false;entry=Mathf.Clamp(entry,from,to);return true;
        }
        bool InsideMesh(Vector3 origin,int[] triangles)
        {
            crossings.Clear();var ray=new Vector3(1,.371f,.529f).normalized;
            for(int i=0;i<triangles.Length;i+=3){var p=beamVertices[triangles[i]];var e1=beamVertices[triangles[i+1]]-p;var e2=beamVertices[triangles[i+2]]-p;var h=Vector3.Cross(ray,e2);float det=Vector3.Dot(e1,h);if(Mathf.Abs(det)<1e-8f)continue;
                var s=origin-p;float u=Vector3.Dot(s,h)/det;if(u<0||u>1)continue;var q=Vector3.Cross(s,e1);float v=Vector3.Dot(ray,q)/det;if(v<0||u+v>1)continue;float distance=Vector3.Dot(e2,q)/det;if(distance<=.00001f)continue;
                bool duplicate=false;foreach(float t in crossings)if(Mathf.Abs(t-distance)<.00001f){duplicate=true;break;}if(!duplicate)crossings.Add(distance);
            }return(crossings.Count&1)!=0;
        }
        static void Candidate(float t,Vector3 start,Vector3 delta,ref float entry){if(t>=0&&t<=1)entry=Mathf.Min(entry,start.z+delta.z*t);}
        static int Clip(Vector3[] input,int count,Vector3[] output,float plane,bool lower)
        {
            if(count==0)return 0;int n=0;var previous=input[count-1];bool was=lower?previous.z>=plane:previous.z<=plane;
            for(int i=0;i<count;i++){var current=input[i];bool inside=lower?current.z>=plane:current.z<=plane;
                if(inside!=was){float t=(plane-previous.z)/(current.z-previous.z);output[n++]=Vector3.LerpUnclamped(previous,current,t);}
                if(inside)output[n++]=current;previous=current;was=inside;
            }return n;
        }
        static bool Inside(Vector2 p,Vector3[] polygon,int count)
        {
            float sign=0;for(int i=0;i<count;i++){var v=polygon[i];var w=polygon[(i+1)%count];float cross=(w.x-v.x)*(p.y-v.y)-(w.y-v.y)*(p.x-v.x);
                if(Mathf.Abs(cross)<.000001f)continue;if(sign==0)sign=Mathf.Sign(cross);else if(sign*cross<0)return false;
            }return sign!=0;
        }
    }
}
