// @role Draw minimal speed, shield and energy instruments without enclosing panels or decorative geometry.
// @calls UiTheme
using UnityEngine;
using UnityEngine.UI;
namespace Starframe.Flight
{
 [RequireComponent(typeof(CanvasRenderer))]
 public sealed class FlightInstrumentGraphic:MaskableGraphic
 {
  float speed,energy,shield;bool boosting;
  public void SetTelemetry(float velocity,float limit,float battery,float shieldRatio,bool boost)
  {speed=limit>0?Mathf.Clamp01(velocity/limit):0;energy=Mathf.Clamp01(battery);shield=Mathf.Clamp01(shieldRatio);boosting=boost;SetVerticesDirty();}
  protected override void OnPopulateMesh(VertexHelper vh)
  {
   vh.Clear();var ink=Starframe.Presentation.UiTheme.Current.combatInstrument;ink.a=.98f;var dim=ink;dim.a=.24f;
   Bar(vh,40,speed,ink,dim,true);Bar(vh,580,shield,ink,dim,false);Bar(vh,640,energy,boosting?new Color(1,.7f,.3f,.95f):ink,dim,false);
   float y=35+speed*245;Line(vh,new Vector2(52,y),new Vector2(64,y+5),ink,2);Line(vh,new Vector2(52,y),new Vector2(64,y-5),ink,2);
   Line(vh,new Vector2(570,304),new Vector2(580,311),ink,2);Line(vh,new Vector2(580,311),new Vector2(590,304),ink,2);Line(vh,new Vector2(570,304),new Vector2(573,292),ink,2);Line(vh,new Vector2(590,304),new Vector2(587,292),ink,2);Line(vh,new Vector2(573,292),new Vector2(580,287),ink,2);Line(vh,new Vector2(587,292),new Vector2(580,287),ink,2);
   Line(vh,new Vector2(644,312),new Vector2(635,299),ink,2);Line(vh,new Vector2(635,299),new Vector2(644,299),ink,2);Line(vh,new Vector2(644,299),new Vector2(637,286),ink,2);
  }
  void Bar(VertexHelper v,float x,float value,Color ink,Color dim,bool tape)
  {
   Line(v,new Vector2(x,35),new Vector2(x,280),dim,2);
   if(tape){for(int i=0;i<=10;i++)Line(v,new Vector2(x-(i%5==0?12:6),35+i*24.5f),new Vector2(x,35+i*24.5f),ink,1.5f);}
   else for(int i=0;i<20;i++){float a=i/20f;Line(v,new Vector2(x-7,35+i*12.25f),new Vector2(x+7,35+i*12.25f),a<value?ink:dim,5);}
  }
  void Line(VertexHelper v,Vector2 a,Vector2 b,Color c,float width)
  {
   var r=rectTransform.rect;Vector2 Map(Vector2 p)=>new Vector2(r.xMin+p.x/680*r.width,r.yMin+p.y/350*r.height);
   a=Map(a);b=Map(b);var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int i=v.currentVertCount;
   v.AddVert(a-n,c,Vector2.zero);v.AddVert(a+n,c,Vector2.zero);v.AddVert(b+n,c,Vector2.zero);v.AddVert(b-n,c,Vector2.zero);v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);
  }
 }
}
