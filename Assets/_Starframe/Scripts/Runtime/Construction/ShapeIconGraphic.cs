// @role Draw modular-part icons in the construction interface.
// @calls BlockGeometry

using UnityEngine;
namespace Starframe.Construction
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ShapeIconGraphic : UnityEngine.UI.MaskableGraphic
    {
        public BlockShape shape;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();var p=BlockGeometry.Points(shape);var edges=BlockGeometry.Edges(shape);
            float scale=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.59f;
            for(int i=0;i<edges.Length;i+=2)
            {
                Vector2 a=Project(p[edges[i]])*scale,b=Project(p[edges[i+1]])*scale;
                Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*.7f;
                int start=vh.currentVertCount;
                vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
        static Vector2 Project(Vector3 p)=>new Vector2((p.x-p.z)*.86f,p.y+(p.x+p.z)*.4f);
    }
}