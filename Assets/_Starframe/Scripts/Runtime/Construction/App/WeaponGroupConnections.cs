// @role Draw canvas-space assignment links and diamond markers on visible or occluded native weapons.

using System.Collections.Generic;
using UnityEngine;

namespace Starframe.App
{
    /// <summary>Canvas-space links from group cards to the actual weapon preview.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WeaponGroupConnections:UnityEngine.UI.MaskableGraphic
    {
        public struct Connection
        {
            public Vector2 from,to;
            public Color color;
            public float width;
            public bool markerOnly;
        }
        readonly List<Connection> connections=new List<Connection>();
        protected override void Awake(){base.Awake();raycastTarget=false;}
        public void SetConnections(IReadOnlyList<Connection> values)
        {
            int count=values!=null?Mathf.Min(values.Count,512):0;bool changed=connections.Count!=count;
            if(!changed)for(int i=0;i<count;i++){
                var previous=connections[i];var current=values[i];
                if((previous.from-current.from).sqrMagnitude>.01f||(previous.to-current.to).sqrMagnitude>.01f||previous.color!=current.color||previous.markerOnly!=current.markerOnly||!Mathf.Approximately(previous.width,current.width)){changed=true;break;}
            }
            if(!changed)return;connections.Clear();for(int i=0;i<count;i++)connections.Add(values[i]);SetVerticesDirty();
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();foreach(var link in connections){
                if(link.markerOnly){Dot(mesh,link.to,Mathf.Max(3,link.width),link.color);continue;}
                Vector2 previous=link.from;float bend=Mathf.Max(35,Mathf.Abs(link.to.y-link.from.y)*.4f);
                var a=link.from+Vector2.down*bend;var b=link.to+Vector2.up*bend;b.y=Mathf.Min(link.from.y,b.y);
                Segment(mesh,link.from+Vector2.up*5,link.from,Mathf.Max(1,link.width),link.color);
                const int steps=18;
                for(int i=1;i<=steps;i++){
                    float t=(float)i/steps,s=1-t;var next=s*s*s*link.from+3*s*s*t*a+3*s*t*t*b+t*t*t*link.to;
                    Segment(mesh,previous,next,Mathf.Max(1,link.width),link.color);previous=next;
                }
                Dot(mesh,link.to,Mathf.Max(2.4f,link.width*1.5f),link.color);
            }
        }
        static void Segment(UnityEngine.UI.VertexHelper mesh,Vector2 start,Vector2 end,float width,Color color)
        {
            var delta=end-start;if(delta.sqrMagnitude<.001f)return;var offset=new Vector2(-delta.y,delta.x).normalized*width*.5f;int index=mesh.currentVertCount;
            mesh.AddVert(start-offset,color,Vector2.zero);mesh.AddVert(start+offset,color,Vector2.zero);
            mesh.AddVert(end+offset,color,Vector2.zero);mesh.AddVert(end-offset,color,Vector2.zero);
            mesh.AddTriangle(index,index+1,index+2);mesh.AddTriangle(index,index+2,index+3);
        }
        static void Dot(UnityEngine.UI.VertexHelper mesh,Vector2 centre,float radius,Color color)
        {
            int index=mesh.currentVertCount;mesh.AddVert(centre,color,Vector2.zero);
            const int sides=4;for(int i=0;i<sides;i++){float angle=i*(Mathf.PI*2/sides);mesh.AddVert(centre+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,color,Vector2.zero);}
            for(int i=0;i<sides;i++)mesh.AddTriangle(index,index+1+i,index+1+(i+1)%sides);
        }
    }
}
