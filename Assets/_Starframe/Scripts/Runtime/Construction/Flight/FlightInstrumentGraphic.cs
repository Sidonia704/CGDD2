// @role Draw the Figma speed tape, shield and energy bars from live flight telemetry.
// @calls UiTheme
using UnityEngine;
using UnityEngine.UI;
namespace Starframe.Flight
{
 [RequireComponent(typeof(CanvasRenderer))]
 public sealed class FlightInstrumentGraphic:MaskableGraphic
 {
  float speed,energy,shield;bool boosting;
  static readonly Color Lime=new Color32(185,255,3,255),Track=new Color32(117,158,132,255);
  public void SetTelemetry(float velocity,float limit,float battery,float shieldRatio,bool boost)
  {speed=limit>0?Mathf.Clamp01(velocity/limit):0;energy=Mathf.Clamp01(battery);shield=Mathf.Clamp01(shieldRatio);boosting=boost;SetVerticesDirty();}
  protected override void OnPopulateMesh(VertexHelper vh)
  {
   vh.Clear();
   Bar(vh,658.5f,414,18,240,speed);Bar(vh,1185.75f,415.5f,18,246.75f,shield);Bar(vh,1244.25f,415.5f,18,243,energy);
   float y=654-speed*240;
   Triangle(vh,new Vector2(684.75f,y),new Vector2(698.25f,y-6.375f),new Vector2(698.25f,y+6.375f),Lime);
   // Battery icon: three stacked cells and the small positive terminal, as in the reference.
   Box(vh,1245.75f,385.5f,14.25f,6,Lime);Box(vh,1245.75f,393,14.25f,6,Lime);Box(vh,1245.75f,400.5f,14.25f,6,Lime);Box(vh,1249.5f,381.75f,6.75f,2.25f,Lime);
  }
  void Bar(VertexHelper vh,float x,float y,float w,float h,float value)
  {Box(vh,x,y,w,h,Track);Box(vh,x,y+h-h*value,w,h*value,Lime);}
  Vector2 Map(Vector2 p){var r=rectTransform.rect;return new Vector2(r.xMin+p.x/1920*r.width,r.yMax-p.y/1080*r.height);}
  void Box(VertexHelper vh,float x,float y,float w,float h,Color tint)
  {if(h<=0)return;Vector2 a=Map(new Vector2(x,y+h)),b=Map(new Vector2(x+w,y));int i=vh.currentVertCount;vh.AddVert(a,tint,Vector2.zero);vh.AddVert(new Vector2(a.x,b.y),tint,Vector2.zero);vh.AddVert(b,tint,Vector2.zero);vh.AddVert(new Vector2(b.x,a.y),tint,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
  void Triangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color tint)
  {int i=vh.currentVertCount;vh.AddVert(Map(a),tint,Vector2.zero);vh.AddVert(Map(b),tint,Vector2.zero);vh.AddVert(Map(c),tint,Vector2.zero);vh.AddTriangle(i,i+1,i+2);}
 }
}
