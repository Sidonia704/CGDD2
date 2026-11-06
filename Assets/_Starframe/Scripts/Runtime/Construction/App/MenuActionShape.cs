// @role Render the Figma menu action silhouette and release owned icon materials.
// @calls FigmaVisuals

using UnityEngine;
using UnityEngine.EventSystems;

namespace Starframe.App
{
    public sealed class MenuIconMaterialOwner:MonoBehaviour
    {
        public Material value;void OnDestroy(){if(value)Destroy(value);}
    }
    /// <summary>The reference's action button rounds only its lower-right corner.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuActionShape:UnityEngine.UI.MaskableGraphic
    {
        public float radius=35;
        public static void Apply(UnityEngine.UI.Button button,float cornerRadius=35)
        {
            var original=button.GetComponent<UnityEngine.UI.Image>();if(original)original.enabled=false;
            var shapeObject=new GameObject("ActionSilhouette",typeof(RectTransform));shapeObject.layer=button.gameObject.layer;
            var shapeRect=(RectTransform)shapeObject.transform;shapeRect.SetParent(button.transform,false);shapeRect.anchorMin=Vector2.zero;shapeRect.anchorMax=Vector2.one;shapeRect.offsetMin=shapeRect.offsetMax=Vector2.zero;shapeRect.SetAsFirstSibling();
            var shape=shapeObject.AddComponent<MenuActionShape>();shape.radius=cornerRadius;shape.color=Starframe.Presentation.FigmaVisuals.Orange;button.targetGraphic=shape;
            var label=button.GetComponentInChildren<TMPro.TextMeshProUGUI>();if(label){label.alignment=TMPro.TextAlignmentOptions.MidlineLeft;label.margin=new Vector4(38,0,4,0);label.overflowMode=TMPro.TextOverflowModes.Overflow;}
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();var rect=rectTransform.rect;float r=Mathf.Min(radius,rect.width,rect.height);var tint=(Color32)color;
            var points=new System.Collections.Generic.List<Vector2>{new Vector2(rect.xMin,rect.yMax),new Vector2(rect.xMax,rect.yMax),new Vector2(rect.xMax,rect.yMin+r)};
            var arcCenter=new Vector2(rect.xMax-r,rect.yMin+r);
            for(int i=1;i<=12;i++){float angle=-Mathf.PI*.5f*i/12;points.Add(arcCenter+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*r);}
            points.Add(new Vector2(rect.xMin,rect.yMin));vh.AddVert(rect.center,tint,Vector2.zero);
            foreach(var point in points)vh.AddVert(point,tint,Vector2.zero);
            for(int i=0;i<points.Count;i++)vh.AddTriangle(0,i+1,(i+1)%points.Count+1);
        }
    }
}
