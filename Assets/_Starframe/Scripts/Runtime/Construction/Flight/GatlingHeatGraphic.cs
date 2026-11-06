// @role Draw a separate Gatling heat gauge using reported heat and the real per-mount overheat state.
// @calls UiTheme

using UnityEngine;
namespace Starframe.Flight
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class GatlingHeatGraphic:UnityEngine.UI.MaskableGraphic
    {
        float heat;
        bool overheated,horizontal;
        public static Color OverheatColor=>new Color(1f,.66f,.16f,.98f);
        public void SetTelemetry(float fraction,bool lockedOut)
        {
            float next=Mathf.Clamp01(fraction);
            heat=next;overheated=lockedOut;SetVerticesDirty();
        }
        public void SetHorizontal(bool value){horizontal=value;SetVerticesDirty();}
        protected override void Awake(){base.Awake();raycastTarget=false;}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
        {
            vertices.Clear();Color ink=overheated?OverheatColor:Starframe.Presentation.UiTheme.Current.combatInstrument;
            ink.a=.98f;Color dim=ink;dim.a=.24f;
            if(horizontal)
            {
                Line(vertices,new Vector2(10,24),new Vector2(140,24),dim,2);
                for(int i=0;i<20;i++)Line(vertices,new Vector2(10+i*6.5f,18),new Vector2(10+i*6.5f,30),i/20f<heat?ink:dim,4);
                return;
            }
            Line(vertices,new Vector2(32,35),new Vector2(32,280),dim,2);
            for(int i=0;i<20;i++)Line(vertices,new Vector2(25,35+i*12.25f),new Vector2(39,35+i*12.25f),i/20f<heat?ink:dim,5);
            // Three small rising heat waves distinguish this gauge from battery and shield.
            for(int i=0;i<3;i++)
            {
                float x=22+i*10;
                Line(vertices,new Vector2(x,289),new Vector2(x-3,297),ink,2);
                Line(vertices,new Vector2(x-3,297),new Vector2(x+2,304),ink,2);
                Line(vertices,new Vector2(x+2,304),new Vector2(x,312),ink,2);
            }
        }
        void Line(UnityEngine.UI.VertexHelper vertices,Vector2 a,Vector2 b,Color tint,float width)
        {
            Rect rect=rectTransform.rect;float referenceWidth=horizontal?150:64,referenceHeight=horizontal?64:350;
            a=new Vector2(rect.xMin+a.x/referenceWidth*rect.width,rect.yMin+a.y/referenceHeight*rect.height);
            b=new Vector2(rect.xMin+b.x/referenceWidth*rect.width,rect.yMin+b.y/referenceHeight*rect.height);
            Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int index=vertices.currentVertCount;
            vertices.AddVert(a-normal,tint,Vector2.zero);vertices.AddVert(a+normal,tint,Vector2.zero);
            vertices.AddVert(b+normal,tint,Vector2.zero);vertices.AddVert(b-normal,tint,Vector2.zero);
            vertices.AddTriangle(index,index+1,index+2);vertices.AddTriangle(index,index+2,index+3);
        }
    }
}
